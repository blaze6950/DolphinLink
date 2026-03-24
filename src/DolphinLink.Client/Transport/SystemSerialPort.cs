using System.IO.Ports;
using System.Reflection;
using System.Text;
using DolphinLink.Client.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace DolphinLink.Client.Transport;

/// <summary>
/// <see cref="ISerialPort"/> implementation backed by <see cref="SerialPort"/>
/// from <c>System.IO.Ports</c>.
///
/// <para>
/// This class centralises all <see cref="SerialPort"/>-specific logic — including
/// the Windows <see cref="SafeFileHandle"/> force-close trick needed to abort
/// in-flight async I/O — so that both <see cref="SerialPortTransport"/> (NDJSON
/// framing) and the bootstrapper's <c>NativeRpcTransport</c> (protobuf framing)
/// can share a single implementation.
/// </para>
///
/// Windows note: <see cref="SerialPort.BaseStream"/> <c>ReadAsync</c>/<c>WriteAsync</c>
/// ignore <see cref="CancellationToken"/> on Windows.  The only reliable way to unblock
/// pending I/O is to close the underlying OS handle, which <see cref="DisposeAsync"/>
/// does via reflection on the internal <c>SerialStream._handle</c> field.
/// </summary>
public sealed class SystemSerialPort : ISerialPort
{
    private readonly SerialPort _port;
    private Stream? _stream;
    private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

    /// <param name="portName">
    /// COM port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyACM0"</c>.
    /// </param>
    /// <param name="baudRate">
    /// Baud rate.  For USB-CDC this is typically ignored by the OS but must be
    /// supplied.  Defaults to 115200.
    /// </param>
    /// <param name="dtrEnable">
    /// Initial DTR state.  Pass <c>false</c> when the caller needs to toggle DTR
    /// manually during a handshake (native RPC bootstrap); pass <c>true</c> when
    /// the port should signal ready immediately on open (NDJSON daemon port).
    /// </param>
    public SystemSerialPort(string portName, int baudRate = 115200, bool dtrEnable = true)
    {
        _port = new SerialPort(portName, baudRate)
        {
            Encoding     = Encoding.UTF8,
            ReadTimeout  = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            DtrEnable    = dtrEnable,
        };
    }

    // -------------------------------------------------------------------------
    // ISerialPort
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public ValueTask OpenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _port.Open();
        _stream = _port.BaseStream;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public Stream Stream => _stream
        ?? throw new InvalidOperationException("Port is not open. Call OpenAsync first.");

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="SerialPort.DtrEnable"/> is a synchronous property, so this
    /// completes synchronously.  The async signature matches the <see cref="ISerialPort"/>
    /// contract, which is async because WebSerial's <c>setSignals()</c> is async.
    /// </remarks>
    public ValueTask SetDtrAsync(bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _port.DtrEnable = enabled;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public int ReadTimeout
    {
        get => _port.ReadTimeout;
        set => _port.ReadTimeout = value;
    }

    /// <inheritdoc/>
    public int WriteTimeout
    {
        get => _port.WriteTimeout;
        set => _port.WriteTimeout = value;
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Force-closes the underlying OS handle to immediately abort any pending
    /// <c>ReadFile</c>/<c>WriteFile</c> operations, then disposes the port.
    ///
    /// On Windows, <see cref="SerialPort.Close()"/> deadlocks when async I/O
    /// is in-flight: it waits for its internal read thread to exit, but that
    /// thread is blocked on a synchronous <c>ReadFile()</c> call that only
    /// returns once the handle is closed — a circular dependency.
    ///
    /// <see cref="SerialPort.BaseStream"/> is typed as <see cref="Stream"/>
    /// (not <see cref="System.IO.FileStream"/>), so <see cref="SafeFileHandle"/>
    /// is not directly accessible.  The actual runtime type on Windows is the
    /// internal <c>System.IO.Ports.SerialStream</c>, which stores the native
    /// handle in a private <c>_handle</c> field.  Closing it via reflection
    /// yanks the handle at the OS level: all pending I/O returns immediately
    /// with an error, the internal thread exits, and <c>_port.Dispose()</c>
    /// can then complete without hanging.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        if (_stream is not null)
        {
            try
            {
                var handleField = _stream
                    .GetType()
                    .GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);
                if (handleField?.GetValue(_stream) is SafeFileHandle handle)
                {
                    handle.Close();
                }
            }
            catch { /* best-effort — port may not be open or already disposed */ }
        }

        try { _port.Dispose(); }
        catch { /* best-effort */ }

        return ValueTask.CompletedTask;
    }
}
