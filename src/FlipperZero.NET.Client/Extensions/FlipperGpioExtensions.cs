using FlipperZero.NET.Commands.Gpio;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for GPIO commands.
/// </summary>
public static class FlipperGpioExtensions
{
    /// <summary>
    /// Reads the current digital level of a GPIO pin.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="pin">The GPIO header pin to read.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the pin is high; <c>false</c> if low.</returns>
    public static async Task<bool> GpioReadAsync(
        this FlipperRpcClient client,
        GpioPin pin,
        CancellationToken ct = default)
    {
        var r = await client.SendAsync<GpioReadCommand, GpioReadResponse>(
            new GpioReadCommand(pin), ct).ConfigureAwait(false);
        return r.Level;
    }

    /// <summary>Drives a GPIO pin high or low.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="pin">The GPIO header pin to drive.</param>
    /// <param name="level"><c>true</c> to drive high; <c>false</c> to drive low.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<GpioWriteResponse> GpioWriteAsync(
        this FlipperRpcClient client,
        GpioPin pin, bool level,
        CancellationToken ct = default)
        => client.SendAsync<GpioWriteCommand, GpioWriteResponse>(new GpioWriteCommand(pin, level), ct);

    /// <summary>
    /// Reads the ADC voltage on a GPIO pin.
    /// ADC-capable pins: <see cref="GpioPin.Pin1"/>, <see cref="GpioPin.Pin2"/>,
    /// <see cref="GpioPin.Pin3"/>, <see cref="GpioPin.Pin6"/>, <see cref="GpioPin.Pin7"/>.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="pin">ADC-capable GPIO pin.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<AdcReadResponse> AdcReadAsync(
        this FlipperRpcClient client,
        GpioPin pin,
        CancellationToken ct = default)
        => client.SendAsync<AdcReadCommand, AdcReadResponse>(new AdcReadCommand(pin), ct);

    /// <summary>Enables or disables the 5 V header supply rail.</summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="enable"><c>true</c> to enable; <c>false</c> to disable.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<GpioSet5vResponse> GpioSet5vAsync(
        this FlipperRpcClient client,
        bool enable,
        CancellationToken ct = default)
        => client.SendAsync<GpioSet5vCommand, GpioSet5vResponse>(new GpioSet5vCommand(enable), ct);

    /// <summary>
    /// Watches a GPIO pin for level changes.
    /// </summary>
    /// <param name="client">The RPC client.</param>
    /// <param name="pin">Physical GPIO header pin to watch.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="GpioWatchEvent"/>
    /// on each rising or falling edge.  Dispose the stream to remove the interrupt.
    /// </returns>
    public static Task<RpcStream<GpioWatchEvent>> GpioWatchStartAsync(
        this FlipperRpcClient client,
        GpioPin pin,
        CancellationToken ct = default)
        => client.SendStreamAsync<GpioWatchStartCommand, GpioWatchEvent>(new GpioWatchStartCommand(pin), ct);
}
