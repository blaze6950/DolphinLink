using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.Core;

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
/// Hardware tests for the heartbeat / keep-alive mechanism.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~HeartbeatTests"
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
    // 2. Host disposal releases daemon resources (automated)
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
}
