using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.System;

/// <summary>
/// Integration tests for <see cref="FlipperRpcClient.DeviceInfoAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~DeviceInfoTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class DeviceInfoTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.DeviceInfoAsync"/> must round-trip and
    /// return a non-empty firmware version string.
    /// Validates: JSON serialisation, request-id routing, response
    /// deserialisation of the <c>firmware</c> field.
    /// </summary>
    [RequiresFlipperFact]
    public async Task DeviceInfo_ReturnsNonEmptyFirmware()
    {
        var response = await Client.DeviceInfoAsync();

        Assert.False(string.IsNullOrWhiteSpace(response.Firmware),
            "DeviceInfoResponse.Firmware must not be empty");
        Assert.False(string.IsNullOrWhiteSpace(response.Model),
            "DeviceInfoResponse.Model must not be empty");
        Assert.False(string.IsNullOrWhiteSpace(response.Uid),
            "DeviceInfoResponse.Uid must not be empty");
    }

    /// <summary>
    /// Calling <see cref="FlipperRpcClient.DeviceInfoAsync"/> multiple times
    /// must return the same firmware, model, and UID values each time.
    /// Validates: idempotency — device info is read-only and must be stable
    /// across repeated round-trips.
    /// </summary>
    [RequiresFlipperFact]
    public async Task DeviceInfo_MultipleCalls_ReturnConsistentValues()
    {
        var first = await Client.DeviceInfoAsync();
        var second = await Client.DeviceInfoAsync();

        Assert.Equal(first.Firmware, second.Firmware);
        Assert.Equal(first.Model, second.Model);
        Assert.Equal(first.Uid, second.Uid);
        Assert.Equal(first.Hardware, second.Hardware);
    }
}
