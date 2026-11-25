using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Analytics;

public enum AnalyticsMode
{
    /// <summary>INSERT inside the redirect request (US-11). Never loses a click, costs a DB round-trip per 302.</summary>
    Sync,

    /// <summary>Queue in memory, a background worker inserts in batches (US-13). At-most-once.</summary>
    Async,
}

public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    /// <summary>Record one click_events row per successful redirect. Off in tests and in benchmark modes A/B/D.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Async by default: benchmark mode E showed the synchronous INSERT costs ~68% of throughput.</summary>
    public AnalyticsMode Mode { get; set; } = AnalyticsMode.Async;

    /// <summary>Bounded queue: memory stays capped when the DB is slow; overflow is dropped and counted.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    public int BatchSize { get; set; } = 500;

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Turns a redirect into a click event and hands it to the configured path:
/// Sync inserts now (redirect waits for SQL); Async puts it in <see cref="ClickQueue"/> and returns
/// at once, and <see cref="ClickWriterService"/> writes it later in a batch.
/// </summary>
public sealed class ClickRecorder(
    UrlShortenerDbContext db,
    ClickQueue queue,
    TimeProvider time,
    IOptions<AnalyticsOptions> options)
{
    public const int MaxReferrerHostLength = 255;

    public bool Enabled => options.Value.Enabled;

    public async Task RecordAsync(long linkId, string? referer, string? userAgent, CancellationToken ct)
    {
        if (!Enabled)
        {
            return;
        }

        var click = new ClickEvent
        {
            LinkId = linkId,
            ClickedAt = time.GetUtcNow().UtcDateTime,
            ReferrerHost = ReferrerHost(referer),
            UaFamily = UaFamily(userAgent),
        };

        if (options.Value.Mode == AnalyticsMode.Async)
        {
            // Never blocks and never throws into the redirect: a full queue means the click is dropped.
            queue.TryEnqueue(click);
            return;
        }

        db.ClickEvents.Add(click);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Only the host of the Referer, lower-cased: path and query can contain personal data.</summary>
    public static string? ReferrerHost(string? referer)
    {
        if (string.IsNullOrWhiteSpace(referer)
            || !Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.Length <= MaxReferrerHostLength ? host : host[..MaxReferrerHostLength];
    }

    /// <summary>
    /// Coarse browser family, enough for "which browsers click my links". Order matters: Edge and
    /// Chrome both contain "Chrome/", Chrome contains "Safari/".
    /// </summary>
    public static string? UaFamily(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        if (userAgent.Contains("Edg/", StringComparison.Ordinal)) return "Edge";
        if (userAgent.Contains("Firefox/", StringComparison.Ordinal)) return "Firefox";
        if (userAgent.Contains("Chrome/", StringComparison.Ordinal)) return "Chrome";
        if (userAgent.Contains("Safari/", StringComparison.Ordinal)) return "Safari";
        if (userAgent.StartsWith("curl/", StringComparison.OrdinalIgnoreCase)) return "curl";
        if (userAgent.Contains("k6/", StringComparison.OrdinalIgnoreCase)) return "k6";
        return "Other";
    }
}
