using System.Threading.Channels;

namespace FlipperZero.NET;

/// <summary>
/// State for an open stream, stored while the stream is alive.
/// </summary>
internal sealed class StreamState
{
    /// <summary>Channel events are pushed into.</summary>
    public required Channel<JsonElement> EventChannel { get; init; }
    /// <summary>Called when the stream is remotely closed or on error.</summary>
    public required Action Complete { get; init; }
    public required Action<Exception> Fault { get; init; }
}
