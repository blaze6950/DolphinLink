namespace FlipperZero.NET;

/// <summary>
/// Type-erased callbacks stored in the pending-request table.
/// </summary>
internal sealed class PendingRequest
{
    /// <summary>Called when a <c>"status":"ok"</c> or <c>"stream"</c> response arrives.</summary>
    public required Action<JsonElement> OnSuccess { get; init; }

    /// <summary>Called when an <c>"error"</c> response arrives.</summary>
    public required Action<string> OnError { get; init; }

    /// <summary>
    /// Stopwatch ticks recorded when the command line was sent.
    /// Set by the writer loop immediately after <see cref="FlipperRpcTransport.SendLineAsync"/>;
    /// read by the dispatcher to compute round-trip time.
    /// </summary>
    public long SentTicks { get; set; }
}