using FlipperZero.NET.Commands.Rfid;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.Rfid;

/// <summary>
/// Manual test for LF RFID streaming that requires presenting a compatible
/// RFID tag to the Flipper's RFID coil.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~LfRfidReadManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class LfRfidReadManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening an LF RFID read stream we must be able to receive at
    /// least one <see cref="LfrfidReadEvent"/> when a compatible RFID tag is
    /// presented to the Flipper's RFID coil.
    /// Validates: stream event routing and <see cref="LfrfidReadEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: present a compatible RFID tag to the
    /// Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task LfRfidReadStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.LfrfidReadStartAsync(timeout.Token);

        LfrfidReadEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.NotEqual(LfRfidProtocol.Unknown, firstEvent.Value.Type);
        Assert.NotNull(firstEvent.Value.Data);
        Assert.NotEmpty(firstEvent.Value.Data!);
    }
}
