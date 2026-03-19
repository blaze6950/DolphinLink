using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Ui;

/// <summary>
/// Integration tests for the host-driven UI canvas commands via
/// <see cref="FlipperUiExtensions.UiScreenAcquireAsync"/> and
/// <see cref="FlipperScreenSession"/>.
///
/// All UI operations follow this lifecycle:
///   1. Acquire the screen  (<c>ui_screen_acquire</c>)
///   2. Queue draw operations  (<c>ui_draw_str</c>, <c>ui_draw_rect</c>, <c>ui_draw_line</c>)
///   3. Flush to the display  (<c>ui_flush</c>)
///   4. Release the screen   (<c>ui_screen_release</c>, called automatically by DisposeAsync)
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~UiScreenTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class UiScreenTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperUiExtensions.UiScreenAcquireAsync"/> must succeed and
    /// return a non-null <see cref="FlipperScreenSession"/>.
    /// Validates: <c>ui_screen_acquire</c> round-trip and the <c>GUI</c> resource
    /// is correctly acquired on the daemon side.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_ReturnsSession()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        Assert.NotNull(screen);
    }

    /// <summary>
    /// A complete draw-string workflow must succeed without throwing:
    /// acquire → draw text → flush → dispose (releases screen).
    /// Validates: <c>ui_screen_acquire</c>, <c>ui_draw_str</c>, <c>ui_flush</c>,
    /// and <c>ui_screen_release</c> (via dispose) all succeed in sequence.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_DrawStr_FlushAndRelease()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        await screen.DrawStrAsync(10, 32, "Hello!");
        await screen.FlushAsync();

        // DisposeAsync sends ui_screen_release — must not throw
    }

    /// <summary>
    /// A complete draw-rectangle workflow must succeed without throwing:
    /// acquire → draw rect → flush → dispose.
    /// Validates: <c>ui_draw_rect</c> request serialisation and daemon-side
    /// execution of the canvas draw primitive.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_DrawRect_FlushAndRelease()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        await screen.DrawRectAsync(x: 0, y: 0, width: 64, height: 32, filled: false);
        await screen.FlushAsync();
    }

    /// <summary>
    /// A complete draw-line workflow must succeed without throwing:
    /// acquire → draw line → flush → dispose.
    /// Validates: <c>ui_draw_line</c> request serialisation and daemon-side
    /// execution of the canvas draw primitive.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_DrawLine_FlushAndRelease()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        await screen.DrawLineAsync(x1: 0, y1: 0, x2: 127, y2: 63);
        await screen.FlushAsync();
    }

    /// <summary>
    /// All three draw primitives queued before a single flush must succeed.
    /// Validates: multiple queued draw commands are processed in order by the
    /// daemon before <c>ui_flush</c> commits them to the display.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_AllPrimitives_FlushAndRelease()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        await screen.DrawRectAsync(x: 0, y: 0, width: 128, height: 64, filled: false);
        await screen.DrawLineAsync(x1: 0, y1: 0, x2: 127, y2: 63);
        await screen.DrawStrAsync(10, 32, "Test");
        await screen.FlushAsync();
    }

    /// <summary>
    /// Attempting to acquire the screen while it is already held must throw a
    /// <see cref="FlipperRpcException"/> with error code <c>resource_busy</c>.
    /// Validates: the <c>GUI</c> resource bitmask exclusion in the daemon's
    /// dispatcher correctly rejects a second <c>ui_screen_acquire</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenAcquire_DoubleAcquire_ThrowsResourceBusy()
    {
        await using var screen = await Client.UiScreenAcquireAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.UiScreenAcquireAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }

    /// <summary>
    /// After disposing a <see cref="FlipperScreenSession"/>, the caller must be
    /// able to acquire the screen again immediately.
    /// Validates: <c>ui_screen_release</c> (sent by <c>DisposeAsync</c>) correctly
    /// clears the <c>GUI</c> resource bitmask so a subsequent acquire succeeds.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenSession_Dispose_ReleasesScreen()
    {
        var first = await Client.UiScreenAcquireAsync();
        await first.DisposeAsync();

        // Must succeed — screen was released
        await using var second = await Client.UiScreenAcquireAsync();

        Assert.NotNull(second);
    }

    /// <summary>
    /// Calling <see cref="FlipperScreenSession.DisposeAsync"/> more than once
    /// must not throw.
    /// Validates: the idempotency guard (<c>Interlocked.Exchange</c>) in
    /// <see cref="FlipperScreenSession.DisposeAsync"/> prevents a double
    /// <c>ui_screen_release</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task UiScreenSession_Dispose_IsIdempotent()
    {
        var screen = await Client.UiScreenAcquireAsync();

        await screen.DisposeAsync();

        // Second dispose must not throw
        await screen.DisposeAsync();
    }
}
