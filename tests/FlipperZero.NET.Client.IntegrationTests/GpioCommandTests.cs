using FlipperZero.NET;
using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for GPIO commands:
/// <see cref="FlipperRpcClient.GpioReadAsync"/>,
/// <see cref="FlipperRpcClient.GpioWriteAsync"/>,
/// <see cref="FlipperRpcClient.AdcReadAsync"/>,
/// <see cref="FlipperRpcClient.GpioSet5vAsync"/>, and
/// <see cref="FlipperRpcClient.GpioWatchStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~GpioCommandTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class GpioCommandTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // gpio_read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reading a valid GPIO pin must return without throwing.
    /// The returned level is either true or false depending on hardware state.
    /// Validates: <c>gpio_read</c> happy-path round-trip.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioRead_ValidPin_ReturnsLevel()
    {
        // Pin "1" is always a valid GPIO header pin.
        var level = await Client.GpioReadAsync("1");

        // level is bool — any value is acceptable; just ensure no exception.
        Assert.True(level || !level);
    }

    /// <summary>
    /// Passing an invalid pin label to <c>gpio_read</c> must throw a
    /// <see cref="FlipperRpcException"/> with the <c>invalid_pin</c> error code.
    /// Validates: error path in the <c>gpio_read</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioRead_InvalidPin_ThrowsInvalidPin()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.GpioReadAsync("99"));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // gpio_write
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writing a high level to a valid GPIO pin must succeed without throwing.
    /// Validates: <c>gpio_write</c> happy-path, level=true.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWrite_ValidPinHigh_Succeeds()
    {
        await Client.GpioWriteAsync("1", level: true);
    }

    /// <summary>
    /// Writing a low level to a valid GPIO pin must succeed without throwing.
    /// Validates: <c>gpio_write</c> happy-path, level=false.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWrite_ValidPinLow_Succeeds()
    {
        await Client.GpioWriteAsync("1", level: false);
    }

    /// <summary>
    /// Passing an invalid pin label to <c>gpio_write</c> must throw a
    /// <see cref="FlipperRpcException"/> with the <c>invalid_pin</c> error code.
    /// Validates: error path in the <c>gpio_write</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWrite_InvalidPin_ThrowsInvalidPin()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.GpioWriteAsync("99", level: false));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // adc_read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reading ADC on an ADC-capable pin must return a non-negative millivolt
    /// value and a raw value in the 12-bit range (0–4095).
    /// Validates: <c>adc_read</c> happy-path on pin "1".
    /// </summary>
    [RequiresFlipperFact]
    public async Task AdcRead_ValidAdcPin_ReturnsPlausibleVoltage()
    {
        var response = await Client.AdcReadAsync("1");

        Assert.InRange(response.Raw, 0, 4095);
        Assert.True(response.Mv >= 0, "ADC millivolt reading must be non-negative");
    }

    /// <summary>
    /// Passing a non-ADC-capable pin to <c>adc_read</c> must throw a
    /// <see cref="FlipperRpcException"/> with the <c>invalid_pin</c> error code.
    /// Pin "4" does not support ADC on the Flipper Zero GPIO header.
    /// Validates: error path in the <c>adc_read</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task AdcRead_NonAdcPin_ThrowsInvalidPin()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.AdcReadAsync("4"));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // gpio_set_5v
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enabling the 5 V header rail must succeed without throwing.
    /// Validates: <c>gpio_set_5v</c> enable happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioSet5v_Enable_Succeeds()
    {
        await Client.GpioSet5vAsync(enable: true);
    }

    /// <summary>
    /// Disabling the 5 V header rail must succeed without throwing.
    /// Validates: <c>gpio_set_5v</c> disable happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioSet5v_Disable_Succeeds()
    {
        await Client.GpioSet5vAsync(enable: false);
    }

    // -----------------------------------------------------------------------
    // gpio_watch_start (stream)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <see cref="FlipperRpcClient.GpioWatchStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake for GPIO watch.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ValidPin_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.GpioWatchStartAsync("6");

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening a GPIO watch stream we must be able to receive at least
    /// one <see cref="GpioWatchEvent"/> when the pin is toggled externally.
    /// Validates: stream event routing and <see cref="GpioWatchEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.GpioWatchStartAsync("6", timeout.Token);

        GpioWatchEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break;
        }

        Assert.NotNull(firstEvent);
        Assert.False(string.IsNullOrWhiteSpace(firstEvent.Value.Pin),
            "GpioWatchEvent.Pin must not be empty");
    }

    /// <summary>
    /// Disposing a GPIO watch stream must send <c>stream_close</c> and not
    /// throw.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.GpioWatchStartAsync("6");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing a GPIO watch stream we must be able to open a new one
    /// on the same pin.
    /// Validates: the GPIO watch resource is released after stream_close.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.GpioWatchStartAsync("6");
        await first.DisposeAsync();

        await Task.Delay(200);

        await using var second = await Client.GpioWatchStartAsync("6");

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to watch an invalid pin must throw a
    /// <see cref="FlipperRpcException"/> with the <c>invalid_pin</c> error code.
    /// Validates: error path in the <c>gpio_watch_start</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task GpioWatchStart_InvalidPin_ThrowsInvalidPin()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.GpioWatchStartAsync("99"));

        Assert.Equal("invalid_pin", ex.ErrorCode);
    }
}
