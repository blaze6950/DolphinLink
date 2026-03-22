namespace FlipperZero.NET.Abstractions;

/// <summary>
/// Base interface shared by all RPC command types.
/// Provides the command name and argument serialisation, without coupling to
/// the response or event type.
/// </summary>
/// <remarks>
/// Implement this interface indirectly by implementing
/// <see cref="IRpcCommand{TResponse}"/> (for request/response commands) or
/// <see cref="IRpcStreamCommand{TEvent}"/> (for streaming commands).
/// </remarks>
public interface IRpcCommandBase
{
    /// <summary>
    /// Name of the command as it appears in the wire protocol and daemon dispatch table,
    /// e.g. <c>"ping"</c>.
    /// Used for logging and capability negotiation (<see cref="DaemonInfoResponse.Supports"/>).
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Numeric command id used in the V1 wire protocol <c>"c"</c> field.
    /// Matches the zero-based index of the command in the <c>command-registry.json</c>
    /// array, which is also its position in the daemon's COMMAND_NAMES dispatch table.
    /// </summary>
    int CommandId { get; }

    /// <summary>
    /// Serialise any command-specific arguments into the JSON object body.
    /// Called by the client to build the outgoing message.
    /// Write only the argument fields (no <c>"i"</c>, <c>"c"</c>) —
    /// those are added by the client.
    /// </summary>
    /// <param name="writer">
    /// A <see cref="System.Text.Json.Utf8JsonWriter"/> positioned inside a JSON object.
    /// </param>
    void WriteArgs(System.Text.Json.Utf8JsonWriter writer);
}
