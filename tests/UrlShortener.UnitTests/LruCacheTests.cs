using UrlShortener.Infrastructure.Caching;

namespace UrlShortener.UnitTests;

public class LruCacheTests
{
    [Fact]
    public void Evicts_the_least_recently_used_key_when_full()
    {
        var lru = new LruCache<string, int>(2);
        lru.Set("a", 1);
        lru.Set("b", 2);

        lru.Set("c", 3); // "a" is the oldest

        Assert.False(lru.TryGet("a", out _));
        Assert.True(lru.TryGet("b", out var b) && b == 2);
        Assert.True(lru.TryGet("c", out var c) && c == 3);
    }

    [Fact]
    public void Reading_a_key_protects_it_from_eviction()
    {
        var lru = new LruCache<string, int>(2);
        lru.Set("a", 1);
        lru.Set("b", 2);

        lru.TryGet("a", out _); // now "b" is least recently used
        lru.Set("c", 3);

        Assert.True(lru.TryGet("a", out _));
        Assert.False(lru.TryGet("b", out _));
    }

    [Fact]
    public void Updating_a_key_replaces_value_and_counts_as_use()
    {
        var lru = new LruCache<string, int>(2);
        lru.Set("a", 1);
        lru.Set("b", 2);

        lru.Set("a", 10);
        lru.Set("c", 3);

        Assert.Equal(2, lru.Count);
        Assert.True(lru.TryGet("a", out var a) && a == 10);
        Assert.False(lru.TryGet("b", out _));
    }

    [Fact]
    public void Capacity_one_keeps_only_the_last_key()
    {
        var lru = new LruCache<int, string>(1);
        lru.Set(1, "x");
        lru.Set(2, "y");

        Assert.False(lru.TryGet(1, out _));
        Assert.True(lru.TryGet(2, out _));
    }

    [Fact]
    public void Remove_deletes_the_key()
    {
        var lru = new LruCache<string, int>(2);
        lru.Set("a", 1);

        Assert.True(lru.Remove("a"));
        Assert.False(lru.Remove("a"));
        Assert.Equal(0, lru.Count);
    }

    [Fact]
    public void Rejects_capacity_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }

    [Fact]
    public void Concurrent_use_never_exceeds_capacity()
    {
        var lru = new LruCache<int, int>(100);

        Parallel.For(0, 10_000, i =>
        {
            lru.Set(i % 500, i);
            lru.TryGet((i * 7) % 500, out _);
        });

        Assert.Equal(100, lru.Count);
    }
}
