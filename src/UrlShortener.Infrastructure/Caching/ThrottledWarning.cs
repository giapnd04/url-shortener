namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// When Redis is down every request fails over to SQL. Logging each failure would flood the logs
/// (thousands per second), so a Warning is written at most once per interval (US-08).
/// </summary>
public sealed class ThrottledWarning(TimeProvider time, TimeSpan interval)
{
    private long _lastTicks = long.MinValue;

    public bool ShouldLog()
    {
        var now = time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastTicks);
        if (last != long.MinValue && now - last < interval.Ticks)
        {
            return false;
        }

        // Only the thread that wins the swap logs; concurrent failures in the same instant are skipped.
        return Interlocked.CompareExchange(ref _lastTicks, now, last) == last;
    }
}
