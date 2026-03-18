using FlipperZero.NET.Commands;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for notification commands:
/// <see cref="FlipperRpcClient.LedSetAsync"/>,
/// <see cref="FlipperRpcClient.LedSetRgbAsync"/>,
/// <see cref="FlipperRpcClient.VibroAsync"/>,
/// <see cref="FlipperRpcClient.SpeakerStartAsync"/>,
/// <see cref="FlipperRpcClient.SpeakerStopAsync"/>, and
/// <see cref="FlipperRpcClient.BacklightAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~NotificationTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class NotificationTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    // -----------------------------------------------------------------------
    // led_set
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting the red LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Red"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Red_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Red, 255);
    }

    /// <summary>
    /// Setting the green LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Green"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Green_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Green, 255);
    }

    /// <summary>
    /// Setting the blue LED to maximum intensity must succeed without throwing.
    /// Validates: <c>led_set</c> happy-path with <see cref="LedChannel.Blue"/>.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_Blue_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Blue, 255);
    }

    /// <summary>
    /// Setting an LED to zero intensity must succeed without throwing.
    /// Validates: <c>led_set</c> with value=0 (off).
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSet_ZeroValue_Succeeds()
    {
        await Client.LedSetAsync(LedChannel.Red, 0);
    }

    // -----------------------------------------------------------------------
    // led_set_rgb
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting all three LED channels to a specific colour in one call must
    /// succeed without throwing.
    /// Validates: <c>led_set_rgb</c> happy-path with a non-trivial colour.
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSetRgb_Succeeds()
    {
        await Client.LedSetRgbAsync(red: 255, green: 128, blue: 0);
    }

    /// <summary>
    /// Setting all three LED channels to zero in one call must succeed without
    /// throwing.
    /// Validates: <c>led_set_rgb</c> with all channels off (black).
    /// </summary>
    [RequiresFlipperFact]
    public async Task LedSetRgb_Black_Succeeds()
    {
        await Client.LedSetRgbAsync(red: 0, green: 0, blue: 0);
    }

    // -----------------------------------------------------------------------
    // vibro
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enabling the vibration motor must succeed without throwing.
    /// Validates: <c>vibro</c> enable happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Vibro_EnableAndDisable_Succeeds()
    {
        await Client.VibroAsync(enable: true);
        await Task.Delay(1000);
        await Client.VibroAsync(enable: false);
    }

    // -----------------------------------------------------------------------
    // speaker_start / speaker_stop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starting the piezo speaker at A4 (440 Hz) must succeed without throwing.
    /// Validates: <c>speaker_start</c> happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStart_ValidFreqAndVolume_Succeeds()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        await Task.Delay(1000);
        // Clean up
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Stopping the speaker when it is active must succeed without throwing.
    /// Validates: <c>speaker_stop</c> happy-path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStop_WhenActive_Succeeds()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Stopping the speaker when it was never started must succeed without
    /// throwing (the daemon's <c>speaker_stop</c> is idempotent).
    /// Validates: idempotent stop path in the <c>speaker_stop</c> handler.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStop_WhenNotActive_Succeeds()
    {
        // Ensure speaker is off first
        await Client.SpeakerStopAsync();
        // Call again — must still succeed
        await Client.SpeakerStopAsync();
    }

    /// <summary>
    /// Attempting to start the speaker a second time while it is already
    /// active must throw a <see cref="FlipperRpcException"/> with the
    /// <c>resource_busy</c> error code.
    /// Validates: the RESOURCE_SPEAKER bitmask enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task SpeakerStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await Client.SpeakerStartAsync(freq: 440, volume: 128);
        try
        {
            var ex = await Assert.ThrowsAsync<FlipperRpcException>(
                () => Client.SpeakerStartAsync(freq: 880, volume: 128));

            Assert.Equal("resource_busy", ex.ErrorCode);
        }
        finally
        {
            await Client.SpeakerStopAsync();
        }
    }

    // -----------------------------------------------------------------------
    // backlight
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting the backlight to maximum brightness must succeed without
    /// throwing.
    /// Validates: <c>backlight</c> happy-path with value=255.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_MaxValue_Succeeds()
    {
        await Client.BacklightAsync(255);
    }

    /// <summary>
    /// Setting the backlight to zero brightness must succeed without throwing.
    /// Validates: <c>backlight</c> happy-path with value=0.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_ZeroValue_Succeeds()
    {
        await Client.BacklightAsync(0);
    }

    /// <summary>
    /// Setting the backlight to mid-range brightness must succeed without
    /// throwing.
    /// Validates: <c>backlight</c> happy-path with an intermediate value.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Backlight_MidValue_Succeeds()
    {
        await Client.BacklightAsync(128);
    }
}
