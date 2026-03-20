namespace FlipperZero.NET;

/// <summary>
/// An item placed on the outbound channel.
/// <see cref="Json"/> is the fully-serialised line (without trailing \n).
/// <see cref="RequestId"/> is used to register pending state before the line
/// is actually sent so the reader loop can never race ahead of registration.
/// </summary>
internal sealed class RpcWorkItem
{
    public required uint RequestId { get; init; }
    public required string Json { get; init; }
    public required string CommandName { get; init; }

    /// <summary>
    /// Called by the writer loop immediately after dequeuing
    /// (before the send) to register pending state in the router.
    /// </summary>
    public required Action Register { get; init; }
}
