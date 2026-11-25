using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.Infrastructure.Links;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-13 async analytics, US-12 correlation id, Day 24 global exception handling.</summary>
[Collection(SqlCollection.Name)]
public class ObservabilityAndAsyncTests(ApiFactory api)
{
    private sealed record StatsDto(int Total);

    [Fact]
    public async Task Async_analytics_records_every_click_without_an_insert_in_the_redirect()
    {
        var withAsync = api.WithAnalytics("Async");
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key, withAsync);
        var anon = api.CreateClientWithKey(null, withAsync);
        var code = await ApiFactory.CreateLinkAsync(http);
        await anon.GetAsync($"/{code}"); // miss: fills the cache
        var counter = withAsync.Services.GetRequiredService<QueryCounter>();

        counter.Reset();
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(HttpStatusCode.Found, (await anon.GetAsync($"/{code}")).StatusCode);
        }
        var queriesDuringRedirects = counter.Count;

        // The worker writes in the background: poll until all 10 clicks are there (or give up).
        var total = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (total < 10 && DateTime.UtcNow < deadline)
        {
            total = (await http.GetFromJsonAsync<StatsDto>($"/api/links/{code}/stats"))!.Total;
            if (total < 10)
            {
                await Task.Delay(100);
            }
        }

        Assert.Equal(10, total);
        Assert.True(queriesDuringRedirects <= 1,
            $"cache hits should not wait for an INSERT; saw {queriesDuringRedirects} queries for 9 redirects (at most one batch flush may land in the window)");
        Assert.Equal(0, withAsync.Services.GetRequiredService<ClickQueue>().Dropped);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_or_generated()
    {
        var anon = api.CreateClientWithKey(null);

        var generated = await anon.GetAsync("/health/live");
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "abc-123");
        var echoed = await anon.SendAsync(request);
        var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        unsafeRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad id with spaces");
        var replaced = await anon.SendAsync(unsafeRequest);

        Assert.Matches("^[0-9a-f]{32}$", generated.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("abc-123", echoed.Headers.GetValues("X-Correlation-Id").Single());
        Assert.NotEqual("bad id with spaces", replaced.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_problem_without_leaking_details()
    {
        var factory = api.WithServices(s => s.AddScoped<IRedirectReader, ThrowingReader>());
        var anon = api.CreateClientWithKey(null, factory);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{FakeCodeGenerator.NewCode()}");
        request.Headers.Add("X-Correlation-Id", "trace-me-1");

        var response = await anon.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ThrowingReader.Secret, body);
        Assert.DoesNotContain("ThrowingReader", body); // no stack trace
        Assert.Contains("\"traceId\":\"trace-me-1\"", body);
        Assert.Equal("trace-me-1", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    private sealed class ThrowingReader : IRedirectReader
    {
        public const string Secret = "Server=db-prod-7;Password=hunter2";

        public Task<RedirectTarget?> GetAsync(string code, CancellationToken ct) =>
            throw new InvalidOperationException("connection failed: " + Secret);
    }
}
