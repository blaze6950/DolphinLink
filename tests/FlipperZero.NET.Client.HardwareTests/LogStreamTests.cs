using System.Text.Json;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Extensions;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET.Client.HardwareTests;

/// <summary>
/// xUnit collection for diagnostics tests that open their own
/// <see cref="FlipperRpcClient"/> instances per test.  These tests require
/// exclusive access to the serial port so they CANNOT share the
/// <see cref="FlipperCollection"/> fixture (which holds the port open for the
/// duration of that collection).
///
/// xUnit runs collections sequentially, so <see cref="FlipperCollection"/>
/// finishes (and releases the port) before this collection starts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DiagnosticsCollection
{
    public const string Name = "Flipper diagnostics";
}

/// <summary>
/// Hardware tests for client-side diagnostics via <see cref="IRpcDiagnostics"/>
/// injection, and for daemon-side per-request timing metrics (<c>"_m"</c>).
///
/// Every test opens its own <see cref="FlipperRpcClient"/> so it can supply
/// custom <see cref="FlipperRpcClientOptions"/> and a capturing sink.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LogStreamTests"
/// </summary>
[Collection(DiagnosticsCollection.Name)]
public sealed class LogStreamTests
{
    private readonly string _portName;

    public LogStreamTests()
    {
        _portName = Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)
            ?? string.Empty;
    }

    /// <summary>
    /// A capturing <see cref="IRpcDiagnostics"/> used as an injected sink in tests
    /// that need their own <see cref="FlipperRpcClient"/> instance.
    /// </summary>
    private sealed class CapturingSink : IRpcDiagnostics
    {
        private readonly List<RpcLogEntry> _entries = [];
        public IReadOnlyList<RpcLogEntry> Entries => _entries;
        public void Log(RpcLogEntry entry) => _entries.Add(entry);
    }

    // -------------------------------------------------------------------------
    // Client-side diagnostics (IRpcDiagnostics / RpcLogEntry)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A ping round-trip must produce at least one <see cref="RpcLogKind.CommandSent"/>
    /// and one <see cref="RpcLogKind.ResponseReceived"/> entry via the injected
    /// <see cref="IRpcDiagnostics"/> sink, with the expected field values
    /// and a positive round-trip time.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_PingProducesExpectedEntries()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName), diagnostics: sink);
        await client.ConnectAsync();

        await client.PingAsync();

        var sent = sink.Entries.FirstOrDefault(e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });

        // Match the response to its command by RequestId, not just Status == "ok"
        // (every successful response has Status "ok", including daemon_info/configure).
        var received = sink.Entries.FirstOrDefault(e =>
            e.Kind == RpcLogKind.ResponseReceived && e.RequestId == sent.RequestId);

        // CommandSent entry
        Assert.Equal(RpcLogSource.Client, sent.Source);
        Assert.NotNull(sent.RawJson);
        Assert.Null(sent.RoundTrip);

        // ResponseReceived entry
        Assert.Equal(RpcLogSource.Client, received.Source);
        Assert.NotNull(received.RawJson);
        Assert.Equal("ok", received.Status);
        Assert.NotNull(received.RoundTrip);
        Assert.True(received.RoundTrip!.Value > TimeSpan.Zero, "RoundTrip must be positive");

        // Confirm the IDs match (documents the pairing intent)
        Assert.Equal(sent.RequestId, received.RequestId);
    }

    /// <summary>
    /// A client created without a diagnostics sink (no-op path) must still execute
    /// commands successfully — verifies the JIT-eliminated logging path does not fault.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_NullSink_CommandSucceeds()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        // Construct without diagnostics — uses the no-op NullDiagnostics singleton.
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        // Must not throw; the null-sink path is exercised.
        await client.PingAsync();
    }

    /// <summary>
    /// The injected sink receives entries for every command sent during the
    /// lifetime of the client, covering multiple sequential commands.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Diagnostics_MultipleCommands_AllEntriesCaptured()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName), diagnostics: sink);
        await client.ConnectAsync();

        await client.PingAsync();
        await client.PingAsync();

        var sentCount = sink.Entries.Count(e =>
            e is { Kind: RpcLogKind.CommandSent, CommandName: "ping" });

        Assert.Equal(2, sentCount);
    }

    // -------------------------------------------------------------------------
    // Daemon-side metrics ("_m") — requires DaemonDiagnostics = true
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sending <c>"dx":true</c> via <see cref="ConfigureCommand"/> causes the daemon
    /// to echo <c>"dx":true</c> in the configure response, confirming that the
    /// per-request timing metrics toggle is accepted and stored.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonDiagnostics_ConfigureEnablesDx_ResponseEchoesDxTrue()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        await using var client = new FlipperRpcClient(
            new SerialPortTransport(_portName),
            new FlipperRpcClientOptions { DaemonDiagnostics = true });
        await client.ConnectAsync();

        // ConnectAsync already sent configure with dx:true during negotiation.
        // Send a second explicit configure to read back the effective dx value.
        var response = await client.SendAsync<ConfigureCommand, ConfigureResponse>(
            new ConfigureCommand(heartbeatMs: 3000, timeoutMs: 10000, diagnostics: true));

        Assert.True(response.Diagnostics,
            "Expected daemon to echo \"dx\":true in configure response " +
            "after metrics were enabled.");
    }

    /// <summary>
    /// When daemon diagnostics are enabled (via <see cref="FlipperRpcClientOptions.DaemonDiagnostics"/>),
    /// every <c>"t":0</c> response returned by the daemon includes a <c>"_m"</c> timing
    /// object containing the five metric keys.  This verifies that the raw JSON is
    /// preserved verbatim in <see cref="RpcLogEntry.RawJson"/> so callers can inspect it.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonDiagnostics_Enabled_ResponsesContainMetricsInRawJson()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(
            new SerialPortTransport(_portName),
            new FlipperRpcClientOptions { DaemonDiagnostics = true },
            sink);
        await client.ConnectAsync();

        await client.PingAsync();

        // Every ResponseReceived entry for a real command should carry "_m".
        // Filter to ping (not the negotiation commands) for a clean assertion.
        var pingResponse = sink.Entries.Last(e => e.Kind == RpcLogKind.ResponseReceived);

        Assert.NotNull(pingResponse.RawJson);
        Assert.Contains("\"_m\":", pingResponse.RawJson, StringComparison.Ordinal);
        Assert.Contains("\"tt\":", pingResponse.RawJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// With default options (no <c>"dx"</c>), daemon responses must NOT contain
    /// a <c>"_m"</c> timing object — confirming the opt-in semantics and that
    /// disabled metrics add zero bytes to the wire.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonDiagnostics_Disabled_ResponsesOmitMetrics()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        // Default options: DaemonDiagnostics = false (the default).
        await using var client = new FlipperRpcClient(
            new SerialPortTransport(_portName),
            diagnostics: sink);
        await client.ConnectAsync();

        await client.PingAsync();

        var pingResponse = sink.Entries.Last(e => e.Kind == RpcLogKind.ResponseReceived);

        Assert.NotNull(pingResponse.RawJson);
        Assert.DoesNotContain("\"_m\":", pingResponse.RawJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// After enabling daemon diagnostics, the metric values in <c>"_m"</c> must be
    /// internally consistent: all sub-metrics are non-negative and the total
    /// (<c>"tt"</c>) is at least as large as each individual phase metric.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonDiagnostics_Enabled_MetricValuesAreReasonable()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(
            new SerialPortTransport(_portName),
            new FlipperRpcClientOptions { DaemonDiagnostics = true },
            sink);
        await client.ConnectAsync();

        await client.PingAsync();

        var pingResponse = sink.Entries.Last(e => e.Kind == RpcLogKind.ResponseReceived);

        Assert.NotNull(pingResponse.RawJson);
        Assert.Contains("\"_m\":", pingResponse.RawJson, StringComparison.Ordinal);

        // Parse the raw JSON to extract and validate individual metric values.
        using var doc = JsonDocument.Parse(pingResponse.RawJson!);
        var m = doc.RootElement.GetProperty("_m");

        uint pr = m.GetProperty("pr").GetUInt32();
        uint dp = m.GetProperty("dp").GetUInt32();
        uint ex = m.GetProperty("ex").GetUInt32();
        uint sr = m.GetProperty("sr").GetUInt32();
        uint tt = m.GetProperty("tt").GetUInt32();

        // Total must be >= each individual phase (each phase is a subset of total).
        Assert.True(tt >= pr, $"tt ({tt}) must be >= pr ({pr})");
        Assert.True(tt >= dp, $"tt ({tt}) must be >= dp ({dp})");
        Assert.True(tt >= ex, $"tt ({tt}) must be >= ex ({ex})");
        Assert.True(tt >= sr, $"tt ({tt}) must be >= sr ({sr})");
    }

    /// <summary>
    /// A slow command (device info) with daemon diagnostics enabled must produce
    /// non-zero metric values in <c>"_m"</c>, proving the timing instrumentation
    /// captures real work rather than always returning zero.
    ///
    /// <c>DeviceInfoAsync()</c> reads OTP flash and hardware registers and builds
    /// a large JSON response (~1.5 KB), so at minimum <c>ex</c> (execute) and
    /// <c>tt</c> (total) must be &gt; 0. No SD card or external hardware required.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonDiagnostics_Enabled_SlowCommand_MetricValuesAreNonZero()
    {
        Skip.If(string.IsNullOrEmpty(_portName));

        var sink = new CapturingSink();
        await using var client = new FlipperRpcClient(
            new SerialPortTransport(_portName),
            new FlipperRpcClientOptions { DaemonDiagnostics = true },
            sink);
        await client.ConnectAsync();

        // DeviceInfoAsync() reads OTP flash + hardware registers and builds a large
        // JSON response — consistently >1 ms with no external hardware dependency.
        await client.DeviceInfoAsync();

        var response = sink.Entries.Last(e => e.Kind == RpcLogKind.ResponseReceived);

        Assert.NotNull(response.RawJson);
        Assert.Contains("\"_m\":", response.RawJson, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(response.RawJson!);
        var m = doc.RootElement.GetProperty("_m");

        uint ex = m.GetProperty("ex").GetUInt32();
        uint tt = m.GetProperty("tt").GetUInt32();

        // The execute phase captures hardware register reads and JSON serialization
        // — must be non-zero for a command this substantial.
        Assert.True(ex > 0, $"ex ({ex} ms) must be > 0 for a device_info command");
        // Total must also be non-zero and at least as large as execute.
        Assert.True(tt > 0, $"tt ({tt} ms) must be > 0 for a device_info command");
        Assert.True(tt >= ex, $"tt ({tt}) must be >= ex ({ex})");
    }
}
