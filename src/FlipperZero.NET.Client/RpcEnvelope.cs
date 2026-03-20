namespace FlipperZero.NET;

/// <summary>
/// The three V2 message types the daemon can send.
/// </summary>
internal enum RpcMessageType
{
    Response,
    Event,
    Disconnect,
    Unknown,
}

/// <summary>
/// Parsed representation of one V2 NDJSON envelope received from the daemon.
///
/// Wire formats:
/// <code>
/// {"type":"response","id":1,"payload":{...}}  — success with data
/// {"type":"response","id":1}                  — success, void
/// {"type":"response","id":1,"error":"..."}    — error
/// {"type":"event","id":7,"payload":{...}}     — stream event (id = stream id)
/// {"type":"disconnect"}                       — graceful daemon exit
/// </code>
/// </summary>
/// todo:think about compacting json by using one letter names for fields . This approach can be applied to all jsons (we already use C lang short forms, we can use them in json as well and this is really beneficial when in C no).
internal readonly struct RpcEnvelope
{
    // todo: think about using type value as numbers (enum values) to make json more compact
    public RpcMessageType Type { get; private init; }

    /// <summary>
    /// For <see cref="RpcMessageType.Response"/>: the request id.
    /// For <see cref="RpcMessageType.Event"/>: the stream id.
    /// <c>null</c> for <see cref="RpcMessageType.Disconnect"/> or unknown.
    /// </summary>
    public uint? Id { get; private init; }

    /// <summary>
    /// Content of the <c>"payload"</c> field, or a default (Undefined kind)
    /// <see cref="JsonElement"/> when the field is absent.
    /// </summary>
    public JsonElement Payload { get; private init; }

    /// <summary>
    /// Content of the <c>"error"</c> field, or <c>null</c> when absent.
    /// Non-null only for <see cref="RpcMessageType.Response"/> error messages.
    /// </summary>
    /// Todo: think about making a new RpcMessageType.Error. We can use id for event or response, but for distibuishing them - store also what is it - stream or response? Also, this type must not be on the envelop level, probably it should be inside the payload for the error type of message.
    public string? Error { get; private init; }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a single V2 NDJSON line into an <see cref="RpcEnvelope"/>.
    /// Returns a <see cref="RpcMessageType.Unknown"/> envelope on malformed input.
    /// </summary>
    public static RpcEnvelope Parse(string line)
    {
        try
        {
            // todo perfromance note - I am not sure that calling everytime TryGetProperty is a good idea if it is starts searching everytime from the first char
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return new RpcEnvelope { Type = RpcMessageType.Unknown };
            }

            var typeStr = typeProp.GetString();

            if (typeStr == "disconnect")
            {
                return new RpcEnvelope { Type = RpcMessageType.Disconnect };
            }

            root.TryGetProperty("payload", out var payload);

            string? error = null;
            if (root.TryGetProperty("error", out var errorProp))
            {
                error = errorProp.GetString();
            }

            uint? id = null;
            if (root.TryGetProperty("id", out var idProp) && idProp.TryGetUInt32(out var idVal))
            {
                id = idVal;
            }

            return typeStr switch
            {
                "response" => new RpcEnvelope
                {
                    Type = RpcMessageType.Response, Id = id, Payload = payload, Error = error,
                },
                "event" => new RpcEnvelope
                {
                    Type = RpcMessageType.Event, Id = id, Payload = payload,
                },
                _ => new RpcEnvelope
                {
                    Type = RpcMessageType.Unknown
                }
            };
        }
        catch
        {
            return new RpcEnvelope { Type = RpcMessageType.Unknown };
        }
    }
}
