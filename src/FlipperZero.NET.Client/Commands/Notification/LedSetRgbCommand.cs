using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Sets all three RGB LED channels atomically in a single round-trip.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"led_set_rgb","red":255,"green":0,"blue":128}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// To set a single channel, use <see cref="LedSetCommand"/> instead.
/// </summary>
public readonly struct LedSetRgbCommand : IRpcCommand<LedSetRgbResponse>
{
    /// <param name="red">Red channel intensity 0–255.</param>
    /// <param name="green">Green channel intensity 0–255.</param>
    /// <param name="blue">Blue channel intensity 0–255.</param>
    public LedSetRgbCommand(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>Red channel intensity (0–255).</summary>
    public byte Red { get; }

    /// <summary>Green channel intensity (0–255).</summary>
    public byte Green { get; }

    /// <summary>Blue channel intensity (0–255).</summary>
    public byte Blue { get; }

    /// <inheritdoc />
    public string CommandName => "led_set_rgb";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("red", Red);
        writer.WriteNumber("green", Green);
        writer.WriteNumber("blue", Blue);
    }
}

/// <summary>Response to <see cref="LedSetRgbCommand"/>.</summary>
public readonly struct LedSetRgbResponse : IRpcCommandResponse { }
