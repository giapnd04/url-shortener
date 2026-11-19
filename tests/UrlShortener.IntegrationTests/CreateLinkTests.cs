using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Links;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-01: create link, bounded retry on collision, case-sensitive codes.</summary>
[Collection(SqlCollection.Name)]
public class CreateLinkTests(ApiFactory api)
{
    [Fact]
    public async Task Valid_url_returns_201_with_location_and_short_url()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);

        var response = await http.PostAsJsonAsync("/api/links", new { url = "https://example.com/a/b" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkDto>();
        Assert.NotNull(body);
        Assert.Matches("^[0-9A-Za-z]{7}$", body.Code);
        Assert.Equal($"https://sho.rt/{body.Code}", body.ShortUrl);
        Assert.Equal("https://example.com/a/b", body.TargetUrl);
        Assert.Equal("active", body.Status);
        Assert.Equal($"/api/links/{body.Code}", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("https://sho.rt/abc1234")]
    public async Task Invalid_url_returns_400_problem_details(string url)
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);

        var response = await http.PostAsJsonAsync("/api/links", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"url\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expiry_in_the_past_returns_400()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);

        var response = await http.PostAsJsonAsync("/api/links",
            new { url = "https://example.com", expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_code_twice_then_third_attempt_succeeds()
    {
        var (_, key) = await api.CreateClientAsync();
        var taken = FakeCodeGenerator.NewCode();
        var fresh = FakeCodeGenerator.NewCode();

        // Occupy "taken" first.
        var setup = new FakeCodeGenerator(taken);
        await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key, api.WithCodes(setup)));

        var generator = new FakeCodeGenerator(taken, taken, fresh);
        var http = api.CreateClientWithKey(key, api.WithCodes(generator));

        var response = await http.PostAsJsonAsync("/api/links", new { url = "https://example.com/retry" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkDto>();
        Assert.Equal(fresh, body!.Code);
        Assert.Equal(3, generator.Calls);
    }

    [Fact]
    public async Task Collision_on_every_attempt_returns_503_after_bounded_retries()
    {
        var (_, key) = await api.CreateClientAsync();
        var taken = FakeCodeGenerator.NewCode();
        await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key, api.WithCodes(new FakeCodeGenerator(taken))));

        var generator = new FakeCodeGenerator(taken); // always the same, always taken
        var http = api.CreateClientWithKey(key, api.WithCodes(generator));

        var response = await http.PostAsJsonAsync("/api/links", new { url = "https://example.com/loop" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(LinkService.MaxCreateAttempts, generator.Calls);
    }

    [Fact]
    public async Task Codes_differing_only_in_case_are_two_different_links()
    {
        var (_, key) = await api.CreateClientAsync();
        var lower = "a" + FakeCodeGenerator.NewCode()[1..].ToLowerInvariant();
        var upper = lower.ToUpperInvariant();
        var http = api.CreateClientWithKey(key, api.WithCodes(new FakeCodeGenerator(lower, upper)));

        var first = await http.PostAsJsonAsync("/api/links", new { url = "https://example.com/lower" });
        var second = await http.PostAsJsonAsync("/api/links", new { url = "https://example.com/upper" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        // And each code resolves to its own target, i.e. the lookup is case-sensitive too.
        var anon = api.CreateClientWithKey(null);
        Assert.Equal("https://example.com/lower", (await anon.GetAsync($"/{lower}")).Headers.Location?.OriginalString);
        Assert.Equal("https://example.com/upper", (await anon.GetAsync($"/{upper}")).Headers.Location?.OriginalString);

        var stored = await api.WithDbAsync(db => db.ShortLinks.CountAsync(l => l.Code == lower || l.Code == upper));
        Assert.Equal(2, stored);
    }
}
