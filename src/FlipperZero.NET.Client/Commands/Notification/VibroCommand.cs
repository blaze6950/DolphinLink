using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Notification;

/// <summary>
/// Enables or disables the Flipper's vibration motor.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"vibro","enable":true}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
/// </summary>
public readonly struct VibroCommand : IRpcCommand<VibroResponse>
{
    /// <param name="enable"><c>true</c> to start vibrating; <c>false</c> to stop.</param>
    public VibroCommand(bool enable) => Enable = enable;

    /// <summary><c>true</c> to start vibrating; <c>false</c> to stop.</summary>
    public bool Enable { get; }

    /// <inheritdoc />
    public string CommandName => "vibro";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteBoolean("enable", Enable);
    }
}

/// <summary>Response to <see cref="VibroCommand"/>.</summary>
public readonly struct VibroResponse : IRpcCommandResponse { }
