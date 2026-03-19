using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Ir;

/// <summary>
/// Decoded IR protocols supported by the Flipper Zero IR subsystem.
/// </summary>
public enum IrProtocol
{
    /// <summary>Protocol not recognised by this library version.</summary>
    Unknown = 0,

    /// <summary>NEC protocol (32-bit: 8-bit address, 8-bit ~address, 8-bit command, 8-bit ~command).</summary>
    NEC,

    /// <summary>Extended NEC with 16-bit address.</summary>
    NECext,

    /// <summary>NEC with 42-bit frame (used by some Samsung remotes).</summary>
    NEC42,

    /// <summary>Extended NEC42.</summary>
    NEC42ext,

    /// <summary>Samsung 32-bit protocol.</summary>
    Samsung32,

    /// <summary>RC5 protocol (Philips).</summary>
    RC5,

    /// <summary>RC5X extended protocol.</summary>
    RC5X,

    /// <summary>RC6 protocol (Philips).</summary>
    RC6,

    /// <summary>Sony SIRC 12-bit protocol.</summary>
    SIRC,

    /// <summary>Sony SIRC 15-bit protocol.</summary>
    SIRC15,

    /// <summary>Sony SIRC 20-bit protocol.</summary>
    SIRC20,

    /// <summary>Kaseikyo protocol (Panasonic / JVC family).</summary>
    Kaseikyo,

    /// <summary>RCA protocol.</summary>
    RCA,
}

/// <summary>
/// Converts between the wire string representation of <see cref="IrProtocol"/>
/// (e.g. <c>"NEC"</c>, <c>"Samsung32"</c>) and the enum value.
/// Unknown strings map to <see cref="IrProtocol.Unknown"/>.
/// </summary>
internal sealed class IrProtocolJsonConverter : JsonConverter<IrProtocol>
{
    /// <inheritdoc />
    public override IrProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Enum.TryParse<IrProtocol>(s, ignoreCase: false, out var result) ? result : IrProtocol.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IrProtocol value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
