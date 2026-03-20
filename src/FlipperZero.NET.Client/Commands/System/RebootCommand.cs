using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Requests an immediate hardware reset of the Flipper Zero. The daemon sends
/// this OK response and then calls <c>furi_hal_power_reset()</c>, which
/// performs a hard MCU reset equivalent to pressing the physical reset button.
/// The USB connection will drop almost immediately after the response is sent.
/// </summary>
public readonly struct RebootCommand : IRpcCommand<RebootResponse>
{
    public string CommandName => "reboot";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response envelope for <see cref="RebootCommand"/>.</summary>
public readonly struct RebootResponse : IRpcCommandResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
