namespace DolphinLink.Client.Exceptions;

/// <summary>
/// Thrown when the Flipper returns an <c>"error"</c> field in the RPC response,
/// or when the transport encounters a protocol-level problem.
/// </summary>
public class RpcException : Exception
{
    /// <summary>
    /// The error code string returned by the daemon, e.g. <c>"resource_busy"</c>,
    /// <c>"unknown_command"</c>, <c>"stream_not_found"</c>.
    /// <c>null</c> for transport / framing errors that have no daemon error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>The request id that produced this error (0 when not applicable).</summary>
    public uint RequestId { get; }

    public RpcException(string message)
        : base(message) { }

    public RpcException(uint requestId, string errorCode)
        : base($"RPC error for request {requestId}: {errorCode}")
    {
        RequestId = requestId;
        ErrorCode = errorCode;
    }

    public RpcException(string message, Exception inner)
        : base(message, inner) { }
}
