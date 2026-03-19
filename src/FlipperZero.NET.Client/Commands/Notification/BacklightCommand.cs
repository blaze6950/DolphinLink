using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Sets the LCD backlight brightness.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"backlight","value":200}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// A value of <c>0</c> turns the backlight off; <c>255</c> is maximum brightness.
/// </summary>
public readonly struct BacklightCommand : IRpcCommand<BacklightResponse>
{
    /// <param name="value">Brightness 0–255.</param>
    public BacklightCommand(byte value) => Value = value;

    /// <summary>Brightness level 0–255.</summary>
    public byte Value { get; }

    /// <inheritdoc />
    public string CommandName => "backlight";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("value", Value);
    }
}

/// <summary>Response to <see cref="BacklightCommand"/>.</summary>
public readonly struct BacklightResponse : IRpcCommandResponse { }
