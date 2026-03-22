using FlipperZero.NET.Commands;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Dispatch;

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
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("c").GetInt32());
        Assert.Equal(3000u, root.GetProperty("hb").GetUInt32());
        Assert.Equal(10000u, root.GetProperty("to").GetUInt32());
    }

    [Fact]
    public void ConfigureCommand_CommandName_IsCorrect()
    {
        var cmd = new ConfigureCommand(500, 2000);
        Assert.Equal("configure", cmd.CommandName);
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_WithoutLed_ProducesExactlyTwoArgFields()
    {
        var cmd = new ConfigureCommand(1000, 5000, led: null);
        var json = RpcMessageSerializer.Serialize(99, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Expect exactly: i, c, hb, to (no led)
        var properties = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "c", "i", "hb", "to" }, properties);
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_WithLed_EmitsLedObject()
    {
        var cmd = new ConfigureCommand(3000, 10000, led: RgbColor.DotNetPurple);
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Expect: i, c, hb, to, led
        var properties = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "c", "i", "hb", "to", "led" }, properties);

        var led = root.GetProperty("led");
        Assert.Equal(0x51, led.GetProperty("r").GetByte());
        Assert.Equal(0x2B, led.GetProperty("g").GetByte());
        Assert.Equal(0xD4, led.GetProperty("b").GetByte());
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
        // Simulate a daemon response payload: {"hb":3000,"to":10000}
        var payloadJson = """{"hb":3000,"to":10000}""";
        var response = System.Text.Json.JsonSerializer.Deserialize<ConfigureResponse>(payloadJson);

        Assert.Equal(3000u, response.HeartbeatMs);
        Assert.Equal(10000u, response.TimeoutMs);
    }

    [Fact]
    public void FlipperRpcClientOptions_Default_HasDotNetPurpleLed()
    {
        var opts = default(FlipperRpcClientOptions);
        Assert.Equal(RgbColor.DotNetPurple, opts.LedIndicatorColor);
    }

    [Fact]
    public void FlipperRpcClientOptions_LedCanBeDisabled()
    {
        var opts = new FlipperRpcClientOptions { LedIndicatorColor = null };
        Assert.Null(opts.LedIndicatorColor);
    }

    [Fact]
    public void FlipperRpcClientOptions_LedCanBeOverridden()
    {
        var opts = new FlipperRpcClientOptions { LedIndicatorColor = RgbColor.Red };
        Assert.Equal(RgbColor.Red, opts.LedIndicatorColor);
    }
}
