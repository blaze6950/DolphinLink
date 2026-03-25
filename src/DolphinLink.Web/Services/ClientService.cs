using System.Runtime.Versioning;
using DolphinLink.Bootstrapper;
using DolphinLink.Client;
using DolphinLink.Client.Commands.System;
using DolphinLink.SerialPort.WebSerial;
using DolphinLinkBootstrapper = DolphinLink.Bootstrapper.Bootstrapper;

namespace DolphinLink.Web.Services;

/// <summary>
/// Singleton service that owns the <see cref="RpcClient"/> and the entire
/// connection lifecycle — auto-connect on startup, manual bootstrap flow, disconnect.
///
/// <para>
/// Port lifetime is managed here, not in any page.  When a page navigates away its
/// component is disposed, but this service (and the ports / client it holds) lives for
/// the entire application lifetime.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class ClientService : IAsyncDisposable
{
    // ── Connected client ──────────────────────────────────────────────────────

    private RpcClient? _client;

    /// <summary>The connected RPC client, or <c>null</c> when disconnected.</summary>
    public RpcClient? Client => _client;

    /// <summary>Daemon info fetched on connect, or <c>null</c> when disconnected.</summary>
    public DaemonInfoResponse? DaemonInfo { get; private set; }

    /// <summary>Device info fetched on connect, or <c>null</c> when disconnected.</summary>
    public DeviceInfoResponse? DeviceInfo { get; private set; }

    /// <summary>True when a client is connected and ready.</summary>
    public bool IsConnected => _client is not null;

    // ── UI-observable state ───────────────────────────────────────────────────

    /// <summary>True while an async connect / disconnect operation is in progress.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Human-readable progress message, or <c>null</c> when idle.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>Latest error message, or <c>null</c> when no error.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Whether WebSerial is supported in the current browser.</summary>
    public bool WebSerialSupported { get; private set; } = true; // assume until checked

    /// <summary>
    /// True during the bootstrap flow while the app waits for the user to pick
    /// the daemon port (CDC 1) via a fresh browser dialog.
    /// </summary>
    public bool ShowPickDaemonButton { get; private set; }

    /// <summary>Raised whenever any observable state changes so that components can re-render.</summary>
    public event Action? StateChanged;

    // ── Bootstrap transient ports ─────────────────────────────────────────────

    // System port (CDC 0) — held only for the duration of the bootstrap flow.
    private WebSerialPort? _systemPort;

    // Daemon port (CDC 1) — held for the lifetime of the connection so the
    // browser's WebSerial handle stays open while the client is alive.
    private WebSerialPort? _daemonPort;

    // TCS bridge: pauses onBeforeDaemonConnect until the user picks the daemon port.
    private TaskCompletionSource<WebSerialPort?>? _daemonPortTcs;

    // Shared options & diagnostics ──────────────────────────────────────────

    /// <summary>
    /// When <c>true</c> the client enables daemon-side diagnostics so that every
    /// response includes a <c>_m</c> metrics object (parse / dispatch / execute /
    /// serialize / total milliseconds).  Defaults to <c>true</c>.  Changes take
    /// effect on the next connection attempt.
    /// </summary>
    public bool DaemonMetricsEnabled { get; set; } = true;

    // Builds options tuned for single-threaded Blazor WASM:
    //   DisablePacketSerialization — no OS thread concurrency in WASM; the Channel
    //     + writer-loop Task.Run adds overhead without benefit.
    //   DisableHeartbeat — the keep-alive timer loop competes with the WebSerial JS
    //     read pump on the cooperative scheduler, causing false RX timeouts and
    //     spurious disconnects.  The configure command still sets effectively-infinite
    //     hb/to values so the daemon never times out the client itself.
    //   DaemonDiagnostics — controlled by DaemonMetricsEnabled at connect time.
    private RpcClientOptions BuildClientOptions() => new()
    {
        DisableHeartbeat           = true,
        DisablePacketSerialization = true,
        DaemonDiagnostics          = DaemonMetricsEnabled,
    };

    // Singleton diagnostics sink — buffers entries for the in-page RPC console and
    // also mirrors raw JSON to the browser DevTools console via Console.WriteLine.
    private readonly RpcConsoleService _diagnostics;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ClientService(RpcConsoleService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    // ── Auto-connect (called once from MainLayout on app startup) ─────────────

    /// <summary>
    /// Initialises WebSerial support detection and attempts a no-interaction connect
    /// if the daemon is already running and the browser has a permission grant.
    /// Safe to call multiple times — no-ops if already connected.
    /// </summary>
    public async Task AutoConnectAsync()
    {
        if (IsConnected) return;

        // Detect browser support (also loads the JS interop module).
        try
        {
            WebSerialSupported = await WebSerialHelpers.IsSupportedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DolphinLink] WebSerial interop load failed: {ex}");
            WebSerialSupported = false;
            ErrorMessage = $"Failed to load WebSerial interop: {ex.Message}";
            NotifyStateChanged();
            return;
        }

        if (!WebSerialSupported)
        {
            NotifyStateChanged();
            return;
        }

        IsBusy = true;
        StatusMessage = "Looking for Flipper Zero daemon...";
        NotifyStateChanged();

        try
        {
            var autoResult = await WebSerialPortPicker.TryAutoConnectAsync(
                clientOptions: BuildClientOptions(),
                diagnostics:   _diagnostics).ConfigureAwait(false);

            if (autoResult is not null)
            {
                // Track the daemon port so DisconnectAsync can close it properly.
                _daemonPort = autoResult.DaemonPort;

                var daemonInfo = await autoResult.Client
                    .SendAsync<DaemonInfoCommand, DaemonInfoResponse>(new DaemonInfoCommand())
                    .ConfigureAwait(false);
                var deviceInfo = await autoResult.Client
                    .SendAsync<DeviceInfoCommand, DeviceInfoResponse>(new DeviceInfoCommand())
                    .ConfigureAwait(false);
                SetConnectedCore(autoResult.Client, daemonInfo, deviceInfo);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Auto-connect failed: {ex.Message}";
            await DisposeClientAsync().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
            NotifyStateChanged();
        }
    }

    // ── Manual connect — bootstrap flow ──────────────────────────────────────

    /// <summary>
    /// Starts the manual connect flow.  First attempts a quick reconnect using
    /// previously-granted ports (no user interaction needed); if the daemon is
    /// already running this completes instantly without bootstrap.  If no daemon
    /// is reachable, falls through to the full bootstrap flow: shows the system-port
    /// picker (CDC 0), installs / launches the daemon FAP, waits for USB
    /// re-enumeration, then connects via CDC 1.
    ///
    /// <para>
    /// Must be called directly from a button-click handler to satisfy the browser's
    /// user-gesture requirement for <c>navigator.serial.requestPort()</c>.
    /// </para>
    /// </summary>
    public async Task ConnectAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            // Quick reconnect: if the daemon is still running from a previous session,
            // connect immediately using previously-granted ports (no user gesture needed).
            StatusMessage = "Checking for running daemon...";
            NotifyStateChanged();

            var quickResult = await WebSerialPortPicker.TryAutoConnectAsync(
                clientOptions: BuildClientOptions(),
                diagnostics:   _diagnostics).ConfigureAwait(false);

            if (quickResult is not null)
            {
                // Track the daemon port so the next DisconnectAsync can close it.
                _daemonPort = quickResult.DaemonPort;

                var quickDaemonInfo = await quickResult.Client
                    .SendAsync<DaemonInfoCommand, DaemonInfoResponse>(new DaemonInfoCommand())
                    .ConfigureAwait(false);
                var quickDeviceInfo = await quickResult.Client
                    .SendAsync<DeviceInfoCommand, DeviceInfoResponse>(new DeviceInfoCommand())
                    .ConfigureAwait(false);
                SetConnectedCore(quickResult.Client, quickDaemonInfo, quickDeviceInfo);
                return;
            }

            // No daemon reachable — fall through to full bootstrap.
            // Step 1: system port picker — must be in a user-gesture handler.
            StatusMessage = "Select the Flipper system port, e.g. COM3 (CDC 0), in the browser dialog...";
            NotifyStateChanged();

            _systemPort = await WebSerialPortPicker.PickSystemPortAsync().ConfigureAwait(false);
            if (_systemPort is null)
            {
                ErrorMessage = "System port selection cancelled.";
                return;
            }

            // Step 2: TCS that bridges onBeforeDaemonConnect with the daemon-port button click.
            _daemonPortTcs = WebSerialPortPicker.CreateDaemonPortWaiter();

            // Step 3: bootstrap — install / update / launch the daemon FAP.
            StatusMessage = "Bootstrapping (installing / launching daemon)...";
            NotifyStateChanged();

            BootstrapResult result = await DolphinLinkBootstrapper.BootstrapAsync(
                systemPortName:  "(webserial)",
                daemonPortName:  "(webserial)",
                systemPortFactory: () => _systemPort,
                daemonPortFactory: () => _daemonPort
                    ?? throw new InvalidOperationException("Daemon port was not set."),
                clientOptions: BuildClientOptions(),
                diagnostics:   _diagnostics,
                onBeforeDaemonConnect: async () =>
                {
                    // Forget the system port so the browser releases its USB claim,
                    // allowing the daemon FAP to switch the Flipper to usb_cdc_dual.
                    await WebSerialPortPicker.ForgetSystemPortAsync(_systemPort!).ConfigureAwait(false);
                    _systemPort = null;

                    // Wait for the Flipper to re-enumerate its dual-CDC interfaces.
                    StatusMessage = "Waiting for USB re-enumeration...";
                    NotifyStateChanged();
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                    // Show the "Select Daemon Port" button; pause until the user picks.
                    ShowPickDaemonButton = true;
                    NotifyStateChanged();

                    _daemonPort = await WebSerialPortPicker
                        .WaitForDaemonPortAsync(_daemonPortTcs!).ConfigureAwait(false);

                    ShowPickDaemonButton = false;

                    if (_daemonPort is null)
                        throw new OperationCanceledException("Daemon port selection was cancelled.");

                    StatusMessage = "Connecting to daemon...";
                    NotifyStateChanged();
                }).ConfigureAwait(false);

            StatusMessage = null;

            var deviceInfo = await result.Client
                .SendAsync<DeviceInfoCommand, DeviceInfoResponse>(new DeviceInfoCommand())
                .ConfigureAwait(false);

            // result.Client is owned by the result; take it over (don't dispose result).
            SetConnectedCore(result.Client, result.DaemonInfo, deviceInfo);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Connection cancelled.";
            await CleanupBootstrapPortsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            await CleanupBootstrapPortsAsync().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
            ShowPickDaemonButton = false;
            StatusMessage = null;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Called from the "Select Daemon Port, e.g. COM4 (CDC 1)" button click — a fresh
    /// user gesture that satisfies the browser's <c>requestPort()</c> activation requirement.
    /// </summary>
    public async Task PickDaemonPortAsync()
    {
        WebSerialPort? port = await WebSerialPortPicker.PickSystemPortAsync().ConfigureAwait(false);
        WebSerialPortPicker.SignalDaemonPortReady(_daemonPortTcs!, port);
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the client, closes the daemon port, and resets all state.
    /// </summary>
    public async Task DisconnectAsync()
    {
        IsBusy = true;
        NotifyStateChanged();
        try
        {
            _daemonPortTcs?.TrySetCanceled();
            _daemonPortTcs = null;

            await DisposeClientAsync().ConfigureAwait(false);
            await CleanupBootstrapPortsAsync().ConfigureAwait(false);

            // Close the daemon port that was kept alive for the connection lifetime.
            if (_daemonPort is not null)
            {
                await _daemonPort.DisposeAsync().ConfigureAwait(false);
                _daemonPort = null;
            }

            ErrorMessage = null;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void SetConnectedCore(
        RpcClient client,
        DaemonInfoResponse daemonInfo,
        DeviceInfoResponse? deviceInfo)
    {
        _client    = client;
        DaemonInfo = daemonInfo;
        DeviceInfo = deviceInfo;
        ErrorMessage = null;
        NotifyStateChanged();
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client    = null;
            DaemonInfo = null;
            DeviceInfo = null;
        }
    }

    /// <summary>
    /// Disposes the system port (if still held) after a failed or cancelled bootstrap.
    /// Does NOT dispose the daemon port — that is handled separately on disconnect
    /// because it must stay open for the lifetime of the connection.
    /// </summary>
    private async Task CleanupBootstrapPortsAsync()
    {
        if (_systemPort is not null)
        {
            await _systemPort.DisposeAsync().ConfigureAwait(false);
            _systemPort = null;
        }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _daemonPortTcs?.TrySetCanceled();
        _daemonPortTcs = null;

        await DisposeClientAsync().ConfigureAwait(false);
        await CleanupBootstrapPortsAsync().ConfigureAwait(false);

        if (_daemonPort is not null)
        {
            await _daemonPort.DisposeAsync().ConfigureAwait(false);
            _daemonPort = null;
        }
    }
}
