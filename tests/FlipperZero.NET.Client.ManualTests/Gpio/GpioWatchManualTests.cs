using FlipperZero.NET.Commands.Gpio;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.Gpio;

/// <summary>
/// Manual test for GPIO watch streaming that requires physical interaction
/// (toggling a wire on Pin6).
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~GpioWatchManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class GpioWatchManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening a GPIO watch stream we must be able to receive at least
    /// one <see cref="GpioWatchEvent"/> when the pin level changes (e.g. by
    /// toggling a wire connected to GPIO header pin 6 / PA4).
    /// Validates: stream event routing and <see cref="GpioWatchEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: toggle a wire on Pin6 within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.GpioWatchStartAsync(GpioPin.Pin6, timeout.Token);

        GpioWatchEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.Equal(GpioPin.Pin6, firstEvent.Value.Pin);
    }
}
