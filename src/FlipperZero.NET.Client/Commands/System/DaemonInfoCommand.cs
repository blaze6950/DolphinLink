using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Queries the daemon's identity and full capability list.
///
/// Use this command for capability negotiation: verify you are talking to the
/// correct FAP (<see cref="DaemonInfoResponse.Name"/> ==
/// <c>"flipper_zero_rpc_daemon"</c>), check the protocol version, and inspect
/// <see cref="DaemonInfoResponse.Commands"/> to determine which commands the
/// running daemon supports before calling them.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"daemon_info"}</code>
///
 /// Wire format (response):
/// <code>
/// {"t":0,"i":N,"p":{
///   "name":"flipper_zero_rpc_daemon",
///   "version":3,
///   "commands":["ping","stream_close",...]}}
/// </code>
///
/// Resources required: none.
/// </summary>
public readonly struct DaemonInfoCommand : IRpcCommand<DaemonInfoResponse>
{
    /// <inheritdoc />
    public string CommandName => "daemon_info";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        // No arguments required.
    }
}

/// <summary>Response to <see cref="DaemonInfoCommand"/>.</summary>
public readonly struct DaemonInfoResponse : IRpcCommandResponse
{
    /// <summary>
    /// Stable daemon identifier string.
    /// Expected value: <c>"flipper_zero_rpc_daemon"</c>.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Monotonically increasing integer protocol version.
    /// The current version is <c>3</c>.
    /// Increment this in the C daemon whenever a breaking wire-format change is made.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// All command names registered in the running daemon's dispatch table.
    /// Use <see cref="Supports"/> to test for individual commands.
    /// </summary>
    [JsonPropertyName("commands")]
    public string[]? Commands { get; init; }

    /// <summary>
    /// Returns <c>true</c> if the daemon supports the specified command name.
    /// </summary>
    /// <param name="commandName">The command name to test (e.g. <c>"ui_draw_str"</c>).</param>
    public bool Supports(string commandName) =>
        Commands is not null &&
        Array.IndexOf(Commands, commandName) >= 0;

    /// <summary>
    /// Returns <c>true</c> if the daemon supports the command represented by
    /// <typeparamref name="TCommand"/>.
    ///
    /// Equivalent to <c>Supports(default(TCommand).CommandName)</c> but
    /// expressed at the type level for use with strongly-typed command structs.
    /// No boxing occurs — <typeparamref name="TCommand"/> is a <c>struct</c>.
    /// </summary>
    /// <typeparam name="TCommand">
    /// A command struct that implements <see cref="IRpcCommandBase"/>.
    /// </typeparam>
    public bool Supports<TCommand>()
        where TCommand : struct, IRpcCommandBase
        => Supports(default(TCommand).CommandName);
}
