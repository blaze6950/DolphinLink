using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// Watches a GPIO pin for level changes, opening a stream that yields one
/// <see cref="GpioWatchEvent"/> per rising or falling edge.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"gpio_watch_start","pin":"1"}</code>
///
/// Wire format (stream open response):
/// <code>{"id":N,"stream":M}</code>
///
/// Wire format (stream event):
/// <code>{"event":{"pin":"1","level":true},"stream":M}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Dispose the returned <see cref="RpcStream{TEvent}"/> to remove the interrupt
/// and release the pin.
/// </summary>
public readonly struct GpioWatchStartCommand : IRpcStreamCommand<GpioWatchEvent>
{
    /// <param name="pin">
    /// Physical GPIO header pin to watch: <see cref="GpioPin.Pin1"/>–<see cref="GpioPin.Pin8"/>.
    /// Maps to the <c>gpio_ext_*</c> symbols on the Flipper Zero expansion connector.
    /// </param>
    public GpioWatchStartCommand(GpioPin pin) => Pin = pin;

    /// <summary>The GPIO pin to watch.</summary>
    public GpioPin Pin { get; }

    /// <inheritdoc />
    public string CommandName => "gpio_watch_start";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", ((int)Pin).ToString());
    }
}

/// <summary>A GPIO level-change event delivered on each rising or falling edge.</summary>
public readonly struct GpioWatchEvent : IRpcCommandResponse
{
    /// <summary>Pin that changed.</summary>
    [JsonPropertyName("pin")]
    [JsonConverter(typeof(GpioPinJsonConverter))]
    public GpioPin Pin { get; init; }

    /// <summary><c>true</c> = pin went high; <c>false</c> = pin went low.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }
}
