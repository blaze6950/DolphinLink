using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Input;

/// <summary>Hardware button event types on the Flipper Zero.</summary>
[JsonConverter(typeof(FlipperInputTypeConverter))]
public enum FlipperInputType
{
    /// <summary>Button was physically pressed down.</summary>
    Press,
    /// <summary>Button was released.</summary>
    Release,
    /// <summary>Button was pressed and released quickly.</summary>
    Short,
    /// <summary>Button was held for a long press.</summary>
    Long,
    /// <summary>Button is being held and repeating.</summary>
    Repeat,
}

/// <summary>
/// Custom JSON converter that maps lowercase wire strings
/// (<c>"press"</c>, <c>"short"</c>, <c>"long"</c>, …) to <see cref="FlipperInputType"/> values.
/// </summary>
internal sealed class FlipperInputTypeConverter : JsonConverter<FlipperInputType>
{
    public override FlipperInputType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "press" => FlipperInputType.Press,
            "release" => FlipperInputType.Release,
            "short" => FlipperInputType.Short,
            "long" => FlipperInputType.Long,
            "repeat" => FlipperInputType.Repeat,
            var s => throw new JsonException($"Unknown FlipperInputType value: '{s}'."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        FlipperInputType value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            FlipperInputType.Press => "press",
            FlipperInputType.Release => "release",
            FlipperInputType.Short => "short",
            FlipperInputType.Long => "long",
            FlipperInputType.Repeat => "repeat",
            _ => throw new JsonException($"Unknown FlipperInputType value: {value}."),
        });
}
