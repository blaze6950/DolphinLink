using System.Diagnostics;
using System.Threading.Channels;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcMessageDispatcher"/>.
/// No transport or hardware required.
/// </summary>
public sealed class DispatcherTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private sealed class DispatcherFixture
    {
        public RpcPendingRequests Pending { get; } = new();
        public RpcStreamManager Streams { get; } = new();
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public List<RpcLogEntry> LogEntries { get; } = new();
        public List<Exception> Faults { get; } = new();

        public RpcMessageDispatcher Dispatcher { get; }

        public DispatcherFixture()
        {
            Dispatcher = new RpcMessageDispatcher(
                Pending,
                Streams,
                Clock,
                entry => LogEntries.Add(entry),
                ex => Faults.Add(ex));
        }

        /// <summary>Registers a pending request and returns the collected successes/errors.</summary>
        public (List<JsonElement> successes, List<string> errors) RegisterPending(uint id)
        {
            var successes = new List<JsonElement>();
            var errors = new List<string>();
            Pending.Register(id, new PendingRequest
            {
                OnSuccess = el => successes.Add(el),
                OnError = code => errors.Add(code),
            });
            return (successes, errors);
        }
    }

    // -------------------------------------------------------------------------
    // Malformed JSON
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_MalformedJson_LogsErrorAndDoesNotThrow()
    {
        var f = new DispatcherFixture();

        f.Dispatcher.Dispatch("not valid json {{{{");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.Error, f.LogEntries[0].Kind);
        Assert.Contains("Malformed", f.LogEntries[0].Status);
    }

    // -------------------------------------------------------------------------
    // Disconnect message
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_DisconnectMessage_CallsOnFault()
    {
        var f = new DispatcherFixture();

        f.Dispatcher.Dispatch("""{"disconnect":true}""");

        Assert.Single(f.Faults);
        Assert.IsType<FlipperRpcException>(f.Faults[0]);
        Assert.Contains("Daemon disconnected", f.Faults[0].Message);
    }

    // -------------------------------------------------------------------------
    // Stream event
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_StreamEvent_WritesToStreamChannel()
    {
        var f = new DispatcherFixture();

        var channel = Channel.CreateUnbounded<JsonElement>();
        f.Streams.Register(3u, new StreamState
        {
            EventChannel = channel,
            Complete = () => channel.Writer.TryComplete(),
            Fault = ex => channel.Writer.TryComplete(ex),
        });

        f.Dispatcher.Dispatch("""{"event":{"value":99},"stream":3}""");

        await Task.Delay(10); // give async write path a moment if needed
        Assert.True(channel.Reader.TryRead(out var received));
        Assert.Equal(99, received.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Dispatch_StreamEvent_LogsStreamEventReceived()
    {
        var f = new DispatcherFixture();

        var channel = Channel.CreateUnbounded<JsonElement>();
        f.Streams.Register(5u, new StreamState
        {
            EventChannel = channel,
            Complete = () => channel.Writer.TryComplete(),
            Fault = ex => channel.Writer.TryComplete(ex),
        });

        f.Dispatcher.Dispatch("""{"event":{},"stream":5}""");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.StreamEventReceived, f.LogEntries[0].Kind);
        Assert.Equal(5u, f.LogEntries[0].StreamId);
    }

    // -------------------------------------------------------------------------
    // Success response
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_SuccessResponse_CallsOnSuccess()
    {
        var f = new DispatcherFixture();
        var (successes, errors) = f.RegisterPending(1);

        f.Dispatcher.Dispatch("""{"id":1,"status":"ok","data":{"pong":true}}""");

        Assert.Single(successes);
        Assert.Empty(errors);
    }

    [Fact]
    public void Dispatch_SuccessResponse_LogsResponseReceived()
    {
        var f = new DispatcherFixture();
        f.RegisterPending(2);

        f.Dispatcher.Dispatch("""{"id":2,"status":"ok","data":{}}""");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.ResponseReceived, f.LogEntries[0].Kind);
        Assert.Equal("ok", f.LogEntries[0].Status);
    }

    // -------------------------------------------------------------------------
    // Error response
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_ErrorResponse_CallsOnError()
    {
        var f = new DispatcherFixture();
        var (successes, errors) = f.RegisterPending(3);

        f.Dispatcher.Dispatch("""{"id":3,"error":"resource_busy"}""");

        Assert.Empty(successes);
        Assert.Single(errors);
        Assert.Equal("resource_busy", errors[0]);
    }

    // -------------------------------------------------------------------------
    // Unknown id / no pending
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_ResponseForUnknownId_IsIgnoredSilently()
    {
        var f = new DispatcherFixture();

        // Should not throw or fault
        f.Dispatcher.Dispatch("""{"id":999,"status":"ok","data":{}}""");

        Assert.Empty(f.Faults);
        Assert.Empty(f.LogEntries);
    }

    // -------------------------------------------------------------------------
    // Round-trip computation
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_WithSentTicks_ComputesRoundTrip()
    {
        var f = new DispatcherFixture();
        var (_, _) = f.RegisterPending(4);
        // Stamp a non-zero sent time
        f.Pending.StampSentTicks(4, f.Clock.ElapsedTicks);

        f.Dispatcher.Dispatch("""{"id":4,"status":"ok","data":{}}""");

        Assert.Single(f.LogEntries);
        Assert.NotNull(f.LogEntries[0].RoundTrip);
        Assert.True(f.LogEntries[0].RoundTrip!.Value.Ticks >= 0);
    }
}
