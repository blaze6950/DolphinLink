using System.Diagnostics;
using System.Threading.Channels;
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
///   IFlipperTransport   (raw: Serial, BLE, TCP, …)
///       ↑
///   HeartbeatTransport  (keep-alive, disconnect detection)
///       ↑
///   FlipperRpcClient    (this class — RPC logic only)
/// </code>
///
/// All outbound commands are serialised through a bounded
/// <see cref="Channel{T}"/> (capacity 32).  A single writer loop dequeues work
/// items and sends them over the transport, guaranteeing no interleaving on the
/// wire.  A single reader loop parses inbound NDJSON lines and dispatches them
/// to the correct pending request.
///
/// Request/response
/// ----------------
/// <see cref="SendAsync{TCommand,TResponse}"/> delegates to
/// <see cref="RpcRequestFactory.CreateRequest{TCommand,TResponse}"/> which
/// builds a typed <see cref="RpcWorkItem"/> and a matching
/// <see cref="System.Threading.Tasks.Task{TResponse}"/> backed by a
/// <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/> closure.
/// No boxing of the TCS; the pending table stores only type-erased
/// <see cref="PendingRequest"/> callbacks.
///
/// Streams
/// -------
/// <see cref="SendStreamAsync{TCommand,TEvent}"/> delegates to
/// <see cref="RpcRequestFactory.CreateStreamRequest{TCommand,TEvent}"/> which
/// builds the work item and event channel wiring, returning a
/// <see cref="StreamHandle{TEvent}"/> once the Flipper assigns a stream id.
/// The client wraps this in an <see cref="RpcStream{TEvent}"/> and returns it
/// to the caller.
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
    private readonly Channel<RpcWorkItem> _outbound;

    /// <summary>
    /// The <see cref="DaemonInfoResponse"/> returned by the daemon during
    /// <see cref="ConnectAsync"/>.  <c>null</c> before <see cref="ConnectAsync"/>
    /// has completed successfully.
    /// </summary>
    public DaemonInfoResponse? DaemonInfo { get; private set; }

    /// <summary>Pending request-id → callbacks (for request/response commands).</summary>
    private readonly RpcPendingRequests _pending = new();

    /// <summary>Active stream-id → stream state (for streaming commands).</summary>
    private readonly RpcStreamManager _streams = new();

    /// <summary>Parses and routes inbound NDJSON lines.</summary>
    private readonly RpcMessageDispatcher _dispatcher;

    /// <summary>
    /// Constructs typed <see cref="RpcWorkItem"/> instances and the matching
    /// completion handles for both request/response and stream commands.
    /// </summary>
    private readonly RpcRequestFactory _factory;

    private Task? _writerTask;
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
    /// wrapped in a <see cref="HeartbeatTransport"/> with default timing.
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
    /// The transport is automatically wrapped in a <see cref="HeartbeatTransport"/>
    /// with default timing (3 s heartbeat interval, 10 s timeout).
    /// Use <see cref="FlipperRpcClient(IFlipperTransport,TimeSpan,TimeSpan)"/>
    /// to override the heartbeat timing.
    /// </summary>
    /// <param name="transport">
    /// A transport that has not yet been opened.
    /// The client takes ownership: it will call <see cref="IFlipperTransport.Open"/>,
    /// <see cref="IFlipperTransport.Close"/>, and <see cref="IAsyncDisposable.DisposeAsync"/>.
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
        _heartbeat = new HeartbeatTransport(transport, heartbeatInterval, timeout);

        // Subscribe to the transport-level disconnect event.
        // HeartbeatTransport fires this when the inbound channel is silent for
        // longer than `timeout`, or when sending a keep-alive frame fails.
        // We translate it into a FaultAll so all pending RPC work fails fast.
        _heartbeat.Disconnected += OnHeartbeatDisconnected;

        _transport = _heartbeat;

        _outbound = Channel.CreateBounded<RpcWorkItem>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        _factory = new RpcRequestFactory(_pending, _streams);

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
    /// Opens the transport, starts background I/O loops, and performs
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
        _transport.Open();
        _clock.Start();
        _writerTask = Task.Run(() => WriterLoopAsync(_cts.Token));
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
        var (workItem, response) = _factory.CreateRequest<TCommand, TResponse>(command, ct);
        await _outbound.Writer.WriteAsync(workItem, ct).ConfigureAwait(false);
        return await response.ConfigureAwait(false);
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
        var (workItem, handleTask) = _factory.CreateStreamRequest<TCommand, TEvent>(command, ct);
        await _outbound.Writer.WriteAsync(workItem, ct).ConfigureAwait(false);

        var handle = await handleTask.ConfigureAwait(false);

        return new RpcStream<TEvent>(
            handle.StreamId,
            handle.EventReader,
            // Use CancellationToken.None: the original `ct` may already be
            // cancelled (e.g. a per-call timeout) by the time the caller
            // disposes the stream, and we still need to send stream_close.
            closeAsync: streamId => CloseStreamAsync(streamId, CancellationToken.None),
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
        catch (ChannelClosedException) { /* client already faulted/disposed */ }
    }

    // -------------------------------------------------------------------------
    // Writer loop
    // -------------------------------------------------------------------------

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        Exception exitException = new FlipperRpcException("Connection lost.");
        try
        {
            await foreach (var item in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Register pending state BEFORE sending so the reader loop
                // cannot receive the response before we're registered.
                item.Register();

                // Stamp the send time for round-trip tracking.
                var sentTicks = _clock.ElapsedTicks;
                // todo the writer loop is responsible for working with common RpcWorkItem, but for some reason we call _pending here - that is command specific, not stream, so we do not count time for streams.
                _pending.StampSentTicks(item.RequestId, sentTicks);

                await _transport.SendLineAsync(item.Json, ct).ConfigureAwait(false);

                // Emit a CommandSent log entry.
                OnLogEntry?.Invoke(new RpcLogEntry
                {
                    Source = RpcLogSource.Client,
                    Kind = RpcLogKind.CommandSent,
                    RequestId = item.RequestId,
                    CommandName = item.CommandName,
                    RawJson = item.Json,
                    Elapsed = TimeSpan.FromTicks(sentTicks),
                });
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            exitException = new FlipperRpcException("Writer loop failed.", ex);
            LogError(ex.Message);
            FaultAll(exitException);
        }
        finally
        {
            // Fail any item that was Register()-ed but whose TCS was not yet
            // reached by FaultAll (race between writer loop and FaultAll, or
            // items registered after FaultAll's _pending sweep ran).
            _pending.FailAll(exitException.Message);
        }
    }

    // -------------------------------------------------------------------------
    // Reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _transport.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    // Transport returned EOF — the Flipper disconnected (USB
                    // pulled, daemon stopped, port closed by OS, etc.).
                    // Fault all pending requests and open streams immediately
                    // so consumers are not left hanging indefinitely.
                    FaultAll(new FlipperRpcException("Connection lost."));
                    break;
                }

                _dispatcher.Dispatch(line);
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

    /// <summary>
    /// Emits an <see cref="RpcLogKind.Error"/> log entry stamped with the
    /// current elapsed time.  The three call sites (writer-loop catch,
    /// reader-loop catch, heartbeat disconnect) all emit the same shape.
    /// </summary>
    private void LogError(string status) =>
        OnLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.Error,
            Status = status,
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
        });

    /// <summary>
    /// Called by <see cref="HeartbeatTransport.Disconnected"/> when the
    /// transport-level keep-alive detects a silent connection loss.
    /// Translates the transport event into an RPC-level fault.
    /// </summary>
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

        // todo: think about simplifying this logic by having a single main Disconnect cts that is linked to all other components that must be stopped when disconnect cts is cacnelled.
        // Phase 1 — stop I/O: signal consumers, seal the channel, kill the loops,
        // close the transport.  Order within this phase is critical:
        //   • _disconnectCts before TryComplete — consumers see disconnect before channel closes.
        //   • TryComplete before _cts.Cancel — writer loop's ReadAllAsync must see a sealed
        //     channel, not a cancellation, so its finally block runs and drains orphans.
        //   • _cts.Cancel before Close — loops should attempt a clean exit before the port
        //     is torn down under them.
        StopIo();

        // Phase 2 — fail outstanding work: now that no new items can be enqueued
        // and both loops are exiting, it's safe to sweep pending requests and streams.
        FailOutstanding(ex);
    }

    /// <summary>
    /// Phase 1 of <see cref="FaultAll"/>: stops all I/O without touching the
    /// pending-request or stream tables.  Ordering within this method is critical;
    /// see the comment in <see cref="FaultAll"/>.
    /// </summary>
    private void StopIo()
    {
        // 1. Signal the disconnect token so consumers react immediately.
        _disconnectCts.Cancel();

        // 2. Seal the outbound channel so no new work items can be enqueued and
        //    the writer loop's ReadAllAsync exits at its next iteration.
        _outbound.Writer.TryComplete();

        // 3. Cancel the I/O loop CTS so both loops exit cleanly on their next
        //    cancellation check (writer loop via ReadAllAsync, reader loop via
        //    while(!ct.IsCancellationRequested)).
        _cts.Cancel();

        // 4. Close the transport immediately.
        //    - Drops DTR so the daemon sees a clean disconnect and resets its state.
        //    - On Windows, SerialPort.BaseStream.ReadAsync ignores the CancellationToken,
        //      so closing the port is the only reliable way to unblock the reader loop.
        _heartbeat.Close();
    }

    /// <summary>
    /// Phase 2 of <see cref="FaultAll"/>: fails all pending requests and open
    /// streams.  Must only be called after <see cref="StopIo"/> has run.
    /// </summary>
    private void FailOutstanding(Exception ex)
    {
        // 5. Fail all already-registered pending requests, and drain any orphan
        //    work items in the outbound channel whose TCS was never registered.
        _pending.FailAllAndOrphans(_outbound, ex.Message);

        // 6. Fault all active streams.
        _streams.FaultAll(ex);
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Mark as disposed so subsequent calls to SendAsync / SendStreamAsync /
        // ConnectAsync throw ObjectDisposedException instead of silently enqueuing.
        Interlocked.Exchange(ref _disposed, 1);

        // FaultAll is a one-shot teardown: closes the transport, cancels the
        // I/O-loop CTS, completes the outbound channel, and fails all pending
        // work.  If a disconnect was already detected (heartbeat timeout, reader
        // EOF, writer error) FaultAll is a no-op here.
        FaultAll(new FlipperRpcException("Client disposed."));

        if (_writerTask is not null)
        {
            await _writerTask.ConfigureAwait(false);
        }

        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        _disconnectCts.Dispose();
        _cts.Dispose();
    }
}
