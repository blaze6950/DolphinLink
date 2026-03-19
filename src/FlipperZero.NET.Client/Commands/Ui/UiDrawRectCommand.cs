using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Queues a draw-rectangle operation on the host canvas.
///
/// The rectangle is rendered at the next <see cref="UiFlushCommand"/> call.
/// Requires the screen to be acquired via <see cref="UiScreenAcquireCommand"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_draw_rect","x":0,"y":0,"w":128,"h":64,"filled":false}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — screen not acquired.</item>
/// </list>
/// </summary>
public readonly struct UiDrawRectCommand : IRpcCommand<UiDrawRectResponse>
{
    /// <param name="x">Left edge pixel position (0–127).</param>
    /// <param name="y">Top edge pixel position (0–63).</param>
    /// <param name="width">Rectangle width in pixels.</param>
    /// <param name="height">Rectangle height in pixels.</param>
    /// <param name="filled">
    /// <c>true</c> for a filled box; <c>false</c> (default) for an outline frame.
    /// </param>
    public UiDrawRectCommand(byte x, byte y, byte width, byte height, bool filled = false)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Filled = filled;
    }

    /// <summary>Left edge pixel position (0–127).</summary>
    public byte X { get; }

    /// <summary>Top edge pixel position (0–63).</summary>
    public byte Y { get; }

    /// <summary>Rectangle width in pixels.</summary>
    public byte Width { get; }

    /// <summary>Rectangle height in pixels.</summary>
    public byte Height { get; }

    /// <summary><c>true</c> = filled box; <c>false</c> = outline frame.</summary>
    public bool Filled { get; }

    /// <inheritdoc />
    public string CommandName => "ui_draw_rect";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("x", X);
        writer.WriteNumber("y", Y);
        writer.WriteNumber("w", Width);
        writer.WriteNumber("h", Height);
        writer.WriteBoolean("filled", Filled);
    }
}

/// <summary>Response to <see cref="UiDrawRectCommand"/>.</summary>
public readonly struct UiDrawRectResponse : IRpcCommandResponse { }
