using DolphinLink.Client.Exceptions;

namespace DolphinLink.Client.HardwareTests.Gpio;

/// <summary>
/// Integration tests for GPIO commands:
/// <see cref="RpcClient.GpioReadAsync"/>,
/// <see cref="RpcClient.GpioWriteAsync"/>,
/// <see cref="RpcClient.AdcReadAsync"/>, and
/// <see cref="RpcClient.GpioSet5vAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~GpioCommandTests"
/// </summary>
[Collection(DeviceCollection.Name)]
public sealed class GpioCommandTests(DeviceFixture fixture)
{
    private RpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // gpio_read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reading a valid GPIO pin must return without throwing.
    /// The returned level is either true or false depending on hardware state.
    /// Validates: <c>gpio_read</c> happy-path round-trip.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task GpioRead_ValidPin_ReturnsLevel()
    {
        var level = await Client.GpioReadAsync(GpioPin.Pin1);

        // level is bool — any value is acceptable; just ensure no exception.
        Assert.True(level || !level);
    }

    // -----------------------------------------------------------------------
    // gpio_write
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writing a high level to a valid GPIO pin must succeed without throwing.
    /// Validates: <c>gpio_write</c> happy-path, level=true.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task GpioWrite_ValidPinHigh_Succeeds()
    {
        await Client.GpioWriteAsync(GpioPin.Pin1, level: true);
    }

    /// <summary>
    /// Writing a low level to a valid GPIO pin must succeed without throwing.
    /// Validates: <c>gpio_write</c> happy-path, level=false.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task GpioWrite_ValidPinLow_Succeeds()
    {
        await Client.GpioWriteAsync(GpioPin.Pin1, level: false);
    }

    // -----------------------------------------------------------------------
    // adc_read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reading ADC on an ADC-capable pin must return a non-negative millivolt
    /// value and a raw value in the 12-bit range (0–4095).
    /// Validates: <c>adc_read</c> happy-path on pin 1.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task AdcRead_ValidAdcPin_ReturnsPlausibleVoltage()
    {
        var response = await Client.AdcReadAsync(GpioPin.Pin1);

        Assert.InRange(response.Raw, 0u, 4095u);
        Assert.True(response.Mv >= 0, "ADC millivolt reading must be non-negative");
    }

    /// <summary>
    /// Passing a non-ADC-capable pin to <c>adc_read</c> must throw a
    /// <see cref="RpcException"/> with the <c>invalid_pin</c> error code.
    /// Pin 4 does not support ADC on the Flipper Zero GPIO header.
    /// Validates: error path in the <c>adc_read</c> handler.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task AdcRead_NonAdcPin_ThrowsInvalidPin()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => Client.AdcReadAsync(GpioPin.Pin4));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // gpio_set_5v
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enabling the 5 V header rail must succeed without throwing.
    /// Validates: <c>gpio_set_5v</c> enable happy-path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task GpioSet5v_Enable_Succeeds()
    {
        await Client.GpioSet5vAsync(enable: true);
    }

    /// <summary>
    /// Disabling the 5 V header rail must succeed without throwing.
    /// Validates: <c>gpio_set_5v</c> disable happy-path.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresDeviceFact]
    public async Task GpioSet5v_Disable_Succeeds()
    {
        await Client.GpioSet5vAsync(enable: false);
    }
}
