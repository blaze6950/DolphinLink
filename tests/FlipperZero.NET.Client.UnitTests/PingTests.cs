using FlipperZero.NET.Commands.Core;
using FlipperZero.NET.Exceptions;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests for basic request/response commands using <see cref="FakeTransport"/>.
/// No hardware required.
/// </summary>
public sealed class PingTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly FlipperRpcClient _client;

    public PingTests()
    {
        _client = _transport.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Enqueue daemon_info response for ConnectAsync negotiation (id:1)
        _transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"n":"flipper_zero_rpc_daemon","v":1,"cmds":["ping","daemon_info"]}}""");
        await _client.ConnectAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public async Task PingAsync_ReturnsPong_WhenDaemonRespondsOk()
    {
        // Arrange: enqueue the daemon response before (or shortly after) the send
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        var result = await _client.PingAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SendAsync_Serialises_CorrectJsonLine()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");

        // Act
        await _client.SendAsync<PingCommand, PingResponse>(new PingCommand());

        // Assert: the client sent two lines total (daemon_info + ping)
        var sent = _transport.SentLines;
        Assert.Equal(2, sent.Count);
        Assert.Contains("\"c\":0", sent[1]);
        Assert.Contains("\"i\":2", sent[1]);
    }

    [Fact]
    public async Task SendAsync_ThrowsFlipperRpcException_OnErrorResponse()
    {
        // Arrange: daemon returns an error
        _transport.EnqueueResponse("""{"t":0,"i":2,"e":"unknown_command"}""");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => _client.SendAsync<PingCommand, PingResponse>(new PingCommand()));

        Assert.Equal("unknown_command", ex.ErrorCode);
        Assert.Equal(2u, ex.RequestId);
    }

    [Fact]
    public async Task SendAsync_MultipleCommands_MatchCorrectResponses()
    {
        // Arrange: two commands in flight, responses arrive in order
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"pg":1}}""");
        _transport.EnqueueResponse("""{"t":0,"i":3,"p":{"pg":1}}""");

        // Act: send both
        var t1 = _client.SendAsync<PingCommand, PingResponse>(new PingCommand());
        var t2 = _client.SendAsync<PingCommand, PingResponse>(new PingCommand());

        var r1 = await t1;
        var r2 = await t2;

        // Assert
        Assert.True(r1.Pong);
        Assert.True(r2.Pong);
        // 3 total lines: daemon_info (from ConnectAsync) + two pings
        Assert.Equal(3, _transport.SentLines.Count);
    }
}
