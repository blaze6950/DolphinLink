namespace DolphinLink.Client.Commands;

/// <summary>
/// A 24-bit RGB colour value used to set the Flipper's RGB LED.
/// </summary>
/// <param name="R">Red channel intensity 0–255.</param>
/// <param name="G">Green channel intensity 0–255.</param>
/// <param name="B">Blue channel intensity 0–255.</param>
public readonly struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Red channel intensity (0–255).</summary>
    public byte R { get; } = R;

    /// <summary>Green channel intensity (0–255).</summary>
    public byte G { get; } = G;

    /// <summary>Blue channel intensity (0–255).</summary>
    public byte B { get; } = B;

    // ------------------------------------------------------------------
    // Common presets
    // ------------------------------------------------------------------

    /// <summary>All channels off (0, 0, 0).</summary>
    public static RgbColor Off => new(0, 0, 0);

    /// <summary>Full white (255, 255, 255).</summary>
    public static RgbColor White => new(255, 255, 255);

    /// <summary>Full red (255, 0, 0).</summary>
    public static RgbColor Red => new(255, 0, 0);

    /// <summary>Full green (0, 255, 0).</summary>
    public static RgbColor Green => new(0, 255, 0);

    /// <summary>Full blue (0, 0, 255).</summary>
    public static RgbColor Blue => new(0, 0, 255);

    /// <summary>Full yellow (255, 255, 0).</summary>
    public static RgbColor Yellow => new(255, 255, 0);

    /// <summary>Full cyan (0, 255, 255).</summary>
    public static RgbColor Cyan => new(0, 255, 255);

    /// <summary>Full magenta (255, 0, 255).</summary>
    public static RgbColor Magenta => new(255, 0, 255);

    /// <summary>Full orange (255, 128, 0).</summary>
    public static RgbColor Orange => new(255, 128, 0);

    /// <summary>.NET purple — the official .NET brand colour (#512BD4), used as the default LED connection indicator.</summary>
    public static RgbColor DotNetPurple => new(0x51, 0x2B, 0xD4);

    // ------------------------------------------------------------------
    // Deconstruct
    // ------------------------------------------------------------------

    /// <summary>Deconstructs the colour into its three channel bytes.</summary>
    public void Deconstruct(out byte r, out byte g, out byte b)
    {
        r = R;
        g = G;
        b = B;
    }

    /// <inheritdoc />
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
