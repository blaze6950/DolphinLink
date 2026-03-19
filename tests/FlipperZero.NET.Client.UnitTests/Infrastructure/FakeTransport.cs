using System.Threading.Channels;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Client.UnitTests.Infrastructure;

/// <summary>
/// An in-process <see cref="IFlipperTransport"/> for unit tests.
///
/// Usage
/// -----
/// • <see cref="EnqueueResponse"/> — schedules a response to be delivered to the
///   client reader loop after the client's next outbound <see cref="SendLineAsync"/>.
///   This guarantees the pending-request entry is registered before the response
///   arrives, preventing a reader-loop race.
///
/// • <see cref="InjectEvent"/> — immediately delivers a line to the reader loop.
///   Use this for unsolicited stream events that are not tied to an outbound send.
///
/// Thread safety: all public methods may be called from any thread.
/// </summary>
public sealed class FakeTransport : IFlipperTransport
{
    // Inbound channel: reader loop reads from here.
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    // Responses waiting to be triggered by the next SendLineAsync call (one per send).
    private readonly Queue<string> _responseQueue = new();
    private readonly object _responseQueueLock = new();

    // Lines the client sent (captured by SendLineAsync).
    private readonly List<string> _sentLines = new();
    private readonly object _sentLock = new();

    private bool _closed;

    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// All JSON lines sent by the client (in order, without trailing newline).
    /// </summary>
    public IReadOnlyList<string> SentLines
    {
        get { lock(_sentLock) { return _sentLines.ToList(); } }
    }

    /// <summary>
    /// Schedules <paramref name="json"/> to be delivered to the client's reader
    /// loop as a daemon response immediately after the client's next
    /// <see cref="SendLineAsync"/> completes.  This ensures the pending-request
    /// entry is registered before the response is dispatched.
    /// </summary>
    public void EnqueueResponse(string json)
    {
        lock(_responseQueueLock)
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
    /// Closes the transport, unblocking <see cref="ReadLineAsync"/> with <c>null</c>.
    /// </summary>
    public void SimulateDisconnect() => Close();

    // -------------------------------------------------------------------------
    // IFlipperTransport
    // -------------------------------------------------------------------------

    public void Open()
    {
        _closed = false;
    }

    public void Close()
    {
        _closed = true;
        _inbound.Writer.TryComplete();
    }

    public Task SendLineAsync(string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock(_sentLock)
        {
            _sentLines.Add(json);
        }

        // Deliver the next scripted response now that the send (and the preceding
        // Register() call in the writer loop) has completed.
        string? response = null;
        lock(_responseQueueLock)
        {
            if(_responseQueue.Count > 0)
            {
                response = _responseQueue.Dequeue();
            }
        }

        if(response is not null)
        {
            _inbound.Writer.TryWrite(response);
        }

        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        if(_closed)
        {
            return null;
        }

        try
        {
            if(await _inbound.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                _inbound.Reader.TryRead(out var line);
                return line;
            }
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(ChannelClosedException) { /* fall through */ }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }
}
