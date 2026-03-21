using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Exceptions;
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
        """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping","daemon_info"]}}""";

    // -------------------------------------------------------------------------
    // ConnectAsync negotiation behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_Succeeds_WhenDaemonNameAndVersionMatch()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

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
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        Assert.Equal("flipper_zero_rpc_daemon", client.DaemonInfo!.Value.Name);
        Assert.Equal(1, client.DaemonInfo!.Value.Version);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenDaemonNameIsWrong()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"some_other_app","version":1,"commands":[]}}""");

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(() => client.ConnectAsync());

        Assert.Contains("flipper_zero_rpc_daemon", ex.Message);
        Assert.Contains("some_other_app", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenProtocolVersionTooLow()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":0,"commands":[]}}""");

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => client.ConnectAsync(minProtocolVersion: 1));

        Assert.Contains("version 0", ex.Message);
        Assert.Contains("minimum 1", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_Succeeds_WithHigherProtocolVersion()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":5,"commands":["ping"]}}""");

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
        await using var client = transport.CreateClient();

        // ConnectAsync negotiation
        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        // DaemonInfoAsync call
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping"]}}""");

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
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(ValidDaemonInfoJson);
        await client.ConnectAsync();

        // ping is in the daemon_info commands list from ValidDaemonInfoJson
        Assert.True(client.DaemonInfo!.Value.Supports<PingCommand>());
    }

    [Fact]
    public async Task SupportsGeneric_ReturnsFalse_WhenCommandIsNotInList()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        // Only "ping" in the list — no UI commands
        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping"]}}""");
        await client.ConnectAsync();

        Assert.False(client.DaemonInfo!.Value.Supports<DaemonInfoCommand>());
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

    // -------------------------------------------------------------------------
    // configure auto-send during ConnectAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_SendsConfigure_WhenDaemonSupportsIt()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        // daemon_info response includes "configure" in the commands list
        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":4,"commands":["ping","daemon_info","configure"]}}""");
        // configure response
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"heartbeat_ms":3600000,"timeout_ms":7200000}}""");

        await client.ConnectAsync();

        // Verify that two lines were sent: daemon_info + configure
        var sent = transport.SentLines;
        Assert.Equal(2, sent.Count);

        using var firstDoc = JsonDocument.Parse(sent[0]);
        Assert.Equal("daemon_info", firstDoc.RootElement.GetProperty("cmd").GetString());

        using var secondDoc = JsonDocument.Parse(sent[1]);
        Assert.Equal("configure", secondDoc.RootElement.GetProperty("cmd").GetString());
        // heartbeat_ms and timeout_ms should match the FakeTransport client options (1h / 2h)
        Assert.Equal(3_600_000u, secondDoc.RootElement.GetProperty("heartbeat_ms").GetUInt32());
        Assert.Equal(7_200_000u, secondDoc.RootElement.GetProperty("timeout_ms").GetUInt32());
    }

    [Fact]
    public async Task ConnectAsync_SkipsConfigure_WhenDaemonDoesNotSupportIt()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        // daemon_info response does NOT include "configure"
        transport.EnqueueResponse(ValidDaemonInfoJson);

        await client.ConnectAsync();

        // Only one line sent: daemon_info only
        var sent = transport.SentLines;
        var line = Assert.Single(sent);

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("daemon_info", doc.RootElement.GetProperty("cmd").GetString());
    }

    [Fact]
    public async Task ConnectAsync_WithConfigure_StoresDaemonInfoCorrectly()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"name":"flipper_zero_rpc_daemon","version":4,"commands":["ping","daemon_info","configure"]}}""");
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"heartbeat_ms":3600000,"timeout_ms":7200000}}""");

        var info = await client.ConnectAsync();

        Assert.Equal("flipper_zero_rpc_daemon", info.Name);
        Assert.Equal(4, info.Version);
        Assert.True(info.Supports("configure"));
    }
}
