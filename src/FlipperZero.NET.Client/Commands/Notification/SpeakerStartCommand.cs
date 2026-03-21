using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Starts a continuous tone on the Flipper's piezo speaker.
/// The speaker hardware resource is held until <see cref="SpeakerStopCommand"/> is sent.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"speaker_start","freq":440,"volume":128}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// Returns <c>resource_busy</c> if the speaker resource is already held.
/// Always pair with a <see cref="SpeakerStopCommand"/> to release the resource.
/// </summary>
public readonly struct SpeakerStartCommand : IRpcCommand<SpeakerStartResponse>
{
    /// <param name="freq">Frequency in Hz, e.g. <c>440</c> for A4.</param>
    /// <param name="volume">Volume 0–255 (mapped to 0.0–1.0 on the Flipper).</param>
    public SpeakerStartCommand(uint freq, byte volume)
    {
        Freq = freq;
        Volume = volume;
    }

    /// <summary>Tone frequency in Hz.</summary>
    public uint Freq { get; }

    /// <summary>Volume 0–255.</summary>
    public byte Volume { get; }

    /// <inheritdoc />
    public string CommandName => "speaker_start";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
        writer.WriteNumber("volume", Volume);
    }
}

/// <summary>Response to <see cref="SpeakerStartCommand"/>.</summary>
public readonly struct SpeakerStartResponse : IRpcCommandResponse { }
