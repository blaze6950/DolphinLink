using FlipperZero.NET.Commands;

namespace FlipperZero.NET;

/// <summary>
/// Public convenience API.  Users call these methods and never touch
/// <c>SendAsync&lt;TCommand, TResponse&gt;</c> directly.
/// </summary>
public sealed partial class FlipperRpcClient
{
    // -----------------------------------------------------------------------
    // Ping
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends a <c>ping</c> and waits for the Flipper to respond with
    /// <c>{"pong":true}</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the Flipper acknowledges the ping.
    /// </returns>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<PingCommand, PingResponse>(
            new PingCommand(), ct).ConfigureAwait(false);
        return response.Pong;
    }

    // -----------------------------------------------------------------------
    // BLE scan
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a BLE device scan on the Flipper.
    /// </summary>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="BleScanEvent"/>
    /// for every advertisement received.  Dispose the stream to stop scanning
    /// and release the BLE radio.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var scan = await client.BleScanStartAsync(ct);
    /// await foreach (var evt in scan.WithCancellation(ct))
    /// {
    ///     Console.WriteLine($"{evt.Address}  RSSI={evt.Rssi}  Name={evt.Name}");
    /// }
    /// </code>
    /// </example>
    public Task<RpcStream<BleScanEvent>> BleScanStartAsync(CancellationToken ct = default)
        => SendStreamAsync<BleScanStartCommand, BleScanEvent>(new BleScanStartCommand(), ct);

    // -----------------------------------------------------------------------
    // Stream close (also called internally by RpcStream<T>.DisposeAsync)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Explicitly closes a stream by id.
    /// Prefer disposing the <see cref="RpcStream{TEvent}"/> returned by
    /// <see cref="BleScanStartAsync"/> instead of calling this directly.
    /// </summary>
    public async Task StreamCloseAsync(uint streamId, CancellationToken ct = default)
    {
        await SendAsync<StreamCloseCommand, StreamCloseResponse>(
            new StreamCloseCommand(streamId), ct).ConfigureAwait(false);
    }
}
