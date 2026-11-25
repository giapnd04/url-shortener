using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>What a cache lookup found. Error = the cache itself failed (e.g. Redis down), not "key absent".</summary>
public enum CacheStatus
{
    Hit,
    Miss,
    Error,
}

public readonly record struct CacheLookup(RedirectTarget? Target, CacheStatus Status)
{
    public static readonly CacheLookup Miss = new(null, CacheStatus.Miss);
    public static readonly CacheLookup Error = new(null, CacheStatus.Error);
    public static CacheLookup Hit(RedirectTarget target) => new(target, CacheStatus.Hit);
}

/// <summary>
/// Cache of redirect targets keyed by short code. Implementations are interchangeable
/// (Strategy): Redis (shared by all instances), in-memory LRU (per process), or none.
/// SQL Server stays the source of truth; a cache entry may be lost at any time.
/// </summary>
public interface ILinkCache
{
    /// <summary>Never throws for infrastructure failures (fail-open, US-08); reports them as Error instead.</summary>
    Task<CacheLookup> GetAsync(string code, CancellationToken ct);

    Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct);

    Task RemoveAsync(string code, CancellationToken ct);
}

/// <summary>Cache turned off (Cache:Provider=None). Used for the no-cache benchmark mode and local dev without Redis.</summary>
public sealed class NullLinkCache : ILinkCache
{
    public Task<CacheLookup> GetAsync(string code, CancellationToken ct) => Task.FromResult(CacheLookup.Miss);
    public Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(string code, CancellationToken ct) => Task.CompletedTask;
}
