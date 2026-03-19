using FlipperZero.NET.Commands.Ir;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for IR transmit and receive commands.
/// </summary>
public static class FlipperIrExtensions
{
    /// <summary>
    /// Transmits a decoded IR signal (protocol + address + command).
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="protocol">IR protocol to use.</param>
    /// <param name="address">Device address field.</param>
    /// <param name="command">Command field.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<IrTxResponse> IrTxAsync(
        this FlipperRpcClient client,
        IrProtocol protocol, uint address, uint command,
        CancellationToken ct = default)
        => client.SendAsync<IrTxCommand, IrTxResponse>(new IrTxCommand(protocol, address, command), ct);

    /// <summary>
    /// Transmits a raw IR timing array.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="timings">Microsecond durations, alternating mark/space.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<IrTxRawResponse> IrTxRawAsync(
        this FlipperRpcClient client,
        uint[] timings,
        CancellationToken ct = default)
        => client.SendAsync<IrTxRawCommand, IrTxRawResponse>(new IrTxRawCommand(timings), ct);

    /// <summary>
    /// Starts the IR receiver on the Flipper.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="IrReceiveEvent"/>
    /// for every decoded IR signal.  Dispose the stream to stop receiving.
    /// </returns>
    public static Task<RpcStream<IrReceiveEvent>> IrReceiveStartAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendStreamAsync<IrReceiveStartCommand, IrReceiveEvent>(new IrReceiveStartCommand(), ct);
}
