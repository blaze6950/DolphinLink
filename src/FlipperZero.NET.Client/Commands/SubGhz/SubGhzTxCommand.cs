using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.SubGhz;

/// <summary>
/// Transmits a raw OOK Sub-GHz packet at the specified carrier frequency.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"subghz_tx","freq":433920000,"timings":[300,300,600,300]}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// Requires the Sub-GHz hardware resource.
/// </summary>
public readonly struct SubGhzTxCommand : IRpcCommand<SubGhzTxResponse>
{
    /// <param name="freq">Carrier frequency in Hz, e.g. <c>433920000</c>.</param>
    /// <param name="timings">OOK timing array in microseconds (mark, space, mark, space, …).</param>
    public SubGhzTxCommand(uint freq, uint[] timings)
    {
        Freq = freq;
        Timings = timings;
    }

    /// <summary>Carrier frequency in Hz.</summary>
    public uint Freq { get; }

    /// <summary>OOK timing array in microseconds (mark, space, mark, space, …).</summary>
    public uint[] Timings { get; }

    /// <inheritdoc />
    public string CommandName => "subghz_tx";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
        writer.WriteStartArray("timings");
        foreach (var t in Timings)
        {
            writer.WriteNumberValue(t);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Response to <see cref="SubGhzTxCommand"/>.</summary>
public readonly struct SubGhzTxResponse : IRpcCommandResponse { }
