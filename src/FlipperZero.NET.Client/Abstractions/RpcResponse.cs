using System.Text.Json.Serialization;

namespace FlipperZero.NET.Abstractions;

/// <summary>
/// Typed wrapper for the NDJSON response envelope sent by the Flipper daemon.
///
/// Wire format (success):
/// <code>
/// {"id":1,"status":"ok","data":{"pong":true}}
/// </code>
///
/// The envelope is deserialized here; <typeparamref name="TData"/> is
/// deserialized from the <c>"data"</c> property and returned to the caller.
/// Error responses are handled upstream in the dispatcher before this type
/// is ever constructed.
/// </summary>
/// <typeparam name="TData">
/// The command-specific payload type, e.g. <c>PingResponse</c>.
/// </typeparam>
public readonly struct RpcResponse<TData> where TData : struct, IRpcCommandResponse
{
    /// <summary>Echo of the request id.</summary>
    [JsonPropertyName("id")]
    public uint Id { get; init; }

    /// <summary><c>"ok"</c> on success.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Command-specific response payload.</summary>
    [JsonPropertyName("data")]
    public TData Data { get; init; }
}
