using System.IO.Ports;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Transport;

/// <summary>
/// USB-CDC <see cref="IFlipperTransport"/> implementation backed by <see cref="SerialPort"/>.
///
/// Pass an instance of this class to the
/// <see cref="FlipperRpcClient(IFlipperTransport,FlipperRpcClientOptions,IRpcDiagnostics)"/>
/// constructor to connect to a Flipper Zero over a serial port.
///
/// Threading contract (inherited from <see cref="IFlipperTransport"/>):
///   - <see cref="SendAsync"/> may be called from any thread; callers that need a
///     single-writer guarantee should wrap this in a
///     <see cref="PacketSerializationTransport"/>.
///   - <see cref="ReceiveAsync"/> must only be called from one reader at a time.
///   - <see cref="OpenAsync"/> must be called before any I/O.
///   - Closing is via cancelling the <see cref="ReceiveAsync"/> token and
///     <see cref="DisposeAsync"/>.
///
/// Windows note: <see cref="SerialPort.BaseStream"/> ReadAsync ignores
/// CancellationTokens.  The only reliable way to unblock a pending read is to
/// close the port — which is what <see cref="DisposeAsync"/> does.
/// </summary>
public sealed class SerialPortTransport : IFlipperTransport
{
    private readonly SerialPort _port;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    /// <param name="portName">COM port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.</param>
    /// <param name="baudRate">
    /// Baud rate. For USB-CDC this is typically ignored by the OS but must be supplied.
    /// Defaults to 115200.
    /// </param>
    public SerialPortTransport(string portName, int baudRate = 115200)
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

    /// <inheritdoc/>
    public ValueTask OpenAsync(CancellationToken ct = default)
    {
        _port.Open();
        _writer = new StreamWriter(_port.BaseStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        _reader = new StreamReader(_port.BaseStream, Encoding.UTF8, leaveOpen: true);
        return ValueTask.CompletedTask;
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

    public async ValueTask DisposeAsync()
    {
        // Force-close the underlying OS handle to immediately abort any pending
        // ReadFile/WriteFile operations on the port.
        //
        // On Windows, SerialPort.Close() deadlocks when async I/O is in-flight:
        // it waits for its internal read thread to exit, but that thread is
        // blocked on a synchronous ReadFile() call which only returns once the
        // handle is closed — a circular dependency.
        //
        // SerialPort.BaseStream is typed as Stream (not FileStream), so
        // SafeFileHandle is not directly accessible. The actual runtime type on
        // Windows is the internal System.IO.Ports.SerialStream, which holds the
        // native handle in a private "_handle" field of type SafeFileHandle.
        // Closing it via reflection yanks the handle at the OS level: all
        // pending I/O returns immediately with an error, the internal read
        // thread exits, and SerialPort.Dispose() can then clean up cleanly.
        try
        {
            var handleField = _port.BaseStream
                .GetType()
                .GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);
            if (handleField?.GetValue(_port.BaseStream) is SafeFileHandle handle)
            {
                handle.Close();
            }
        }
        catch { /* best-effort — port may not be open or already disposed */ }

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

        try { _port.Dispose(); }
        catch { /* best-effort */ }
    }
}
