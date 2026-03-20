using System.Text.Json.Serialization;

namespace FlipperZero.NET.Commands.Nfc;

/// <summary>
/// NFC tag protocols detectable by the Flipper Zero NFC scanner.
/// </summary>
public enum NfcProtocol
{
    /// <summary>Protocol not recognised by this library version.</summary>
    Unknown = 0,

    /// <summary>ISO 14443-3A (e.g. MIFARE Ultralight, NTAG).</summary>
    Iso14443_3a,

    /// <summary>ISO 14443-3B.</summary>
    Iso14443_3b,

    /// <summary>ISO 14443-4A (e.g. MIFARE DESFire).</summary>
    Iso14443_4a,

    /// <summary>ISO 14443-4B.</summary>
    Iso14443_4b,

    /// <summary>ISO 15693 (vicinity cards).</summary>
    Iso15693,

    /// <summary>FeliCa (Sony).</summary>
    Felica,

    /// <summary>MIFARE Classic (1K / 4K).</summary>
    MfClassic,

    /// <summary>MIFARE Plus.</summary>
    MfPlus,

    /// <summary>MIFARE DESFire (ISO 14443-4A subtype).</summary>
    MfDesfire,

    /// <summary>MIFARE Ultralight / NTAG (ISO 14443-3A subtype).</summary>
    MfUltralight,
}

/// <summary>
/// Converts between the wire string (e.g. <c>"Iso14443-3a"</c>) and <see cref="NfcProtocol"/>.
/// Unknown strings map to <see cref="NfcProtocol.Unknown"/>.
/// </summary>
internal sealed class NfcProtocolJsonConverter : JsonConverter<NfcProtocol>
{
    // The firmware uses hyphenated names (e.g. "Iso14443-3a") that don't map
    // directly to C# enum names, so we do the mapping manually.
    private static readonly Dictionary<string, NfcProtocol> WireToEnum = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Iso14443-3a"] = NfcProtocol.Iso14443_3a,
        ["Iso14443-3b"] = NfcProtocol.Iso14443_3b,
        ["Iso14443-4a"] = NfcProtocol.Iso14443_4a,
        ["Iso14443-4b"] = NfcProtocol.Iso14443_4b,
        ["Iso15693"] = NfcProtocol.Iso15693,
        ["Felica"] = NfcProtocol.Felica,
        ["MfClassic"] = NfcProtocol.MfClassic,
        ["MfPlus"] = NfcProtocol.MfPlus,
        ["MfDesfire"] = NfcProtocol.MfDesfire,
        ["MfUltralight"] = NfcProtocol.MfUltralight,
    };

    private static readonly Dictionary<NfcProtocol, string> EnumToWire =
        WireToEnum.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <inheritdoc />
    public override NfcProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s is not null && WireToEnum.TryGetValue(s, out var result) ? result : NfcProtocol.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, NfcProtocol value, JsonSerializerOptions options)
    {
        if (EnumToWire.TryGetValue(value, out var wire))
        {
            writer.WriteStringValue(wire);
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
