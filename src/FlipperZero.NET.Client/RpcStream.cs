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
/// </summary>
/// <typeparam name="TEvent">The event type emitted by this stream.</typeparam>
public sealed class RpcStream<TEvent> : IAsyncEnumerable<TEvent>, IAsyncDisposable
    where TEvent : struct
{
    private readonly uint _streamId;
    private readonly ChannelReader<JsonElement> _reader;
    private readonly Func<uint, Task> _closeAsync;
    private int _disposed;

    internal RpcStream(
        uint streamId,
        ChannelReader<JsonElement> reader,
        Func<uint, Task> closeAsync)
    {
        _streamId = streamId;
        _reader = reader;
        _closeAsync = closeAsync;
    }

    /// <summary>The numeric stream id assigned by the Flipper.</summary>
    public uint StreamId => _streamId;

    /// <inheritdoc/>
    public async IAsyncEnumerator<TEvent> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        await foreach(var element in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            TEvent evt;
            try
            {
                evt = JsonSerializer.Deserialize<TEvent>(element.GetRawText());
            }
            catch(JsonException)
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
        if(Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _closeAsync(_streamId).ConfigureAwait(false);
    }
}
