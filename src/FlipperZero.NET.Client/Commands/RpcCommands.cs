using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands;

// ---------------------------------------------------------------------------
// Ping
// ---------------------------------------------------------------------------

/// <summary>Sends a <c>ping</c> command and returns a <see cref="PingResponse"/>.</summary>
public readonly struct PingCommand : IRpcCommand<PingResponse>
{
    public string CommandName => "ping";

    /// <summary>Ping has no arguments.</summary>
    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="PingCommand"/>.</summary>
public readonly struct PingResponse : IRpcCommandResponse
{
    /// <summary>Always <c>true</c> when the Flipper responds to a ping.</summary>
    [JsonPropertyName("pong")]
    public bool Pong { get; init; }
}

// ---------------------------------------------------------------------------
// IR receive start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens an IR receive stream.  Each decoded IR signal is delivered as an
/// <see cref="IrReceiveEvent"/>.
/// </summary>
public readonly struct IrReceiveStartCommand : IRpcStreamCommand<IrReceiveEvent>
{
    public string CommandName => "ir_receive_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer) { }
}

/// <summary>A decoded IR signal received from the IR receiver.</summary>
public readonly struct IrReceiveEvent : IRpcCommandResponse
{
    /// <summary>Protocol name, e.g. <c>"NEC"</c> or <c>"Samsung32"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }

    /// <summary>Device address field from the decoded IR frame.</summary>
    [JsonPropertyName("address")]
    public uint Address { get; init; }

    /// <summary>Command field from the decoded IR frame.</summary>
    [JsonPropertyName("command")]
    public uint Command { get; init; }

    /// <summary><c>true</c> if this is a repeat frame (button held down).</summary>
    [JsonPropertyName("repeat")]
    public bool Repeat { get; init; }
}

// ---------------------------------------------------------------------------
// GPIO watch start
// ---------------------------------------------------------------------------

/// <summary>
/// Watches a GPIO pin for level changes.  Each transition is delivered as a
/// <see cref="GpioWatchEvent"/>.
/// </summary>
public readonly struct GpioWatchStartCommand : IRpcStreamCommand<GpioWatchEvent>
{
    /// <param name="pin">
    /// Physical GPIO header pin label, e.g. <c>"1"</c> through <c>"8"</c>.
    /// Maps to the <c>gpio_ext_*</c> symbols on the Flipper Zero expansion connector.
    /// </param>
    public GpioWatchStartCommand(string pin) => Pin = pin;

    /// <summary>Pin label as sent in the <c>"pin"</c> JSON field.</summary>
    public string Pin { get; }

    public string CommandName => "gpio_watch_start";

    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer)
    {
        writer.WriteString("pin", Pin);
    }
}

/// <summary>A GPIO level-change event.</summary>
public readonly struct GpioWatchEvent : IRpcCommandResponse
{
    /// <summary>Pin label that changed, e.g. <c>"1"</c>.</summary>
    [JsonPropertyName("pin")]
    public string? Pin { get; init; }

    /// <summary><c>true</c> = high; <c>false</c> = low.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }
}

// ---------------------------------------------------------------------------
// SubGHz RX start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a Sub-GHz OOK receive stream.  Each raw pulse is delivered as a
/// <see cref="SubGhzRxEvent"/>.
/// </summary>
public readonly struct SubGhzRxStartCommand : IRpcStreamCommand<SubGhzRxEvent>
{
    /// <param name="freq">
    /// Optional carrier frequency in Hz (e.g. <c>433920000</c>).
    /// Defaults to 433.92 MHz when <c>null</c>.
    /// </param>
    public SubGhzRxStartCommand(uint? freq = null) => Freq = freq;

    /// <summary>Carrier frequency in Hz, or <c>null</c> to use the default (433.92 MHz).</summary>
    public uint? Freq { get; }

    public string CommandName => "subghz_rx_start";

    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer)
    {
        if(Freq.HasValue)
            writer.WriteNumber("freq", Freq.Value);
    }
}

/// <summary>A raw Sub-GHz OOK pulse.</summary>
public readonly struct SubGhzRxEvent : IRpcCommandResponse
{
    /// <summary><c>true</c> = carrier on; <c>false</c> = carrier off.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }

    /// <summary>Pulse duration in microseconds.</summary>
    [JsonPropertyName("duration_us")]
    public uint DurationUs { get; init; }
}

// ---------------------------------------------------------------------------
// NFC scan start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens an NFC scanner stream.  Each detected NFC tag protocol is delivered
/// as an <see cref="NfcScanEvent"/>.
/// Note: <c>NfcScanner</c> performs protocol detection only — no UID is
/// available without running a full anti-collision poller.
/// </summary>
public readonly struct NfcScanStartCommand : IRpcStreamCommand<NfcScanEvent>
{
    public string CommandName => "nfc_scan_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer) { }
}

/// <summary>An NFC tag protocol detection event.</summary>
public readonly struct NfcScanEvent : IRpcCommandResponse
{
    /// <summary>Detected protocol name, e.g. <c>"Iso14443-3a"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }
}

// ---------------------------------------------------------------------------
// Stream close
// ---------------------------------------------------------------------------

/// <summary>
/// Closes an open stream identified by <see cref="StreamId"/>.
/// Called automatically by <see cref="RpcStream{TEvent}.DisposeAsync"/>.
/// </summary>
public readonly struct StreamCloseCommand : IRpcCommand<StreamCloseResponse>
{
    public StreamCloseCommand(uint streamId) => StreamId = streamId;

    /// <summary>The stream id to close (maps to <c>"stream"</c> in JSON).</summary>
    public uint StreamId { get; }

    public string CommandName => "stream_close";

    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer)
    {
        writer.WriteNumber("stream", StreamId);
    }
}

/// <summary>Response to <see cref="StreamCloseCommand"/>.</summary>
public readonly struct StreamCloseResponse : IRpcCommandResponse { }
