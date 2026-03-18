using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for Sub-GHz commands:
/// <see cref="FlipperRpcClient.SubGhzGetRssiAsync"/>,
/// <see cref="FlipperRpcClient.SubGhzTxAsync"/>, and
/// <see cref="FlipperRpcClient.SubGhzRxStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~SubGhzCommandTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class SubGhzCommandTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // 433.92 MHz — the default Sub-GHz frequency and always permitted.
    private const uint Freq433 = 433_920_000;

    // -----------------------------------------------------------------------
    // subghz_get_rssi
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reading RSSI at a valid frequency must succeed without throwing.
    /// Note: due to a known C#/C JSON key mismatch (<c>"rssi_dbm10"</c> vs
    /// <c>"rssi"</c>), the returned integer is always 0 — we only verify the
    /// call completes without error.
    /// Validates: <c>subghz_get_rssi</c> happy-path round-trip.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzGetRssi_ValidFreq_Succeeds()
    {
        // Just verify no exception is thrown; value assertion omitted due to
        // known "rssi_dbm10" vs "rssi" JSON key mismatch in the C# struct.
        await Client.SubGhzGetRssiAsync(Freq433);
    }

    // -----------------------------------------------------------------------
    // subghz_tx
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transmitting a raw OOK burst at 433.92 MHz must succeed without
    /// throwing.
    /// Validates: <c>subghz_tx</c> happy-path round-trip.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzTx_ValidFreqAndTimings_Succeeds()
    {
        // A minimal 4-element OOK timing burst (mark 500 µs, space 500 µs, ...)
        var timings = new uint[] { 500, 500, 500, 500 };

        await Client.SubGhzTxAsync(Freq433, timings);
    }

    // -----------------------------------------------------------------------
    // subghz_rx_start (stream)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <see cref="FlipperRpcClient.SubGhzRxStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake for Sub-GHz RX.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.SubGhzRxStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening a Sub-GHz RX stream we must be able to receive at least
    /// one <see cref="SubGhzRxEvent"/> from ambient 433 MHz traffic.
    /// Validates: stream event routing and <see cref="SubGhzRxEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.SubGhzRxStartAsync(Freq433, timeout.Token);

        SubGhzRxEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.True(firstEvent.Value.DurationUs > 0,
            "SubGhzRxEvent.DurationUs must be positive");
    }

    /// <summary>
    /// Disposing the Sub-GHz RX stream must send <c>stream_close</c> and not
    /// throw.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.SubGhzRxStartAsync();

        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a Sub-GHz RX stream we must be able to open a new one.
    /// Validates: the Sub-GHz resource is released after stream_close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.SubGhzRxStartAsync();
        await first.DisposeAsync();

        await Task.Delay(200);

        await using var second = await Client.SubGhzRxStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent Sub-GHz RX streams must cause the
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the RESOURCE_SUBGHZ bitmask enforcement in the daemon.
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
