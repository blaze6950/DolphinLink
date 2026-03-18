using System.IO.Ports;
using System.Text;

namespace FlipperZero.NET;

/// <summary>
/// Wraps a <see cref="SerialPort"/> for the Flipper USB-CDC connection.
/// Provides line-oriented send/receive over NDJSON framing.
///
/// Thread-safety contract:
///   - <see cref="SendLineAsync"/> is called exclusively by the writer loop (single writer).
///   - <see cref="ReadLineAsync"/> is called exclusively by the reader loop (single reader).
///   - Open / close happen outside both loops.
/// </summary>
internal sealed class FlipperRpcTransport : IAsyncDisposable
{
    private readonly SerialPort _port;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    /// <param name="portName">COM port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.</param>
    /// <param name="baudRate">
    /// Baud rate. For USB-CDC this is typically ignored by the OS but must be supplied.
    /// Defaults to 115200.
    /// </param>
    public FlipperRpcTransport(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            Encoding = Encoding.UTF8,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            DtrEnable = true, // Required by some CDC implementations to signal ready
        };
    }

    /// <summary>Opens the serial port and sets up the reader / writer streams.</summary>
    public void Open()
    {
        _port.Open();
        // Use the underlying BaseStream for async I/O; SerialPort itself is sync-only.
        _writer = new StreamWriter(_port.BaseStream, Encoding.UTF8, leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        _reader = new StreamReader(_port.BaseStream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>
    /// Writes a single JSON line followed by <c>\n</c> and flushes the buffer.
    /// Must only be called from the writer loop.
    /// </summary>
    public async Task SendLineAsync(string json, CancellationToken ct)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Transport not open.");
        }

        await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads until the next <c>\n</c> and returns the trimmed line.
    /// Returns <c>null</c> when the stream ends (port closed / disconnected).
    /// Must only be called from the reader loop.
    /// </summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("Transport not open.");
        }

        try
        {
            return await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null; // EOF / port disconnect
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
        }

        _reader?.Dispose();
        _port.Dispose();
    }
}