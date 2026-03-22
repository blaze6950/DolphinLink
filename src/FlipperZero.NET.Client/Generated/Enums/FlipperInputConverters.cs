using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlipperZero.NET;

/// <summary>
/// Converts between lowercase wire strings (<c>"up"</c>, <c>"ok"</c>, <c>"back"</c>, …)
/// and <see cref="FlipperInputKey"/> enum values.
/// </summary>
internal sealed class FlipperInputKeyJsonConverter : JsonConverter<FlipperInputKey>
{
    public override FlipperInputKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "up"    => FlipperInputKey.Up,
            "down"  => FlipperInputKey.Down,
            "right" => FlipperInputKey.Right,
            "left"  => FlipperInputKey.Left,
            "ok"    => FlipperInputKey.Ok,
            "back"  => FlipperInputKey.Back,
            var s   => throw new JsonException($"Unknown FlipperInputKey value: '{s}'."),
        };

    public override void Write(Utf8JsonWriter writer, FlipperInputKey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            FlipperInputKey.Up    => "up",
            FlipperInputKey.Down  => "down",
            FlipperInputKey.Right => "right",
            FlipperInputKey.Left  => "left",
            FlipperInputKey.Ok    => "ok",
            FlipperInputKey.Back  => "back",
            _                     => throw new JsonException($"Unknown FlipperInputKey value: {value}."),
        });
}

/// <summary>
/// Converts between lowercase wire strings (<c>"press"</c>, <c>"short"</c>, <c>"long"</c>, …)
/// and <see cref="FlipperInputType"/> enum values.
/// </summary>
internal sealed class FlipperInputTypeJsonConverter : JsonConverter<FlipperInputType>
{
    public override FlipperInputType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "press"   => FlipperInputType.Press,
            "release" => FlipperInputType.Release,
            "short"   => FlipperInputType.Short,
            "long"    => FlipperInputType.Long,
            "repeat"  => FlipperInputType.Repeat,
            var s     => throw new JsonException($"Unknown FlipperInputType value: '{s}'."),
        };

    public override void Write(Utf8JsonWriter writer, FlipperInputType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            FlipperInputType.Press   => "press",
            FlipperInputType.Release => "release",
            FlipperInputType.Short   => "short",
            FlipperInputType.Long    => "long",
            FlipperInputType.Repeat  => "repeat",
            _                        => throw new JsonException($"Unknown FlipperInputType value: {value}."),
        });
}
