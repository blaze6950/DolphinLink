using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.IButton;

/// <summary>
/// Hardware tests for iButton streaming via
/// <see cref="FlipperRpcClient.IButtonReadStartAsync"/>.
/// Excludes the manual test <c>IButtonReadStart_ReceivesAtLeastOneEvent</c> which requires
/// a human to touch an iButton key to the Flipper's 1-Wire port.
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
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task IButtonReadStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.IButtonReadStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Disposing an iButton read stream must send <c>stream_close</c> and
    /// not throw.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [Trait("Category", "Hardware")]
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
    [Trait("Category", "Hardware")]
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
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task IButtonReadStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.IButtonReadStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.IButtonReadStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
