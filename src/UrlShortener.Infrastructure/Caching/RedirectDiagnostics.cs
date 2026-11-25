namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Per-request (scoped) facts about a redirect, read by the request log line. Null when the
/// request did not go through the cache (cache off, or not a redirect).
/// </summary>
public sealed class RedirectDiagnostics
{
    public CacheStatus? CacheResult { get; set; }
}
