using FlipperZero.NET.Commands;

namespace FlipperZero.NET;

/// <summary>
/// Public convenience API.  Users call these methods and never touch
/// <c>SendAsync&lt;TCommand, TResponse&gt;</c> directly.
/// </summary>
public sealed partial class FlipperRpcClient
{
    // -----------------------------------------------------------------------
    // Ping
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends a <c>ping</c> and waits for the Flipper to respond with
    /// <c>{"pong":true}</c>.
    /// </summary>
    /// <returns><c>true</c> when the Flipper acknowledges the ping.</returns>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await SendAsync<PingCommand, PingResponse>(
            new PingCommand(), ct).ConfigureAwait(false);
        return response.Pong;
    }

    // -----------------------------------------------------------------------
    // System / device info
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns comprehensive device information: identity (name, model, UID, BLE MAC),
    /// firmware (version, origin, branch, git hash, build date), hardware OTP fields
    /// (revision, target, body, color, region, display, manufacture timestamp),
    /// and regulatory IDs (FCC, IC, MIC, SRRC, NCC).
    /// </summary>
    public Task<DeviceInfoResponse> DeviceInfoAsync(CancellationToken ct = default)
        => SendAsync<DeviceInfoCommand, DeviceInfoResponse>(new DeviceInfoCommand(), ct);

    /// <summary>Returns battery charge percentage, voltage, and charging state.</summary>
    public Task<PowerInfoResponse> PowerInfoAsync(CancellationToken ct = default)
        => SendAsync<PowerInfoCommand, PowerInfoResponse>(new PowerInfoCommand(), ct);

    /// <summary>Returns the current RTC date and time from the Flipper.</summary>
    public Task<DatetimeGetResponse> DatetimeGetAsync(CancellationToken ct = default)
        => SendAsync<DatetimeGetCommand, DatetimeGetResponse>(new DatetimeGetCommand(), ct);

    /// <summary>Sets the RTC date and time on the Flipper.</summary>
    public Task<DatetimeSetResponse> DatetimeSetAsync(
        uint year, uint month, uint day,
        uint hour, uint minute, uint second, uint weekday,
        CancellationToken ct = default)
        => SendAsync<DatetimeSetCommand, DatetimeSetResponse>(
            new DatetimeSetCommand(year, month, day, hour, minute, second, weekday), ct);

    /// <summary>Returns the RF region name and allowed frequency bands.</summary>
    public Task<RegionInfoResponse> RegionInfoAsync(CancellationToken ct = default)
        => SendAsync<RegionInfoCommand, RegionInfoResponse>(new RegionInfoCommand(), ct);

    /// <summary>
    /// Checks whether a frequency (in Hz) is permitted in the Flipper's current region.
    /// </summary>
    public async Task<bool> FrequencyIsAllowedAsync(uint freq, CancellationToken ct = default)
    {
        var r = await SendAsync<FrequencyIsAllowedCommand, FrequencyIsAllowedResponse>(
            new FrequencyIsAllowedCommand(freq), ct).ConfigureAwait(false);
        return r.Allowed;
    }

    // -----------------------------------------------------------------------
    // GPIO
    // -----------------------------------------------------------------------

    /// <summary>Reads the current digital level of a GPIO pin (label <c>"1"</c>–<c>"8"</c>).</summary>
    public async Task<bool> GpioReadAsync(string pin, CancellationToken ct = default)
    {
        var r = await SendAsync<GpioReadCommand, GpioReadResponse>(
            new GpioReadCommand(pin), ct).ConfigureAwait(false);
        return r.Level;
    }

    /// <summary>Drives a GPIO pin high or low.</summary>
    public Task<GpioWriteResponse> GpioWriteAsync(string pin, bool level, CancellationToken ct = default)
        => SendAsync<GpioWriteCommand, GpioWriteResponse>(new GpioWriteCommand(pin, level), ct);

    /// <summary>
    /// Reads the ADC voltage on a GPIO pin.
    /// Supported pins: <c>"1"</c>, <c>"2"</c>, <c>"3"</c>, <c>"6"</c>, <c>"7"</c>.
    /// </summary>
    public Task<AdcReadResponse> AdcReadAsync(string pin, CancellationToken ct = default)
        => SendAsync<AdcReadCommand, AdcReadResponse>(new AdcReadCommand(pin), ct);

    /// <summary>Enables or disables the 5 V header supply rail.</summary>
    public Task<GpioSet5vResponse> GpioSet5vAsync(bool enable, CancellationToken ct = default)
        => SendAsync<GpioSet5vCommand, GpioSet5vResponse>(new GpioSet5vCommand(enable), ct);

    /// <summary>
    /// Watches a GPIO pin for level changes.
    /// </summary>
    /// <param name="pin">
    /// Physical GPIO header pin label: <c>"1"</c> through <c>"8"</c>.
    /// </param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="GpioWatchEvent"/>
    /// on each rising or falling edge.  Dispose the stream to remove the interrupt.
    /// </returns>
    public Task<RpcStream<GpioWatchEvent>> GpioWatchStartAsync(string pin, CancellationToken ct = default)
        => SendStreamAsync<GpioWatchStartCommand, GpioWatchEvent>(new GpioWatchStartCommand(pin), ct);

    // -----------------------------------------------------------------------
    // IR
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transmits a decoded IR signal (protocol + address + command).
    /// </summary>
    /// <param name="protocol">Protocol name, e.g. <c>"NEC"</c>.</param>
    public Task<IrTxResponse> IrTxAsync(string protocol, uint address, uint command, CancellationToken ct = default)
        => SendAsync<IrTxCommand, IrTxResponse>(new IrTxCommand(protocol, address, command), ct);

    /// <summary>
    /// Transmits a raw IR timing array.
    /// </summary>
    /// <param name="timings">
    /// Microsecond durations, alternating mark/space.
    /// </param>
    public Task<IrTxRawResponse> IrTxRawAsync(uint[] timings, CancellationToken ct = default)
        => SendAsync<IrTxRawCommand, IrTxRawResponse>(new IrTxRawCommand(timings), ct);

    /// <summary>
    /// Starts the IR receiver on the Flipper.
    /// </summary>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="IrReceiveEvent"/>
    /// for every decoded IR signal.  Dispose the stream to stop receiving.
    /// </returns>
    public Task<RpcStream<IrReceiveEvent>> IrReceiveStartAsync(CancellationToken ct = default)
        => SendStreamAsync<IrReceiveStartCommand, IrReceiveEvent>(new IrReceiveStartCommand(), ct);

    // -----------------------------------------------------------------------
    // Sub-GHz
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transmits a raw OOK Sub-GHz packet at the specified frequency.
    /// </summary>
    public Task<SubGhzTxResponse> SubGhzTxAsync(uint freq, uint[] timings, CancellationToken ct = default)
        => SendAsync<SubGhzTxCommand, SubGhzTxResponse>(new SubGhzTxCommand(freq, timings), ct);

    /// <summary>
    /// Returns the current RSSI (in dBm) at the given frequency.
    /// </summary>
    public async Task<int> SubGhzGetRssiAsync(uint freq, CancellationToken ct = default)
    {
        var r = await SendAsync<SubGhzGetRssiCommand, SubGhzGetRssiResponse>(
            new SubGhzGetRssiCommand(freq), ct).ConfigureAwait(false);
        return r.Rssi;
    }

    /// <summary>
    /// Starts Sub-GHz OOK raw receive.
    /// </summary>
    /// <param name="freq">
    /// Carrier frequency in Hz.  Defaults to 433.92 MHz (<c>null</c>).
    /// </param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="SubGhzRxEvent"/>
    /// for each raw OOK pulse.  Dispose the stream to stop receiving and release the radio.
    /// </returns>
    public Task<RpcStream<SubGhzRxEvent>> SubGhzRxStartAsync(uint? freq = null, CancellationToken ct = default)
        => SendStreamAsync<SubGhzRxStartCommand, SubGhzRxEvent>(new SubGhzRxStartCommand(freq), ct);

    // -----------------------------------------------------------------------
    // NFC scan
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts NFC protocol scanning on the Flipper.
    /// </summary>
    /// <remarks>
    /// Uses <c>NfcScanner</c> which detects protocol type only.
    /// No UID is available without a full anti-collision poller (<c>NfcPoller</c>).
    /// </remarks>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="NfcScanEvent"/>
    /// for each detected NFC tag.  Dispose the stream to stop scanning and release the NFC hardware.
    /// </returns>
    public Task<RpcStream<NfcScanEvent>> NfcScanStartAsync(CancellationToken ct = default)
        => SendStreamAsync<NfcScanStartCommand, NfcScanEvent>(new NfcScanStartCommand(), ct);

    // -----------------------------------------------------------------------
    // Notifications / LED / vibro / speaker
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets an LED colour channel intensity.
    /// </summary>
    /// <param name="color">One of <c>"red"</c>, <c>"green"</c>, or <c>"blue"</c>.</param>
    /// <param name="value">Intensity 0–255.</param>
    public Task<LedSetResponse> LedSetAsync(string color, byte value, CancellationToken ct = default)
        => SendAsync<LedSetCommand, LedSetResponse>(new LedSetCommand(color, value), ct);

    /// <summary>Enables or disables the vibration motor.</summary>
    public Task<VibroResponse> VibroAsync(bool enable, CancellationToken ct = default)
        => SendAsync<VibroCommand, VibroResponse>(new VibroCommand(enable), ct);

    /// <summary>
    /// Starts a continuous tone on the piezo speaker.
    /// Call <see cref="SpeakerStopAsync"/> to release the speaker resource.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <param name="volume">Volume 0–255.</param>
    public Task<SpeakerStartResponse> SpeakerStartAsync(uint freq, byte volume, CancellationToken ct = default)
        => SendAsync<SpeakerStartCommand, SpeakerStartResponse>(new SpeakerStartCommand(freq, volume), ct);

    /// <summary>Stops the piezo speaker and releases the speaker resource.</summary>
    public Task<SpeakerStopResponse> SpeakerStopAsync(CancellationToken ct = default)
        => SendAsync<SpeakerStopCommand, SpeakerStopResponse>(new SpeakerStopCommand(), ct);

    /// <summary>Sets the LCD backlight brightness (0–255).</summary>
    public Task<BacklightResponse> BacklightAsync(byte value, CancellationToken ct = default)
        => SendAsync<BacklightCommand, BacklightResponse>(new BacklightCommand(value), ct);

    // -----------------------------------------------------------------------
    // Storage
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns filesystem capacity and free space.
    /// </summary>
    /// <param name="path">Storage root, e.g. <c>"/int"</c> or <c>"/ext"</c>.</param>
    public Task<StorageInfoResponse> StorageInfoAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageInfoCommand, StorageInfoResponse>(new StorageInfoCommand(path), ct);

    /// <summary>Lists the contents of a directory on the Flipper.</summary>
    public Task<StorageListResponse> StorageListAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageListCommand, StorageListResponse>(new StorageListCommand(path), ct);

    /// <summary>
    /// Reads a file from the Flipper.
    /// The response <see cref="StorageReadResponse.Data"/> is Base64-encoded file content.
    /// </summary>
    public Task<StorageReadResponse> StorageReadAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageReadCommand, StorageReadResponse>(new StorageReadCommand(path), ct);

    /// <summary>
    /// Writes data to a file on the Flipper (creates or overwrites).
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="base64Data">File content encoded as Base64.</param>
    public Task<StorageWriteResponse> StorageWriteAsync(string path, string base64Data, CancellationToken ct = default)
        => SendAsync<StorageWriteCommand, StorageWriteResponse>(new StorageWriteCommand(path, base64Data), ct);

    /// <summary>Creates a directory on the Flipper filesystem.</summary>
    public Task<StorageMkdirResponse> StorageMkdirAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageMkdirCommand, StorageMkdirResponse>(new StorageMkdirCommand(path), ct);

    /// <summary>Removes a file or empty directory from the Flipper filesystem.</summary>
    public Task<StorageRemoveResponse> StorageRemoveAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageRemoveCommand, StorageRemoveResponse>(new StorageRemoveCommand(path), ct);

    /// <summary>Returns metadata (size and is_dir flag) for a path on the Flipper.</summary>
    public Task<StorageStatResponse> StorageStatAsync(string path, CancellationToken ct = default)
        => SendAsync<StorageStatCommand, StorageStatResponse>(new StorageStatCommand(path), ct);

    // -----------------------------------------------------------------------
    // LF RFID
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a streaming LF RFID read session.
    /// </summary>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="LfRfidReadEvent"/>
    /// for each detected tag.  Dispose to stop reading and release the RFID hardware.
    /// </returns>
    public Task<RpcStream<LfRfidReadEvent>> LfRfidReadStartAsync(CancellationToken ct = default)
        => SendStreamAsync<LfRfidReadStartCommand, LfRfidReadEvent>(new LfRfidReadStartCommand(), ct);

    // -----------------------------------------------------------------------
    // iButton
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a streaming iButton read session.
    /// </summary>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="IButtonReadEvent"/>
    /// for each detected key.  Dispose to stop reading and release the iButton hardware.
    /// </returns>
    public Task<RpcStream<IButtonReadEvent>> IButtonReadStartAsync(CancellationToken ct = default)
        => SendStreamAsync<IButtonReadStartCommand, IButtonReadEvent>(new IButtonReadStartCommand(), ct);

    // -----------------------------------------------------------------------
    // Stream close (also called internally by RpcStream<T>.DisposeAsync)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Explicitly closes a stream by id.
    /// Prefer disposing the <see cref="RpcStream{TEvent}"/> returned by the
    /// stream-open methods instead of calling this directly.
    /// </summary>
    public async Task StreamCloseAsync(uint streamId, CancellationToken ct = default)
    {
        await SendAsync<StreamCloseCommand, StreamCloseResponse>(
            new StreamCloseCommand(streamId), ct).ConfigureAwait(false);
    }
}
