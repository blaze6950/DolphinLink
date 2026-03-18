using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for iButton streaming via
/// <see cref="FlipperRpcClient.IButtonReadStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IButtonReadTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class IButtonReadTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.IButtonReadStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake for iButton read.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IButtonReadStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.IButtonReadStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening an iButton read stream we must be able to receive at
    /// least one <see cref="IButtonReadEvent"/> when a compatible iButton key
    /// is touched to the Flipper's 1-Wire port.
    /// Validates: stream event routing and <see cref="IButtonReadEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IButtonReadStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.IButtonReadStartAsync(timeout.Token);

        IButtonReadEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.Value.Type),
            "IButtonReadEvent.Type must not be empty");
    }

    /// <summary>
    /// Disposing an iButton read stream must send <c>stream_close</c> and
    /// not throw.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IButtonReadStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.IButtonReadStartAsync();

        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing an iButton read stream we must be able to open a new
    /// one.
    /// Validates: the iButton resource is released after stream_close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IButtonReadStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.IButtonReadStartAsync();
        await first.DisposeAsync();

        await Task.Delay(200);

        await using var second = await Client.IButtonReadStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent iButton read streams must cause the
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the RESOURCE_IBUTTON bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IButtonReadStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.IButtonReadStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.IButtonReadStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
