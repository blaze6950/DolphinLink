using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Rfid;

/// <summary>
/// LF RFID tag protocols supported by the Flipper Zero RFID reader.
/// </summary>
public enum LfRfidProtocol
{
    /// <summary>Protocol not recognised by this library version.</summary>
    Unknown = 0,

    /// <summary>EM4100 / EM4102 (64-bit read-only proximity tags).</summary>
    EM4100,

    /// <summary>HID Prox (26-bit Wiegand).</summary>
    HIDProx,

    /// <summary>HID Indala (older HID format).</summary>
    Indala26,

    /// <summary>Paradox format.</summary>
    Paradox,

    /// <summary>AWID format.</summary>
    AWID,

    /// <summary>FDX-B (ISO 11784/11785 animal tags).</summary>
    FDX_B,

    /// <summary>Gallagher access control.</summary>
    Gallagher,

    /// <summary>Keri Systems.</summary>
    Keri,

    /// <summary>Motorola format.</summary>
    Motorola,

    /// <summary>Viking format.</summary>
    Viking,

    /// <summary>Visa2000.</summary>
    Visa2000,
}

/// <summary>
/// Converts between the wire string (e.g. <c>"EM4100"</c>) and <see cref="LfRfidProtocol"/>.
/// Unknown strings map to <see cref="LfRfidProtocol.Unknown"/>.
/// </summary>
internal sealed class LfRfidProtocolJsonConverter : JsonConverter<LfRfidProtocol>
{
    /// <inheritdoc />
    public override LfRfidProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Enum.TryParse<LfRfidProtocol>(s, ignoreCase: false, out var result) ? result : LfRfidProtocol.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LfRfidProtocol value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
