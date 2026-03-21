using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Identifies a single RGB LED channel on the Flipper Zero.
/// Used by <see cref="LedSetCommand"/>.
/// </summary>
public enum LedChannel
{
    /// <summary>Red channel.</summary>
    Red,

    /// <summary>Green channel.</summary>
    Green,

    /// <summary>Blue channel.</summary>
    Blue,
}

/// <summary>
/// Sets an individual RGB LED channel intensity on the Flipper.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"led_set","color":"red","value":255}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// To set all three channels atomically in a single round-trip, use
/// <see cref="LedSetRgbCommand"/> instead.
/// </summary>
public readonly struct LedSetCommand : IRpcCommand<LedSetResponse>
{
    /// <param name="channel">The RGB channel to set.</param>
    /// <param name="value">Intensity 0–255.</param>
    public LedSetCommand(LedChannel channel, byte value)
    {
        Channel = channel;
        Value = value;
    }

    /// <summary>The RGB channel to control.</summary>
    public LedChannel Channel { get; }

    /// <summary>Intensity 0–255.</summary>
    public byte Value { get; }

    /// <inheritdoc />
    public string CommandName => "led_set";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        string wire = Channel switch
        {
            LedChannel.Red => "red",
            LedChannel.Green => "green",
            LedChannel.Blue => "blue",
            _ => throw new ArgumentOutOfRangeException(nameof(Channel), Channel, null),
        };
        writer.WriteString("color", wire);
        writer.WriteNumber("value", Value);
    }
}

/// <summary>Response to <see cref="LedSetCommand"/>.</summary>
public readonly struct LedSetResponse : IRpcCommandResponse { }
