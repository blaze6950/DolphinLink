using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Commands.Ir;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Ir;

/// <summary>
/// Integration tests for IR receive streaming via <see cref="FlipperRpcClient.IrReceiveStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IrReceiveTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class IrReceiveTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.IrReceiveStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"id":N,"stream":M}</c>).
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrReceiveStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.IrReceiveStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening an IR receive stream we must be able to receive at least
    /// one <see cref="IrReceiveEvent"/> when an IR remote is pointed at the
    /// Flipper and a button is pressed.
    /// Validates: stream event routing and <see cref="IrReceiveEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrReceiveStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.IrReceiveStartAsync(timeout.Token);

        IrReceiveEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.NotEqual(IrProtocol.Unknown, firstEvent.Value.Protocol);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the IR receiver is stopped and resources are
    /// freed on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
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
    [RequiresFlipperFact]
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
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: the resource-bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrReceiveStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.IrReceiveStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.IrReceiveStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
