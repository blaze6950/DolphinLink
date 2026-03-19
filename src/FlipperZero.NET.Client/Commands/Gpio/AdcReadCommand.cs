using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// Reads the ADC voltage on a GPIO pin that supports analog input.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"adc_read","pin":"1"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok","data":{"raw":2048,"mv":1650}}</code>
///
/// ADC-capable pins: <see cref="GpioPin.Pin1"/> (PC0), <see cref="GpioPin.Pin2"/> (PC1),
/// <see cref="GpioPin.Pin3"/> (PC3), <see cref="GpioPin.Pin6"/> (PA4),
/// <see cref="GpioPin.Pin7"/> (PA6).
/// Pins 4, 5, and 8 have no ADC and return an error.
/// </summary>
public readonly struct AdcReadCommand : IRpcCommand<AdcReadResponse>
{
    /// <param name="pin">ADC-capable pin: <see cref="GpioPin.Pin1"/>, <see cref="GpioPin.Pin2"/>,
    /// <see cref="GpioPin.Pin3"/>, <see cref="GpioPin.Pin6"/>, or <see cref="GpioPin.Pin7"/>.</param>
    public AdcReadCommand(GpioPin pin) => Pin = pin;

    /// <summary>The GPIO pin to read.</summary>
    public GpioPin Pin { get; }

    /// <inheritdoc />
    public string CommandName => "adc_read";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", ((int)Pin).ToString());
    }
}

/// <summary>Response to <see cref="AdcReadCommand"/>.</summary>
public readonly struct AdcReadResponse : IRpcCommandResponse
{
    /// <summary>Raw 12-bit ADC value (0–4095).</summary>
    [JsonPropertyName("raw")]
    public ushort Raw { get; init; }

    /// <summary>Converted voltage in millivolts.</summary>
    [JsonPropertyName("mv")]
    public int Mv { get; init; }
}
