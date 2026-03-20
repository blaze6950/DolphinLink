using System.Diagnostics;

namespace FlipperZero.NET;

/// <summary>
/// Parses a single inbound V2 NDJSON line and routes it to the correct pending
/// request or open stream.
///
/// Injected dependencies (all <see cref="FlipperRpcClient"/> internals):
/// <list type="bullet">
///   <item><see cref="RpcPendingRequests"/> — resolved request/response callbacks.</item>
///   <item><see cref="RpcStreamManager"/> — active stream event channels.</item>
///   <item><see cref="Stopwatch"/> — monotonic clock for log timestamps and round-trip times.</item>
///   <item><c>onLogEntry</c> — optional log subscriber (invoked synchronously; must not throw).</item>
///   <item><c>onFault</c> — called when a <c>{"type":"disconnect"}</c> message is received.</item>
/// </list>
/// </summary>
internal sealed class RpcMessageDispatcher
{
    private readonly RpcPendingRequests _pending;
    private readonly RpcStreamManager _streams;

    private readonly Stopwatch _clock;

    // todo revise the need of these two action fields.
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
        var receivedTicks = _clock.ElapsedTicks;
        var envelope = RpcEnvelope.Parse(line);

        switch (envelope.Type)
        {
            case RpcMessageType.Disconnect:
                LogErrorWithJson("Daemon disconnected.", line, receivedTicks);
                _onFault(new FlipperRpcException("Daemon disconnected."));
                return;

            case RpcMessageType.Event:
                DispatchEvent(envelope, line, receivedTicks);
                return;

            case RpcMessageType.Response:
                DispatchResponse(envelope, line, receivedTicks);
                return;

            case RpcMessageType.Unknown:
            default:
                LogErrorWithJson("Malformed JSON received.", line, receivedTicks);
                return;
        }
    }

    // -------------------------------------------------------------------------
    // Event dispatch
    // -------------------------------------------------------------------------

    private void DispatchEvent(RpcEnvelope envelope, string line, long receivedTicks)
    {
        if (envelope.Id is not { } streamId)
        {
            return;
        }

        _onLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.StreamEventReceived,
            StreamId = streamId,
            RawJson = line,
            Elapsed = TimeSpan.FromTicks(receivedTicks),
        });

        _streams.TryRouteEvent(streamId, envelope.Payload);
    }

    // -------------------------------------------------------------------------
    // Response dispatch
    // -------------------------------------------------------------------------

    private void DispatchResponse(RpcEnvelope envelope, string line, long receivedTicks)
    {
        if (envelope.Id is not { } requestId)
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

        var status = envelope.Error ?? "ok";

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

        if (envelope.Error is { } errorCode)
        {
            pending.Fail(new FlipperRpcException(requestId, errorCode));
        }
        else
        {
            pending.Complete(envelope.Payload);
        }
    }

    // -------------------------------------------------------------------------
    // Logging helpers
    // -------------------------------------------------------------------------

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