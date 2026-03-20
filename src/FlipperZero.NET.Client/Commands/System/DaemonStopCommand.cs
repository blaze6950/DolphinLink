using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Requests the RPC daemon to stop gracefully. The daemon sends this OK
/// response, then stops its event loop, which triggers the full teardown
/// sequence (closes all streams, releases hardware resources, sends a
/// <c>{"disconnect":true}</c> notification, and restores the USB config).
/// </summary>
public readonly struct DaemonStopCommand : IRpcCommand<DaemonStopResponse>
{
    public string CommandName => "daemon_stop";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response envelope for <see cref="DaemonStopCommand"/>.</summary>
public readonly struct DaemonStopResponse : IRpcCommandResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
