using FlipperZero.NET;
using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for IR transmit commands:
/// <see cref="FlipperRpcClient.IrTxAsync"/> and
/// <see cref="FlipperRpcClient.IrTxRawAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~IrTxTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class IrTxTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // ir_tx (decoded)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transmitting a decoded IR signal with a known protocol must succeed
    /// without throwing.
    /// Validates: <c>ir_tx</c> happy-path round-trip with the NEC protocol.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrTx_ValidNecSignal_Succeeds()
    {
        // NEC protocol, address=0x00, command=0x0D (power)
        await Client.IrTxAsync("NEC", address: 0x00, command: 0x0D);
    }

    /// <summary>
    /// Transmitting with an unknown protocol name must throw a
    /// <see cref="FlipperRpcException"/> with the <c>unknown_protocol</c>
    /// error code.
    /// Validates: error path in the <c>ir_tx</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrTx_UnknownProtocol_ThrowsUnknownProtocol()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.IrTxAsync("BOGUS_PROTOCOL", address: 0, command: 0));

        Assert.Equal("unknown_protocol", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // ir_tx_raw
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transmitting a raw IR timing array must succeed without throwing.
    /// Validates: <c>ir_tx_raw</c> happy-path with a minimal NEC-like burst.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrTxRaw_ValidTimings_Succeeds()
    {
        // A simple two-element mark/space pattern (9 ms mark, 4.5 ms space)
        var timings = new uint[] { 9000, 4500, 560, 560 };

        await Client.IrTxRawAsync(timings);
    }

    /// <summary>
    /// Passing an empty timings array to <c>ir_tx_raw</c> must throw a
    /// <see cref="FlipperRpcException"/> because the daemon's
    /// <c>json_extract_uint32_array</c> returns false for an empty array.
    /// Validates: error path in the <c>ir_tx_raw</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrTxRaw_EmptyTimings_ThrowsMissingTimings()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.IrTxRawAsync(Array.Empty<uint>()));

        Assert.Equal("missing_timings", ex.ErrorCode);
    }
}
