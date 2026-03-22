using FlipperZero.NET.Commands.Input;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.Input;

/// <summary>
/// Manual tests for the <c>input_listen_start</c> stream command that require
/// pressing physical buttons on the Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~InputListenManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class InputListenManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening an input listen stream we must receive at least one
    /// <see cref="InputListenEvent"/> when a hardware button is pressed.
    /// Validates: stream event routing and <see cref="InputListenEvent"/>
    /// deserialisation of the <c>key</c> and <c>type</c> fields.
    ///
    /// Requires manual interaction: press any button on the Flipper within
    /// 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task InputListenStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.InputListenStartAsync(ct: timeout.Token);

        InputListenEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one event
        }

        Assert.NotNull(firstEvent);
        Assert.True(Enum.IsDefined(firstEvent.Value.Key),
            $"Received undefined FlipperInputKey value: {firstEvent.Value.Key}");
        Assert.True(Enum.IsDefined(firstEvent.Value.Type),
            $"Received undefined FlipperInputType value: {firstEvent.Value.Type}");
    }

    /// <summary>
    /// With a custom exit combo set (Ok+Short), pressing Back must NOT stop
    /// the daemon — both Back presses must arrive as ordinary stream events.
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

        var backEvents = new List<InputListenEvent>();

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
