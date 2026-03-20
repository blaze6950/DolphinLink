using System.Threading.Channels;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET;

/// <summary>
/// Constructs <see cref="RpcWorkItem"/> instances from typed commands and
/// wires the corresponding completion handles.
///
/// Responsibilities
/// ----------------
/// <list type="bullet">
///   <item>Allocating monotonically-increasing request ids.</item>
///   <item>Serialising the outbound JSON line via <see cref="RpcMessageSerializer"/>.</item>
///   <item>Building the type-erased <see cref="PendingRequest"/> callbacks that
///         resolve the caller's <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>.</item>
///   <item>For stream commands: creating and registering the event channel in
///         <see cref="RpcStreamManager"/> once the Flipper assigns a stream id.</item>
/// </list>
///
/// Non-responsibilities
/// --------------------
/// The factory does <em>not</em> enqueue work items — that is done by
/// <see cref="FlipperRpcClient"/> after calling <c>CreateRequest</c> or
/// <c>CreateStreamRequest</c>.  The factory also has no knowledge of the
/// outbound channel, the disconnect token, or disposal state.
/// </summary>
internal sealed class RpcRequestFactory
{
    private readonly RpcPendingRequests _pending;
    private readonly RpcStreamManager _streams;
    private static readonly JsonSerializerOptions _jsonOptions = JsonSerializerOptions.Default;

    private uint _nextId;

    public RpcRequestFactory(RpcPendingRequests pending, RpcStreamManager streams)
    {
        _pending = pending;
        _streams = streams;
    }

    // -------------------------------------------------------------------------
    // Request / response
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a work item for a request/response command.
    /// </summary>
    /// <typeparam name="TCommand">The command struct type.</typeparam>
    /// <typeparam name="TResponse">The response struct type.</typeparam>
    /// <param name="command">The command to serialise.</param>
    /// <param name="ct">
    /// Used to cancel the returned <paramref name="response"/> task before it
    /// is even dequeued by the writer loop.
    /// </param>
    /// <returns>
    /// A <see cref="RpcWorkItem"/> ready to be enqueued on the outbound channel,
    /// and a <see cref="Task{TResponse}"/> that completes when the response arrives
    /// (or faults if the connection is lost or the request is cancelled).
    /// </returns>
    public (RpcWorkItem WorkItem, Task<TResponse> Response) CreateRequest<TCommand, TResponse>(
        TCommand command,
        CancellationToken ct)
        where TCommand : struct, IRpcCommand<TResponse>
        where TResponse : struct, IRpcCommandResponse
    {
        var id = Interlocked.Increment(ref _nextId);

        var tcs = new TaskCompletionSource<TResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Register cancellation outside the work item so the TCS is cancelled
        // even if the item is still sitting in the outbound channel.
        // The registration is fire-and-forget: if the TCS is already resolved
        // (success or fault) before ct fires, TrySetCanceled is a no-op.
        ct.Register(() => tcs.TrySetCanceled(ct));

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
                        var envelope = JsonSerializer.Deserialize<RpcResponse<TResponse>>(
                            element.GetRawText(), _jsonOptions);
                        tcs.TrySetResult(envelope.Data);
                    },
                    OnError = code => tcs.TrySetException(new FlipperRpcException(id, code)),
                });
            },
        };

        return (workItem, tcs.Task);
    }

    // -------------------------------------------------------------------------
    // Stream commands
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a work item for a stream-opening command.
    /// </summary>
    /// <typeparam name="TCommand">The stream command struct type.</typeparam>
    /// <typeparam name="TEvent">The event struct type produced by the stream.</typeparam>
    /// <param name="command">The command to serialise.</param>
    /// <param name="ct">
    /// Used to cancel the returned <paramref name="handle"/> task if the stream
    /// fails to open (e.g. resource busy, client disposed) before the Flipper
    /// has assigned a stream id.
    /// </param>
    /// <returns>
    /// A <see cref="RpcWorkItem"/> ready to be enqueued on the outbound channel,
    /// and a <see cref="Task{StreamHandle}"/> that completes once the Flipper
    /// returns <c>{"id":N,"stream":M}</c> and the stream state has been registered.
    /// The caller uses the <see cref="StreamHandle{TEvent}"/> to construct an
    /// <see cref="RpcStream{TEvent}"/>.
    /// </returns>
    public (RpcWorkItem WorkItem, Task<StreamHandle<TEvent>> Handle)
        CreateStreamRequest<TCommand, TEvent>(
            TCommand command,
            CancellationToken ct)
        where TCommand : struct, IRpcStreamCommand<TEvent>
        where TEvent : struct, IRpcCommandResponse
    {
        var id = Interlocked.Increment(ref _nextId);

        // Resolved once the Flipper assigns a stream id in the initial response.
        var streamIdTcs = new TaskCompletionSource<StreamHandle<TEvent>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Created up-front so events can be buffered before the consumer
        // starts iterating.
        var eventChannel = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        // Cancel the stream-open wait if the caller's token fires before the
        // Flipper responds.
        ct.Register(() => streamIdTcs.TrySetCanceled(ct));

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
                        // Initial stream response: {"id":N,"stream":M}
                        if (element.TryGetProperty("stream", out var streamProp)
                            && streamProp.TryGetUInt32(out var streamId))
                        {
                            // Register the stream state BEFORE resolving the TCS
                            // so the dispatcher can route events immediately.
                            _streams.Register(streamId, new StreamState
                            {
                                EventChannel = eventChannel,
                                Complete = () => eventChannel.Writer.TryComplete(),
                                Fault = ex => eventChannel.Writer.TryComplete(ex),
                            });

                            streamIdTcs.TrySetResult(new StreamHandle<TEvent>
                            {
                                StreamId = streamId,
                                EventReader = eventChannel.Reader,
                            });
                        }
                        else
                        {
                            streamIdTcs.TrySetException(
                                new FlipperRpcException(
                                    "Stream open response missing 'stream' field."));
                        }
                    },
                    OnError = code =>
                    {
                        var ex = new FlipperRpcException(id, code);
                        streamIdTcs.TrySetException(ex);
                        eventChannel.Writer.TryComplete(ex);
                    },
                });
            },
        };

        return (workItem, streamIdTcs.Task);
    }
}
