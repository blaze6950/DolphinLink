using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// Reads the current digital level of a GPIO pin.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"gpio_read","pin":"1"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok","data":{"level":true}}</code>
///
/// Supported pins: <see cref="GpioPin.Pin1"/>–<see cref="GpioPin.Pin8"/>.
/// </summary>
public readonly struct GpioReadCommand : IRpcCommand<GpioReadResponse>
{
    /// <param name="pin">The GPIO header pin to read.</param>
    public GpioReadCommand(GpioPin pin) => Pin = pin;

    /// <summary>The GPIO pin to read.</summary>
    public GpioPin Pin { get; }

    /// <inheritdoc />
    public string CommandName => "gpio_read";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", ((int)Pin).ToString());
    }
}

/// <summary>Response to <see cref="GpioReadCommand"/>.</summary>
public readonly struct GpioReadResponse : IRpcCommandResponse
{
    /// <summary><c>true</c> = pin is high; <c>false</c> = pin is low.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }
}
