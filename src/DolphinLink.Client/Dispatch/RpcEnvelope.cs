using System.Text.Json.Serialization;

namespace DolphinLink.Client.Dispatch;

/// <summary>
/// The three V3 message types the daemon can send.
/// </summary>
internal enum RpcMessageType
{
    Response = 0,
    Event = 1,
    Disconnect = 2,
    Unknown = 255,
}

/// <summary>
/// Parsed representation of one V3 NDJSON envelope received from the daemon.
///
/// Wire formats (compact single-letter keys, numeric type discriminator):
/// <code>
/// {"t":0,"i":1,"p":{...}}   — success with data
/// {"t":0,"i":1}             — success, void
/// {"t":0,"i":1,"e":"..."}   — error
/// {"t":1,"i":7,"p":{...}}   — stream event (i = stream id)
/// {"t":2}                   — graceful daemon exit
/// </code>
///
/// The <c>"t"</c> field is deserialized directly into <see cref="Type"/> as an integer-backed
/// <see cref="RpcMessageType"/> — no runtime switch required.  Any unrecognised integer value
/// deserializes to the corresponding unnamed enum value; the dispatcher's <c>default:</c> arm
/// treats it as <see cref="RpcMessageType.Unknown"/>.
///
/// Payload field values ("p" contents) retain full readable names.
/// </summary>
internal readonly struct RpcEnvelope
{
    [JsonPropertyName("t")] public RpcMessageType Type { get; init; }
    [JsonPropertyName("i")] public uint? Id { get; init; }
    [JsonPropertyName("p")] public JsonElement Payload { get; init; }
    [JsonPropertyName("e")] public string? Error { get; init; }

    /// <summary>
    /// Parses a single V3 NDJSON line into an <see cref="RpcEnvelope"/>.
    /// Returns an envelope with <see cref="Type"/> == <see cref="RpcMessageType.Unknown"/>
    /// on malformed input.
    /// </summary>
    public static RpcEnvelope Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<RpcEnvelope>(line);
        }
        catch
        {
            return new RpcEnvelope { Type = RpcMessageType.Unknown };
        }
    }
}
