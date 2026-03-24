using System.Reflection;
using System.Security.Cryptography;
using DolphinLink.Bootstrapper.NativeRpc;
using DolphinLink.Client;
using DolphinLink.Client.Abstractions;
using DolphinLink.Client.Commands.System;
using DolphinLink.Client.Transport;

namespace DolphinLink.Bootstrapper;

/// <summary>
/// Bootstraps the DolphinLink RPC connection end-to-end.
///
/// <para>
/// The Flipper Zero presents two CDC serial interfaces over USB:
/// <list type="bullet">
///   <item><term>Interface 0 (system port)</term><description>
///     The native protobuf RPC — always available; used by qFlipper.
///   </description></item>
///   <item><term>Interface 1 (daemon port)</term><description>
///     The custom NDJSON RPC — only available while the
///     <c>dolphin_link_rpc_daemon</c> FAP is running.
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="BootstrapAsync"/> automates the complete connection lifecycle:
/// <list type="number">
///   <item>Attempt a direct connection on the daemon port.  If the daemon is
///     already running with a compatible version, return immediately
///     (<see cref="BootstrapAction.AlreadyRunning"/>).</item>
///   <item>Open the system port and use the native protobuf RPC to inspect
///     the SD card.</item>
///   <item>If the FAP is missing or its MD5 differs from the bundled binary,
///     upload it (subject to <see cref="BootstrapOptions.AutoInstall"/>).
///   </item>
///   <item>Launch the FAP via <c>app_start</c>.</item>
///   <item>Wait for the daemon to appear on the daemon port (retrying every 500 ms).</item>
///   <item>Connect the <see cref="RpcClient"/> and return.</item>
/// </list>
/// </para>
/// </summary>
public static class Bootstrapper
{
    // Embedded resource name for the bundled FAP binary.
    private const string FapResourceName =
        "DolphinLink.Bootstrapper.Resources.dolphin_link_rpc_daemon.fap";

    // Retry interval while waiting for the daemon to start.
    private static readonly TimeSpan DaemonPollInterval = TimeSpan.FromMilliseconds(500);

    // The minimum daemon protocol version this bootstrapper requires.
    private const int MinProtocolVersion = 1;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Bootstraps the RPC connection.
    /// </summary>
    /// <param name="systemPortName">
    /// COM port for CDC interface 0 (Flipper system / native protobuf RPC),
    /// e.g. <c>"COM3"</c>.  This is the port that qFlipper uses.
    /// </param>
    /// <param name="daemonPortName">
    /// COM port for CDC interface 1 (custom NDJSON RPC daemon),
    /// e.g. <c>"COM4"</c>.
    /// </param>
    /// <param name="options">Bootstrap policy options.  Defaults are safe.</param>
    /// <param name="clientOptions">
    /// Options forwarded to the constructed <see cref="RpcClient"/>.
    /// </param>
    /// <param name="diagnostics">Optional diagnostics sink for the RPC client.</param>
    /// <param name="progress">
    /// Optional progress sink.  Reports FAP upload progress as
    /// <c>(bytesWritten, totalBytes)</c>.  Not called when the daemon is
    /// already running.
    /// </param>
    /// <param name="fapOverride">
    /// Optional FAP binary to use instead of the one embedded in the assembly.
    /// When non-null, the MD5 of these bytes is compared against the installed
    /// FAP on the Flipper SD card, and these bytes are uploaded if the versions
    /// differ.  Intended for development workflows where the FAP is built
    /// locally and passed in directly rather than via an assembly rebuild.
    /// </param>
    /// <param name="systemPortFactory">
    /// Optional factory that creates the <see cref="ISerialPort"/> for CDC interface 0
    /// (the native protobuf system RPC used during bootstrapping).
    /// When <c>null</c>, defaults to <c>() => new SystemSerialPort(systemPortName, dtrEnable: false)</c>.
    /// Provide a custom factory to use a non-system-ports implementation, such as a
    /// WebSerial-backed port in a browser WASM environment.
    /// </param>
    /// <param name="daemonPortFactory">
    /// Optional factory that creates the <see cref="ISerialPort"/> for CDC interface 1
    /// (the NDJSON RPC daemon port).  The factory may be called multiple times during
    /// the daemon-wait retry loop.
    /// When <c>null</c>, defaults to <c>() => new SystemSerialPort(daemonPortName)</c>.
    /// </param>
    /// <param name="onBeforeDaemonConnect">
    /// Optional async callback invoked after the native RPC session closes (i.e. after
    /// the FAP has been installed and launched) and immediately before the daemon-wait
    /// retry loop begins.
    ///
    /// <para>
    /// Use this hook to perform any preparation required before the daemon port becomes
    /// accessible — for example, prompting the user to select the daemon serial port in a
    /// browser WebSerial environment, where the port picker must be shown <em>after</em>
    /// the daemon process has started and registered its CDC interface.
    /// </para>
    ///
    /// <para>
    /// If the callback throws, the exception propagates out of <see cref="BootstrapAsync"/>
    /// unchanged, allowing the caller to handle cancellation or user-abort scenarios.
    /// </para>
    /// </param>
    /// <param name="ct">Cancellation token for the entire bootstrap operation.</param>
    /// <returns>
    /// A <see cref="BootstrapResult"/> holding a ready-to-use
    /// <see cref="RpcClient"/>.  The caller is responsible for disposing it.
    /// </returns>
    /// <exception cref="BootstrapException">
    /// Thrown when bootstrapping fails (connection error, version mismatch when
    /// <see cref="BootstrapOptions.AutoInstall"/> is <c>false</c>,
    /// daemon did not start in time, etc.).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled.
    /// </exception>
    public static async Task<BootstrapResult> BootstrapAsync(
        string systemPortName,
        string daemonPortName,
        BootstrapOptions options        = default,
        RpcClientOptions clientOptions  = default,
        IRpcDiagnostics?        diagnostics    = null,
        IProgress<(int Written, int Total)>? progress = null,
        byte[]?                 fapOverride    = null,
        Func<ISerialPort>?      systemPortFactory = null,
        Func<ISerialPort>?      daemonPortFactory = null,
        Func<Task>?             onBeforeDaemonConnect = null,
        CancellationToken ct = default)
    {
        // Build defaults for any unspecified factory.
        Func<ISerialPort> resolvedSystemFactory  = systemPortFactory
            ?? (() => new SystemSerialPort(systemPortName, dtrEnable: false));
        Func<ISerialPort> resolvedDaemonFactory  = daemonPortFactory
            ?? (() => new SystemSerialPort(daemonPortName));

        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        CancellationToken linked = linkedCts.Token;

        // Step 1 — fast path: try the daemon port directly.
        var (directClient, directInfo) = await TryConnectDaemonDirectAsync(
            resolvedDaemonFactory, clientOptions, diagnostics, linked).ConfigureAwait(false);

        if (directClient is not null && directInfo.HasValue)
        {
            return new BootstrapResult(directClient, BootstrapAction.AlreadyRunning, directInfo.Value);
        }

        // Step 2 — slow path: interact with the native protobuf RPC.
        byte[] bundledFap = fapOverride ?? LoadBundledFap();
        string bundledMd5 = ComputeMd5(bundledFap);

        await using var native = new NativeRpcClient(resolvedSystemFactory());
        try
        {
            await native.OpenAsync(linked).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new BootstrapException(
                $"Could not open native RPC port '{systemPortName}'. " +
                $"Ensure the Flipper is connected and no other application (e.g. qFlipper) " +
                $"is using the port. Inner: {ex.Message}", ex);
        }

        // Verify the connection.
        try
        {
            await native.PingAsync(linked).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new BootstrapException(
                $"Native RPC ping on '{systemPortName}' failed. " +
                $"Inner: {ex.Message}", ex);
        }

        // Determine what needs to happen.
        BootstrapAction action = await DetermineActionAsync(
            native, options.InstallPath, bundledMd5, linked).ConfigureAwait(false);

        if (action is BootstrapAction.Installed or BootstrapAction.Updated)
        {
            if (!options.AutoInstall)
            {
                throw new BootstrapException(
                    $"The daemon FAP requires {(action == BootstrapAction.Installed ? "installation" : "an update")} " +
                    $"at '{options.InstallPath}', but AutoInstall is disabled. " +
                    $"Set BootstrapOptions.AutoInstall = true to allow automatic installation.",
                    action);
            }

            await UploadFapAsync(native, options.InstallPath, bundledFap, progress, linked)
                .ConfigureAwait(false);
        }

        if (action is not BootstrapAction.AlreadyRunning)
        {
            // Launch the FAP via the native RPC.
            await LaunchFapAsync(native, options.InstallPath, linked).ConfigureAwait(false);
        }

        // Close the native RPC connection before opening the daemon port.
        await native.DisposeAsync().ConfigureAwait(false);

        // Allow the caller to prepare the daemon port before the retry loop starts.
        // In a browser WebSerial environment this is where the port picker is shown,
        // since the CDC interface only appears after the daemon FAP has launched.
        if (onBeforeDaemonConnect is not null)
        {
            await onBeforeDaemonConnect().ConfigureAwait(false);
        }

        // Step 3 — wait for the daemon to appear on the NDJSON port.
        // Pass the caller's original ct (not the linked/timeout token) so that
        // WaitForDaemonAsync uses only its own DaemonStartTimeout budget.
        // The overall 60-second timeout (linked) may be nearly exhausted by the
        // time native RPC work completes; using it here would cancel the daemon
        // wait before it even starts.
        RpcClient client = await WaitForDaemonAsync(
            resolvedDaemonFactory, clientOptions, diagnostics,
            options.DaemonStartTimeout, ct).ConfigureAwait(false);

        DaemonInfoResponse daemonInfo = client.DaemonInfo
            ?? throw new BootstrapException("Daemon connected but DaemonInfo is null (internal error).");

        return new BootstrapResult(client, action, daemonInfo);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts a direct connection to the daemon port.  Returns <c>(null, null)</c>
    /// if the daemon is not available or has an incompatible version.
    /// </summary>
    private static async Task<(RpcClient? Client, DaemonInfoResponse? Info)>
        TryConnectDaemonDirectAsync(
            Func<ISerialPort> daemonPortFactory,
            RpcClientOptions clientOptions,
            IRpcDiagnostics? diagnostics,
            CancellationToken ct)
    {
        ISerialPort? port   = null;
        RpcClient? client = null;
        try
        {
            port = daemonPortFactory();
            var transport = new SerialPortTransport(port);
            client = new RpcClient(transport, clientOptions, diagnostics);

            // Use a short timeout for the direct attempt so we don't block long.
            using var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked   = CancellationTokenSource.CreateLinkedTokenSource(ct, shortCts.Token);

            DaemonInfoResponse info = await client
                .ConnectAsync(MinProtocolVersion, linked.Token)
                .ConfigureAwait(false);

            // Success — client owns the transport chain; port lifetime is managed
            // by the transport and ultimately the client's DisposeAsync.
            return (client, info);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Short timeout expired — daemon is not running.
            await DisposeClientAndPortSilently(client, port).ConfigureAwait(false);
            return (null, null);
        }
        catch (OperationCanceledException)
        {
            // Outer ct was cancelled — propagate so the caller stops immediately.
            await DisposeClientAndPortSilently(client, port).ConfigureAwait(false);
            throw;
        }
        catch
        {
            // Port doesn't exist, wrong daemon, version too old, etc.
            await DisposeClientAndPortSilently(client, port).ConfigureAwait(false);
            return (null, null);
        }
    }

    /// <summary>
    /// Disposes the client (which owns its transport chain) and then, if the port was
    /// not yet handed off to the transport, disposes the port directly.
    ///
    /// <para>
    /// <see cref="SerialPortTransport(ISerialPort)"/> sets <c>ownsPort=false</c>, so the
    /// transport does NOT dispose the port when it is itself disposed.  In failure paths
    /// the port must therefore be disposed explicitly alongside the client.
    /// </para>
    ///
    /// <para>
    /// Pass <paramref name="port"/> as <c>null</c> when the port was never created (e.g.
    /// the factory threw before the port was assigned), or when the caller has already
    /// arranged for the port's lifetime separately.
    /// </para>
    /// </summary>
    private static async ValueTask DisposeClientAndPortSilently(
        RpcClient? client,
        ISerialPort? port)
    {
        if (client is not null)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        // Dispose the port directly — the transport does not own it.
        if (port is not null)
        {
            try { await port.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Inspects the Flipper SD card and decides what action is needed.
    /// </summary>
    private static async Task<BootstrapAction> DetermineActionAsync(
        NativeRpcClient native,
        string installPath,
        string bundledMd5,
        CancellationToken ct)
    {
        var stat = await native.StorageStatAsync(installPath, ct).ConfigureAwait(false);

        if (stat is null)
        {
            // File is absent.
            return BootstrapAction.Installed;
        }

        // File exists — compare MD5 to decide if it needs updating.
        string? installedMd5 = await native.StorageMd5SumAsync(installPath, ct).ConfigureAwait(false);

        if (!string.Equals(installedMd5, bundledMd5, StringComparison.OrdinalIgnoreCase))
        {
            return BootstrapAction.Updated;
        }

        // Correct version is present but not running.
        return BootstrapAction.Launched;
    }

    /// <summary>
    /// Ensures the parent directory exists and uploads the FAP in chunks.
    /// </summary>
    private static async Task UploadFapAsync(
        NativeRpcClient native,
        string installPath,
        byte[] fapData,
        IProgress<(int Written, int Total)>? progress,
        CancellationToken ct)
    {
        // Ensure parent directory exists.
        string parentDir = GetParentDirectory(installPath);
        await native.StorageMkdirAsync(parentDir, ct).ConfigureAwait(false);

        // Upload in 512-byte chunks.
        await native.StorageWriteAsync(installPath, fapData, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends <c>app_start</c> for the FAP at <paramref name="installPath"/>.
    /// </summary>
    private static async Task LaunchFapAsync(
        NativeRpcClient native,
        string installPath,
        CancellationToken ct)
    {
        try
        {
            await native.AppStartAsync(installPath, ct).ConfigureAwait(false);
        }
        catch (NativeRpcException ex)
        {
            throw new BootstrapException(
                $"Failed to launch the daemon FAP at '{installPath}' via the native RPC. " +
                $"Native RPC status: {ex.Status}. " +
                $"Ensure no other application is running on the Flipper.", ex);
        }
    }

    /// <summary>
    /// Retries connecting to the daemon port until the daemon appears or the
    /// <paramref name="startTimeout"/> elapses.
    /// </summary>
    private static async Task<RpcClient> WaitForDaemonAsync(
        Func<ISerialPort> daemonPortFactory,
        RpcClientOptions clientOptions,
        IRpcDiagnostics? diagnostics,
        TimeSpan startTimeout,
        CancellationToken ct)
    {
        using var startCts  = new CancellationTokenSource(startTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, startCts.Token);
        CancellationToken linked = linkedCts.Token;

        Exception? lastEx = null;

        while (!linked.IsCancellationRequested)
        {
            ISerialPort? port = null;
            RpcClient? client = null;
            try
            {
                port = daemonPortFactory();
                var transport = new SerialPortTransport(port);
                client = new RpcClient(transport, clientOptions, diagnostics);

                using var attemptCts  = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var attemptLink = CancellationTokenSource.CreateLinkedTokenSource(linked, attemptCts.Token);

                await client.ConnectAsync(MinProtocolVersion, attemptLink.Token).ConfigureAwait(false);
                return client; // success — caller owns client (and transitively the transport)
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await DisposeClientAndPortSilently(client, port).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                await DisposeClientAndPortSilently(client, port).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(DaemonPollInterval, linked).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // startCts expired — fall through to throw below.
                break;
            }
        }

        // Check whether it was the outer token or the start timeout.
        ct.ThrowIfCancellationRequested();

        var timeoutMessage =
            $"The daemon did not appear within {startTimeout.TotalSeconds:0}s " +
            $"after being launched. Last error: {lastEx?.Message ?? "(none)"}";
        throw lastEx is not null
            ? new BootstrapException(timeoutMessage, lastEx)
            : new BootstrapException(timeoutMessage);
    }

    // -------------------------------------------------------------------------
    // FAP / MD5 helpers
    // -------------------------------------------------------------------------

    /// <summary>Loads the bundled FAP binary from the assembly's embedded resources.</summary>
    private static byte[] LoadBundledFap()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(FapResourceName)
            ?? throw new InvalidOperationException(
                $"Bundled FAP resource '{FapResourceName}' was not found in the assembly. " +
                "This is an internal packaging error.");

        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Computes a lowercase hex MD5 hash of <paramref name="data"/>.</summary>
    /// <remarks>
    /// Returns <see cref="string.Empty"/> in browser WASM environments where the MD5
    /// crypto implementation is unavailable at runtime.  The empty string never matches
    /// the Flipper's stored MD5, so the FAP is always re-uploaded in the browser.
    /// </remarks>
    private static string ComputeMd5(byte[] data)
    {
        if (OperatingSystem.IsBrowser())
        {
            // MD5 is not available in the .NET browser WASM runtime.
            // Returning an empty string ensures it never matches the device's hash,
            // so the FAP is always re-uploaded — the correct safe default.
            return string.Empty;
        }

        byte[] hash = MD5.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns the parent directory path of a Flipper SD path.</summary>
    private static string GetParentDirectory(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return lastSlash > 0 ? path[..lastSlash] : "/";
    }
}
