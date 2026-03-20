using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Thread-safe store of in-flight RPC request callbacks.
///
/// Centralises all access to the pending-request table so that
/// <see cref="FlipperRpcClient"/> does not scatter ConcurrentDictionary
/// operations throughout its I/O loops.
/// </summary>
internal sealed class RpcPendingRequests
{
    private readonly ConcurrentDictionary<uint, PendingRequest> _pending = new();

    /// <summary>Registers a pending request under the given <paramref name="id"/>.</summary>
    public void Register(uint id, PendingRequest request)
        => _pending[id] = request;

    /// <summary>
    /// Stamps the send-time ticks on an already-registered request.
    /// No-op if the id is not found (defensive: race with FaultAll is benign).
    /// </summary>
    public void StampSentTicks(uint id, long ticks)
    {
        if (_pending.TryGetValue(id, out var pr))
        {
            pr.SentTicks = ticks;
        }
    }

    /// <summary>
    /// Removes and returns the pending request for <paramref name="id"/>.
    /// Returns <see langword="false"/> if no request is registered.
    /// </summary>
    public bool TryRemove(uint id, out PendingRequest request)
        => _pending.TryRemove(id, out request!);

    /// <summary>
    /// Fails every registered pending request with <paramref name="errorMessage"/>,
    /// then clears the table.
    /// </summary>
    public void FailAll(string errorMessage)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var p))
            {
                p.OnError(errorMessage);
            }
        }
    }

    /// <summary>
    /// Drains unregistered work items from <paramref name="outbound"/>, registers
    /// them, then immediately fails them with <paramref name="errorMessage"/>.
    ///
    /// Called by <see cref="FlipperRpcClient.FaultAll"/> after sealing the channel
    /// so that tasks enqueued but not yet dequeued by the writer loop are not
    /// silently abandoned.
    /// </summary>
    public void FailOrphans(Channel<RpcWorkItem> outbound, string errorMessage)
    {
        while (outbound.Reader.TryRead(out var orphan))
        {
            orphan.Register();
            if (_pending.TryRemove(orphan.RequestId, out var p))
            {
                p.OnError(errorMessage);
            }
        }
    }

    /// <summary>
    /// Fails every registered pending request, then drains and fails orphan channel
    /// items. Convenience wrapper that calls <see cref="FailAll"/> then
    /// <see cref="FailOrphans"/>.
    /// </summary>
    public void FailAllAndOrphans(Channel<RpcWorkItem> outbound, string errorMessage)
    {
        //todo looks like this method is weird - it does its internal job - FailAll, but also for some reason it knows hot to cleanup the external Channel
        FailAll(errorMessage);
        FailOrphans(outbound, errorMessage);
    }
}
