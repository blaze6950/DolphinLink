using DolphinLink.Client.Commands;
using DolphinLink.Client.Commands.System;
using DolphinLink.Client.Dispatch;

namespace DolphinLink.Client.UnitTests;

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

        // Expect exactly: i, c, hb, to (no led, no dx)
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
    public void RpcClientOptions_Default_HasDotNetPurpleLed()
    {
        var opts = default(RpcClientOptions);
        Assert.Equal(RgbColor.DotNetPurple, opts.LedIndicatorColor);
    }

    [Fact]
    public void RpcClientOptions_LedCanBeDisabled()
    {
        var opts = new RpcClientOptions { LedIndicatorColor = null };
        Assert.Null(opts.LedIndicatorColor);
    }

    [Fact]
    public void RpcClientOptions_LedCanBeOverridden()
    {
        var opts = new RpcClientOptions { LedIndicatorColor = RgbColor.Red };
        Assert.Equal(RgbColor.Red, opts.LedIndicatorColor);
    }

    // -------------------------------------------------------------------------
    // Diagnostics flag tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureCommand_Diagnostics_DefaultIsFalse()
    {
        var cmd = new ConfigureCommand(3000, 10000);
        Assert.False(cmd.Diagnostics);
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_DiagnosticsTrue_EmitsDxTrue()
    {
        var cmd = new ConfigureCommand(3000, 10000, diagnostics: true);
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("dx").GetBoolean());
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_DiagnosticsFalse_OmitsDx()
    {
        // When diagnostics is false (default), "dx" must be absent — PATCH semantics.
        var cmd = new ConfigureCommand(3000, 10000, diagnostics: false);
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("dx", out _));
    }

    [Fact]
    public void ConfigureCommand_WriteArgs_DiagnosticsTrue_WithLed_FieldOrder()
    {
        // Ensure field order is: c, i, hb, to, led, dx
        var cmd = new ConfigureCommand(3000, 10000, led: RgbColor.DotNetPurple, diagnostics: true);
        var json = RpcMessageSerializer.Serialize(1, cmd.CommandId, cmd.WriteArgs);

        using var doc = JsonDocument.Parse(json);
        var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "c", "i", "hb", "to", "led", "dx" }, properties);
    }

    [Fact]
    public void ConfigureResponse_DeserializesFromJson_WithDx()
    {
        var payloadJson = """{"hb":3000,"to":10000,"dx":true}""";
        var response = System.Text.Json.JsonSerializer.Deserialize<ConfigureResponse>(payloadJson);

        Assert.Equal(3000u, response.HeartbeatMs);
        Assert.Equal(10000u, response.TimeoutMs);
        Assert.True(response.Diagnostics);
    }

    [Fact]
    public void ConfigureResponse_DeserializesFromJson_DxAbsentDefaultsFalse()
    {
        // Older daemons won't emit "dx" — it must default to false.
        var payloadJson = """{"hb":3000,"to":10000}""";
        var response = System.Text.Json.JsonSerializer.Deserialize<ConfigureResponse>(payloadJson);

        Assert.False(response.Diagnostics);
    }

    [Fact]
    public void RpcClientOptions_DaemonDiagnostics_DefaultIsFalse()
    {
        var opts = default(RpcClientOptions);
        Assert.False(opts.DaemonDiagnostics);
    }

    [Fact]
    public void RpcClientOptions_DaemonDiagnosticsCanBeEnabled()
    {
        var opts = new RpcClientOptions { DaemonDiagnostics = true };
        Assert.True(opts.DaemonDiagnostics);
    }
}
