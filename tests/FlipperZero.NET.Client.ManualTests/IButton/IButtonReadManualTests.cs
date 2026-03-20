using FlipperZero.NET.Commands.IButton;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.ManualTests.IButton;

/// <summary>
/// Manual test for iButton streaming that requires touching a compatible iButton
/// key to the Flipper's 1-Wire port.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IButtonReadManualTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class IButtonReadManualTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// After opening an iButton read stream we must be able to receive at
    /// least one <see cref="IButtonReadEvent"/> when a compatible iButton key
    /// is touched to the Flipper's 1-Wire port.
    /// Validates: stream event routing and <see cref="IButtonReadEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: touch a compatible iButton key to the
    /// Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task IButtonReadStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.IButtonReadStartAsync(timeout.Token);

        IButtonReadEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.NotEqual(IButtonProtocol.Unknown, firstEvent.Value.Type);
        Assert.NotNull(firstEvent.Value.Data);
        Assert.NotEmpty(firstEvent.Value.Data!);
    }
}
