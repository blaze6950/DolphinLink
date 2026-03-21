using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.HardwareTests.System;

/// <summary>
/// Integration tests for <see cref="FlipperSystemExtensions.ConfigureAsync"/>.
///
/// Run with a Flipper Zero connected (daemon protocol version &gt;= 4):
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~ConfigureTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class ConfigureTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperSystemExtensions.ConfigureAsync"/> must echo back
    /// the values that were sent (happy-path round-trip).
    ///
    /// Uses values well above the daemon's minimum thresholds
    /// (heartbeat_ms &gt;= 500, timeout_ms &gt;= 2000, timeout_ms &gt; heartbeat_ms).
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_EchoesEffectiveValues()
    {
        var response = await Client.ConfigureAsync(heartbeatMs: 3000, timeoutMs: 10000);

        Assert.Equal(3000u, response.HeartbeatMs);
        Assert.Equal(10000u, response.TimeoutMs);
    }

    /// <summary>
    /// Sending the minimum legal values must succeed and echo them back.
    /// Validates: boundary values (heartbeat_ms = 500, timeout_ms = 2000)
    /// are accepted by the daemon without triggering <c>invalid_config</c>.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_MinimumLegalValues_Succeeds()
    {
        var response = await Client.ConfigureAsync(heartbeatMs: 500, timeoutMs: 2000);

        Assert.Equal(500u, response.HeartbeatMs);
        Assert.Equal(2000u, response.TimeoutMs);
    }

    /// <summary>
    /// Sending a <c>heartbeat_ms</c> below the daemon minimum (500) must
    /// return a <see cref="FlipperRpcException"/> with error code
    /// <c>invalid_config</c> and must NOT change the daemon's current values.
    ///
    /// Validates: daemon-side validation rejects out-of-range inputs.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_HeartbeatMsBelowMinimum_ThrowsInvalidConfig()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.ConfigureAsync(heartbeatMs: 499, timeoutMs: 10000));

        Assert.Equal("invalid_config", ex.ErrorCode);
    }

    /// <summary>
    /// Sending a <c>timeout_ms</c> below the daemon minimum (2000) must
    /// return a <see cref="FlipperRpcException"/> with error code
    /// <c>invalid_config</c>.
    ///
    /// Validates: daemon-side validation rejects out-of-range timeout values.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_TimeoutMsBelowMinimum_ThrowsInvalidConfig()
    {
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.ConfigureAsync(heartbeatMs: 500, timeoutMs: 1999));

        Assert.Equal("invalid_config", ex.ErrorCode);
    }

    /// <summary>
    /// Sending a <c>timeout_ms</c> that is not greater than <c>heartbeat_ms</c>
    /// must return a <see cref="FlipperRpcException"/> with error code
    /// <c>invalid_config</c>.
    ///
    /// Validates: the daemon enforces timeout_ms &gt; heartbeat_ms.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_TimeoutNotGreaterThanHeartbeat_ThrowsInvalidConfig()
    {
        // Both values are individually above their respective minimums,
        // but timeout_ms == heartbeat_ms — not strictly greater.
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.ConfigureAsync(heartbeatMs: 3000, timeoutMs: 3000));

        Assert.Equal("invalid_config", ex.ErrorCode);
    }

    /// <summary>
    /// The <c>configure</c> command must appear in <see cref="DaemonInfoResponse.Commands"/>
    /// on a daemon with protocol version &gt;= 4.
    ///
    /// Validates: <see cref="DaemonInfoResponse.Supports{TCommand}"/> correctly
    /// detects <see cref="ConfigureCommand"/> support, which is the gate used
    /// by <see cref="FlipperRpcClient.ConnectAsync"/> to decide whether to send
    /// the configure handshake.
    /// </summary>
    [Trait("Category", "Hardware")]
    [RequiresFlipperFact]
    public async Task Configure_IsReportedAsSupported_ByDaemonInfo()
    {
        var info = await Client.DaemonInfoAsync();

        Assert.True(info.Supports<ConfigureCommand>(),
            "Expected 'configure' to appear in daemon_info commands list " +
            "(requires daemon protocol version >= 4).");
    }
}
