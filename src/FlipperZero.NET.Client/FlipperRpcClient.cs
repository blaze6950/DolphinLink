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
/// <see cref="SendAsync{TCommand,TResponse}"/> enqueues a <see cref="RpcWorkItem"/>
/// that captures a typed TCS in a closure.  This avoids boxing the TCS itself —
/// the dictionary stores only type-erased <see cref="PendingRequest"/> callbacks.
///
/// Streams
/// -------
/// <see cref="SendStreamAsync{TCommand,TEvent}"/> enqueues a work item and
/// returns an <see cref="RpcStream{TEvent}"/>.  The stream id returned by the
/// Flipper is resolved in the reader loop; subsequent event messages are pushed
/// into the stream's internal channel.
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

    private static readonly JsonSerializerOptions _jsonOptions = JsonSerializerOptions.Default;

    private uint _nextId = 0;

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
        var id = Interlocked.Increment(ref _nextId);

        var tcs = new TaskCompletionSource<TResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        var commandName = command.CommandName;

        var workItem = new RpcWorkItem
        {
            RequestId = id,
            CommandName = commandName,
            Json = RpcMessageSerializer.Serialize(id, commandName, command.WriteArgs),
            Register = () =>
            {
                _pending.Register(id, new PendingRequest
                {
                    OnSuccess = element =>
                    {
                        var envelope = JsonSerializer.Deserialize<RpcResponse<TResponse>>(element.GetRawText(), _jsonOptions);
                        tcs.TrySetResult(envelope.Data);
                    },
                    OnError = code =>
                    {
                        tcs.TrySetException(new FlipperRpcException(id, code));
                    },
                });
            },
        };

        await _outbound.Writer.WriteAsync(workItem, ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
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
        var id = Interlocked.Increment(ref _nextId);

        // The stream id assigned by the Flipper is resolved via this TCS.
        var streamIdTcs = new TaskCompletionSource<uint>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // The event channel is created up-front so events can be buffered
        // even if the consumer hasn't started iterating yet.
        var eventChannel = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        await using var reg = ct.Register(() => streamIdTcs.TrySetCanceled(ct));

        var commandName = command.CommandName;

        var workItem = new RpcWorkItem
        {
            RequestId = id,
            CommandName = commandName,
            Json = RpcMessageSerializer.Serialize(id, commandName, command.WriteArgs),
            Register = () =>
            {
                _pending.Register(id, new PendingRequest
                {
                    OnSuccess = element =>
                    {
                        // The initial response is {"id":N,"stream":M}
                        if (element.TryGetProperty("stream", out var streamProp)
                            && streamProp.TryGetUInt32(out var streamId))
                        {
                            // Register stream state before resolving the TCS
                            _streams.Register(streamId, new StreamState
                            {
                                EventChannel = eventChannel,
                                Complete = () => eventChannel.Writer.TryComplete(),
                                Fault = ex => eventChannel.Writer.TryComplete(ex),
                            });
                            streamIdTcs.TrySetResult(streamId);
                        }
                        else
                        {
                            streamIdTcs.TrySetException(
                                new FlipperRpcException("Stream open response missing 'stream' field."));
                        }
                    },
                    OnError = code =>
                    {
                        streamIdTcs.TrySetException(new FlipperRpcException(id, code));
                        eventChannel.Writer.TryComplete(new FlipperRpcException(id, code));
                    },
                });
            },
        };

        await _outbound.Writer.WriteAsync(workItem, ct).ConfigureAwait(false);

        var resolvedStreamId = await streamIdTcs.Task.ConfigureAwait(false);

        return new RpcStream<TEvent>(
            resolvedStreamId,
            eventChannel.Reader,
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
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.Error,
                Status = ex.Message,
                Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
            });
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

                line = line.Trim();
                if (line.Length == 0)
                {
                    // HeartbeatTransport already filters out keep-alive frames
                    // before they reach here. This is defence-in-depth only.
                    continue;
                }

                _dispatcher.Dispatch(line);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.Error,
                Status = ex.Message,
                Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
            });
            FaultAll(new FlipperRpcException("Reader loop failed.", ex));
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by <see cref="HeartbeatTransport.Disconnected"/> when the
    /// transport-level keep-alive detects a silent connection loss.
    /// Translates the transport event into an RPC-level fault.
    /// </summary>
    private void OnHeartbeatDisconnected()
    {
        OnLogEntry?.Invoke(new RpcLogEntry
        {
            Source = RpcLogSource.Client,
            Kind = RpcLogKind.Error,
            Status = "Connection lost — heartbeat timeout.",
            Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
        });
        FaultAll(new FlipperRpcException("Connection lost — heartbeat timeout."));
    }

    private void FaultAll(Exception ex)
    {
        // Only the first caller executes the teardown; subsequent calls are no-ops.
        if (Interlocked.CompareExchange(ref _faulted, 1, 0) != 0)
        {
            return;
        }

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
