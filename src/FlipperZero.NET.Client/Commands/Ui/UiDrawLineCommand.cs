using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Queues a draw-line operation on the host canvas.
///
/// The line is rendered at the next <see cref="UiFlushCommand"/> call.
/// Requires the screen to be acquired via <see cref="UiScreenAcquireCommand"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_draw_line","x1":0,"y1":0,"x2":127,"y2":63}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — screen not acquired.</item>
/// </list>
/// </summary>
public readonly struct UiDrawLineCommand : IRpcCommand<UiDrawLineResponse>
{
    /// <param name="x1">Start point horizontal pixel position (0–127).</param>
    /// <param name="y1">Start point vertical pixel position (0–63).</param>
    /// <param name="x2">End point horizontal pixel position (0–127).</param>
    /// <param name="y2">End point vertical pixel position (0–63).</param>
    public UiDrawLineCommand(byte x1, byte y1, byte x2, byte y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Start point horizontal pixel position (0–127).</summary>
    public byte X1 { get; }

    /// <summary>Start point vertical pixel position (0–63).</summary>
    public byte Y1 { get; }

    /// <summary>End point horizontal pixel position (0–127).</summary>
    public byte X2 { get; }

    /// <summary>End point vertical pixel position (0–63).</summary>
    public byte Y2 { get; }

    /// <inheritdoc />
    public string CommandName => "ui_draw_line";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("x1", X1);
        writer.WriteNumber("y1", Y1);
        writer.WriteNumber("x2", X2);
        writer.WriteNumber("y2", Y2);
    }
}

/// <summary>Response to <see cref="UiDrawLineCommand"/>.</summary>
public readonly struct UiDrawLineResponse : IRpcCommandResponse { }
