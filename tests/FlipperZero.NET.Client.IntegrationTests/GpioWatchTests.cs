using FlipperZero.NET;
using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for GPIO watch streaming via <see cref="FlipperRpcClient.GpioWatchStartAsync"/>.
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
    /// <c>{"id":N,"stream":M}</c>).
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.GpioWatchStartAsync("6");

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening a GPIO watch stream we must be able to receive at least
    /// one <see cref="GpioWatchEvent"/> when the pin level changes (e.g. by
    /// toggling a wire connected to GPIO header pin 6 / PA4).
    /// Validates: stream event routing and <see cref="GpioWatchEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.GpioWatchStartAsync("6", timeout.Token);

        GpioWatchEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.Value.Pin),
            "GpioWatchEvent.Pin must not be empty");
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the GPIO watch is stopped and resources are
    /// freed on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.GpioWatchStartAsync("6");

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a GPIO watch stream we must be able to open a new one
    /// on the same pin.
    /// Validates: GPIO pins have no resource-mask exclusion, so they can be
    /// freely re-acquired after close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.GpioWatchStartAsync("6");
        await first.DisposeAsync();

        // Give the Flipper a moment to release the stream slot
        await Task.Delay(200);

        await using var second = await Client.GpioWatchStartAsync("6");

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Calling <see cref="FlipperRpcClient.GpioWatchStartAsync"/> without a
    /// pin argument (null) must cause the daemon to return a
    /// <see cref="FlipperRpcException"/> with the <c>missing_pin</c> error
    /// code.
    /// Validates: server-side argument validation for the "pin" field.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_MissingPin_ThrowsError()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.GpioWatchStartAsync(null!));

        Assert.Equal("missing_pin", ex.ErrorCode);
    }

    /// <summary>
    /// Supplying an unrecognised pin label (e.g. <c>"99"</c>) must cause the
    /// daemon to return a <see cref="FlipperRpcException"/> with the
    /// <c>invalid_pin</c> error code.
    /// Validates: server-side pin-label validation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_InvalidPin_ThrowsError()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.GpioWatchStartAsync("99"));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }

    /// <summary>
    /// Opening GPIO watch streams on two different pins concurrently must
    /// succeed — GPIO has no shared resource bitmask so both streams can
    /// coexist.
    /// Validates: independent pin streams do not interfere with each other.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_TwoPinsConcurrently_BothSucceed()
    {
        await using var stream6 = await Client.GpioWatchStartAsync("6");
        await using var stream7 = await Client.GpioWatchStartAsync("7");

        Assert.NotEqual(0u, stream6.StreamId);
        Assert.NotEqual(0u, stream7.StreamId);
        Assert.NotEqual(stream6.StreamId, stream7.StreamId);
    }
}
