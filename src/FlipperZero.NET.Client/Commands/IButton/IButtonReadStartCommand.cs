using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Converters;

namespace FlipperZero.NET.Commands.IButton;

/// <summary>
/// Opens a streaming iButton read session.  Each detected key is delivered
/// as an <see cref="IButtonReadEvent"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ibutton_read_start"}</code>
///
/// Wire format (stream open response):
/// <code>{"t":0,"i":N,"p":{"stream":M}}</code>
///
/// Wire format (stream event):
/// <code>{"t":1,"i":M,"p":{"type":"DS1990Raw","data":"0102030405060708"}}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Requires the iButton hardware resource.  Dispose the returned
/// <see cref="RpcStream{TEvent}"/> to stop reading and release the iButton hardware.
/// </summary>
public readonly struct IButtonReadStartCommand : IRpcStreamCommand<IButtonReadEvent>
{
    /// <inheritdoc />
    public string CommandName => "ibutton_read_start";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An iButton key read event.</summary>
public readonly struct IButtonReadEvent : IRpcCommandResponse
{
    /// <summary>Protocol/key type.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(IButtonProtocolJsonConverter))]
    public IButtonProtocol Type { get; init; }

    /// <summary>Raw key data bytes (decoded from the uppercase hex wire representation).</summary>
    [JsonPropertyName("data")]
    [JsonConverter(typeof(HexJsonConverter))]
    public byte[]? Data { get; init; }
}
