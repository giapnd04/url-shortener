using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.Infrastructure.Links;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-05 cache-aside and US-07 invalidation, against real Redis.</summary>
[Collection(SqlCollection.Name)]
public class CacheAsideTests(ApiFactory api)
{
    [Fact]
    public async Task Miss_reads_sql_once_and_writes_the_key_then_hit_reads_no_sql()
    {
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key), "https://example.com/cached");
        var anon = api.CreateClientWithKey(null);
        var counter = api.Services.GetRequiredService<QueryCounter>();
        var redis = await api.RedisDbAsync();

        counter.Reset();
        var miss = await anon.GetAsync($"/{code}");
        Assert.Equal(HttpStatusCode.Found, miss.StatusCode);
        Assert.Equal(1, counter.Count);

        var stored = await redis.StringGetAsync(RedisLinkCache.Key(code));
        Assert.True(stored.HasValue);
        using var json = JsonDocument.Parse(stored.ToString());
        Assert.Equal("https://example.com/cached", json.RootElement.GetProperty("t").GetString());
        Assert.Equal("active", json.RootElement.GetProperty("s").GetString());
        var ttl = await redis.KeyTimeToLiveAsync(RedisLinkCache.Key(code));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));

        counter.Reset();
        var hit = await anon.GetAsync($"/{code}");
        Assert.Equal(HttpStatusCode.Found, hit.StatusCode);
        Assert.Equal("https://example.com/cached", hit.Headers.Location?.OriginalString);
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task Ttl_never_exceeds_the_links_remaining_lifetime()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var response = await http.PostAsJsonAsync("/api/links",
            new { url = "https://example.com/short-lived", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        var code = (await response.Content.ReadFromJsonAsync<LinkDto>())!.Code;

        await api.CreateClientWithKey(null).GetAsync($"/{code}");

        var ttl = await (await api.RedisDbAsync()).KeyTimeToLiveAsync(RedisLinkCache.Key(code));
        Assert.NotNull(ttl);
        Assert.True(ttl.Value <= TimeSpan.FromMinutes(10), $"TTL {ttl} is longer than the link's remaining 10 minutes");
    }

    [Fact]
    public async Task Unknown_code_is_not_cached()
    {
        var code = FakeCodeGenerator.NewCode();

        await api.CreateClientWithKey(null).GetAsync($"/{code}");

        Assert.False(await (await api.RedisDbAsync()).KeyExistsAsync(RedisLinkCache.Key(code)));
    }

    [Fact]
    public async Task Update_after_caching_sends_the_next_redirect_to_the_new_target()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var anon = api.CreateClientWithKey(null);
        var code = await ApiFactory.CreateLinkAsync(http, "https://example.com/old");
        await anon.GetAsync($"/{code}"); // now cached

        (await http.PatchAsJsonAsync($"/api/links/{code}", new { url = "https://example.com/new" })).EnsureSuccessStatusCode();

        Assert.False(await (await api.RedisDbAsync()).KeyExistsAsync(RedisLinkCache.Key(code)));
        var redirect = await anon.GetAsync($"/{code}");
        Assert.Equal("https://example.com/new", redirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Disable_after_caching_returns_410_not_the_cached_redirect()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var anon = api.CreateClientWithKey(null);
        var code = await ApiFactory.CreateLinkAsync(http);
        Assert.Equal(HttpStatusCode.Found, (await anon.GetAsync($"/{code}")).StatusCode); // cached

        (await http.PostAsync($"/api/links/{code}/disable", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Gone, (await anon.GetAsync($"/{code}")).StatusCode);
    }

    [Fact]
    public async Task Failed_invalidation_does_not_fail_the_update()
    {
        var (_, key) = await api.CreateClientAsync();
        var factory = api.WithServices(s => s.AddSingleton<ILinkCache>(new ThrowingOnRemoveCache()));
        var http = api.CreateClientWithKey(key, factory);
        var code = await ApiFactory.CreateLinkAsync(http);

        var patch = await http.PatchAsJsonAsync($"/api/links/{code}", new { url = "https://example.com/after" });
        var disable = await http.PostAsync($"/api/links/{code}/disable", null);

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
    }

    [Fact]
    public void Cache_is_a_single_instance_for_the_whole_app()
    {
        using var scope1 = api.Services.CreateScope();
        using var scope2 = api.Services.CreateScope();

        Assert.Same(
            scope1.ServiceProvider.GetRequiredService<ILinkCache>(),
            scope2.ServiceProvider.GetRequiredService<ILinkCache>());
        Assert.IsType<RedisLinkCache>(scope1.ServiceProvider.GetRequiredService<ILinkCache>());
    }

    private sealed class ThrowingOnRemoveCache : ILinkCache
    {
        public Task<CacheLookup> GetAsync(string code, CancellationToken ct) =>
            Task.FromResult(CacheLookup.Miss);

        public Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct) =>
            Task.CompletedTask;

        public Task RemoveAsync(string code, CancellationToken ct) =>
            throw new InvalidOperationException("simulated cache failure");
    }
}
