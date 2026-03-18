namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="FlipperRpcClient.RegionInfoAsync"/> and
/// <see cref="FlipperRpcClient.FrequencyIsAllowedAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~RegionAndFrequencyTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class RegionAndFrequencyTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.RegionInfoAsync"/> must return a non-empty
    /// region name string identifying the RF regulatory region.
    /// Validates: JSON serialisation, request-id routing, and deserialisation
    /// of the <c>region</c> field.
    /// </summary>
    [RequiresFlipperFact]
    public async Task RegionInfo_ReturnsNonEmptyRegion()
    {
        var response = await Client.RegionInfoAsync();

        Assert.False(string.IsNullOrWhiteSpace(response.Region),
            "RegionInfoResponse.Region must not be empty");
    }

    /// <summary>
    /// 433.92 MHz (433 920 000 Hz) is a globally common ISM band frequency
    /// that is permitted in virtually all regulatory regions supported by the
    /// Flipper.  The daemon must return <c>true</c> for this frequency.
    /// Validates: <c>frequency_is_allowed</c> allowed path and response
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task FrequencyIsAllowed_433MHz_ReturnsTrue()
    {
        const uint freq433 = 433_920_000;

        var allowed = await Client.FrequencyIsAllowedAsync(freq433);

        Assert.True(allowed,
            $"433.92 MHz ({freq433} Hz) must be allowed in the device's current region");
    }

    /// <summary>
    /// A frequency below the CC1101 tuning range (10 MHz) must not be
    /// permitted in any region — the CC1101 radio chip only supports
    /// 300–928 MHz.
    /// Validates: <c>frequency_is_allowed</c> denied path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task FrequencyIsAllowed_OutOfBandFreq_ReturnsFalse()
    {
        // 10 MHz is far below the CC1101 tuning range (300–928 MHz) so it
        // must be denied regardless of the device's regulatory region.
        const uint freq10MHz = 10_000_000;

        var allowed = await Client.FrequencyIsAllowedAsync(freq10MHz);

        Assert.False(allowed,
            $"10 MHz ({freq10MHz} Hz) must not be allowed — it is below the CC1101 tuning range");
    }
}
