using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ir;

/// <summary>
/// Transmits a raw IR timing array via the Flipper's IR LED.
/// Each element of the timing array is a pulse duration in microseconds,
/// alternating mark (carrier on) / space (carrier off).
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ir_tx_raw","timings":[500,250,500,250]}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// Requires the IR hardware resource.
/// </summary>
public readonly struct IrTxRawCommand : IRpcCommand<IrTxRawResponse>
{
    /// <param name="timings">
    /// Pulse durations in microseconds, alternating mark/space.
    /// First element is a mark (carrier on).
    /// </param>
    public IrTxRawCommand(uint[] timings) => Timings = timings;

    /// <summary>Timing array in microseconds (mark, space, mark, space, …).</summary>
    public uint[] Timings { get; }

    /// <inheritdoc />
    public string CommandName => "ir_tx_raw";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("timings");
        foreach (var t in Timings)
        {
            writer.WriteNumberValue(t);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Response to <see cref="IrTxRawCommand"/>.</summary>
public readonly struct IrTxRawResponse : IRpcCommandResponse { }
