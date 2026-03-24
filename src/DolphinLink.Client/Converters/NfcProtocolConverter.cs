using System.Text.Json.Serialization;

namespace DolphinLink.Client.Converters;

/// <summary>
/// Converts between hyphenated wire strings (e.g. <c>"Iso14443-3a"</c>)
/// and <see cref="NfcProtocol"/> enum values.
/// Unknown strings map to <see cref="NfcProtocol.Unknown"/>.
/// </summary>
internal sealed class NfcProtocolJsonConverter : JsonConverter<NfcProtocol>
{
    private static readonly Dictionary<string, NfcProtocol> WireToEnum = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Iso14443-3a"]  = NfcProtocol.Iso14443_3a,
        ["Iso14443-3b"]  = NfcProtocol.Iso14443_3b,
        ["Iso14443-4a"]  = NfcProtocol.Iso14443_4a,
        ["Iso14443-4b"]  = NfcProtocol.Iso14443_4b,
        ["Iso15693-3"]   = NfcProtocol.Iso15693_3,
        ["FeliCa"]       = NfcProtocol.FeliCa,
        ["Mf1S50"]       = NfcProtocol.Mf1S50,
        ["Mf1S70"]       = NfcProtocol.Mf1S70,
        ["MfUltralight"] = NfcProtocol.MfUltralight,
        ["MfDesfire"]    = NfcProtocol.MfDesfire,
        ["Slix"]         = NfcProtocol.Slix,
        ["SlixS"]        = NfcProtocol.SlixS,
        ["SlixL"]        = NfcProtocol.SlixL,
        ["Slix2"]        = NfcProtocol.Slix2,
        ["St25tb"]       = NfcProtocol.St25tb,
        ["St25pc"]       = NfcProtocol.St25pc,
        ["Unknown"]      = NfcProtocol.Unknown,
    };

    private static readonly Dictionary<NfcProtocol, string> EnumToWire =
        WireToEnum.ToDictionary(kv => kv.Value, kv => kv.Key);

    public override NfcProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s is not null && WireToEnum.TryGetValue(s, out var result) ? result : NfcProtocol.Unknown;
    }

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
