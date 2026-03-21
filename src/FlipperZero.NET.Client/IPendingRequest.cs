namespace FlipperZero.NET;

/// <summary>
/// Type-erased interface for an in-flight RPC request stored in the pending table.
/// </summary>
internal interface IPendingRequest
{
    /// <summary>
    /// Stopwatch ticks recorded when the command line was sent.
    /// Set by the writer loop immediately after the send; read by the dispatcher
    /// to compute round-trip time.
    /// </summary>
    long SentTicks { get; set; }

    /// <summary>
    /// Called when a success response arrives.
    /// <paramref name="payload"/> is the content of the <c>"p"</c> field
    /// from the V3 envelope, or a default <see cref="JsonElement"/> for void responses.
    /// </summary>
    void Complete(JsonElement payload);

    /// <summary>Called when an error response or transport fault is detected.</summary>
    void Fail(Exception ex);
}
