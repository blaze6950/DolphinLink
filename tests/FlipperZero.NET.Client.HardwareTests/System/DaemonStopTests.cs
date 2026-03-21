using FlipperZero.NET.Extensions;
using FlipperZero.NET.Client.HardwareTests.Core;
using FlipperZero.NET.Exceptions;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET.Client.HardwareTests.System;

/// <summary>
/// Hardware tests for <see cref="FlipperSystemExtensions.DaemonStopAsync"/>.
///
/// These tests open their own <see cref="FlipperRpcClient"/> instance and must
/// NOT share the <see cref="FlipperCollection"/> fixture because each test
/// terminates the RPC daemon (daemon_stop stops the event loop, which triggers
/// teardown and sends a <c>{"t":2}</c> Disconnect envelope).  They use
/// <see cref="LifecycleCollection"/> so xUnit serialises them with the other
/// exclusive-port collections.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~DaemonStopTests"
/// </summary>
[Collection(LifecycleCollection.Name)]
public sealed class DaemonStopTests
{
    private readonly string _portName;

    public DaemonStopTests()
    {
        _portName = Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)
            ?? string.Empty;
    }

    /// <summary>
    /// <see cref="FlipperSystemExtensions.DaemonStopAsync"/> must return an OK
    /// response before the daemon stops its event loop.
    ///
    /// The daemon calls <c>rpc_send_ok()</c> first (so the host receives the
    /// response), then calls <c>furi_event_loop_stop()</c>.  The exit path in
    /// <c>flipper_zero_rpc_daemon_app()</c> subsequently sends <c>{"t":2}</c>
    /// and tears down the USB configuration.
    ///
    /// Validates: the <c>daemon_stop</c> command round-trip succeeds and the
    /// response is delivered before the daemon terminates.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonStop_ReturnsOkResponse()
    {
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        var response = await client.DaemonStopAsync();

        Assert.Equal("ok", response.Status);
    }

    /// <summary>
    /// After <see cref="FlipperSystemExtensions.DaemonStopAsync"/> returns, the
    /// <see cref="FlipperRpcClient.Disconnected"/> token must be cancelled
    /// because the daemon sends <c>{"t":2}</c> (Disconnect envelope) as part
    /// of its graceful exit, which the reader loop converts into a
    /// <see cref="FlipperRpcException"/> that calls FaultAll.
    ///
    /// Validates: the Disconnect envelope emitted by the daemon teardown path
    /// is received by the reader loop and cancels the Disconnected token.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task DaemonStop_DisconnectedToken_IsCancelledAfterStop()
    {
        await using var client = new FlipperRpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        await client.DaemonStopAsync();

        // Allow up to 5 s for the {"t":2} disconnect envelope to arrive and
        // be processed by the reader loop.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Delay(Timeout.Infinite, client.Disconnected).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: Disconnected was cancelled by the reader loop.
        }

        Assert.True(
            client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled within 5 s after daemon_stop — " +
            "the reader loop did not receive the {\"t\":2} Disconnect envelope.");
    }
}
