using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Queues a draw-string operation on the host canvas.
///
/// The text is rendered at the next <see cref="UiFlushCommand"/> call.
/// Requires the screen to be acquired via <see cref="UiScreenAcquireCommand"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"ui_draw_str","x":10,"y":20,"text":"Hello","font":1}</code>
///
/// Wire format (response):
/// <code>{"id":N,"status":"ok"}</code>
///
/// Error codes:
/// <list type="bullet">
///   <item><c>resource_busy</c> — screen not acquired.</item>
///   <item><c>missing_text</c> — <paramref name="text"/> is empty.</item>
/// </list>
/// </summary>
public readonly struct UiDrawStrCommand : IRpcCommand<UiDrawStrResponse>
{
    /// <param name="x">Horizontal pixel position (0–127).</param>
    /// <param name="y">Vertical pixel position / baseline (0–63).</param>
    /// <param name="text">String to draw (max 63 characters).</param>
    /// <param name="font">Font selection (default <see cref="UiFont.Secondary"/>).</param>
    public UiDrawStrCommand(byte x, byte y, string text, UiFont font = UiFont.Secondary)
    {
        X = x;
        Y = y;
        Text = text;
        Font = font;
    }

    /// <summary>Horizontal pixel position (0–127).</summary>
    public byte X { get; }

    /// <summary>Vertical pixel position / baseline (0–63).</summary>
    public byte Y { get; }

    /// <summary>String to draw.</summary>
    public string Text { get; }

    /// <summary>Font selection.</summary>
    public UiFont Font { get; }

    /// <inheritdoc />
    public string CommandName => "ui_draw_str";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("x", X);
        writer.WriteNumber("y", Y);
        writer.WriteString("text", Text);
        writer.WriteNumber("font", (byte)Font);
    }
}

/// <summary>Response to <see cref="UiDrawStrCommand"/>.</summary>
public readonly struct UiDrawStrResponse : IRpcCommandResponse { }
