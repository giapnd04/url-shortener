using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Per-process cache on top of <see cref="LruCache{TKey,TValue}"/> (Cache:Provider=Memory, US-16).
/// Registered as a DI Singleton: one instance for the whole app, otherwise every request would get
/// an empty cache. Limits compared with Redis: each instance has its own copy, so invalidation on one
/// instance does not reach the others, and the cache is lost on restart.
/// </summary>
public sealed class InMemoryLinkCache(int capacity, TimeProvider time) : ILinkCache
{
    private readonly LruCache<string, Entry> _lru = new(capacity);

    public int Count => _lru.Count;

    public Task<CacheLookup> GetAsync(string code, CancellationToken ct)
    {
        if (_lru.TryGet(code, out var entry))
        {
            if (entry.ExpiresAt > time.GetUtcNow())
            {
                return Task.FromResult(CacheLookup.Hit(entry.Target));
            }

            // Expired entries are dropped lazily on read; LRU eviction handles the rest.
            _lru.Remove(code);
        }

        return Task.FromResult(CacheLookup.Miss);
    }

    public Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct)
    {
        _lru.Set(code, new Entry(target, time.GetUtcNow() + ttl));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string code, CancellationToken ct)
    {
        _lru.Remove(code);
        return Task.CompletedTask;
    }

    private sealed record Entry(RedirectTarget Target, DateTimeOffset ExpiresAt);
}
