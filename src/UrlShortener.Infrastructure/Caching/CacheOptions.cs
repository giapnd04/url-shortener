namespace UrlShortener.Infrastructure.Caching;

public enum CacheProvider
{
    None,
    Redis,
    Memory,
}

/// <summary>Bound from the "Cache" section.</summary>
public sealed class CacheOptions
{
    public const string Section = "Cache";
    public const string RedisConnectionStringName = "Redis";

    /// <summary>Null means: Redis when ConnectionStrings:Redis is set, otherwise None.</summary>
    public CacheProvider? Provider { get; set; }

    /// <summary>Upper bound for how long an entry lives; never longer than the link's own expiry.</summary>
    public TimeSpan LinkTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Max entries in the in-memory LRU cache (Provider=Memory).</summary>
    public int MemoryCapacity { get; set; } = 10_000;
}
