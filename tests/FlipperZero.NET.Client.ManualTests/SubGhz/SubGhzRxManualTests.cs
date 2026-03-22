using FlipperZero.NET.Commands.SubGhz;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.SubGhz;

/// <summary>
/// Manual test for Sub-GHz RX streaming that requires triggering a 433 MHz
/// transmitter near the Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~SubGhzRxManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class SubGhzRxManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening a Sub-GHz RX stream we must be able to receive at least
    /// one <see cref="SubghzRxEvent"/> when a 433 MHz transmitter fires
    /// (e.g. a remote control or key fob).
    /// Validates: stream event routing and <see cref="SubghzRxEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: trigger a 433 MHz transmitter near the
    /// Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task SubGhzRxStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.SubghzRxStartAsync(ct: timeout.Token);

        SubghzRxEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.True(firstEvent.Value.DurationUs > 0,
            "SubghzRxEvent.DurationUs must be positive");
    }
}
