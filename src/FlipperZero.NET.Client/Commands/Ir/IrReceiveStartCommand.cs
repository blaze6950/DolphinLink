using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ir;

/// <summary>
/// Opens an IR receive stream.  The Flipper's IR receiver decodes incoming signals
/// and delivers each one as an <see cref="IrReceiveEvent"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ir_receive_start"}</code>
///
/// Wire format (stream open response):
/// <code>{"t":0,"i":N,"p":{"stream":M}}</code>
///
/// Wire format (stream event):
/// <code>{"t":1,"i":M,"p":{"protocol":"NEC","address":32,"command":11,"repeat":false}}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Requires the IR hardware resource.  Dispose the returned
/// <see cref="RpcStream{TEvent}"/> to stop receiving and release the IR hardware.
/// </summary>
public readonly struct IrReceiveStartCommand : IRpcStreamCommand<IrReceiveEvent>
{
    /// <inheritdoc />
    public string CommandName => "ir_receive_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>A decoded IR signal received from the Flipper's IR receiver.</summary>
public readonly struct IrReceiveEvent : IRpcCommandResponse
{
    /// <summary>Decoded IR protocol.</summary>
    [JsonPropertyName("protocol")]
    [JsonConverter(typeof(IrProtocolJsonConverter))]
    public IrProtocol Protocol { get; init; }

    /// <summary>Device address field from the decoded IR frame.</summary>
    [JsonPropertyName("address")]
    public uint Address { get; init; }

    /// <summary>Command field from the decoded IR frame.</summary>
    [JsonPropertyName("command")]
    public uint Command { get; init; }

    /// <summary><c>true</c> if this is a repeat frame (button held down).</summary>
    [JsonPropertyName("repeat")]
    public bool Repeat { get; init; }
}
