using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Gpio;

/// <summary>
/// GPIO header pin numbers on the Flipper Zero external connector.
/// Pins 1–8 correspond to the physical header positions.
/// </summary>
public enum GpioPin
{
    /// <summary>Header pin 1 (PC0).</summary>
    Pin1 = 1,

    /// <summary>Header pin 2 (PC1).</summary>
    Pin2 = 2,

    /// <summary>Header pin 3 (PC3).</summary>
    Pin3 = 3,

    /// <summary>Header pin 4 (PB2).</summary>
    Pin4 = 4,

    /// <summary>Header pin 5 (PB3).</summary>
    Pin5 = 5,

    /// <summary>Header pin 6 (PA4). ADC-capable.</summary>
    Pin6 = 6,

    /// <summary>Header pin 7 (PA6). ADC-capable.</summary>
    Pin7 = 7,

    /// <summary>Header pin 8 (PA7).</summary>
    Pin8 = 8,
}

/// <summary>
/// Converts between the wire string (e.g. <c>"1"</c> through <c>"8"</c>) and <see cref="GpioPin"/>.
/// </summary>
internal sealed class GpioPinJsonConverter : JsonConverter<GpioPin>
{
    /// <inheritdoc />
    public override GpioPin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is not null && int.TryParse(s, out var n) && n is >= 1 and <= 8)
        {
            return (GpioPin)n;
        }

        throw new JsonException($"Unrecognised GPIO pin value: '{s}'. Expected \"1\"–\"8\".");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, GpioPin value, JsonSerializerOptions options)
        => writer.WriteStringValue(((int)value).ToString());
}
