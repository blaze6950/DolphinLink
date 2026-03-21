using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Self-contained state for an open RPC stream.
///
/// Owns an unbounded <see cref="Channel{T}"/> with <c>SingleReader = true</c> and
/// <c>SingleWriter = true</c> — the reader loop is the sole writer, and the
/// <see cref="RpcStream{TEvent}"/> enumerator is the sole reader.
/// Because the channel is unbounded, <see cref="Writer"/>.<c>TryWrite</c> always succeeds.
/// </summary>
internal sealed class StreamState
{
    private readonly Channel<JsonElement> _channel;

    public StreamState()
    {
        _channel = Channel.CreateUnbounded<JsonElement>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    }

    /// <summary>Read end exposed to <see cref="RpcStream{TEvent}"/>.</summary>
    public ChannelReader<JsonElement> Reader => _channel.Reader;

    /// <summary>Write end used exclusively by <see cref="RpcStreamManager"/>.</summary>
    public ChannelWriter<JsonElement> Writer => _channel.Writer;

    /// <summary>Marks the channel complete (normal close, no exception).</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>Faults the channel so the consumer sees <paramref name="ex"/>.</summary>
    public void Fault(Exception ex) => _channel.Writer.TryComplete(ex);
}
