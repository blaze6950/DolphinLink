using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Dispatch;

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

    /// <summary>
    /// Minimal <see cref="IRpcCommandResponse"/> for use as the generic parameter
    /// in <see cref="PendingRequest{TResponse}"/> within these tests.
    /// </summary>
    private readonly struct TestResponse : IRpcCommandResponse { }

    private static PendingRequest<TestResponse> MakeRequest()
        => new PendingRequest<TestResponse>();

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
        var req = MakeRequest();
        sut.Register(1, req);

        var found = sut.TryRemove(1, out var removed);

        Assert.True(found);
        Assert.Same(req, removed);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_AfterAlreadyRemoved()
    {
        var sut = new RpcPendingRequests();
        sut.Register(1, MakeRequest());
        sut.TryRemove(1, out _);

        var found = sut.TryRemove(1, out _);

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // StampSentTimestamp
    // -------------------------------------------------------------------------

    [Fact]
    public void StampSentTimestamp_UpdatesExistingRequest()
    {
        var sut = new RpcPendingRequests();
        var req = MakeRequest();
        sut.Register(7, req);

        sut.StampSentTimestamp(7, 12345L);

        Assert.Equal(12345L, req.SentTimestamp);
    }

    [Fact]
    public void StampSentTimestamp_IsNoOp_WhenIdNotFound()
    {
        var sut = new RpcPendingRequests();

        // Should not throw
        sut.StampSentTimestamp(999, 12345L);
    }

    // -------------------------------------------------------------------------
    // FailAll
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FailAll_FailsTask_ForEveryRegisteredRequest()
    {
        var sut = new RpcPendingRequests();
        var req1 = MakeRequest();
        var req2 = MakeRequest();

        sut.Register(1, req1);
        sut.Register(2, req2);

        var ex = new Exception("boom");
        sut.FailAll(ex);

        await Assert.ThrowsAsync<Exception>(() => req1.Task);
        await Assert.ThrowsAsync<Exception>(() => req2.Task);
    }

    [Fact]
    public void FailAll_RemovesAllEntries_SoSubsequentTryRemoveFails()
    {
        var sut = new RpcPendingRequests();
        sut.Register(1, MakeRequest());
        sut.Register(2, MakeRequest());

        sut.FailAll(new Exception("gone"));

        Assert.False(sut.TryRemove(1, out _));
        Assert.False(sut.TryRemove(2, out _));
    }
}
