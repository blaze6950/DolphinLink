using FlipperZero.NET.Commands.Gpio;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.Gpio;

/// <summary>
/// Hardware tests for GPIO watch streaming via <see cref="FlipperRpcClient.GpioWatchStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~GpioWatchTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class GpioWatchTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.GpioWatchStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"t":0,"i":N,"p":{"s":M}}</c>).
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.GpioWatchStartAsync(GpioPin.Pin6);

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the GPIO watch is stopped and resources are
    /// freed on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task GpioWatchStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.GpioWatchStartAsync(GpioPin.Pin6);

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a GPIO watch stream we must be able to open a new one
    /// on the same pin.
    /// Validates: GPIO pins have no resource-mask exclusion, so they can be
    /// freely re-acquired after close.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task GpioWatchStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.GpioWatchStartAsync(GpioPin.Pin6);
        await first.DisposeAsync();

        // Give the Flipper a moment to release the stream slot
        await Task.Delay(200);

        await using var second = await Client.GpioWatchStartAsync(GpioPin.Pin6);

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Opening GPIO watch streams on two different pins concurrently must
    /// succeed — GPIO has no shared resource bitmask so both streams can
    /// coexist.
    /// Validates: independent pin streams do not interfere with each other.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task GpioWatchStart_TwoPinsConcurrently_BothSucceed()
    {
        await using var stream6 = await Client.GpioWatchStartAsync(GpioPin.Pin6);
        await using var stream7 = await Client.GpioWatchStartAsync(GpioPin.Pin7);

        Assert.NotEqual(0u, stream6.StreamId);
        Assert.NotEqual(0u, stream7.StreamId);
        Assert.NotEqual(stream6.StreamId, stream7.StreamId);
    }
}
