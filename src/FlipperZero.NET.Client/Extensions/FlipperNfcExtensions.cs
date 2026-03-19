using FlipperZero.NET.Commands.Nfc;

namespace FlipperZero.NET.Extensions;

/// <summary>
/// Extension methods on <see cref="FlipperRpcClient"/> for NFC commands.
/// </summary>
public static class FlipperNfcExtensions
{
    /// <summary>
    /// Starts NFC protocol scanning on the Flipper.
    /// </summary>
    /// <remarks>
    /// Uses <c>NfcScanner</c> which detects protocol type only.
    /// No UID is available without a full anti-collision poller (<c>NfcPoller</c>).
    /// </remarks>
    /// <param name="client">The RPC client.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcStream{TEvent}"/> that yields <see cref="NfcScanEvent"/>
    /// for each detected NFC tag.  Dispose the stream to stop scanning and release the NFC hardware.
    /// </returns>
    public static Task<RpcStream<NfcScanEvent>> NfcScanStartAsync(
        this FlipperRpcClient client,
        CancellationToken ct = default)
        => client.SendStreamAsync<NfcScanStartCommand, NfcScanEvent>(new NfcScanStartCommand(), ct);
}
