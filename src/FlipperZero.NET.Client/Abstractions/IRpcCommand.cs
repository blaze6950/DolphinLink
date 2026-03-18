namespace FlipperZero.NET.Abstractions;

/// <summary>
/// A fire-and-response RPC command.
/// Implementations must be <c>readonly struct</c> — no boxing occurs when passed as a
/// generic type parameter to
/// <see cref="FlipperRpcClient.SendAsync{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">
/// The strongly-typed response produced by the Flipper for this command.
/// </typeparam>
public interface IRpcCommand<TResponse>
    where TResponse : struct, IRpcCommandResponse
{
    /// <summary>
    /// Name of the command as it appears in the JSON <c>"cmd"</c> field,
    /// e.g. <c>"ping"</c>.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Serialise any command-specific arguments into the JSON object body.
    /// Called by the client to build the outgoing message.
    /// Write only the argument fields (no <c>id</c> or <c>cmd</c>) —
    /// those are added by the client.
    /// </summary>
    /// <param name="writer">
    /// A <see cref="System.Text.Json.Utf8JsonWriter"/> positioned inside a JSON object.
    /// </param>
    void WriteArgs(System.Text.Json.Utf8JsonWriter writer);
}
