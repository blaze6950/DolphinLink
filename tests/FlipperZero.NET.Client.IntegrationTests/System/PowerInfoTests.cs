using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.System;

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
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task PowerInfo_ReturnsValidValues()
    {
        var response = await Client.PowerInfoAsync();

        Assert.True(response.Charge <= 100,
            $"PowerInfoResponse.Charge must be 0–100, got {response.Charge}");
        Assert.True(response.VoltageMv > 0,
            $"PowerInfoResponse.VoltageMv must be > 0, got {response.VoltageMv}");
    }
}
