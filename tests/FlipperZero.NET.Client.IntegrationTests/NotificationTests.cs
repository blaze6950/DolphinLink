using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for notification commands:
/// <see cref="FlipperRpcClient.LedSetAsync"/>,
/// <see cref="FlipperRpcClient.LedSetRgbAsync"/>,
/// <see cref="FlipperRpcClient.VibroAsync"/>,
/// <see cref="FlipperRpcClient.SpeakerStartAsync"/>,
/// <see cref="FlipperRpcClient.SpeakerStopAsync"/>, and
/// <see cref="FlipperRpcClient.BacklightAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~NotificationTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class NotificationTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // led_set
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting the red LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Red"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Red_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Red, 255);
    }

    /// <summary>
    /// Setting the green LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Green"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Green_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Green, 255);
    }

    /// <summary>
    /// Setting the blue LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Blue"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Blue_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Blue, 255);
    }

    /// <summary>
    /// Setting an LED to zero intensity must succeed without throwing.
    /// Validates: <c>led_set</c> with value=0 (off).
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_ZeroValue_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Red, 0);
    }

    // -----------------------------------------------------------------------
    // led_set_rgb
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting all three LED channels to a specific colour in one call must
    /// succeed without throwing.
    /// Validates: <c>led_set_rgb</c> happy-path with a non-trivial colour.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSetRgb_Succeeds()
    {
        await Client.LedSetRgbAsync(red: 255, green: 128, blue: 0);
    }

    /// <summary>
    /// Animates the RGB LED through a three-act colour melody.
    ///
    /// Act 1 — Hue sweep: a full 360° pass around the colour wheel (36 steps).
    /// Act 2 — Pulse: cyan fades in then out three times.
    /// Act 3 — Colour chords: four named colours played like musical beats,
    ///          repeated twice, with a short black gap between each chord.
    /// Finale — Fade to black over 25 steps.
    ///
    /// <paramref name="speed"/> is a tempo multiplier applied to every base delay:
    /// 1.0 = normal (≈ 8 s total), 0.5 = twice as fast, 2.0 = half speed.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedColorMelodyScenario_Succeeds()
    {
        const double speed = 1.0; // change this knob to rescale every delay

        // Scale a base delay (ms) by the tempo multiplier.
        // Clamp to 1 ms minimum so even speed=0 never blocks indefinitely.
        Task Wait(int baseMs) =>
            Task.Delay(Math.Max(1, (int)(baseMs * speed)));

        // --- Act 1: Hue sweep (full 360°, 36 steps at 50 ms/frame) ----------
        for (int step = 0; step < 36; step++)
        {
            var (r, g, b) = HsvToRgb(step * 10.0, 1.0, 1.0);
            await Client.LedSetRgbAsync(r, g, b);
            await Wait(50);
        }

        // --- Act 2: Pulse — cyan fades in then out, three times (30 ms/frame)
        for (int pulse = 0; pulse < 3; pulse++)
        {
            // Fade in
            for (int v = 0; v <= 255; v += 5)
            {
                var (r, g, b) = HsvToRgb(180.0, 1.0, v / 255.0);
                await Client.LedSetRgbAsync(r, g, b);
                await Wait(30);
            }

            // Fade out
            for (int v = 255; v >= 0; v -= 5)
            {
                var (r, g, b) = HsvToRgb(180.0, 1.0, v / 255.0);
                await Client.LedSetRgbAsync(r, g, b);
                await Wait(30);
            }
        }

        // --- Act 3: Colour chords — four "notes" × two repeats (200 ms hold)
        (byte R, byte G, byte B)[] chords =
        [
            (255, 36, 0), // red-orange
            (255, 200, 0), // amber
            (0, 210, 180), // teal
            (160, 32, 240), // violet
        ];

        for (int repeat = 0; repeat < 2; repeat++)
        {
            foreach (var (r, g, b) in chords)
            {
                await Client.LedSetRgbAsync(r, g, b);
                await Wait(200);
                await Client.LedSetRgbAsync(0, 0, 0); // black gap
                await Wait(50);
            }
        }

        // --- Finale: fade to black over 25 steps (40 ms/frame) --------------
        for (int step = 25; step >= 0; step--)
        {
            var (r, g, b) = HsvToRgb(0.0, 0.0, step / 25.0); // desaturate + dim
            await Client.LedSetRgbAsync(r, g, b);
            await Wait(40);
        }

        await Client.LedSetRgbAsync(0, 0, 0);
    }

    [RequiresFlipperFact]
    public async Task LedComplexCheckScenario_Succeeds()
    {
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
        await Task.Delay(500);

        await Client.LedSetAsync(LedChannel.Red, 255);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
        await Client.LedSetAsync(LedChannel.Green, 255);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
        await Client.LedSetAsync(LedChannel.Blue, 255);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);

        await Task.Delay(500);
        await Client.LedSetAsync(LedChannel.Red, 255);
        await Task.Delay(500);
        await Client.LedSetAsync(LedChannel.Green, 255);
        await Task.Delay(500);
        await Client.LedSetAsync(LedChannel.Blue, 255);
        await Task.Delay(500);

        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
        await Task.Delay(500);

        await Client.LedSetRgbAsync(red: 10, green: 186, blue: 181);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 0, green: 255, blue: 0);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 255, green: 105, blue: 180);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 255, green: 255, blue: 0);
        await Task.Delay(500);
        await Client.LedSetRgbAsync(red: 1, green: 0, blue: 128);
        await Task.Delay(500);

        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
    }

    /// <summary>
    /// Setting all three LED channels to zero in one call must succeed without
    /// throwing.
    /// Validates: <c>led_set_rgb</c> with all channels off (black).
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSetRgb_Black_Succeeds()
    {
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
    }

    // -----------------------------------------------------------------------
    // vibro
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enabling the vibration motor must succeed without throwing.
    /// Validates: <c>vibro</c> enable happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Vibro_EnableAndDisable_Succeeds()
    {
        await Client.VibroAsync(enable: true);
        await Task.Delay(1000);
        await Client.VibroAsync(enable: false);
    }

    // -----------------------------------------------------------------------
    // speaker_start / speaker_stop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starting the piezo speaker at A4 (440 Hz) must succeed without throwing.
    /// Validates: <c>speaker_start</c> happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStart_ValidFreqAndVolume_Succeeds()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        await Task.Delay(1000);
        // Clean up
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Stopping the speaker when it is active must succeed without throwing.
    /// Validates: <c>speaker_stop</c> happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStop_WhenActive_Succeeds()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Stopping the speaker when it was never started must succeed without
    /// throwing (the daemon's <c>speaker_stop</c> is idempotent).
    /// Validates: idempotent stop path in the <c>speaker_stop</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStop_WhenNotActive_Succeeds()
    {
        // Ensure speaker is off first
        await Client.SpeakerStopAsync();
        // Call again — must still succeed
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Attempting to start the speaker a second time while it is already
    /// active must throw a <see cref="FlipperRpcException"/> with the
    /// <c>resource_busy</c> error code.
    /// Validates: the RESOURCE_SPEAKER bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        try
        {
            var ex = await Assert.ThrowsAsync<FlipperRpcException>(() =>
                Client.SpeakerStartAsync(freq: 880, volume: 128));

            Assert.Equal("resource_busy", ex.ErrorCode);
        }
        finally
        {
            await Client.SpeakerStopAsync();
        }
    }

    // -----------------------------------------------------------------------
    // backlight
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting the backlight to maximum brightness must succeed without
    /// throwing.
    /// Validates: <c>backlight</c> happy-path with value=255.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_MaxValue_Succeeds()
    {
        await Client.BacklightAsync(255);
    }

    /// <summary>
    /// Setting the backlight to zero brightness must succeed without throwing.
    /// Validates: <c>backlight</c> happy-path with value=0.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_ZeroValue_Succeeds()
    {
        await Client.BacklightAsync(0);
    }

    /// <summary>
    /// Setting the backlight to mid-range brightness must succeed without
    /// throwing.
    /// Validates: <c>backlight</c> happy-path with an intermediate value.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_MidValue_Succeeds()
    {
        await Client.BacklightAsync(128);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts HSV colour space to RGB bytes.
    /// </summary>
    /// <param name="h">Hue in degrees [0, 360).</param>
    /// <param name="s">Saturation [0, 1].</param>
    /// <param name="v">Value / brightness [0, 1].</param>
    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        if (s == 0.0)
        {
            byte grey = (byte)(v * 255);
            return (grey, grey, grey);
        }

        double sector = h / 60.0;
        int i = (int)Math.Floor(sector) % 6;
        double f = sector - Math.Floor(sector);

        double p = v * (1.0 - s);
        double q = v * (1.0 - s * f);
        double t = v * (1.0 - s * (1.0 - f));

        (double r, double g, double b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };

        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}