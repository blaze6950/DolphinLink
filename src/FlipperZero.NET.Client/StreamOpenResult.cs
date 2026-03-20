using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET;

/// <summary>
/// Deserialized from the <c>"payload"</c> field of a V2 stream-open response:
/// <code>{"type":"response","id":N,"payload":{"stream":M}}</code>
/// Used as the <typeparamref name="TResponse"/> for stream-opening commands so the
/// generic pending-request machinery can extract the assigned stream id.
/// </summary>
internal readonly struct StreamOpenResult : IRpcCommandResponse
{
    /// <summary>The numeric stream id assigned by the Flipper daemon.</summary>
    [JsonPropertyName("stream")]
    public uint StreamId { get; init; }
}
