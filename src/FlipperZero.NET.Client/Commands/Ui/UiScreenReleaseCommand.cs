using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Releases exclusive control of the Flipper screen.
///
/// The host's secondary ViewPort is removed and the daemon's own status
/// ViewPort is restored.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_screen_release"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — the screen is not currently acquired.</item>
/// </list>
/// </summary>
public readonly struct UiScreenReleaseCommand : IRpcCommand<UiScreenReleaseResponse>
{
    /// <inheritdoc />
    public string CommandName => "ui_screen_release";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        // No arguments required.
    }
}

/// <summary>Response to <see cref="UiScreenReleaseCommand"/>.</summary>
public readonly struct UiScreenReleaseResponse : IRpcCommandResponse { }
