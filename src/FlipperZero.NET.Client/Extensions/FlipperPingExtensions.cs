using FlipperZero.NET.Commands.Core;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for the <c>ping</c> command.
/// </summary>
public static class FlipperPingExtensions
{
    /// <summary>
    /// Sends a <c>ping</c> and waits for the Flipper to respond with
    /// <c>{"pong":true}</c>.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> when the Flipper acknowledges the ping.</returns>
    public static async Task<bool> PingAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
    {
        var response = await client.SendAsync<PingCommand, PingResponse>(
            new PingCommand(), ct).ConfigureAwait(false);
        return response.Pong;
    }
}
