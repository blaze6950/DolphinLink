using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Input;

/// <summary>
/// Subscribes to all hardware button events on the Flipper, opening a stream
/// that yields one <see cref="FlipperInputEvent"/> per button action.
///
/// Multiple concurrent streams are allowed; all active streams receive every
/// event (broadcast, no exclusive lock).
///
/// Wire format (request, no custom exit):
/// <code>{"id":N,"cmd":"input_listen_start"}</code>
///
/// Wire format (request, with custom exit combo):
/// <code>{"id":N,"cmd":"input_listen_start","exit_key":"ok","exit_type":"long"}</code>
///
/// Wire format (stream open response):
/// <code>{"id":N,"stream":M}</code>
///
/// Wire format (stream events):
/// <code>{"event":{"key":"ok","type":"short"},"stream":M}</code>
/// <code>{"event":{"key":"back","type":"long"},"stream":M}</code>
///
/// Dispose the returned <see cref="RpcStream{TEvent}"/> to unsubscribe.
/// </summary>
public readonly struct InputListenStartCommand : IRpcStreamCommand<FlipperInputEvent>
{
    /// <summary>
    /// When non-null, overrides the Back+Short exit combo used to quit the daemon.
    /// Both <see cref="ExitKey"/> and <see cref="ExitType"/> must be non-null together.
    /// </summary>
    public FlipperInputKey? ExitKey { get; init; }

    /// <summary>
    /// When non-null, overrides the exit event type used to quit the daemon.
    /// Both <see cref="ExitKey"/> and <see cref="ExitType"/> must be non-null together.
    /// </summary>
    public FlipperInputType? ExitType { get; init; }

    /// <inheritdoc />
    public string CommandName => "input_listen_start";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        if (ExitKey.HasValue && ExitType.HasValue)
        {
            // Serialise lowercase string values matching the wire format.
            writer.WriteString("exit_key", ExitKey.Value switch
            {
                FlipperInputKey.Up => "up",
                FlipperInputKey.Down => "down",
                FlipperInputKey.Left => "left",
                FlipperInputKey.Right => "right",
                FlipperInputKey.Ok => "ok",
                FlipperInputKey.Back => "back",
                _ => "back",
            });
            writer.WriteString("exit_type", ExitType.Value switch
            {
                FlipperInputType.Press => "press",
                FlipperInputType.Release => "release",
                FlipperInputType.Short => "short",
                FlipperInputType.Long => "long",
                FlipperInputType.Repeat => "repeat",
                _ => "short",
            });
        }
    }
}

/// <summary>A hardware button event from the Flipper.</summary>
public readonly struct FlipperInputEvent : IRpcCommandResponse
{
    /// <summary>The physical key that was pressed.</summary>
    [JsonPropertyName("key")]
    public FlipperInputKey Key { get; init; }

    /// <summary>The event type.</summary>
    [JsonPropertyName("type")]
    public FlipperInputType Type { get; init; }
}
