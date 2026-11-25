namespace UrlShortener.Infrastructure.Caching;

/// <summary>Hit/miss counters for the benchmark's hit ratio (US-10). Singleton, lock-free.</summary>
public sealed class CacheStats
{
    private long _hits;
    private long _misses;
    private long _errors;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Errors => Interlocked.Read(ref _errors);

    public void Hit() => Interlocked.Increment(ref _hits);
    public void Miss() => Interlocked.Increment(ref _misses);
    public void Error() => Interlocked.Increment(ref _errors);

    public void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _errors, 0);
    }
}
