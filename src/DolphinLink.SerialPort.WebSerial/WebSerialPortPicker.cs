using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DolphinLink.Client;
using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Transport;

namespace DolphinLink.SerialPort.WebSerial;

/// <summary>
/// Provides convenience factory methods for prompting the user to select a
/// Flipper Zero serial port via the browser's WebSerial port picker, and for
/// connecting to the daemon port after the system port is forgotten and the
/// device re-enumerates.
///
/// <para>
/// The Flipper Zero exposes two USB CDC interfaces:
/// <list type="bullet">
///   <item>CDC 0 — system/native protobuf RPC (used by <c>Bootstrapper</c>)</item>
///   <item>CDC 1 — daemon NDJSON RPC (used by <c>RpcClient</c>)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Bootstrap flow (first connect):</b>
/// <list type="number">
///   <item>Call <see cref="PickSystemPortAsync"/> from a button click to let the user
///     select the system port (CDC 0).</item>
    /// <item>Run <c>Bootstrapper.BootstrapAsync</c>.  In the
///     <c>onBeforeDaemonConnect</c> callback:
///     <list type="bullet">
///       <item>Call <see cref="ForgetSystemPortAsync"/> to fully close and revoke the
///         system port, releasing the OS-level USB claim so the daemon FAP can switch
///         the Flipper to <c>usb_cdc_dual</c> mode.</item>
///       <item>Use <see cref="CreateDaemonPortWaiter"/> / <see cref="SignalDaemonPortReady"/>
///         to pause the bootstrap flow until the user picks the re-enumerated daemon
///         port (CDC 1) via a fresh <c>requestPort()</c> picker triggered by a button
///         click in the UI.</item>
///     </list>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Subsequent visits (daemon already running):</b>
/// <c>TryAutoConnectAsync</c> in <c>OnInitializedAsync</c> connects immediately
/// with no user interaction.
/// </para>
///
/// <para>
/// Picker methods must be called from a user-gesture handler (button click, etc.)
/// because browsers gate <c>navigator.serial.requestPort()</c> on user activation.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public static class WebSerialPortPicker
{
    /// <summary>Flipper Zero USB vendor ID.</summary>
    public const int FlipperVendorId  = 0x0483;

    /// <summary>Flipper Zero USB product ID.</summary>
    public const int FlipperProductId = 0x5740;

    // Minimum daemon protocol version accepted by TryAutoConnectAsync.
    private const int MinProtocolVersion = 1;

    // -------------------------------------------------------------------------
    // Picker helpers (require user gesture)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the browser port picker filtered to Flipper Zero devices and returns
    /// a <see cref="WebSerialPort"/> for the <b>system / native RPC</b> port (CDC 0).
    ///
    /// <para>
    /// The system port is used by <c>Bootstrapper</c> to upload and launch
    /// the daemon FAP via the Flipper's built-in protobuf RPC.
    /// </para>
    /// </summary>
    /// <param name="baudRate">Baud rate.  Defaults to 115200.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The selected <see cref="WebSerialPort"/>, or <see langword="null"/> if the
    /// user cancelled or the port failed to open.
    /// </returns>
    public static Task<WebSerialPort?> PickSystemPortAsync(
        int baudRate = 115200,
        CancellationToken ct = default)
        => WebSerialPort.CreateAsync(FlipperVendorId, FlipperProductId, baudRate, ct);

    /// <summary>
    /// Shows the port picker without any USB VID/PID filter, allowing the user to
    /// select any available serial port.
    /// </summary>
    /// <param name="baudRate">Baud rate.  Defaults to 115200.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The selected <see cref="WebSerialPort"/>, or <see langword="null"/> if the
    /// user cancelled or the port failed to open.
    /// </returns>
    public static Task<WebSerialPort?> PickAnyPortAsync(
        int baudRate = 115200,
        CancellationToken ct = default)
        => WebSerialPort.CreateAsync(usbVendorId: 0, usbProductId: 0, baudRate, ct);

    // -------------------------------------------------------------------------
    // System port teardown
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fully closes the system port (CDC 0) and revokes the browser's permission
    /// grant, releasing all OS-level USB claims.
    ///
    /// <para>
    /// Call this inside the <c>onBeforeDaemonConnect</c> callback of
    /// <c>Bootstrapper.BootstrapAsync</c>, immediately after the native RPC
    /// session closes and before waiting for the daemon port.  Forgetting the port
    /// removes the browser's hold on the USB device so that the daemon FAP's call to
    /// <c>furi_hal_usb_set_config(&amp;usb_cdc_dual)</c> can succeed — without this,
    /// the browser's open port handle locks the Flipper's USB stack and the switch
    /// to dual-CDC mode never happens.
    /// </para>
    ///
    /// <para>
    /// After this call the port is invalid and must not be used again.
    /// </para>
    /// </summary>
    /// <param name="systemPort">The system port returned by <see cref="PickSystemPortAsync"/>.</param>
    public static async Task ForgetSystemPortAsync(WebSerialPort systemPort)
    {
        await systemPort.ForgetAsync().ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Daemon port waiter — TCS bridge between bootstrap callback and UI button
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="TaskCompletionSource{TResult}"/> that bridges the
    /// <c>onBeforeDaemonConnect</c> bootstrap callback (which must pause until the
    /// daemon port is selected) with a UI button click (which calls
    /// <c>requestPort()</c> to satisfy the browser's user-gesture requirement).
    ///
    /// <para>
    /// <b>Usage pattern:</b>
    /// <code>
    /// // In component state:
    /// TaskCompletionSource&lt;WebSerialPort?&gt;? _daemonPortTcs;
    ///
    /// // In ConnectAsync (bootstrap flow):
    /// _daemonPortTcs = WebSerialPortPicker.CreateDaemonPortWaiter();
    /// await Bootstrapper.BootstrapAsync(
    ///     ...
    ///     onBeforeDaemonConnect: async () =>
    ///     {
    ///         await WebSerialPortPicker.ForgetSystemPortAsync(_systemPort);
    ///         await Task.Delay(reEnumerationDelay);          // wait for dual-CDC
    ///         _showPickDaemonButton = true;
    ///         StateHasChanged();
    ///         _daemonPort = await WebSerialPortPicker.WaitForDaemonPortAsync(_daemonPortTcs);
    ///     });
    ///
    /// // In PickDaemonPortAsync (button click — fresh user gesture):
    /// var port = await WebSerialPortPicker.PickSystemPortAsync();
    /// WebSerialPortPicker.SignalDaemonPortReady(_daemonPortTcs, port);
    /// </code>
    /// </para>
    /// </summary>
    public static TaskCompletionSource<WebSerialPort?> CreateDaemonPortWaiter()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Awaits the daemon port selected by the user via a button click.
    /// Blocks until <see cref="SignalDaemonPortReady"/> is called.
    /// </summary>
    /// <param name="tcs">The waiter created by <see cref="CreateDaemonPortWaiter"/>.</param>
    /// <param name="ct">Cancellation token.  Cancelling rejects the TCS.</param>
    /// <returns>
    /// The <see cref="WebSerialPort"/> supplied by <see cref="SignalDaemonPortReady"/>,
    /// or <see langword="null"/> if the user cancelled the picker.
    /// </returns>
    public static async Task<WebSerialPort?> WaitForDaemonPortAsync(
        TaskCompletionSource<WebSerialPort?> tcs,
        CancellationToken ct = default)
    {
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the daemon port waiter with the port selected by the user.
    /// Call this from a button-click handler (fresh user gesture) after calling
    /// <see cref="PickSystemPortAsync"/> or <see cref="PickAnyPortAsync"/>.
    /// </summary>
    /// <param name="tcs">The waiter created by <see cref="CreateDaemonPortWaiter"/>.</param>
    /// <param name="port">
    /// The selected port, or <see langword="null"/> if the user cancelled the picker.
    /// </param>
    public static void SignalDaemonPortReady(
        TaskCompletionSource<WebSerialPort?> tcs,
        WebSerialPort? port)
        => tcs.TrySetResult(port);

    // -------------------------------------------------------------------------
    // Auto-connect (no user gesture required)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to connect to the Flipper Zero daemon automatically, without
    /// requiring any user interaction.
    ///
    /// <para>
    /// Uses <c>navigator.serial.getPorts()</c> to enumerate previously-granted
    /// serial ports.  For each Flipper-matching port it tries to open a
    /// <see cref="RpcClient"/> and call <c>ConnectAsync</c> with a short
    /// timeout.  The first port that responds correctly is returned; all others
    /// are closed.
    /// </para>
    ///
    /// <para>
    /// Call this from <c>OnInitializedAsync</c> (no user gesture needed).
    /// It returns <see langword="null"/> when:
    /// <list type="bullet">
    ///   <item>No previously-granted Flipper ports exist (first visit, no permission yet).</item>
    ///   <item>The daemon is not running on any of the available ports.</item>
    ///   <item>The browser is not Chromium-based (WebSerial unsupported).</item>
    /// </list>
    /// In all these cases the caller should show the normal "Connect" button.
    /// </para>
    /// </summary>
    /// <param name="baudRate">Baud rate used when opening each candidate port.  Defaults to 115200.</param>
    /// <param name="probeTimeout">
    /// Timeout for each individual port probe.  Defaults to 3 seconds.
    /// </param>
    /// <param name="clientOptions">Options forwarded to the <see cref="RpcClient"/>.</param>
    /// <param name="diagnostics">Optional diagnostics sink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WebSerialAutoConnectResult"/> holding the connected
    /// <see cref="RpcClient"/> and the underlying <see cref="WebSerialPort"/>,
    /// or <see langword="null"/> if the daemon is not reachable on any available port.
    /// The caller owns both and is responsible for disposing them on disconnect.
    /// </returns>
    public static async Task<WebSerialAutoConnectResult?> TryAutoConnectAsync(
        int baudRate = 115200,
        TimeSpan? probeTimeout = null,
        RpcClientOptions clientOptions = default,
        IRpcDiagnostics? diagnostics = null,
        CancellationToken ct = default)
    {
        var timeout = probeTimeout ?? TimeSpan.FromSeconds(3);

        // Load the JS module so GetPortsJs is available.
        await JSHost.ImportAsync(WebSerialInterop.ModuleName, WebSerialInterop.ModuleUrl, ct)
                    .ConfigureAwait(false);
        await WebSerialInterop.InitModuleJs("DolphinLink.SerialPort.WebSerial.dll")
                               .ConfigureAwait(false);

        if (!WebSerialHelpers.IsSupported())
        {
            return null;
        }

        // Enumerate all previously-granted Flipper ports (no user gesture needed).
        int[] portIds = JsonSerializer.Deserialize<int[]>(
            await WebSerialInterop
                .GetPortsJs(FlipperVendorId, FlipperProductId, baudRate)
                .ConfigureAwait(false)) ?? [];

        if (portIds.Length == 0)
        {
            return null;
        }

        // Probe each port: try to ConnectAsync with a short timeout.
        // The daemon port (CDC 1) will respond; the system port (CDC 0) will not.
        foreach (int portId in portIds)
        {
            var port = new WebSerialPort(portId);
            RpcClient? client = null;
            try
            {
                var transport = new SerialPortTransport(port);
                client = new RpcClient(transport, clientOptions, diagnostics);

                using var probeCts  = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, probeCts.Token);

                await client.ConnectAsync(MinProtocolVersion, linkedCts.Token).ConfigureAwait(false);

                // Success — this is the daemon port.  Close any remaining ports.
                foreach (int otherId in portIds)
                {
                    if (otherId == portId)
                    {
                        continue;
                    }

                    try { await WebSerialInterop.ClosePortJs(otherId).ConfigureAwait(false); }
                    catch { /* best-effort */ }
                }

                return new WebSerialAutoConnectResult(client, port);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Outer cancellation — clean up and propagate.
                if (client is not null)
                {
                    try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                // Port is not owned by the transport; dispose it explicitly.
                try { await port.DisposeAsync().ConfigureAwait(false); } catch { }

                throw;
            }
            catch
            {
                // Probe timeout or wrong port — dispose and try the next one.
                if (client is not null)
                {
                    try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                // Port is not owned by the transport; dispose it explicitly.
                try { await port.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        return null;
    }
}

/// <summary>
/// Holds the result of a successful <see cref="WebSerialPortPicker.TryAutoConnectAsync"/>
/// call: the connected <see cref="RpcClient"/> and the underlying
/// <see cref="WebSerialPort"/> daemon handle.
///
/// <para>
/// The caller owns both objects.  On disconnect, call
/// <see cref="IAsyncDisposable.DisposeAsync"/> on the client first, then on
/// the port — or simply dispose this result object which handles both in the
/// correct order.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class WebSerialAutoConnectResult : IAsyncDisposable
{
    /// <summary>The connected RPC client.</summary>
    public RpcClient Client { get; }

    /// <summary>
    /// The underlying <see cref="WebSerialPort"/> that the client communicates
    /// through.  Must be kept alive (not disposed) for as long as the client is
    /// in use, and closed after the client is disposed on disconnect.
    /// </summary>
    public WebSerialPort DaemonPort { get; }

    internal WebSerialAutoConnectResult(RpcClient client, WebSerialPort daemonPort)
    {
        Client     = client;
        DaemonPort = daemonPort;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        await DaemonPort.DisposeAsync().ConfigureAwait(false);
    }
}
