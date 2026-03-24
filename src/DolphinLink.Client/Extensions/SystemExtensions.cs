using DolphinLink.Client.Commands;
using DolphinLink.Client.Commands.System;
using DatetimeGetCommand = DolphinLink.Client.Commands.System.DatetimeGetCommand;

namespace DolphinLink.Client.Extensions;

/// <summary>
/// Hand-written extension methods for system commands that require custom
/// argument construction or response unwrapping not suited to code generation.
/// </summary>
public static partial class SystemExtensions
{
    /// <summary>
    /// Propagates host-side configuration (heartbeat timing, LED indicator colour)
    /// to the daemon. Called automatically by <see cref="RpcClient.ConnectAsync"/>
    /// when the daemon reports <c>configure</c> support.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="heartbeatMs">TX idle interval in milliseconds (must be &gt;= 500).</param>
    /// <param name="timeoutMs">RX silence timeout in milliseconds (must be &gt; heartbeatMs and &gt;= 2000).</param>
    /// <param name="led">Optional LED connection indicator colour.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective configuration echoed back by the daemon.</returns>
    public static Task<ConfigureResponse> ConfigureAsync(
        this RpcClient client,
        uint heartbeatMs,
        uint timeoutMs,
        RgbColor? led = null,
        CancellationToken ct = default)
        => client.SendAsync<ConfigureCommand, ConfigureResponse>(
            new ConfigureCommand(heartbeatMs, timeoutMs, led), ct);

    /// <summary>
    /// Reads the current date and time from the Flipper's RTC.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current RTC date/time.</returns>
    public static Task<DatetimeGetResponse> DatetimeGetAsync(
        this RpcClient client,
        CancellationToken ct = default)
        => client.SendAsync<DatetimeGetCommand, DatetimeGetResponse>(new DatetimeGetCommand(), ct);

    /// <summary>
    /// Sets the date and time on the Flipper's RTC.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="dateTime">The date and time to set. <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<DatetimeSetResponse> DatetimeSetAsync(
        this RpcClient client,
        DateTime dateTime,
        CancellationToken ct = default)
        => client.SendAsync<DatetimeSetCommand, DatetimeSetResponse>(
            new DatetimeSetCommand(dateTime), ct);
}
