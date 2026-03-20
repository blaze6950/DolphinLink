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

    /// <summary>
    /// Minimal test double for <see cref="IPendingRequest"/> that collects
    /// all <see cref="Complete"/> and <see cref="Fail"/> calls.
    /// </summary>
    private sealed class FakePendingRequest : IPendingRequest
    {
        public long SentTicks { get; set; }
        public List<JsonElement> Completions { get; } = new();
        public List<Exception> Failures { get; } = new();

        public void Complete(JsonElement payload) => Completions.Add(payload);
        public void Fail(Exception ex) => Failures.Add(ex);
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

        /// <summary>Registers a <see cref="FakePendingRequest"/> and returns it.</summary>
        public FakePendingRequest RegisterPending(uint id)
        {
            var req = new FakePendingRequest();
            Pending.Register(id, req);
            return req;
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

        f.Dispatcher.Dispatch("""{"type":"disconnect"}""");

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

        f.Dispatcher.Dispatch("""{"type":"event","id":3,"payload":{"value":99}}""");

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

        f.Dispatcher.Dispatch("""{"type":"event","id":5,"payload":{}}""");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.StreamEventReceived, f.LogEntries[0].Kind);
        Assert.Equal(5u, f.LogEntries[0].StreamId);
    }

    // -------------------------------------------------------------------------
    // Success response
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_SuccessResponse_CallsComplete()
    {
        var f = new DispatcherFixture();
        var req = f.RegisterPending(1);

        f.Dispatcher.Dispatch("""{"type":"response","id":1,"payload":{"pong":true}}""");

        Assert.Single(req.Completions);
        Assert.Empty(req.Failures);
    }

    [Fact]
    public void Dispatch_SuccessResponse_LogsResponseReceived()
    {
        var f = new DispatcherFixture();
        f.RegisterPending(2);

        f.Dispatcher.Dispatch("""{"type":"response","id":2}""");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.ResponseReceived, f.LogEntries[0].Kind);
        Assert.Equal("ok", f.LogEntries[0].Status);
    }

    // -------------------------------------------------------------------------
    // Error response
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_ErrorResponse_CallsFail()
    {
        var f = new DispatcherFixture();
        var req = f.RegisterPending(3);

        f.Dispatcher.Dispatch("""{"type":"response","id":3,"error":"resource_busy"}""");

        Assert.Empty(req.Completions);
        Assert.Single(req.Failures);
        Assert.IsType<FlipperRpcException>(req.Failures[0]);
        Assert.Equal("resource_busy", ((FlipperRpcException)req.Failures[0]).ErrorCode);
    }

    // -------------------------------------------------------------------------
    // Unknown id / no pending
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_ResponseForUnknownId_IsIgnoredSilently()
    {
        var f = new DispatcherFixture();

        // Should not throw or fault
        f.Dispatcher.Dispatch("""{"type":"response","id":999}""");

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
        f.RegisterPending(4);
        // Stamp a non-zero sent time
        f.Pending.StampSentTicks(4, f.Clock.ElapsedTicks);

        f.Dispatcher.Dispatch("""{"type":"response","id":4}""");

        Assert.Single(f.LogEntries);
        Assert.NotNull(f.LogEntries[0].RoundTrip);
        Assert.True(f.LogEntries[0].RoundTrip!.Value.Ticks >= 0);
    }
}
