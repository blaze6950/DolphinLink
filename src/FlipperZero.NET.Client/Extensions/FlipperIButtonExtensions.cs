using FlipperZero.NET.Commands.IButton;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for iButton commands.
/// </summary>
public static class FlipperIButtonExtensions
{
    /// <summary>
    /// Starts a streaming iButton read session.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="IButtonReadEvent"/>
    /// for each detected key.  Dispose to stop reading and release the iButton hardware.
    /// </returns>
    public static Task<RpcStream<IButtonReadEvent>> IButtonReadStartAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendStreamAsync<IButtonReadStartCommand, IButtonReadEvent>(new IButtonReadStartCommand(), ct);
}
