using System.Text;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Bootstrapper.NativeRpc.Proto;

namespace FlipperZero.NET.Bootstrapper.NativeRpc;

/// <summary>
/// Low-level transport for the Flipper Zero native protobuf RPC (CDC interface 0).
///
/// Wire format: each message is preceded by a protobuf base-128 varint encoding the
/// serialized byte length of the following <see cref="Main"/> message, matching the
/// framing used by qFlipper and the official mobile apps.
///
/// CDC interface 0 is always available (it is the system RPC), regardless of whether
/// the custom NDJSON daemon FAP is installed or running.
///
/// <para>
/// <b>Session handshake (required before any protobuf I/O):</b>
/// When a host opens CDC interface 0, the Flipper starts its CLI shell and emits
/// an ASCII MOTD banner ending with the prompt <c>&gt;: </c>.  The host must then
/// send the ASCII command <c>start_rpc_session\r</c> to switch the port from CLI
/// mode to binary protobuf mode.  The Flipper echoes the command followed by
/// <c>\r\n</c> to confirm the transition.  Only after consuming that echo is the
/// port ready for varint-prefixed protobuf frames.
///
/// This is the exact sequence used by qFlipper (C++) and the official Python SDK.
/// Sending protobuf frames before the handshake is complete causes the CLI to
/// interpret the binary data as a garbage command and ignore it — the device
/// never sends a response and the caller hangs.
/// </para>
///
/// <para>
/// The underlying serial port is provided via <see cref="ISerialPort"/>, enabling
/// this class to run over <see cref="FlipperZero.NET.Transport.SystemSerialPort"/>
/// on desktop or a WebSerial-backed implementation in a browser WASM environment.
/// </para>
/// </summary>
internal sealed class NativeRpcTransport : IAsyncDisposable
{
    // After opening the serial port, the USB CDC endpoint needs a brief
    // settling period before it reliably accepts I/O.  Without this delay
    // the first ReadFile/WriteFile call returns ERROR_OPERATION_ABORTED (995).
    private const int OpenStabilizationDelayMs = 150;

    // DTR must be toggled low→high to force the Flipper to start a fresh CLI
    // session.  If DTR was already high (e.g. left over from a previous
    // connection), the Flipper will not re-emit the MOTD prompt.
    // qFlipper uses the same 50 ms inter-toggle delay.
    private const int DtrToggleDelayMs = 50;

    // Maximum time to wait for the CLI prompt or the session command echo.
    // On a USB-attached Flipper with no other load, the prompt arrives in
    // well under 1 s.  3 s gives generous headroom.
    private const int HandshakeReadTimeoutMs = 3000;

    // The Flipper CLI prompt that signals the shell is ready for commands.
    // qFlipper waits for exactly this byte sequence before sending start_rpc_session.
    private static readonly byte[] s_cliPrompt = ">: "u8.ToArray();

    // The ASCII CLI command that switches the Flipper from CLI mode to binary
    // protobuf mode.  Must end with \r (carriage return), NOT \r\n.
    // qFlipper: QByteArrayLiteral("start_rpc_session\r")
    // Python SDK: b"start_rpc_session\r"
    private static readonly byte[] s_rpcSessionCmd = "start_rpc_session\r"u8.ToArray();

    // Substring that appears in the Flipper's error reply when an RPC session
    // is already active (e.g. BLE or a previous USB session that wasn't closed).
    private const string SessionStartError = "Session start error";

    private readonly ISerialPort _port;
    private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

    // Per-call read buffer for varint decoding — avoids per-call allocation.
    private readonly byte[] _readByte = new byte[1];

    /// <summary>
    /// Creates a transport using the supplied <paramref name="port"/>.
    /// The port must not yet be open; <see cref="OpenAsync"/> will open it.
    /// </summary>
    internal NativeRpcTransport(ISerialPort port)
    {
        _port = port;
    }

    // Convenience property — Stream is only valid after OpenAsync.
    private Stream Stream => _port.Stream;

    /// <summary>
    /// Opens the serial port and performs the full CLI-to-RPC session handshake.
    ///
    /// <para>Sequence (matching qFlipper and the official Python SDK):</para>
    /// <list type="number">
    ///   <item>Open the port with DTR low.</item>
    ///   <item>Wait for USB CDC stabilization (<see cref="OpenStabilizationDelayMs"/> ms).</item>
    ///   <item>Assert DTR high — this triggers the Flipper CLI shell to start and
    ///     emit the MOTD banner.</item>
    ///   <item>Read bytes until the CLI prompt <c>&gt;: </c> appears.</item>
    ///   <item>Send <c>start_rpc_session\r</c> (ASCII CLI command).</item>
    ///   <item>Read bytes until <c>\n</c> (end of echo line).  Verify the echo
    ///     does not contain <c>"Session start error"</c>.</item>
    ///   <item>Restore <see cref="ISerialPort.ReadTimeout"/> to
    ///     <c>-1</c> (infinite) for subsequent protobuf I/O.</item>
    /// </list>
    ///
    /// After this method returns the port is in binary protobuf mode and
    /// <see cref="SendAsync"/> / <see cref="ReceiveAsync"/> may be used freely.
    /// </summary>
    internal async ValueTask OpenAsync(CancellationToken ct = default)
    {
        // Open with DTR low; we toggle it manually below.
        await _port.OpenAsync(ct).ConfigureAwait(false);
        await _port.SetDtrAsync(false, ct).ConfigureAwait(false);

        // Step 1 — USB CDC stabilization delay.
        // Let the Windows USB stack finish setting up the endpoint before we
        // toggle DTR.  Without this the DTR toggle races with the driver init.
        await Task.Delay(OpenStabilizationDelayMs, ct).ConfigureAwait(false);

        // Step 2 — Assert DTR high to trigger the Flipper CLI shell.
        // Asserting DTR now causes the Flipper's cli_vcp_cdc_ctrl_line_callback
        // to fire the CliVcpInternalEventConnected event, which starts the shell
        // thread, waits 100 ms for transient disconnects, and then emits the MOTD.
        await _port.SetDtrAsync(true, ct).ConfigureAwait(false);

        // Step 3 — Read until the CLI prompt ">: " appears.
        // The prompt is the last thing the Flipper sends before it waits for a
        // command.  We use ReadTimeout (not CancellationToken) because
        // SerialStream.ReadAsync on Windows ignores the token entirely.
        // WebSerial implementations should use ct-based deadlines instead.
        _port.ReadTimeout = HandshakeReadTimeoutMs;
        try
        {
            byte[] motd = await ReadUntilAsync(s_cliPrompt, ct).ConfigureAwait(false);
            _ = motd; // MOTD text is discarded; log it here if diagnostics are needed.
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for the Flipper CLI prompt (\">: \") after " +
                $"{HandshakeReadTimeoutMs} ms.  Ensure no other application " +
                $"(e.g. qFlipper, a terminal emulator) is holding the port open.", ex);
        }

        // Step 4 — Send "start_rpc_session\r" to switch to protobuf mode.
        // WriteAsync on Windows may surface ERROR_OPERATION_ABORTED (995) as
        // OperationCanceledException immediately after open.  WriteWithRetryAsync
        // retries those spurious aborts transparently.
        await WriteWithRetryAsync(s_rpcSessionCmd, ct).ConfigureAwait(false);
        await Stream.FlushAsync(ct).ConfigureAwait(false);

        // Step 5 — Read until end of the echo line (\n).
        // The Flipper CLI echoes the command back followed by \r\n.  Consuming
        // this echo is mandatory: any bytes left in the buffer would be
        // misinterpreted as the length prefix of the first protobuf frame.
        byte[] echo;
        try
        {
            echo = await ReadUntilAsync("\n"u8.ToArray(), ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for the Flipper to echo 'start_rpc_session' " +
                $"after {HandshakeReadTimeoutMs} ms.", ex);
        }

        // Step 6 — Check for "Session start error" in the echo.
        // This occurs when another RPC session is already active (e.g. BLE or a
        // previous USB session).  The Flipper prints the error instead of the
        // normal echo and stays in CLI mode.
        string echoText = Encoding.ASCII.GetString(echo);
        if (echoText.Contains(SessionStartError, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Flipper reported 'Session start error': another RPC session is already " +
                "active (e.g. Bluetooth, or qFlipper is running).  Close all other " +
                "connections to the Flipper and retry.");
        }

        // Step 7 — Restore infinite read timeout for protobuf I/O.
        // The protobuf reader loop has no inherent timeout; it blocks until
        // the device responds.  The outer CancellationToken (from BootstrapAsync)
        // provides the overall deadline via transport disposal.
        _port.ReadTimeout = -1; // SerialPort.InfiniteTimeout == -1
    }

    // -------------------------------------------------------------------------
    // Handshake helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads bytes from the stream one at a time until <paramref name="marker"/>
    /// appears at the tail of the accumulated data, then returns all accumulated
    /// bytes (including the marker).
    ///
    /// <para>
    /// Uses <see cref="ISerialPort.ReadTimeout"/> (already set by the caller) to
    /// enforce a deadline on desktop (where <c>SerialStream.ReadAsync</c> respects
    /// the port-level timeout and throws <see cref="TimeoutException"/>).
    /// WebSerial implementations should rely on <paramref name="ct"/> instead.
    /// </para>
    /// </summary>
    private async ValueTask<byte[]> ReadUntilAsync(byte[] marker, CancellationToken ct)
    {
        var buf = new List<byte>(256);
        var oneByte = new byte[1];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            int read = await Stream.ReadAsync(oneByte, ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Port closed during handshake.");
            }

            buf.Add(oneByte[0]);

            // Check if the tail of buf matches marker.
            if (buf.Count >= marker.Length)
            {
                bool match = true;
                int offset = buf.Count - marker.Length;
                for (int i = 0; i < marker.Length; i++)
                {
                    if (buf[offset + i] != marker[i]) { match = false; break; }
                }
                if (match)
                {
                    return [.. buf];
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="message"/> as a varint-prefixed protobuf frame
    /// and writes it to the serial stream.
    /// </summary>
    internal async ValueTask SendAsync(Main message, CancellationToken ct = default)
    {
        // Serialize the message body.
        byte[] body = message.ToByteArray();

        // Build the varint length prefix into a local stack buffer — avoids
        // a shared instance field that would not be thread-safe.
        byte[] varintBuf = new byte[5];
        int prefixLen = WriteVarint32ToBuffer(varintBuf, (uint)body.Length);

        await WriteWithRetryAsync(varintBuf.AsMemory(0, prefixLen), ct).ConfigureAwait(false);
        await WriteWithRetryAsync(body, ct).ConfigureAwait(false);
        await Stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes <paramref name="buffer"/> to the stream, retrying up to 4 times on
    /// <see cref="OperationCanceledException"/> caused by Win32
    /// <c>ERROR_OPERATION_ABORTED</c> (error 995).
    ///
    /// On Windows, USB CDC devices can briefly reject I/O immediately after
    /// <see cref="OpenAsync"/> is called — before the endpoint is fully ready.
    /// The .NET runtime maps Win32 error 995 to
    /// <see cref="OperationCanceledException"/>/<see cref="TaskCanceledException"/>,
    /// which is indistinguishable from a real cancellation without checking
    /// <see cref="CancellationToken.IsCancellationRequested"/>.
    ///
    /// The guard <c>when (!ct.IsCancellationRequested)</c> separates genuine
    /// user cancellation (rethrown immediately) from spurious device-not-ready
    /// aborts (retried with a short back-off).
    /// </summary>
    private async ValueTask WriteWithRetryAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        const int MaxAttempts = 4;
        const int RetryDelayMs = 50;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await Stream.WriteAsync(buffer, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < MaxAttempts - 1)
            {
                await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads one varint-prefixed protobuf frame from the serial stream and
    /// deserializes it as a <see cref="Main"/> message.
    /// </summary>
    internal async ValueTask<Main> ReceiveAsync(CancellationToken ct = default)
    {
        uint length = await ReadVarint32Async(ct).ConfigureAwait(false);

        if (length == 0)
        {
            // Zero-length frame — return an empty Main (can happen as keep-alive).
            return new Main();
        }

        byte[] body = new byte[length];
        await ReadExactAsync(body, ct).ConfigureAwait(false);

        return Main.Parser.ParseFrom(body);
    }

    // -------------------------------------------------------------------------
    // Varint helpers
    // -------------------------------------------------------------------------

    /// <summary>Encodes <paramref name="value"/> as a base-128 varint into <paramref name="buf"/>.</summary>
    /// <returns>Number of bytes written.</returns>
    private static int WriteVarint32ToBuffer(byte[] buf, uint value)
    {
        int i = 0;
        while (value > 0x7F)
        {
            buf[i++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }
        buf[i++] = (byte)value;
        return i;
    }

    /// <summary>
    /// Reads a base-128 varint from the stream one byte at a time.
    /// Supports up to 32-bit values (5 bytes maximum).
    /// </summary>
    private async ValueTask<uint> ReadVarint32Async(CancellationToken ct)
    {
        uint result = 0;
        int shift = 0;

        while (shift < 35)
        {
            // Reuse the class-level single-byte buffer to avoid per-call allocation.
            await ReadExactAsync(_readByte, ct).ConfigureAwait(false);
            byte b = _readByte[0];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("Varint too long (> 5 bytes).");
    }

    /// <summary>Reads exactly <c>buffer.Length</c> bytes from the stream.</summary>
    private async ValueTask ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int offset    = 0;
        int remaining = buffer.Length;
        while (remaining > 0)
        {
            int read = await Stream.ReadAsync(buffer.AsMemory(offset, remaining), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Connection closed while reading protobuf frame.");
            }

            offset    += read;
            remaining -= read;
        }
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Dispose the ISerialPort — SystemSerialPort.DisposeAsync handles the
        // Windows SafeFileHandle force-close to unblock in-flight async I/O.
        // Other ISerialPort implementations (e.g. WebSerial) handle it their own way.
        await _port.DisposeAsync().ConfigureAwait(false);
    }
}
