using FlipperZero.NET.Extensions;
using FlipperZero.NET.Client.HardwareTests.Core;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET.Client.HardwareTests.System;

/// <summary>
/// Hardware tests for <see cref="FlipperSystemExtensions.RebootAsync"/>.
///
/// These tests open their own <see cref="FlipperRpcClient"/> instance and must
/// NOT share the <see cref="FlipperCollection"/> fixture because each test
/// terminates the connection (the MCU resets after the response).  They use
/// <see cref="LifecycleCollection"/> so xUnit serialises them with the other
/// exclusive-port collections.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~RebootTests"
/// </summary>
[Collection(LifecycleCollection.Name)]
public sealed class RebootTests
{
    private readonly string _portName;

    public RebootTests()
    {
        _portName = Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)
            ?? string.Empty;
    }

    /// <summary>
    /// <see cref="FlipperSystemExtensions.RebootAsync"/> must return an OK
    /// response before the MCU resets.
    ///
    /// The daemon acknowledges the command with <c>{"t":0,"i":N}</c> and then
    /// calls <c>furi_hal_power_reset()</c>.  The C# client must receive this
    /// response (status "ok") before the USB connection drops.
    ///
    /// Validates: the <c>reboot</c> command round-trip succeeds and the
    /// response is delivered before the hardware reset terminates the
    /// connection.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Reboot_ReturnsOkResponse()
    {
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        // The daemon sends the OK envelope before calling furi_hal_power_reset().
        var response = await client.RebootAsync();

        Assert.Equal("ok", response.Status);
    }

    /// <summary>
    /// After <see cref="FlipperSystemExtensions.RebootAsync"/> returns, the
    /// <see cref="FlipperRpcClient.Disconnected"/> token must eventually be
    /// cancelled because the MCU reset causes the USB connection to drop.
    ///
    /// Validates: the reader loop detects the connection loss (EOF or error)
    /// and calls <see cref="FlipperRpcClient.FaultAll"/>, which cancels
    /// <see cref="FlipperRpcClient.Disconnected"/>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Reboot_DisconnectedToken_IsCancelledAfterReset()
    {
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        await client.RebootAsync();

        // Allow up to 5 s for the USB drop to propagate to the reader loop.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Delay(Timeout.Infinite, client.Disconnected).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: either Disconnected or the 5 s timeout fired.
        }

        Assert.True(
            client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled within 5 s after reboot — " +
            "the reader loop did not detect the USB drop.");
    }
}
