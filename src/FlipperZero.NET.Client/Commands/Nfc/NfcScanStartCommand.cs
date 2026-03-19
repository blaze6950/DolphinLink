using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.Nfc;

/// <summary>
/// Opens an NFC scanner stream.  Each detected NFC tag protocol is delivered
/// as an <see cref="NfcScanEvent"/>.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"nfc_scan_start"}</code>
///
/// Wire format (stream open response):
/// <code>{"id":N,"stream":M}</code>
///
/// Wire format (stream event):
/// <code>{"event":{"protocol":"Iso14443-3a"},"stream":M}</code>
///
/// Wire format (stream close request):
/// <code>{"id":N,"cmd":"stream_close","stream":M}</code>
///
/// Uses <c>NfcScanner</c> internally which performs protocol detection only —
/// no UID is available without running a full anti-collision poller.
/// Requires the NFC hardware resource.  Dispose the returned
/// <see cref="RpcStream{TEvent}"/> to stop scanning and release the NFC hardware.
/// </summary>
public readonly struct NfcScanStartCommand : IRpcStreamCommand<NfcScanEvent>
{
    /// <inheritdoc />
    public string CommandName => "nfc_scan_start";

    /// <summary>No arguments needed.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>An NFC tag protocol detection event.</summary>
public readonly struct NfcScanEvent : IRpcCommandResponse
{
    /// <summary>Detected NFC protocol.</summary>
    [JsonPropertyName("protocol")]
    [JsonConverter(typeof(NfcProtocolJsonConverter))]
    public NfcProtocol Protocol { get; init; }
}
