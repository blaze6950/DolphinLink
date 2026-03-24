using DolphinLink.Client.Commands.Core;
using DolphinLink.Client.Commands.Input;

namespace DolphinLink.Client.UnitTests;

/// <summary>
/// Tests that <see cref="RpcClient"/> throws <see cref="ObjectDisposedException"/>
/// when <see cref="RpcClient.SendAsync{TCommand,TResponse}"/>,
/// <see cref="RpcClient.SendStreamAsync{TCommand,TEvent}"/>, or
/// <see cref="RpcClient.ConnectAsync"/> are called after
/// <see cref="RpcClient.DisposeAsync"/>.
///
/// Also verifies that <see cref="RpcClient.DaemonInfo"/> is <c>null</c>
/// before <see cref="RpcClient.ConnectAsync"/> completes.
/// </summary>
public sealed class LifecycleGuardTests
{
    // -------------------------------------------------------------------------
    // DaemonInfo is null before ConnectAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DaemonInfo_IsNull_BeforeConnectAsync()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        Assert.Null(client.DaemonInfo);
    }

    // -------------------------------------------------------------------------
    // ObjectDisposedException after DisposeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_Throws_ObjectDisposedException_AfterDispose()
    {
        var transport = new FakeTransport();
        var client = transport.CreateClient();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.SendAsync<PingCommand, PingResponse>(new PingCommand()));
    }

    [Fact]
    public async Task SendStreamAsync_Throws_ObjectDisposedException_AfterDispose()
    {
        var transport = new FakeTransport();
        var client = transport.CreateClient();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
                new InputListenStartCommand()));
    }

    [Fact]
    public async Task ConnectAsync_Throws_ObjectDisposedException_AfterDispose()
    {
        var transport = new FakeTransport();
        var client = transport.CreateClient();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.ConnectAsync());
    }
}
