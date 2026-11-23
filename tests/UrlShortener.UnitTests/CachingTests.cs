using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.UnitTests;

public class CachingTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static RedirectTarget Target(DateTime? expiresAt = null) =>
        new("https://example.com", expiresAt, null, Now);

    [Fact]
    public void Ttl_is_the_configured_maximum_for_links_without_expiry()
    {
        Assert.Equal(TimeSpan.FromHours(1), CachedRedirectReader.TtlFor(Target(), Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Ttl_is_capped_by_the_links_remaining_lifetime()
    {
        var ttl = CachedRedirectReader.TtlFor(Target(Now.AddMinutes(10)), Now, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromMinutes(10), ttl);
    }

    [Fact]
    public void Ttl_is_not_positive_for_an_expired_link()
    {
        Assert.True(CachedRedirectReader.TtlFor(Target(Now.AddSeconds(-1)), Now, TimeSpan.FromHours(1)) <= TimeSpan.Zero);
    }

    [Fact]
    public async Task In_memory_cache_returns_entries_until_their_ttl()
    {
        var time = new ManualTime(Now);
        var cache = new InMemoryLinkCache(10, time);
        await cache.SetAsync("abc1234", Target(), TimeSpan.FromMinutes(5), default);

        Assert.NotNull(await cache.GetAsync("abc1234", default));

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await cache.GetAsync("abc1234", default));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task In_memory_cache_evicts_by_lru_at_capacity()
    {
        var cache = new InMemoryLinkCache(2, new ManualTime(Now));
        await cache.SetAsync("a", Target(), TimeSpan.FromHours(1), default);
        await cache.SetAsync("b", Target(), TimeSpan.FromHours(1), default);
        await cache.GetAsync("a", default);

        await cache.SetAsync("c", Target(), TimeSpan.FromHours(1), default);

        Assert.NotNull(await cache.GetAsync("a", default));
        Assert.Null(await cache.GetAsync("b", default));
    }

    [Fact]
    public void Throttled_warning_logs_once_per_interval()
    {
        var time = new ManualTime(Now);
        var warning = new ThrottledWarning(time, TimeSpan.FromSeconds(10));

        Assert.True(warning.ShouldLog());
        Assert.False(warning.ShouldLog());
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(warning.ShouldLog());
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(warning.ShouldLog());
    }

    private sealed class ManualTime(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
