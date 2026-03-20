using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Input;

/// <summary>Physical hardware buttons on the Flipper Zero.</summary>
[JsonConverter(typeof(FlipperInputKeyConverter))]
public enum FlipperInputKey
{
    /// <summary>Up directional button.</summary>
    Up,
    /// <summary>Down directional button.</summary>
    Down,
    /// <summary>Left directional button.</summary>
    Left,
    /// <summary>Right directional button.</summary>
    Right,
    /// <summary>Centre (OK) button.</summary>
    Ok,
    /// <summary>Back button.</summary>
    Back,
}

/// <summary>
/// Custom JSON converter that maps lowercase wire strings
/// (<c>"up"</c>, <c>"ok"</c>, <c>"back"</c>, …) to <see cref="FlipperInputKey"/> values.
/// </summary>
internal sealed class FlipperInputKeyConverter : JsonConverter<FlipperInputKey>
{
    public override FlipperInputKey Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "up" => FlipperInputKey.Up,
            "down" => FlipperInputKey.Down,
            "left" => FlipperInputKey.Left,
            "right" => FlipperInputKey.Right,
            "ok" => FlipperInputKey.Ok,
            "back" => FlipperInputKey.Back,
            var s => throw new JsonException($"Unknown FlipperInputKey value: '{s}'."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        FlipperInputKey value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            FlipperInputKey.Up => "up",
            FlipperInputKey.Down => "down",
            FlipperInputKey.Left => "left",
            FlipperInputKey.Right => "right",
            FlipperInputKey.Ok => "ok",
            FlipperInputKey.Back => "back",
            _ => throw new JsonException($"Unknown FlipperInputKey value: {value}."),
        });
}
