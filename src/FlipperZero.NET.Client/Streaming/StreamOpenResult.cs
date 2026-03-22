using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Streaming;

/// <summary>
/// Deserialized from the <c>"p"</c> field of a V3 stream-open response:
/// <code>{"t":0,"i":N,"p":{"s":M}}</code>
/// Used as the <typeparamref name="TResponse"/> for stream-opening commands so the
/// generic pending-request machinery can extract the assigned stream id.
/// </summary>
internal readonly struct StreamOpenResult : IRpcCommandResponse
{
    /// <summary>The numeric stream id assigned by the Flipper daemon.</summary>
    [JsonPropertyName("s")]
    public uint StreamId { get; init; }
}
