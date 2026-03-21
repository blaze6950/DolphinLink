#!/usr/bin/env dotnet-script
// deploy-daemon.csx
//
// Builds the C daemon FAP then uses FlipperBootstrapper.BootstrapAsync to
// upload and launch it on the Flipper Zero — no qFlipper needed.
//
// Prerequisites
// -------------
//   1. Run `dotnet tool restore` once after cloning the repo.
//   2. Run `dotnet build` once so the DLLs loaded by this script exist.
//   3. Flipper Zero connected over USB; no other app (e.g. qFlipper) on the ports.
//   4. ufbt + Python on PATH (not needed with --no-build).
//
// Usage
// -----
//   dotnet script deploy-daemon.csx                           # build C + upload + launch
//   dotnet script deploy-daemon.csx -- --no-build             # skip C build, re-upload existing FAP
//   dotnet script deploy-daemon.csx -- --system COM5 --daemon COM6
//   dotnet script deploy-daemon.csx -- --no-build --system COM5 --daemon COM6
//
// How it works
// ------------
// The FAP bytes are read directly from the ufbt dist/ folder on disk and
// passed to BootstrapAsync via fapOverride.  This bypasses the FAP that is
// embedded inside the Bootstrapper DLL, so there is no need to rebuild the
// DLL between C daemon iterations.

// NOTE: #r directives are resolved at parse time — the DLL paths must exist
//       before the first run.  Run `dotnet build` once after cloning.
#r "nuget: Google.Protobuf, 3.28.3"
#r "nuget: System.IO.Ports, 8.0.0"
#r "src/FlipperZero.NET.Client/bin/Debug/net8.0/FlipperZero.NET.Client.dll"
#r "src/FlipperZero.NET.Bootstrapper/bin/Debug/net8.0/FlipperZero.NET.Bootstrapper.dll"

#nullable enable

using System.Diagnostics;
using FlipperZero.NET.Bootstrapper;

// ---------------------------------------------------------------------------
// Parse arguments
// ---------------------------------------------------------------------------

bool   noBuild = Args.Contains("--no-build");
string system  = OptionValue("--system") ?? "COM3";
string daemon  = OptionValue("--daemon") ?? "COM4";

string? OptionValue(string flag)
{
    int idx = Args.IndexOf(flag);
    return (idx >= 0 && idx + 1 < Args.Count) ? Args[idx + 1] : null;
}

// ---------------------------------------------------------------------------
// Resolve paths
// ---------------------------------------------------------------------------

string repoRoot  = Directory.GetCurrentDirectory();
string daemonSrc = Path.Combine(repoRoot, "src", "FlipperZeroRpcDaemon");
string fapDist   = Path.Combine(daemonSrc, "dist", "flipper_zero_rpc_daemon.fap");

// ---------------------------------------------------------------------------
// Step 1 — (optionally) build the C daemon
// ---------------------------------------------------------------------------

if (!noBuild)
{
    Console.WriteLine("==> Building C daemon (ufbt)...");
    int rc = RunProcess("python", "-m ufbt", daemonSrc);
    if (rc != 0)
    {
        Console.Error.WriteLine($"ufbt failed (exit code {rc}). Aborting.");
        Environment.Exit(rc);
    }
    Console.WriteLine();
}
else
{
    Console.WriteLine("==> Skipping C build (--no-build).");
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
// Step 2 — read the freshly-built FAP from disk
// ---------------------------------------------------------------------------

if (!File.Exists(fapDist))
{
    Console.Error.WriteLine($"FAP not found at: {fapDist}");
    Console.Error.WriteLine("Run without --no-build to build it first.");
    Environment.Exit(1);
    return;
}

byte[] fapBytes = File.ReadAllBytes(fapDist);
Console.WriteLine($"==> FAP loaded from dist/  ({fapBytes.Length:N0} bytes)");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Step 3 — deploy via FlipperBootstrapper.BootstrapAsync
// ---------------------------------------------------------------------------

Console.WriteLine("==> Deploying daemon...");
Console.WriteLine($"    System port : {system}");
Console.WriteLine($"    Daemon port : {daemon}");
Console.WriteLine();

var options = new FlipperBootstrapOptions { AutoInstall = true };

int lastPct = -1;
var progress = new Progress<(int Written, int Total)>(r =>
{
    int pct = r.Total > 0 ? (int)(100.0 * r.Written / r.Total) : 0;
    if (pct != lastPct)
    {
        lastPct = pct;
        int bars = pct / 5;
        string bar = new string('#', bars) + new string('-', 20 - bars);
        Console.Write($"\r    Uploading FAP  [{bar}] {pct,3}%  ({r.Written}/{r.Total} bytes)  ");
    }
});

FlipperBootstrapResult result;
try
{
    result = await FlipperBootstrapper.BootstrapAsync(
        system, daemon,
        options:     options,
        progress:    progress,
        fapOverride: fapBytes);
}
catch (FlipperBootstrapException ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
    Environment.Exit(1);
    return;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Unexpected error: {ex}");
    Environment.Exit(1);
    return;
}

if (lastPct >= 0) Console.WriteLine();
Console.WriteLine();

string actionLabel = result.Action switch
{
    BootstrapAction.AlreadyRunning => "already running — no install needed",
    BootstrapAction.Installed      => "installed and launched",
    BootstrapAction.Updated        => "updated and relaunched",
    BootstrapAction.Launched       => "launched (FAP already up-to-date on SD card)",
    _                              => result.Action.ToString()
};

Console.WriteLine($"==> Done. Daemon {actionLabel}.");
Console.WriteLine($"    Name    : {result.DaemonInfo.Name}");
Console.WriteLine($"    Version : {result.DaemonInfo.Version}");
Console.WriteLine($"    Port    : {daemon}");

await result.DisposeAsync();

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

static int RunProcess(string exe, string args, string workDir)
{
    var psi = new ProcessStartInfo(exe, args)
    {
        WorkingDirectory = workDir,
        UseShellExecute  = false,
    };
    using var proc = Process.Start(psi)!;
    proc.WaitForExit();
    return proc.ExitCode;
}
