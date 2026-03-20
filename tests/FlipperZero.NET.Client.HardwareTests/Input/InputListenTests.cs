using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.Input;

/// <summary>
/// Hardware tests for the <c>input_listen_start</c> stream command via
/// <see cref="FlipperInputExtensions.InputListenStartAsync"/>.
/// Excludes manual tests that require a human to press buttons on the Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~InputListenTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class InputListenTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperInputExtensions.InputListenStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"id":N,"stream":M}</c>).
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task InputListenStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.InputListenStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// Disposing the stream must send <c>stream_close</c> automatically and
    /// not throw. After dispose, the input subscription is cancelled on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task InputListenStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.InputListenStartAsync();

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing an input listen stream we must be able to open a new one.
    /// Input has no exclusive resource lock (multiple concurrent streams are
    /// allowed), so re-acquiring must always succeed.
    /// Validates: stream slot is freed and reusable after dispose.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task InputListenStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.InputListenStartAsync();
        await first.DisposeAsync();

        await using var second = await Client.InputListenStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Two concurrent input listen streams must both succeed and receive
    /// independent stream ids, since input has no exclusive resource bitmask
    /// (events are broadcast to all active streams).
    /// Validates: concurrent multi-stream support for broadcast resources.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task InputListenStart_TwoConcurrentStreams_BothSucceed()
    {
        await using var stream1 = await Client.InputListenStartAsync();
        await using var stream2 = await Client.InputListenStartAsync();

        Assert.NotEqual(0u, stream1.StreamId);
        Assert.NotEqual(0u, stream2.StreamId);
        Assert.NotEqual(stream1.StreamId, stream2.StreamId);
    }
}
