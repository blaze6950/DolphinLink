using DolphinLink.Client.Commands;
using DolphinLink.Client.Commands.Notification;

namespace DolphinLink.Client.Extensions;

/// <summary>
/// Hand-written partial of <see cref="NotificationExtensions"/> adding
/// single-channel LED convenience helpers and an RgbColor overload.
/// </summary>
public static partial class NotificationExtensions
{
    /// <summary>Sets the red LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetRedAsync(
        this RpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Red, value, ct);

    /// <summary>Sets the green LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetGreenAsync(
        this RpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Green, value, ct);

    /// <summary>Sets the blue LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetBlueAsync(
        this RpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Blue, value, ct);

    /// <summary>
    /// Sets all three RGB LED channels atomically using an <see cref="RgbColor"/> value.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="color">The RGB colour to set.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetRgbResponse> LedSetRgbAsync(
        this RpcClient client,
        RgbColor color,
        CancellationToken ct = default)
        => client.LedSetRgbAsync(color.R, color.G, color.B, ct);
}
