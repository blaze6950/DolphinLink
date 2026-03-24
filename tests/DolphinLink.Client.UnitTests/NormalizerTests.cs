using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Extensions;

namespace DolphinLink.Client.UnitTests;

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
        """{"t":0,"i":1,"p":{"n":"dolphin_link_rpc_daemon","v":1,"cmds":["ping","daemon_info"]}}""";

    private readonly FakeTransport _transport = new();
    private readonly CapturingSink _sink = new();
    private readonly RpcClient _client;

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
        // "k":4 = Ok (InputKey), "ty":2 = Short (InputType).
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

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RpcJsonNormalizer.Normalize(string.Empty));
    }

    [Fact]
    public void Request_DaemonInfo_ExpandsCommandName()
    {
        // Command ID 3 = daemon_info; no payload.
        var result = RpcJsonNormalizer.Normalize("""{"c":3,"i":5}""");
        Assert.Equal("""{"command":"daemon_info","id":5}""", result);
    }

    [Fact]
    public void Request_StreamClose_ExpandsStreamField()
    {
        // Command ID 1 = stream_close; "s" → "stream".
        var result = RpcJsonNormalizer.Normalize("""{"c":1,"i":9,"s":3}""");
        Assert.Equal("""{"command":"stream_close","id":9,"stream":3}""", result);
    }

    [Fact]
    public void Request_Configure_ExpandsAllFields()
    {
        // Command ID 2 = configure; "hb"→heartbeat_ms, "to"→timeout_ms, "dx"→diagnostics (bool).
        var result = RpcJsonNormalizer.Normalize("""{"c":2,"i":1,"hb":3000,"to":10000,"dx":1}""");
        Assert.Equal("""{"command":"configure","id":1,"heartbeat_ms":3000,"timeout_ms":10000,"diagnostics":true}""", result);
    }

    [Fact]
    public void Request_GpioSet5v_ExpandsBoolEnable()
    {
        // Command ID 15 = gpio_set_5v; "en"→enable (bool).
        var result = RpcJsonNormalizer.Normalize("""{"c":15,"i":1,"en":0}""");
        Assert.Equal("""{"command":"gpio_set_5v","id":1,"enable":false}""", result);
    }

    [Fact]
    public void Request_Vibro_ExpandsBoolEnable()
    {
        // Command ID 26 = vibro; "en"→enable (bool).
        var result = RpcJsonNormalizer.Normalize("""{"c":26,"i":1,"en":1}""");
        Assert.Equal("""{"command":"vibro","id":1,"enable":true}""", result);
    }

    [Fact]
    public void Request_IrTx_ExpandsStringFields()
    {
        // Command ID 17 = ir_tx; "pr"→protocol, "a"→address, "cm"→command.
        var result = RpcJsonNormalizer.Normalize("""{"c":17,"i":1,"pr":"NEC","a":1,"cm":5}""");
        Assert.Equal("""{"command":"ir_tx","id":1,"protocol":"NEC","address":1,"command":5}""", result);
    }

    [Fact]
    public void Request_LedSetRgb_ExpandsColorFields()
    {
        // Command ID 25 = led_set_rgb; "r"→red, "g"→green, "b"→blue.
        var result = RpcJsonNormalizer.Normalize("""{"c":25,"i":1,"r":81,"g":43,"b":212}""");
        Assert.Equal("""{"command":"led_set_rgb","id":1,"red":81,"green":43,"blue":212}""", result);
    }

    [Fact]
    public void Request_StorageWrite_ExpandsPathAndData()
    {
        // Command ID 33 = storage_write; "p"→path (string), "d"→data.
        var result = RpcJsonNormalizer.Normalize("""{"c":33,"i":1,"p":"/ext/test.fap","d":"AAEC"}""");
        Assert.Equal("""{"command":"storage_write","id":1,"path":"/ext/test.fap","data":"AAEC"}""", result);
    }

    [Fact]
    public void Request_InputListenStart_ExpandsExitKeyEnum()
    {
        // Command ID 39 = input_listen_start; "ek"=exit_key (InputKey), "et"=exit_type (InputType).
        var result = RpcJsonNormalizer.Normalize("""{"c":39,"i":1,"ek":5,"et":1}""");
        Assert.Equal("""{"command":"input_listen_start","id":1,"exit_key":"Back","exit_type":"Release"}""", result);
    }

    [Fact]
    public void Request_UnknownKey_PassesThroughVerbatim()
    {
        // Unknown payload key "zz" for a known command passes through unchanged.
        var result = RpcJsonNormalizer.Normalize("""{"c":0,"i":1,"zz":99}""");
        Assert.Equal("""{"command":"ping","id":1,"zz":99}""", result);
    }

    [Fact]
    public void Response_DaemonInfo_ExpandsPayloadKeys()
    {
        var result = RpcJsonNormalizer.Normalize(
            """{"t":0,"i":1,"p":{"n":"dolphin_link_rpc_daemon","v":1,"cmds":["ping"]}}""",
            "daemon_info");
        Assert.Contains("\"name\":\"dolphin_link_rpc_daemon\"", result);
        Assert.Contains("\"version\":1", result);
        Assert.Contains("\"commands\":[\"ping\"]", result);
    }

    [Fact]
    public void Response_FrequencyIsAllowed_ExpandsBoolAllowed()
    {
        // "al" → "allowed" (bool)
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"al":1}}""", "frequency_is_allowed");
        Assert.Contains("\"allowed\":true", result);
    }

    [Fact]
    public void Response_GpioRead_ExpandsBoolLevel()
    {
        // "lv" → "level" (bool)
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"lv":0}}""", "gpio_read");
        Assert.Contains("\"level\":false", result);
    }

    [Fact]
    public void Response_StorageStat_ExpandsBoolIsDir()
    {
        // "d" → "is_dir" (bool), "sz" → "size"
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"d":1,"sz":1024}}""", "storage_stat");
        Assert.Contains("\"is_dir\":true", result);
        Assert.Contains("\"size\":1024", result);
    }

    [Fact]
    public void Response_StorageInfo_ExpandsAllFields()
    {
        var result = RpcJsonNormalizer.Normalize(
            """{"t":0,"i":1,"p":{"p":"/ext","tk":65536,"fk":32768}}""",
            "storage_info");
        Assert.Contains("\"path\":\"/ext\"", result);
        Assert.Contains("\"total_kb\":65536", result);
        Assert.Contains("\"free_kb\":32768", result);
    }

    [Fact]
    public void Response_PowerInfo_ExpandsChargingBool()
    {
        // "cg" → "charging" (bool), "ch" → "charge", "mv" → "voltage_mv", "ma" → "current_ma"
        var result = RpcJsonNormalizer.Normalize(
            """{"t":0,"i":1,"p":{"cg":1,"ch":85,"mv":4100,"ma":120}}""",
            "power_info");
        Assert.Contains("\"charging\":true", result);
        Assert.Contains("\"charge\":85", result);
        Assert.Contains("\"voltage_mv\":4100", result);
        Assert.Contains("\"current_ma\":120", result);
    }

    [Fact]
    public void Event_GpioWatchStart_ExpandsPinEnumAndLevelBool()
    {
        // Stream event for gpio_watch_start: "p"=pin (enum), "lv"=level (bool).
        var result = RpcJsonNormalizer.Normalize(
            """{"t":1,"i":5,"p":{"p":3,"lv":1}}""",
            "gpio_watch_start");
        Assert.Contains("\"pin\":\"Pin3\"", result);
        Assert.Contains("\"level\":true", result);
        Assert.Contains("\"type\":\"event\"", result);
        Assert.Contains("\"stream_id\":5", result);
    }

    [Fact]
    public void Event_SubghzRxStart_ExpandsLevelBool()
    {
        // "lv" → "level" (bool), "du" → "duration_us"
        var result = RpcJsonNormalizer.Normalize(
            """{"t":1,"i":2,"p":{"lv":0,"du":450}}""",
            "subghz_rx_start");
        Assert.Contains("\"level\":false", result);
        Assert.Contains("\"duration_us\":450", result);
    }

    [Fact]
    public void Response_StreamOpen_WithCommandName_StillExpandsSToStreamId()
    {
        // Even with a commandName, "s" in the payload with no matching response key → stream_id.
        // (No command has a response key named "s" that maps to something else.)
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"s":9}}""", "gpio_watch_start");
        Assert.Contains("\"stream_id\":9", result);
    }

    [Fact]
    public void Response_UnknownPayloadKey_PassesThroughVerbatim()
    {
        // Unknown key "zz" in payload of a known command passes through unchanged.
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":1,"p":{"zz":42}}""", "ping");
        Assert.Contains("\"zz\":42", result);
    }

    [Fact]
    public void Request_NotJsonObject_ReturnsOriginal()
    {
        const string notObj = "[1,2,3]";
        Assert.Equal(notObj, RpcJsonNormalizer.Normalize(notObj));
    }

    [Fact]
    public void Response_Error_WithNullPayload_HandledGracefully()
    {
        // Error envelope with string "e" field and no "p".
        var result = RpcJsonNormalizer.Normalize("""{"t":0,"i":3,"e":"resource_busy"}""");
        Assert.Equal("""{"type":"response","id":3,"error":"resource_busy"}""", result);
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
