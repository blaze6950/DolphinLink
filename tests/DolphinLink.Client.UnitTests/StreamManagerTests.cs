using DolphinLink.Client.Streaming;

namespace DolphinLink.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcStreamManager"/>.
/// No transport or hardware required.
/// </summary>
public sealed class StreamManagerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // -------------------------------------------------------------------------
    // TryRouteEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void TryRouteEvent_ReturnsFalse_WhenStreamNotRegistered()
    {
        var sut = new RpcStreamManager();

        var result = sut.TryRouteEvent(99, ParseElement("{}"));

        Assert.False(result);
    }

    [Fact]
    public async Task TryRouteEvent_WritesEventToChannel_WhenStreamRegistered()
    {
        var sut = new RpcStreamManager();
        var state = new StreamState();
        sut.Register(1, state);

        var element = ParseElement("""{"value":42}""");
        var result = sut.TryRouteEvent(1, element);

        Assert.True(result);
        // TryWrite on an unbounded channel always succeeds synchronously
        Assert.True(state.Reader.TryRead(out var received));
        Assert.Equal(42, received.GetProperty("value").GetInt32());

        await Task.CompletedTask; // satisfy async signature
    }

    // -------------------------------------------------------------------------
    // TryRemoveAndComplete
    // -------------------------------------------------------------------------

    [Fact]
    public void TryRemoveAndComplete_ReturnsFalse_WhenStreamNotRegistered()
    {
        var sut = new RpcStreamManager();

        var result = sut.TryRemoveAndComplete(99);

        Assert.False(result);
    }

    [Fact]
    public void TryRemoveAndComplete_ReturnsTrue_AndCompletesChannel()
    {
        var sut = new RpcStreamManager();
        var state = new StreamState();
        sut.Register(1, state);

        var result = sut.TryRemoveAndComplete(1);

        Assert.True(result);
        Assert.True(state.Reader.Completion.IsCompleted);
        Assert.Null(state.Reader.Completion.Exception); // normal completion, no fault
    }

    [Fact]
    public void TryRemoveAndComplete_ReturnsFalse_AfterAlreadyRemoved()
    {
        var sut = new RpcStreamManager();
        var state = new StreamState();
        sut.Register(1, state);
        sut.TryRemoveAndComplete(1);

        var result = sut.TryRemoveAndComplete(1);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // FaultAll
    // -------------------------------------------------------------------------

    [Fact]
    public void FaultAll_FaultsEveryRegisteredStream()
    {
        var sut = new RpcStreamManager();
        var s1 = new StreamState();
        var s2 = new StreamState();
        sut.Register(1, s1);
        sut.Register(2, s2);

        var ex = new Exception("test fault");
        sut.FaultAll(ex);

        Assert.True(s1.Reader.Completion.IsCompleted);
        Assert.True(s2.Reader.Completion.IsCompleted);
        // Both channels should be faulted (exception present on completion task)
        Assert.NotNull(s1.Reader.Completion.Exception);
        Assert.NotNull(s2.Reader.Completion.Exception);
    }

    [Fact]
    public void FaultAll_RemovesAllStreams_SoSubsequentTryRouteReturnsFalse()
    {
        var sut = new RpcStreamManager();
        var state = new StreamState();
        sut.Register(1, state);

        sut.FaultAll(new Exception("gone"));

        Assert.False(sut.TryRouteEvent(1, ParseElement("{}")));
    }
}
