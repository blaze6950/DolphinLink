using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Propagates host-side configuration to the daemon during session startup.
///
/// The client sends this command immediately after <see cref="DaemonInfoCommand"/>
/// so the daemon can align its behaviour (heartbeat timing) with the host's
/// <see cref="FlipperRpcClientOptions"/> settings.
///
/// Both arguments are optional: if a field is omitted the daemon keeps its
/// current value (initially the compile-time default: 3 s interval, 10 s timeout).
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"configure","heartbeat_ms":3000,"timeout_ms":10000}</code>
///
/// Wire format (response — effective values after applying the configuration):
/// <code>{"t":0,"i":N,"p":{"heartbeat_ms":3000,"timeout_ms":10000}}</code>
///
/// Daemon-side validation rules:
/// <list type="bullet">
///   <item><c>heartbeat_ms</c> &gt;= 500</item>
///   <item><c>timeout_ms</c> &gt;= 2000</item>
///   <item><c>timeout_ms</c> &gt; <c>heartbeat_ms</c></item>
/// </list>
/// A violation returns error code <c>invalid_config</c>; no values are changed.
///
/// Resources required: none.
/// </summary>
public readonly struct ConfigureCommand : IRpcCommand<ConfigureResponse>
{
    /// <summary>
    /// Creates a configure command with the specified heartbeat timing.
    /// </summary>
    /// <param name="heartbeatMs">
    /// TX idle interval in milliseconds — a keep-alive frame is sent when no
    /// outbound message has been transmitted for this long.  Must be &gt;= 500.
    /// </param>
    /// <param name="timeoutMs">
    /// RX silence timeout in milliseconds — the host is declared gone when no
    /// inbound data arrives for this long.  Must be &gt; <paramref name="heartbeatMs"/>
    /// and &gt;= 2000.
    /// </param>
    public ConfigureCommand(uint heartbeatMs, uint timeoutMs)
    {
        HeartbeatMs = heartbeatMs;
        TimeoutMs   = timeoutMs;
    }

    /// <summary>TX idle interval to send to the daemon, in milliseconds.</summary>
    public uint HeartbeatMs { get; }

    /// <summary>RX silence timeout to send to the daemon, in milliseconds.</summary>
    public uint TimeoutMs { get; }

    /// <inheritdoc />
    public string CommandName => "configure";

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        writer.WriteNumber("heartbeat_ms", HeartbeatMs);
        writer.WriteNumber("timeout_ms",   TimeoutMs);
    }
}

/// <summary>Response to <see cref="ConfigureCommand"/>.</summary>
public readonly struct ConfigureResponse : IRpcCommandResponse
{
    /// <summary>
    /// The TX idle interval (ms) the daemon is now using — the effective value
    /// after applying the requested configuration.
    /// </summary>
    [JsonPropertyName("heartbeat_ms")]
    public uint HeartbeatMs { get; init; }

    /// <summary>
    /// The RX silence timeout (ms) the daemon is now using — the effective value
    /// after applying the requested configuration.
    /// </summary>
    [JsonPropertyName("timeout_ms")]
    public uint TimeoutMs { get; init; }
}
