using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// Sets the digital output level of a GPIO pin.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"gpio_write","pin":"1","level":true}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// The pin is configured as a push-pull output before driving.
/// Supported pins: <see cref="GpioPin.Pin1"/>–<see cref="GpioPin.Pin8"/>.
/// </summary>
public readonly struct GpioWriteCommand : IRpcCommand<GpioWriteResponse>
{
    /// <param name="pin">The GPIO header pin to drive.</param>
    /// <param name="level"><c>true</c> to drive high; <c>false</c> to drive low.</param>
    public GpioWriteCommand(GpioPin pin, bool level)
    {
        Pin = pin;
        Level = level;
    }

    /// <summary>The GPIO pin to drive.</summary>
    public GpioPin Pin { get; }

    /// <summary><c>true</c> = drive high; <c>false</c> = drive low.</summary>
    public bool Level { get; }

    /// <inheritdoc />
    public string CommandName => "gpio_write";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", ((int)Pin).ToString());
        writer.WriteBoolean("level", Level);
    }
}

/// <summary>Response to <see cref="GpioWriteCommand"/>.</summary>
public readonly struct GpioWriteResponse : IRpcCommandResponse { }
