using System.IO.Ports;
using System.Text;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET;

/// <summary>
/// USB-CDC <see cref="IFlipperTransport"/> implementation backed by <see cref="SerialPort"/>.
///
/// Thread-safety contract:
///   - <see cref="SendLineAsync"/> is called exclusively by the writer loop (single writer).
///   - <see cref="ReadLineAsync"/> is called exclusively by the reader loop (single reader).
///   - <see cref="Open"/> and <see cref="Close"/> happen outside both loops.
/// </summary>
internal sealed class FlipperRpcTransport : IFlipperTransport
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
    /// Closes the serial port without disposing it.
    /// Calling this unblocks any <see cref="ReadLineAsync"/> that is stuck waiting
    /// for data — on Windows, <see cref="SerialPort"/> ignores cancellation tokens
    /// on the underlying <c>BaseStream.ReadAsync</c>, so closing the port is the
    /// only reliable way to wake the reader loop during shutdown.
    /// Safe to call concurrently with <see cref="ReadLineAsync"/>; the resulting
    /// exception is swallowed by <see cref="ReadLineAsync"/>'s catch-all and returned
    /// as <c>null</c> (EOF).
    /// </summary>
    public void Close()
    {
        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch
        {
            // Best-effort: ignore errors during shutdown close.
        }
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
            // If the port was already closed (via Close()), StreamWriter.DisposeAsync
            // will attempt to flush the underlying SerialStream and throw
            // ObjectDisposedException. Suppress it — there is nothing to flush
            // once the port is shut down.
            try
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { }
        }

        _reader?.Dispose();
        _port.Dispose();
    }
}