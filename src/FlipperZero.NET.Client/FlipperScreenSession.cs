using FlipperZero.NET.Commands.Ui;

namespace FlipperZero.NET;

/// <summary>
/// Represents an exclusive host-driven screen session acquired from the Flipper daemon.
///
/// Obtain an instance via
/// <see cref="Extensions.FlipperUiExtensions.UiScreenAcquireAsync"/>.
/// Dispose to automatically release the screen and restore the daemon's own ViewPort.
///
/// <code>
/// await using var screen = await client.UiScreenAcquireAsync();
/// await screen.DrawStrAsync(10, 32, "Hello!", UiFont.Primary);
/// await screen.FlushAsync();
/// // screen is automatically released when the using block exits
/// </code>
/// </summary>
public sealed class FlipperScreenSession : IAsyncDisposable
{
    private readonly FlipperRpcClient _client;
    private int _disposed;

    internal FlipperScreenSession(FlipperRpcClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Queues a draw-string operation on the canvas.
    ///
    /// The text is rendered at the next <see cref="FlushAsync"/> call.
    /// </summary>
    /// <param name="x">Horizontal pixel position (0–127).</param>
    /// <param name="y">Vertical pixel position / baseline (0–63).</param>
    /// <param name="text">String to draw (max 63 characters).</param>
    /// <param name="font">Font selection (default <see cref="UiFont.Secondary"/>).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<UiDrawStrResponse> DrawStrAsync(
        byte x,
        byte y,
        string text,
        UiFont font = UiFont.Secondary,
        CancellationToken ct = default)
        => _client.SendAsync<UiDrawStrCommand, UiDrawStrResponse>(
            new UiDrawStrCommand(x, y, text, font), ct);

    /// <summary>
    /// Queues a draw-rectangle operation on the canvas.
    ///
    /// The rectangle is rendered at the next <see cref="FlushAsync"/> call.
    /// </summary>
    /// <param name="x">Left edge pixel position (0–127).</param>
    /// <param name="y">Top edge pixel position (0–63).</param>
    /// <param name="width">Rectangle width in pixels.</param>
    /// <param name="height">Rectangle height in pixels.</param>
    /// <param name="filled"><c>true</c> for a filled box; <c>false</c> (default) for an outline frame.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<UiDrawRectResponse> DrawRectAsync(
        byte x,
        byte y,
        byte width,
        byte height,
        bool filled = false,
        CancellationToken ct = default)
        => _client.SendAsync<UiDrawRectCommand, UiDrawRectResponse>(
            new UiDrawRectCommand(x, y, width, height, filled), ct);

    /// <summary>
    /// Queues a draw-line operation on the canvas.
    ///
    /// The line is rendered at the next <see cref="FlushAsync"/> call.
    /// </summary>
    /// <param name="x1">Start point horizontal pixel position (0–127).</param>
    /// <param name="y1">Start point vertical pixel position (0–63).</param>
    /// <param name="x2">End point horizontal pixel position (0–127).</param>
    /// <param name="y2">End point vertical pixel position (0–63).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<UiDrawLineResponse> DrawLineAsync(
        byte x1,
        byte y1,
        byte x2,
        byte y2,
        CancellationToken ct = default)
        => _client.SendAsync<UiDrawLineCommand, UiDrawLineResponse>(
            new UiDrawLineCommand(x1, y1, x2, y2), ct);

    /// <summary>
    /// Flushes all queued draw operations to the Flipper screen.
    ///
    /// Calls <c>view_port_update()</c> on the daemon side, triggering a canvas
    /// redraw with all pending draw operations, then clears the operation queue.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<UiFlushResponse> FlushAsync(CancellationToken ct = default)
        => _client.SendAsync<UiFlushCommand, UiFlushResponse>(new UiFlushCommand(), ct);

    /// <summary>
    /// Releases exclusive control of the Flipper screen and restores the daemon's
    /// own ViewPort. Idempotent — safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _client.SendAsync<UiScreenReleaseCommand, UiScreenReleaseResponse>(
                new UiScreenReleaseCommand()).ConfigureAwait(false);
        }
        catch (FlipperRpcException) { /* screen may already be released */ }
        catch (OperationCanceledException) { /* client shutting down */ }
    }
}
