using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Analytics;

public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    /// <summary>Record one click_events row per successful redirect. Off in tests and in benchmark modes A/B/D.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// US-11, synchronous version: the INSERT runs inside the redirect request, so it adds one DB
/// round-trip to every 302 (measured as benchmark mode E). The async version (Channel + background
/// batch writer, US-13) would take it off the hot path at the cost of losing events on a crash.
/// </summary>
public sealed class ClickRecorder(UrlShortenerDbContext db, TimeProvider time, IOptions<AnalyticsOptions> options)
{
    public const int MaxReferrerHostLength = 255;

    public bool Enabled => options.Value.Enabled;

    public async Task RecordAsync(long linkId, string? referer, string? userAgent, CancellationToken ct)
    {
        if (!Enabled)
        {
            return;
        }

        db.ClickEvents.Add(new ClickEvent
        {
            LinkId = linkId,
            ClickedAt = time.GetUtcNow().UtcDateTime,
            ReferrerHost = ReferrerHost(referer),
            UaFamily = UaFamily(userAgent),
        });
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
