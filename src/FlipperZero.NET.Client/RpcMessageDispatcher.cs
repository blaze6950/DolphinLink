using System.Diagnostics;

namespace FlipperZero.NET;

/// <summary>
/// Parses a single inbound NDJSON line and routes it to the correct pending
/// request or open stream.
///
/// Injected dependencies (all <see cref="FlipperRpcClient"/> internals):
/// <list type="bullet">
///   <item><see cref="RpcPendingRequests"/> — resolved request/response callbacks.</item>
///   <item><see cref="RpcStreamManager"/> — active stream event channels.</item>
///   <item><see cref="Stopwatch"/> — monotonic clock for log timestamps and round-trip times.</item>
///   <item><c>onLogEntry</c> — optional log subscriber (invoked synchronously; must not throw).</item>
///   <item><c>onFault</c> — called when a <c>{"disconnect":true}</c> message is received.</item>
/// </list>
/// </summary>
internal sealed class RpcMessageDispatcher
{
    private readonly RpcPendingRequests _pending;
    private readonly RpcStreamManager _streams;
    private readonly Stopwatch _clock;
    private readonly Action<RpcLogEntry>? _onLogEntry;
    private readonly Action<Exception> _onFault;

    public RpcMessageDispatcher(
        RpcPendingRequests pending,
        RpcStreamManager streams,
        Stopwatch clock,
        Action<RpcLogEntry>? onLogEntry,
        Action<Exception> onFault)
    {
        _pending = pending;
        _streams = streams;
        _clock = clock;
        _onLogEntry = onLogEntry;
        _onFault = onFault;
    }

    /// <summary>
    /// Parses <paramref name="line"/> (a single trimmed, non-empty NDJSON line)
    /// and dispatches it to the correct pending request or stream.
    /// </summary>
    public void Dispatch(string line)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(line);
            root = doc.RootElement.Clone();
        }
        catch
        {
            LogErrorWithJson("Malformed JSON received.", line, _clock.ElapsedTicks);
            return;
        }

        var receivedTicks = _clock.ElapsedTicks;

        // Graceful daemon exit: {"disconnect":true}
        if (root.TryGetProperty("disconnect", out _))
        {
            LogErrorWithJson("Daemon disconnected.", line, receivedTicks);
            _onFault(new FlipperRpcException("Daemon disconnected."));
            return;
        }

        // Stream event: {"event":{...},"stream":<id>}
        if (root.TryGetProperty("event", out var eventElement)
            && root.TryGetProperty("stream", out var streamProp)
            && streamProp.TryGetUInt32(out var streamId))
        {
            _onLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.StreamEventReceived,
                StreamId = streamId,
                RawJson = line,
                Elapsed = TimeSpan.FromTicks(receivedTicks),
            });

            _streams.TryRouteEvent(streamId, eventElement);
            return;
        }

        // Request/response: must have "id"
        if (!root.TryGetProperty("id", out var idProp)
            || !idProp.TryGetUInt32(out var requestId))
        {
            return;
        }

        if (!_pending.TryRemove(requestId, out var pending))
        {
            return; // No one waiting — ignore
        }

        // Compute round-trip time
        TimeSpan? roundTrip = null;
        if (pending.SentTicks > 0)
        {
            roundTrip = TimeSpan.FromTicks(receivedTicks - pending.SentTicks);
        }

        string? status;
        bool isError = root.TryGetProperty("error", out var errorProp);
        if (isError)
        {
            status = errorProp.GetString() ?? "unknown_error";
        }
        else
        {
            // Detect stream-open response vs plain ok
            status = root.TryGetProperty("stream", out _) ? "stream_opened" : "ok";
        }

        _onLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.ResponseReceived,
            RequestId = requestId,
            Status = status,
            RawJson = line,
            Elapsed = TimeSpan.FromTicks(receivedTicks),
            RoundTrip = roundTrip,
        });

        if (isError)
        {
            pending.OnError(status);
        }
        else
        {
            pending.OnSuccess(root);
        }
    }

    /// <summary>
    /// Emits an <see cref="RpcLogKind.Error"/> entry that includes the raw JSON
    /// line.  The two call sites (malformed JSON, daemon disconnect) emit the
    /// same shape.
    /// </summary>
    private void LogErrorWithJson(string status, string rawJson, long ticks) =>
        _onLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.Error,
            Status = status,
            RawJson = rawJson,
            Elapsed = TimeSpan.FromTicks(ticks),
        });
}
