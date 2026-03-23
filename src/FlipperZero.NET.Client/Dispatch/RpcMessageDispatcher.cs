using System.Diagnostics;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Exceptions;
using FlipperZero.NET.Streaming;

namespace FlipperZero.NET.Dispatch;

/// <summary>
/// Routes a single inbound V3 envelope to the correct pending request or open stream.
///
/// Injected dependencies (all <see cref="FlipperRpcClient"/> internals):
/// <list type="bullet">
///   <item><see cref="RpcPendingRequests"/> — resolved request/response callbacks.</item>
///   <item><see cref="RpcStreamManager"/> — active stream event channels.</item>
///   <item><see cref="IRpcDiagnostics"/> — log sink; defaults to <see cref="NullDiagnostics"/> (no-op).</item>
/// </list>
///
/// The <see cref="RpcMessageType.Disconnect"/> case is intentionally NOT handled here.
/// It is handled directly in the reader loop of <see cref="FlipperRpcClient"/> so that
/// the disconnect path is collocated with all other transport-level faulting logic.
/// </summary>
internal sealed class RpcMessageDispatcher
{
    /// <summary>
    /// No-op <see cref="IRpcDiagnostics"/> singleton used when no diagnostics sink is supplied.
    /// Allows the dispatcher to call <c>_diagnostics.Log</c> unconditionally; the JIT can
    /// inline and eliminate the call entirely.
    /// </summary>
    private sealed class NullDiagnostics : IRpcDiagnostics
    {
        public static readonly NullDiagnostics Instance = new();
        private NullDiagnostics() { }
        public void Log(RpcLogEntry entry) { }
    }

    /// <summary>
    /// The no-op <see cref="IRpcDiagnostics"/> singleton, exposed so that
    /// <see cref="FlipperRpcClient"/> can use the same instance as its own
    /// <c>_diagnostics</c> field default without duplicating the type.
    /// </summary>
    internal static IRpcDiagnostics NullDiagnosticsInstance => NullDiagnostics.Instance;

    private readonly RpcPendingRequests _pending;
    private readonly RpcStreamManager _streams;
    private readonly IRpcDiagnostics _diagnostics;

    public RpcMessageDispatcher(
        RpcPendingRequests pending,
        RpcStreamManager streams,
        IRpcDiagnostics? diagnostics = null)
    {
        _pending = pending;
        _streams = streams;
        _diagnostics = diagnostics ?? NullDiagnostics.Instance;
    }

    /// <summary>
    /// Dispatches a pre-parsed V3 envelope to the correct pending request or stream.
    /// <see cref="RpcMessageType.Disconnect"/> is ignored — callers handle it externally.
    /// </summary>
    /// <param name="receivedTimestamp">
    /// Absolute timestamp captured via <see cref="Stopwatch.GetTimestamp"/> in the
    /// reader loop at the moment the line was received.  Used together with
    /// <see cref="IPendingRequest.SentTimestamp"/> to compute round-trip time via
    /// <see cref="Stopwatch.GetElapsedTime"/>.
    /// </param>
    public void Dispatch(RpcEnvelope envelope, string rawLine, long receivedTimestamp)
    {
        switch (envelope.Type)
        {
            case RpcMessageType.Event:
                DispatchEvent(envelope, rawLine);
                return;

            case RpcMessageType.Response:
                DispatchResponse(envelope, rawLine, receivedTimestamp);
                return;

            case RpcMessageType.Disconnect:
                // Handled by the reader loop; dispatcher ignores it.
                return;

            case RpcMessageType.Unknown:
            default:
                _diagnostics.Log(new RpcLogEntry
                {
                    Source = RpcLogSource.Client,
                    Kind = RpcLogKind.Error,
                    Status = "Malformed JSON received.",
                    RawJson = rawLine,
                });
                return;
        }
    }

    // -------------------------------------------------------------------------
    // Event dispatch
    // -------------------------------------------------------------------------

    private void DispatchEvent(RpcEnvelope envelope, string rawLine)
    {
        if (envelope.Id is not { } streamId)
        {
            return;
        }

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.StreamEventReceived,
            StreamId = streamId,
            CommandName = _streams.TryGetCommandName(streamId, out var evtCmdName) ? evtCmdName : null,
            RawJson = rawLine,
        });

        _streams.TryRouteEvent(streamId, envelope.Payload);
    }

    // -------------------------------------------------------------------------
    // Response dispatch
    // -------------------------------------------------------------------------

    private void DispatchResponse(RpcEnvelope envelope, string rawLine, long receivedTimestamp)
    {
        if (envelope.Id is not { } requestId)
        {
            return;
        }

        if (!_pending.TryRemove(requestId, out var pending))
        {
            return; // No one waiting — ignore
        }

        // Compute round-trip time using Stopwatch.GetElapsedTime so the tick
        // units are handled correctly on all platforms.
        TimeSpan? roundTrip = null;
        if (pending.SentTimestamp > 0)
        {
            roundTrip = Stopwatch.GetElapsedTime(pending.SentTimestamp, receivedTimestamp);
        }

        var status = envelope.Error ?? "ok";

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.ResponseReceived,
            RequestId = requestId,
            CommandName = pending.CommandName,
            Status = status,
            RawJson = rawLine,
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
}
