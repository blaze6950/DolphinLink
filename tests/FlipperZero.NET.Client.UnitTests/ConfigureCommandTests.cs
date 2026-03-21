using FlipperZero.NET.Commands.System;
using System.Linq;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="ConfigureCommand"/> serialization and
/// <see cref="ConfigureResponse"/> field mapping.
/// No transport or hardware required.
/// </summary>
public sealed class ConfigureCommandTests
{
    [Fact]
    public void ConfigureCommand_WriteArgs_EmitsHeartbeatMsAndTimeoutMs()
    {
        var cmd = new ConfigureCommand(heartbeatMs: 3000, timeoutMs: 10000);
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandName, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("configure", root.GetProperty("cmd").GetString());
        Assert.Equal(3000u, root.GetProperty("heartbeat_ms").GetUInt32());
        Assert.Equal(10000u, root.GetProperty("timeout_ms").GetUInt32());
    }

    [Fact]
    public void ConfigureCommand_CommandName_IsCorrect()
    {
        var cmd = new ConfigureCommand(500, 2000);
        Assert.Equal("configure", cmd.CommandName);
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_ProducesExactlyTwoArgFields()
    {
        var cmd = new ConfigureCommand(1000, 5000);
        var json = RpcMessageSerializer.Serialize(99, cmd.CommandName, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Expect exactly: id, cmd, heartbeat_ms, timeout_ms
        var properties = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "id", "cmd", "heartbeat_ms", "timeout_ms" }, properties);
    }

    [Fact]
    public void ConfigureCommand_Properties_RoundTrip()
    {
        var cmd = new ConfigureCommand(heartbeatMs: 7500, timeoutMs: 30000);

        Assert.Equal(7500u, cmd.HeartbeatMs);
        Assert.Equal(30000u, cmd.TimeoutMs);
    }

    [Fact]
    public void ConfigureResponse_DeserializesFromJson()
    {
        // Simulate a daemon response payload: {"heartbeat_ms":3000,"timeout_ms":10000}
        var payloadJson = """{"heartbeat_ms":3000,"timeout_ms":10000}""";
        var response = System.Text.Json.JsonSerializer.Deserialize<ConfigureResponse>(payloadJson);

        Assert.Equal(3000u, response.HeartbeatMs);
        Assert.Equal(10000u, response.TimeoutMs);
    }
}
