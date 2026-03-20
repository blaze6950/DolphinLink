using FlipperZero.NET.Commands.Nfc;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.Nfc;

/// <summary>
/// Manual test for NFC scanning that requires tapping an NFC card against the
/// Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~NfcScanManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class NfcScanManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening an NFC scan stream we must be able to receive at least
    /// one <see cref="NfcScanEvent"/> when an NFC card is held near the
    /// Flipper's NFC antenna.
    /// Validates: stream event routing and <see cref="NfcScanEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: tap an NFC card against the Flipper within
    /// 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task NfcScanStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.NfcScanStartAsync(timeout.Token);

        NfcScanEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.NotEqual(NfcProtocol.Unknown, firstEvent.Value.Protocol);
    }
}
