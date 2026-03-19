using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Commands.Input;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Input;

/// <summary>
/// Integration tests for the <c>input_listen_start</c> stream command via
/// <see cref="FlipperInputExtensions.InputListenStartAsync"/>.
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

    /// <summary>
    /// After opening an input listen stream we must receive at least one
    /// <see cref="FlipperInputEvent"/> when a hardware button is pressed.
    /// Validates: stream event routing and <see cref="FlipperInputEvent"/>
    /// deserialisation of the <c>key</c> and <c>type</c> fields.
    ///
    /// Requires manual interaction: press any button on the Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task InputListenStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.InputListenStartAsync(ct: timeout.Token);

        FlipperInputEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one event
        }

        Assert.NotNull(firstEvent);
        // Enum values must be valid (not default 0 unless that is a defined member)
        Assert.True(Enum.IsDefined(firstEvent.Value.Key),
            $"Received undefined FlipperInputKey value: {firstEvent.Value.Key}");
        Assert.True(Enum.IsDefined(firstEvent.Value.Type),
            $"Received undefined FlipperInputType value: {firstEvent.Value.Type}");
    }

    /// <summary>
    /// With a custom exit combo set (Ok+Short), pressing Back must NOT stop
    /// the daemon — both Back presses must arrive as ordinary stream events.
    ///
    /// Background: without an override the daemon's default Back+Short combo
    /// terminates itself, so only the first Back press would be received before
    /// the stream silently dies.  By overriding the exit trigger to Ok+Short the
    /// Back button is demoted to a regular key, allowing both presses to be
    /// observed and the stream to be cleanly disposed afterwards.
    ///
    /// Validates: custom exit-combo wiring in <c>input_listen_start</c> handler
    /// and the <c>on_input_queue</c> fallback-suppression path in rpc_gui.c.
    ///
    /// Requires manual interaction: press Back twice on the Flipper within
    /// 15 minutes (do NOT press Ok — that would stop the daemon).
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task InputListenStart_WithCustomExitKey_BackPressDoesNotStopDaemon()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        // Override exit trigger to Ok+Short so Back is treated as a normal key.
        await using var stream = await Client.InputListenStartAsync(
            exitKey: FlipperInputKey.Ok,
            exitType: FlipperInputType.Short,
            ct: timeout.Token);

        var backEvents = new List<FlipperInputEvent>();

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            if (evt is { Key: FlipperInputKey.Back, Type: FlipperInputType.Short })
            {
                backEvents.Add(evt);
            }

            if (backEvents.Count >= 2)
            {
                break; // Both Back presses received — daemon is still alive
            }
        }

        // Both events must have arrived; if the daemon stopped after the first
        // Back press the await foreach would have thrown or timed out.
        Assert.Equal(2, backEvents.Count);

        // The stream must still be alive and closable cleanly.
        // DisposeAsync sends stream_close — must not throw.
    }
}
