using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Returns comprehensive device information including identity, firmware version,
/// hardware OTP fields, and regulatory IDs.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"device_info"}</code>
///
/// Wire format (response, abbreviated):
/// <code>{"t":0,"i":N,"p":{"name":"Flipper","firmware":"1.0","uid":"AABBCCDD",...}}</code>
/// </summary>
public readonly struct DeviceInfoCommand : IRpcCommand<DeviceInfoResponse>
{
    /// <inheritdoc />
    public string CommandName => "device_info";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="DeviceInfoCommand"/>.</summary>
public readonly struct DeviceInfoResponse : IRpcCommandResponse
{
    // ---- Identity ----

    /// <summary>Device name as set in settings.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Model string, e.g. <c>"Flipper Zero"</c>.</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }

    /// <summary>Short model code, e.g. <c>"F7"</c>.</summary>
    [JsonPropertyName("model_code")] public string? ModelCode { get; init; }

    // ---- Firmware ----

    /// <summary>Firmware version string.</summary>
    [JsonPropertyName("firmware")] public string? Firmware { get; init; }

    /// <summary>Firmware origin (e.g. <c>"Official"</c> or <c>"Custom"</c>).</summary>
    [JsonPropertyName("firmware_origin")] public string? FirmwareOrigin { get; init; }

    /// <summary>Build date string.</summary>
    [JsonPropertyName("build_date")] public string? BuildDate { get; init; }

    /// <summary>Short git commit hash at build time.</summary>
    [JsonPropertyName("git_hash")] public string? GitHash { get; init; }

    /// <summary>Git branch at build time.</summary>
    [JsonPropertyName("git_branch")] public string? GitBranch { get; init; }

    /// <summary>Branch number string.</summary>
    [JsonPropertyName("git_branch_num")] public string? GitBranchNum { get; init; }

    /// <summary>Git remote origin URL at build time.</summary>
    [JsonPropertyName("git_origin")] public string? GitOrigin { get; init; }

    /// <summary><c>true</c> if the firmware was built with uncommitted changes.</summary>
    [JsonPropertyName("dirty")] public bool Dirty { get; init; }

    // ---- Hardware OTP ----

    /// <summary>Hardware revision number.</summary>
    [JsonPropertyName("hardware")] public uint Hardware { get; init; }

    /// <summary>Hardware target code.</summary>
    [JsonPropertyName("hw_target")] public uint HwTarget { get; init; }

    /// <summary>Hardware body variant.</summary>
    [JsonPropertyName("hw_body")] public uint HwBody { get; init; }

    /// <summary>Hardware color variant.</summary>
    [JsonPropertyName("hw_color")] public uint HwColor { get; init; }

    /// <summary>Hardware connector type.</summary>
    [JsonPropertyName("hw_connect")] public uint HwConnect { get; init; }

    /// <summary>Display type code.</summary>
    [JsonPropertyName("hw_display")] public uint HwDisplay { get; init; }

    /// <summary>RF region code burned into OTP.</summary>
    [JsonPropertyName("hw_region")] public uint HwRegion { get; init; }

    /// <summary>Human-readable RF region name.</summary>
    [JsonPropertyName("hw_region_name")] public string? HwRegionName { get; init; }

    /// <summary>Manufacture timestamp (Unix seconds).</summary>
    [JsonPropertyName("hw_timestamp")] public uint HwTimestamp { get; init; }

    /// <summary>
    /// Manufacture date derived from <see cref="HwTimestamp"/>.
    /// Returns <see cref="DateTimeOffset.MinValue"/> when <see cref="HwTimestamp"/> is 0.
    /// </summary>
    public DateTimeOffset ManufactureDate =>
        HwTimestamp == 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(HwTimestamp);

    // ---- Unique identifiers ----

    /// <summary>Device unique ID as an uppercase hex string.</summary>
    [JsonPropertyName("uid")] public string? Uid { get; init; }

    /// <summary>BLE MAC address as a colon-separated hex string.</summary>
    [JsonPropertyName("ble_mac")] public string? BleMac { get; init; }

    // ---- Regulatory ----

    /// <summary>FCC certification ID.</summary>
    [JsonPropertyName("fcc_id")] public string? FccId { get; init; }

    /// <summary>Industry Canada certification ID.</summary>
    [JsonPropertyName("ic_id")] public string? IcId { get; init; }

    /// <summary>Japan MIC certification ID.</summary>
    [JsonPropertyName("mic_id")] public string? MicId { get; init; }

    /// <summary>China SRRC certification ID.</summary>
    [JsonPropertyName("srrc_id")] public string? SrrcId { get; init; }

    /// <summary>Taiwan NCC certification ID.</summary>
    [JsonPropertyName("ncc_id")] public string? NccId { get; init; }
}
