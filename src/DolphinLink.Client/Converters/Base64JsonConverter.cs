using System.Text.Json.Serialization;

namespace DolphinLink.Client.Converters;

/// <summary>
/// Converts between a JSON Base64 string and a <c>byte[]</c>.
/// On read, calls <see cref="Convert.FromBase64String"/>.
/// On write, calls <see cref="Convert.ToBase64String"/>.
/// </summary>
internal sealed class Base64JsonConverter : JsonConverter<byte[]?>
{
    /// <inheritdoc />
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var s = reader.GetString();
        return s is null ? null : Convert.FromBase64String(s);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, byte[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(Convert.ToBase64String(value));
        }
    }
}
