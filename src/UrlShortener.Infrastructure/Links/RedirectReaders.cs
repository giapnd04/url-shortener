using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Links;

/// <summary>Reads what GET /{code} needs. Implemented by SQL, optionally wrapped by a cache decorator.</summary>
public interface IRedirectReader
{
    Task<RedirectTarget?> GetAsync(string code, CancellationToken ct);
}

/// <summary>Single query: index seek on UX_short_links_code, no tracking, only the needed columns.</summary>
public sealed class SqlRedirectReader(UrlShortenerDbContext db) : IRedirectReader
{
    public Task<RedirectTarget?> GetAsync(string code, CancellationToken ct) =>
        db.ShortLinks
            .AsNoTracking()
            .Where(l => l.Code == code)
            .Select(l => new RedirectTarget(l.TargetUrl, l.ExpiresAt, l.DisabledAt, l.UpdatedAt))
            .FirstOrDefaultAsync(ct);
}

/// <summary>
/// Cache-aside as a Decorator (US-05): same interface as the SQL reader, adds caching around it,
/// and the endpoint does not know which one it got.
///   1. GET cache → hit: return, no DB query.
///   2. miss → read SQL (1 query) → SET with TTL = min(configured TTL, time left before the link expires).
/// Not-found codes are not cached (negative caching is US-06, phase 2), and neither are expired links.
/// </summary>
public sealed class CachedRedirectReader(
    IRedirectReader inner,
    ILinkCache cache,
    TimeProvider time,
    IOptions<CacheOptions> options) : IRedirectReader
{
    public async Task<RedirectTarget?> GetAsync(string code, CancellationToken ct)
    {
        var cached = await cache.GetAsync(code, ct);
        if (cached is not null)
        {
            return cached;
        }

        var target = await inner.GetAsync(code, ct);
        if (target is null)
        {
            return null;
        }

        var ttl = TtlFor(target, time.GetUtcNow().UtcDateTime, options.Value.LinkTtl);
        if (ttl > TimeSpan.Zero)
        {
            await cache.SetAsync(code, target, ttl, ct);
        }

        return target;
    }

    /// <summary>A cache entry must never outlive the link itself.</summary>
    public static TimeSpan TtlFor(RedirectTarget target, DateTime nowUtc, TimeSpan maxTtl)
    {
        if (target.ExpiresAt is null)
        {
            return maxTtl;
        }

        var remaining = target.ExpiresAt.Value - nowUtc;
        return remaining < maxTtl ? remaining : maxTtl;
    }
}
