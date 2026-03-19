using System.Text.Json.Serialization;

namespace FlipperZero.NET.Converters;

/// <summary>
/// Converts between an uppercase hex JSON string and a <c>byte[]</c>.
/// On read, calls <see cref="Convert.FromHexString"/>.
/// On write, calls <see cref="Convert.ToHexString"/> (uppercase).
/// </summary>
internal sealed class HexJsonConverter : JsonConverter<byte[]?>
{
    /// <inheritdoc />
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var s = reader.GetString();
        return s is null ? null : Convert.FromHexString(s);
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
            writer.WriteStringValue(Convert.ToHexString(value));
        }
    }
}
