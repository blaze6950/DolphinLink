using System.Text.Json.Serialization;
using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Converters;

namespace DolphinLink.Client.Commands.System;

/// <summary>Response to <see cref="DeviceInfoCommand"/>.</summary>
public readonly partial struct DeviceInfoResponse : IRpcCommandResponse
{
    // ---- Identity ----

    /// <summary>Device name as set in settings.</summary>
    [JsonPropertyName("nm")] public string? Name { get; init; }

    /// <summary>Model string, e.g. <c>"Flipper Zero"</c>.</summary>
    [JsonPropertyName("m")] public string? Model { get; init; }

    /// <summary>Short model code, e.g. <c>"F7"</c>.</summary>
    [JsonPropertyName("mc")] public string? ModelCode { get; init; }

    // ---- Firmware ----

    /// <summary>Firmware version string.</summary>
    [JsonPropertyName("fw")] public string? Firmware { get; init; }

    /// <summary>Firmware origin (e.g. <c>"Official"</c> or <c>"Custom"</c>).</summary>
    [JsonPropertyName("fo")] public string? FirmwareOrigin { get; init; }

    /// <summary>Build date string.</summary>
    [JsonPropertyName("bd")] public string? BuildDate { get; init; }

    /// <summary>Short git commit hash at build time.</summary>
    [JsonPropertyName("gh")] public string? GitHash { get; init; }

    /// <summary>Git branch at build time.</summary>
    [JsonPropertyName("gb")] public string? GitBranch { get; init; }

    /// <summary>Branch number string.</summary>
    [JsonPropertyName("gbn")] public string? GitBranchNum { get; init; }

    /// <summary>Git remote origin URL at build time.</summary>
    [JsonPropertyName("go")] public string? GitOrigin { get; init; }

    /// <summary><c>true</c> if the firmware was built with uncommitted changes.</summary>
    [JsonConverter(typeof(NumericBoolJsonConverter))]
    [JsonPropertyName("dy")] public bool Dirty { get; init; }

    // ---- Hardware OTP ----

    /// <summary>Hardware revision number.</summary>
    [JsonPropertyName("hw")] public uint Hardware { get; init; }

    /// <summary>Hardware target code.</summary>
    [JsonPropertyName("hwt")] public uint HwTarget { get; init; }

    /// <summary>Hardware body variant.</summary>
    [JsonPropertyName("hwb")] public uint HwBody { get; init; }

    /// <summary>Hardware color variant.</summary>
    [JsonPropertyName("hwc")] public uint HwColor { get; init; }

    /// <summary>Hardware connector type.</summary>
    [JsonPropertyName("hwcn")] public uint HwConnect { get; init; }

    /// <summary>Display type code.</summary>
    [JsonPropertyName("hwd")] public uint HwDisplay { get; init; }

    /// <summary>RF region code burned into OTP.</summary>
    [JsonPropertyName("hwr")] public uint HwRegion { get; init; }

    /// <summary>Human-readable RF region name.</summary>
    [JsonPropertyName("hwrn")] public string? HwRegionName { get; init; }

    /// <summary>Manufacture timestamp (Unix seconds).</summary>
    [JsonPropertyName("hwts")] public uint HwTimestamp { get; init; }

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
    [JsonPropertyName("bm")] public string? BleMac { get; init; }

    // ---- Regulatory ----

    /// <summary>FCC certification ID.</summary>
    [JsonPropertyName("fcc")] public string? FccId { get; init; }

    /// <summary>Industry Canada certification ID.</summary>
    [JsonPropertyName("ic")] public string? IcId { get; init; }

    /// <summary>Japan MIC certification ID.</summary>
    [JsonPropertyName("mic")] public string? MicId { get; init; }

    /// <summary>China SRRC certification ID.</summary>
    [JsonPropertyName("srrc")] public string? SrrcId { get; init; }

    /// <summary>Taiwan NCC certification ID.</summary>
    [JsonPropertyName("ncc")] public string? NccId { get; init; }
}
