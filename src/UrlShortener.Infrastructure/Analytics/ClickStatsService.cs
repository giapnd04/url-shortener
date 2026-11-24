using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Analytics;

public sealed record DayClicks(DateOnly Day, int Clicks);

public sealed record ClickStats(int Total, DateOnly From, DateOnly To, IReadOnlyList<DayClicks> ByDay);

/// <summary>
/// Clicks per UTC day for one link. Days are UTC on purpose: clicked_at is stored in UTC, and a
/// "day" in a local time zone would need the caller's zone (a link has no zone of its own).
/// The API contract says so explicitly.
/// </summary>
public sealed class ClickStatsService(UrlShortenerDbContext db)
{
    public const int MaxRangeDays = 366;
    public const int DefaultRangeDays = 30;

    /// <summary>Plain SQL so the shape is obvious and the plan test can run exactly this text.</summary>
    public const string ByDaySql = """
        SELECT CAST(clicked_at AS date) AS Day, COUNT(*) AS Clicks
        FROM click_events
        WHERE link_id = @linkId AND clicked_at >= @fromUtc AND clicked_at < @toExclusiveUtc
        GROUP BY CAST(clicked_at AS date)
        """;

    public async Task<ClickStats> GetAsync(long linkId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // Half-open range [from, to + 1 day): includes the whole "to" day, no 23:59:59.999 tricks,
        // and stays sargable (no function on clicked_at in WHERE), so the index range seek works.
        var toExclusiveUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await db.Database
            .SqlQueryRaw<DayRow>(ByDaySql,
                new Microsoft.Data.SqlClient.SqlParameter("@linkId", linkId),
                new Microsoft.Data.SqlClient.SqlParameter("@fromUtc", System.Data.SqlDbType.DateTime2) { Value = fromUtc },
                new Microsoft.Data.SqlClient.SqlParameter("@toExclusiveUtc", System.Data.SqlDbType.DateTime2) { Value = toExclusiveUtc })
            .ToListAsync(ct);

        var byDay = FillDays(from, to, rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Clicks));
        return new ClickStats(byDay.Sum(d => d.Clicks), from, to, byDay);
    }

    /// <summary>Every day in the range appears, zero when there were no clicks, so charts need no gap logic.</summary>
    public static IReadOnlyList<DayClicks> FillDays(DateOnly from, DateOnly to, IReadOnlyDictionary<DateOnly, int> counts)
    {
        var days = new List<DayClicks>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(new DayClicks(day, counts.GetValueOrDefault(day)));
        }

        return days;
    }

    /// <summary>Defaults to the last 30 UTC days ending today. Returns an error message for a bad range.</summary>
    public static (DateOnly From, DateOnly To, string? Error) ResolveRange(DateOnly? from, DateOnly? to, DateOnly todayUtc)
    {
        var resolvedTo = to ?? todayUtc;
        var resolvedFrom = from ?? resolvedTo.AddDays(-(DefaultRangeDays - 1));

        if (resolvedFrom > resolvedTo)
        {
            return (resolvedFrom, resolvedTo, "from must be on or before to.");
        }

        if (resolvedTo.DayNumber - resolvedFrom.DayNumber + 1 > MaxRangeDays)
        {
            return (resolvedFrom, resolvedTo, $"The range can be at most {MaxRangeDays} days.");
        }

        return (resolvedFrom, resolvedTo, null);
    }

    /// <summary>Row shape for SqlQueryRaw; property names match the column aliases.</summary>
    public sealed class DayRow
    {
        public DateTime Day { get; set; }
        public int Clicks { get; set; }
    }
}
