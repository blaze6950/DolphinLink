using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.SubGhz;

/// <summary>
/// Returns the current RSSI (received signal strength indicator) at a given frequency.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"subghz_get_rssi","freq":433920000}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok","data":{"rssi":-70}}</code>
///
/// Requires the Sub-GHz hardware resource briefly (tunes, samples, releases).
/// </summary>
public readonly struct SubGhzGetRssiCommand : IRpcCommand<SubGhzGetRssiResponse>
{
    /// <param name="freq">Frequency in Hz to tune to before sampling RSSI.</param>
    public SubGhzGetRssiCommand(uint freq) => Freq = freq;

    /// <summary>Frequency in Hz.</summary>
    public uint Freq { get; }

    /// <inheritdoc />
    public string CommandName => "subghz_get_rssi";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
    }
}

/// <summary>Response to <see cref="SubGhzGetRssiCommand"/>.</summary>
public readonly struct SubGhzGetRssiResponse : IRpcCommandResponse
{
    /// <summary>RSSI in dBm (negative integer, e.g. <c>-70</c>).</summary>
    [JsonPropertyName("rssi")]
    public int Rssi { get; init; }
}
