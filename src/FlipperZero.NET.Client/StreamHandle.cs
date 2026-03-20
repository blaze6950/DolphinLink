using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Returned by <see cref="RpcRequestFactory.CreateStreamRequest{TCommand,TEvent}"/>
/// once the Flipper has assigned a stream id to the opened stream.
///
/// The factory resolves the stream id and pre-wires the event channel;
/// <see cref="FlipperRpcClient"/> uses this handle to construct the
/// consumer-facing <see cref="RpcStream{TEvent}"/>.
/// </summary>
internal readonly struct StreamHandle<TEvent>
    where TEvent : struct
{
    /// <summary>The numeric stream id assigned by the Flipper daemon.</summary>
    public uint StreamId { get; init; }

    /// <summary>
    /// Reader side of the event channel.  The factory owns the writer side
    /// (via the registered <see cref="StreamState"/>); the client hands
    /// the reader to <see cref="RpcStream{TEvent}"/>.
    /// </summary>
    public ChannelReader<JsonElement> EventReader { get; init; }
}
