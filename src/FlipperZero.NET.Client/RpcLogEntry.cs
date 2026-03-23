namespace FlipperZero.NET;

/// <summary>
/// The origin of an <see cref="RpcLogEntry"/>: the local C# client.
/// </summary>
public enum RpcLogSource
{
    /// <summary>
    /// The entry was produced by the C# client itself (writer or reader loop).
    /// </summary>
    Client,
}

/// <summary>
/// Describes the kind of event captured in an <see cref="RpcLogEntry"/>.
/// </summary>
public enum RpcLogKind
{
    /// <summary>A command JSON line was written to the serial port.</summary>
    CommandSent,

    /// <summary>A response JSON line was received from the serial port.</summary>
    ResponseReceived,

    /// <summary>A stream event JSON line was received from the serial port.</summary>
    StreamEventReceived,

    /// <summary>An unrecoverable transport error occurred.</summary>
    Error,
}

/// <summary>
/// A single diagnostic log entry produced by the C# client for every
/// sent command and every received response or stream event, with zero
/// additional protocol overhead.
/// </summary>
public readonly struct RpcLogEntry
{
    /// <summary>Where this entry originated.</summary>
    public RpcLogSource Source { get; init; }

    /// <summary>What kind of event this entry describes.</summary>
    public RpcLogKind Kind { get; init; }

    /// <summary>
    /// The request ID associated with this entry, or <c>null</c> for
    /// stream events that have no request ID.
    /// </summary>
    public uint? RequestId { get; init; }

    /// <summary>
    /// The stream ID associated with this entry, or <c>null</c> for
    /// request/response entries.
    /// </summary>
    public uint? StreamId { get; init; }

    /// <summary>
    /// The command name (e.g. <c>"gpio_read"</c>), if known at the time
    /// of this entry.  May be <c>null</c> for raw stream events.
    /// </summary>
    public string? CommandName { get; init; }

    /// <summary>
    /// The result string: <c>"ok"</c>, an error code such as
    /// <c>"resource_busy"</c>, or <c>null</c> if not yet known
    /// (e.g. for <see cref="RpcLogKind.CommandSent"/> entries).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// The full raw JSON line (request or response).
    /// Present for all entries.
    /// </summary>
    public string? RawJson { get; init; }

    /// <summary>
    /// For <see cref="RpcLogKind.ResponseReceived"/> entries:
    /// the time elapsed between the matching <see cref="RpcLogKind.CommandSent"/>
    /// and this response.  Measures the full client-observable round-trip
    /// (serialisation + USB transfer + daemon processing + USB return).
    /// <c>null</c> for all other entry kinds.
    /// </summary>
    public TimeSpan? RoundTrip { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{Kind,-22}");

        if (RequestId.HasValue)
        {
            sb.Append($" #{RequestId}");
        }

        if (StreamId.HasValue)
        {
            sb.Append($" s:{StreamId}");
        }

        if (CommandName is not null)
        {
            sb.Append($" {CommandName}");
        }

        if (Status is not null)
        {
            sb.Append($" -> {Status}");
        }

        if (RoundTrip.HasValue)
        {
            sb.Append($" (RT {RoundTrip.Value.TotalMilliseconds:F1}ms)");
        }

        return sb.ToString();
    }
}

