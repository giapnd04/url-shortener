using System.Threading.Channels;
using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// In-process bounded queue between redirects (many writers) and <see cref="ClickWriterService"/>
/// (one reader). Singleton.
///
/// Backpressure policy: drop and count. A redirect must never wait for analytics, so when the queue
/// is full (DB slower than traffic for a while), TryWrite fails at once and the click is counted as
/// dropped instead of blocking the request or growing memory without limit.
///
/// Delivery is at-most-once: whatever is still in the queue when the process crashes is lost.
/// Acceptable for click statistics; not acceptable for anything billed (that needs an outbox or broker).
/// </summary>
public sealed class ClickQueue
{
    private readonly Channel<ClickEvent> _channel;
    private long _enqueued;
    private long _dropped;

    public ClickQueue(IOptions<AnalyticsOptions> options)
    {
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            // Wait (not DropWrite): with Wait, TryWrite returns false when full, so we can count drops.
            // DropWrite would report success while silently discarding the item.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(ClickEvent click)
    {
        if (_channel.Writer.TryWrite(click))
        {
            Interlocked.Increment(ref _enqueued);
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>No more writes; the reader drains what is left and then sees the channel as completed.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
