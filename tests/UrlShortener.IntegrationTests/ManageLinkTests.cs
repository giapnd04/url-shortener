using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Auth;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-03: X-Api-Key auth, owner scoping, list / get / update / disable.</summary>
[Collection(SqlCollection.Name)]
public class ManageLinkTests(ApiFactory api)
{
    [Fact]
    public async Task Missing_key_returns_401()
    {
        var response = await api.CreateClientWithKey(null).GetAsync("/api/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_key_returns_401()
    {
        var response = await api.CreateClientWithKey(ApiKeyHasher.NewKey()).GetAsync("/api/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_client_returns_401()
    {
        var (_, key) = await api.CreateClientAsync(disabled: true);

        var response = await api.CreateClientWithKey(key).GetAsync("/api/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Raw_key_is_never_stored()
    {
        var (id, key) = await api.CreateClientAsync();

        var stored = await api.WithDbAsync(db => db.ApiClients.Where(c => c.Id == id).Select(c => c.ApiKeyHash).SingleAsync());

        Assert.Equal(32, stored.Length);
        Assert.NotEqual(Encoding.UTF8.GetBytes(key), stored);
        Assert.Equal(ApiKeyHasher.Hash(key), stored);
    }

    [Fact]
    public async Task Another_clients_link_looks_missing()
    {
        var (_, ownerKey) = await api.CreateClientAsync("owner");
        var (_, otherKey) = await api.CreateClientAsync("other");
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(ownerKey));
        var other = api.CreateClientWithKey(otherKey);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/links/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PatchAsJsonAsync($"/api/links/{code}", new { url = "https://evil.example" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/links/{code}/disable", null)).StatusCode);

        // Owner's link is untouched.
        var redirect = await api.CreateClientWithKey(null).GetAsync($"/{code}");
        Assert.Equal("https://example.com/", redirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task List_is_paged_newest_first_and_only_shows_own_links()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var codes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add(await ApiFactory.CreateLinkAsync(http, $"https://example.com/{i}"));
        }

        var page1 = await http.GetFromJsonAsync<LinkListDto>("/api/links?page=1&pageSize=2");
        var page2 = await http.GetFromJsonAsync<LinkListDto>("/api/links?page=2&pageSize=2");

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal([codes[2], codes[1]], page1.Items.Select(x => x.Code));
        Assert.Equal([codes[0]], page2!.Items.Select(x => x.Code));
    }

    [Theory]
    [InlineData("/api/links?page=0")]
    [InlineData("/api/links?pageSize=101")]
    public async Task Invalid_paging_returns_400(string url)
    {
        var (_, key) = await api.CreateClientAsync();

        var response = await api.CreateClientWithKey(key).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_link_details()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http, "https://example.com/details");

        var link = await http.GetFromJsonAsync<LinkDto>($"/api/links/{code}");

        Assert.Equal("https://example.com/details", link!.TargetUrl);
        Assert.Equal("active", link.Status);
        Assert.Equal(DateTimeKind.Utc, link.CreatedAt.Kind);
    }

    [Fact]
    public async Task Patch_changes_target_and_expiry_and_redirect_follows()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http, "https://example.com/old");
        var expiry = DateTimeOffset.UtcNow.AddDays(7);

        var response = await http.PatchAsJsonAsync($"/api/links/{code}", new { url = "https://example.com/new", expiresAt = expiry });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await response.Content.ReadFromJsonAsync<LinkDto>();
        Assert.Equal("https://example.com/new", link!.TargetUrl);
        Assert.NotNull(link.ExpiresAt);
        Assert.True(link.UpdatedAt >= link.CreatedAt);

        var redirect = await api.CreateClientWithKey(null).GetAsync($"/{code}");
        Assert.Equal("https://example.com/new", redirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Patch_with_invalid_url_returns_400()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http);

        var response = await http.PatchAsJsonAsync($"/api/links/{code}", new { url = "javascript:alert(1)" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disable_is_idempotent()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http);

        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/links/{code}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/links/{code}/disable", null)).StatusCode);

        var link = await http.GetFromJsonAsync<LinkDto>($"/api/links/{code}");
        Assert.Equal("disabled", link!.Status);
    }
}
