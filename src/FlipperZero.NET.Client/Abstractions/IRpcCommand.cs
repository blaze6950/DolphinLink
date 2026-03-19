namespace FlipperZero.NET.Abstractions;

/// <summary>
/// A fire-and-response RPC command.
/// Implementations must be <c>readonly struct</c> — no boxing occurs when passed as a
/// generic type parameter to
/// <see cref="FlipperRpcClient.SendAsync{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">
/// The strongly-typed response produced by the Flipper for this command.
/// </typeparam>
public interface IRpcCommand<TResponse> : IRpcCommandBase
    where TResponse : struct, IRpcCommandResponse
{
}
