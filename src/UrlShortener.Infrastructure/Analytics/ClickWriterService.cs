using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>Writes one batch of clicks. Separate so the worker can be unit-tested without a database.</summary>
public interface IClickBatchWriter
{
    Task WriteAsync(IReadOnlyList<ClickEvent> batch, CancellationToken ct);
}

/// <summary>One INSERT batch per call: EF Core sends the rows together, not one round-trip per click.</summary>
public sealed class SqlClickBatchWriter(IServiceScopeFactory scopes) : IClickBatchWriter
{
    public async Task WriteAsync(IReadOnlyList<ClickEvent> batch, CancellationToken ct)
    {
        // The worker is a singleton; DbContext is scoped, so take a fresh scope per batch.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        db.ClickEvents.AddRange(batch);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// US-13 background worker: reads <see cref="ClickQueue"/> and writes batches of up to BatchSize
/// clicks, or whatever arrived within FlushInterval, whichever comes first.
///
/// Graceful shutdown: StopAsync completes the queue, then the loop drains everything still queued
/// and writes it before returning (bounded by the host's shutdown timeout). A crash or kill -9
/// loses the queue: at-most-once, documented in ADR-006.
/// A failed batch is logged and dropped rather than retried forever, so one bad row cannot stall
/// analytics; the count is exposed for monitoring.
/// </summary>
public sealed class ClickWriterService(
    ClickQueue queue,
    IClickBatchWriter writer,
    IOptions<AnalyticsOptions> options,
    ILogger<ClickWriterService> logger) : BackgroundService
{
    private long _written;
    private long _failed;

    public long Written => Interlocked.Read(ref _written);
    public long Failed => Interlocked.Read(ref _failed);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting clicks first, so the drain below has a definite end.
        queue.Complete();
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchSize = options.Value.BatchSize;
        var interval = options.Value.FlushInterval;
        var batch = new List<ClickEvent>(batchSize);
        var reader = queue.Reader;

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(interval);
                try
                {
                    while (batch.Count < batchSize)
                    {
                        if (reader.TryRead(out var click))
                        {
                            batch.Add(click);
                        }
                        else if (!await reader.WaitToReadAsync(window.Token))
                        {
                            break; // completed
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Flush interval elapsed with a partial batch: write what we have.
                }

                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; fall through to the drain.
        }

        // Drain: the partial batch plus anything still queued. CancellationToken.None so the last
        // clicks are written; the host's shutdown timeout is the upper bound.
        while (reader.TryRead(out var click))
        {
            batch.Add(click);
            if (batch.Count >= batchSize)
            {
                await FlushAsync(batch);
            }
        }

        await FlushAsync(batch);
    }

    private async Task FlushAsync(List<ClickEvent> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await writer.WriteAsync(batch, CancellationToken.None);
            Interlocked.Add(ref _written, batch.Count);
        }
        catch (Exception ex)
        {
            Interlocked.Add(ref _failed, batch.Count);
            logger.LogError(ex, "Writing {Count} click events failed; they are dropped", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
