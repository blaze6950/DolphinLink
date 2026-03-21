using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Returns battery and power state information.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"power_info"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"charge":85,"voltage_mv":4050,"charging":true}}</code>
/// </summary>
public readonly struct PowerInfoCommand : IRpcCommand<PowerInfoResponse>
{
    /// <inheritdoc />
    public string CommandName => "power_info";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="PowerInfoCommand"/>.</summary>
public readonly struct PowerInfoResponse : IRpcCommandResponse
{
    /// <summary>Battery state of charge, 0–100 %.</summary>
    [JsonPropertyName("charge")]
    public uint Charge { get; init; }

    /// <summary>Battery voltage in millivolts.</summary>
    [JsonPropertyName("voltage_mv")]
    public uint VoltageMv { get; init; }

    /// <summary><c>true</c> when USB power is connected and the battery is charging.</summary>
    [JsonPropertyName("charging")]
    public bool Charging { get; init; }
}
