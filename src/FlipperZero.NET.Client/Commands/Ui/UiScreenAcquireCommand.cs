using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Claims exclusive control of the Flipper screen.
///
/// While the screen is acquired, the daemon's own status ViewPort is hidden
/// and a secondary full-screen ViewPort is activated for the host to paint
/// via <see cref="UiDrawStrCommand"/>, <see cref="UiDrawRectCommand"/>,
/// <see cref="UiDrawLineCommand"/>, and <see cref="UiFlushCommand"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_screen_acquire"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — another client already holds the screen.</item>
/// </list>
///
/// Release the screen with <see cref="UiScreenReleaseCommand"/> when done.
/// </summary>
public readonly struct UiScreenAcquireCommand : IRpcCommand<UiScreenAcquireResponse>
{
    /// <inheritdoc />
    public string CommandName => "ui_screen_acquire";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        // No arguments required.
    }
}

/// <summary>Response to <see cref="UiScreenAcquireCommand"/>.</summary>
public readonly struct UiScreenAcquireResponse : IRpcCommandResponse { }
