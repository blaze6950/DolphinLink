using FlipperZero.NET.Commands;
using FlipperZero.NET.Commands.Notification;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for notification commands:
/// LED, vibro, speaker, and backlight.
/// </summary>
public static class FlipperNotificationExtensions
{
    // -----------------------------------------------------------------------
    // LED — single channel
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets an LED colour channel intensity.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="channel">The RGB channel to set.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetAsync(
        this FlipperRpcClient client,
        LedChannel channel, byte value,
        CancellationToken ct = default)
        => client.SendAsync<LedSetCommand, LedSetResponse>(new LedSetCommand(channel, value), ct);

    /// <summary>Sets the red LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetRedAsync(
        this FlipperRpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Red, value, ct);

    /// <summary>Sets the green LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetGreenAsync(
        this FlipperRpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Green, value, ct);

    /// <summary>Sets the blue LED channel intensity.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetResponse> LedSetBlueAsync(
        this FlipperRpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.LedSetAsync(LedChannel.Blue, value, ct);

    // -----------------------------------------------------------------------
    // LED — all channels (RGB)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets all three RGB LED channels atomically in a single round-trip.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="red">Red channel intensity 0–255.</param>
    /// <param name="green">Green channel intensity 0–255.</param>
    /// <param name="blue">Blue channel intensity 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetRgbResponse> LedSetRgbAsync(
        this FlipperRpcClient client,
        byte red, byte green, byte blue,
        CancellationToken ct = default)
        => client.SendAsync<LedSetRgbCommand, LedSetRgbResponse>(new LedSetRgbCommand(red, green, blue), ct);

    /// <summary>
    /// Sets all three RGB LED channels atomically in a single round-trip.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="color">The RGB colour to set.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<LedSetRgbResponse> LedSetRgbAsync(
        this FlipperRpcClient client,
        RgbColor color,
        CancellationToken ct = default)
        => client.SendAsync<LedSetRgbCommand, LedSetRgbResponse>(new LedSetRgbCommand(color.R, color.G, color.B), ct);

    // -----------------------------------------------------------------------
    // Vibro
    // -----------------------------------------------------------------------

    /// <summary>Enables or disables the vibration motor.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="enable"><c>true</c> to enable; <c>false</c> to disable.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<VibroResponse> VibroAsync(
        this FlipperRpcClient client,
        bool enable,
        CancellationToken ct = default)
        => client.SendAsync<VibroCommand, VibroResponse>(new VibroCommand(enable), ct);

    // -----------------------------------------------------------------------
    // Speaker
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a continuous tone on the piezo speaker.
    /// Call <see cref="SpeakerStopAsync"/> to release the speaker resource.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="freq">Frequency in Hz.</param>
    /// <param name="volume">Volume 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<SpeakerStartResponse> SpeakerStartAsync(
        this FlipperRpcClient client,
        uint freq, byte volume,
        CancellationToken ct = default)
        => client.SendAsync<SpeakerStartCommand, SpeakerStartResponse>(new SpeakerStartCommand(freq, volume), ct);

    /// <summary>Stops the piezo speaker and releases the speaker resource.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<SpeakerStopResponse> SpeakerStopAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<SpeakerStopCommand, SpeakerStopResponse>(new SpeakerStopCommand(), ct);

    // -----------------------------------------------------------------------
    // Backlight
    // -----------------------------------------------------------------------

    /// <summary>Sets the LCD backlight brightness (0–255).</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="value">Brightness 0–255.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<BacklightResponse> BacklightAsync(
        this FlipperRpcClient client,
        byte value,
        CancellationToken ct = default)
        => client.SendAsync<BacklightCommand, BacklightResponse>(new BacklightCommand(value), ct);
}
