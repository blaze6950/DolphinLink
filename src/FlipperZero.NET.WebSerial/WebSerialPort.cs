using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.WebSerial;

/// <summary>
/// <see cref="ISerialPort"/> implementation backed by the browser's WebSerial API.
///
/// <para>
/// Use <see cref="CreateAsync"/> to prompt the user to pick a port and obtain a
/// fully-initialised instance.  <see cref="OpenAsync"/> must still be called before
/// any I/O (this matches the <see cref="ISerialPort"/> contract and mirrors the
/// <c>SystemSerialPort</c> design).
/// </para>
///
/// <para>
/// The port handle lifecycle is:
/// <code>
/// await using var port = await WebSerialPort.CreateAsync(usbVendorId: 0x0483, usbProductId: 0x5740);
/// await port.OpenAsync(ct);
/// var transport = new SerialPortTransport(port);
/// await transport.OpenAsync(ct);
/// // ...use transport...
/// </code>
/// (Note: <see cref="OpenAsync"/> on <c>WebSerialPort</c> is a no-op for WebSerial
/// because the browser opens the port during the picker flow in <see cref="CreateAsync"/>.
/// The method exists only to satisfy <see cref="ISerialPort"/>; callers should call it
/// for forward compatibility.)
/// </para>
///
/// <para>
/// <b>Browser support:</b> WebSerial is only available in Chromium-based browsers.
/// Check <see cref="WebSerialHelpers.IsSupported"/> before showing any port-picker UI.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class WebSerialPort : ISerialPort
{
    private readonly int _portId;
    private WebSerialStream? _stream;
    private int _readTimeout  = -1;
    private int _writeTimeout = -1;
    private int _disposed;  // 0 = alive, 1 = disposed (Interlocked)
    private int _forgotten; // 0 = not forgotten, 1 = forgotten (Interlocked)

    internal WebSerialPort(int portId)
    {
        _portId = portId;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the browser's serial port picker and opens the selected port.
    ///
    /// <para>
    /// Call this on a user-gesture (button click, etc.) — browsers require a
    /// user activation before the picker can appear.
    /// </para>
    /// </summary>
    /// <param name="usbVendorId">
    /// USB vendor ID filter.  Pass <c>0x0483</c> for Flipper Zero.
    /// Pass <c>0</c> to show all available ports.
    /// </param>
    /// <param name="usbProductId">
    /// USB product ID filter.  Pass <c>0x5740</c> for Flipper Zero.
    /// Pass <c>0</c> to match any product ID within the vendor.
    /// </param>
    /// <param name="baudRate">
    /// Baud rate.  For USB-CDC this is typically ignored by the OS but is required
    /// by the WebSerial open options.  Defaults to 115200.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WebSerialPort"/> ready to be passed to <see cref="OpenAsync"/>,
    /// or <see langword="null"/> if the user cancelled the picker or the port failed
    /// to open.
    /// </returns>
    public static async Task<WebSerialPort?> CreateAsync(
        int usbVendorId  = 0x0483,
        int usbProductId = 0x5740,
        int baudRate     = 115200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Load the JS interop module on first use.
        await JSHost.ImportAsync(WebSerialInterop.ModuleName, WebSerialInterop.ModuleUrl, ct)
                    .ConfigureAwait(false);

        // Resolve the [JSExport] OnData entry point so the JS pump can call back into .NET.
        // The assembly name must match the DLL produced by this project.
        await WebSerialInterop.InitModuleJs("FlipperZero.NET.WebSerial.dll")
                               .ConfigureAwait(false);

        int portId = await WebSerialInterop.OpenPortJs(usbVendorId, usbProductId, baudRate)
                                           .ConfigureAwait(false);

        return portId < 0 ? null : new WebSerialPort(portId);
    }

    // -------------------------------------------------------------------------
    // ISerialPort
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// For WebSerial the port is already open after <see cref="CreateAsync"/> completes —
    /// the browser's <c>port.open()</c> is called inside <c>openPort()</c> in JS.
    /// This method creates the <see cref="WebSerialStream"/> and starts the JS read pump.
    /// </remarks>
    public ValueTask OpenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _stream = new WebSerialStream(_portId);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public Stream Stream => _stream
        ?? throw new InvalidOperationException("Port is not open. Call OpenAsync first.");

    /// <inheritdoc/>
    public async ValueTask SetDtrAsync(bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await WebSerialInterop.SetSignalsJs(_portId, enabled).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// WebSerial does not support per-read timeouts; this value is stored for
    /// interface compatibility but has no effect.  Use <see cref="CancellationToken"/>
    /// for bounding I/O operations in browser environments.
    /// </remarks>
    public int ReadTimeout
    {
        get => _readTimeout;
        set => _readTimeout = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stored for interface compatibility; no effect on WebSerial.
    /// </remarks>
    public int WriteTimeout
    {
        get => _writeTimeout;
        set => _writeTimeout = value;
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Dispose the stream first (completes the channel → unblocks ReadAsync).
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        // Close the JS port handle (keeps browser permission grant).
        try
        {
            await WebSerialInterop.ClosePortJs(_portId).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort; port may already be closed (e.g. physical disconnect).
        }
    }

    /// <summary>
    /// Closes the port AND revokes the browser's permission grant, completely
    /// releasing all OS-level claims and removing it from
    /// <c>navigator.serial.getPorts()</c>.
    ///
    /// <para>
    /// Call this <b>instead of</b> <see cref="DisposeAsync"/> when the port must be
    /// fully released so that the Flipper's USB stack can re-enumerate — for example,
    /// just before the daemon FAP switches the device from <c>usb_cdc_single</c> to
    /// <c>usb_cdc_dual</c>.  After forgetting, the browser will show the re-enumerated
    /// port as an unrecognised device in the next <c>requestPort()</c> picker.
    /// </para>
    ///
    /// <para>
    /// This method is safe to call even if <see cref="DisposeAsync"/> has already run
    /// (e.g. because the bootstrapper disposed the underlying port before
    /// <c>onBeforeDaemonConnect</c> fired).  The JS <c>forgetPort()</c> function
    /// maintains a secondary map of closed-but-not-forgotten ports and will still
    /// revoke the permission grant via <c>SerialPort.forget()</c>.
    /// </para>
    ///
    /// <para>
    /// This method is idempotent.  Subsequent calls return immediately.
    /// </para>
    /// </summary>
    public async ValueTask ForgetAsync()
    {
        if (Interlocked.Exchange(ref _forgotten, 1) == 1)
        {
            return;
        }

        // If DisposeAsync has not yet run, clean up the stream first to unblock
        // any in-progress ReadAsync before we tell JS to forget the port.
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        // Always call ForgetPortJs — even if DisposeAsync (and therefore ClosePortJs)
        // already ran.  The JS forgetPort() function checks its _closedPorts fallback
        // map so it can still call SerialPort.forget() on the raw port object.
        try
        {
            await WebSerialInterop.ForgetPortJs(_portId).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort; port may have been physically disconnected.
        }
    }
}
