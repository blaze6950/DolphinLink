namespace DolphinLink.Tests.Infrastructure;

/// <summary>
/// Shared helpers for streaming tests.
///
/// These utilities abstract the common pattern of collecting N events from an
/// <see cref="IAsyncEnumerable{T}"/> with a wall-clock timeout, used in tests
/// that need to receive at least one event from an open RPC stream.
/// </summary>
public static class StreamTestHelper
{
    /// <summary>
    /// Collects up to <paramref name="maxItems"/> items from
    /// <paramref name="stream"/> or until <paramref name="timeout"/> elapses,
    /// whichever comes first.  Never throws on timeout — simply returns what
    /// was collected.
    /// </summary>
    /// <typeparam name="T">The stream event type.</typeparam>
    /// <param name="stream">The async enumerable to iterate.</param>
    /// <param name="maxItems">Stop after collecting this many items.</param>
    /// <param name="timeout">Wall-clock deadline; the method returns on expiry.</param>
    /// <returns>All items collected before the limit or timeout was reached.</returns>
    public static async Task<List<T>> CollectAsync<T>(
        IAsyncEnumerable<T> stream,
        int maxItems,
        TimeSpan timeout)
    {
        var items = new List<T>();
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            await foreach (var item in stream.WithCancellation(cts.Token).ConfigureAwait(false))
            {
                items.Add(item);
                if (items.Count >= maxItems)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { /* timeout — return what we have */ }

        return items;
    }
}
