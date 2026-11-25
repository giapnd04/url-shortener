using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.UnitTests;

public class ClickQueueTests
{
    private static IOptions<AnalyticsOptions> Options(int capacity = 10, int batchSize = 500, int flushMs = 50) =>
        Microsoft.Extensions.Options.Options.Create(new AnalyticsOptions
        {
            QueueCapacity = capacity,
            BatchSize = batchSize,
            FlushInterval = TimeSpan.FromMilliseconds(flushMs),
        });

    private static ClickEvent Click(long linkId = 1) => new() { LinkId = linkId, ClickedAt = DateTime.UtcNow };

    [Fact]
    public void Full_queue_drops_and_counts_instead_of_blocking()
    {
        var queue = new ClickQueue(Options(capacity: 3));

        var results = Enumerable.Range(0, 5).Select(_ => queue.TryEnqueue(Click())).ToList();

        Assert.Equal([true, true, true, false, false], results);
        Assert.Equal(3, queue.Enqueued);
        Assert.Equal(2, queue.Dropped);
    }

    [Fact]
    public async Task Worker_writes_in_batches_of_at_most_batch_size()
    {
        var queue = new ClickQueue(Options(capacity: 100));
        var writer = new RecordingWriter();
        var worker = new ClickWriterService(queue, writer, Options(capacity: 100, batchSize: 4), NullLogger<ClickWriterService>.Instance);
        for (var i = 0; i < 10; i++)
        {
            queue.TryEnqueue(Click(i));
        }

        await worker.StartAsync(default);
        await WaitUntil(() => writer.Total == 10);
        await worker.StopAsync(default);

        Assert.All(writer.Batches, b => Assert.InRange(b.Count, 1, 4));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (long)i), writer.Batches.SelectMany(b => b).Select(c => c.LinkId));
        Assert.Equal(10, worker.Written);
    }

    [Fact]
    public async Task Stop_flushes_clicks_that_are_still_queued()
    {
        var queue = new ClickQueue(Options(capacity: 100));
        var writer = new RecordingWriter();
        // Long flush interval, so nothing would be written before Stop unless the batch filled up.
        var worker = new ClickWriterService(queue, writer, Options(capacity: 100, batchSize: 500, flushMs: 60_000),
            NullLogger<ClickWriterService>.Instance);
        await worker.StartAsync(default);
        for (var i = 0; i < 7; i++)
        {
            queue.TryEnqueue(Click(i));
        }

        await worker.StopAsync(default);

        Assert.Equal(7, writer.Total);
        Assert.False(queue.TryEnqueue(Click()), "the queue must be closed after stop");
    }

    [Fact]
    public async Task Failed_batch_is_counted_and_the_worker_keeps_going()
    {
        var queue = new ClickQueue(Options(capacity: 100));
        var writer = new RecordingWriter { FailFirst = true };
        var worker = new ClickWriterService(queue, writer, Options(capacity: 100, batchSize: 2), NullLogger<ClickWriterService>.Instance);
        await worker.StartAsync(default);

        queue.TryEnqueue(Click(1));
        queue.TryEnqueue(Click(2));
        await WaitUntil(() => worker.Failed == 2);
        queue.TryEnqueue(Click(3));
        await worker.StopAsync(default);

        Assert.Equal(2, worker.Failed);
        Assert.Equal(1, worker.Written);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition not reached within 5 s");
    }

    private sealed class RecordingWriter : IClickBatchWriter
    {
        private readonly Lock _lock = new();
        public List<List<ClickEvent>> Batches { get; } = [];
        public bool FailFirst { get; set; }

        public int Total
        {
            get { lock (_lock) { return Batches.Sum(b => b.Count); } }
        }

        public Task WriteAsync(IReadOnlyList<ClickEvent> batch, CancellationToken ct)
        {
            lock (_lock)
            {
                if (FailFirst)
                {
                    FailFirst = false;
                    throw new InvalidOperationException("simulated DB failure");
                }

                Batches.Add(batch.ToList()); // copy: the worker reuses its list
            }

            return Task.CompletedTask;
        }
    }
}
