using System.Diagnostics;

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

    private sealed class FakeDiagnostics : IRpcDiagnostics
    {
        public List<RpcLogEntry> LogEntries { get; } = new();
        public void Log(RpcLogEntry entry) => LogEntries.Add(entry);
    }

    private sealed class DispatcherFixture
    {
        public RpcPendingRequests Pending { get; } = new();
        public RpcStreamManager Streams { get; } = new();
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public FakeDiagnostics Diagnostics { get; } = new();
        public List<RpcLogEntry> LogEntries => Diagnostics.LogEntries;

        public RpcMessageDispatcher Dispatcher { get; }

        public DispatcherFixture()
        {
            Dispatcher = new RpcMessageDispatcher(
                Pending,
                Streams,
                Clock,
                Diagnostics);
        }

        /// <summary>Parses a raw V3 JSON line and calls <see cref="RpcMessageDispatcher.Dispatch"/>.</summary>
        public void Dispatch(string rawLine)
        {
            var envelope = RpcEnvelope.Parse(rawLine);
            Dispatcher.Dispatch(envelope, rawLine, Clock.ElapsedTicks);
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

        f.Dispatch("not valid json {{{{");

        Assert.Single(f.LogEntries);
        Assert.Equal(RpcLogKind.Error, f.LogEntries[0].Kind);
        Assert.Contains("Malformed", f.LogEntries[0].Status);
    }

    // -------------------------------------------------------------------------
    // Stream event
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_StreamEvent_WritesToStreamChannel()
    {
        var f = new DispatcherFixture();

        var state = new StreamState();
        f.Streams.Register(3u, state);

        f.Dispatch("""{"t":1,"i":3,"p":{"value":99}}""");

        await Task.Delay(10); // give async write path a moment if needed
        Assert.True(state.Reader.TryRead(out var received));
        Assert.Equal(99, received.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Dispatch_StreamEvent_LogsStreamEventReceived()
    {
        var f = new DispatcherFixture();

        var state = new StreamState();
        f.Streams.Register(5u, state);

        f.Dispatch("""{"t":1,"i":5,"p":{}}""");

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

        f.Dispatch("""{"t":0,"i":1,"p":{"pong":true}}""");

        Assert.Single(req.Completions);
        Assert.Empty(req.Failures);
    }

    [Fact]
    public void Dispatch_SuccessResponse_LogsResponseReceived()
    {
        var f = new DispatcherFixture();
        f.RegisterPending(2);

        f.Dispatch("""{"t":0,"i":2}""");

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

        f.Dispatch("""{"t":0,"i":3,"e":"resource_busy"}""");

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

        // Should not throw or log
        f.Dispatch("""{"t":0,"i":999}""");

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

        f.Dispatch("""{"t":0,"i":4}""");

        Assert.Single(f.LogEntries);
        Assert.NotNull(f.LogEntries[0].RoundTrip);
        Assert.True(f.LogEntries[0].RoundTrip!.Value.Ticks >= 0);
    }
}
