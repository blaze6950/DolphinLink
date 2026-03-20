using FlipperZero.NET.Commands.Input;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Tests for <see cref="FlipperRpcClient.SendStreamAsync{TCommand,TEvent}"/>, stream
/// lifecycle, and <see cref="FlipperScreenSession"/> using <see cref="FakeTransport"/>.
/// No hardware required.
/// </summary>
public sealed class StreamTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly FlipperRpcClient _client;

    public StreamTests()
    {
        _client = _transport.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _transport.EnqueueResponse(
            """{"id":1,"status":"ok","data":{"name":"flipper_zero_rpc_daemon","version":1,"commands":["ping","daemon_info","input_listen_start","stream_close","ui_screen_acquire","ui_screen_release","ui_draw_str","ui_draw_rect","ui_draw_line","ui_flush"]}}""");
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
        _transport.EnqueueResponse("""{"id":2,"stream":42}""");

        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Assert
        Assert.Equal(42u, stream.StreamId);

        // Clean up: enqueue close response before disposing
        _transport.EnqueueResponse("""{"id":3,"status":"ok"}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task SendStreamAsync_SendsCorrectCommandLine()
    {
        // Arrange
        _transport.EnqueueResponse("""{"id":2,"stream":1}""");

        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Assert: second line (after daemon_info) has the right command name
        var sent = _transport.SentLines;
        Assert.Equal(2, sent.Count);
        Assert.Contains("\"cmd\":\"input_listen_start\"", sent[1]);
        Assert.Contains("\"id\":2", sent[1]);

        // Clean up
        _transport.EnqueueResponse("""{"id":3,"status":"ok"}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task SendStreamAsync_DeliversEvents_ToAsyncEnumerable()
    {
        // Arrange: open the stream
        _transport.EnqueueResponse("""{"id":2,"stream":7}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Inject two unsolicited stream events using enum wire strings
        _transport.InjectEvent("""{"event":{"key":"ok","type":"short"},"stream":7}""");
        _transport.InjectEvent("""{"event":{"key":"back","type":"long"},"stream":7}""");

        // Act: collect both events with a timeout
        var events = new List<FlipperInputEvent>();
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
        Assert.Contains(events, e => e is { Key: FlipperInputKey.Ok, Type: FlipperInputType.Short });
        Assert.Contains(events, e => e is { Key: FlipperInputKey.Back, Type: FlipperInputType.Long });

        // Clean up
        _transport.EnqueueResponse("""{"id":3,"status":"ok"}""");
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_SendsStreamClose_WithCorrectStreamId()
    {
        // Arrange: open stream with id 99
        _transport.EnqueueResponse("""{"id":2,"stream":99}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        // Enqueue the response for stream_close (id 3)
        _transport.EnqueueResponse("""{"id":3,"status":"ok"}""");

        // Act
        await stream.DisposeAsync();

        // Assert: daemon_info + input_listen_start + stream_close
        var sent = _transport.SentLines;
        Assert.Equal(3, sent.Count);
        var closeJson = sent[2];
        Assert.Contains("\"cmd\":\"stream_close\"", closeJson);
        Assert.Contains("\"stream\":99", closeJson);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        _transport.EnqueueResponse("""{"id":2,"stream":5}""");
        var stream = await _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
            new InputListenStartCommand());

        _transport.EnqueueResponse("""{"id":3,"status":"ok"}""");

        // Act: dispose twice — should not throw or send two stream_close commands
        await stream.DisposeAsync();
        await stream.DisposeAsync(); // second call should be a no-op

        // Assert: daemon_info + open + one close = 3 lines total
        Assert.Equal(3, _transport.SentLines.Count);
    }

    [Fact]
    public async Task SendStreamAsync_ThrowsFlipperRpcException_OnErrorResponse()
    {
        // Arrange: daemon refuses to open the stream
        _transport.EnqueueResponse("""{"id":2,"error":"resource_busy"}""");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => _client.SendStreamAsync<InputListenStartCommand, FlipperInputEvent>(
                new InputListenStartCommand()));

        Assert.Equal("resource_busy", ex.ErrorCode);
    }

    // -------------------------------------------------------------------------
    // FlipperScreenSession lifecycle tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UiScreenAcquireAsync_ReturnsSession()
    {
        // Arrange
        _transport.EnqueueResponse("""{"id":2,"status":"ok","data":{}}""");

        // Act
        var session = await _client.UiScreenAcquireAsync();

        // Assert
        Assert.NotNull(session);

        // Clean up
        _transport.EnqueueResponse("""{"id":3,"status":"ok","data":{}}""");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task FlipperScreenSession_DrawStr_SendsCorrectCommand()
    {
        // Acquire
        _transport.EnqueueResponse("""{"id":2,"status":"ok","data":{}}""");
        await using var session = await _client.UiScreenAcquireAsync();

        // Draw
        _transport.EnqueueResponse("""{"id":3,"status":"ok","data":{}}""");
        await session.DrawStrAsync(10, 32, "Hello");

        // Release (from DisposeAsync)
        _transport.EnqueueResponse("""{"id":4,"status":"ok","data":{}}""");
        // DisposeAsync called by await using

        var sent = _transport.SentLines;
        // daemon_info, ui_screen_acquire, ui_draw_str — release sent on dispose
        Assert.True(sent.Count >= 3);
        Assert.Contains("\"cmd\":\"ui_draw_str\"", sent[2]);
    }

    [Fact]
    public async Task FlipperScreenSession_DisposeAsync_SendsScreenRelease()
    {
        // Acquire
        _transport.EnqueueResponse("""{"id":2,"status":"ok","data":{}}""");
        var session = await _client.UiScreenAcquireAsync();

        // Enqueue release response
        _transport.EnqueueResponse("""{"id":3,"status":"ok","data":{}}""");

        // Act
        await session.DisposeAsync();

        // Assert: daemon_info + acquire + release = 3 lines
        var sent = _transport.SentLines;
        Assert.Equal(3, sent.Count);
        Assert.Contains("\"cmd\":\"ui_screen_release\"", sent[2]);
    }

    [Fact]
    public async Task FlipperScreenSession_DisposeAsync_IsIdempotent()
    {
        // Acquire
        _transport.EnqueueResponse("""{"id":2,"status":"ok","data":{}}""");
        var session = await _client.UiScreenAcquireAsync();

        // Only one release response needed — second dispose should be a no-op
        _transport.EnqueueResponse("""{"id":3,"status":"ok","data":{}}""");

        // Act
        await session.DisposeAsync();
        await session.DisposeAsync(); // second call is no-op

        // Assert: daemon_info + acquire + release = 3 lines (not 4)
        Assert.Equal(3, _transport.SentLines.Count);
    }
}
