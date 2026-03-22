using FlipperZero.NET.Commands.Ui;
using FlipperZero.NET.Exceptions;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Hand-written partial of <see cref="FlipperUiExtensions"/> adding the screen session helper.
/// </summary>
/// <remarks>
/// Typical usage pattern:
/// <code>
/// await using var screen = await client.UiScreenAcquireAsync();
/// await screen.DrawStrAsync(10, 32, "Hello!");
/// await screen.FlushAsync();
/// // screen is automatically released when the using block exits
/// </code>
/// </remarks>
public static partial class FlipperUiExtensions
{
    /// <summary>
    /// Claims exclusive control of the Flipper screen and returns a
    /// <see cref="FlipperScreenSession"/> that exposes all draw primitives.
    ///
    /// The daemon's own status ViewPort is hidden and a secondary full-screen
    /// ViewPort is activated.  Dispose the returned session to release the
    /// screen and restore the daemon's ViewPort automatically.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="FlipperScreenSession"/> that must be disposed when the
    /// caller is done drawing.
    /// </returns>
    /// <exception cref="FlipperRpcException">
    /// Thrown with <c>resource_busy</c> if another client already holds the screen.
    /// </exception>
    public static async Task<FlipperScreenSession> UiScreenAcquireAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
    {
        await client.SendAsync<UiScreenAcquireCommand, UiScreenAcquireResponse>(
            new UiScreenAcquireCommand(), ct).ConfigureAwait(false);

        return new FlipperScreenSession(client);
    }
}
