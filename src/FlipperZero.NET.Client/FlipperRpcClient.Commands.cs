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
    /// <returns><c>true</c> when the Flipper acknowledges the ping.</returns>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<PingCommand, PingResponse>(
            new PingCommand(), ct).ConfigureAwait(false);
        return response.Pong;
    }

    // -----------------------------------------------------------------------
    // IR receive
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts the IR receiver on the Flipper.
    /// </summary>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="IrReceiveEvent"/>
    /// for every decoded IR signal.  Dispose the stream to stop receiving.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var stream = await client.IrReceiveStartAsync(ct);
    /// await foreach (var evt in stream.WithCancellation(ct))
    /// {
    ///     Console.WriteLine($"{evt.Protocol}  addr={evt.Address}  cmd={evt.Command}  repeat={evt.Repeat}");
    /// }
    /// </code>
    /// </example>
    public Task<RpcStream<IrReceiveEvent>> IrReceiveStartAsync(CancellationToken ct = default)
        => SendStreamAsync<IrReceiveStartCommand, IrReceiveEvent>(new IrReceiveStartCommand(), ct);

    // -----------------------------------------------------------------------
    // GPIO watch
    // -----------------------------------------------------------------------

    /// <summary>
    /// Watches a GPIO pin for level changes.
    /// </summary>
    /// <param name="pin">
    /// Physical GPIO header pin label: <c>"1"</c> through <c>"8"</c>.
    /// </param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="GpioWatchEvent"/>
    /// on each rising or falling edge.  Dispose the stream to remove the interrupt.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var stream = await client.GpioWatchStartAsync("6", ct);
    /// await foreach (var evt in stream.WithCancellation(ct))
    /// {
    ///     Console.WriteLine($"pin {evt.Pin} -> {(evt.Level ? "HIGH" : "LOW")}");
    /// }
    /// </code>
    /// </example>
    public Task<RpcStream<GpioWatchEvent>> GpioWatchStartAsync(string pin, CancellationToken ct = default)
        => SendStreamAsync<GpioWatchStartCommand, GpioWatchEvent>(new GpioWatchStartCommand(pin), ct);

    // -----------------------------------------------------------------------
    // Sub-GHz RX
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts Sub-GHz OOK raw receive.
    /// </summary>
    /// <param name="freq">
    /// Carrier frequency in Hz.  Defaults to 433.92 MHz (<c>null</c>).
    /// </param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="SubGhzRxEvent"/>
    /// for each raw OOK pulse.  Dispose the stream to stop receiving and release the radio.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var stream = await client.SubGhzRxStartAsync(433920000, ct);
    /// await foreach (var evt in stream.WithCancellation(ct))
    /// {
    ///     Console.WriteLine($"level={evt.Level}  duration={evt.DurationUs} us");
    /// }
    /// </code>
    /// </example>
    public Task<RpcStream<SubGhzRxEvent>> SubGhzRxStartAsync(uint? freq = null, CancellationToken ct = default)
        => SendStreamAsync<SubGhzRxStartCommand, SubGhzRxEvent>(new SubGhzRxStartCommand(freq), ct);

    // -----------------------------------------------------------------------
    // NFC scan
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts NFC protocol scanning on the Flipper.
    /// </summary>
    /// <remarks>
    /// Uses <c>NfcScanner</c> which detects protocol type only.
    /// No UID is available without a full anti-collision poller (<c>NfcPoller</c>).
    /// </remarks>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="NfcScanEvent"/>
    /// for each detected NFC tag.  Dispose the stream to stop scanning and release the NFC hardware.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var stream = await client.NfcScanStartAsync(ct);
    /// await foreach (var evt in stream.WithCancellation(ct))
    /// {
    ///     Console.WriteLine($"NFC tag detected: protocol={evt.Protocol}");
    /// }
    /// </code>
    /// </example>
    public Task<RpcStream<NfcScanEvent>> NfcScanStartAsync(CancellationToken ct = default)
        => SendStreamAsync<NfcScanStartCommand, NfcScanEvent>(new NfcScanStartCommand(), ct);

    // -----------------------------------------------------------------------
    // Stream close (also called internally by RpcStream<T>.DisposeAsync)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Explicitly closes a stream by id.
    /// Prefer disposing the <see cref="RpcStream{TEvent}"/> returned by the
    /// stream-open methods instead of calling this directly.
    /// </summary>
    public async Task StreamCloseAsync(uint streamId, CancellationToken ct = default)
    {
        await SendAsync<StreamCloseCommand, StreamCloseResponse>(
            new StreamCloseCommand(streamId), ct).ConfigureAwait(false);
    }
}
