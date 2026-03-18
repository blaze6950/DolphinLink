using FlipperZero.NET;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for explicit stream closure via
/// <see cref="FlipperRpcClient.StreamCloseAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~StreamCloseTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class StreamCloseTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// Explicitly closing a stream by id via
    /// <see cref="FlipperRpcClient.StreamCloseAsync"/> must succeed without
    /// throwing.
    /// Validates: the stream_close command/response round-trip when called
    /// directly rather than through <see cref="RpcStream{TEvent}.DisposeAsync"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StreamClose_ExplicitClose_Succeeds()
    {
        // Open the stream but don't use the await-using pattern, so
        // DisposeAsync won't auto-close it — we'll close manually.
        var stream = await Client.IrReceiveStartAsync();
        var streamId = stream.StreamId;

        // Manually close via the public API
        await Client.StreamCloseAsync(streamId);

        // Dispose the RpcStream handle too (which tries a second stream_close
        // internally; should be a no-op / swallowed error).
        await stream.DisposeAsync();
    }

    /// <summary>
    /// Closing a stream id that does not exist (or is already closed) must
    /// return a <see cref="FlipperRpcException"/> with the
    /// <c>stream_not_found</c> error code.
    /// Validates: the daemon's "unknown stream id" error path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StreamClose_NonExistentStreamId_ThrowsStreamNotFound()
    {
        // Use a stream id that is very unlikely to exist.
        const uint bogusStreamId = 255u;

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.StreamCloseAsync(bogusStreamId));

        Assert.Equal("stream_not_found", ex.ErrorCode);
    }

    /// <summary>
    /// After an explicit stream_close the IR resource must be released, so
    /// we can immediately open a fresh IR receive stream.
    /// Validates: resource is freed on the daemon side after close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StreamClose_AfterExplicitClose_CanReopenStream()
    {
        var stream = await Client.IrReceiveStartAsync();
        await Client.StreamCloseAsync(stream.StreamId);
        await stream.DisposeAsync(); // Cleanup the handle

        // Give the Flipper a moment to release the resource
        await Task.Delay(200);

        await using var second = await Client.IrReceiveStartAsync();
        Assert.NotEqual(0u, second.StreamId);
    }
}
