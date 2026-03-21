using System.Reflection;
using System.Security.Cryptography;
using FlipperZero.NET.Abstractions;
using FlipperZero.NET.Bootstrapper.NativeRpc;
using FlipperZero.NET.Commands.System;
using FlipperZero.NET.Transport;

namespace FlipperZero.NET.Bootstrapper;

/// <summary>
/// Bootstraps the FlipperZero.NET RPC connection end-to-end.
///
/// <para>
/// The Flipper Zero presents two CDC serial interfaces over USB:
/// <list type="bullet">
///   <item><term>Interface 0 (system port)</term><description>
///     The native protobuf RPC — always available; used by qFlipper.
///   </description></item>
///   <item><term>Interface 1 (daemon port)</term><description>
///     The custom NDJSON RPC — only available while the
///     <c>flipper_zero_rpc_daemon</c> FAP is running.
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
///     upload it (subject to <see cref="FlipperBootstrapOptions.AutoInstall"/>).
///   </item>
///   <item>Launch the FAP via <c>app_start</c>.</item>
///   <item>Wait for the daemon to appear on the daemon port (retrying every 500 ms).</item>
///   <item>Connect the <see cref="FlipperRpcClient"/> and return.</item>
/// </list>
/// </para>
/// </summary>
public static class FlipperBootstrapper
{
    // Embedded resource name for the bundled FAP binary.
    private const string FapResourceName =
        "FlipperZero.NET.Bootstrapper.Resources.flipper_zero_rpc_daemon.fap";

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
    /// Options forwarded to the constructed <see cref="FlipperRpcClient"/>.
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
    /// <param name="ct">Cancellation token for the entire bootstrap operation.</param>
    /// <returns>
    /// A <see cref="FlipperBootstrapResult"/> holding a ready-to-use
    /// <see cref="FlipperRpcClient"/>.  The caller is responsible for disposing it.
    /// </returns>
    /// <exception cref="FlipperBootstrapException">
    /// Thrown when bootstrapping fails (connection error, version mismatch when
    /// <see cref="FlipperBootstrapOptions.AutoInstall"/> is <c>false</c>,
    /// daemon did not start in time, etc.).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled.
    /// </exception>
    public static async Task<FlipperBootstrapResult> BootstrapAsync(
        string systemPortName,
        string daemonPortName,
        FlipperBootstrapOptions options        = default,
        FlipperRpcClientOptions clientOptions  = default,
        IRpcDiagnostics?        diagnostics    = null,
        IProgress<(int Written, int Total)>? progress = null,
        byte[]?                 fapOverride    = null,
        CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        CancellationToken linked = linkedCts.Token;

        // Step 1 — fast path: try the daemon port directly.
        var (directClient, directInfo) = await TryConnectDaemonDirectAsync(
            daemonPortName, clientOptions, diagnostics, linked).ConfigureAwait(false);

        if (directClient is not null && directInfo.HasValue)
        {
            return new FlipperBootstrapResult(directClient, BootstrapAction.AlreadyRunning, directInfo.Value);
        }

        // Step 2 — slow path: interact with the native protobuf RPC.
        byte[] bundledFap = fapOverride ?? LoadBundledFap();
        string bundledMd5 = ComputeMd5(bundledFap);

        await using var native = new FlipperNativeRpcClient(systemPortName);
        try
        {
            await native.OpenAsync(linked).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new FlipperBootstrapException(
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
            throw new FlipperBootstrapException(
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
                throw new FlipperBootstrapException(
                    $"The daemon FAP requires {(action == BootstrapAction.Installed ? "installation" : "an update")} " +
                    $"at '{options.InstallPath}', but AutoInstall is disabled. " +
                    $"Set FlipperBootstrapOptions.AutoInstall = true to allow automatic installation.",
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

        // Step 3 — wait for the daemon to appear on the NDJSON port.
        // Pass the caller's original ct (not the linked/timeout token) so that
        // WaitForDaemonAsync uses only its own DaemonStartTimeout budget.
        // The overall 60-second timeout (linked) may be nearly exhausted by the
        // time native RPC work completes; using it here would cancel the daemon
        // wait before it even starts.
        FlipperRpcClient client = await WaitForDaemonAsync(
            daemonPortName, clientOptions, diagnostics,
            options.DaemonStartTimeout, ct).ConfigureAwait(false);

        DaemonInfoResponse daemonInfo = client.DaemonInfo
            ?? throw new FlipperBootstrapException("Daemon connected but DaemonInfo is null (internal error).");

        return new FlipperBootstrapResult(client, action, daemonInfo);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts a direct connection to the daemon port.  Returns <c>(null, null)</c>
    /// if the daemon is not available or has an incompatible version.
    /// </summary>
    private static async Task<(FlipperRpcClient? Client, DaemonInfoResponse? Info)>
        TryConnectDaemonDirectAsync(
            string daemonPortName,
            FlipperRpcClientOptions clientOptions,
            IRpcDiagnostics? diagnostics,
            CancellationToken ct)
    {
        FlipperRpcClient? client = null;
        try
        {
            var transport = new SerialPortTransport(daemonPortName);
            client = new FlipperRpcClient(transport, clientOptions, diagnostics);

            // Use a short timeout for the direct attempt so we don't block long.
            using var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked   = CancellationTokenSource.CreateLinkedTokenSource(ct, shortCts.Token);

            DaemonInfoResponse info = await client
                .ConnectAsync(MinProtocolVersion, linked.Token)
                .ConfigureAwait(false);

            return (client, info);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Short timeout expired — daemon is not running.
            await DisposeClientSilently(client).ConfigureAwait(false);
            return (null, null);
        }
        catch (OperationCanceledException)
        {
            // Outer ct was cancelled — propagate so the caller stops immediately.
            await DisposeClientSilently(client).ConfigureAwait(false);
            throw;
        }
        catch
        {
            // Port doesn't exist, wrong daemon, version too old, etc.
            await DisposeClientSilently(client).ConfigureAwait(false);
            return (null, null);
        }
    }

    private static async ValueTask DisposeClientSilently(FlipperRpcClient? client)
    {
        if (client is null) return;
        try { await client.DisposeAsync().ConfigureAwait(false); }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Inspects the Flipper SD card and decides what action is needed.
    /// </summary>
    private static async Task<BootstrapAction> DetermineActionAsync(
        FlipperNativeRpcClient native,
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
        FlipperNativeRpcClient native,
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
        FlipperNativeRpcClient native,
        string installPath,
        CancellationToken ct)
    {
        try
        {
            await native.AppStartAsync(installPath, ct).ConfigureAwait(false);
        }
        catch (FlipperNativeRpcException ex)
        {
            throw new FlipperBootstrapException(
                $"Failed to launch the daemon FAP at '{installPath}' via the native RPC. " +
                $"Native RPC status: {ex.Status}. " +
                $"Ensure no other application is running on the Flipper.", ex);
        }
    }

    /// <summary>
    /// Retries connecting to the daemon port until the daemon appears or the
    /// <paramref name="startTimeout"/> elapses.
    /// </summary>
    private static async Task<FlipperRpcClient> WaitForDaemonAsync(
        string daemonPortName,
        FlipperRpcClientOptions clientOptions,
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
            FlipperRpcClient? client = null;
            try
            {
                var transport = new SerialPortTransport(daemonPortName);
                client = new FlipperRpcClient(transport, clientOptions, diagnostics);

                using var attemptCts  = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var attemptLink = CancellationTokenSource.CreateLinkedTokenSource(linked, attemptCts.Token);

                await client.ConnectAsync(MinProtocolVersion, attemptLink.Token).ConfigureAwait(false);
                return client; // success
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await DisposeClientSilently(client).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                await DisposeClientSilently(client).ConfigureAwait(false);
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
            $"The daemon did not appear on '{daemonPortName}' within {startTimeout.TotalSeconds:0}s " +
            $"after being launched. Last error: {lastEx?.Message ?? "(none)"}";
        throw lastEx is not null
            ? new FlipperBootstrapException(timeoutMessage, lastEx)
            : new FlipperBootstrapException(timeoutMessage);
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
    private static string ComputeMd5(byte[] data)
    {
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
