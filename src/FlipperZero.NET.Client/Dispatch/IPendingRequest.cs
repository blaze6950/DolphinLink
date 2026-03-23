namespace FlipperZero.NET.Dispatch;

/// <summary>
/// Type-erased interface for an in-flight RPC request stored in the pending table.
/// </summary>
internal interface IPendingRequest
{
    /// <summary>
    /// Absolute timestamp recorded when the command line was sent, obtained via
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.
    /// Set by the writer loop immediately after the send; read by the dispatcher
    /// to compute round-trip time via <see cref="System.Diagnostics.Stopwatch.GetElapsedTime"/>.
    /// </summary>
    long SentTimestamp { get; set; }

    /// <summary>
    /// The command name (e.g. <c>"device_info"</c>) associated with this request.
    /// Set by the writer loop after the send; read by the dispatcher to populate
    /// <see cref="RpcLogEntry.CommandName"/> on the <see cref="RpcLogKind.ResponseReceived"/>
    /// log entry so the normalizer can expand abbreviated wire keys.
    /// </summary>
    string? CommandName { get; set; }

    /// <summary>
    /// Called when a success response arrives.
    /// <paramref name="payload"/> is the content of the <c>"p"</c> field
    /// from the V3 envelope, or a default <see cref="JsonElement"/> for void responses.
    /// </summary>
    void Complete(JsonElement payload);

    /// <summary>Called when an error response or transport fault is detected.</summary>
    void Fail(Exception ex);
}
