using System.Diagnostics;
using System.Runtime.CompilerServices;
using DolphinLink.Client.Abstractions;

namespace DolphinLink.Client.Transport;

/// <summary>
/// Transport-level bidirectional keep-alive wrapper.
///
/// Architecture
/// ============
/// <code>
///   PacketSerializationTransport  (single-writer serialiser)
///       ↑
///   HeartbeatTransport            (this class)
///       ↑
///   RpcClient              (RPC logic)
/// </code>
///
/// Responsibilities
/// ----------------
/// 1. Proxy all <see cref="SendAsync"/> / <see cref="ReceiveAsync"/> calls
///    transparently to the inner transport.
/// 2. Maintain <c>lastSeen</c> (timestamp of last received line) and
///    <c>lastSent</c> (timestamp of last sent line), updated on every I/O call.
/// 3. Run a background heartbeat loop that:
///    - Sends an empty string (<c>""</c>) via <see cref="SendAsync"/> — the
///      underlying transport appends <c>\n</c>, producing a bare keep-alive frame —
///      when the outbound channel has been idle for <see cref="_heartbeatInterval"/>.
///    - Raises <see cref="Disconnected"/> when no inbound data has arrived
///      for <see cref="_timeout"/>.
/// 4. Intercept inbound keep-alive frames before forwarding to the caller:
///    any empty / whitespace-only line is consumed here and does not appear
///    in the <see cref="ReceiveAsync"/> enumerable.
///
/// Heartbeat design
/// ----------------
/// - Heartbeat is NOT request/response. No ping, no ack.
/// - ANY incoming line (RPC response, stream event, or keep-alive) updates <c>lastSeen</c>.
/// - Heartbeat is sent based ONLY on <c>lastSent</c>, independent of receive activity.
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
/// </summary>
internal sealed class HeartbeatTransport : ITransport
{
    // -------------------------------------------------------------------------
    // Defaults
    // -------------------------------------------------------------------------

    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly ITransport _inner;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _timeout;

    // Absolute Stopwatch timestamps (from Stopwatch.GetTimestamp()) for the
    // last received / sent lines.
    // Using Stopwatch.GetTimestamp() + Stopwatch.GetElapsedTime() avoids the
    // TimeSpan.FromTicks(ElapsedTicks) unit-mismatch bug (Stopwatch ticks ≠ TimeSpan ticks).
    private long _lastSeenTimestamp;
    private long _lastSentTimestamp;
    private int _disconnected;

    private Task? _heartbeatTask;
    private readonly CancellationTokenSource _heartbeatCts = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised exactly once when the connection is considered lost (inbound silence
    /// exceeds <see cref="_timeout"/>, or a keep-alive send fails).
    /// Handlers must be short-running and must not throw.
    /// </summary>
    public event Action? Disconnected;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public HeartbeatTransport(
        ITransport inner,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? timeout = null)
    {
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _timeout = timeout ?? DefaultTimeout;

        if (_timeout <= _heartbeatInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"Timeout ({_timeout}) must be strictly greater than " +
                $"heartbeatInterval ({_heartbeatInterval}).");
        }

        _inner = inner;
    }

    // -------------------------------------------------------------------------
    // ITransport
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask OpenAsync(CancellationToken ct = default)
    {
        await _inner.OpenAsync(ct).ConfigureAwait(false);
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(string data, CancellationToken ct = default)
    {
        await _inner.SendAsync(data, ct).ConfigureAwait(false);
        Interlocked.Exchange(ref _lastSentTimestamp, Stopwatch.GetTimestamp());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Keep-alive frames (empty / whitespace-only lines) are consumed here and
    /// not yielded to the caller. All received lines update <c>lastSeen</c>.
    /// </remarks>
    public async IAsyncEnumerable<string> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var line in _inner.ReceiveAsync(ct).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _lastSeenTimestamp, Stopwatch.GetTimestamp());

            if (line.AsSpan().Trim().IsEmpty)
            {
                // Keep-alive frame — update lastSeen (done above) and skip.
                continue;
            }

            yield return line;
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await _heartbeatCts.CancelAsync().ConfigureAwait(false);

        if (_heartbeatTask is not null)
        {
            await _heartbeatTask.ConfigureAwait(false);
        }

        _heartbeatCts.Dispose();
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Heartbeat loop
    // -------------------------------------------------------------------------

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var nowTs   = Stopwatch.GetTimestamp();
                var lastSeen = Interlocked.Read(ref _lastSeenTimestamp);
                var lastSent = Interlocked.Read(ref _lastSentTimestamp);

                // ---- RX timeout check ----
                // Skip until the first line arrives (grace period during connect).
                if (lastSeen > 0)
                {
                    var silenceSeen = Stopwatch.GetElapsedTime(lastSeen, nowTs);
                    if (silenceSeen > _timeout)
                    {
                        TriggerDisconnect();
                        return;
                    }
                }

                // ---- TX heartbeat check ----
                var silenceSent = lastSent == 0
                    ? _heartbeatInterval
                    : Stopwatch.GetElapsedTime(lastSent, nowTs);

                if (silenceSent >= _heartbeatInterval)
                {
                    try
                    {
                        // Empty string → transport appends \n → bare \n on the wire.
                        await _inner.SendAsync(string.Empty, ct).ConfigureAwait(false);
                        Interlocked.Exchange(ref _lastSentTimestamp, Stopwatch.GetTimestamp());
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch
                    {
                        TriggerDisconnect();
                        return;
                    }
                }

                // ---- Dynamic delay ----
                var now2Ts   = Stopwatch.GetTimestamp();
                var lastSeen2 = Interlocked.Read(ref _lastSeenTimestamp);
                var lastSent2 = Interlocked.Read(ref _lastSentTimestamp);

                var timeoutDelay = lastSeen2 == 0
                    ? _timeout
                    : _timeout - Stopwatch.GetElapsedTime(lastSeen2, now2Ts);

                var heartbeatDelay = lastSent2 == 0
                    ? _heartbeatInterval
                    : _heartbeatInterval - Stopwatch.GetElapsedTime(lastSent2, now2Ts);

                var delay = Min(timeoutDelay, heartbeatDelay);
                delay = Max(delay, TimeSpan.FromMilliseconds(1));
                delay = Min(delay, _timeout);

                await Task.Delay(delay, ct).ConfigureAwait(false);

                // Yield to give the JS event loop a turn.  In single-threaded
                // Blazor WASM, Task.Delay resumes via a .NET timer continuation
                // that does NOT pump the browser event loop — so reader.read()
                // in the WebSerial JS pump would starve indefinitely.
                // Task.Yield() in WASM schedules the continuation via
                // setTimeout(0), guaranteeing that pending JS microtasks and
                // I/O callbacks (USB data-arrived events, etc.) run before
                // this loop resumes.  On desktop .NET this is harmless — it
                // simply re-queues the continuation to the ThreadPool.
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    // -------------------------------------------------------------------------
    // Disconnect (fires exactly once)
    // -------------------------------------------------------------------------

    private void TriggerDisconnect()
    {
        if (Interlocked.CompareExchange(ref _disconnected, 1, 0) == 0)
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
