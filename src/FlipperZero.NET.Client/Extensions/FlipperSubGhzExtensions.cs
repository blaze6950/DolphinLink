using FlipperZero.NET.Commands.SubGhz;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for Sub-GHz radio commands.
/// </summary>
public static class FlipperSubGhzExtensions
{
    /// <summary>
    /// Transmits a raw OOK Sub-GHz packet at the specified frequency.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="freq">Carrier frequency in Hz.</param>
    /// <param name="timings">Microsecond pulse durations, alternating mark/space.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<SubGhzTxResponse> SubGhzTxAsync(
        this FlipperRpcClient client,
        uint freq, uint[] timings,
        CancellationToken ct = default)
        => client.SendAsync<SubGhzTxCommand, SubGhzTxResponse>(new SubGhzTxCommand(freq, timings), ct);

    /// <summary>
    /// Returns the current RSSI (in dBm) at the given frequency.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="freq">Carrier frequency in Hz.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>RSSI in dBm as a signed integer.</returns>
    public static async Task<int> SubGhzGetRssiAsync(
        this FlipperRpcClient client,
        uint freq,
        CancellationToken ct = default)
    {
        var r = await client.SendAsync<SubGhzGetRssiCommand, SubGhzGetRssiResponse>(
            new SubGhzGetRssiCommand(freq), ct).ConfigureAwait(false);
        return r.Rssi;
    }

    /// <summary>
    /// Starts Sub-GHz OOK raw receive.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="freq">
    /// Carrier frequency in Hz.  Defaults to 433.92 MHz (<c>null</c>).
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="SubGhzRxEvent"/>
    /// for each raw OOK pulse.  Dispose the stream to stop receiving and release the radio.
    /// </returns>
    public static Task<RpcStream<SubGhzRxEvent>> SubGhzRxStartAsync(
        this FlipperRpcClient client,
        uint? freq = null,
        CancellationToken ct = default)
        => client.SendStreamAsync<SubGhzRxStartCommand, SubGhzRxEvent>(new SubGhzRxStartCommand(freq), ct);
}
