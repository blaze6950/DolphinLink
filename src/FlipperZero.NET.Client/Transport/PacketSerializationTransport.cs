using System.Threading.Channels;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Exceptions;

namespace FlipperZero.NET.Transport;

/// <summary>
/// Transport decorator that serialises all outbound writes through a
/// <see cref="BoundedChannel{T}"/> writer loop, providing a single-writer
/// guarantee for the underlying transport regardless of how many callers
/// invoke <see cref="SendAsync"/> concurrently.
///
/// Architecture
/// ============
/// <code>
///   SerialPortTransport   (raw USB-CDC)
///       ↑
///   PacketSerializationTransport   (this class — single-writer serialiser)
///       ↑
///   HeartbeatTransport   (keep-alive)
///       ↑
///   FlipperRpcClient     (RPC logic)
/// </code>
///
/// <see cref="ReceiveAsync"/> is forwarded directly to the inner transport.
/// </summary>
internal sealed class PacketSerializationTransport : IFlipperTransport
{
    private readonly IFlipperTransport _inner;

    // Outbound channel: all callers of SendAsync enqueue here; the writer
    // loop is the only one that calls _inner.SendAsync.
    private readonly Channel<string> _outbound = Channel.CreateBounded<string>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private Task? _writerTask;
    private readonly CancellationTokenSource _writerCts = new();

    public PacketSerializationTransport(IFlipperTransport inner)
    {
        _inner = inner;
    }

    /// <inheritdoc/>
    public async ValueTask OpenAsync(CancellationToken ct = default)
    {
        await _inner.OpenAsync(ct).ConfigureAwait(false);
        _writerTask = Task.Run(() => WriterLoopAsync(_writerCts.Token));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Enqueues <paramref name="data"/> on the outbound channel.  The writer
    /// loop dequeues and calls <c>_inner.SendAsync</c>, ensuring a single writer.
    /// If the channel has been sealed due to a transport error, throws
    /// <see cref="FlipperDisconnectedException"/> instead of the raw
    /// <see cref="ChannelClosedException"/> so callers see a consistent exception type.
    /// </remarks>
    public async ValueTask SendAsync(string data, CancellationToken ct = default)
    {
        try
        {
            await _outbound.Writer.WriteAsync(data, ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new FlipperDisconnectedException(
                DisconnectReason.ConnectionLost, "Connection lost.", ex);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ReceiveAsync(CancellationToken ct = default)
        => _inner.ReceiveAsync(ct);

    // -------------------------------------------------------------------------
    // Writer loop
    // -------------------------------------------------------------------------

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var data in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await _inner.SendAsync(data, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch
        {
            // Inner transport error — seal the channel so further SendAsync calls fail fast.
            _outbound.Writer.TryComplete();
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Seal the outbound channel and cancel the writer loop.
        _outbound.Writer.TryComplete();
        await _writerCts.CancelAsync().ConfigureAwait(false);

        if (_writerTask is not null)
        {
            await _writerTask.ConfigureAwait(false);
        }

        _writerCts.Dispose();
        await _inner.DisposeAsync().ConfigureAwait(false);
    }
}
