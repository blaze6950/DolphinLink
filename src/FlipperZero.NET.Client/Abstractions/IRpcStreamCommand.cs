namespace FlipperZero.NET.Abstractions;

/// <summary>
/// An RPC command that opens a server-push stream.
/// The initial response carries the <c>"stream"</c> id; subsequent messages
/// carry <c>"event"</c> payloads until the stream is closed.
/// Implementations must be <c>readonly struct</c>.
/// </summary>
/// <typeparam name="TEvent">
/// The strongly-typed event payload pushed by the Flipper for each stream message.
/// </typeparam>
public interface IRpcStreamCommand<TEvent>
    where TEvent : struct, IRpcCommandResponse
{
    /// <summary>Name of the command, e.g. <c>"ble_scan_start"</c>.</summary>
    string CommandName { get; }

    /// <summary>
    /// Serialise any command-specific arguments into the JSON object body.
    /// Same contract as <see cref="IRpcCommand{TResponse}.WriteArgs"/>.
    /// </summary>
    void WriteArgs(System.Text.Json.Utf8JsonWriter writer);
}
