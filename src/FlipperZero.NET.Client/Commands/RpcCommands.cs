using System.Text.Json;
using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands;

// ---------------------------------------------------------------------------
// Ping
// ---------------------------------------------------------------------------

/// <summary>Sends a <c>ping</c> command and returns a <see cref="PingResponse"/>.</summary>
public readonly struct PingCommand : IRpcCommand<PingResponse>
{
    public string CommandName => "ping";

    /// <summary>Ping has no arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="PingCommand"/>.</summary>
public readonly struct PingResponse : IRpcCommandResponse
{
    /// <summary>Always <c>true</c> when the Flipper responds to a ping.</summary>
    [JsonPropertyName("pong")]
    public bool Pong { get; init; }
}

// ---------------------------------------------------------------------------
// IR receive start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens an IR receive stream.  Each decoded IR signal is delivered as an
/// <see cref="IrReceiveEvent"/>.
/// </summary>
public readonly struct IrReceiveStartCommand : IRpcStreamCommand<IrReceiveEvent>
{
    public string CommandName => "ir_receive_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>A decoded IR signal received from the IR receiver.</summary>
public readonly struct IrReceiveEvent : IRpcCommandResponse
{
    /// <summary>Protocol name, e.g. <c>"NEC"</c> or <c>"Samsung32"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }

    /// <summary>Device address field from the decoded IR frame.</summary>
    [JsonPropertyName("address")]
    public uint Address { get; init; }

    /// <summary>Command field from the decoded IR frame.</summary>
    [JsonPropertyName("command")]
    public uint Command { get; init; }

    /// <summary><c>true</c> if this is a repeat frame (button held down).</summary>
    [JsonPropertyName("repeat")]
    public bool Repeat { get; init; }
}

// ---------------------------------------------------------------------------
// IR TX (decoded)
// ---------------------------------------------------------------------------

/// <summary>Transmits a decoded IR signal (protocol + address + command).</summary>
public readonly struct IrTxCommand : IRpcCommand<IrTxResponse>
{
    public IrTxCommand(string protocol, uint address, uint command)
    {
        Protocol = protocol;
        Address = address;
        Command = command;
    }

    /// <summary>Protocol name, e.g. <c>"NEC"</c>.</summary>
    public string Protocol { get; }

    /// <summary>Device address.</summary>
    public uint Address { get; }

    /// <summary>Command code.</summary>
    public uint Command { get; }

    public string CommandName => "ir_tx";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("protocol", Protocol);
        writer.WriteNumber("address", Address);
        writer.WriteNumber("command", Command);
    }
}

/// <summary>Response to <see cref="IrTxCommand"/>.</summary>
public readonly struct IrTxResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// IR TX raw
// ---------------------------------------------------------------------------

/// <summary>
/// Transmits a raw IR timing array.
/// Each element is a pulse duration in microseconds, alternating mark/space.
/// </summary>
public readonly struct IrTxRawCommand : IRpcCommand<IrTxRawResponse>
{
    public IrTxRawCommand(uint[] timings) => Timings = timings;

    /// <summary>Timing array in microseconds (mark, space, mark, space, …).</summary>
    public uint[] Timings { get; }

    public string CommandName => "ir_tx_raw";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("timings");
        foreach (var t in Timings)
        {
            writer.WriteNumberValue(t);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Response to <see cref="IrTxRawCommand"/>.</summary>
public readonly struct IrTxRawResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// GPIO watch start
// ---------------------------------------------------------------------------

/// <summary>
/// Watches a GPIO pin for level changes.  Each transition is delivered as a
/// <see cref="GpioWatchEvent"/>.
/// </summary>
public readonly struct GpioWatchStartCommand : IRpcStreamCommand<GpioWatchEvent>
{
    /// <param name="pin">
    /// Physical GPIO header pin label, e.g. <c>"1"</c> through <c>"8"</c>.
    /// Maps to the <c>gpio_ext_*</c> symbols on the Flipper Zero expansion connector.
    /// </param>
    public GpioWatchStartCommand(string pin) => Pin = pin;

    /// <summary>Pin label as sent in the <c>"pin"</c> JSON field.</summary>
    public string Pin { get; }

    public string CommandName => "gpio_watch_start";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", Pin);
    }
}

/// <summary>A GPIO level-change event.</summary>
public readonly struct GpioWatchEvent : IRpcCommandResponse
{
    /// <summary>Pin label that changed, e.g. <c>"1"</c>.</summary>
    [JsonPropertyName("pin")]
    public string? Pin { get; init; }

    /// <summary><c>true</c> = high; <c>false</c> = low.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }
}

// ---------------------------------------------------------------------------
// GPIO read
// ---------------------------------------------------------------------------

/// <summary>Reads the current digital level of a GPIO pin.</summary>
public readonly struct GpioReadCommand : IRpcCommand<GpioReadResponse>
{
    public GpioReadCommand(string pin) => Pin = pin;

    /// <summary>Pin label, e.g. <c>"1"</c> through <c>"8"</c>.</summary>
    public string Pin { get; }

    public string CommandName => "gpio_read";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", Pin);
    }
}

/// <summary>Response to <see cref="GpioReadCommand"/>.</summary>
public readonly struct GpioReadResponse : IRpcCommandResponse
{
    /// <summary><c>true</c> = high; <c>false</c> = low.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }
}

// ---------------------------------------------------------------------------
// GPIO write
// ---------------------------------------------------------------------------

/// <summary>Sets the digital output level of a GPIO pin.</summary>
public readonly struct GpioWriteCommand : IRpcCommand<GpioWriteResponse>
{
    public GpioWriteCommand(string pin, bool level)
    {
        Pin = pin;
        Level = level;
    }

    /// <summary>Pin label, e.g. <c>"1"</c> through <c>"8"</c>.</summary>
    public string Pin { get; }

    /// <summary><c>true</c> = drive high; <c>false</c> = drive low.</summary>
    public bool Level { get; }

    public string CommandName => "gpio_write";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", Pin);
        writer.WriteBoolean("level", Level);
    }
}

/// <summary>Response to <see cref="GpioWriteCommand"/>.</summary>
public readonly struct GpioWriteResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// ADC read
// ---------------------------------------------------------------------------

/// <summary>Reads the ADC voltage on a GPIO pin that supports analog input.</summary>
public readonly struct AdcReadCommand : IRpcCommand<AdcReadResponse>
{
    public AdcReadCommand(string pin) => Pin = pin;

    /// <summary>ADC-capable pin label: <c>"1"</c>, <c>"2"</c>, <c>"3"</c>, <c>"6"</c>, or <c>"7"</c>.</summary>
    public string Pin { get; }

    public string CommandName => "adc_read";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("pin", Pin);
    }
}

/// <summary>Response to <see cref="AdcReadCommand"/>.</summary>
public readonly struct AdcReadResponse : IRpcCommandResponse
{
    /// <summary>Raw 12-bit ADC value (0–4095).</summary>
    [JsonPropertyName("raw")]
    public ushort Raw { get; init; }

    /// <summary>Voltage in millivolts.</summary>
    [JsonPropertyName("mv")]
    public int Mv { get; init; }
}

// ---------------------------------------------------------------------------
// GPIO set 5 V rail
// ---------------------------------------------------------------------------

/// <summary>Enables or disables the 5 V header supply rail.</summary>
public readonly struct GpioSet5vCommand : IRpcCommand<GpioSet5vResponse>
{
    public GpioSet5vCommand(bool enable) => Enable = enable;

    /// <summary><c>true</c> to enable the 5 V rail; <c>false</c> to disable it.</summary>
    public bool Enable { get; }

    public string CommandName => "gpio_set_5v";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteBoolean("enable", Enable);
    }
}

/// <summary>Response to <see cref="GpioSet5vCommand"/>.</summary>
public readonly struct GpioSet5vResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// SubGHz RX start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a Sub-GHz OOK receive stream.  Each raw pulse is delivered as a
/// <see cref="SubGhzRxEvent"/>.
/// </summary>
public readonly struct SubGhzRxStartCommand : IRpcStreamCommand<SubGhzRxEvent>
{
    /// <param name="freq">
    /// Optional carrier frequency in Hz (e.g. <c>433920000</c>).
    /// Defaults to 433.92 MHz when <c>null</c>.
    /// </param>
    public SubGhzRxStartCommand(uint? freq = null) => Freq = freq;

    /// <summary>Carrier frequency in Hz, or <c>null</c> to use the default (433.92 MHz).</summary>
    public uint? Freq { get; }

    public string CommandName => "subghz_rx_start";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        if(Freq.HasValue)
            writer.WriteNumber("freq", Freq.Value);
    }
}

/// <summary>A raw Sub-GHz OOK pulse.</summary>
public readonly struct SubGhzRxEvent : IRpcCommandResponse
{
    /// <summary><c>true</c> = carrier on; <c>false</c> = carrier off.</summary>
    [JsonPropertyName("level")]
    public bool Level { get; init; }

    /// <summary>Pulse duration in microseconds.</summary>
    [JsonPropertyName("duration_us")]
    public uint DurationUs { get; init; }
}

// ---------------------------------------------------------------------------
// SubGHz TX (raw timing array)
// ---------------------------------------------------------------------------

/// <summary>Transmits a raw OOK Sub-GHz packet at the specified frequency.</summary>
public readonly struct SubGhzTxCommand : IRpcCommand<SubGhzTxResponse>
{
    public SubGhzTxCommand(uint freq, uint[] timings)
    {
        Freq = freq;
        Timings = timings;
    }

    /// <summary>Carrier frequency in Hz, e.g. <c>433920000</c>.</summary>
    public uint Freq { get; }

    /// <summary>OOK timing array in microseconds (mark, space, mark, space, …).</summary>
    public uint[] Timings { get; }

    public string CommandName => "subghz_tx";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
        writer.WriteStartArray("timings");
        foreach (var t in Timings)
        {
            writer.WriteNumberValue(t);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Response to <see cref="SubGhzTxCommand"/>.</summary>
public readonly struct SubGhzTxResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// SubGHz get RSSI
// ---------------------------------------------------------------------------

/// <summary>Returns the current RSSI (received signal strength) at a given frequency.</summary>
public readonly struct SubGhzGetRssiCommand : IRpcCommand<SubGhzGetRssiResponse>
{
    public SubGhzGetRssiCommand(uint freq) => Freq = freq;

    /// <summary>Frequency in Hz to tune to before sampling RSSI.</summary>
    public uint Freq { get; }

    public string CommandName => "subghz_get_rssi";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
    }
}

/// <summary>Response to <see cref="SubGhzGetRssiCommand"/>.</summary>
public readonly struct SubGhzGetRssiResponse : IRpcCommandResponse
{
    /// <summary>RSSI in dBm (encoded as integer, e.g. <c>-70</c>).</summary>
    [JsonPropertyName("rssi")]
    public int Rssi { get; init; }
}

// ---------------------------------------------------------------------------
// NFC scan start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens an NFC scanner stream.  Each detected NFC tag protocol is delivered
/// as an <see cref="NfcScanEvent"/>.
/// Note: <c>NfcScanner</c> performs protocol detection only — no UID is
/// available without running a full anti-collision poller.
/// </summary>
public readonly struct NfcScanStartCommand : IRpcStreamCommand<NfcScanEvent>
{
    public string CommandName => "nfc_scan_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An NFC tag protocol detection event.</summary>
public readonly struct NfcScanEvent : IRpcCommandResponse
{
    /// <summary>Detected protocol name, e.g. <c>"Iso14443-3a"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }
}

// ---------------------------------------------------------------------------
// Device info
// ---------------------------------------------------------------------------

/// <summary>Returns device information (firmware version, model, hardware revision, UID).</summary>
public readonly struct DeviceInfoCommand : IRpcCommand<DeviceInfoResponse>
{
    public string CommandName => "device_info";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="DeviceInfoCommand"/>.</summary>
public readonly struct DeviceInfoResponse : IRpcCommandResponse
{
    [JsonPropertyName("firmware")]
    public string? Firmware { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("hardware")]
    public uint Hardware { get; init; }

    [JsonPropertyName("uid")]
    public string? Uid { get; init; }
}

// ---------------------------------------------------------------------------
// Power info
// ---------------------------------------------------------------------------

/// <summary>Returns battery and power state information.</summary>
public readonly struct PowerInfoCommand : IRpcCommand<PowerInfoResponse>
{
    public string CommandName => "power_info";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="PowerInfoCommand"/>.</summary>
public readonly struct PowerInfoResponse : IRpcCommandResponse
{
    /// <summary>Battery state of charge, 0–100.</summary>
    [JsonPropertyName("charge")]
    public uint Charge { get; init; }

    /// <summary>Battery voltage in millivolts.</summary>
    [JsonPropertyName("voltage_mv")]
    public uint VoltageMv { get; init; }

    /// <summary><c>true</c> when USB power is connected and charging.</summary>
    [JsonPropertyName("charging")]
    public bool Charging { get; init; }
}

// ---------------------------------------------------------------------------
// Datetime get
// ---------------------------------------------------------------------------

/// <summary>Returns the current RTC date and time.</summary>
public readonly struct DatetimeGetCommand : IRpcCommand<DatetimeGetResponse>
{
    public string CommandName => "datetime_get";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="DatetimeGetCommand"/>.</summary>
public readonly struct DatetimeGetResponse : IRpcCommandResponse
{
    [JsonPropertyName("year")]
    public uint Year { get; init; }

    [JsonPropertyName("month")]
    public uint Month { get; init; }

    [JsonPropertyName("day")]
    public uint Day { get; init; }

    [JsonPropertyName("hour")]
    public uint Hour { get; init; }

    [JsonPropertyName("minute")]
    public uint Minute { get; init; }

    [JsonPropertyName("second")]
    public uint Second { get; init; }

    [JsonPropertyName("weekday")]
    public uint Weekday { get; init; }
}

// ---------------------------------------------------------------------------
// Datetime set
// ---------------------------------------------------------------------------

/// <summary>Sets the RTC date and time on the Flipper.</summary>
public readonly struct DatetimeSetCommand : IRpcCommand<DatetimeSetResponse>
{
    public DatetimeSetCommand(uint year, uint month, uint day,
        uint hour, uint minute, uint second, uint weekday)
    {
        Year = year; Month = month; Day = day;
        Hour = hour; Minute = minute; Second = second; Weekday = weekday;
    }

    public uint Year { get; }
    public uint Month { get; }
    public uint Day { get; }
    public uint Hour { get; }
    public uint Minute { get; }
    public uint Second { get; }
    public uint Weekday { get; }

    public string CommandName => "datetime_set";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("year", Year);
        writer.WriteNumber("month", Month);
        writer.WriteNumber("day", Day);
        writer.WriteNumber("hour", Hour);
        writer.WriteNumber("minute", Minute);
        writer.WriteNumber("second", Second);
        writer.WriteNumber("weekday", Weekday);
    }
}

/// <summary>Response to <see cref="DatetimeSetCommand"/>.</summary>
public readonly struct DatetimeSetResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Region info
// ---------------------------------------------------------------------------

/// <summary>Returns the RF region name and allowed frequency band list.</summary>
public readonly struct RegionInfoCommand : IRpcCommand<RegionInfoResponse>
{
    public string CommandName => "region_info";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="RegionInfoCommand"/>.</summary>
public readonly struct RegionInfoResponse : IRpcCommandResponse
{
    [JsonPropertyName("region")]
    public string? Region { get; init; }
}

// ---------------------------------------------------------------------------
// Frequency is allowed
// ---------------------------------------------------------------------------

/// <summary>Checks whether a given frequency is permitted in the current region.</summary>
public readonly struct FrequencyIsAllowedCommand : IRpcCommand<FrequencyIsAllowedResponse>
{
    public FrequencyIsAllowedCommand(uint freq) => Freq = freq;

    /// <summary>Frequency in Hz to check.</summary>
    public uint Freq { get; }

    public string CommandName => "frequency_is_allowed";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
    }
}

/// <summary>Response to <see cref="FrequencyIsAllowedCommand"/>.</summary>
public readonly struct FrequencyIsAllowedResponse : IRpcCommandResponse
{
    /// <summary><c>true</c> if the frequency is permitted in the current region.</summary>
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }
}

// ---------------------------------------------------------------------------
// LED set
// ---------------------------------------------------------------------------

/// <summary>Sets an LED colour channel intensity on the Flipper.</summary>
public readonly struct LedSetCommand : IRpcCommand<LedSetResponse>
{
    /// <param name="color">One of <c>"red"</c>, <c>"green"</c>, or <c>"blue"</c>.</param>
    /// <param name="value">Intensity 0–255.</param>
    public LedSetCommand(string color, byte value)
    {
        Color = color;
        Value = value;
    }

    public string Color { get; }
    public byte Value { get; }

    public string CommandName => "led_set";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("color", Color);
        writer.WriteNumber("value", Value);
    }
}

/// <summary>Response to <see cref="LedSetCommand"/>.</summary>
public readonly struct LedSetResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Vibro
// ---------------------------------------------------------------------------

/// <summary>Enables or disables the Flipper's vibration motor.</summary>
public readonly struct VibroCommand : IRpcCommand<VibroResponse>
{
    public VibroCommand(bool enable) => Enable = enable;

    /// <summary><c>true</c> to start vibrating; <c>false</c> to stop.</summary>
    public bool Enable { get; }

    public string CommandName => "vibro";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteBoolean("enable", Enable);
    }
}

/// <summary>Response to <see cref="VibroCommand"/>.</summary>
public readonly struct VibroResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Speaker start
// ---------------------------------------------------------------------------

/// <summary>
/// Starts a continuous tone on the Flipper's piezo speaker.
/// The speaker resource is held until <see cref="SpeakerStopCommand"/> is sent.
/// </summary>
public readonly struct SpeakerStartCommand : IRpcCommand<SpeakerStartResponse>
{
    /// <param name="freq">Frequency in Hz (e.g. <c>440</c> for A4).</param>
    /// <param name="volume">Volume 0–255 (mapped to 0.0–1.0 on the Flipper).</param>
    public SpeakerStartCommand(uint freq, byte volume)
    {
        Freq = freq;
        Volume = volume;
    }

    public uint Freq { get; }
    public byte Volume { get; }

    public string CommandName => "speaker_start";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("freq", Freq);
        writer.WriteNumber("volume", Volume);
    }
}

/// <summary>Response to <see cref="SpeakerStartCommand"/>.</summary>
public readonly struct SpeakerStartResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Speaker stop
// ---------------------------------------------------------------------------

/// <summary>Stops the piezo speaker and releases the speaker resource.</summary>
public readonly struct SpeakerStopCommand : IRpcCommand<SpeakerStopResponse>
{
    public string CommandName => "speaker_stop";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="SpeakerStopCommand"/>.</summary>
public readonly struct SpeakerStopResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Backlight
// ---------------------------------------------------------------------------

/// <summary>Sets the LCD backlight brightness.</summary>
public readonly struct BacklightCommand : IRpcCommand<BacklightResponse>
{
    public BacklightCommand(byte value) => Value = value;

    /// <summary>Brightness 0–255.</summary>
    public byte Value { get; }

    public string CommandName => "backlight";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("value", Value);
    }
}

/// <summary>Response to <see cref="BacklightCommand"/>.</summary>
public readonly struct BacklightResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Storage info
// ---------------------------------------------------------------------------

/// <summary>Returns filesystem capacity and free space for a storage path.</summary>
public readonly struct StorageInfoCommand : IRpcCommand<StorageInfoResponse>
{
    /// <param name="path">Storage root, e.g. <c>"/int"</c> or <c>"/ext"</c>.</param>
    public StorageInfoCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_info";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageInfoCommand"/>.</summary>
public readonly struct StorageInfoResponse : IRpcCommandResponse
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Total capacity in kibibytes.</summary>
    [JsonPropertyName("total_kb")]
    public uint TotalKb { get; init; }

    /// <summary>Free space in kibibytes.</summary>
    [JsonPropertyName("free_kb")]
    public uint FreeKb { get; init; }
}

// ---------------------------------------------------------------------------
// Storage list
// ---------------------------------------------------------------------------

/// <summary>Lists the contents of a directory on the Flipper storage.</summary>
public readonly struct StorageListCommand : IRpcCommand<StorageListResponse>
{
    public StorageListCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_list";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>A single entry in a directory listing.</summary>
public readonly struct StorageEntry : IRpcCommandResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; init; }

    [JsonPropertyName("size")]
    public uint Size { get; init; }
}

/// <summary>Response to <see cref="StorageListCommand"/>.</summary>
public readonly struct StorageListResponse : IRpcCommandResponse
{
    [JsonPropertyName("entries")]
    public StorageEntry[]? Entries { get; init; }
}

// ---------------------------------------------------------------------------
// Storage read
// ---------------------------------------------------------------------------

/// <summary>Reads a file from the Flipper filesystem. File content is Base64-encoded.</summary>
public readonly struct StorageReadCommand : IRpcCommand<StorageReadResponse>
{
    public StorageReadCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_read";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageReadCommand"/>.</summary>
public readonly struct StorageReadResponse : IRpcCommandResponse
{
    /// <summary>File contents encoded as a Base64 string.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

// ---------------------------------------------------------------------------
// Storage write
// ---------------------------------------------------------------------------

/// <summary>
/// Writes data to a file on the Flipper filesystem, creating or overwriting it.
/// File content must be Base64-encoded.
/// </summary>
public readonly struct StorageWriteCommand : IRpcCommand<StorageWriteResponse>
{
    public StorageWriteCommand(string path, string base64Data)
    {
        Path = path;
        Base64Data = base64Data;
    }

    public string Path { get; }

    /// <summary>File content encoded as Base64.</summary>
    public string Base64Data { get; }

    public string CommandName => "storage_write";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
        writer.WriteString("data", Base64Data);
    }
}

/// <summary>Response to <see cref="StorageWriteCommand"/>.</summary>
public readonly struct StorageWriteResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Storage mkdir
// ---------------------------------------------------------------------------

/// <summary>Creates a directory on the Flipper filesystem.</summary>
public readonly struct StorageMkdirCommand : IRpcCommand<StorageMkdirResponse>
{
    public StorageMkdirCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_mkdir";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageMkdirCommand"/>.</summary>
public readonly struct StorageMkdirResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Storage remove
// ---------------------------------------------------------------------------

/// <summary>Removes a file or empty directory from the Flipper filesystem.</summary>
public readonly struct StorageRemoveCommand : IRpcCommand<StorageRemoveResponse>
{
    public StorageRemoveCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_remove";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageRemoveCommand"/>.</summary>
public readonly struct StorageRemoveResponse : IRpcCommandResponse { }

// ---------------------------------------------------------------------------
// Storage stat
// ---------------------------------------------------------------------------

/// <summary>Returns metadata for a file or directory on the Flipper filesystem.</summary>
public readonly struct StorageStatCommand : IRpcCommand<StorageStatResponse>
{
    public StorageStatCommand(string path) => Path = path;

    public string Path { get; }

    public string CommandName => "storage_stat";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteString("path", Path);
    }
}

/// <summary>Response to <see cref="StorageStatCommand"/>.</summary>
public readonly struct StorageStatResponse : IRpcCommandResponse
{
    /// <summary>File size in bytes (0 for directories).</summary>
    [JsonPropertyName("size")]
    public uint Size { get; init; }

    /// <summary><c>true</c> if the path is a directory.</summary>
    [JsonPropertyName("is_dir")]
    public bool IsDir { get; init; }
}

// ---------------------------------------------------------------------------
// LF RFID read start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a streaming LF RFID read session.  Each detected tag is delivered as
/// an <see cref="LfRfidReadEvent"/>.
/// </summary>
public readonly struct LfRfidReadStartCommand : IRpcStreamCommand<LfRfidReadEvent>
{
    public string CommandName => "lfrfid_read_start";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An LF RFID tag read event.</summary>
public readonly struct LfRfidReadEvent : IRpcCommandResponse
{
    /// <summary>Protocol type identifier (numeric string from the Flipper SDK).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Raw tag data as an uppercase hex string.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

// ---------------------------------------------------------------------------
// iButton read start
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a streaming iButton read session.  Each detected key is delivered as
/// an <see cref="IButtonReadEvent"/>.
/// </summary>
public readonly struct IButtonReadStartCommand : IRpcStreamCommand<IButtonReadEvent>
{
    public string CommandName => "ibutton_read_start";
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An iButton key read event.</summary>
public readonly struct IButtonReadEvent : IRpcCommandResponse
{
    /// <summary>Protocol/key type name, e.g. <c>"DS1990Raw"</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Raw key data as an uppercase hex string.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

// ---------------------------------------------------------------------------
// Stream close
// ---------------------------------------------------------------------------

/// <summary>
/// Closes an open stream identified by <see cref="StreamId"/>.
/// Called automatically by <see cref="RpcStream{TEvent}.DisposeAsync"/>.
/// </summary>
public readonly struct StreamCloseCommand : IRpcCommand<StreamCloseResponse>
{
    public StreamCloseCommand(uint streamId) => StreamId = streamId;

    /// <summary>The stream id to close (maps to <c>"stream"</c> in JSON).</summary>
    public uint StreamId { get; }

    public string CommandName => "stream_close";

    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("stream", StreamId);
    }
}

/// <summary>Response to <see cref="StreamCloseCommand"/>.</summary>
public readonly struct StreamCloseResponse : IRpcCommandResponse { }
