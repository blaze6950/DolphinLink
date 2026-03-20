using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.SubGhz;

/// <summary>
/// Hardware tests for Sub-GHz RX streaming via <see cref="FlipperRpcClient.SubGhzRxStartAsync"/>.
/// Excludes the manual test <c>SubGhzRxStart_ReceivesAtLeastOneEvent</c> which requires
/// a human to trigger a 433 MHz transmitter.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~SubGhzRxTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class SubGhzRxTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.SubGhzRxStartAsync"/> with no explicit
    /// frequency must return an <see cref="RpcStream{TEvent}"/> with a
    /// non-zero stream id (daemon defaults to 433.92 MHz).
    /// Validates: the stream-open handshake.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.SubGhzRxStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Passing an explicit frequency of 433.92 MHz must also result in a
    /// successful stream open with a non-zero stream id.
    /// Validates: the optional <c>"freq"</c> argument is forwarded to the
    /// daemon correctly.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_WithExplicitFreq_ReturnsStream()
    {
        await using var stream = await Client.SubGhzRxStartAsync(freq: 433_920_000);

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the Sub-GHz radio is put to sleep and the
    /// SUBGHZ resource is released on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.SubGhzRxStartAsync();

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a Sub-GHz RX stream we must be able to open a new one.
    /// Validates: the SUBGHZ resource bitmask is actually cleared after
    /// <c>stream_close</c> so a second stream can acquire it.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.SubGhzRxStartAsync();
        await first.DisposeAsync();

        // Give the Flipper a moment to release the resource
        await Task.Delay(200);

        await using var second = await Client.SubGhzRxStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent Sub-GHz RX streams must cause the
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: SUBGHZ resource-bitmask exclusivity enforcement in the
    /// daemon.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.SubGhzRxStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.SubGhzRxStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
