using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Cache of redirect targets keyed by short code. Implementations are interchangeable
/// (Strategy): Redis (shared by all instances), in-memory LRU (per process), or none.
/// SQL Server stays the source of truth; a cache entry may be lost at any time.
/// </summary>
public interface ILinkCache
{
    /// <summary>Null on a miss. Implementations must not throw for infrastructure failures (fail-open, US-08).</summary>
    Task<RedirectTarget?> GetAsync(string code, CancellationToken ct);

    Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct);

    Task RemoveAsync(string code, CancellationToken ct);
}

/// <summary>Cache turned off (Cache:Provider=None). Used for the no-cache benchmark mode and local dev without Redis.</summary>
public sealed class NullLinkCache : ILinkCache
{
    public Task<RedirectTarget?> GetAsync(string code, CancellationToken ct) => Task.FromResult<RedirectTarget?>(null);
    public Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(string code, CancellationToken ct) => Task.CompletedTask;
}
