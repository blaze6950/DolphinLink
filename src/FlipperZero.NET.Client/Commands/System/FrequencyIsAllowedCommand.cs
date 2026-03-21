using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Checks whether a given frequency is permitted in the Flipper's current region.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"frequency_is_allowed","freq":433920000}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"allowed":true}}</code>
/// </summary>
public readonly struct FrequencyIsAllowedCommand : IRpcCommand<FrequencyIsAllowedResponse>
{
    /// <param name="freq">Frequency in Hz to check (e.g. <c>433920000</c>).</param>
    public FrequencyIsAllowedCommand(uint freq) => Freq = freq;

    /// <summary>Frequency in Hz to check.</summary>
    public uint Freq { get; }

    /// <inheritdoc />
    public string CommandName => "frequency_is_allowed";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
    }
}

/// <summary>Response to <see cref="FrequencyIsAllowedCommand"/>.</summary>
public readonly struct FrequencyIsAllowedResponse : IRpcCommandResponse
{
    /// <summary><c>true</c> if the frequency is permitted in the current region.</summary>
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }
}
