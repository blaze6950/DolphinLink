using DolphinLink.Client.Exceptions;

namespace DolphinLink.Client.HardwareTests.Ir;

/// <summary>
/// Hardware tests for IR receive streaming via <see cref="RpcClient.IrReceiveStartAsync"/>.
/// Excludes the manual test <c>IrReceiveStart_ReceivesAtLeastOneEvent</c> which requires
/// a human to point an IR remote at the Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IrReceiveTests"
/// </summary>
[Collection(DeviceCollection.Name)]
public sealed class IrReceiveTests(DeviceFixture fixture)
{
    private RpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="RpcClient.IrReceiveStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"t":0,"i":N,"p":{"s":M}}</c>).
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task IrReceiveStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.IrReceiveStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the IR receiver is stopped and resources are
    /// freed on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task IrReceiveStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.IrReceiveStartAsync();

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing an IR stream we must be able to open a new one.
    /// Validates: the IR resource is actually released after stream_close so
    /// a second receive can acquire it.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task IrReceiveStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.IrReceiveStartAsync();
        await first.DisposeAsync();

        // Give the Flipper a moment to release the resource
        await Task.Delay(200);

        await using var second = await Client.IrReceiveStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent IR receive streams must cause the
    /// second to fail with a <see cref="RpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the resource-bitmask enforcement in the daemon.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task IrReceiveStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.IrReceiveStartAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => Client.IrReceiveStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
