using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Commands.Core;

namespace FlipperZero.NET;

/// <summary>
/// Core Flipper RPC client.
///
/// Architecture
/// ============
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
/// Logging
/// -------
/// Subscribe to <see cref="OnLogEntry"/> to receive <see cref="RpcLogEntry"/>
/// records for every command sent and response received.  Handlers are invoked
/// synchronously on the client's I/O loop threads and must return quickly and
/// must not throw.
/// </summary>
public sealed partial class FlipperRpcClient : IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Internal types
    // -------------------------------------------------------------------------

    /// <summary>
    /// An item placed on the outbound channel.
    /// <see cref="Json"/> is the fully-serialised line (without trailing \n).
    /// <see cref="RequestId"/> is used to register pending state before the line
    /// is actually sent so the reader loop can never race ahead of registration.
    /// </summary>
    private sealed class RpcWorkItem
    {
        public required uint RequestId { get; init; }
        public required string Json { get; init; }
        public required string CommandName { get; init; }

        /// <summary>
        /// Called by the writer loop immediately after enqueuing
        /// (before the send) to register pending state in the router.
        /// </summary>
        public required Action Register { get; init; }
    }

    /// <summary>
    /// Type-erased callbacks stored in the pending-request table.
    /// </summary>
    private sealed class PendingRequest
    {
        /// <summary>Called when a <c>"status":"ok"</c> or <c>"stream"</c> response arrives.</summary>
        public required Action<JsonElement> OnSuccess { get; init; }
        /// <summary>Called when an <c>"error"</c> response arrives.</summary>
        public required Action<string> OnError { get; init; }
        /// <summary>
        /// Stopwatch ticks recorded when the command line was sent.
        /// Set by the writer loop immediately after <see cref="FlipperRpcTransport.SendLineAsync"/>;
        /// read by <see cref="DispatchLine"/> to compute round-trip time.
        /// </summary>
        public long SentTicks { get; set; }
    }

    /// <summary>
    /// State for an open stream, stored while the stream is alive.
    /// </summary>
    private sealed class StreamState
    {
        /// <summary>Channel events are pushed into.</summary>
        public required Channel<JsonElement> EventChannel { get; init; }
        /// <summary>Called when the stream is remotely closed or on error.</summary>
        public required Action Complete { get; init; }
        public required Action<Exception> Fault { get; init; }
    }

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly FlipperRpcTransport _transport;
    private readonly Channel<RpcWorkItem> _outbound;

    /// <summary>Pending request-id → callbacks (for request/response commands).</summary>
    private readonly ConcurrentDictionary<uint, PendingRequest> _pending = new();

    /// <summary>Active stream-id → stream state (for streaming commands).</summary>
    private readonly ConcurrentDictionary<uint, StreamState> _streams = new();

    private static readonly JsonSerializerOptions _jsonOptions = JsonSerializerOptions.Default;

    private uint _nextId = 0;

    private Task? _writerTask;
    private Task? _readerTask;
    private readonly CancellationTokenSource _cts = new();

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    /// <summary>
    /// Monotonic clock started at <see cref="Connect"/> time.
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
    /// </param>
    public FlipperRpcClient(string portName)
    {
        _transport = new FlipperRpcTransport(portName);
        _outbound = Channel.CreateBounded<RpcWorkItem>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Opens the transport and starts background I/O loops.</summary>
    public void Connect()
    {
        _transport.Open();
        _clock.Start();
        _writerTask = Task.Run(() => WriterLoopAsync(_cts.Token));
        _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token));
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
        var id = Interlocked.Increment(ref _nextId);

        var tcs = new TaskCompletionSource<TResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        var commandName = command.CommandName;

        var workItem = new RpcWorkItem
        {
            RequestId = id,
            CommandName = commandName,
            Json = SerialiseMessage(id, commandName, command.WriteArgs),
            Register = () =>
            {
                _pending[id] = new PendingRequest
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
                };
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
            Json = SerialiseMessage(id, commandName, command.WriteArgs),
            Register = () =>
            {
                _pending[id] = new PendingRequest
                {
                    OnSuccess = element =>
                    {
                        // The initial response is {"id":N,"stream":M}
                        if(element.TryGetProperty("stream", out var streamProp)
                            && streamProp.TryGetUInt32(out var streamId))
                        {
                            // Register stream state before resolving the TCS
                            _streams[streamId] = new StreamState
                            {
                                EventChannel = eventChannel,
                                Complete = () => eventChannel.Writer.TryComplete(),
                                Fault = ex => eventChannel.Writer.TryComplete(ex),
                            };
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
                };
            },
        };

        await _outbound.Writer.WriteAsync(workItem, ct).ConfigureAwait(false);

        var resolvedStreamId = await streamIdTcs.Task.ConfigureAwait(false);

        return new RpcStream<TEvent>(
            resolvedStreamId,
            eventChannel.Reader,
            closeAsync: streamId => CloseStreamAsync(streamId, ct));
    }

    // -------------------------------------------------------------------------
    // Stream close (called by RpcStream<T>.DisposeAsync)
    // -------------------------------------------------------------------------

    private async Task CloseStreamAsync(uint streamId, CancellationToken ct)
    {
        // Remove from the live streams table first so no more events are routed.
        if(_streams.TryRemove(streamId, out var state))
        {
            state.Complete();
        }

        // Send stream_close to the Flipper (fire-and-forget-ish; ignore errors)
        try
        {
            var closeCmd = new StreamCloseCommand(streamId);
            await SendAsync<StreamCloseCommand, StreamCloseResponse>(closeCmd, ct)
                .ConfigureAwait(false);
        }
        catch(OperationCanceledException) { /* disposing — acceptable */ }
        catch(FlipperRpcException) { /* stream may already be gone on the device */ }
    }

    // -------------------------------------------------------------------------
    // Writer loop
    // -------------------------------------------------------------------------

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach(var item in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Register pending state BEFORE sending so the reader loop
                // cannot receive the response before we're registered.
                item.Register();

                // Stamp the send time for round-trip tracking.
                var sentTicks = _clock.ElapsedTicks;
                if(_pending.TryGetValue(item.RequestId, out var pr))
                {
                    pr.SentTicks = sentTicks;
                }

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
        catch(OperationCanceledException) { /* normal shutdown */ }
        catch(Exception ex)
        {
            // Fault all pending requests
            var faultEx = new FlipperRpcException("Writer loop failed.", ex);
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.Error,
                Status = ex.Message,
                Elapsed = TimeSpan.FromTicks(_clock.ElapsedTicks),
            });
            FaultAll(faultEx);
        }
    }

    // -------------------------------------------------------------------------
    // Reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var line = await _transport.ReadLineAsync(ct).ConfigureAwait(false);
                if(line is null)
                {
                    break; // port disconnected
                }

                line = line.Trim();
                if(line.Length == 0)
                {
                    continue;
                }

                DispatchLine(line);
            }
        }
        catch(OperationCanceledException) { /* normal shutdown */ }
        catch(Exception ex)
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

    private void DispatchLine(string line)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(line);
            root = doc.RootElement.Clone(); // Clone so we outlive the doc
        }
        catch
        {
            // Malformed JSON — ignore
            return;
        }

        var receivedTicks = _clock.ElapsedTicks;

        // Stream event: {"event":{...},"stream":<id>}
        if(root.TryGetProperty("event", out var eventElement)
            && root.TryGetProperty("stream", out var streamProp)
            && streamProp.TryGetUInt32(out var streamId))
        {
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.StreamEventReceived,
                StreamId = streamId,
                RawJson = line,
                Elapsed = TimeSpan.FromTicks(receivedTicks),
            });

            if(_streams.TryGetValue(streamId, out var streamState))
            {
                // Offer to the channel; if the channel is full the write will
                // back-pressure (WaitToWriteAsync) — fire on a background Task
                // to avoid blocking the reader loop.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await streamState.EventChannel.Writer
                            .WriteAsync(eventElement)
                            .ConfigureAwait(false);
                    }
                    catch { /* channel completed */ }
                });
            }
            return;
        }

        // Request/response: must have "id"
        if(!root.TryGetProperty("id", out var idProp)
            || !idProp.TryGetUInt32(out var requestId))
        {
            return;
        }

        if(!_pending.TryRemove(requestId, out var pending))
        {
            return; // No one waiting — ignore
        }

        // Compute round-trip time
        TimeSpan? roundTrip = null;
        if(pending.SentTicks > 0)
        {
            roundTrip = TimeSpan.FromTicks(receivedTicks - pending.SentTicks);
        }

        string? status = null;
        if(root.TryGetProperty("error", out var errorProp))
        {
            status = errorProp.GetString() ?? "unknown_error";
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.ResponseReceived,
                RequestId = requestId,
                Status = status,
                RawJson = line,
                Elapsed = TimeSpan.FromTicks(receivedTicks),
                RoundTrip = roundTrip,
            });
            pending.OnError(status);
        }
        else
        {
            // Detect stream-open response vs plain ok
            status = root.TryGetProperty("stream", out _) ? "stream_opened" : "ok";
            OnLogEntry?.Invoke(new RpcLogEntry
            {
                Source = RpcLogSource.Client,
                Kind = RpcLogKind.ResponseReceived,
                RequestId = requestId,
                Status = status,
                RawJson = line,
                Elapsed = TimeSpan.FromTicks(receivedTicks),
                RoundTrip = roundTrip,
            });
            pending.OnSuccess(root);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shared serialiser: writes <c>{"id":N,"cmd":"name",...args...}</c>.
    /// The <paramref name="writeArgs"/> delegate calls <c>command.WriteArgs(writer)</c>
    /// from the strongly-typed call-site so we avoid the phantom type parameter.
    /// </summary>
    private static string SerialiseMessage(uint id, string cmdName, Action<Utf8JsonWriter> writeArgs)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();
        writer.WriteNumber("id", id);
        writer.WriteString("cmd", cmdName);
        writeArgs(writer);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void FaultAll(Exception ex)
    {
        foreach(var kv in _pending)
        {
            if(_pending.TryRemove(kv.Key, out var p))
            {
                p.OnError(ex.Message);
            }
        }
        foreach(var kv in _streams)
        {
            if(_streams.TryRemove(kv.Key, out var s))
            {
                s.Fault(ex);
            }
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        _outbound.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);

        if(_writerTask is not null)
        {
            await _writerTask.ConfigureAwait(false);
        }

        // Close the port before awaiting the reader task. On Windows,
        // SerialPort.BaseStream.ReadAsync ignores the cancellation token, so
        // the reader loop can be permanently stuck in ReadLineAsync even after
        // _cts is cancelled. Closing the port forces the underlying read to
        // throw immediately, which exits the reader loop and unblocks the await below.
        _transport.Close();

        if(_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        FaultAll(new FlipperRpcException("Client disposed."));

        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
