using FlipperZero.NET;
using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for LF RFID streaming via
/// <see cref="FlipperRpcClient.LfRfidReadStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LfRfidReadTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class LfRfidReadTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.LfRfidReadStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake for LFRFID read.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.LfRfidReadStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening an LF RFID read stream we must be able to receive at
    /// least one <see cref="LfRfidReadEvent"/> when a compatible RFID tag is
    /// presented to the Flipper's RFID coil.
    /// Validates: stream event routing and <see cref="LfRfidReadEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.LfRfidReadStartAsync(timeout.Token);

        LfRfidReadEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.Value.Type),
            "LfRfidReadEvent.Type must not be empty");
    }

    /// <summary>
    /// Disposing an LF RFID read stream must send <c>stream_close</c> and
    /// not throw.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.LfRfidReadStartAsync();

        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing an LF RFID read stream we must be able to open a new
    /// one.
    /// Validates: the RFID resource is released after stream_close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.LfRfidReadStartAsync();
        await first.DisposeAsync();

        await Task.Delay(200);

        await using var second = await Client.LfRfidReadStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent LF RFID read streams must cause the
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the RESOURCE_RFID bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.LfRfidReadStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.LfRfidReadStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
