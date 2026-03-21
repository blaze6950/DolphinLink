using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Client.UnitTests.Infrastructure;

/// <summary>
/// An in-process <see cref="IFlipperTransport"/> for unit tests.
///
/// Usage
/// -----
/// • <see cref="EnqueueResponse"/> — schedules a response to be delivered to the
///   client reader loop after the client's next <see cref="SendAsync"/> completes.
///   This guarantees the pending-request entry is registered before the response
///   arrives, preventing a reader-loop race.
///
/// • <see cref="InjectEvent"/> — immediately delivers a line to the reader loop.
///   Use this for unsolicited stream events that are not tied to an outbound send.
///
/// Thread safety: all public methods may be called from any thread.
///
/// Heartbeat note
/// --------------
/// Use <see cref="CreateClient"/> (instead of <c>new FlipperRpcClient(transport)</c>)
/// to create a client that wraps this transport.  The factory sets a near-infinite
/// heartbeat interval and timeout so the heartbeat loop never fires during tests,
/// keeping <see cref="SentLines"/> free of keep-alive frames and preventing
/// spurious heartbeat-timeout faults.
/// </summary>
public sealed class FakeTransport : IFlipperTransport
{
    // Inbound channel: reader loop reads from here.
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    // Responses waiting to be triggered by the next SendAsync call (one per send).
    private readonly Queue<string> _responseQueue = new();
    private readonly object _responseQueueLock = new();

    // Lines the client sent (captured by SendAsync), excluding keep-alive frames.
    private readonly List<string> _sentLines = new();
    private readonly object _sentLock = new();

    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="FlipperRpcClient"/> wrapping this transport with
    /// near-infinite heartbeat timing so the heartbeat loop never fires during
    /// tests.  Always prefer this over <c>new FlipperRpcClient(this)</c>.
    /// </summary>
    public FlipperRpcClient CreateClient() =>
        new FlipperRpcClient(
            this,
            new FlipperRpcClientOptions
            {
                HeartbeatInterval = TimeSpan.FromHours(1),
                Timeout = TimeSpan.FromHours(2),
            });

    /// <summary>
    /// All JSON lines sent by the client (in order, without trailing newline).
    /// Keep-alive frames (empty strings) are excluded.
    /// </summary>
    public IReadOnlyList<string> SentLines
    {
        get { lock (_sentLock) { return _sentLines.ToList(); } }
    }

    /// <summary>
    /// Schedules <paramref name="json"/> to be delivered to the client's reader
    /// loop as a daemon response immediately after the client's next
    /// <see cref="SendAsync"/> completes.  This ensures the pending-request
    /// entry is registered before the response is dispatched.
    /// </summary>
    public void EnqueueResponse(string json)
    {
        lock (_responseQueueLock)
        {
            _responseQueue.Enqueue(json);
        }
    }

    /// <summary>
    /// Immediately writes <paramref name="json"/> into the inbound channel so
    /// the client's reader loop receives it as an unsolicited push (e.g. a stream
    /// event) without waiting for an outbound send.
    /// </summary>
    public void InjectEvent(string json) =>
        _inbound.Writer.TryWrite(json);

    /// <summary>
    /// Simulates a transport disconnect by completing the inbound channel,
    /// which causes <see cref="ReceiveAsync"/> to end its enumeration.
    /// </summary>
    public void SimulateDisconnect() =>
        _inbound.Writer.TryComplete();

    // -------------------------------------------------------------------------
    // IFlipperTransport
    // -------------------------------------------------------------------------

    public ValueTask OpenAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask SendAsync(string data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Exclude keep-alive frames (empty strings sent by HeartbeatTransport).
        if (data.Length > 0)
        {
            lock (_sentLock)
            {
                _sentLines.Add(data);
            }
        }

        // Deliver the next scripted response now that the send has completed.
        // Keep-alive frames do not consume a queued response.
        string? response = null;
        if (data.Length > 0)
        {
            lock (_responseQueueLock)
            {
                if (_responseQueue.Count > 0)
                {
                    response = _responseQueue.Dequeue();
                }
            }
        }

        if (response is not null)
        {
            _inbound.Writer.TryWrite(response);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var line in _inbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public ValueTask DisposeAsync()
    {
        SimulateDisconnect();
        return ValueTask.CompletedTask;
    }
}
