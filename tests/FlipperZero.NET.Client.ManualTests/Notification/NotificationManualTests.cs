using FlipperZero.NET.Commands.Notification;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.Notification;

/// <summary>
/// Manual tests for notification commands that produce visible/audible output
/// requiring human verification.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~NotificationManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class NotificationManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// Animates the RGB LED through a three-act colour melody.
    ///
    /// Act 1 — Hue sweep: a full 360° pass around the colour wheel (36 steps).
    /// Act 2 — Pulse: cyan fades in then out three times.
    /// Act 3 — Colour chords: four named colours played like musical beats,
    ///          repeated twice, with a short black gap between each chord.
    /// Finale — Fade to black over 25 steps.
    ///
    /// Requires human verification that the LED animates correctly.
    /// </summary>
    [Trait("Category", "Manual")]
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
            (255, 36, 0),    // red-orange
            (255, 200, 0),   // amber
            (0, 210, 180),   // teal
            (160, 32, 240),  // violet
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

    /// <summary>
    /// Runs sequential RGB channel checks, lighting each channel individually
    /// and then in combination.
    ///
    /// Requires human verification that the LED colours match expectations.
    /// </summary>
    [Trait("Category", "Manual")]
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
