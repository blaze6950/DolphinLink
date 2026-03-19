using FlipperZero.NET.Commands.Input;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for hardware input events.
/// </summary>
public static class FlipperInputExtensions
{
    /// <summary>
    /// Opens a stream that delivers every hardware button event from the Flipper.
    ///
    /// Multiple concurrent listeners are allowed; all active streams receive every
    /// event (broadcast — no exclusive lock on the input record).
    ///
    /// When <paramref name="exitKey"/> and <paramref name="exitType"/> are both
    /// supplied, the daemon replaces its default Back+Short exit trigger with
    /// that combo for the lifetime of this stream.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="exitKey">
    /// Optional key that overrides the daemon's exit trigger.
    /// Must be supplied together with <paramref name="exitType"/>.
    /// </param>
    /// <param name="exitType">
    /// Optional event type that overrides the daemon's exit trigger.
    /// Must be supplied together with <paramref name="exitKey"/>.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="FlipperInputEvent"/>
    /// for every button action.  Dispose the stream to unsubscribe.
    /// </returns>
    public static Task<RpcStream<FlipperInputEvent>> InputListenStartAsync(
        this FlipperRpcClient client,
        FlipperInputKey? exitKey = null,
        FlipperInputType? exitType = null,
        CancellationToken ct = default)
        => client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand { ExitKey = exitKey, ExitType = exitType }, ct);
}
