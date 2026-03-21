using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Core;

/// <summary>
/// Sends a <c>ping</c> command to the Flipper and waits for a <see cref="PingResponse"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ping"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"pong":true}}</code>
/// </summary>
public readonly struct PingCommand : IRpcCommand<PingResponse>
{
    /// <inheritdoc />
    public string CommandName => "ping";

    /// <summary>Ping carries no arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="PingCommand"/>.</summary>
public readonly struct PingResponse : IRpcCommandResponse
{
    /// <summary>Always <c>true</c> when the Flipper acknowledges the ping.</summary>
    [JsonPropertyName("pong")]
    public bool Pong { get; init; }
}
