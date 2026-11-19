namespace UrlShortener.Infrastructure.Data;

public sealed class ShortLink
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string TargetUrl { get; set; }
    public int ClientId { get; set; }
    public ApiClient? Client { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Changes on every edit; used as a version for stale-cache checks from v0.2 (US-07).</summary>
    public DateTime UpdatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime? DisabledAt { get; set; }
}
