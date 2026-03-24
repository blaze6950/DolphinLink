using System.Text.Json.Serialization;

namespace DolphinLink.Client.Converters;

/// <summary>
/// Reads and writes booleans as <c>1</c>/<c>0</c> integers on the wire (V1 format).
/// Reads <c>true</c>/<c>false</c> as well for compatibility.
/// </summary>
internal sealed class NumericBoolJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt32() != 0,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to bool.")
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value ? 1 : 0);
}
