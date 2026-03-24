namespace DolphinLink.Client.Abstractions;

/// <summary>
/// An RPC command that opens a server-push stream.
/// The initial response carries the stream id in the <c>"p"</c> payload field;
/// subsequent messages carry event payloads in <c>"p"</c> until the stream is closed.
/// Implementations must be <c>readonly struct</c>.
/// </summary>
/// <typeparam name="TEvent">
/// The strongly-typed event payload pushed by the Flipper for each stream message.
/// </typeparam>
public interface IRpcStreamCommand<TEvent> : IRpcCommandBase
    where TEvent : struct, IRpcCommandResponse
{
}
