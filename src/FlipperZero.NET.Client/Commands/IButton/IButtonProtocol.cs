using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.IButton;

/// <summary>
/// iButton / Dallas 1-Wire key protocols supported by the Flipper Zero iButton reader.
/// </summary>
public enum IButtonProtocol
{
    /// <summary>Protocol not recognised by this library version.</summary>
    Unknown = 0,

    /// <summary>DS1990A / DS1990R – raw 64-bit ROM code read (no CRC verification).</summary>
    DS1990Raw,

    /// <summary>DS1992 – 1 Kbit NVRAM key.</summary>
    DS1992,

    /// <summary>DS1996 – 64 Kbit NVRAM key.</summary>
    DS1996,

    /// <summary>Cyfral access control key.</summary>
    Cyfral,

    /// <summary>Metakom access control key.</summary>
    Metakom,
}

/// <summary>
/// Converts between the wire string (e.g. <c>"DS1990Raw"</c>) and <see cref="IButtonProtocol"/>.
/// Unknown strings map to <see cref="IButtonProtocol.Unknown"/>.
/// </summary>
internal sealed class IButtonProtocolJsonConverter : JsonConverter<IButtonProtocol>
{
    /// <inheritdoc />
    public override IButtonProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Enum.TryParse<IButtonProtocol>(s, ignoreCase: false, out var result) ? result : IButtonProtocol.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IButtonProtocol value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
