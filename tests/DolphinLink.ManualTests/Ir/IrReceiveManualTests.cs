using DolphinLink.Client.Commands.Ir;
using DolphinLink.Client.Extensions;

namespace DolphinLink.ManualTests.Ir;

/// <summary>
/// Manual test for IR receive streaming that requires pointing a remote at the
/// Flipper.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IrReceiveManualTests"
/// </summary>
[Collection(DeviceCollection.Name)]
public sealed class IrReceiveManualTests(DeviceFixture fixture)
{
    private RpcClient Client => fixture.Client;

    /// <summary>
    /// After opening an IR receive stream we must be able to receive at least
    /// one <see cref="IrReceiveEvent"/> when an IR remote is pointed at the
    /// Flipper and a button is pressed.
    /// Validates: stream event routing and <see cref="IrReceiveEvent"/>
    /// deserialisation.
    ///
    /// Requires manual interaction: point an IR remote at the Flipper and press
    /// a button within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
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
}
