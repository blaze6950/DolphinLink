namespace FlipperZero.NET;

/// <summary>
/// Connection-behaviour options for <see cref="FlipperRpcClient"/>.
///
/// All properties have sensible defaults so the struct can be used with
/// <c>default</c> or a <c>with</c>-expression to override only what differs:
///
/// <code>
/// // Zero-config — default 3 s heartbeat interval, 10 s timeout:
/// new FlipperRpcClient(transport)
///
/// // Accelerated timing only:
/// new FlipperRpcClient(transport, new FlipperRpcClientOptions
/// {
///     HeartbeatInterval = TimeSpan.FromSeconds(1),
///     Timeout           = TimeSpan.FromSeconds(4),
/// })
/// </code>
/// </summary>
public readonly record struct FlipperRpcClientOptions
{
    /// <summary>
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// Defaults to <see cref="HeartbeatTransport.DefaultHeartbeatInterval"/> (3 s).
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = HeartbeatTransport.DefaultHeartbeatInterval;

    /// <summary>
    /// How long inbound silence is allowed before the connection is declared lost.
    /// Must be strictly greater than <see cref="HeartbeatInterval"/>.
    /// Defaults to <see cref="HeartbeatTransport.DefaultTimeout"/> (10 s).
    /// </summary>
    public TimeSpan Timeout { get; init; } = HeartbeatTransport.DefaultTimeout;

    /// <summary>Initialises an options instance with default timing.</summary>
    public FlipperRpcClientOptions() { }
}
