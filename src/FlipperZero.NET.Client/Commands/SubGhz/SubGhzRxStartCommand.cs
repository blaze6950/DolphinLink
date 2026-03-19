using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.SubGhz;

/// <summary>
/// Opens a Sub-GHz OOK raw receive stream.  Each detected pulse is delivered
/// as a <see cref="SubGhzRxEvent"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"subghz_rx_start"}</code>
/// or with explicit frequency:
/// <code>{"id":N,"cmd":"subghz_rx_start","freq":433920000}</code>
///
/// Wire format (stream open response):
/// <code>{"id":N,"stream":M}</code>
///
/// Wire format (stream event):
/// <code>{"event":{"level":true,"duration_us":300},"stream":M}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Requires the Sub-GHz hardware resource.  Dispose the returned
/// <see cref="RpcStream{TEvent}"/> to stop receiving and release the radio.
/// </summary>
public readonly struct SubGhzRxStartCommand : IRpcStreamCommand<SubGhzRxEvent>
{
    /// <param name="freq">
    /// Optional carrier frequency in Hz (e.g. <c>433920000</c>).
    /// Defaults to 433.92 MHz when <c>null</c>.
    /// </param>
    public SubGhzRxStartCommand(uint? freq = null) => Freq = freq;

    /// <summary>Carrier frequency in Hz, or <c>null</c> to use the Flipper default (433.92 MHz).</summary>
    public uint? Freq { get; }

    /// <inheritdoc />
    public string CommandName => "subghz_rx_start";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        if(Freq.HasValue)
        {
            writer.WriteNumber("freq", Freq.Value);
        }
    }
}

/// <summary>A raw Sub-GHz OOK pulse event.</summary>
public readonly struct SubGhzRxEvent : IRpcCommandResponse
{
    /// <summary><c>true</c> = carrier on (mark); <c>false</c> = carrier off (space).</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }

    /// <summary>Pulse duration in microseconds.</summary>
    [JsonPropertyName("duration_us")]
    public uint DurationUs { get; init; }
}
