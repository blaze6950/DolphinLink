using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Flushes the buffered draw operations to the Flipper screen.
///
/// Triggers a <c>view_port_update()</c> on the daemon side, causing the GUI
/// to invoke the draw callback which replays all queued operations.
/// The op buffer is cleared after the flush.
///
/// Requires the screen to be acquired via <see cref="UiScreenAcquireCommand"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_flush"}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — screen not acquired.</item>
/// </list>
/// </summary>
public readonly struct UiFlushCommand : IRpcCommand<UiFlushResponse>
{
    /// <inheritdoc />
    public string CommandName => "ui_flush";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        // No arguments required.
    }
}

/// <summary>Response to <see cref="UiFlushCommand"/>.</summary>
public readonly struct UiFlushResponse : IRpcCommandResponse { }
