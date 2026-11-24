namespace UrlShortener.Infrastructure.Data;

/// <summary>
/// One successful redirect (302). Privacy (SEC-04): no IP address and no full referrer URL are
/// stored; only the referrer's host and a coarse browser family.
/// </summary>
public sealed class ClickEvent
{
    public long Id { get; set; }
    public long LinkId { get; set; }
    public DateTime ClickedAt { get; set; }
    public string? ReferrerHost { get; set; }
    public string? UaFamily { get; set; }
}
