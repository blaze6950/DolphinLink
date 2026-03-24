using System.Collections.Concurrent;

namespace DolphinLink.Client.Streaming;

/// <summary>
/// Thread-safe registry of open RPC streams.
///
/// Centralises all access to the active-stream table so that
/// <see cref="RpcClient"/> does not scatter ConcurrentDictionary
/// operations throughout its I/O loops.
/// </summary>
internal sealed class RpcStreamManager
{
    private readonly ConcurrentDictionary<uint, StreamState> _streams = new();
    private readonly ConcurrentDictionary<uint, string> _commandNames = new();

    /// <summary>Registers an open stream under the given <paramref name="streamId"/>.</summary>
    public void Register(uint streamId, StreamState state)
        => _streams[streamId] = state;

    /// <summary>
    /// Creates a new <see cref="RpcStream{TEvent}"/>, registers it in the stream table,
    /// and wires the <see cref="RpcStream{TEvent}.Closed"/> callback.
    /// </summary>
    /// <typeparam name="TEvent">The event type the stream emits.</typeparam>
    /// <param name="streamId">The stream id assigned by the Flipper.</param>
    /// <param name="disconnectToken">
    /// Cancelled when the connection is lost; passed through to
    /// <see cref="RpcStream{TEvent}"/> so enumeration exits promptly.
    /// </param>
    public RpcStream<TEvent> CreateStream<TEvent>(
        uint streamId,
        CancellationToken disconnectToken)
        where TEvent : struct
    {
        var state = new StreamState();
        _streams[streamId] = state;
        var stream = new RpcStream<TEvent>(streamId, state.Reader, disconnectToken);
        // Wire the stream's Closed callback to remove and complete the stream state.
        stream.Closed += sId => Task.FromResult(TryRemoveAndComplete(sId));
        return stream;
    }

    /// <summary>
    /// Routes an incoming event to the channel of the stream identified by
    /// <paramref name="streamId"/>.
    ///
    /// Because <see cref="StreamState"/> uses an unbounded channel with
    /// <c>SingleWriter = true</c>, <c>TryWrite</c> always succeeds and never
    /// needs a <c>Task.Run</c> fallback.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the stream was found (event was written);
    /// <see langword="false"/> if no stream with that id is registered.
    /// </returns>
    public bool TryRouteEvent(uint streamId, JsonElement eventElement)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return false;
        }

        state.Writer.TryWrite(eventElement);
        return true;
    }

    /// <summary>
    /// Records the command name associated with the stream identified by
    /// <paramref name="streamId"/>.  Called by the writer loop after
    /// <see cref="CreateStream{TEvent}"/> so the dispatcher can populate
    /// <see cref="RpcLogEntry.CommandName"/> on stream-event log entries.
    /// </summary>
    public void StampCommandName(uint streamId, string? commandName)
    {
        if (commandName is not null)
        {
            _commandNames[streamId] = commandName;
        }
    }

    /// <summary>
    /// Returns the command name associated with <paramref name="streamId"/>, if any.
    /// </summary>
    public bool TryGetCommandName(uint streamId, out string? commandName)
    {
        if (_commandNames.TryGetValue(streamId, out var name))
        {
            commandName = name;
            return true;
        }
        commandName = null;
        return false;
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
        _commandNames.TryRemove(streamId, out _);
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
