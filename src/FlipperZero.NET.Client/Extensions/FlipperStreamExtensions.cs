using FlipperZero.NET.Commands.Core;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for explicit stream close.
/// </summary>
/// <remarks>
/// Prefer disposing the <see cref="RpcStream{TEvent}"/> returned by stream-open methods
/// instead of calling <see cref="StreamCloseAsync"/> directly — disposal sends the
/// <c>stream_close</c> command automatically.
/// </remarks>
public static class FlipperStreamExtensions
{
    /// <summary>
    /// Explicitly closes a stream by id.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="streamId">The numeric stream id to close.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task StreamCloseAsync(
        this FlipperRpcClient client,
        uint streamId,
        CancellationToken ct = default)
    {
        await client.SendAsync<StreamCloseCommand, StreamCloseResponse>(
            new StreamCloseCommand(streamId), ct).ConfigureAwait(false);
    }
}
