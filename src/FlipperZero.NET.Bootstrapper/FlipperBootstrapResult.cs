using FlipperZero.NET.Commands.System;

namespace FlipperZero.NET.Bootstrapper;

/// <summary>
/// What the bootstrapper did during a successful <see cref="FlipperBootstrapper.BootstrapAsync"/> call.
/// </summary>
public enum BootstrapAction
{
    /// <summary>
    /// The daemon was already running on CDC interface 1 with a compatible version.
    /// No interaction with the native RPC was required.
    /// </summary>
    AlreadyRunning,

    /// <summary>
    /// The FAP was missing from the SD card; the bootstrapper installed it and launched it.
    /// </summary>
    Installed,

    /// <summary>
    /// The FAP was present but its MD5 differed from the bundled version;
    /// the bootstrapper replaced it and relaunched it.
    /// </summary>
    Updated,

    /// <summary>
    /// The FAP was present and up-to-date but was not running;
    /// the bootstrapper launched it without replacing it.
    /// </summary>
    Launched,
}

/// <summary>
/// The result of a successful <see cref="FlipperBootstrapper.BootstrapAsync"/> call.
/// Owns the <see cref="Client"/> and disposes it when this object is disposed.
/// </summary>
public sealed class FlipperBootstrapResult : IAsyncDisposable
{
    /// <summary>
    /// A fully connected and negotiated <see cref="FlipperRpcClient"/> ready for use.
    /// </summary>
    public FlipperRpcClient Client { get; }

    /// <summary>What the bootstrapper did to arrive at a running daemon.</summary>
    public BootstrapAction Action { get; }

    /// <summary>The daemon's capability descriptor, as returned by <c>daemon_info</c>.</summary>
    public DaemonInfoResponse DaemonInfo { get; }

    internal FlipperBootstrapResult(
        FlipperRpcClient client,
        BootstrapAction action,
        DaemonInfoResponse daemonInfo)
    {
        Client     = client;
        Action     = action;
        DaemonInfo = daemonInfo;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

/// <summary>
/// Thrown when the bootstrapper cannot complete successfully.
/// </summary>
public sealed class FlipperBootstrapException : Exception
{
    /// <summary>
    /// The action that would have been taken if <see cref="FlipperBootstrapOptions.AutoInstall"/>
    /// had been <c>true</c>, or <c>null</c> if the failure is unrelated to install policy.
    /// </summary>
    public BootstrapAction? RequiredAction { get; }

    internal FlipperBootstrapException(string message, BootstrapAction? requiredAction = null)
        : base(message)
    {
        RequiredAction = requiredAction;
    }

    internal FlipperBootstrapException(string message, Exception inner, BootstrapAction? requiredAction = null)
        : base(message, inner)
    {
        RequiredAction = requiredAction;
    }
}
