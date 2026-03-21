using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Exceptions;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for system/device info commands.
/// </summary>
public static class FlipperSystemExtensions
{
    /// <summary>
    /// Propagates host-side configuration to the daemon.
    ///
    /// Sends a <c>configure</c> command with the specified heartbeat timing values and
    /// returns the effective configuration the daemon is now using.  The response echoes
    /// the accepted values so the caller can confirm them (useful when the daemon clamps
    /// or rejects individual fields).
    ///
    /// Both <paramref name="heartbeatMs"/> and <paramref name="timeoutMs"/> are validated
    /// by the daemon:
    /// <list type="bullet">
    ///   <item><c>heartbeatMs</c> &gt;= 500</item>
    ///   <item><c>timeoutMs</c> &gt;= 2000</item>
    ///   <item><c>timeoutMs</c> &gt; <c>heartbeatMs</c></item>
    /// </list>
    /// A violation throws <see cref="FlipperRpcException"/> with error code
    /// <c>invalid_config</c>.
    ///
    /// <para>
    /// This method is called automatically by <see cref="FlipperRpcClient.ConnectAsync"/>
    /// when the daemon supports the <c>configure</c> command (protocol version &gt;= 4).
    /// Use it directly only when you need to change the configuration after the initial
    /// handshake.
    /// </para>
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="heartbeatMs">
    /// TX idle interval in milliseconds — a keep-alive frame is sent when no outbound
    /// message has been transmitted for this long.  Must be &gt;= 500.
    /// </param>
    /// <param name="timeoutMs">
    /// RX silence timeout in milliseconds — the host is declared gone when no inbound
    /// data arrives for this long.  Must be &gt; <paramref name="heartbeatMs"/> and
    /// &gt;= 2000.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// The effective <see cref="ConfigureResponse"/> containing the values now in use on
    /// the daemon.
    /// </returns>
    public static Task<ConfigureResponse> ConfigureAsync(
        this FlipperRpcClient client,
        uint heartbeatMs,
        uint timeoutMs,
        CancellationToken ct = default)
        => client.SendAsync<ConfigureCommand, ConfigureResponse>(
            new ConfigureCommand(heartbeatMs, timeoutMs), ct);

    /// <summary>
    /// Queries the daemon's identity and full command capability list.
    ///
    /// Use this for ad-hoc capability queries after
    /// <see cref="FlipperRpcClient.ConnectAsync"/>. The result of the initial
    /// negotiation performed by <c>ConnectAsync</c> is already stored in
    /// <see cref="FlipperRpcClient.DaemonInfo"/>; call this method only when
    /// you need a fresh snapshot.
    ///
    /// To test whether a specific command is available, use
    /// <see cref="DaemonInfoResponse.Supports(string)"/> or the generic
    /// <see cref="DaemonInfoResponse.Supports{TCommand}"/> overload.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<DaemonInfoResponse> DaemonInfoAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<DaemonInfoCommand, DaemonInfoResponse>(new DaemonInfoCommand(), ct);

    /// <summary>
    /// Returns comprehensive device information: identity (name, model, UID, BLE MAC),
    /// firmware (version, origin, branch, git hash, build date), hardware OTP fields
    /// (revision, target, body, color, region, display, manufacture timestamp),
    /// and regulatory IDs (FCC, IC, MIC, SRRC, NCC).
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<DeviceInfoResponse> DeviceInfoAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<DeviceInfoCommand, DeviceInfoResponse>(new DeviceInfoCommand(), ct);

    /// <summary>Returns battery charge percentage, voltage, and charging state.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<PowerInfoResponse> PowerInfoAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<PowerInfoCommand, PowerInfoResponse>(new PowerInfoCommand(), ct);

    /// <summary>Returns the current RTC date and time from the Flipper.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<DatetimeGetResponse> DatetimeGetAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<DatetimeGetCommand, DatetimeGetResponse>(new DatetimeGetCommand(), ct);

    /// <summary>
    /// Sets the RTC date and time on the Flipper.
    /// The weekday is derived automatically from <paramref name="dateTime"/>.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="dateTime">
    /// The date and time to set. <see cref="DateTime.Kind"/> is ignored —
    /// the Flipper RTC has no timezone concept.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<DatetimeSetResponse> DatetimeSetAsync(
        this FlipperRpcClient client,
        DateTime dateTime,
        CancellationToken ct = default)
        => client.SendAsync<DatetimeSetCommand, DatetimeSetResponse>(
            new DatetimeSetCommand(dateTime), ct);

    /// <summary>Returns the RF region name and allowed frequency bands.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<RegionInfoResponse> RegionInfoAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<RegionInfoCommand, RegionInfoResponse>(new RegionInfoCommand(), ct);

    /// <summary>
    /// Checks whether a frequency (in Hz) is permitted in the Flipper's current region.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="freq">Frequency in Hz to check.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the frequency is allowed.</returns>
    public static async Task<bool> FrequencyIsAllowedAsync(
        this FlipperRpcClient client,
        uint freq,
        CancellationToken ct = default)
    {
        var r = await client.SendAsync<FrequencyIsAllowedCommand, FrequencyIsAllowedResponse>(
            new FrequencyIsAllowedCommand(freq), ct).ConfigureAwait(false);
        return r.Allowed;
    }

    /// <summary>
    /// Requests the RPC daemon to stop gracefully. The daemon sends the OK
    /// response, then stops its event loop, which triggers a full teardown:
    /// all open streams are closed, hardware resources are released, a
    /// <c>{"disconnect":true}</c> notification is sent, and the USB
    /// configuration is restored. The connection will drop shortly after
    /// this call returns.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<DaemonStopResponse> DaemonStopAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<DaemonStopCommand, DaemonStopResponse>(new DaemonStopCommand(), ct);

    /// <summary>
    /// Requests an immediate hardware reset of the Flipper Zero. The daemon
    /// sends the OK response and then calls <c>furi_hal_power_reset()</c>,
    /// which performs a hard MCU reset equivalent to pressing the physical
    /// reset button. The USB connection will drop almost immediately after
    /// this call returns.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<RebootResponse> RebootAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<RebootCommand, RebootResponse>(new RebootCommand(), ct);
}