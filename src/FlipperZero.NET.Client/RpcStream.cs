using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// Represents an open server-push stream from the Flipper.
///
/// Usage
/// -----
/// <code>
/// await using var stream = await client.BleScanStartAsync();
/// await foreach (var evt in stream.WithCancellation(ct))
/// {
///     Console.WriteLine(evt.Address);
/// }
/// </code>
///
/// Disposing the stream sends <c>stream_close</c> to the Flipper and releases
/// the associated hardware resource (e.g. BLE radio).
///
/// If the connection to the Flipper is lost while iterating, the enumeration
/// is cancelled via the client's <see cref="FlipperRpcClient.Disconnected"/>
/// token, so the <c>await foreach</c> exits with an
/// <see cref="OperationCanceledException"/> instead of hanging forever.
/// </summary>
/// <typeparam name="TEvent">The event type emitted by this stream.</typeparam>
public sealed class RpcStream<TEvent> : IAsyncEnumerable<TEvent>, IAsyncDisposable
    where TEvent : struct
{
    private readonly uint _streamId;
    private readonly ChannelReader<JsonElement> _reader;
    private readonly Func<uint, Task> _closeAsync;
    private readonly CancellationToken _disconnectToken;
    private int _disposed;

    internal RpcStream(
        uint streamId,
        ChannelReader<JsonElement> reader,
        Func<uint, Task> closeAsync,
        CancellationToken disconnectToken)
    {
        _streamId = streamId;
        _reader = reader;
        _closeAsync = closeAsync;
        _disconnectToken = disconnectToken;
    }

    /// <summary>The numeric stream id assigned by the Flipper.</summary>
    public uint StreamId => _streamId;

    /// <inheritdoc/>
    public async IAsyncEnumerator<TEvent> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Closes the stream: sends <c>stream_close</c> to the Flipper, drains
    /// the internal channel and releases resources.
    /// Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _closeAsync(_streamId).ConfigureAwait(false);
    }
}
