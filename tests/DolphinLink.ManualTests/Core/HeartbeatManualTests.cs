using DolphinLink.Client.Exceptions;
using DolphinLink.Client.Transport;
using DolphinLink.Client.Commands.Input;
using DolphinLink.Client.Extensions;

namespace DolphinLink.ManualTests.Core;

/// <summary>
/// Manual tests for the heartbeat / keep-alive mechanism. All tests require
/// human interaction (pressing buttons or unplugging USB) and open their own
/// <see cref="RpcClient"/> instances so they cannot share the
/// <see cref="DeviceCollection"/> fixture.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~HeartbeatManualTests"
/// </summary>
[Collection("Flipper heartbeat")]
public sealed class HeartbeatManualTests
{
    private readonly string _portName;

    public HeartbeatManualTests()
    {
        _portName = Environment.GetEnvironmentVariable(DeviceFixture.EnvVar)
            ?? string.Empty;
    }

    // -------------------------------------------------------------------------
    // 1. Graceful daemon exit — host detects disconnect
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully (operator presses Back on the Flipper),
    /// it sends <c>{"disconnect":true}</c> before cleaning up. The host reader
    /// loop must detect this frame and cancel
    /// <see cref="RpcClient.Disconnected"/> immediately — without waiting
    /// for the 10 s heartbeat timeout.
    ///
    /// Requires manual interaction: press the Back button on the Flipper to stop
    /// the daemon within 60 seconds of the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task DaemonExitViaBackButton_HostDetectsDisconnect()
    {
        await using var client = new RpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        // Give the tester up to 60 s to press Back on the Flipper.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationTokenSource
                .CreateLinkedTokenSource(client.Disconnected, deadline.Token).Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected */ }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 60 s. " +
            "Press Back on the Flipper to exit the daemon.");

        // Any subsequent command must throw, not hang.
        await Assert.ThrowsAsync<RpcException>(
            () => client.PingAsync());
    }

    // -------------------------------------------------------------------------
    // 2. Graceful disconnect during pending command
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully while an RPC command is in-flight, the
    /// pending <see cref="Task"/> must be faulted with
    /// <see cref="RpcException"/> rather than hanging indefinitely.
    ///
    /// Requires manual interaction: press Back on the Flipper within 60 seconds.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task GracefulDisconnect_DuringPendingCommand_FaultsPendingTask()
    {
        await using var client = new RpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        using var longTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var pingTask = client.PingAsync(longTimeout.Token);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationTokenSource
                .CreateLinkedTokenSource(client.Disconnected, deadline.Token).Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 60 s. " +
            "Press Back on the Flipper to exit the daemon.");

        // The in-flight ping must fault, not succeed or hang.
        await Assert.ThrowsAnyAsync<Exception>(() => pingTask);
    }

    // -------------------------------------------------------------------------
    // 3. Hard disconnect during pending command
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the USB cable is physically unplugged while an RPC command is
    /// in-flight, the heartbeat watchdog must detect the silence and cancel
    /// <see cref="RpcClient.Disconnected"/> within the timeout window.
    /// The pending command must be faulted rather than hanging.
    ///
    /// Uses accelerated timing: 1 s interval, 4 s timeout.
    ///
    /// Requires manual interaction: unplug the USB cable within 30 seconds of
    /// the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task HardDisconnect_DuringPendingCommand_FaultsPendingTask()
    {
        await using var client = new RpcClient(
            new SerialPortTransport(_portName),
            new RpcClientOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                Timeout = TimeSpan.FromSeconds(4),
            });

        await client.ConnectAsync();

        using var longTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var pingTask = client.PingAsync(longTimeout.Token);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationTokenSource
                .CreateLinkedTokenSource(client.Disconnected, deadline.Token).Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 30 s. " +
            "Unplug the USB cable to simulate a hard disconnect.");

        await Assert.ThrowsAnyAsync<Exception>(() => pingTask);
    }

    // -------------------------------------------------------------------------
    // 4. Graceful disconnect during stream iteration
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully (Back button) while the host is blocked
    /// inside an <c>await foreach</c> on a stream, the enumeration must
    /// terminate rather than blocking forever.
    ///
    /// Uses a GPIO watch stream (blocks waiting for pin-level changes).
    ///
    /// Requires manual interaction: press Back on the Flipper within 60 seconds
    /// of the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task GracefulDisconnect_DuringStreamIteration_TerminatesEnumeration()
    {
        await using var client = new RpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        await using var stream = await client.GpioWatchStartAsync(GpioPin.Pin6);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Exception? caughtException = null;

        try
        {
            await foreach (var _ in stream.WithCancellation(deadline.Token))
            {
                // No pin events expected; we just need the loop to be running
                // when the daemon exits.
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 60 s. " +
            "Press Back on the Flipper to exit the daemon.");

        Assert.NotNull(caughtException);
        Assert.True(
            caughtException is OperationCanceledException or RpcException,
            $"Expected OperationCanceledException or RpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }

    // -------------------------------------------------------------------------
    // 5. Hard disconnect during stream iteration
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the USB cable is physically unplugged while the host is blocked
    /// inside an <c>await foreach</c> on a stream, the heartbeat watchdog must
    /// detect the silence and terminate the enumeration within the timeout window.
    ///
    /// Uses accelerated timing: 1 s interval, 4 s timeout.
    ///
    /// Requires manual interaction: unplug the USB cable within 30 seconds of
    /// the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task HardDisconnect_DuringStreamIteration_TerminatesEnumeration()
    {
        await using var client = new RpcClient(
            new SerialPortTransport(_portName),
            new RpcClientOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                Timeout = TimeSpan.FromSeconds(4),
            });

        await client.ConnectAsync();

        await using var stream = await client.GpioWatchStartAsync(GpioPin.Pin6);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Exception? caughtException = null;

        try
        {
            await foreach (var _ in stream.WithCancellation(deadline.Token))
            {
                // Waiting for the watchdog to fire after the cable is pulled.
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 30 s. " +
            "Unplug the USB cable to simulate a hard disconnect.");

        Assert.NotNull(caughtException);
        Assert.True(
            caughtException is OperationCanceledException or RpcException,
            $"Expected OperationCanceledException or RpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }

    // -------------------------------------------------------------------------
    // 6. Input stream — custom exit combo triggers disconnect
    // -------------------------------------------------------------------------

    /// <summary>
    /// With a custom exit combo (Ok+Long) registered on the input stream, the
    /// standard Back+Short key must be demoted to an ordinary event and must NOT
    /// stop the daemon. Only pressing the custom combo (Ok+Long) must trigger
    /// daemon exit.
    ///
    /// Test flow:
    /// 1. Open <c>input_listen_start</c> with exit override Ok+Long.
    /// 2. Tester presses Back twice — events must arrive in the stream (daemon stays alive).
    /// 3. Tester long-presses Ok — daemon exits, host detects disconnect.
    ///
    /// Requires manual interaction: press Back twice, then long-press Ok on the
    /// Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresDeviceFact]
    public async Task InputStream_CustomExitCombo_BackPassesThroughAndOkLongDisconnects()
    {
        await using var client = new RpcClient(new SerialPortTransport(_portName));
        await client.ConnectAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        await using var stream = await client.InputListenStartAsync(
            exitKey: InputKey.Ok,
            exitType: InputType.Long,
            ct: timeout.Token);

        var backShortEvents = new List<InputListenEvent>();
        Exception? caughtException = null;

        try
        {
            await foreach (var evt in stream.WithCancellation(timeout.Token))
            {
                if (evt is { Key: InputKey.Back, Type: InputType.Short })
                {
                    backShortEvents.Add(evt);
                }
                // Continue iterating until daemon exits via Ok+Long.
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        Assert.True(backShortEvents.Count >= 2,
            $"Expected at least 2 Back+Short events before daemon exit, " +
            $"got {backShortEvents.Count}. " +
            "Press Back twice, then long-press Ok on the Flipper.");

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled after the Ok+Long exit combo. " +
            "Long-press Ok on the Flipper to exit the daemon.");

        Assert.NotNull(caughtException);
        Assert.True(
            caughtException is OperationCanceledException or RpcException,
            $"Expected OperationCanceledException or RpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }
}
