using FlipperZero.NET;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="FlipperRpcClient.PowerInfoAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~PowerInfoTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class PowerInfoTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.PowerInfoAsync"/> must return a charge
    /// percentage in the range 0–100.
    /// Validates: JSON serialisation, request-id routing, response
    /// deserialisation of the <c>charge</c> field.
    /// </summary>
    [RequiresFlipperFact]
    public async Task PowerInfo_ReturnsValidCharge()
    {
        var response = await Client.PowerInfoAsync();

        Assert.True(response.Charge <= 100,
            $"PowerInfoResponse.Charge must be 0–100, got {response.Charge}");
    }

    /// <summary>
    /// The response must include a positive battery voltage in millivolts.
    /// A connected Flipper is always powered so voltage must be non-zero.
    /// Validates: deserialisation of the <c>voltage_mv</c> field.
    /// </summary>
    [RequiresFlipperFact]
    public async Task PowerInfo_ReturnsPositiveVoltage()
    {
        var response = await Client.PowerInfoAsync();

        Assert.True(response.VoltageMv > 0,
            $"PowerInfoResponse.VoltageMv must be > 0, got {response.VoltageMv}");
    }
}
