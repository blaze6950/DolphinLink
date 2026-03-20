using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.Input;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests that <see cref="FlipperRpcClient"/> throws <see cref="ObjectDisposedException"/>
/// when <see cref="FlipperRpcClient.SendAsync{TCommand,TResponse}"/>,
/// <see cref="FlipperRpcClient.SendStreamAsync{TCommand,TEvent}"/>, or
/// <see cref="FlipperRpcClient.ConnectAsync"/> are called after
/// <see cref="FlipperRpcClient.DisposeAsync"/>.
///
/// Also verifies that <see cref="FlipperRpcClient.DaemonInfo"/> is <c>null</c>
/// before <see cref="FlipperRpcClient.ConnectAsync"/> completes.
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
            () => client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
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
