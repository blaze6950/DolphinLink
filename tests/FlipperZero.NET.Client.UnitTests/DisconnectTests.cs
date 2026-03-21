using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.Input;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests that verify <see cref="FlipperRpcClient"/> does not hang when the
/// transport disconnects while commands are pending or while streams are open.
///
/// All tests use <see cref="FakeTransport.SimulateDisconnect"/> to close the
/// inbound channel, which mirrors what happens when:
/// <list type="bullet">
///   <item>the daemon sends <c>{"t":2}</c> and exits (graceful),</item>
///   <item>the USB cable is pulled (transport EOF),</item>
///   <item>or the heartbeat watchdog fires (silent disconnect).</item>
/// </list>
///
/// No hardware required.
/// </summary>
public sealed class DisconnectTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly FlipperRpcClient _client;

    public DisconnectTests()
    {
        _client = _transport.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping","daemon_info","input_listen_start","stream_close"]}}""");
        await _client.ConnectAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Faulted-state guard: SendAsync/SendStreamAsync must not hang after disconnect
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ThrowsFlipperRpcException_AfterTransportEof()
    {
        // Simulate cable pull / daemon exit: close the inbound channel.
        _transport.SimulateDisconnect();

        // Give the reader loop a moment to detect the EOF and call FaultAll.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // SendAsync must throw immediately, NOT hang waiting for a response
        // that will never arrive.
        await Assert.ThrowsAsync<FlipperRpcException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
    }

    [Fact]
    public async Task SendStreamAsync_ThrowsFlipperRpcException_AfterTransportEof()
    {
        // Simulate cable pull / daemon exit.
        _transport.SimulateDisconnect();

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<FlipperRpcException>(
            () => _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
                new InputListenStartCommand()));
    }

    [Fact]
    public async Task SendAsync_ThrowsFlipperRpcException_AfterDaemonExitEnvelope()
    {
        // Daemon sends {"t":2} — graceful exit (the custom exit-key scenario).
        _transport.InjectEvent("""{"t":2}""");

        // Give the reader loop time to process the envelope and call FaultAll.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<FlipperRpcException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
    }

    // -------------------------------------------------------------------------
    // Stream DisposeAsync must not hang after disconnect
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamDispose_AfterTransportEof_CompletesPromptly()
    {
        // Open a stream.
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"stream":1}}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Simulate cable pull while the stream is open.
        _transport.SimulateDisconnect();

        // Give reader loop time to fault.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // DisposeAsync must complete quickly without hanging.
        // (Before the fix, CloseStreamAsync called SendAsync with CancellationToken.None
        // after FaultAll, registering a pending request that was never completed.)
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var disposeTask = stream.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(Timeout.Infinite, timeout.Token));

        Assert.Same(disposeTask, completed);
        await disposeTask; // propagate any unexpected exception
    }

    [Fact]
    public async Task StreamDispose_AfterDaemonExitEnvelope_CompletesPromptly()
    {
        // Open a stream.
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"stream":2}}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Daemon exits gracefully (custom exit key pressed).
        _transport.InjectEvent("""{"t":2}""");

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var disposeTask = stream.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(Timeout.Infinite, timeout.Token));

        Assert.Same(disposeTask, completed);
        await disposeTask;
    }

    // -------------------------------------------------------------------------
    // Disconnected token is cancelled after disconnect
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Disconnected_IsCancelled_AfterTransportEof()
    {
        _transport.SimulateDisconnect();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.Delay(Timeout.Infinite,
            CancellationTokenSource.CreateLinkedTokenSource(
                _client.Disconnected, timeout.Token).Token)
            .ContinueWith(_ => { });  // swallow cancellation

        Assert.True(_client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled within 5 s after transport EOF.");
    }

    [Fact]
    public async Task Disconnected_IsCancelled_AfterDaemonExitEnvelope()
    {
        _transport.InjectEvent("""{"t":2}""");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.Delay(Timeout.Infinite,
            CancellationTokenSource.CreateLinkedTokenSource(
                _client.Disconnected, timeout.Token).Token)
            .ContinueWith(_ => { });

        Assert.True(_client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled within 5 s after daemon exit envelope.");
    }

    // -------------------------------------------------------------------------
    // Stream enumeration exits after disconnect (no hanging await foreach)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamEnumeration_Exits_AfterTransportEof()
    {
        // Open a stream.
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"stream":3}}""");
        await using var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Start iterating in the background.
        var iterationTask = Task.Run(async () =>
        {
            await foreach (var _ in stream)
            {
                // consume events; exits when stream is faulted/cancelled
            }
        });

        // Simulate cable pull.
        _transport.SimulateDisconnect();

        // Iteration must exit within 5 s.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completed = await Task.WhenAny(iterationTask, Task.Delay(Timeout.Infinite, timeout.Token));

        Assert.Same(iterationTask, completed);
        // The task may complete with OperationCanceledException or a FlipperRpcException —
        // both are acceptable; what matters is that it does NOT hang.
        await iterationTask.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception!.InnerException is not OperationCanceledException
                                                             and not FlipperRpcException)
            {
                throw t.Exception;
            }
        });
    }
}
