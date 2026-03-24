using DolphinLink.Client.Commands.Ui;
using DolphinLink.Client.Exceptions;
using DolphinLink.Client.Extensions;
using DolphinLink.Client.Commands.Input;

namespace DolphinLink.Client.UnitTests;

/// <summary>
/// Tests for <see cref="RpcClient.SendStreamAsync{TCommand,TEvent}"/>, stream
/// lifecycle, and <see cref="ScreenSession"/> using <see cref="FakeTransport"/>.
/// No hardware required.
/// </summary>
public sealed class StreamTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly RpcClient _client;

    public StreamTests()
    {
        _client = _transport.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _transport.EnqueueResponse(
            """{"t":0,"i":1,"p":{"n":"dolphin_link_rpc_daemon","v":5,"cmds":["ping","daemon_info","input_listen_start","stream_close","ui_screen_acquire","ui_screen_release","ui_draw_str","ui_draw_rect","ui_draw_line","ui_flush"]}}""");
        await _client.ConnectAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public async Task SendStreamAsync_ReturnsStream_WithCorrectStreamId()
    {
        // Arrange: daemon acknowledges stream open with stream id 42
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":42}}""");

        var stream = await _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        // Assert
        Assert.Equal(42u, stream.StreamId);

        // Clean up: enqueue close response before disposing
        _transport.EnqueueResponse("""{"t":0,"i":3}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task SendStreamAsync_SendsCorrectCommandLine()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":1}}""");

        var stream = await _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        // Assert: second line (after daemon_info) has the right command name
        var sent = _transport.SentLines;
        Assert.Equal(2, sent.Count);
        Assert.Contains("\"c\":39", sent[1]);
        Assert.Contains("\"i\":2", sent[1]);

        // Clean up
        _transport.EnqueueResponse("""{"t":0,"i":3}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task SendStreamAsync_DeliversEvents_ToAsyncEnumerable()
    {
        // Arrange: open the stream
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":7}}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        // Inject two unsolicited stream events using enum wire integers
        _transport.InjectEvent("""{"t":1,"i":7,"p":{"k":4,"ty":2}}""");
        _transport.InjectEvent("""{"t":1,"i":7,"p":{"k":5,"ty":3}}""");

        // Act: collect both events with a timeout
        var events = new List<InputListenEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var evt in stream.WithCancellation(cts.Token))
        {
            events.Add(evt);
            if (events.Count == 2)
            {
                break;
            }
        }

        // Assert: both events delivered with strongly-typed enum values
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e is { Key: InputKey.Ok, Type: InputType.Short });
        Assert.Contains(events, e => e is { Key: InputKey.Back, Type: InputType.Long });

        // Clean up
        _transport.EnqueueResponse("""{"t":0,"i":3}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_SendsStreamClose_WithCorrectStreamId()
    {
        // Arrange: open stream with id 99
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":99}}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        // Enqueue the response for stream_close (id 3)
        _transport.EnqueueResponse("""{"t":0,"i":3}""");

        // Act
        await stream.DisposeAsync();

        // Assert: daemon_info + input_listen_start + stream_close
        var sent = _transport.SentLines;
        Assert.Equal(3, sent.Count);
        var closeJson = sent[2];
        Assert.Contains("\"c\":1", closeJson);
        Assert.Contains("\"s\":99", closeJson);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2,"p":{"s":5}}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
            new InputListenStartCommand());

        _transport.EnqueueResponse("""{"t":0,"i":3}""");

        // Act: dispose twice — should not throw or send two stream_close commands
        await stream.DisposeAsync();
        await stream.DisposeAsync(); // second call should be a no-op

        // Assert: daemon_info + open + one close = 3 lines total
        Assert.Equal(3, _transport.SentLines.Count);
    }

    [Fact]
    public async Task SendStreamAsync_ThrowsRpcException_OnErrorResponse()
    {
        // Arrange: daemon refuses to open the stream
        _transport.EnqueueResponse("""{"t":0,"i":2,"e":"resource_busy"}""");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _client.SendStreamAsync<InputListenStartCommand, InputListenEvent>(
                new InputListenStartCommand()));

        Assert.Equal("resource_busy", ex.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // ScreenSession lifecycle tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UiScreenAcquireAsync_ReturnsSession()
    {
        // Arrange
        _transport.EnqueueResponse("""{"t":0,"i":2}""");

        // Act
        var session = await _client.UiScreenAcquireAsync();

        // Assert
        Assert.NotNull(session);

        // Clean up
        _transport.EnqueueResponse("""{"t":0,"i":3}""");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ScreenSession_DrawStr_SendsCorrectCommand()
    {
        // Acquire
        _transport.EnqueueResponse("""{"t":0,"i":2}""");
        await using var session = await _client.UiScreenAcquireAsync();

        // Draw
        _transport.EnqueueResponse("""{"t":0,"i":3}""");
        await session.DrawStrAsync(10, 32, "Hello");

        // Release (from DisposeAsync)
        _transport.EnqueueResponse("""{"t":0,"i":4}""");
        // DisposeAsync called by await using

        var sent = _transport.SentLines;
        // daemon_info, ui_screen_acquire, ui_draw_str — release sent on dispose
        Assert.True(sent.Count >= 3);
        Assert.Contains("\"c\":42", sent[2]);
    }

    [Fact]
    public async Task ScreenSession_DisposeAsync_SendsScreenRelease()
    {
        // Acquire
        _transport.EnqueueResponse("""{"t":0,"i":2}""");
        var session = await _client.UiScreenAcquireAsync();

        // Enqueue release response
        _transport.EnqueueResponse("""{"t":0,"i":3}""");

        // Act
        await session.DisposeAsync();

        // Assert: daemon_info + acquire + release = 3 lines
        var sent = _transport.SentLines;
        Assert.Equal(3, sent.Count);
        Assert.Contains("\"c\":41", sent[2]);
    }

    [Fact]
    public async Task ScreenSession_DisposeAsync_IsIdempotent()
    {
        // Acquire
        _transport.EnqueueResponse("""{"t":0,"i":2}""");
        var session = await _client.UiScreenAcquireAsync();

        // Only one release response needed — second dispose should be a no-op
        _transport.EnqueueResponse("""{"t":0,"i":3}""");

        // Act
        await session.DisposeAsync();
        await session.DisposeAsync(); // second call is no-op

        // Assert: daemon_info + acquire + release = 3 lines (not 4)
        Assert.Equal(3, _transport.SentLines.Count);
    }
}
