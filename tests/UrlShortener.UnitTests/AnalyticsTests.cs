using UrlShortener.Infrastructure.Analytics;

namespace UrlShortener.UnitTests;

public class AnalyticsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36 Edg/131.0", "Edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36", "Chrome")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0", "Firefox")]
    [InlineData("Mozilla/5.0 (Macintosh) AppleWebKit/605.1.15 Version/18.1 Safari/605.1.15", "Safari")]
    [InlineData("curl/8.9.1", "curl")]
    [InlineData("Grafana k6/0.54.0", "k6")]
    [InlineData("SomeBot/1.0", "Other")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void Ua_family_is_coarse(string? userAgent, string? expected)
    {
        Assert.Equal(expected, ClickRecorder.UaFamily(userAgent));
    }

    [Theory]
    [InlineData("https://News.Example.org/path?user=42", "news.example.org")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Referrer_keeps_only_the_host(string? referer, string? expected)
    {
        Assert.Equal(expected, ClickRecorder.ReferrerHost(referer));
    }

    [Fact]
    public void Default_range_is_the_last_30_days_including_today()
    {
        var today = new DateOnly(2026, 11, 24);

        var (from, to, error) = ClickStatsService.ResolveRange(null, null, today);

        Assert.Null(error);
        Assert.Equal(today, to);
        Assert.Equal(new DateOnly(2026, 10, 26), from);
        Assert.Equal(30, to.DayNumber - from.DayNumber + 1);
    }

    [Fact]
    public void Range_rules()
    {
        var today = new DateOnly(2026, 11, 24);

        Assert.NotNull(ClickStatsService.ResolveRange(new(2026, 11, 5), new(2026, 11, 1), today).Error);
        Assert.Null(ClickStatsService.ResolveRange(new(2026, 1, 1), new(2026, 12, 31), today).Error);   // 365 days
        Assert.Null(ClickStatsService.ResolveRange(new(2024, 1, 1), new(2024, 12, 31), today).Error);   // 366 (leap year)
        Assert.NotNull(ClickStatsService.ResolveRange(new(2025, 1, 1), new(2026, 1, 2), today).Error);  // 367
        Assert.Null(ClickStatsService.ResolveRange(new(2026, 11, 1), new(2026, 11, 1), today).Error);   // one day
    }

    [Fact]
    public void Fill_days_includes_every_day_with_zero_for_gaps()
    {
        var counts = new Dictionary<DateOnly, int> { [new(2026, 11, 1)] = 2, [new(2026, 11, 3)] = 1 };

        var days = ClickStatsService.FillDays(new(2026, 11, 1), new(2026, 11, 3), counts);

        Assert.Equal(
            [new DayClicks(new(2026, 11, 1), 2), new DayClicks(new(2026, 11, 2), 0), new DayClicks(new(2026, 11, 3), 1)],
            days);
    }
}
