using FlipperZero.NET;
using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for BLE scanning via <see cref="FlipperRpcClient.BleScanStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~BleScanTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class BleScanTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.BleScanStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"id":N,"stream":M}</c>).
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.BleScanStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening a scan stream we must be able to receive at least one
    /// <see cref="BleScanEvent"/>.  The event must carry a non-empty address
    /// and an RSSI value in a plausible range for BLE (−120 … 0 dBm).
    /// Validates: stream event routing and <see cref="BleScanEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var stream = await Client.BleScanStartAsync(timeout.Token);

        BleScanEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.Value.Address),
            "BleScanEvent.Address must not be empty");
        Assert.InRange(firstEvent.Value.Rssi, -120, 0);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the scan is stopped and resources are freed
    /// on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.BleScanStartAsync();

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a scan stream we must be able to open a new one.
    /// Validates: the BLE resource is actually released after stream_close so
    /// a second scan can acquire it.
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.BleScanStartAsync();
        await first.DisposeAsync();

        // Give the Flipper a moment to release the resource
        await Task.Delay(200);

        await using var second = await Client.BleScanStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent BLE scans must cause the second
    /// to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the resource-bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.BleScanStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.BleScanStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }

    /// <summary>
    /// Collect several events and verify they all deserialise without throwing.
    /// Validates: repeated event deserialization over the lifetime of a stream.
    /// </summary>
    [RequiresFlipperFact]
    public async Task BleScanStart_CollectMultipleEvents_AllDeserialiseCleanly()
    {
        const int target = 3;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var stream = await Client.BleScanStartAsync(timeout.Token);

        var events = new List<BleScanEvent>();

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            events.Add(evt);
            if (events.Count >= target)
            {
                break;
            }
        }

        Assert.Equal(target, events.Count);
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.Address)));
    }
}
