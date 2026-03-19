using FlipperZero.NET.Client.UnitTests.Infrastructure;
using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests for <see cref="FlipperRpcClient.ConnectAsync"/> capability negotiation,
/// <see cref="DaemonInfoResponse.Supports(string)"/>, and the generic
/// <see cref="DaemonInfoResponse.Supports{TCommand}"/> overload.
/// Uses <see cref="FakeTransport"/>. No hardware required.
/// </summary>
public sealed class NegotiateTests
{
    private const string ValidDaemonInfoJson =
        """{"id":1,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping","daemon_info"]}}""";

    // -------------------------------------------------------------------------
    // ConnectAsync negotiation behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_Succeeds_WhenDaemonNameAndVersionMatch()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(ValidDaemonInfoJson);
        var info = await client.ConnectAsync(minProtocolVersion: 1);

        Assert.Equal("flipper_zero_rpc_daemon", info.Name);
        Assert.Equal(1, info.Version);
        Assert.True(info.Supports("ping"));
        Assert.True(info.Supports("daemon_info"));
        Assert.False(info.Supports("nonexistent_cmd"));
    }

    [Fact]
    public async Task ConnectAsync_StoresDaemonInfo_OnSuccess()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        Assert.Equal("flipper_zero_rpc_daemon", client.DaemonInfo.Name);
        Assert.Equal(1, client.DaemonInfo.Version);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenDaemonNameIsWrong()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(
            """{"id":1,"status":"ok","data":{"name":"some_other_app","version":1,"commands":[]}}""");

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(() => client.ConnectAsync());

        Assert.Contains("flipper_zero_rpc_daemon", ex.Message);
        Assert.Contains("some_other_app", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenProtocolVersionTooLow()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(
            """{"id":1,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":0,"commands":[]}}""");

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => client.ConnectAsync(minProtocolVersion: 1));

        Assert.Contains("version 0", ex.Message);
        Assert.Contains("minimum 1", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_Succeeds_WithHigherProtocolVersion()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(
            """{"id":1,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":5,"commands":["ping"]}}""");

        var info = await client.ConnectAsync(minProtocolVersion: 1);

        Assert.Equal(5, info.Version);
    }

    // -------------------------------------------------------------------------
    // DaemonInfoAsync + Supports(string)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DaemonInfoAsync_Supports_ReturnsFalse_ForUnknownCommand()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        // ConnectAsync negotiation
        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        // DaemonInfoAsync call
        transport.EnqueueResponse(
            """{"id":2,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping"]}}""");

        var info = await client.DaemonInfoAsync();

        Assert.False(info.Supports("ui_draw_str"));
        Assert.True(info.Supports("ping"));
    }

    // -------------------------------------------------------------------------
    // Supports<TCommand>() generic overload
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SupportsGeneric_ReturnsTrue_WhenCommandIsInList()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        // ping is in the daemon_info commands list from ValidDaemonInfoJson
        Assert.True(client.DaemonInfo.Supports<PingCommand>());
    }

    [Fact]
    public async Task SupportsGeneric_ReturnsFalse_WhenCommandIsNotInList()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport);

        // Only "ping" in the list — no UI commands
        transport.EnqueueResponse(
            """{"id":1,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping"]}}""");
        await client.ConnectAsync();

        Assert.False(client.DaemonInfo.Supports<DaemonInfoCommand>());
    }

    [Fact]
    public void SupportsGeneric_And_SupportsString_AreConsistent()
    {
        // Test without a live client: just exercise DaemonInfoResponse directly.
        var info = new DaemonInfoResponse
        {
            Name = "flipper_zero_rpc_daemon",
            Version = 1,
            Commands = ["ping", "daemon_info"],
        };

        Assert.Equal(info.Supports("ping"), info.Supports<PingCommand>());
        Assert.Equal(info.Supports("daemon_info"), info.Supports<DaemonInfoCommand>());
        Assert.Equal(info.Supports("not_there"), info.Supports<DaemonInfoCommand>() && false);
    }
}
