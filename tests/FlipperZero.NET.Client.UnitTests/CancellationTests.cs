using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Commands.Input;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests for cancellation-token behaviour in <see cref="FlipperRpcClient"/>.
/// No hardware required.
/// </summary>
public sealed class CancellationTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly FlipperRpcClient _client;

    public CancellationTests()
    {
        _client = _transport.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _transport.EnqueueResponse(
            """{"type":"response","id":1,"payload":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping","daemon_info"]}}""");
        await _client.ConnectAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_ThrowsOperationCanceledException_WhenTokenAlreadyCancelled()
    {
        // Arrange: a pre-cancelled token
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand(), cts.Token));
    }

    [Fact]
    public async Task SendStreamAsync_ThrowsOperationCanceledException_WhenTokenAlreadyCancelled()
    {
        // Arrange: a pre-cancelled token
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
                new InputListenStartCommand(), cts.Token));
    }

    [Fact]
    public async Task SendAsync_ThrowsOperationCanceledException_WhenCancelledWhileWaiting()
    {
        // Arrange: token that cancels after a short delay (no response queued so it will hang)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert: no response enqueued — SendAsync blocks until cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand(), cts.Token));
    }
}
