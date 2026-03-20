namespace FlipperZero.NET.Abstractions;

/// <summary>
/// A line-oriented transport for the Flipper NDJSON RPC protocol.
///
/// Implementations include:
///   - <see cref="FlipperRpcTransport"/> — raw USB-CDC serial
///   - <see cref="HeartbeatTransport"/> — keep-alive wrapper
///   - <see cref="PacketSerializationTransport"/> — single-writer serialiser wrapper
///   - In-process fake (tests)
///
/// Threading contract:
///   - <see cref="SendAsync"/> may be called from any thread; implementations
///     that need serialisation wrap themselves in a
///     <see cref="PacketSerializationTransport"/>.
///   - <see cref="ReceiveAsync"/> is called exclusively by the reader loop
///     (single reader).
///   - <see cref="OpenAsync"/> is called once before any I/O.
///   - Closing is via <see cref="IAsyncDisposable.DisposeAsync"/> and
///     cancellation of the <see cref="ReceiveAsync"/> token.
/// </summary>
public interface IFlipperTransport : IAsyncDisposable
{
    /// <summary>Opens the transport. Must be called before <see cref="SendAsync"/> or <see cref="ReceiveAsync"/>.</summary>
    ValueTask OpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes a single line and flushes.
    /// May be called from any thread (the implementation serialises internally if needed).
    /// An empty string sends a bare keep-alive newline.
    /// </summary>
    ValueTask SendAsync(string data, CancellationToken ct = default);

    /// <summary>
    /// Returns an async stream of lines received from the remote side.
    /// Empty / whitespace lines are keep-alive frames; callers may filter them.
    /// The stream ends when the transport is closed or the token is cancelled.
    /// Must only be called from one reader at a time.
    /// </summary>
    IAsyncEnumerable<string> ReceiveAsync(CancellationToken ct = default);
}
