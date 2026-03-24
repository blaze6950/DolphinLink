namespace DolphinLink.Client.Abstractions;

/// <summary>
/// Receives diagnostic log entries from the RPC infrastructure.
///
/// Implement this interface to integrate with a structured logging framework
/// (e.g. Microsoft.Extensions.Logging, Serilog, etc.).  Implementations must
/// be synchronous, non-blocking, and must not throw.
///
/// Pass your implementation to the <see cref="RpcClient"/> constructor.
/// </summary>
public interface IRpcDiagnostics
{
    void Log(RpcLogEntry entry);
}
