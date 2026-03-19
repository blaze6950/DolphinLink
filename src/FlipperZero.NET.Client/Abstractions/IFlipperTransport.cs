namespace FlipperZero.NET.Abstractions;

/// <summary>
/// A line-oriented transport for the Flipper NDJSON RPC protocol.
///
/// Implementations include:
///   - <see cref="FlipperRpcTransport"/> — USB-CDC via <see cref="System.IO.Ports.SerialPort"/>
///   - Future: BLE, Wi-Fi, WebSerial (WASM bridge), in-process fake (tests)
///
/// Threading contract:
///   - <see cref="SendLineAsync"/> is called exclusively by the writer loop (single writer).
///   - <see cref="ReadLineAsync"/> is called exclusively by the reader loop (single reader).
///   - <see cref="Open"/> and <see cref="Close"/> are called outside both loops.
/// </summary>
public interface IFlipperTransport : IAsyncDisposable
{
    /// <summary>Opens the transport. Must be called before <see cref="SendLineAsync"/> or <see cref="ReadLineAsync"/>.</summary>
    void Open();

    /// <summary>
    /// Closes the transport without disposing it.
    /// Must unblock any in-progress <see cref="ReadLineAsync"/> by causing it to return <c>null</c>.
    /// Safe to call concurrently with <see cref="ReadLineAsync"/>.
    /// </summary>
    void Close();

    /// <summary>
    /// Writes a single JSON line followed by <c>\n</c> and flushes.
    /// Must only be called from the writer loop.
    /// </summary>
    Task SendLineAsync(string json, CancellationToken ct);

    /// <summary>
    /// Reads until the next <c>\n</c> and returns the trimmed line.
    /// Returns <c>null</c> when the transport is closed or the stream ends.
    /// Must only be called from the reader loop.
    /// </summary>
    Task<string?> ReadLineAsync(CancellationToken ct);
}
