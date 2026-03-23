using System.Threading.Channels;
using System.Runtime.Versioning;

namespace FlipperZero.NET.WebSerial;

/// <summary>
/// A <see cref="Stream"/> that bridges the WebSerial ReadableStream pump (JS side)
/// into the standard .NET <see cref="Stream"/> API.
///
/// <para>
/// <b>Read path:</b> The JS pump calls <see cref="WebSerialInterop.OnData"/> for every
/// received chunk.  That static [JSExport] method dispatches to this stream's
/// <c>OnDataReceived</c> via the per-port callback table in
/// <see cref="WebSerialInterop"/>.  Received chunks are enqueued into a bounded
/// <see cref="Channel{T}"/>.  <see cref="ReadAsync"/> dequeues chunks and copies bytes
/// into the caller's buffer.  When the JS side signals EOF (empty byte array),
/// the channel is completed and all subsequent reads return 0 bytes.
/// </para>
///
/// <para>
/// <b>Write path:</b> <see cref="WriteAsync"/> calls <see cref="WebSerialInterop.WriteJs"/>
/// directly; the JS side forwards the bytes to the port's <c>WritableStream</c>.
/// </para>
///
/// <para>
/// <b>Threading:</b> In single-threaded Blazor WASM all JS callbacks arrive on the same
/// cooperative thread as .NET code, so the channel capacity is merely a back-pressure
/// bound rather than a synchronisation primitive.  A capacity of 64 chunks is more
/// than sufficient for normal serial traffic.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class WebSerialStream : Stream, IAsyncDisposable
{
    private const int ChannelCapacity = 64;

    private readonly int _portId;
    private readonly Channel<byte[]> _channel;
    private byte[]? _currentChunk;   // chunk being partially consumed
    private int _currentOffset;       // bytes already consumed from _currentChunk
    private bool _eof;
    private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

    internal WebSerialStream(int portId)
    {
        _portId = portId;
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // Register the dispatch-table entry before starting the pump so no data is lost.
        WebSerialInterop.RegisterCallback(portId, OnDataReceived);
        WebSerialInterop.StartReadingJs(portId);
    }

    // -------------------------------------------------------------------------
    // Data delivery — called by WebSerialInterop.OnData ([JSExport] dispatch)
    // -------------------------------------------------------------------------

    private void OnDataReceived(byte[] data)
    {
        if (data.Length == 0)
        {
            // Empty array is the EOF sentinel — JS pump sends this when the stream ends.
            _channel.Writer.TryComplete();
        }
        else
        {
            // In single-threaded WASM this write will never block (the bounded channel
            // only applies back-pressure across yield points, and the JS pump yields
            // between reads anyway).  TryWrite is fine here; if the channel is somehow
            // full, drop the chunk rather than deadlocking the cooperative scheduler.
            _channel.Writer.TryWrite(data);
        }
    }

    // -------------------------------------------------------------------------
    // Stream overrides — Read
    // -------------------------------------------------------------------------

    public override bool CanRead  => true;
    public override bool CanWrite => true;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Use ReadAsync.");

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_eof || buffer.IsEmpty)
        {
            return 0;
        }

        // If we have leftover bytes from a previous chunk, drain them first.
        if (_currentChunk is not null)
        {
            return CopyFromCurrent(buffer);
        }

        // Block until the next chunk arrives (or the channel completes).
        if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            _eof = true;
            return 0; // Channel completed → EOF
        }

        if (!_channel.Reader.TryRead(out var chunk))
        {
            _eof = true;
            return 0;
        }

        _currentChunk  = chunk;
        _currentOffset = 0;
        return CopyFromCurrent(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    private int CopyFromCurrent(Memory<byte> buffer)
    {
        var remaining = _currentChunk!.Length - _currentOffset;
        var toCopy    = Math.Min(remaining, buffer.Length);

        _currentChunk.AsSpan(_currentOffset, toCopy).CopyTo(buffer.Span);
        _currentOffset += toCopy;

        if (_currentOffset >= _currentChunk.Length)
        {
            _currentChunk  = null;
            _currentOffset = 0;
        }

        return toCopy;
    }

    // -------------------------------------------------------------------------
    // Stream overrides — Write
    // -------------------------------------------------------------------------

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Use WriteAsync.");

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        // [JSImport] requires a plain byte[] — copy if needed.
        var data = buffer.IsEmpty
            ? Array.Empty<byte>()
            : buffer.ToArray();
        await WebSerialInterop.WriteJs(_portId, data).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush() { /* no-op — writes go directly to JS */ }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Unsupported seek operations
    // -------------------------------------------------------------------------

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    // -------------------------------------------------------------------------
    // IAsyncDisposable / Dispose
    // -------------------------------------------------------------------------

    /// <summary>
    /// Unregisters the dispatch-table entry and completes the channel so any
    /// in-progress <see cref="ReadAsync"/> unblocks immediately.
    /// The port itself is closed by <see cref="WebSerialPort.DisposeAsync"/>.
    /// </summary>
    public new async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        WebSerialInterop.UnregisterCallback(_portId);
        _channel.Writer.TryComplete();

        // Drain the channel so the bounded-channel writer (if blocked) can proceed.
        while (_channel.Reader.TryRead(out _)) { }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        WebSerialInterop.UnregisterCallback(_portId);
        _channel.Writer.TryComplete();
        while (_channel.Reader.TryRead(out _)) { }

        base.Dispose(disposing);
    }
}
