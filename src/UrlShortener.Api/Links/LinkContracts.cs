using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Api.Links;

public sealed record CreateLinkRequest(string? Url, DateTimeOffset? ExpiresAt);

/// <summary>Each field optional; null means unchanged.</summary>
public sealed record UpdateLinkRequest(string? Url, DateTimeOffset? ExpiresAt);

public sealed record LinkResponse(
    string Code,
    string ShortUrl,
    string TargetUrl,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpiresAt,
    DateTime? DisabledAt)
{
    public static LinkResponse From(LinkView v, string baseUrl, DateTime nowUtc) => new(
        v.Code,
        $"{baseUrl}/{v.Code}",
        v.TargetUrl,
        LinkStatus.Of(v.DisabledAt, v.ExpiresAt, nowUtc),
        v.CreatedAt,
        v.UpdatedAt,
        v.ExpiresAt,
        v.DisabledAt);
}

public sealed record DayClicksResponse(DateOnly Day, int Clicks);

/// <summary>Days are UTC calendar days ("timeZone": "UTC" makes that explicit to clients).</summary>
public sealed record ClickStatsResponse(int Total, DateOnly From, DateOnly To, string TimeZone, IReadOnlyList<DayClicksResponse> ByDay);

public sealed record LinkListResponse(IReadOnlyList<LinkResponse> Items, int Page, int PageSize, int TotalCount);

public static class LinkStatus
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Expired = "expired";

    public static string Of(DateTime? disabledAt, DateTime? expiresAt, DateTime nowUtc) =>
        disabledAt is not null ? Disabled
        : expiresAt is not null && expiresAt <= nowUtc ? Expired
        : Active;
}
