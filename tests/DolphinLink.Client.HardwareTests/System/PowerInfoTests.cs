namespace DolphinLink.Client.HardwareTests.System;

/// <summary>
/// Integration tests for <see cref="RpcClient.PowerInfoAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~PowerInfoTests"
/// </summary>
[Collection(DeviceCollection.Name)]
public sealed class PowerInfoTests(DeviceFixture fixture)
{
    private RpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="RpcClient.PowerInfoAsync"/> must return a charge
    /// percentage in the range 0–100.
    /// Validates: JSON serialisation, request-id routing, response
    /// deserialisation of the <c>charge</c> field.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task PowerInfo_ReturnsValidValues()
    {
        var response = await Client.PowerInfoAsync();

        Assert.True(response.Charge <= 100,
            $"PowerInfoResponse.Charge must be 0–100, got {response.Charge}");
        Assert.True(response.VoltageMv > 0,
            $"PowerInfoResponse.VoltageMv must be > 0, got {response.VoltageMv}");
    }
}
