using FlipperZero.NET;

namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="FlipperRpcClient.PingAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~PingTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class PingTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// A single ping must round-trip and return <c>true</c>.
    /// Validates: JSON serialisation, request-id routing, response
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Ping_ReturnsTrue()
    {
        var result = await Client.PingAsync();

        Assert.True(result);
    }

    /// <summary>
    /// Fire several pings sequentially and verify each returns <c>true</c>.
    /// Validates: the outbound channel and id counter stay consistent across
    /// multiple round-trips.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Ping_MultipleTimes_AllReturnTrue()
    {
        const int count = 5;

        for (var i = 0; i < count; i++)
        {
            var result = await Client.PingAsync();
            Assert.True(result, $"Ping {i + 1}/{count} failed");
        }
    }

    /// <summary>
    /// Fire several pings concurrently and verify all return <c>true</c>.
    /// Validates: concurrent request routing — each response lands on the
    /// correct <c>TaskCompletionSource</c> by request-id.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Ping_ConcurrentRequests_AllReturnTrue()
    {
        const int count = 4;

        var tasks = Enumerable
            .Range(0, count)
            .Select(_ => Client.PingAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r));
    }

    /// <summary>
    /// A ping with an already-cancelled token must throw
    /// <see cref="OperationCanceledException"/> without sending anything.
    /// </summary>
    [RequiresFlipperFact]
    public async Task Ping_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client.PingAsync(cts.Token));
    }
}
