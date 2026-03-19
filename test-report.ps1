#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run integration tests against a connected Flipper Zero and open an HTML report.

.DESCRIPTION
    Builds the solution, runs the integration test project with HTML and TRX
    loggers, then opens the resulting HTML report in the default browser.

    Requires FLIPPER_PORT to be set before invoking this script, e.g.:
        $env:FLIPPER_PORT = "COM3"
        .\test-report.ps1

    Optional -Category filter limits which tests are run:
        .\test-report.ps1 -Category Hardware   # automated tests only
        .\test-report.ps1 -Category Manual     # tests requiring physical interaction
        .\test-report.ps1                      # all categories (default)

.PARAMETER Category
    Trait category filter: "Hardware", "Manual", or "" (all). Default: "".
#>
param(
    [string]$Category = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Validate environment
# ---------------------------------------------------------------------------
if (-not $env:FLIPPER_PORT) {
    Write-Error "FLIPPER_PORT is not set. Export it before running this script:`n  `$env:FLIPPER_PORT = 'COM3'"
    exit 1
}

Write-Host "Flipper port : $env:FLIPPER_PORT"
Write-Host "Category     : $(if ($Category) { $Category } else { '(all)' })"
Write-Host ""

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$RepoRoot   = $PSScriptRoot
$ResultsDir = Join-Path $RepoRoot "test-results"
$Timestamp  = Get-Date -Format "yyyyMMdd_HHmmss"
$HtmlFile   = Join-Path $ResultsDir "report_$Timestamp.html"
$TrxFile    = Join-Path $ResultsDir "report_$Timestamp.trx"

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
Write-Host "Building solution..."
dotnet build "$RepoRoot" --no-incremental -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ---------------------------------------------------------------------------
# Compose filter expression
# ---------------------------------------------------------------------------
$FilterParts = @("FullyQualifiedName~IntegrationTests")
if ($Category) {
    $FilterParts += "Category=$Category"
}
$Filter = $FilterParts -join "&"

# ---------------------------------------------------------------------------
# Run tests
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Running tests (filter: $Filter)..."
Write-Host ""

$LogArgs = @(
    "--logger", "html;LogFileName=$HtmlFile",
    "--logger", "trx;LogFileName=$TrxFile",
    "--results-directory", $ResultsDir,
    "--filter", $Filter,
    "--no-build"
)

dotnet test "$RepoRoot" @LogArgs
$ExitCode = $LASTEXITCODE

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Results written to:"
Write-Host "  HTML : $HtmlFile"
Write-Host "  TRX  : $TrxFile"

if (Test-Path $HtmlFile) {
    Write-Host ""
    Write-Host "Opening report in default browser..."
    Start-Process $HtmlFile
}

exit $ExitCode
