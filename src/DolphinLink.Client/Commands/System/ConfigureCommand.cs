using System.Text.Json.Serialization;
using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Converters;

namespace DolphinLink.Client.Commands.System;

/// <summary>
/// Propagates host-side configuration to the daemon during session startup.
///
/// The client sends this command immediately after <see cref="DaemonInfoCommand"/>
/// so the daemon can align its behaviour (heartbeat timing, LED indicator,
/// diagnostics) with the host's <see cref="RpcClientOptions"/> settings.
///
/// All arguments are optional: if a field is omitted the daemon keeps its current
/// value (initially the compile-time default: 3 s interval, 10 s timeout, LED off,
/// diagnostics disabled).
///
/// Wire format (request — all fields optional):
/// <code>{"c":2,"i":N,"hb":3000,"to":10000,"led":{"r":81,"g":43,"b":212},"dx":true}</code>
///
/// Wire format (response — effective values; "led" omitted when not configured):
/// <code>{"t":0,"i":N,"p":{"hb":3000,"to":10000,"led":{"r":81,"g":43,"b":212},"dx":true}}</code>
///
/// Daemon-side validation rules (heartbeat):
/// <list type="bullet">
///   <item><c>hb</c> &gt;= 500</item>
///   <item><c>to</c> &gt;= 2000</item>
///   <item><c>to</c> &gt; <c>hb</c></item>
/// </list>
/// A violation returns error code <c>invalid_config</c>; no values are changed.
///
/// LED indicator behaviour:
/// When <c>"led"</c> is present the daemon uses the supplied RGB colour as a
/// connection indicator: LED on (stored colour) while connected, LED off when
/// disconnected.  The config is scoped to a single connection lifecycle and is
/// cleared on every disconnect.
///
/// Diagnostics behaviour:
/// When <c>"dx":true</c> is sent the daemon appends a <c>"_m"</c> timing object
/// to every <c>"t":0</c> response for the rest of the session.  See
/// <see cref="RpcClientOptions.DaemonDiagnostics"/> for details.
///
/// Resources required: none.
/// </summary>
public readonly partial struct ConfigureCommand : IRpcCommand<ConfigureResponse>
{
    /// <summary>
    /// Creates a configure command with the specified heartbeat timing, optional LED colour,
    /// and optional diagnostics flag.
    /// </summary>
    /// <param name="heartbeatMs">
    /// TX idle interval in milliseconds — a keep-alive frame is sent when no outbound
    /// message has been transmitted for this long.  Must be &gt;= 500.
    /// </param>
    /// <param name="timeoutMs">
    /// RX silence timeout in milliseconds — the host is declared gone when no inbound
    /// data arrives for this long.  Must be &gt; <paramref name="heartbeatMs"/> and &gt;= 2000.
    /// </param>
    /// <param name="led">
    /// Optional LED connection indicator colour.  When non-null the daemon turns on the LED
    /// with this colour while connected and turns it off on disconnect.
    /// Pass <c>null</c> to leave the LED configuration unchanged (PATCH semantics).
    /// </param>
    /// <param name="diagnostics">
    /// When <c>true</c>, requests the daemon to append per-request timing metrics
    /// (<c>"_m"</c>) to every response during this session.
    /// Defaults to <c>false</c>.
    /// </param>
    public ConfigureCommand(uint heartbeatMs, uint timeoutMs, RgbColor? led = null, bool diagnostics = false)
    {
        HeartbeatMs  = heartbeatMs;
        TimeoutMs    = timeoutMs;
        Led          = led;
        Diagnostics  = diagnostics;
    }

    /// <summary>TX idle interval to send to the daemon, in milliseconds.</summary>
    public uint HeartbeatMs { get; }

    /// <summary>RX silence timeout to send to the daemon, in milliseconds.</summary>
    public uint TimeoutMs { get; }

    /// <summary>
    /// Optional LED connection indicator colour.  When non-null the daemon uses this colour
    /// to signal an active connection.  When null the LED configuration is left unchanged.
    /// </summary>
    public RgbColor? Led { get; }

    /// <summary>
    /// When <c>true</c>, requests the daemon to emit per-request timing metrics
    /// in every response (<c>"_m"</c> field).
    /// </summary>
    public bool Diagnostics { get; }

    /// <inheritdoc />
    public string CommandName => "configure";

    /// <inheritdoc />
    public int CommandId => 2;

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("hb", HeartbeatMs);
        writer.WriteNumber("to", TimeoutMs);

        if (Led is { } led)
        {
            writer.WriteStartObject("led");
            writer.WriteNumber("r", led.R);
            writer.WriteNumber("g", led.G);
            writer.WriteNumber("b", led.B);
            writer.WriteEndObject();
        }

        // Only emit "dx" when explicitly requesting diagnostics — omit when false
        // to keep the wire compact and match PATCH semantics (daemon keeps current value).
        if (Diagnostics)
        {
            writer.WriteBoolean("dx", true);
        }
    }
}

/// <summary>Response to <see cref="ConfigureCommand"/>.</summary>
public readonly struct ConfigureResponse : IRpcCommandResponse
{
    /// <summary>
    /// The TX idle interval (ms) the daemon is now using — the effective value
    /// after applying the requested configuration.
    /// </summary>
    [JsonPropertyName("hb")]
    public uint HeartbeatMs { get; init; }

    /// <summary>
    /// The RX silence timeout (ms) the daemon is now using — the effective value
    /// after applying the requested configuration.
    /// </summary>
    [JsonPropertyName("to")]
    public uint TimeoutMs { get; init; }

    /// <summary>
    /// The effective LED connection indicator colour the daemon is now using.
    /// <c>null</c> when no LED indicator has been configured for this session.
    /// </summary>
    [JsonPropertyName("led")]
    public RgbColor? Led { get; init; }

    /// <summary>
    /// Whether per-request timing metrics (<c>"_m"</c> field) are currently
    /// enabled on the daemon for this session.
    /// </summary>
    [JsonConverter(typeof(NumericBoolJsonConverter))]
    [JsonPropertyName("dx")]
    public bool Diagnostics { get; init; }
}

