using FlipperZero.NET.Commands.System;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for system/device info commands.
/// </summary>
public static class FlipperSystemExtensions
{
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
}
