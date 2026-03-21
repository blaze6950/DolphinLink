namespace FlipperZero.NET;

/// <summary>
/// Connection-behaviour options for <see cref="FlipperRpcClient"/>.
///
/// Both <c>default</c> and <c>new FlipperRpcClientOptions()</c> produce an
/// instance with the standard heartbeat timing (3 s interval, 10 s timeout).
/// Use a <c>with</c>-expression or object initialiser to override individual
/// values:
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
    // Raw backing values — TimeSpan.Zero means "use the default".
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// Defaults to <see cref="HeartbeatTransport.DefaultHeartbeatInterval"/> (3 s).
    /// </summary>
    public TimeSpan HeartbeatInterval
    {
        get => _heartbeatInterval == TimeSpan.Zero
            ? HeartbeatTransport.DefaultHeartbeatInterval
            : _heartbeatInterval;
        init => _heartbeatInterval = value;
    }

    /// <summary>
    /// How long inbound silence is allowed before the connection is declared lost.
    /// Must be strictly greater than <see cref="HeartbeatInterval"/>.
    /// Defaults to <see cref="HeartbeatTransport.DefaultTimeout"/> (10 s).
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout == TimeSpan.Zero
            ? HeartbeatTransport.DefaultTimeout
            : _timeout;
        init => _timeout = value;
    }
}
