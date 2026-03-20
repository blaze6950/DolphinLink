using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Thread-safe registry of open RPC streams.
///
/// Centralises all access to the active-stream table so that
/// <see cref="FlipperRpcClient"/> does not scatter ConcurrentDictionary
/// operations throughout its I/O loops.
/// </summary>
internal sealed class RpcStreamManager
{
    private readonly ConcurrentDictionary<uint, StreamState> _streams = new();

    /// <summary>Registers an open stream under the given <paramref name="streamId"/>.</summary>
    public void Register(uint streamId, StreamState state)
        => _streams[streamId] = state;

    /// <summary>
    /// Routes an incoming event to the channel of the stream identified by
    /// <paramref name="streamId"/>.
    ///
    /// Tries a non-blocking <see cref="ChannelWriter{T}.TryWrite"/> first (fast path).
    /// Falls back to a <c>Task.Run</c> + <see cref="ChannelWriter{T}.WriteAsync"/>
    /// only under back-pressure to avoid allocating a <see cref="System.Threading.ThreadPool"/>
    /// work item per event in the common case.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the stream was found (event was dispatched or queued);
    /// <see langword="false"/> if no stream with that id is registered.
    /// </returns>
    public bool TryRouteEvent(uint streamId, JsonElement eventElement)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return false;
        }

        if (!state.EventChannel.Writer.TryWrite(eventElement))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await state.EventChannel.Writer
                        .WriteAsync(eventElement)
                        .ConfigureAwait(false);
                }
                catch { /* channel completed */ }
            });
        }

        return true;
    }

    /// <summary>
    /// Removes the stream identified by <paramref name="streamId"/> and marks
    /// its channel as complete (normal close, no exception).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the stream was found and removed;
    /// <see langword="false"/> if no stream with that id is registered.
    /// </returns>
    public bool TryRemoveAndComplete(uint streamId)
    {
        if (!_streams.TryRemove(streamId, out var state))
        {
            return false;
        }

        state.Complete();
        return true;
    }

    /// <summary>
    /// Faults every active stream with <paramref name="ex"/>, then clears the table.
    /// </summary>
    public void FaultAll(Exception ex)
    {
        foreach (var kv in _streams)
        {
            if (_streams.TryRemove(kv.Key, out var s))
            {
                s.Fault(ex);
            }
        }
    }
}
