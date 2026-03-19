using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Converters;

namespace FlipperZero.NET.Commands.Rfid;

/// <summary>
/// Opens a streaming LF RFID read session.  Each detected tag is delivered
/// as an <see cref="LfRfidReadEvent"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"lfrfid_read_start"}</code>
///
/// Wire format (stream open response):
/// <code>{"id":N,"stream":M}</code>
///
/// Wire format (stream event):
/// <code>{"event":{"type":"EM4100","data":"AABBCCDDEE"},"stream":M}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Requires the RFID hardware resource.  Dispose the returned
/// <see cref="RpcStream{TEvent}"/> to stop reading and release the RFID hardware.
/// </summary>
public readonly struct LfRfidReadStartCommand : IRpcStreamCommand<LfRfidReadEvent>
{
    /// <inheritdoc />
    public string CommandName => "lfrfid_read_start";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An LF RFID tag read event.</summary>
public readonly struct LfRfidReadEvent : IRpcCommandResponse
{
    /// <summary>Protocol type.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(LfRfidProtocolJsonConverter))]
    public LfRfidProtocol Type { get; init; }

    /// <summary>Raw tag data bytes (decoded from the uppercase hex wire representation).</summary>
    [JsonPropertyName("data")]
    [JsonConverter(typeof(HexJsonConverter))]
    public byte[]? Data { get; init; }
}
