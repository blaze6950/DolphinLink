using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>Response to <see cref="DaemonInfoCommand"/>.</summary>
public readonly partial struct DaemonInfoResponse : IRpcCommandResponse
{
    /// <summary>
    /// Stable daemon identifier string.
    /// Expected value: <c>"flipper_zero_rpc_daemon"</c>.
    /// </summary>
    [JsonPropertyName("n")]
    public string? Name { get; init; }

    /// <summary>
    /// Monotonically increasing integer protocol version.
    /// The current version is <c>5</c>.
    /// Increment this in the C daemon whenever a breaking wire-format change is made.
    /// </summary>
    [JsonPropertyName("v")]
    public uint Version { get; init; }

    /// <summary>
    /// All command names registered in the running daemon's dispatch table.
    /// Use <see cref="Supports(string)"/> to test for individual commands.
    /// </summary>
    [JsonPropertyName("cmds")]
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
