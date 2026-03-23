using System.Diagnostics;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Dispatch;
using FlipperZero.NET.Exceptions;
using FlipperZero.NET.Streaming;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET;

/// <summary>
/// Core Flipper RPC client.
///
/// Architecture
/// ============
///
/// The default transport chain wraps the supplied transport in two decorator layers:
///
/// <code>
///   SerialPortTransport          (raw USB-CDC)
///       ↑
///   PacketSerializationTransport (single-writer serialiser — omitted when DisablePacketSerialization)
///       ↑
///   HeartbeatTransport           (keep-alive, disconnect detection — omitted when DisableHeartbeat)
///       ↑
///   FlipperRpcClient             (this class — RPC logic only)
/// </code>
///
/// Both decorator layers are optional and can be disabled via
/// <see cref="FlipperRpcClientOptions.DisablePacketSerialization"/> and
/// <see cref="FlipperRpcClientOptions.DisableHeartbeat"/>.  This is useful in
/// single-threaded environments such as Blazor WASM, where each extra
/// <c>Task.Run</c> loop competes with the WebSerial JS read pump on the
/// cooperative scheduler.
///
/// There is no outbound channel or writer loop in this class.
/// <see cref="PacketSerializationTransport"/> (when present) provides the
/// single-writer guarantee; <see cref="SendAsync{TCommand,TResponse}"/> calls
/// <c>_transport.SendAsync()</c> directly.
///
/// Request/response
/// ----------------
/// <see cref="SendAsync{TCommand,TResponse}"/> creates a typed
/// <see cref="PendingRequest{TResponse}"/> and registers it in the pending
/// table BEFORE calling <c>_transport.SendAsync()</c>, guaranteeing the
/// reader loop can never receive a response before registration.
///
/// Streams
/// -------
/// <see cref="SendStreamAsync{TCommand,TEvent}"/> sends the command, awaits
/// the <see cref="StreamOpenResult"/> to get the assigned stream id, then
/// registers the stream in <see cref="RpcStreamManager"/> before returning
/// the <see cref="RpcStream{TEvent}"/> to the caller.
///
/// Heartbeat / keep-alive
/// ----------------------
/// When enabled (the default), handled entirely by <see cref="HeartbeatTransport"/>.
/// This class has no knowledge of heartbeat timing, watchdog logic, or keep-alive
/// frames.  When the transport detects a silent disconnect it raises its
/// <see cref="HeartbeatTransport.Disconnected"/> event, which triggers
/// <see cref="FaultAll"/>.  When heartbeat is disabled, the reader loop filters
/// keep-alive frames (bare newlines) from the daemon before parsing.
///
/// Logging
/// -------
/// Pass an <see cref="IRpcDiagnostics"/> implementation to the
/// <see cref="FlipperRpcClient(IFlipperTransport,FlipperRpcClientOptions,IRpcDiagnostics)"/>
/// constructor to receive <see cref="RpcLogEntry"/> records for every command sent and
/// response received.  The implementation is called synchronously on the
/// client's I/O loop threads and must return quickly and must not throw.
/// If no implementation is provided, logging is a no-op (eliminated by the JIT).
/// </summary>
public sealed class FlipperRpcClient : IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly IFlipperTransport _transport;
    private readonly HeartbeatTransport? _heartbeat;

    /// <summary>
    /// Connection options passed at construction time.
    /// Stored so <see cref="NegotiateAsync"/> can propagate heartbeat timing
    /// to the daemon via the <c>configure</c> command.
    /// </summary>
    private readonly FlipperRpcClientOptions _options;

    /// <summary>
    /// The <see cref="DaemonInfoResponse"/> returned by the daemon during
    /// <see cref="ConnectAsync"/>.  <c>null</c> before <see cref="ConnectAsync"/>
    /// has completed successfully.
    /// </summary>
    public DaemonInfoResponse? DaemonInfo { get; private set; }

    /// <summary>Pending request-id → pending request (for request/response commands).</summary>
    private readonly RpcPendingRequests _pending = new();

    /// <summary>Active stream-id → stream state (for streaming commands).</summary>
    private readonly RpcStreamManager _streams = new();

    /// <summary>Parses and routes inbound V3 NDJSON envelopes.</summary>
    private readonly RpcMessageDispatcher _dispatcher;

    /// <summary>
    /// Monotonically increasing counter for request ids.
    /// Wraps to 0 at <see cref="uint.MaxValue"/> (~4.3 billion); this is safe because
    /// request timeouts guarantee no in-flight request survives long enough
    /// to collide with a recycled id.
    /// </summary>
    private uint _nextId;

    private Task? _readerTask;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 0 = not yet faulted; 1 = FaultAll has run.
    /// Guards the teardown so it fires exactly once, even when multiple paths
    /// (reader EOF, writer error, heartbeat timeout, DisposeAsync) race.
    /// </summary>
    private int _faulted;

    /// <summary>
    /// The exception that caused the fault, stored so pre-send guards can
    /// rethrow the exact same <see cref="FlipperDisconnectedException"/> (with the
    /// correct <see cref="DisconnectReason"/>) that pending callers already received.
    /// Written once by <see cref="FaultAll"/> under the interlocked guard;
    /// read by <see cref="SendAsync{TCommand,TResponse}"/> and
    /// <see cref="SendStreamAsync{TCommand,TEvent}"/> after the guard fires.
    /// </summary>
    private volatile FlipperDisconnectedException? _faultException;

    /// <summary>
    /// 0 = live; 1 = <see cref="DisposeAsync"/> has been called.
    /// Checked by <see cref="SendAsync{TCommand,TResponse}"/>,
    /// <see cref="SendStreamAsync{TCommand,TEvent}"/>, and
    /// <see cref="ConnectAsync"/> to prevent use-after-dispose.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Cancelled when the connection to the Flipper is lost (transport EOF,
    /// read error, heartbeat timeout, or explicit <see cref="DisposeAsync"/>).
    /// Consumers can pass <see cref="Disconnected"/> to <c>await foreach</c>
    /// or <c>Task.WhenAny</c> to react immediately when the device disappears.
    /// </summary>
    private readonly CancellationTokenSource _disconnectCts = new();

    /// <summary>
    /// A <see cref="CancellationToken"/> that is cancelled the moment the
    /// connection to the Flipper is lost.
    /// </summary>
    public CancellationToken Disconnected => _disconnectCts.Token;

    /// <summary>
    /// Diagnostics sink. Never null — defaults to the no-op singleton inside
    /// <see cref="RpcMessageDispatcher"/>, but stored here for the two direct
    /// call sites in <see cref="SendAsync{TCommand,TResponse}"/> and
    /// <see cref="SendStreamAsync{TCommand,TEvent}"/>.
    /// </summary>
    private readonly IRpcDiagnostics _diagnostics;

    // -------------------------------------------------------------------------
    // Construction / lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a client using the supplied transport.
    /// Use this to inject any transport implementation (USB-CDC via
    /// <see cref="SerialPortTransport"/>, BLE, Wi-Fi, WASM/WebSerial bridge,
    /// or an in-process fake for unit tests).
    ///
    /// The transport is automatically wrapped in a
    /// <see cref="PacketSerializationTransport"/> and a
    /// <see cref="HeartbeatTransport"/>.  Heartbeat timing is controlled via
    /// <paramref name="options"/>; if omitted, defaults apply
    /// (3 s heartbeat interval, 10 s timeout).
    /// </summary>
    /// <param name="transport">
    /// A transport that has not yet been opened.
    /// The client takes ownership: it will call <see cref="IFlipperTransport.OpenAsync"/>
    /// and <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </param>
    /// <param name="options">
    /// Connection-behaviour options.  Pass <c>default</c> or omit to use the
    /// default heartbeat timing.
    /// </param>
    /// <param name="diagnostics">
    /// Optional diagnostics sink.  If <c>null</c>, logging is a no-op.
    /// </param>
    public FlipperRpcClient(
        IFlipperTransport transport,
        FlipperRpcClientOptions options = default,
        IRpcDiagnostics? diagnostics = null)
    {
        // Build the transport chain: raw → [serializer] → [heartbeat]
        // Both decorator layers are optional; see FlipperRpcClientOptions.
        IFlipperTransport current = transport;

        if (!options.DisablePacketSerialization)
        {
            current = new PacketSerializationTransport(current);
        }

        if (!options.DisableHeartbeat)
        {
            var heartbeat = new HeartbeatTransport(current, options.HeartbeatInterval, options.Timeout);
            // Subscribe to the transport-level disconnect event.
            // HeartbeatTransport fires this when the inbound channel is silent for
            // longer than options.Timeout, or when sending a keep-alive frame fails.
            heartbeat.Disconnected += OnHeartbeatDisconnected;
            _heartbeat = heartbeat;
            current = heartbeat;
        }

        _transport = current;

        _options = options;
        _diagnostics = diagnostics ?? RpcMessageDispatcher.NullDiagnosticsInstance;

        _dispatcher = new RpcMessageDispatcher(_pending, _streams, _diagnostics);
    }

    // -------------------------------------------------------------------------
    // ConnectAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the transport, starts the background reader loop, and performs
    /// capability negotiation with the connected Flipper daemon.
    ///
    /// Calls <c>daemon_info</c>, verifies that <see cref="DaemonInfoResponse.Name"/>
    /// equals <c>"flipper_zero_rpc_daemon"</c>, and checks that the daemon's protocol
    /// version is at least <paramref name="minProtocolVersion"/>.
    ///
    /// If the daemon supports the <c>configure</c> command (protocol version &gt;= 4),
    /// the client's <see cref="FlipperRpcClientOptions"/> are propagated to the daemon
    /// so that both sides use identical heartbeat timing.
    ///
    /// The result is stored in <see cref="DaemonInfo"/> for later inspection.
    /// </summary>
    /// <param name="minProtocolVersion">
    /// Minimum acceptable protocol version (default <c>1</c>).
    /// Throws <see cref="FlipperRpcException"/> if the daemon reports a lower version.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The full <see cref="DaemonInfoResponse"/> for capability inspection.</returns>
    /// <exception cref="FlipperRpcException">
    /// Thrown if the daemon name does not match, or the protocol version is below
    /// <paramref name="minProtocolVersion"/>.
    /// </exception>
    public async Task<DaemonInfoResponse> ConnectAsync(
        uint minProtocolVersion = 1,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        await _transport.OpenAsync(ct).ConfigureAwait(false);
        _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token));

        DaemonInfo = await NegotiateAsync(minProtocolVersion, ct).ConfigureAwait(false);
        return DaemonInfo.Value;
    }

    private async Task<DaemonInfoResponse> NegotiateAsync(
        uint minProtocolVersion,
        CancellationToken ct)
    {
        const string ExpectedName = "flipper_zero_rpc_daemon";

        var info = await SendAsync<DaemonInfoCommand, DaemonInfoResponse>(
            new DaemonInfoCommand(), ct).ConfigureAwait(false);

        if (info.Name != ExpectedName)
        {
            throw new FlipperRpcException(
                $"Capability negotiation failed: expected daemon name '{ExpectedName}', " +
                $"got '{info.Name ?? "(null)"}'. " +
                "Ensure the FlipperZero.NET RPC daemon FAP is running on the device.");
        }

        if (info.Version < minProtocolVersion)
        {
            throw new FlipperRpcException(
                $"Capability negotiation failed: daemon protocol version {info.Version} " +
                $"is below the required minimum {minProtocolVersion}. " +
                "Please update the FlipperZero.NET RPC daemon FAP on the device.");
        }

        // Propagate host-side configuration to the daemon so both sides are in sync.
        // Also send the LED indicator colour when configured, and the diagnostics flag.
        // Skip gracefully when talking to an older daemon that predates the configure command.
        if (info.Supports<ConfigureCommand>())
        {
            uint heartbeatMs;
            uint timeoutMs;

            if (_options.DisableHeartbeat)
            {
                // Client-side heartbeat is disabled — send very large values so the daemon
                // never times out the client.  The daemon still runs its own RX watchdog,
                // so without these values it would disconnect after its default 10 s timeout.
                // 1 h interval / 2 h timeout comfortably satisfies the daemon's validation
                // rule (timeout > heartbeat) and its minimum thresholds (hb >= 500 ms,
                // to >= 2000 ms, to > hb).
                heartbeatMs = (uint)TimeSpan.FromHours(1).TotalMilliseconds;
                timeoutMs   = (uint)TimeSpan.FromHours(2).TotalMilliseconds;
            }
            else
            {
                heartbeatMs = (uint)_options.HeartbeatInterval.TotalMilliseconds;
                timeoutMs   = (uint)_options.Timeout.TotalMilliseconds;
            }

            await SendAsync<ConfigureCommand, ConfigureResponse>(
                new ConfigureCommand(heartbeatMs, timeoutMs, _options.LedIndicatorColor, _options.DaemonDiagnostics), ct).ConfigureAwait(false);
        }
        else if (_options.DisableHeartbeat)
        {
            // The daemon predates the configure command, so we cannot tell it to use
            // extended heartbeat timeouts.  With client-side heartbeat disabled and no
            // configure support, the daemon will disconnect after its built-in ~10 s
            // inbound-silence timeout.  Fail fast with a clear explanation rather than
            // letting the caller observe a cryptic disconnect a few seconds later.
            throw new FlipperRpcException(
                "DisableHeartbeat=true requires the daemon to support the 'configure' command " +
                $"(protocol version >= 4), but the connected daemon reports version {info.Version}. " +
                "Update the FlipperZero.NET RPC daemon FAP, or set DisableHeartbeat=false.");
        }

        return info;
    }

    // -------------------------------------------------------------------------
    // Core generic API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a request/response command and returns the typed response.
    /// No boxing: <typeparamref name="TCommand"/> is passed as a generic parameter.
    /// </summary>
    public async Task<TResponse> SendAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken ct = default)
        where TCommand : struct, IRpcCommand<TResponse>
        where TResponse : struct, IRpcCommandResponse
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (Volatile.Read(ref _faulted) == 1)
        {
            throw _faultException
                ?? new FlipperDisconnectedException(DisconnectReason.ConnectionLost, "Connection lost.");
        }

        ct.ThrowIfCancellationRequested();

        var id = Interlocked.Increment(ref _nextId);
        var pending = new PendingRequest<TResponse>();

        // Register BEFORE sending so the reader loop can never race ahead.
        _pending.Register(id, pending);

        // Wire cancellation: if ct fires before the response arrives, fail the pending request.
        ct.Register(() => pending.Fail(new OperationCanceledException(ct)));

        var json = RpcMessageSerializer.Serialize(id, command.CommandId, command.WriteArgs);

        try
        {
            await _transport.SendAsync(json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            // pending.Fail already called by the ct.Register callback; just rethrow.
            throw new OperationCanceledException(ex.Message, ex, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            pending.Fail(ex);
            throw;
        }

        // Stamp the send time for round-trip tracking.
        _pending.StampSentTimestamp(id, Stopwatch.GetTimestamp());

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.CommandSent,
            RequestId = id,
            CommandName = command.CommandName,
            RawJson = json,
        });

        return await pending.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a stream-opening command.
    /// Returns an <see cref="RpcStream{TEvent}"/> that produces events via
    /// <c>await foreach</c> and releases Flipper resources when disposed.
    /// </summary>
    public async Task<RpcStream<TEvent>> SendStreamAsync<TCommand, TEvent>(
        TCommand command,
        CancellationToken ct = default)
        where TCommand : struct, IRpcStreamCommand<TEvent>
        where TEvent : struct, IRpcCommandResponse
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (Volatile.Read(ref _faulted) == 1)
        {
            throw _faultException
                ?? new FlipperDisconnectedException(DisconnectReason.ConnectionLost, "Connection lost.");
        }

        ct.ThrowIfCancellationRequested();

        // Step 1: send the command and wait for the stream-open response.
        // Reuse the standard request/response path — the daemon replies with
        // {"t":0,"i":N,"p":{"s":M}}.
        var openPending = new PendingRequest<StreamOpenResult>();
        var id = Interlocked.Increment(ref _nextId);

        _pending.Register(id, openPending);
        ct.Register(() => openPending.Fail(new OperationCanceledException(ct)));

        var json = RpcMessageSerializer.Serialize(id, command.CommandId, command.WriteArgs);

        try
        {
            await _transport.SendAsync(json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            openPending.Fail(ex);
            throw;
        }

        _pending.StampSentTimestamp(id, Stopwatch.GetTimestamp());

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.CommandSent,
            RequestId = id,
            CommandName = command.CommandName,
            RawJson = json,
        });

        var openResult = await openPending.Task.ConfigureAwait(false);
        var streamId = openResult.StreamId;

        // Step 2: register the stream AFTER the stream-open response arrives.
        var stream = _streams.CreateStream<TEvent>(streamId, _disconnectCts.Token);
        stream.Closed += sid => CloseStreamAsync(sid, _disconnectCts.Token);
        return stream;
    }

    // -------------------------------------------------------------------------
    // Stream close (called by RpcStream<T>.DisposeAsync)
    // -------------------------------------------------------------------------

    private async Task CloseStreamAsync(uint streamId, CancellationToken ct)
    {
        // Send stream_close to the Flipper (fire-and-forget-ish; ignore errors)
        try
        {
            var closeCmd = new StreamCloseCommand(streamId);
            await SendAsync<StreamCloseCommand, StreamCloseResponse>(closeCmd, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* disposing — acceptable */
        }
        catch (FlipperRpcException)
        {
            /* stream may already be gone on the device */
        }
        catch (ObjectDisposedException)
        {
            /* client already disposed */
        }
    }

    // -------------------------------------------------------------------------
    // Reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var line in _transport.ReceiveAsync(ct).ConfigureAwait(false))
            {
                // When the HeartbeatTransport layer is disabled the daemon's own keep-alive
                // frames (bare newlines) reach this loop directly.  Filter them out here
                // rather than forwarding to RpcEnvelope.Parse, which would log them as
                // malformed JSON.
                if (_options.DisableHeartbeat && line.AsSpan().Trim().IsEmpty)
                {
                    continue;
                }

                var receivedTimestamp = Stopwatch.GetTimestamp();
                var envelope = RpcEnvelope.Parse(line);

                if (envelope.Type == RpcMessageType.Disconnect)
                {
                    FaultAll(new FlipperDisconnectedException(
                        DisconnectReason.DaemonExited, "Daemon disconnected."));
                    return;
                }

                _dispatcher.Dispatch(envelope, line, receivedTimestamp);

                // Yield to the cooperative scheduler after each message.  In single-threaded
                // Blazor WASM this gives the JS event loop (and the WebSerial read pump) a
                // turn between messages, preventing starvation when a burst of responses
                // arrives.  On desktop .NET this is harmless — it simply re-queues the
                // continuation to the ThreadPool.
                await Task.Yield();
            }

            // ReceiveAsync enumeration ended normally (transport EOF / cancelled).
            if (!ct.IsCancellationRequested)
            {
                FaultAll(new FlipperDisconnectedException(
                    DisconnectReason.ConnectionLost, "Connection lost."));
            }
        }
        catch (OperationCanceledException)
        {
            /* normal shutdown */
        }
        catch (Exception ex)
        {
            LogError(ex.Message);
            FaultAll(new FlipperDisconnectedException(
                DisconnectReason.ReaderFailed, "Reader loop failed.", ex));
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void LogError(string status) =>
        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.Error,
            Status = status,
        });

    private void OnHeartbeatDisconnected()
    {
        LogError("Connection lost — heartbeat timeout.");
        FaultAll(new FlipperDisconnectedException(
            DisconnectReason.HeartbeatTimeout, "Connection lost — heartbeat timeout."));
    }

    private void FaultAll(FlipperDisconnectedException ex)
    {
        // Only the first caller executes the teardown; subsequent calls are no-ops.
        if (Interlocked.CompareExchange(ref _faulted, 1, 0) != 0)
        {
            return;
        }

        // Store the fault so pre-send guards can rethrow the exact same exception.
        _faultException = ex;

        // Cancel the reader loop CTS.
        _cts.Cancel();

        // Fail all pending requests and active streams with the typed exception FIRST,
        // so that stream consumers see FlipperDisconnectedException rather than
        // OperationCanceledException when the disconnect token fires next.
        _pending.FailAll(ex);
        _streams.FaultAll(ex);

        // Signal disconnect token last — any code awaiting it will unblock now.
        // Because the channel fault is already in place, RpcStream enumerators
        // will throw FlipperDisconnectedException rather than OperationCanceledException.
        _disconnectCts.Cancel();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);

        FaultAll(new FlipperDisconnectedException(DisconnectReason.ClientDisposed, "Client disposed."));

        // Dispose the transport BEFORE awaiting the reader task.
        // On Windows, SerialPort.BaseStream.ReadAsync ignores CancellationToken;
        // closing the port (done inside SerialPortTransport.DisposeAsync) is the
        // only reliable way to unblock a pending ReadLineAsync and let the reader
        // loop exit.  Awaiting _readerTask first would deadlock because the port
        // close only happens after the await — which never completes.
        await _transport.DisposeAsync().ConfigureAwait(false);

        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }
        _disconnectCts.Dispose();
        _cts.Dispose();
    }
}