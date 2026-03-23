using System.Collections.Concurrent;

namespace FlipperZero.NET.Dispatch;

/// <summary>
/// Thread-safe store of in-flight RPC request callbacks.
///
/// Centralises all access to the pending-request table so that
/// <see cref="FlipperRpcClient"/> does not scatter ConcurrentDictionary
/// operations throughout its I/O loops.
/// </summary>
internal sealed class RpcPendingRequests
{
    private readonly ConcurrentDictionary<uint, IPendingRequest> _pending = new();

    /// <summary>Registers a pending request under the given <paramref name="id"/>.</summary>
    public void Register(uint id, IPendingRequest request)
        => _pending[id] = request;

    /// <summary>
    /// Stamps the send-time timestamp on an already-registered request.
    /// No-op if the id is not found (defensive: race with FailAll is benign).
    /// </summary>
    public void StampSentTimestamp(uint id, long timestamp)
    {
        if (_pending.TryGetValue(id, out var pr))
        {
            pr.SentTimestamp = timestamp;
        }
    }

    /// <summary>
    /// Stamps the command name on an already-registered request.
    /// No-op if the id is not found (defensive: race with FailAll is benign).
    /// </summary>
    public void StampCommandName(uint id, string? commandName)
    {
        if (_pending.TryGetValue(id, out var pr))
        {
            pr.CommandName = commandName;
        }
    }

    /// <summary>
    /// Removes and returns the pending request for <paramref name="id"/>.
    /// Returns <see langword="false"/> if no request is registered.
    /// </summary>
    public bool TryRemove(uint id, out IPendingRequest request)
        => _pending.TryRemove(id, out request!);

    /// <summary>
    /// Fails every registered pending request with <paramref name="ex"/>,
    /// then clears the table.
    /// </summary>
    public void FailAll(Exception ex)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var p))
            {
                p.Fail(ex);
            }
        }
    }
}
