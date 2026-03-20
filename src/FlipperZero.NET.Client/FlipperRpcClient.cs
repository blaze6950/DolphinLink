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
/// Subscribe to <see cref="OnLogEntry"/> to receive <see cref="RpcLogEntry"/>
/// records for every command sent and response received.  Handlers are invoked
/// synchronously on the client's I/O loop threads and must return quickly and
/// must not throw.
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

    /// <summary>Parses and routes inbound V2 NDJSON lines.</summary>
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

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    /// <summary>
    /// Monotonic clock started at <see cref="ConnectAsync"/> time.
    /// Used to stamp all <see cref="RpcLogEntry"/> records.
    /// </summary>
    private readonly Stopwatch _clock = new();

    /// <summary>
    /// Raised for every command sent and response received by the client's I/O
    /// loops.  Handlers are invoked synchronously on the I/O loop threads and
    /// must return quickly and must not throw.
    /// </summary>
    public event Action<RpcLogEntry>? OnLogEntry;

    // -------------------------------------------------------------------------
    // Construction / lifecycle
    // -------------------------------------------------------------------------

    /// <param name="portName">
    /// Serial port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.
    /// Creates a <see cref="FlipperRpcTransport"/> (USB-CDC) internally,
    /// wrapped in <see cref="PacketSerializationTransport"/> and
    /// <see cref="HeartbeatTransport"/> with default timing.
    /// </param>
    public FlipperRpcClient(string portName)
        : this(new FlipperRpcTransport(portName))
    {
    }

    /// <summary>
    /// Creates a client connected to a USB-CDC serial port with explicit
    /// heartbeat timing.
    /// </summary>
    /// <param name="portName">
    /// Serial port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.
    /// </param>
    /// <param name="heartbeatInterval">
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// </param>
    /// <param name="timeout">
    /// How long inbound silence is allowed before the connection is declared lost.
    /// Must be strictly greater than <paramref name="heartbeatInterval"/>.
    /// </param>
    public FlipperRpcClient(string portName, TimeSpan heartbeatInterval, TimeSpan timeout)
        : this(new FlipperRpcTransport(portName), heartbeatInterval, timeout)
    {
    }

    /// <summary>
    /// Creates a client using the supplied transport.
    /// Use this overload to inject a custom transport (BLE, Wi-Fi, WASM/WebSerial bridge,
    /// or an in-process fake for unit tests).
    ///
    /// The transport is automatically wrapped in a <see cref="PacketSerializationTransport"/>
    /// and a <see cref="HeartbeatTransport"/> with default timing (3 s heartbeat interval,
    /// 10 s timeout).  Use
    /// <see cref="FlipperRpcClient(IFlipperTransport,TimeSpan,TimeSpan)"/>
    /// to override the heartbeat timing.
    /// </summary>
    /// <param name="transport">
    /// A transport that has not yet been opened.
    /// The client takes ownership: it will call <see cref="IFlipperTransport.OpenAsync"/>,
    /// and <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </param>
    public FlipperRpcClient(IFlipperTransport transport)
        : this(transport,
               HeartbeatTransport.DefaultHeartbeatInterval,
               HeartbeatTransport.DefaultTimeout)
    {
    }

    /// <summary>
    /// Creates a client using the supplied transport with explicit heartbeat timing.
    /// </summary>
    /// <param name="transport">Raw transport, not yet opened.</param>
    /// <param name="heartbeatInterval">
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// </param>
    /// <param name="timeout">
    /// How long inbound silence is allowed before the connection is declared lost.
    /// Must be strictly greater than <paramref name="heartbeatInterval"/>.
    /// </param>
    public FlipperRpcClient(
        IFlipperTransport transport,
        TimeSpan heartbeatInterval,
        TimeSpan timeout)
    {
        // Build the transport chain: raw → serializer → heartbeat
        var serializer = new PacketSerializationTransport(transport);
        _heartbeat = new HeartbeatTransport(serializer, heartbeatInterval, timeout);

        // Subscribe to the transport-level disconnect event.
        // HeartbeatTransport fires this when the inbound channel is silent for
        // longer than `timeout`, or when sending a keep-alive frame fails.
        _heartbeat.Disconnected += OnHeartbeatDisconnected;

        _transport = _heartbeat;

        _dispatcher = new RpcMessageDispatcher(
            _pending,
            _streams,
            _clock,
            entry => OnLogEntry?.Invoke(entry),
            FaultAll);
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

        OnLogEntry?.Invoke(new RpcLogEntry
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
        ct.ThrowIfCancellationRequested();

        // Step 1: send the command and wait for the stream-open response.
        // Reuse the standard request/response path — the daemon replies with
        // {"type":"response","id":N,"payload":{"stream":M}}.
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

        OnLogEntry?.Invoke(new RpcLogEntry
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
        // todo: maybe we can incapsulate channel inside the StreamState, like an implementation detail, right?
        var eventChannel = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>(
            // todo check the configuration of the channel - I expose IAsyncEnumerable, possible it can be used several times, right? So readers can be many? And actually the writer is single - I have a single internal reader loop.
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _streams.Register(streamId, new StreamState
        {
            EventChannel = eventChannel,
            // todo make this is not like an action, but like a member method of the class
            Complete = () => eventChannel.Writer.TryComplete(),
            // todo make this is not like an action, but like a member method of the class
            Fault = ex => eventChannel.Writer.TryComplete(ex),
        });

        return new RpcStream<TEvent>(
            streamId,
            eventChannel.Reader,
            // Use CancellationToken.None: the original `ct` may already be
            // cancelled by the time the caller disposes the stream, and we
            // still need to send stream_close.
            // todo revise this leaked abstraction - stream knows about the client
            closeAsync: sid => CloseStreamAsync(sid, CancellationToken.None),
            disconnectToken: _disconnectCts.Token);
    }

    // -------------------------------------------------------------------------
    // Stream close (called by RpcStream<T>.DisposeAsync)
    // -------------------------------------------------------------------------

    private async Task CloseStreamAsync(uint streamId, CancellationToken ct)
    {
        // Remove from the live streams table first so no more events are routed.
        _streams.TryRemoveAndComplete(streamId);

        // Send stream_close to the Flipper (fire-and-forget-ish; ignore errors)
        try
        {
            var closeCmd = new StreamCloseCommand(streamId);
            await SendAsync<StreamCloseCommand, StreamCloseResponse>(closeCmd, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* disposing — acceptable */ }
        catch (FlipperRpcException) { /* stream may already be gone on the device */ }
        catch (ObjectDisposedException) { /* client already disposed */ }
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
                _dispatcher.Dispatch(line);
            }

            // ReceiveAsync enumeration ended normally (transport EOF / cancelled).
            if (!ct.IsCancellationRequested)
            {
                FaultAll(new FlipperRpcException("Connection lost."));
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            LogError(ex.Message);
            FaultAll(new FlipperRpcException("Reader loop failed.", ex));
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void LogError(string status) =>
        OnLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.Error,
            Status = status,
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
        });

    private void OnHeartbeatDisconnected()
    {
        LogError("Connection lost — heartbeat timeout.");
        FaultAll(new FlipperRpcException("Connection lost — heartbeat timeout."));
    }

    private void FaultAll(Exception ex)
    {
        // Only the first caller executes the teardown; subsequent calls are no-ops.
        if (Interlocked.CompareExchange(ref _faulted, 1, 0) != 0)
        {
            return;
        }

        // Signal disconnect token so consumers react immediately.
        _disconnectCts.Cancel();

        // Cancel the reader loop CTS.
        _cts.Cancel();

        // Fail all pending requests and active streams.
        _pending.FailAll(ex);
        _streams.FaultAll(ex);
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);

        FaultAll(new FlipperRpcException("Client disposed."));

        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        _disconnectCts.Dispose();
        _cts.Dispose();
    }
}
