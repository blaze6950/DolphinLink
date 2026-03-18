using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for Sub-GHz RX streaming via <see cref="FlipperRpcClient.SubGhzRxStartAsync"/>.
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
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_WithExplicitFreq_ReturnsStream()
    {
        await using var stream = await Client.SubGhzRxStartAsync(freq: 433_920_000);

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening a Sub-GHz RX stream we must be able to receive at least
    /// one <see cref="SubGhzRxEvent"/> when a 433 MHz transmitter fires
    /// (e.g. a remote control or key fob).
    /// Validates: stream event routing and <see cref="SubGhzRxEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.SubGhzRxStartAsync(ct: timeout.Token);

        SubGhzRxEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.True(firstEvent.Value.DurationUs > 0,
            "SubGhzRxEvent.DurationUs must be positive");
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the Sub-GHz radio is put to sleep and the
    /// SUBGHZ resource is released on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
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
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.SubGhzRxStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.SubGhzRxStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
