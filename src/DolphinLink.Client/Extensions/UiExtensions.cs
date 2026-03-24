using DolphinLink.Client.Commands.Ui;
using DolphinLink.Client.Exceptions;

namespace DolphinLink.Client.Extensions;

/// <summary>
/// Hand-written partial of <see cref="UiExtensions"/> adding the screen session helper.
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
public static partial class UiExtensions
{
    /// <summary>
    /// Claims exclusive control of the Flipper screen and returns a
    /// <see cref="ScreenSession"/> that exposes all draw primitives.
    ///
    /// The daemon's own status ViewPort is hidden and a secondary full-screen
    /// ViewPort is activated.  Dispose the returned session to release the
    /// screen and restore the daemon's ViewPort automatically.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="ScreenSession"/> that must be disposed when the
    /// caller is done drawing.
    /// </returns>
    /// <exception cref="RpcException">
    /// Thrown with <c>resource_busy</c> if another client already holds the screen.
    /// </exception>
    public static async Task<ScreenSession> UiScreenAcquireAsync(
        this RpcClient client,
        CancellationToken ct = default)
    {
        await client.SendAsync<UiScreenAcquireCommand, UiScreenAcquireResponse>(
            new UiScreenAcquireCommand(), ct).ConfigureAwait(false);

        return new ScreenSession(client);
    }
}
