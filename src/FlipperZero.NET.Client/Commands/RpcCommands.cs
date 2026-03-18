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
// BLE scan start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a BLE scan stream.  Each scan result is delivered as a
/// <see cref="BleScanEvent"/>.
/// </summary>
public readonly struct BleScanStartCommand : IRpcStreamCommand<BleScanEvent>
{
    public string CommandName => "ble_scan_start";

    /// <summary>No arguments needed to start a scan.</summary>
    public void WriteArgs(System.Text.Json.Utf8JsonWriter writer) { }
}

/// <summary>A single BLE advertisement received during a scan.</summary>
public readonly struct BleScanEvent : IRpcCommandResponse
{
    /// <summary>Bluetooth device address, e.g. <c>"AA:BB:CC:DD:EE:FF"</c>.</summary>
    [JsonPropertyName("addr")]
    public string? Address { get; init; }

    /// <summary>Received signal strength in dBm.</summary>
    [JsonPropertyName("rssi")]
    public int Rssi { get; init; }

    /// <summary>Device name from the advertising packet, if present.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
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
