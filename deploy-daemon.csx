#!/usr/bin/env dotnet-script
// deploy-daemon.csx
//
// Builds the C daemon FAP, embeds it into the Bootstrapper assembly, then uses
// FlipperBootstrapper.BootstrapAsync to upload and launch it on the Flipper Zero.
//
// Prerequisites
// -------------
//   1. Run `dotnet tool restore` once after cloning the repo.
//   2. Flipper Zero connected over USB with no other app (e.g. qFlipper) on the ports.
//   3. ufbt + Python on PATH (only needed unless --no-build is passed).
//
// Usage
// -----
//   dotnet script deploy-daemon.csx                           # build C + upload + launch
//   dotnet script deploy-daemon.csx -- --no-build             # skip C build, re-upload existing FAP
//   dotnet script deploy-daemon.csx -- --system COM5 --daemon COM6
//   dotnet script deploy-daemon.csx -- --no-build --system COM5 --daemon COM6

// NOTE: #r directives are resolved at parse time and must appear before any code.
//       The DLL paths below point to the Debug build output.  Run `dotnet build`
//       at least once before running this script for the first time.
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
// Resolve paths relative to the script file
// ---------------------------------------------------------------------------

string repoRoot        = Directory.GetCurrentDirectory();
string daemonSrc       = Path.Combine(repoRoot, "src", "FlipperZeroRpcDaemon");
string fapDist         = Path.Combine(daemonSrc, "dist", "flipper_zero_rpc_daemon.fap");
string bootstrapperSrc = Path.Combine(repoRoot, "src", "FlipperZero.NET.Bootstrapper");
string fapResource     = Path.Combine(bootstrapperSrc, "Resources", "flipper_zero_rpc_daemon.fap");
string bootstrapperCsproj = Path.Combine(bootstrapperSrc, "FlipperZero.NET.Bootstrapper.csproj");

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

    Console.WriteLine("==> Copying FAP to Bootstrapper resources...");
    File.Copy(fapDist, fapResource, overwrite: true);
    Console.WriteLine($"    {Path.GetRelativePath(repoRoot, fapDist)}");
    Console.WriteLine($"    -> {Path.GetRelativePath(repoRoot, fapResource)}");
}
else
{
    Console.WriteLine("==> Skipping C build (--no-build).");
}

// ---------------------------------------------------------------------------
// Step 2 — (re)build the .NET Bootstrapper so the embedded FAP is current
//           and the DLLs loaded above are up-to-date
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("==> Building FlipperZero.NET.Bootstrapper...");
{
    int rc = RunProcess("dotnet", $"build \"{bootstrapperCsproj}\" --nologo -v q", repoRoot);
    if (rc != 0)
    {
        Console.Error.WriteLine($"dotnet build failed (exit code {rc}). Aborting.");
        Environment.Exit(rc);
    }
}

// ---------------------------------------------------------------------------
// Step 3 — deploy via FlipperBootstrapper.BootstrapAsync
// ---------------------------------------------------------------------------

Console.WriteLine();
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
        options:  options,
        progress: progress);
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

// Clear the progress line if we printed one.
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
