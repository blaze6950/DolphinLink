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
        """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping","daemon_info"]}}""";

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
            """{"t":0,"i":1,"p":{"n":"some_other_app","v":1,"cmds":[]}}""");

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
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":0,"cmds":[]}}""");

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
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":5,"cmds":["ping"]}}""");

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
            """{"t":0,"i":2,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping"]}}""");

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
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping"]}}""");
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
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":4,"cmds":["ping","daemon_info","configure"]}}""");
        // configure response
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"hb":3600000,"to":7200000}}""");

        await client.ConnectAsync();

        // Verify that two lines were sent: daemon_info + configure
        var sent = transport.SentLines;
        Assert.Equal(2, sent.Count);

        using var firstDoc = JsonDocument.Parse(sent[0]);
        Assert.Equal(3, firstDoc.RootElement.GetProperty("c").GetInt32()); // daemon_info CommandId

        using var secondDoc = JsonDocument.Parse(sent[1]);
        Assert.Equal(2, secondDoc.RootElement.GetProperty("c").GetInt32()); // configure CommandId
        // hb and to should match the FakeTransport client options (1h / 2h)
        Assert.Equal(3_600_000u, secondDoc.RootElement.GetProperty("hb").GetUInt32());
        Assert.Equal(7_200_000u, secondDoc.RootElement.GetProperty("to").GetUInt32());
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
        Assert.Equal(3, doc.RootElement.GetProperty("c").GetInt32()); // daemon_info CommandId
    }

    [Fact]
    public async Task ConnectAsync_WithConfigure_StoresDaemonInfoCorrectly()
    {
        var transport = new FakeTransport();
        await using var client = transport.CreateClient();

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":4,"cmds":["ping","daemon_info","configure"]}}""");
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"hb":3600000,"to":7200000}}""");

        var info = await client.ConnectAsync();

        Assert.Equal("flipper_zero_rpc_daemon", info.Name);
        Assert.Equal(4, info.Version);
        Assert.True(info.Supports("configure"));
    }

    // -------------------------------------------------------------------------
    // DisableHeartbeat option
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_DisableHeartbeat_SendsLargeTimeoutViaConfigure()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport, new FlipperRpcClientOptions
        {
            DisableHeartbeat = true,
        });

        // daemon supports configure — client must send very large heartbeat/timeout values.
        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":4,"cmds":["ping","daemon_info","configure"]}}""");
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"hb":3600000,"to":7200000}}""");

        await client.ConnectAsync();

        var sent = transport.SentLines;
        Assert.Equal(2, sent.Count); // daemon_info + configure

        using var configDoc = JsonDocument.Parse(sent[1]);
        var root = configDoc.RootElement;

        // Heartbeat and timeout should be "effectively infinite" (1 h / 2 h).
        uint hb = root.GetProperty("hb").GetUInt32();
        uint to = root.GetProperty("to").GetUInt32();

        Assert.True(hb >= (uint)TimeSpan.FromMinutes(30).TotalMilliseconds,
            $"Expected hb >= 30 min, got {hb} ms");
        Assert.True(to >= (uint)TimeSpan.FromHours(1).TotalMilliseconds,
            $"Expected to >= 1 h, got {to} ms");
        Assert.True(to > hb, "timeout must be > heartbeat interval");
    }

    [Fact]
    public async Task ConnectAsync_DisableHeartbeat_ThrowsWhenDaemonLacksConfigure()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport, new FlipperRpcClientOptions
        {
            DisableHeartbeat = true,
        });

        // daemon_info does NOT advertise configure — old daemon that predates the command.
        transport.EnqueueResponse(ValidDaemonInfoJson);

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(() => client.ConnectAsync());

        Assert.Contains("DisableHeartbeat", ex.Message);
        Assert.Contains("configure", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_DisableHeartbeat_False_SendsNormalTimings()
    {
        var transport = new FakeTransport();
        // HeartbeatInterval = 5 s, Timeout = 15 s, DisableHeartbeat = false (default)
        await using var client = new FlipperRpcClient(transport, new FlipperRpcClientOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            Timeout           = TimeSpan.FromSeconds(15),
        });

        transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":4,"cmds":["ping","daemon_info","configure"]}}""");
        transport.EnqueueResponse(
            """{"t":0,"i":2,"p":{"hb":5000,"to":15000}}""");

        await client.ConnectAsync();

        var sent = transport.SentLines;
        Assert.Equal(2, sent.Count);

        using var doc = JsonDocument.Parse(sent[1]);
        Assert.Equal(5_000u, doc.RootElement.GetProperty("hb").GetUInt32());
        Assert.Equal(15_000u, doc.RootElement.GetProperty("to").GetUInt32());
    }

    // -------------------------------------------------------------------------
    // DisablePacketSerialization option
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_DisablePacketSerialization_SucceedsAndSendsCommands()
    {
        var transport = new FakeTransport();
        await using var client = new FlipperRpcClient(transport, new FlipperRpcClientOptions
        {
            DisablePacketSerialization = true,
            // Use large heartbeat so the heartbeat loop doesn't interfere.
            HeartbeatInterval = TimeSpan.FromHours(1),
            Timeout           = TimeSpan.FromHours(2),
        });

        transport.EnqueueResponse(ValidDaemonInfoJson);
        var info = await client.ConnectAsync();

        Assert.Equal("flipper_zero_rpc_daemon", info.Name);

        // Verify a subsequent command still works (no serialization layer → direct send).
        transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");
        await client.PingAsync();

        var sent = transport.SentLines;
        Assert.Equal(2, sent.Count); // daemon_info + ping
    }

    [Fact]
    public void FlipperRpcClientOptions_DisableHeartbeat_DefaultIsFalse()
    {
        var opts = default(FlipperRpcClientOptions);
        Assert.False(opts.DisableHeartbeat);
    }

    [Fact]
    public void FlipperRpcClientOptions_DisablePacketSerialization_DefaultIsFalse()
    {
        var opts = default(FlipperRpcClientOptions);
        Assert.False(opts.DisablePacketSerialization);
    }

    [Fact]
    public void FlipperRpcClientOptions_DisableHeartbeat_CanBeEnabled()
    {
        var opts = new FlipperRpcClientOptions { DisableHeartbeat = true };
        Assert.True(opts.DisableHeartbeat);
    }

    [Fact]
    public void FlipperRpcClientOptions_DisablePacketSerialization_CanBeEnabled()
    {
        var opts = new FlipperRpcClientOptions { DisablePacketSerialization = true };
        Assert.True(opts.DisablePacketSerialization);
    }
}
