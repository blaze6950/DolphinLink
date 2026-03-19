using FlipperZero.NET.Client.IntegrationTests.Infrastructure;
using FlipperZero.NET.Commands.Nfc;
using FlipperZero.NET.Extensions;

namespace FlipperZero.NET.Client.IntegrationTests.Nfc;

/// <summary>
/// Integration tests for NFC scanning via <see cref="FlipperRpcClient.NfcScanStartAsync"/>.
///
/// Run with a Flipper Zero connected:
///   set FLIPPER_PORT=COM3
///   dotnet test --filter "FullyQualifiedName~NfcScanTests"
/// </summary>
[Collection(FlipperCollection.Name)]
public sealed class NfcScanTests(FlipperFixture fixture)
{
    private FlipperRpcClient Client => fixture.Client;

    /// <summary>
    /// <see cref="FlipperRpcClient.NfcScanStartAsync"/> must return an
    /// <see cref="RpcStream{TEvent}"/> with a non-zero stream id.
    /// Validates: the stream-open handshake (Flipper responds with
    /// <c>{"id":N,"stream":M}</c>).
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcScanStart_ReturnsStreamWithNonZeroId()
    {
        await using var stream = await Client.NfcScanStartAsync();

        Assert.NotEqual(0u, stream.StreamId);
    }

    /// <summary>
    /// After opening an NFC scan stream we must be able to receive at least
    /// one <see cref="NfcScanEvent"/> when an NFC card is held near the
    /// Flipper's NFC antenna.
    /// Validates: stream event routing and <see cref="NfcScanEvent"/>
    /// deserialisation.
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcScanStart_ReceivesAtLeastOneEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var stream = await Client.NfcScanStartAsync(timeout.Token);

        NfcScanEvent? firstEvent = null;

        await foreach (var evt in stream.WithCancellation(timeout.Token))
        {
            firstEvent = evt;
            break; // We only need one
        }

        Assert.NotNull(firstEvent);
        Assert.NotEqual(NfcProtocol.Unknown, firstEvent.Value.Protocol);
    }

    /// <summary>
    /// Disposing the stream must automatically send <c>stream_close</c> and
    /// not throw. After dispose the NFC scanner is stopped and the NFC
    /// resource is released on the Flipper.
    /// Validates: <see cref="RpcStream{TEvent}.DisposeAsync"/> auto-close path.
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcScanStart_Dispose_ClosesStreamCleanly()
    {
        var stream = await Client.NfcScanStartAsync();

        // Should not throw
        await stream.DisposeAsync();
    }

    /// <summary>
    /// After disposing an NFC scan stream we must be able to open a new one.
    /// Validates: the NFC resource bitmask is actually cleared after
    /// <c>stream_close</c> so a second scan can acquire it.
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcScanStart_AfterDispose_CanStartAgain()
    {
        var first = await Client.NfcScanStartAsync();
        await first.DisposeAsync();

        // Give the Flipper a moment to release the resource
        await Task.Delay(200);

        await using var second = await Client.NfcScanStartAsync();

        Assert.NotEqual(0u, second.StreamId);
    }

    /// <summary>
    /// Attempting to open two concurrent NFC scan streams must cause the
    /// second to fail with a <see cref="FlipperRpcException"/> carrying the
    /// <c>resource_busy</c> error code.
    /// Validates: NFC resource-bitmask exclusivity enforcement in the daemon.
    /// </summary>
    [RequiresFlipperFact]
    public async Task NfcScanStart_WhenAlreadyActive_ThrowsResourceBusy()
    {
        await using var first = await Client.NfcScanStartAsync();

        var ex = await Assert.ThrowsAsync<FlipperRpcException>(
            () => Client.NfcScanStartAsync());

        Assert.Equal("resource_busy", ex.ErrorCode);
    }
}
