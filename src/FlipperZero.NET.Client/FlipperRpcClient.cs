using System.Diagnostics;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.System;

namespace FlipperZero.NET;

/// <summary>
/// Core Flipper RPC client.
///
/// Architecture
/// ============
///
/// <code>
///   FlipperRpcTransport          (raw USB-CDC)
///       ↑
///   PacketSerializationTransport (single-writer serialiser)
///       ↑
///   HeartbeatTransport           (keep-alive, disconnect detection)
///       ↑
///   FlipperRpcClient             (this class — RPC logic only)
/// </code>
///
/// There is no outbound channel or writer loop in this class.
/// <see cref="PacketSerializationTransport"/> provides the single-writer
/// guarantee; <see cref="SendAsync{TCommand,TResponse}"/> calls
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
/// Handled entirely by <see cref="HeartbeatTransport"/>. This class has no
/// knowledge of heartbeat timing, watchdog logic, or keep-alive frames.
/// When the transport detects a silent disconnect it raises its
/// <see cref="HeartbeatTransport.Disconnected"/> event, which triggers
/// <see cref="FaultAll"/>.
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
    private readonly HeartbeatTransport _heartbeat;

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

    /// <summary>Monotonically increasing counter for request ids.</summary>
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
    /// Monotonic clock started at <see cref="ConnectAsync"/> time.
    /// Used to stamp all <see cref="RpcLogEntry"/> records.
    /// </summary>
    private readonly Stopwatch _clock = new();

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
    /// <see cref="FlipperRpcTransport"/>, BLE, Wi-Fi, WASM/WebSerial bridge,
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
        // Build the transport chain: raw → serializer → heartbeat
        var serializer = new PacketSerializationTransport(transport);
        _heartbeat = new HeartbeatTransport(serializer, options.HeartbeatInterval, options.Timeout);

        // Subscribe to the transport-level disconnect event.
        // HeartbeatTransport fires this when the inbound channel is silent for
        // longer than options.Timeout, or when sending a keep-alive frame fails.
        _heartbeat.Disconnected += OnHeartbeatDisconnected;

        _transport = _heartbeat;

        _diagnostics = diagnostics ?? RpcMessageDispatcher.NullDiagnosticsInstance;

        _dispatcher = new RpcMessageDispatcher(_pending, _streams, _clock, _diagnostics);
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
        int minProtocolVersion = 1,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        await _transport.OpenAsync(ct).ConfigureAwait(false);
        _clock.Start();
        _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token));

        DaemonInfo = await NegotiateAsync(minProtocolVersion, ct).ConfigureAwait(false);
        return DaemonInfo.Value;
    }

    private async Task<DaemonInfoResponse> NegotiateAsync(
        int minProtocolVersion,
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

        var json = RpcMessageSerializer.Serialize(id, command.CommandName, command.WriteArgs);

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
        _pending.StampSentTicks(id, _clock.ElapsedTicks);

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.CommandSent,
            RequestId = id,
            CommandName = command.CommandName,
            RawJson = json,
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
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
        // {"t":0,"i":N,"p":{"stream":M}}.
        var openPending = new PendingRequest<StreamOpenResult>();
        var id = Interlocked.Increment(ref _nextId);

        _pending.Register(id, openPending);
        ct.Register(() => openPending.Fail(new OperationCanceledException(ct)));

        var json = RpcMessageSerializer.Serialize(id, command.CommandName, command.WriteArgs);

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

        _pending.StampSentTicks(id, _clock.ElapsedTicks);

        _diagnostics.Log(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.CommandSent,
            RequestId = id,
            CommandName = command.CommandName,
            RawJson = json,
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
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
                var receivedTicks = _clock.ElapsedTicks;
                var envelope = RpcEnvelope.Parse(line);

                if (envelope.Type == RpcMessageType.Disconnect)
                {
                    FaultAll(new FlipperDisconnectedException(
                        DisconnectReason.DaemonExited, "Daemon disconnected."));
                    return;
                }

                _dispatcher.Dispatch(envelope, line, receivedTicks);
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
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
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

        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        _disconnectCts.Dispose();
        _cts.Dispose();
    }
}