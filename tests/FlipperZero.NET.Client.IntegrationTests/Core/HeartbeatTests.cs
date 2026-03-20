using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Commands.Gpio;
using FlipperZero.NET.Commands.Input;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Core;

/// <summary>
/// xUnit collection for heartbeat tests that open their own
/// <see cref="FlipperRpcClient"/> instances.  These tests require exclusive
/// access to the serial port so they CANNOT share the
/// <see cref="FlipperCollection"/> fixture (which holds the port open for the
/// duration of that collection).
///
/// xUnit runs collections sequentially, so both <see cref="FlipperCollection"/>
/// and <see cref="LifecycleCollection"/> finish (and release the port) before
/// this collection starts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeartbeatCollection
{
    public const string Name = "Flipper heartbeat";
}

/// <summary>
/// Integration tests for the heartbeat / keep-alive mechanism implemented by
/// <see cref="FlipperZero.NET.HeartbeatTransport"/> on the host side and
/// <c>heartbeat_timer</c> on the daemon side.
///
/// The heartbeat uses bare <c>\n</c> frames sent every 3 s when idle, with a
/// 10 s RX-silence timeout on both ends.  These tests verify that:
/// <list type="bullet">
///   <item>The idle connection stays alive well past the timeout (keep-alives work).</item>
///   <item>Graceful daemon exit (Back button) is detected by the host immediately.</item>
///   <item>Hard disconnect (USB pull) is detected by the host via heartbeat timeout.</item>
///   <item>Host disposal drops DTR, causing the daemon to release resources that are
///         then available to a fresh connection.</item>
///   <item>Both graceful and hard disconnects fault pending commands and stream
///         enumerations cleanly.</item>
///   <item>The input stream's custom exit-combo path terminates the daemon correctly
///         while ordinary Back presses flow through as regular events.</item>
/// </list>
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~HeartbeatTests"
///
/// Manual tests require human interaction and are tagged with
/// <c>[Trait("Category", "Manual")]</c>.
/// </summary>
[Collection(HeartbeatCollection.Name)]
public sealed class HeartbeatTests
{
    private readonly string _portName;

    public HeartbeatTests()
    {
        _portName = Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)
            ?? string.Empty;
    }

    // -------------------------------------------------------------------------
    // 1. Idle keep-alive (automated)
    // -------------------------------------------------------------------------

    /// <summary>
    /// An idle connection must stay alive for longer than the 10 s RX timeout.
    /// Both sides send bare-<c>\n</c> keep-alive frames every 3 s, so a 15 s
    /// silence window must be survived without triggering the watchdog.
    ///
    /// Validates: keep-alive frames are sent and received by both sides;
    /// <see cref="FlipperRpcClient.Disconnected"/> is NOT cancelled after 15 s
    /// of application-level inactivity.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task IdleConnection_SurvivesBeyondTimeout_PingSucceeds()
    {
        await using var client = new FlipperRpcClient(_portName);
        await client.ConnectAsync();

        // Wait 15 s — comfortably beyond the 10 s heartbeat timeout.
        // Both sides must exchange at least four bare-\n keep-alive frames
        // during this window; if either side's watchdog fires the
        // Disconnected token will be cancelled.
        await Task.Delay(TimeSpan.FromSeconds(15));

        Assert.False(client.Disconnected.IsCancellationRequested,
            "Disconnected token was cancelled during the idle window — " +
            "at least one keep-alive frame was missed.");

        var pong = await client.PingAsync();

        Assert.True(pong, "Ping returned false after surviving the idle window.");
    }

    // -------------------------------------------------------------------------
    // 2. Graceful daemon exit — host detects disconnect (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully (operator presses Back on the Flipper),
    /// it sends <c>{"disconnect":true}</c> before cleaning up.  The host reader
    /// loop must detect this frame and cancel
    /// <see cref="FlipperRpcClient.Disconnected"/> immediately — without waiting
    /// for the 10 s heartbeat timeout.
    ///
    /// Validates: <c>{"disconnect":true}</c> parsing in the reader loop and the
    /// <c>FaultAll</c> path it triggers.
    ///
    /// Requires manual interaction: press the Back button on the Flipper to stop
    /// the daemon within 60 seconds of the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task DaemonExitViaBackButton_HostDetectsDisconnect()
    {
        await using var client = new FlipperRpcClient(_portName);
        await client.ConnectAsync();

        // Give the tester up to 60 s to press Back on the Flipper.
        // The daemon sends {"disconnect":true} immediately on exit, so
        // detection should be near-instantaneous once Back is pressed.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Block until Disconnected fires or the 60 s deadline expires.
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationTokenSource
                .CreateLinkedTokenSource(client.Disconnected, deadline.Token).Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected — one of the two tokens fired */ }

        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected was not cancelled within 60 s. " +
            "Press Back on the Flipper to exit the daemon.");

        // Any subsequent command must throw, not hang.
        await Assert.ThrowsAsync<FlipperRpcException>(
            () => client.PingAsync());
    }

    // -------------------------------------------------------------------------
    // 3. Host disposal releases daemon resources (automated)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the host disposes its client the serial port is closed, which drops
    /// DTR.  The daemon's <c>on_ctrl_line_queue</c> detects the DTR de-assertion
    /// and immediately calls <c>stream_close_all()</c> + <c>resource_reset()</c>,
    /// freeing all hardware locks.
    ///
    /// This test acquires the IR receiver (an exclusive resource) on the first
    /// connection, disposes that client (dropping DTR), then reconnects and
    /// acquires IR again.  If the daemon did not release the resource the second
    /// open would fail with <c>resource_busy</c>.
    ///
    /// Validates: DTR-triggered cleanup in <c>on_ctrl_line_queue</c> and
    /// <c>resource_reset</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task HostDispose_DaemonReleasesResources_ReconnectCanAcquireSameResource()
    {
        // First connection — acquire IR receiver (exclusive resource).
        var firstClient = new FlipperRpcClient(_portName);
        await firstClient.ConnectAsync();

        var irStream = await firstClient.IrReceiveStartAsync();
        _ = irStream; // intentionally not disposed — let the client clean up

        // Dispose drops DTR; daemon must release RESOURCE_IR immediately.
        await firstClient.DisposeAsync();

        // Brief pause to let the daemon process the DTR change.
        await Task.Delay(200);

        // Second connection — must be able to acquire the same resource.
        await using var secondClient = new FlipperRpcClient(_portName);
        await secondClient.ConnectAsync();

        // If daemon did not release resources this throws FlipperRpcException("resource_busy").
        await using var secondIrStream = await secondClient.IrReceiveStartAsync();

        Assert.NotEqual(0u, secondIrStream.StreamId);
    }

    // -------------------------------------------------------------------------
    // 4a. Graceful disconnect during pending command (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully while an RPC command is in-flight, the
    /// pending <see cref="Task"/> must be faulted with
    /// <see cref="FlipperRpcException"/> rather than hanging indefinitely.
    ///
    /// The test dispatches a ping immediately before instructing the operator to
    /// press Back.  In practice the timing cannot be guaranteed, so the test
    /// uses a fire-and-forget ping with a long timeout, then waits for the
    /// <see cref="FlipperRpcClient.Disconnected"/> token to cancel and asserts
    /// the awaited result faults.
    ///
    /// Validates: <c>FaultAll</c> correctly fails all entries in the pending-
    /// request dictionary when a graceful disconnect is received.
    ///
    /// Requires manual interaction: press Back on the Flipper within 60 seconds.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task GracefulDisconnect_DuringPendingCommand_FaultsPendingTask()
    {
        await using var client = new FlipperRpcClient(_portName);
        await client.ConnectAsync();

        // Arrange: start a command that the tester can interrupt by pressing Back.
        // We use a very long cancellation window so the command stays in-flight
        // until the daemon exits.
        using var longTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var pingTask = client.PingAsync(longTimeout.Token);

        // Wait for the Disconnected token to fire (operator presses Back).
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
    // 4b. Hard disconnect during pending command (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the USB cable is physically unplugged while an RPC command is
    /// in-flight, the heartbeat watchdog must detect the silence and cancel
    /// <see cref="FlipperRpcClient.Disconnected"/> within the timeout window
    /// (≤ 10 s with default settings, or ≤ 4 s with the accelerated timing
    /// used here).  The pending command must be faulted rather than hanging.
    ///
    /// Uses a shorter heartbeat timeout (1 s interval, 4 s timeout) so the test
    /// completes quickly without changing production defaults.
    ///
    /// Validates: <see cref="FlipperZero.NET.HeartbeatTransport"/> RX watchdog
    /// and the <c>TriggerDisconnect → FaultAll</c> path.
    ///
    /// Requires manual interaction: unplug the USB cable within 30 seconds of
    /// the test starting (you will have ~5 s after the prompt appears).
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task HardDisconnect_DuringPendingCommand_FaultsPendingTask()
    {
        // Accelerated timing: 1 s keep-alive interval, 4 s RX timeout.
        // The daemon still uses its default 3 s / 10 s — the host-side
        // watchdog fires first since 4 s < 10 s.
        await using var client = new FlipperRpcClient(
            _portName,
            heartbeatInterval: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(4));

        await client.ConnectAsync();

        // Start an in-flight command that will be interrupted.
        using var longTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var pingTask = client.PingAsync(longTimeout.Token);

        // Wait for the Disconnected token (watchdog fires after ≤4 s of silence).
        // Give the operator 30 s to physically unplug the cable.
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

        // The pending ping must fault, not hang.
        await Assert.ThrowsAnyAsync<Exception>(() => pingTask);
    }

    // -------------------------------------------------------------------------
    // 5a. Graceful disconnect during stream iteration (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the daemon exits gracefully (Back button) while the host is blocked
    /// inside an <c>await foreach</c> on a stream, the enumeration must
    /// terminate — either by throwing <see cref="FlipperRpcException"/> or
    /// <see cref="OperationCanceledException"/> — rather than blocking forever.
    ///
    /// Uses a GPIO watch stream, which blocks waiting for pin-level changes.
    /// The tester presses Back on the Flipper while the host is awaiting events.
    ///
    /// Validates: <c>FaultAll</c> drains all active stream
    /// <see cref="System.Threading.Channels.Channel{T}"/>s and the linked
    /// cancellation token in <see cref="RpcStream{TEvent}.GetAsyncEnumerator"/>
    /// is triggered.
    ///
    /// Requires manual interaction: press Back on the Flipper within 60 seconds
    /// of the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task GracefulDisconnect_DuringStreamIteration_TerminatesEnumeration()
    {
        await using var client = new FlipperRpcClient(_portName);
        await client.ConnectAsync();

        await using var stream = await client.GpioWatchStartAsync(GpioPin.Pin6);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Exception? caughtException = null;

        // Iterate the stream until the daemon exits (Back button).
        // The loop must NOT block forever — it must exit with an exception.
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
            caughtException is OperationCanceledException or FlipperRpcException,
            $"Expected OperationCanceledException or FlipperRpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }

    // -------------------------------------------------------------------------
    // 5b. Hard disconnect during stream iteration (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the USB cable is physically unplugged while the host is blocked
    /// inside an <c>await foreach</c> on a stream, the heartbeat watchdog must
    /// detect the silence and terminate the enumeration within the timeout window
    /// (≤ 4 s with the accelerated timing used here).
    ///
    /// Uses a GPIO watch stream and the same short heartbeat timings as
    /// <see cref="HardDisconnect_DuringPendingCommand_FaultsPendingTask"/>.
    ///
    /// Validates: hard-disconnect path through
    /// <see cref="FlipperZero.NET.HeartbeatTransport"/>, <c>FaultAll</c>,
    /// and stream channel teardown.
    ///
    /// Requires manual interaction: unplug the USB cable within 30 seconds of
    /// the test starting.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task HardDisconnect_DuringStreamIteration_TerminatesEnumeration()
    {
        await using var client = new FlipperRpcClient(
            _portName,
            heartbeatInterval: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(4));

        await client.ConnectAsync();

        await using var stream = await client.GpioWatchStartAsync(GpioPin.Pin6);

        // Allow 30 s for the operator to unplug the cable;
        // the watchdog fires within 4 s of the unplug.
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
            caughtException is OperationCanceledException or FlipperRpcException,
            $"Expected OperationCanceledException or FlipperRpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }

    // -------------------------------------------------------------------------
    // 6. Input stream — custom exit combo triggers disconnect (manual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// With a custom exit combo (Ok+Long) registered on the input stream, the
    /// standard Back+Short key must be demoted to an ordinary event and must NOT
    /// stop the daemon.  Only pressing the custom combo (Ok+Long) must trigger
    /// daemon exit, which the host detects via the
    /// <see cref="FlipperRpcClient.Disconnected"/> token.
    ///
    /// Test flow:
    /// <list type="number">
    ///   <item>Open <c>input_listen_start</c> with exit override Ok+Long.</item>
    ///   <item>Tester presses Back twice — events must arrive in the stream
    ///         (daemon stays alive).</item>
    ///   <item>Tester long-presses Ok — daemon exits, host detects
    ///         <see cref="FlipperRpcClient.Disconnected"/> cancellation and
    ///         the <c>await foreach</c> terminates.</item>
    /// </list>
    ///
    /// Validates: custom exit-combo wiring in the input handler; graceful-exit
    /// detection via <c>{"disconnect":true}</c>; stream enumeration termination
    /// on disconnect.
    ///
    /// Requires manual interaction: press Back twice, then long-press Ok on the
    /// Flipper within 15 minutes.
    /// </summary>
    [Trait("Category", "Manual")]
    [RequiresFlipperFact]
    public async Task InputStream_CustomExitCombo_BackPassesThroughAndOkLongDisconnects()
    {
        await using var client = new FlipperRpcClient(_portName);
        await client.ConnectAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        // Override exit trigger to Ok+Long so Back+Short is a normal key.
        await using var stream = await client.InputListenStartAsync(
            exitKey: FlipperInputKey.Ok,
            exitType: FlipperInputType.Long,
            ct: timeout.Token);

        var backShortEvents = new List<FlipperInputEvent>();
        Exception? caughtException = null;

        try
        {
            await foreach (var evt in stream.WithCancellation(timeout.Token))
            {
                if (evt is { Key: FlipperInputKey.Back, Type: FlipperInputType.Short })
                {
                    backShortEvents.Add(evt);
                }

                // Once two Back+Short events are received, we continue iterating
                // and wait for the operator to long-press Ok (daemon exit).
                // The loop exits naturally when the daemon sends {"disconnect":true}.
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Both Back presses must have been received as normal events.
        Assert.True(backShortEvents.Count >= 2,
            $"Expected at least 2 Back+Short events before daemon exit, " +
            $"got {backShortEvents.Count}. " +
            "Press Back twice, then long-press Ok on the Flipper.");

        // The connection must now be in the disconnected state.
        Assert.True(client.Disconnected.IsCancellationRequested,
            "Disconnected token was not cancelled after the Ok+Long exit combo. " +
            "Long-press Ok on the Flipper to exit the daemon.");

        // The foreach must have exited with an exception (not silently — the
        // daemon exit is communicated via {"disconnect":true} which triggers FaultAll).
        Assert.NotNull(caughtException);
        Assert.True(
            caughtException is OperationCanceledException or FlipperRpcException,
            $"Expected OperationCanceledException or FlipperRpcException, " +
            $"got {caughtException.GetType().Name}: {caughtException.Message}");
    }
}
