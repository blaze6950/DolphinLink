using FlipperZero.NET.Commands.Rfid;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for LF RFID commands.
/// </summary>
public static class FlipperRfidExtensions
{
    /// <summary>
    /// Starts a streaming LF RFID read session.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="LfRfidReadEvent"/>
    /// for each detected tag.  Dispose to stop reading and release the RFID hardware.
    /// </returns>
    public static Task<RpcStream<LfRfidReadEvent>> LfRfidReadStartAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendStreamAsync<LfRfidReadStartCommand, LfRfidReadEvent>(new LfRfidReadStartCommand(), ct);
}
