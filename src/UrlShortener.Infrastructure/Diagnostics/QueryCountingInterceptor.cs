using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UrlShortener.Infrastructure.Diagnostics;

/// <summary>Counts SQL commands EF Core sends. Proves "1 DB query per redirect" (US-02); reused by US-04.</summary>
public sealed class QueryCounter
{
    private long _count;
    public long Count => Interlocked.Read(ref _count);
    public void Increment() => Interlocked.Increment(ref _count);
    public void Reset() => Interlocked.Exchange(ref _count, 0);
}

public sealed class QueryCountingInterceptor(QueryCounter counter) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        counter.Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        counter.Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        counter.Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return ValueTask.FromResult(result);
    }
}
