using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Represents an open server-push stream from the Flipper.
///
/// Usage
/// -----
/// <code>
/// await using var stream = await client.InputListenStartAsync();
/// await foreach (var evt in stream.WithCancellation(ct))
/// {
///     Console.WriteLine(evt.Key);
/// }
/// </code>
///
/// Disposing the stream sends <c>stream_close</c> to the Flipper and releases
/// the associated hardware resource (e.g. IR receiver).
///
/// If the connection to the Flipper is lost while iterating, the enumeration
/// is cancelled via the client's <see cref="FlipperRpcClient.Disconnected"/>
/// token, so the <c>await foreach</c> exits with an
/// <see cref="OperationCanceledException"/> instead of hanging forever.
///
/// Concurrent enumeration is not supported: a second call to
/// <see cref="GetAsyncEnumerator"/> while one is already active throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
/// <typeparam name="TEvent">The event type emitted by this stream.</typeparam>
public sealed class RpcStream<TEvent> : IAsyncEnumerable<TEvent>, IAsyncDisposable
    where TEvent : struct
{
    private readonly uint _streamId;
    private readonly ChannelReader<JsonElement> _reader;
    private readonly CancellationToken _disconnectToken;
    private int _disposed;
    private int _enumerating;

    /// <summary>
    /// Raised by <see cref="DisposeAsync"/> so the owning
    /// <see cref="FlipperRpcClient"/> can send <c>stream_close</c> without
    /// the stream holding a direct reference back to the client.
    /// </summary>
    internal event Func<uint, Task>? Closed;

    internal RpcStream(
        uint streamId,
        ChannelReader<JsonElement> reader,
        CancellationToken disconnectToken)
    {
        _streamId = streamId;
        _reader = reader;
        _disconnectToken = disconnectToken;
    }

    /// <summary>The numeric stream id assigned by the Flipper.</summary>
    public uint StreamId => _streamId;

    /// <inheritdoc/>
    public async IAsyncEnumerator<TEvent> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _enumerating, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "RpcStream does not support concurrent enumeration.");
        }

        try
        {
            // Link the caller's token with the client's disconnect token so that
            // either a user-requested cancellation OR a connection loss exits the
            // enumeration promptly instead of hanging indefinitely.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disconnectToken);

            await foreach (var element in _reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
            {
                TEvent evt;
                try
                {
                    evt = JsonSerializer.Deserialize<TEvent>(element.GetRawText());
                }
                catch (JsonException)
                {
                    // Malformed event payload — skip
                    continue;
                }
                yield return evt;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _enumerating, 0);
        }
    }

    /// <summary>
    /// Closes the stream: fires <see cref="Closed"/> (which causes the owning client
    /// to send <c>stream_close</c> to the Flipper), then releases resources.
    /// Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Closed is { } handler)
        {
            await handler(_streamId).ConfigureAwait(false);
        }
    }
}
