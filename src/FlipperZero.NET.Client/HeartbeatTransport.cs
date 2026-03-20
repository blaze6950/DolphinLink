using System.Diagnostics;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET;

/// <summary>
/// Transport-level bidirectional keep-alive wrapper.
///
/// Architecture
/// ============
///
/// <code>
///   IFlipperTransport  (raw: Serial, BLE, TCP, …)
///       ↑
///   HeartbeatTransport (this class)
///       ↑
///   FlipperRpcClient   (RPC logic, streams, requests)
/// </code>
///
/// Responsibilities
/// ----------------
/// 1. Proxy all <see cref="SendLineAsync"/> / <see cref="ReadLineAsync"/> calls
///    transparently to the inner transport.
/// 2. Maintain <c>lastSeen</c> (timestamp of last received line) and
///    <c>lastSent</c> (timestamp of last sent line), updated on every I/O call.
/// 3. Run a background heartbeat loop that:
///    - Sends a bare keep-alive frame (<c>\n</c>) when the outbound channel
///      has been idle for <see cref="_heartbeatInterval"/>.
///    - Raises <see cref="Disconnected"/> when no inbound data has arrived
///      for <see cref="_timeout"/>.
/// 4. Intercept inbound keep-alive frames before forwarding to the caller:
///    any empty / whitespace-only line is treated as proof-of-life and
///    consumed here — the RPC layer above never sees it.
///
/// Heartbeat design
/// ----------------
/// - Heartbeat is NOT request/response. No ping, no ack.
/// - ANY incoming message (RPC response, stream event, or keep-alive frame)
///   updates <c>lastSeen</c>.
/// - Heartbeat is sent based ONLY on <c>lastSent</c>, independent of receive
///   activity.
/// - Payload is a bare <c>\n</c> — minimum NDJSON frame (1 byte on the wire).
///
/// Timing
/// ------
/// <code>
///   delay = min(
///       timeout         − (now − lastSeen),
///       heartbeatInterval − (now − lastSent)
///   )
/// </code>
///
/// Thread safety
/// -------------
/// <c>_lastSeenTicks</c> and <c>_lastSentTicks</c> are updated via
/// <see cref="Interlocked.Exchange(ref long, long)"/> so they are safe to
/// read from the heartbeat loop concurrently with the reader / writer loops.
///
/// Disconnect is triggered at most once via
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>.
///
/// Threading contract (inherited from <see cref="IFlipperTransport"/>)
/// -------------------------------------------------------------------
/// - <see cref="SendLineAsync"/> is called exclusively by the writer loop
///   (single writer).
/// - <see cref="ReadLineAsync"/> is called exclusively by the reader loop
///   (single reader).
/// - <see cref="Open"/> and <see cref="Close"/> are called outside both loops.
/// </summary>
internal sealed class HeartbeatTransport : IFlipperTransport
{
    // -------------------------------------------------------------------------
    // Defaults
    // -------------------------------------------------------------------------

    /// <summary>
    /// Default interval between keep-alive frames when the outbound channel
    /// is idle.  Matches the daemon's <c>HEARTBEAT_TX_IDLE_MS</c>.
    /// </summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Default silence duration after which the connection is declared lost.
    /// Must be greater than <see cref="DefaultHeartbeatInterval"/>.
    /// Matches the daemon's <c>HEARTBEAT_RX_TIMEOUT_MS</c>.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly IFlipperTransport _inner;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _clock = new();

    /// <summary>
    /// Stopwatch ticks of the last received line (any line, including keep-alives).
    /// 0 = no data received yet.
    /// Written by the reader loop; read by the heartbeat loop via
    /// <see cref="Interlocked.Read"/>.
    /// </summary>
    private long _lastSeenTicks;

    /// <summary>
    /// Stopwatch ticks of the last sent line (any line, including keep-alives).
    /// 0 = no data sent yet.
    /// Written by the writer loop; read by the heartbeat loop via
    /// <see cref="Interlocked.Read"/>.
    /// </summary>
    private long _lastSentTicks;

    /// <summary>
    /// 0 = connected, 1 = disconnect already triggered.
    /// Guards <see cref="Disconnected"/> so it fires exactly once.
    /// </summary>
    private int _disconnected;

    private Task? _heartbeatTask;
    private readonly CancellationTokenSource _heartbeatCts = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised exactly once when the connection is considered lost:
    /// either <see cref="_timeout"/> seconds have elapsed with no received data,
    /// or sending a keep-alive frame failed.
    ///
    /// The event is raised on the heartbeat background task's thread.
    /// Handlers must be short-running and must not throw.
    /// </summary>
    public event Action? Disconnected;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <param name="inner">
    /// The raw transport to wrap. Must not yet be opened.
    /// Ownership transfers to <see cref="HeartbeatTransport"/>:
    /// <see cref="Open"/>, <see cref="Close"/>, and
    /// <see cref="DisposeAsync"/> are forwarded to it.
    /// </param>
    /// <param name="heartbeatInterval">
    /// How long outbound silence is allowed before a keep-alive frame is sent.
    /// Defaults to <see cref="DefaultHeartbeatInterval"/> (3 s).
    /// </param>
    /// <param name="timeout">
    /// How long inbound silence is allowed before the connection is declared
    /// lost. Must be strictly greater than <paramref name="heartbeatInterval"/>.
    /// Defaults to <see cref="DefaultTimeout"/> (10 s).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="timeout"/> is not strictly greater than
    /// <paramref name="heartbeatInterval"/>.
    /// </exception>
    public HeartbeatTransport(
        IFlipperTransport inner,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? timeout = null)
    {
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _timeout = timeout ?? DefaultTimeout;

        if(_timeout <= _heartbeatInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"Timeout ({_timeout}) must be strictly greater than " +
                $"heartbeatInterval ({_heartbeatInterval}).");
        }

        _inner = inner;
    }

    // -------------------------------------------------------------------------
    // IFlipperTransport — Open / Close
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Open()
    {
        _inner.Open();
        _clock.Start();
        _heartbeatTask = Task.Run(
            () => HeartbeatLoopAsync(_heartbeatCts.Token));
    }

    /// <inheritdoc/>
    public void Close()
    {
        // Cancel the heartbeat loop before closing the inner transport.
        // The loop awaits Task.Delay with the heartbeat CTS, so cancellation
        // exits cleanly. The inner Close() then unblocks any pending
        // ReadLineAsync on the reader loop thread.
        _heartbeatCts.Cancel();
        _inner.Close();
    }

    // -------------------------------------------------------------------------
    // IFlipperTransport — Send / Receive
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forwards the line to the inner transport and records the send timestamp.
    /// </summary>
    public async Task SendLineAsync(string json, CancellationToken ct)
    {
        await _inner.SendLineAsync(json, ct).ConfigureAwait(false);

        // Record after a successful send. A failed send throws, so we never
        // update _lastSentTicks for a frame that was not actually transmitted.
        Interlocked.Exchange(ref _lastSentTicks, _clock.ElapsedTicks);
    }

    /// <summary>
    /// Reads the next line from the inner transport.
    ///
    /// - Updates <c>lastSeen</c> on every non-null line (proof-of-life).
    /// - Intercepts keep-alive frames (empty / whitespace lines) and loops
    ///   without returning them to the caller: the RPC layer never sees them.
    /// - Returns <c>null</c> on EOF / transport close.
    /// </summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while(true)
        {
            var line = await _inner.ReadLineAsync(ct).ConfigureAwait(false);

            if(line is null)
            {
                // EOF — inner transport closed. Return null to propagate.
                return null;
            }

            // Any received bytes are proof-of-life, regardless of content.
            Interlocked.Exchange(ref _lastSeenTicks, _clock.ElapsedTicks);

            if(line.AsSpan().Trim().IsEmpty)
            {
                // Keep-alive frame from the remote side: a bare \n.
                // Update lastSeen (done above) and loop — do not forward.
                continue;
            }

            return line;
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Cancel the heartbeat loop and wait for it to exit before tearing down
        // the inner transport. This prevents the loop from racing against dispose.
        await _heartbeatCts.CancelAsync().ConfigureAwait(false);

        if(_heartbeatTask is not null)
        {
            await _heartbeatTask.ConfigureAwait(false);
        }

        _heartbeatCts.Dispose();
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Heartbeat loop
    // -------------------------------------------------------------------------

    /// <summary>
    /// Background task that maintains bidirectional keep-alive.
    ///
    /// Each iteration:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     Check RX timeout: if <c>now − lastSeen > timeout</c>, trigger
    ///     disconnect and return.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Check TX heartbeat: if <c>now − lastSent ≥ heartbeatInterval</c>,
    ///     send a keep-alive frame.  On failure, trigger disconnect and return.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Compute dynamic delay and sleep.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// The RX timeout check is skipped until the first line has been received
    /// (<c>lastSeen == 0</c>), to avoid false-positives during the initial
    /// connection and capability negotiation phase which may take several
    /// seconds on slow ports.
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var nowTicks = _clock.ElapsedTicks;

                var lastSeen = Interlocked.Read(ref _lastSeenTicks);
                var lastSent = Interlocked.Read(ref _lastSentTicks);

                // ---- RX timeout check ----
                // Skip until the first line arrives (grace period during connect).
                if(lastSeen > 0)
                {
                    var silenceSeen = TimeSpan.FromTicks(nowTicks - lastSeen);
                    if(silenceSeen > _timeout)
                    {
                        TriggerDisconnect();
                        return;
                    }
                }

                // ---- TX heartbeat check ----
                // Send heartbeat based ONLY on lastSent, regardless of receive activity.
                var silenceSent = lastSent == 0
                    ? _heartbeatInterval               // no send yet → send immediately
                    : TimeSpan.FromTicks(nowTicks - lastSent);

                if(silenceSent >= _heartbeatInterval)
                {
                    try
                    {
                        // Empty string → the transport appends \n → bare \n on the wire.
                        // This is the minimum NDJSON keep-alive frame.
                        await _inner.SendLineAsync(string.Empty, ct).ConfigureAwait(false);

                        // Update lastSent directly (bypass our own SendLineAsync so
                        // we don't double-count, but keep the timestamp accurate).
                        Interlocked.Exchange(ref _lastSentTicks, _clock.ElapsedTicks);
                    }
                    catch(OperationCanceledException)
                    {
                        return; // Shutting down — exit cleanly.
                    }
                    catch
                    {
                        // Send failed (port closed, USB pulled, etc.)
                        TriggerDisconnect();
                        return;
                    }
                }

                // ---- Dynamic delay ----
                // Sleep until the next event that might require action:
                //   • the RX timeout would fire, or
                //   • the next TX heartbeat is due,
                // whichever comes first.  Re-read ticks after the send above.
                var now2Ticks = _clock.ElapsedTicks;
                var lastSeen2 = Interlocked.Read(ref _lastSeenTicks);
                var lastSent2 = Interlocked.Read(ref _lastSentTicks);

                var timeoutDelay = lastSeen2 == 0
                    ? _timeout                                                  // still waiting for first RX
                    : _timeout - TimeSpan.FromTicks(now2Ticks - lastSeen2);

                var heartbeatDelay = lastSent2 == 0
                    ? _heartbeatInterval
                    : _heartbeatInterval - TimeSpan.FromTicks(now2Ticks - lastSent2);

                // Clamp to [1 ms, timeout] to avoid spin-loops or excessively long sleeps.
                var delay = Min(timeoutDelay, heartbeatDelay);
                delay = Max(delay, TimeSpan.FromMilliseconds(1));
                delay = Min(delay, _timeout);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException)
        {
            // Normal shutdown via CancellationToken — exit cleanly.
        }
    }

    // -------------------------------------------------------------------------
    // Disconnect (fires exactly once)
    // -------------------------------------------------------------------------

    private void TriggerDisconnect()
    {
        // CompareExchange: only the first caller transitions 0 → 1 and fires the event.
        if(Interlocked.CompareExchange(ref _disconnected, 1, 0) == 0)
        {
            Disconnected?.Invoke();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
