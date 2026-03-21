using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Stops the piezo speaker and releases the speaker hardware resource.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"speaker_stop"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N}</code>
///
/// Must be called after <see cref="SpeakerStartCommand"/> to free the resource.
/// </summary>
public readonly struct SpeakerStopCommand : IRpcCommand<SpeakerStopResponse>
{
    /// <inheritdoc />
    public string CommandName => "speaker_stop";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="SpeakerStopCommand"/>.</summary>
public readonly struct SpeakerStopResponse : IRpcCommandResponse { }
