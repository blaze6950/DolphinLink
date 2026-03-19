using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Commands.Gpio;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests verifying cross-resource coexistence and conflict
/// behaviour of the RPC daemon's resource bitmask.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~ResourceConflictTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class ResourceConflictTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// IR receive and GPIO watch use different resource bits (RESOURCE_IR vs
    /// no resource lock) so both streams must open concurrently without
    /// conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrAndGpio_BothOpenConcurrently_BothSucceed()
    {
        await using var ir = await Client.IrReceiveStartAsync();
        await using var gpio = await Client.GpioWatchStartAsync(GpioPin.Pin6);

        Assert.NotEqual(0u, ir.StreamId);
        Assert.NotEqual(0u, gpio.StreamId);
        Assert.NotEqual(ir.StreamId, gpio.StreamId);
    }

    /// <summary>
    /// IR receive (RESOURCE_IR) and Sub-GHz RX (RESOURCE_SUBGHZ) use
    /// different resource bits so both streams must open concurrently without
    /// conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrAndSubGhz_BothOpenConcurrently_BothSucceed()
    {
        await using var ir = await Client.IrReceiveStartAsync();
        await using var subghz = await Client.SubGhzRxStartAsync();

        Assert.NotEqual(0u, ir.StreamId);
        Assert.NotEqual(0u, subghz.StreamId);
        Assert.NotEqual(ir.StreamId, subghz.StreamId);
    }

    /// <summary>
    /// IR receive (RESOURCE_IR) and NFC scan (RESOURCE_NFC) use different
    /// resource bits so both streams must open concurrently without conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrAndNfc_BothOpenConcurrently_BothSucceed()
    {
        await using var ir = await Client.IrReceiveStartAsync();
        await using var nfc = await Client.NfcScanStartAsync();

        Assert.NotEqual(0u, ir.StreamId);
        Assert.NotEqual(0u, nfc.StreamId);
        Assert.NotEqual(ir.StreamId, nfc.StreamId);
    }

    /// <summary>
    /// Opening 8 GPIO watch streams (one per available slot) and then
    /// attempting to open a ninth must fail with a
    /// <see cref="FlipperRpcException"/> carrying the <c>stream_table_full</c>
    /// error code.
    /// Validates: the MAX_STREAMS (8) limit in the daemon's stream table.
    /// </summary>
    [RequiresFlipperFact]
    public async Task StreamTableFull_NinthStream_ThrowsStreamTableFull()
    {
        // GPIO pins Pin1–Pin8 correspond to the 8 external GPIO header pins,
        // giving us 8 distinct concurrent streams with no resource-mask
        // conflicts.
        var pins = new[] { GpioPin.Pin1, GpioPin.Pin2, GpioPin.Pin3, GpioPin.Pin4,
                           GpioPin.Pin5, GpioPin.Pin6, GpioPin.Pin7, GpioPin.Pin8 };
        var streams = new List<IAsyncDisposable>(pins.Length);

        try
        {
            foreach (var pin in pins)
            {
                streams.Add(await Client.GpioWatchStartAsync(pin));
            }

            // All 8 slots are now occupied — the 9th must fail.
            var ex = await Assert.ThrowsAsync<FlipperRpcException>(
                () => Client.GpioWatchStartAsync(GpioPin.Pin1));

            Assert.Equal("stream_table_full", ex.ErrorCode);
        }
        finally
        {
            // Always clean up, even if the test fails partway through.
            foreach (var s in streams)
            {
                await s.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Sub-GHz RX (RESOURCE_SUBGHZ) and NFC scan (RESOURCE_NFC) use different
    /// resource bits so both streams must open concurrently without conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzAndNfc_BothOpenConcurrently_BothSucceed()
    {
        await using var subghz = await Client.SubGhzRxStartAsync();
        await using var nfc = await Client.NfcScanStartAsync();

        Assert.NotEqual(0u, subghz.StreamId);
        Assert.NotEqual(0u, nfc.StreamId);
        Assert.NotEqual(subghz.StreamId, nfc.StreamId);
    }

    /// <summary>
    /// LF RFID (RESOURCE_RFID) and iButton (RESOURCE_IBUTTON) use different
    /// resource bits so both streams must open concurrently without conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LfRfidAndIButton_BothOpenConcurrently_BothSucceed()
    {
        await using var rfid = await Client.LfRfidReadStartAsync();
        await using var ibutton = await Client.IButtonReadStartAsync();

        Assert.NotEqual(0u, rfid.StreamId);
        Assert.NotEqual(0u, ibutton.StreamId);
        Assert.NotEqual(rfid.StreamId, ibutton.StreamId);
    }

    /// <summary>
    /// IR receive (RESOURCE_IR) and LF RFID (RESOURCE_RFID) use different
    /// resource bits so both streams must open concurrently without conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task IrAndLfRfid_BothOpenConcurrently_BothSucceed()
    {
        await using var ir = await Client.IrReceiveStartAsync();
        await using var rfid = await Client.LfRfidReadStartAsync();

        Assert.NotEqual(0u, ir.StreamId);
        Assert.NotEqual(0u, rfid.StreamId);
        Assert.NotEqual(ir.StreamId, rfid.StreamId);
    }

    /// <summary>
    /// Sub-GHz RX (RESOURCE_SUBGHZ) and iButton (RESOURCE_IBUTTON) use
    /// different resource bits so both streams must open concurrently without
    /// conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SubGhzAndIButton_BothOpenConcurrently_BothSucceed()
    {
        await using var subghz = await Client.SubGhzRxStartAsync();
        await using var ibutton = await Client.IButtonReadStartAsync();

        Assert.NotEqual(0u, subghz.StreamId);
        Assert.NotEqual(0u, ibutton.StreamId);
        Assert.NotEqual(subghz.StreamId, ibutton.StreamId);
    }

    /// <summary>
    /// NFC scan (RESOURCE_NFC) and LF RFID (RESOURCE_RFID) use different
    /// resource bits so both streams must open concurrently without conflict.
    /// Validates: independent resource bits do not interfere.
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcAndLfRfid_BothOpenConcurrently_BothSucceed()
    {
        await using var nfc = await Client.NfcScanStartAsync();
        await using var rfid = await Client.LfRfidReadStartAsync();

        Assert.NotEqual(0u, nfc.StreamId);
        Assert.NotEqual(0u, rfid.StreamId);
        Assert.NotEqual(nfc.StreamId, rfid.StreamId);
    }
}
