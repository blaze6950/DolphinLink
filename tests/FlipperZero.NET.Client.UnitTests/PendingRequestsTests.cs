using System.Threading.Channels;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcPendingRequests"/>.
/// No transport or hardware required.
/// </summary>
public sealed class PendingRequestsTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PendingRequest MakeRequest(
        out List<JsonElement> successes,
        out List<string> errors)
    {
        var s = new List<JsonElement>();
        var e = new List<string>();
        successes = s;
        errors = e;

        return new PendingRequest
        {
            OnSuccess = el => s.Add(el),
            OnError = code => e.Add(code),
        };
    }

    // -------------------------------------------------------------------------
    // Register / TryRemove
    // -------------------------------------------------------------------------

    [Fact]
    public void TryRemove_ReturnsFalse_WhenNotRegistered()
    {
        var sut = new RpcPendingRequests();

        var found = sut.TryRemove(99, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryRemove_ReturnsRegisteredRequest()
    {
        var sut = new RpcPendingRequests();
        var req = MakeRequest(out _, out _);
        sut.Register(1, req);

        var found = sut.TryRemove(1, out var removed);

        Assert.True(found);
        Assert.Same(req, removed);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_AfterAlreadyRemoved()
    {
        var sut = new RpcPendingRequests();
        sut.Register(1, MakeRequest(out _, out _));
        sut.TryRemove(1, out _);

        var found = sut.TryRemove(1, out _);

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // StampSentTicks
    // -------------------------------------------------------------------------

    [Fact]
    public void StampSentTicks_UpdatesExistingRequest()
    {
        var sut = new RpcPendingRequests();
        var req = MakeRequest(out _, out _);
        sut.Register(7, req);

        sut.StampSentTicks(7, 12345L);

        Assert.Equal(12345L, req.SentTicks);
    }

    [Fact]
    public void StampSentTicks_IsNoOp_WhenIdNotFound()
    {
        var sut = new RpcPendingRequests();

        // Should not throw
        sut.StampSentTicks(999, 12345L);
    }

    // -------------------------------------------------------------------------
    // FailAll
    // -------------------------------------------------------------------------

    [Fact]
    public void FailAll_InvokesOnError_ForEveryRegisteredRequest()
    {
        var sut = new RpcPendingRequests();
        var errors1 = new List<string>();
        var errors2 = new List<string>();

        sut.Register(1, new PendingRequest { OnSuccess = _ => { }, OnError = e => errors1.Add(e) });
        sut.Register(2, new PendingRequest { OnSuccess = _ => { }, OnError = e => errors2.Add(e) });

        sut.FailAll("boom");

        Assert.Equal(new[] { "boom" }, errors1);
        Assert.Equal(new[] { "boom" }, errors2);
    }

    [Fact]
    public void FailAll_RemovesAllEntries_SoSubsequentTryRemoveFails()
    {
        var sut = new RpcPendingRequests();
        sut.Register(1, MakeRequest(out _, out _));
        sut.Register(2, MakeRequest(out _, out _));

        sut.FailAll("gone");

        Assert.False(sut.TryRemove(1, out _));
        Assert.False(sut.TryRemove(2, out _));
    }

    // -------------------------------------------------------------------------
    // FailOrphans
    // -------------------------------------------------------------------------

    [Fact]
    public void FailOrphans_FailsUnsentWorkItems_InOutboundChannel()
    {
        var sut = new RpcPendingRequests();
        var errors = new List<string>();

        var channel = Channel.CreateUnbounded<RpcWorkItem>();

        // Write a work item that, when Register()-ed, adds to sut
        channel.Writer.TryWrite(new RpcWorkItem
        {
            RequestId = 10,
            Json = "{}",
            CommandName = "test",
            Register = () => sut.Register(10, new PendingRequest
            {
                OnSuccess = _ => { },
                OnError = e => errors.Add(e),
            }),
        });
        channel.Writer.Complete();

        sut.FailOrphans(channel, "orphan_error");

        Assert.Equal(new[] { "orphan_error" }, errors);
    }
}
