using System.Threading.Channels;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcStreamManager"/>.
/// No transport or hardware required.
/// </summary>
public sealed class StreamManagerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (StreamState state, Channel<JsonElement> channel) MakeStream(
        out List<Exception?> completions)
    {
        var log = new List<Exception?>();
        completions = log;

        var ch = Channel.CreateUnbounded<JsonElement>();
        var state = new StreamState
        {
            EventChannel = ch,
            Complete = () => { ch.Writer.TryComplete(); log.Add(null); },
            Fault = ex => { ch.Writer.TryComplete(ex); log.Add(ex); },
        };
        return (state, ch);
    }

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
        var (state, ch) = MakeStream(out _);
        sut.Register(1, state);

        var element = ParseElement("""{"value":42}""");
        var result = sut.TryRouteEvent(1, element);

        Assert.True(result);
        // Give the write a moment if it went async
        await Task.Delay(10);
        Assert.True(ch.Reader.TryRead(out var received));
        Assert.Equal(42, received.GetProperty("value").GetInt32());
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
        var (state, ch) = MakeStream(out var completions);
        sut.Register(1, state);

        var result = sut.TryRemoveAndComplete(1);

        Assert.True(result);
        Assert.Single(completions);
        Assert.Null(completions[0]); // Complete() was called, not Fault()
        Assert.True(ch.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void TryRemoveAndComplete_ReturnsFalse_AfterAlreadyRemoved()
    {
        var sut = new RpcStreamManager();
        var (state, _) = MakeStream(out _);
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
        var (s1, _) = MakeStream(out var completions1);
        var (s2, _) = MakeStream(out var completions2);
        sut.Register(1, s1);
        sut.Register(2, s2);

        var ex = new Exception("test fault");
        sut.FaultAll(ex);

        Assert.Single(completions1);
        Assert.Same(ex, completions1[0]);
        Assert.Single(completions2);
        Assert.Same(ex, completions2[0]);
    }

    [Fact]
    public void FaultAll_RemovesAllStreams_SoSubsequentTryRouteReturnsFalse()
    {
        var sut = new RpcStreamManager();
        var (state, _) = MakeStream(out _);
        sut.Register(1, state);

        sut.FaultAll(new Exception("gone"));

        Assert.False(sut.TryRouteEvent(1, ParseElement("{}")));
    }
}
