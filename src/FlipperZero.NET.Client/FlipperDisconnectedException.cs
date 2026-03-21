namespace FlipperZero.NET;

/// <summary>
/// Identifies the cause of a <see cref="FlipperDisconnectedException"/>.
/// </summary>
public enum DisconnectReason
{
    /// <summary>
    /// The transport stream ended unexpectedly (e.g. USB cable pulled, serial port closed).
    /// </summary>
    ConnectionLost,

    /// <summary>
    /// The daemon sent a <c>{"t":2}</c> disconnect envelope (graceful daemon exit, e.g. user
    /// pressed the exit key on the Flipper).
    /// </summary>
    DaemonExited,

    /// <summary>
    /// No data was received from the Flipper within the configured heartbeat timeout window.
    /// </summary>
    HeartbeatTimeout,

    /// <summary>
    /// An unexpected exception escaped the reader loop.
    /// </summary>
    ReaderFailed,

    /// <summary>
    /// <see cref="FlipperRpcClient.DisposeAsync"/> was called while operations were in flight.
    /// </summary>
    ClientDisposed,
}

/// <summary>
/// Thrown whenever the connection to the Flipper is lost, regardless of how it was lost.
/// Subclass of <see cref="FlipperRpcException"/> so existing <c>catch (FlipperRpcException)</c>
/// blocks continue to work without modification.
///
/// <para>
/// All disconnection paths converge here:
/// <list type="bullet">
///   <item>Transport EOF / USB cable pull → <see cref="DisconnectReason.ConnectionLost"/></item>
///   <item>Daemon graceful exit <c>{"t":2}</c> → <see cref="DisconnectReason.DaemonExited"/></item>
///   <item>Heartbeat timeout → <see cref="DisconnectReason.HeartbeatTimeout"/></item>
///   <item>Reader loop crash → <see cref="DisconnectReason.ReaderFailed"/></item>
///   <item>Client disposed while in-flight → <see cref="DisconnectReason.ClientDisposed"/></item>
/// </list>
/// </para>
///
/// <para>
/// Code that needs to distinguish connection loss from daemon protocol errors should catch
/// <see cref="FlipperDisconnectedException"/> before the base <see cref="FlipperRpcException"/>.
/// </para>
/// </summary>
public sealed class FlipperDisconnectedException : FlipperRpcException
{
    /// <summary>Why the connection was lost.</summary>
    public DisconnectReason Reason { get; }

    /// <param name="reason">Why the connection was lost.</param>
    /// <param name="message">Human-readable message.</param>
    public FlipperDisconnectedException(DisconnectReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <param name="reason">Why the connection was lost.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="inner">The exception that caused the disconnect, if any.</param>
    public FlipperDisconnectedException(DisconnectReason reason, string message, Exception inner)
        : base(message, inner)
    {
        Reason = reason;
    }
}
