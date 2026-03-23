using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests for <see cref="RpcJsonNormalizer.Normalize"/>.
///
/// Pure unit tests (no transport) exercise the normalizer directly.
/// Integration tests use <see cref="FakeTransport"/> to verify that
/// <see cref="RpcLogEntry.RawJson"/> captured by the diagnostics pipeline
/// normalizes correctly through the full client stack.
/// </summary>
public sealed class NormalizerTests : IAsyncLifetime, IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Infrastructure (for integration tests)
    // -------------------------------------------------------------------------

    private sealed class CapturingSink : IRpcDiagnostics
    {
        private readonly List<RpcLogEntry> _entries = new();
        public IReadOnlyList<RpcLogEntry> Entries { get { lock (_entries) { return _entries.ToList(); } } }
        public void Log(RpcLogEntry entry) { lock (_entries) { _entries.Add(entry); } }
    }

    // daemon_info response — does NOT include "configure", so ConnectAsync sends
    // exactly one command (daemon_info, id:1). Post-connect commands start at id:2.
    private const string DaemonInfoResponse =
        """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping","daemon_info"]}}""";

    private readonly FakeTransport _transport = new();
    private readonly CapturingSink _sink = new();
    private readonly FlipperRpcClient _client;

    public NormalizerTests()
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
    // Pure unit tests — no transport required
    // -------------------------------------------------------------------------

    [Fact]
    public void Request_Ping_ExpandsCommandAndId()
    {
        // Command ID 0 = ping; request has no payload fields.
        var result = RpcJsonNormalizer.Normalize("""{"c":0,"i":1}""");
        Assert.Equal("""{"command":"ping","id":1}""", result);
    }

    [Fact]
    public void Request_GpioRead_ExpandsPinEnum()
    {
        // Command ID 12 = gpio_read; "p" is pin (GpioPin enum); wire value 6 = Pin6.
        var result = RpcJsonNormalizer.Normalize("""{"c":12,"i":1,"p":6}""");
        Assert.Equal("""{"command":"gpio_read","id":1,"pin":"Pin6"}""", result);
    }

    [Fact]
    public void Request_GpioWrite_ExpandsBoolAndEnum()
    {
        // Command ID 13 = gpio_write; "p"=pin (enum), "lv"=level (bool).
        var result = RpcJsonNormalizer.Normalize("""{"c":13,"i":2,"p":3,"lv":1}""");
        Assert.Equal("""{"command":"gpio_write","id":2,"pin":"Pin3","level":true}""", result);
    }

    [Fact]
    public void Response_VoidOk_ExpandsTypeAndId()
    {
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1}""");
        Assert.Equal("""{"type":"response","id":1}""", result);
    }

    [Fact]
    public void Response_Payload_NoCommandName_PayloadKeysNotExpanded()
    {
        // Without a commandName, payload keys and values are NOT expanded (only envelope keys are).
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"pg":1}}""");
        Assert.Equal("""{"type":"response","id":1,"payload":{"pg":1}}""", result);
    }

    [Fact]
    public void Response_Payload_WithCommandName_ExpandsPayloadKeys()
    {
        // "pg" is the wire key for "pong" in the ping response; it's a bool field.
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"pg":1}}""", "ping");
        Assert.Equal("""{"type":"response","id":1,"payload":{"pong":true}}""", result);
    }

    [Fact]
    public void Response_Error_ExpandsEnvelopeKeys()
    {
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"e":"missing_pin"}""");
        Assert.Equal("""{"type":"response","id":1,"error":"missing_pin"}""", result);
    }

    [Fact]
    public void Response_StreamOpen_ExpandsSToStreamId()
    {
        // Stream-open response: "p":{"s":7} — "s" with no command context → "stream_id".
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"s":7}}""");
        Assert.Equal("""{"type":"response","id":1,"payload":{"stream_id":7}}""", result);
    }

    [Fact]
    public void Event_IrReceive_ExpandsKeysAndBool()
    {
        // "t":1 → event, "i" → stream_id; payload: "pr"=protocol (string, passes through),
        // "a"=address, "cm"=command, "rp"=repeat (bool).
        var result = RpcJsonNormalizer.Normalize(
            """{"t":1,"i":7,"p":{"pr":"NEC","a":0,"cm":5,"rp":1}}""",
            "ir_receive_start");
        Assert.Equal(
            """{"type":"event","stream_id":7,"payload":{"protocol":"NEC","address":0,"command":5,"repeat":true}}""",
            result);
    }

    [Fact]
    public void Event_InputListen_ExpandsIntEnums()
    {
        // "k":4 = Ok (FlipperInputKey), "ty":2 = Short (FlipperInputType).
        var result = RpcJsonNormalizer.Normalize(
            """{"t":1,"i":3,"p":{"k":4,"ty":2}}""",
            "input_listen_start");
        Assert.Equal(
            """{"type":"event","stream_id":3,"payload":{"key":"Ok","type":"Short"}}""",
            result);
    }

    [Fact]
    public void Disconnect_ExpandsType()
    {
        var result = RpcJsonNormalizer.Normalize("""{"t":2}""");
        Assert.Equal("""{"type":"disconnect"}""", result);
    }

    [Fact]
    public void Response_Metrics_ExpandsSubkeys()
    {
        // "_m" sub-keys: pr→parse, dp→dispatch, ex→execute, sr→serialize, tt→total.
        var result = RpcJsonNormalizer.Normalize(
            """{"t":0,"i":2,"p":{"pg":1},"_m":{"pr":0,"dp":0,"ex":1,"sr":0,"tt":1}}""",
            "ping");
        Assert.Contains("\"parse\":0", result);
        Assert.Contains("\"dispatch\":0", result);
        Assert.Contains("\"execute\":1", result);
        Assert.Contains("\"serialize\":0", result);
        Assert.Contains("\"total\":1", result);
        Assert.Contains("\"pong\":true", result);
        Assert.Contains("\"metrics\":{", result);
    }

    [Fact]
    public void Request_StorageInfo_PathNotPin()
    {
        // Command ID 30 = storage_info; "p" wire key = "path" (string), not "pin" (GpioPin enum).
        var result = RpcJsonNormalizer.Normalize("""{"c":30,"i":1,"p":"/ext"}""");
        Assert.Equal("""{"command":"storage_info","id":1,"path":"/ext"}""", result);
    }

    [Fact]
    public void UnknownCommandId_PassesThroughAsInteger()
    {
        // No registry entry for ID 999 — emit raw integer, not a quoted string.
        var result = RpcJsonNormalizer.Normalize("""{"c":999,"i":1}""");
        Assert.Equal("""{"command":999,"id":1}""", result);
    }

    [Fact]
    public void MalformedJson_ReturnsOriginal()
    {
        const string bad = "not json";
        Assert.Equal(bad, RpcJsonNormalizer.Normalize(bad));
    }

    [Fact]
    public void NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RpcJsonNormalizer.Normalize(null));
    }

    // -------------------------------------------------------------------------
    // Integration tests — FakeTransport roundtrip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Integration_Ping_CommandSentNormalizes()
    {
        // Arrange: id:2 is the first post-connect command.
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.PingAsync();

        // Assert: CommandSent RawJson normalizes to human-readable form.
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.CommandSent && e.RequestId == 2u);
        var normalized = RpcJsonNormalizer.Normalize(entry.RawJson, entry.CommandName);
        Assert.Contains("\"command\":\"ping\"", normalized);
        Assert.Contains("\"id\":2", normalized);
        Assert.DoesNotContain("\"c\":", normalized);
    }

    [Fact]
    public async Task Integration_Ping_ResponseReceivedNormalizes()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.PingAsync();

        // Assert: ResponseReceived RawJson normalizes correctly when commandName is supplied.
        var entry = _sink.Entries.Single(e => e.Kind == RpcLogKind.ResponseReceived && e.RequestId == 2u);
        var normalized = RpcJsonNormalizer.Normalize(entry.RawJson, "ping");
        Assert.Contains("\"pong\":true", normalized);
        Assert.Contains("\"type\":\"response\"", normalized);
        Assert.DoesNotContain("\"pg\":", normalized);
    }
}
