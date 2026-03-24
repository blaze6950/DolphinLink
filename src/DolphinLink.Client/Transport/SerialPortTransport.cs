using System.Runtime.CompilerServices;
using System.Text;
using DolphinLink.Client.Abstractions;

namespace DolphinLink.Client.Transport;

/// <summary>
/// USB-CDC <see cref="ITransport"/> implementation that builds NDJSON
/// line framing on top of an <see cref="ISerialPort"/>.
///
/// <para>
/// The most common construction path is via the <c>portName</c> constructor, which
/// creates a <see cref="SystemSerialPort"/> backed by <see cref="System.IO.Ports.SerialPort"/>.
/// Alternatively, supply any <see cref="ISerialPort"/> implementation — for example
/// a WebSerial-backed port running in a browser WASM environment.
/// </para>
///
/// Threading contract (inherited from <see cref="ITransport"/>):
///   - <see cref="SendAsync"/> may be called from any thread; callers that need a
///     single-writer guarantee should wrap this in a
///     <see cref="PacketSerializationTransport"/>.
///   - <see cref="ReceiveAsync"/> must only be called from one reader at a time.
///   - <see cref="OpenAsync"/> must be called before any I/O.
///   - Closing is via cancelling the <see cref="ReceiveAsync"/> token and
///     <see cref="DisposeAsync"/>.
///
/// Windows note: <see cref="System.IO.Ports.SerialPort.BaseStream"/> ReadAsync
/// ignores CancellationTokens.  The only reliable way to unblock a pending read
/// is to close the port — which <see cref="ISerialPort.DisposeAsync"/> handles.
/// </summary>
public sealed class SerialPortTransport : ITransport
{
    private readonly ISerialPort _port;
    private readonly bool _ownsPort;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    /// <summary>
    /// Creates a transport backed by a <see cref="SystemSerialPort"/> for the
    /// given COM port name.
    /// </summary>
    /// <param name="portName">COM port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.</param>
    /// <param name="baudRate">
    /// Baud rate. For USB-CDC this is typically ignored by the OS but must be supplied.
    /// Defaults to 115200.
    /// </param>
    public SerialPortTransport(string portName, int baudRate = 115200)
        : this(new SystemSerialPort(portName, baudRate, dtrEnable: true), ownsPort: true)
    {
    }

    /// <summary>
    /// Creates a transport on top of an existing <see cref="ISerialPort"/>.
    /// Use this constructor to plug in a WebSerial-backed port or any other
    /// custom implementation.
    /// </summary>
    /// <param name="port">
    /// The serial port to use.  <see cref="ISerialPort.OpenAsync"/> must not
    /// have been called yet; <see cref="OpenAsync"/> will call it.
    /// </param>
    public SerialPortTransport(ISerialPort port)
        : this(port, ownsPort: false)
    {
    }

    private SerialPortTransport(ISerialPort port, bool ownsPort)
    {
        _port = port;
        _ownsPort = ownsPort;
    }

    /// <inheritdoc/>
    public async ValueTask OpenAsync(CancellationToken ct = default)
    {
        await _port.OpenAsync(ct).ConfigureAwait(false);

        var stream = _port.Stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An empty <paramref name="data"/> string sends a bare <c>\n</c> keep-alive frame.
    /// </remarks>
    public async ValueTask SendAsync(string data, CancellationToken ct = default)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Transport not open.");
        }

        await _writer.WriteLineAsync(data.AsMemory(), ct).ConfigureAwait(false);
        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("Transport not open.");
        }

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch
            {
                yield break; // EOF / port disconnect
            }

            if (line is null)
            {
                yield break; // EOF
            }

            yield return line;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            try
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
            catch { /* handle already closed — exceptions are expected here */ }
        }

        try { _reader?.Dispose(); }
        catch { /* handle already closed */ }

        // Only dispose the port when we own it (i.e. we created it).
        // When the caller supplied the ISerialPort, they are responsible for
        // its lifetime.
        if (_ownsPort)
        {
            await _port.DisposeAsync().ConfigureAwait(false);
        }
    }
}
