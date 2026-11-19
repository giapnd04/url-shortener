using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-02: GET /{code} → 302 / 404 / 410, route constraint, one DB query.</summary>
[Collection(SqlCollection.Name)]
public class RedirectTests(ApiFactory api)
{
    [Fact]
    public async Task Active_link_returns_302_with_location()
    {
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key), "https://example.com/target?q=1");

        var response = await api.CreateClientWithKey(null).GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/target?q=1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unknown_code_returns_404()
    {
        var response = await api.CreateClientWithKey(null).GetAsync($"/{FakeCodeGenerator.NewCode()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_link_returns_410()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http);
        (await http.PostAsync($"/api/links/{code}/disable", null)).EnsureSuccessStatusCode();

        var response = await api.CreateClientWithKey(null).GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Expired_link_returns_410()
    {
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key));
        // Expiry must be in the future at creation time, so move it into the past directly in SQL.
        await api.WithDbAsync(db => db.ShortLinks.Where(l => l.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var response = await api.CreateClientWithKey(null).GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/abc", HttpStatusCode.NotFound)]          // too short
    [InlineData("/abcdefgh", HttpStatusCode.NotFound)]     // too long
    [InlineData("/abc-123", HttpStatusCode.NotFound)]      // not Base62
    [InlineData("/favicon.ico", HttpStatusCode.NotFound)]
    public async Task Route_constraint_keeps_other_paths_away_from_redirect(string path, HttpStatusCode expected)
    {
        var counter = api.Services.GetRequiredService<QueryCounter>();
        counter.Reset();

        var response = await api.CreateClientWithKey(null).GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, counter.Count); // rejected by routing, never reached the database
    }

    [Fact]
    public async Task Redirect_runs_exactly_one_database_query()
    {
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key));
        var anon = api.CreateClientWithKey(null);
        var counter = api.Services.GetRequiredService<QueryCounter>();

        counter.Reset();
        var response = await anon.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, counter.Count);
    }
}
