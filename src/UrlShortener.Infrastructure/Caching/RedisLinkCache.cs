using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Redis implementation. Key link:v1:{code}; the "v1" lets the value format change later by moving
/// to v2 while old keys simply expire. Value is compact JSON {t, s, e, u} (docs/02-database-design.md).
///
/// Fail-open (US-08): any Redis error or timeout is treated as a miss (Get) or a no-op (Set/Remove),
/// so a Redis outage only makes redirects slower (they go to SQL), never failing.
/// </summary>
public sealed class RedisLinkCache(
    IConnectionMultiplexer redis,
    TimeProvider time,
    ILogger<RedisLinkCache> logger) : ILinkCache
{
    private readonly ThrottledWarning _warning = new(time, TimeSpan.FromSeconds(10));

    public static string Key(string code) => $"link:v1:{code}";

    public async Task<RedirectTarget?> GetAsync(string code, CancellationToken ct)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(code));
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CachedLink>(value.ToString())?.ToTarget();
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            LogFailure(ex, "GET");
            return null;
        }
    }

    public async Task SetAsync(string code, RedirectTarget target, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(Key(code), JsonSerializer.Serialize(CachedLink.From(target)), ttl);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            LogFailure(ex, "SET");
        }
    }

    public async Task RemoveAsync(string code, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(code));
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            // The entry stays until its TTL: that TTL is the upper bound for stale data (US-07).
            LogFailure(ex, "DEL");
        }
    }

    private static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException;

    private void LogFailure(Exception ex, string operation)
    {
        if (_warning.ShouldLog())
        {
            logger.LogWarning(ex, "Redis {Operation} failed; serving from SQL (logged at most every 10s)", operation);
        }
    }

    /// <summary>Wire format kept short on purpose: one entry per link, so bytes add up.</summary>
    private sealed record CachedLink(
        [property: JsonPropertyName("t")] string Target,
        [property: JsonPropertyName("s")] string Status,
        [property: JsonPropertyName("e")] DateTime? ExpiresAt,
        [property: JsonPropertyName("u")] DateTime UpdatedAt)
    {
        public static CachedLink From(RedirectTarget t) =>
            new(t.TargetUrl, t.DisabledAt is null ? "active" : "disabled", t.ExpiresAt, t.UpdatedAt);

        // Only "is it disabled" matters for redirect; the exact disable time is not cached.
        public RedirectTarget ToTarget() =>
            new(Target, ExpiresAt, Status == "disabled" ? UpdatedAt : null, UpdatedAt);
    }
}
