namespace DolphinLink.Bootstrapper;

/// <summary>
/// Configuration options for <see cref="Bootstrapper"/>.
///
/// All properties have safe defaults: auto-install is enabled, the standard FAP
/// install path is used, and the overall bootstrap timeout is 60 seconds.
/// </summary>
public readonly record struct BootstrapOptions
{
    // Backing fields let default(BootstrapOptions) resolve to sensible values.
    private readonly bool? _autoInstall;
    private readonly string? _installPath;
    private readonly TimeSpan? _timeout;
    private readonly TimeSpan? _daemonStartTimeout;

    /// <summary>
    /// When <c>true</c> (the default), the bootstrapper will automatically install
    /// or update the daemon FAP if the installed version does not match the one
    /// embedded in this package.
    ///
    /// When <c>false</c>, an exception is thrown instead of installing:
    /// <see cref="BootstrapException"/> describes what action would have
    /// been taken and which ports were involved.
    /// </summary>
    public bool AutoInstall
    {
        get => _autoInstall ?? true;
        init => _autoInstall = value;
    }

    /// <summary>
    /// Path on the Flipper SD card where the FAP will be installed.
    ///
    /// Defaults to <c>/ext/apps/Tools/dolphin_link_rpc_daemon.fap</c>.
    /// Intermediate directories are created automatically if they do not exist.
    /// </summary>
    public string InstallPath
    {
        get => _installPath ?? "/ext/apps/Tools/dolphin_link_rpc_daemon.fap";
        init => _installPath = value;
    }

    /// <summary>
    /// Maximum time allowed for the entire bootstrap sequence (native RPC
    /// interaction + FAP upload + daemon startup + NDJSON handshake).
    ///
    /// Defaults to 60 seconds.
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout ?? TimeSpan.FromSeconds(60);
        init => _timeout = value;
    }

    /// <summary>
    /// Time to wait for the daemon to appear on the NDJSON port after launching it
    /// via the native RPC <c>app_start</c>.  Retries every 500 ms within this window.
    ///
    /// Defaults to 10 seconds.
    /// </summary>
    public TimeSpan DaemonStartTimeout
    {
        get => _daemonStartTimeout ?? TimeSpan.FromSeconds(10);
        init => _daemonStartTimeout = value;
    }
}
