using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Exceptions;
using DolphinLink.Client.Commands.Core;
using DolphinLink.Client.Extensions;

namespace DolphinLink.Client.UnitTests;

/// <summary>
/// End-to-end unit tests for the client-side diagnostics pipeline.
///
/// These tests wire a <see cref="FakeTransport"/> together with a capturing
/// <see cref="IRpcDiagnostics"/> sink into a real <see cref="RpcClient"/>
/// so that both <c>CommandSent</c> (emitted by the client on send) and
/// <c>ResponseReceived</c> (emitted by the dispatcher on receive) entries are
/// exercised through the full stack — without requiring hardware.
///
/// Daemon-side metrics (<c>"_m"</c> JSON field) are produced by the C daemon
/// when diagnostics are enabled via <c>"dx":true</c> in the configure command.
/// These tests verify that when the daemon includes <c>"_m"</c> in a response,
/// the raw JSON is preserved verbatim in <see cref="RpcLogEntry.RawJson"/> so
/// callers can inspect it, even though no structured property parses the field.
/// </summary>
public sealed class DiagnosticsTests : IAsyncLifetime, IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    /// <summary>Capturing sink for all <see cref="RpcLogEntry"/> records.</summary>
    private sealed class CapturingSink : IRpcDiagnostics
    {
        private readonly List<RpcLogEntry> _entries = new();
        public IReadOnlyList<RpcLogEntry> Entries { get { lock (_entries) { return _entries.ToList(); } } }
        public void Log(RpcLogEntry entry) { lock (_entries) { _entries.Add(entry); } }
    }

    // daemon_info response — does NOT include "configure", so ConnectAsync sends
    // exactly one command (daemon_info, id:1). Post-connect commands start at id:2.
    private const string DaemonInfoResponse =
        """{"t":0,"i":1,"p":{"n":"dolphin_link_rpc_daemon","v":1,"cmds":["ping","daemon_info"]}}""";

    // daemon_info response that also advertises "configure" support.
    private const string DaemonInfoWithConfigureResponse =
        """{"t":0,"i":1,"p":{"n":"dolphin_link_rpc_daemon","v":4,"cmds":["ping","daemon_info","configure"]}}""";

    private const string ConfigureResponse =
        """{"t":0,"i":2,"p":{"hb":3600000,"to":7200000}}""";

    private readonly FakeTransport _transport = new();
    private readonly CapturingSink _sink = new();
    private readonly RpcClient _client;

    public DiagnosticsTests()
    {
        _client = _transport.CreateClient(_sink);
    }

    public async Task InitializeAsync()
    {
        _transport.EnqueueResponse(DaemonInfoResponse);
        await _client.ConnectAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // CommandSent + ResponseReceived pairs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PingCommand_ProducesCommandSentAndResponseReceived()
    {
        // Arrange: id:2 is the first post-connect command
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.PingAsync();

        // Assert: one CommandSent for daemon_info (during ConnectAsync), one for ping;
        // one ResponseReceived for each — filter to post-connect entries only.
        var allEntries = _sink.Entries;
        var pingCmd = allEntries.Single(e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 2u);
        var pingResp = allEntries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);

        Assert.Equal("ping", pingCmd.CommandName);
        Assert.Equal("ok", pingResp.Status);
        Assert.Equal(pingCmd.RequestId, pingResp.RequestId);
    }

    [Fact]
    public async Task CommandSent_HasCorrectRawJson()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.SendAsync<PingCommand, PingResponse>(new PingCommand());

        // Assert: the CommandSent RawJson is the serialised outbound JSON line.
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 2u);
        Assert.NotNull(entry.RawJson);
        Assert.Contains("\"c\":", entry.RawJson);   // command-id field present
        Assert.Contains("\"i\":2", entry.RawJson);  // request id matches
    }

    [Fact]
    public async Task ResponseReceived_HasCorrectRawJson()
    {
        // Arrange: the exact bytes the daemon sent back
        const string daemonLine = """{"t":0,"i":2,"p":{"pg":1}}""";
        _transport.EnqueueResponse(daemonLine);

        // Act
        await _client.PingAsync();

        // Assert: RawJson is the verbatim inbound line from the daemon.
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
        Assert.NotNull(entry.RawJson);
        Assert.Equal(daemonLine, entry.RawJson);
    }

    // -------------------------------------------------------------------------
    // Daemon-side metrics ("_m") visibility in RawJson
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseReceived_RawJson_PreservesMetricsFieldWhenDaemonSendsM()
    {
        // Arrange: a response that includes daemon-side timing metrics.
        // This is what the daemon emits when "dx":true was sent in configure.
        const string daemonLine = """{"t":0,"i":2,"p":{"pg":1},"_m":{"pr":0,"dp":0,"ex":1,"sr":0,"tt":1}}""";
        _transport.EnqueueResponse(daemonLine);

        // Act
        await _client.PingAsync();

        // Assert: _m is preserved verbatim in RawJson even though no structured
        // property parses it. Consumers can inspect RawJson to extract metrics.
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
        Assert.NotNull(entry.RawJson);
        Assert.Equal(daemonLine, entry.RawJson);
        Assert.Contains("\"_m\":", entry.RawJson);
        Assert.Contains("\"tt\":1", entry.RawJson);
    }

    // -------------------------------------------------------------------------
    // Round-trip timing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResponseReceived_HasNonNullRoundTrip()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.PingAsync();

        // Assert
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
        Assert.NotNull(entry.RoundTrip);
        Assert.True(entry.RoundTrip!.Value.Ticks >= 0);
    }

    // -------------------------------------------------------------------------
    // Error responses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ErrorResponse_ProducesResponseReceivedWithErrorStatus()
    {
        // Arrange: the daemon returns an error for the ping command
        _transport.EnqueueResponse("""{"t":0,"i":2,"e":"resource_busy"}""");

        // Act: the exception is expected — we're testing the diagnostics entry
        await Assert.ThrowsAsync<RpcException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand()));

        // Assert: a ResponseReceived entry is still logged with the error status
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
        Assert.Equal("resource_busy", entry.Status);
    }

    // -------------------------------------------------------------------------
    // Negotiation phase logging
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_NegotiationPhase_ProducesDiagnosticsEntries()
    {
        // Arrange: a fresh transport + client (separate from the class fixture)
        // so we can observe the ConnectAsync diagnostics in isolation.
        var transport = new FakeTransport();
        var sink = new CapturingSink();
        await using var client = transport.CreateClient(sink);

        transport.EnqueueResponse(DaemonInfoResponse);

        // Act
        await client.ConnectAsync();

        // Assert: daemon_info command produces a CommandSent (id:1) and a
        // ResponseReceived (id:1) during negotiation.
        var entries = sink.Entries;
        Assert.Contains(entries, e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 1u);
        Assert.Contains(entries, e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 1u);
    }

    [Fact]
    public async Task ConnectAsync_WithConfigure_AllNegotiationCommandsAreLogged()
    {
        // Arrange: daemon advertises "configure" — ConnectAsync sends two commands.
        var transport = new FakeTransport();
        var sink = new CapturingSink();
        await using var client = transport.CreateClient(sink);

        transport.EnqueueResponse(DaemonInfoWithConfigureResponse);
        transport.EnqueueResponse(ConfigureResponse);

        // Act
        await client.ConnectAsync();

        // Assert: two CommandSent entries (daemon_info id:1, configure id:2)
        // and two ResponseReceived entries.
        var entries = sink.Entries;
        Assert.Contains(entries, e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 1u);
        Assert.Contains(entries, e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 1u);
        Assert.Contains(entries, e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 2u);
        Assert.Contains(entries, e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
    }

    // -------------------------------------------------------------------------
    // DaemonDiagnostics option propagated via configure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DaemonDiagnostics_True_SendsDxInConfigureCommand()
    {
        // Arrange: a fresh client with DaemonDiagnostics = true.
        var transport = new FakeTransport();
        var sink = new CapturingSink();
        await using var client = new RpcClient(
            transport,
            new RpcClientOptions
            {
                HeartbeatInterval = TimeSpan.FromHours(1),
                Timeout = TimeSpan.FromHours(2),
                DaemonDiagnostics = true,
            },
            sink);

        // Daemon advertises "configure" so the client sends it during ConnectAsync.
        transport.EnqueueResponse(DaemonInfoWithConfigureResponse);
        transport.EnqueueResponse(ConfigureResponse);

        await client.ConnectAsync();

        // Assert: the CommandSent RawJson for the configure command (id:2) contains
        // "dx":true, confirming the option is propagated on the wire.
        var configureEntry = sink.Entries.Single(
            e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 2u);
        Assert.NotNull(configureEntry.RawJson);
        Assert.Contains("\"dx\":true", configureEntry.RawJson);
    }
}
