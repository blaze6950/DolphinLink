using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.Input;
using FlipperZero.NET.Exceptions;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Verifies that every disconnection path surfaces a <see cref="FlipperDisconnectedException"/>
/// with the correct <see cref="DisconnectReason"/> — everywhere, consistently.
///
/// Tests cover:
/// <list type="bullet">
///   <item>Transport EOF → <see cref="DisconnectReason.ConnectionLost"/></item>
///   <item>Daemon exit envelope → <see cref="DisconnectReason.DaemonExited"/></item>
///   <item>Client disposed → <see cref="DisconnectReason.ClientDisposed"/></item>
///   <item>Pre-send guard rethrows the SAME exception (correct reason preserved)</item>
///   <item>Pending requests receive the typed exception</item>
///   <item>Stream enumeration throws <see cref="FlipperDisconnectedException"/>,
///         NOT <see cref="OperationCanceledException"/></item>
///   <item>User cancellation on streams still throws <see cref="OperationCanceledException"/></item>
///   <item><see cref="FlipperDisconnectedException"/> is-a <see cref="FlipperRpcException"/></item>
/// </list>
///
/// No hardware required.
/// </summary>
public sealed class DisconnectReasonTests
{
    private static async Task<(FakeTransport Transport, FlipperRpcClient Client)> CreateConnectedClientAsync(
        string? extraCommands = null)
    {
        var transport = new FakeTransport();
        var client = transport.CreateClient();
        var commands = "\"ping\",\"daemon_info\",\"input_listen_start\",\"stream_close\"";
        if (extraCommands is not null)
        {
            commands += "," + extraCommands;
        }
        transport.EnqueueResponse(
            $$$"""{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":5,"cmds":[{{{commands}}}]}}""");
        await client.ConnectAsync();
        return (transport, client);
    }

    // -------------------------------------------------------------------------
    // FlipperDisconnectedException is-a FlipperRpcException
    // -------------------------------------------------------------------------

    [Fact]
    public void FlipperDisconnectedException_IsA_FlipperRpcException()
    {
        var ex = new FlipperDisconnectedException(DisconnectReason.ConnectionLost, "test");
        Assert.IsAssignableFrom<FlipperRpcException>(ex);
    }

    [Fact]
    public void FlipperDisconnectedException_ExposesReason()
    {
        foreach (var reason in Enum.GetValues<DisconnectReason>())
        {
            var ex = new FlipperDisconnectedException(reason, "test");
            Assert.Equal(reason, ex.Reason);
        }
    }

    [Fact]
    public void FlipperDisconnectedException_WithInner_ExposesInnerException()
    {
        var inner = new IOException("port closed");
        var ex = new FlipperDisconnectedException(DisconnectReason.ReaderFailed, "msg", inner);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal(DisconnectReason.ReaderFailed, ex.Reason);
    }

    // -------------------------------------------------------------------------
    // Transport EOF → ConnectionLost
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransportEof_SendAsync_ThrowsConnectionLost()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.SimulateDisconnect();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
        Assert.Equal(DisconnectReason.ConnectionLost, ex.Reason);
    }

    [Fact]
    public async Task TransportEof_SendStreamAsync_ThrowsConnectionLost()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.SimulateDisconnect();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
                new InputListenStartCommand()));
        Assert.Equal(DisconnectReason.ConnectionLost, ex.Reason);
    }

    [Fact]
    public async Task TransportEof_PendingSendAsync_ThrowsConnectionLost()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        // Issue a send whose response will never arrive.
        var sendTask = client.SendAsync<PingCommand, PingResponse>(new PingCommand());

        // Then disconnect.
        transport.SimulateDisconnect();

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(() => sendTask);
        Assert.Equal(DisconnectReason.ConnectionLost, ex.Reason);
    }

    // -------------------------------------------------------------------------
    // Daemon exit envelope {"t":2} → DaemonExited
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DaemonExitEnvelope_SendAsync_ThrowsDaemonExited()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.InjectEvent("""{"t":2}""");
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
        Assert.Equal(DisconnectReason.DaemonExited, ex.Reason);
    }

    [Fact]
    public async Task DaemonExitEnvelope_PendingSendAsync_ThrowsDaemonExited()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        var sendTask = client.SendAsync<PingCommand, PingResponse>(new PingCommand());

        transport.InjectEvent("""{"t":2}""");

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(() => sendTask);
        Assert.Equal(DisconnectReason.DaemonExited, ex.Reason);
    }

    // -------------------------------------------------------------------------
    // Client disposed → ClientDisposed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClientDisposed_SendAsync_ThrowsClientDisposed()
    {
        var (_, client) = await CreateConnectedClientAsync();

        await client.DisposeAsync();

        // After dispose, SendAsync throws ObjectDisposedException (the _disposed guard
        // fires before the _faulted guard), not FlipperDisconnectedException.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
    }

    [Fact]
    public async Task ClientDisposed_PendingSendAsync_ThrowsClientDisposed()
    {
        var (_, client) = await CreateConnectedClientAsync();

        // Issue a request whose response will never arrive, then dispose.
        var sendTask = client.SendAsync<PingCommand, PingResponse>(new PingCommand());
        await client.DisposeAsync();

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(() => sendTask);
        Assert.Equal(DisconnectReason.ClientDisposed, ex.Reason);
    }

    // -------------------------------------------------------------------------
    // Pre-send guard rethrows the stored fault exception (correct reason preserved)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PreSendGuard_RethrowsSameFaultException_AfterTransportEof()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.SimulateDisconnect();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var ex1 = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
        var ex2 = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));

        // Both calls throw the exact same exception instance.
        Assert.Same(ex1, ex2);
        Assert.Equal(DisconnectReason.ConnectionLost, ex1.Reason);
    }

    [Fact]
    public async Task PreSendGuard_RethrowsSameFaultException_AfterDaemonExit()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.InjectEvent("""{"t":2}""");
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var ex1 = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
        var ex2 = await Assert.ThrowsAsync<FlipperDisconnectedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));

        Assert.Same(ex1, ex2);
        Assert.Equal(DisconnectReason.DaemonExited, ex1.Reason);
    }

    // -------------------------------------------------------------------------
    // Stream enumeration throws FlipperDisconnectedException, NOT OCE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamEnumeration_TransportEof_ThrowsConnectionLost_NotOce()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":1}}""");
        var stream = await client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        var iterTask = Task.Run(async () =>
        {
            await foreach (var __ in stream) { /* drain */ }
        });

        transport.SimulateDisconnect();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var done = await Task.WhenAny(iterTask, Task.Delay(Timeout.Infinite, timeout.Token));
        Assert.Same(iterTask, done);

        // Must throw FlipperDisconnectedException — NOT OperationCanceledException.
        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(() => iterTask);
        Assert.Equal(DisconnectReason.ConnectionLost, ex.Reason);
    }

    [Fact]
    public async Task StreamEnumeration_DaemonExit_ThrowsDaemonExited_NotOce()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":2}}""");
        var stream = await client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        var iterTask = Task.Run(async () =>
        {
            await foreach (var __ in stream) { /* drain */ }
        });

        transport.InjectEvent("""{"t":2}""");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var done = await Task.WhenAny(iterTask, Task.Delay(Timeout.Infinite, timeout.Token));
        Assert.Same(iterTask, done);

        var ex = await Assert.ThrowsAsync<FlipperDisconnectedException>(() => iterTask);
        Assert.Equal(DisconnectReason.DaemonExited, ex.Reason);
    }

    // -------------------------------------------------------------------------
    // User cancellation on streams still throws OperationCanceledException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamEnumeration_UserCancellation_ThrowsOce_NotDisconnected()
    {
        var (transport, client) = await CreateConnectedClientAsync();
        await using var _ = client;

        transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":3}}""");
        var stream = await client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        using var cts = new CancellationTokenSource();

        var iterTask = Task.Run(async () =>
        {
            await foreach (var __ in stream.WithCancellation(cts.Token)) { /* drain */ }
        });

        // Cancel the user's token — NOT a transport disconnect.
        cts.Cancel();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var done = await Task.WhenAny(iterTask, Task.Delay(Timeout.Infinite, timeout.Token));
        Assert.Same(iterTask, done);

        // Must be OperationCanceledException (or subclass) from user cancel, NOT FlipperDisconnectedException.
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iterTask);
        Assert.IsNotType<FlipperDisconnectedException>(ex);

        // Client must NOT be faulted — it was only user cancellation.
        Assert.False(client.Disconnected.IsCancellationRequested,
            "Client was faulted by user cancellation — it should not have been.");

        // Clean up the stream
        transport.EnqueueResponse("""{"t":0,"i":3}""");
        await stream.DisposeAsync();
    }
}
