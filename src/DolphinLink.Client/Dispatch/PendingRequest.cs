using DolphinLink.Client.Abstractions;

namespace DolphinLink.Client.Dispatch;

/// <summary>
/// Typed implementation of <see cref="IPendingRequest"/> backed by a
/// <see cref="TaskCompletionSource{TResponse}"/>.
///
/// No boxing: <typeparamref name="TResponse"/> is a struct constraint.
/// The TCS is owned here; the caller receives only the <see cref="Task"/>.
/// </summary>
internal sealed class PendingRequest<TResponse> : IPendingRequest
    where TResponse : struct, IRpcCommandResponse
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Default;

    private readonly TaskCompletionSource<TResponse> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public long SentTimestamp { get; set; }

    /// <inheritdoc/>
    public string? CommandName { get; set; }

    /// <summary>The task that resolves when the response arrives or the request fails.</summary>
    public Task<TResponse> Task => _tcs.Task;

    /// <inheritdoc/>
    /// <remarks>
    /// If <paramref name="payload"/> is <see cref="JsonValueKind.Undefined"/> (absent field),
    /// the result is <c>default(TResponse)</c> — correct for void-response commands.
    /// Otherwise, <typeparamref name="TResponse"/> is deserialized directly from the payload.
    /// </remarks>
    public void Complete(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            _tcs.TrySetResult(default);
            return;
        }

        var result = JsonSerializer.Deserialize<TResponse>(payload.GetRawText(), JsonOptions);
        _tcs.TrySetResult(result);
    }

    /// <inheritdoc/>
    public void Fail(Exception ex) => _tcs.TrySetException(ex);
}
