using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Data;
using UrlShortener.Infrastructure.Data.Configurations;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>US-11: synchronous click analytics and GET /api/links/{code}/stats.</summary>
[Collection(SqlCollection.Name)]
public class AnalyticsTests(ApiFactory api)
{
    private sealed record StatsDto(int Total, DateOnly From, DateOnly To, string TimeZone, List<DayDto> ByDay);

    private sealed record DayDto(DateOnly Day, int Clicks);

    [Fact]
    public async Task Click_count_matches_the_redirects_sent_and_skips_404_and_410()
    {
        var withAnalytics = api.WithAnalytics();
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key, withAnalytics);
        var anon = api.CreateClientWithKey(null, withAnalytics);
        var code = await ApiFactory.CreateLinkAsync(http);
        var disabledCode = await ApiFactory.CreateLinkAsync(http);
        (await http.PostAsync($"/api/links/{disabledCode}/disable", null)).EnsureSuccessStatusCode();

        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(HttpStatusCode.Found, (await anon.GetAsync($"/{code}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Gone, (await anon.GetAsync($"/{disabledCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/{FakeCodeGenerator.NewCode()}")).StatusCode);

        var stats = await http.GetFromJsonAsync<StatsDto>($"/api/links/{code}/stats");
        var disabledStats = await http.GetFromJsonAsync<StatsDto>($"/api/links/{disabledCode}/stats");

        Assert.Equal(7, stats!.Total);
        Assert.Equal("UTC", stats.TimeZone);
        Assert.Equal(7, stats.ByDay.Single(d => d.Day == DateOnly.FromDateTime(DateTime.UtcNow)).Clicks);
        Assert.Equal(0, disabledStats!.Total);
    }

    [Fact]
    public async Task Clicks_are_recorded_on_cache_hits_too()
    {
        var withAnalytics = api.WithAnalytics();
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key, withAnalytics);
        var anon = api.CreateClientWithKey(null, withAnalytics);
        var code = await ApiFactory.CreateLinkAsync(http);
        var counter = withAnalytics.Services.GetRequiredService<QueryCounter>();

        await anon.GetAsync($"/{code}"); // miss: SELECT + INSERT
        counter.Reset();
        await anon.GetAsync($"/{code}"); // hit: INSERT only

        Assert.Equal(1, counter.Count);
        Assert.Equal(2, (await http.GetFromJsonAsync<StatsDto>($"/api/links/{code}/stats"))!.Total);
    }

    [Fact]
    public async Task Stats_group_by_utc_day_and_fill_days_without_clicks()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http);
        var linkId = await api.WithDbAsync(db => db.ShortLinks.Where(l => l.Code == code).Select(l => l.Id).SingleAsync());
        await api.WithDbAsync(async db =>
        {
            db.ClickEvents.AddRange(
                new ClickEvent { LinkId = linkId, ClickedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
                new ClickEvent { LinkId = linkId, ClickedAt = new DateTime(2026, 11, 1, 23, 59, 59, DateTimeKind.Utc) },
                new ClickEvent { LinkId = linkId, ClickedAt = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc) },
                // Outside the range on both sides: the half-open range must exclude these.
                new ClickEvent { LinkId = linkId, ClickedAt = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc) },
                new ClickEvent { LinkId = linkId, ClickedAt = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc) });
            return await db.SaveChangesAsync();
        });

        var stats = await http.GetFromJsonAsync<StatsDto>($"/api/links/{code}/stats?from=2026-11-01&to=2026-11-03");

        Assert.Equal(3, stats!.Total);
        Assert.Equal(
            [new DayDto(new(2026, 11, 1), 2), new DayDto(new(2026, 11, 2), 0), new DayDto(new(2026, 11, 3), 1)],
            stats.ByDay);
    }

    [Theory]
    [InlineData("from=2026-11-05&to=2026-11-01")]
    [InlineData("from=2025-01-01&to=2026-11-01")]
    [InlineData("from=not-a-date")]
    public async Task Invalid_range_returns_400(string query)
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var code = await ApiFactory.CreateLinkAsync(http);

        var response = await http.GetAsync($"/api/links/{code}/stats?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_clients_stats_return_404()
    {
        var (_, ownerKey) = await api.CreateClientAsync("owner");
        var (_, otherKey) = await api.CreateClientAsync("other");
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(ownerKey));

        var response = await api.CreateClientWithKey(otherKey).GetAsync($"/api/links/{code}/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stores_referrer_host_and_browser_family_but_no_ip()
    {
        var withAnalytics = api.WithAnalytics();
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key, withAnalytics));
        var anon = api.CreateClientWithKey(null, withAnalytics);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{code}");
        request.Headers.Referrer = new Uri("https://News.Example.org/some/private/path?user=42");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");

        await anon.SendAsync(request);

        var click = await api.WithDbAsync(db => db.ClickEvents
            .Where(c => c.LinkId == db.ShortLinks.Where(l => l.Code == code).Select(l => l.Id).Single())
            .SingleAsync());
        Assert.Equal("news.example.org", click.ReferrerHost);
        Assert.Equal("Chrome", click.UaFamily);

        var columns = await api.WithDbAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sys.columns WHERE object_id = OBJECT_ID('click_events')")
            .ToListAsync());
        Assert.DoesNotContain(columns, c => c.Contains("ip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stats_query_is_an_index_seek_on_link_id_and_clicked_at()
    {
        await using var connection = new SqlConnection(api.SqlConnectionString);
        await connection.OpenAsync();
        connection.ChangeDatabase(DatabaseName());
        await using (var on = new SqlCommand("SET SHOWPLAN_XML ON", connection))
        {
            await on.ExecuteNonQueryAsync();
        }

        // A parameterized SqlCommand goes through sp_executesql, whose estimated plan does not include
        // the inner statement. Declaring the same variables in the batch compiles the exact query text.
        var batch = """
            DECLARE @linkId bigint = 1;
            DECLARE @fromUtc datetime2 = '2026-11-01';
            DECLARE @toExclusiveUtc datetime2 = '2026-12-01';
            """ + ClickStatsService.ByDaySql;
        await using var command = new SqlCommand(batch, connection);
        var plans = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    plans.Add(reader.GetString(0));
                }
            } while (await reader.NextResultAsync());
        }
        var plan = string.Join("\n", plans);

        Assert.Contains($"Index=\"[{ClickEventConfiguration.LinkIdClickedAtIndex}]\"", plan);
        Assert.Contains("PhysicalOp=\"Index Seek\"", plan);
        Assert.DoesNotContain("PhysicalOp=\"Clustered Index Scan\"", plan);
    }

    [Fact]
    public async Task Cached_value_carries_the_link_id_under_a_v2_key()
    {
        var (_, key) = await api.CreateClientAsync();
        var code = await ApiFactory.CreateLinkAsync(api.CreateClientWithKey(key));
        var linkId = await api.WithDbAsync(db => db.ShortLinks.Where(l => l.Code == code).Select(l => l.Id).SingleAsync());

        await api.CreateClientWithKey(null).GetAsync($"/{code}");

        Assert.StartsWith("link:v2:", RedisLinkCache.Key(code));
        var value = await (await api.RedisDbAsync()).StringGetAsync(RedisLinkCache.Key(code));
        using var json = JsonDocument.Parse(value.ToString());
        Assert.Equal(linkId, json.RootElement.GetProperty("i").GetInt64());
    }

    private string DatabaseName()
    {
        using var scope = api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>().Database.GetDbConnection().Database;
    }
}
