# Deploy the STBud Host to C:\Program Files (x86)\STBud (requires elevation).
#
# Fixes the chronic silent-stale-deploy problem: the old batch "succeeded" while the
# running Host kept files locked, leaving old DLLs in place. This script stops the Host,
# copies, then VERIFIES every file (timestamp + length) and fails loudly on any mismatch.
#
# Usage:
#   deploy.ps1               # net48 (default)
#   deploy.ps1 net462        # older machines
#   deploy.ps1 -NoPause      # no interactive pause (automation)
param(
    [string]$Tfm = "net48",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $repo "src\STFormatter.Host\bin\Debug\$Tfm"
$cliSrc = Join-Path $repo "src\STBud.Git.CLI\bin\Debug\$Tfm"
$dst = "C:\Program Files (x86)\STBud"

function Finish([int]$code) {
    if (-not $NoPause -and [Environment]::UserInteractive) { Read-Host "Press Enter to close" | Out-Null }
    exit $code
}

# Elevate self if needed (keeps the console open so errors stay visible).
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Elevation required - relaunching as administrator..."
    $argList = @("-ExecutionPolicy", "RemoteSigned", "-File", $MyInvocation.MyCommand.Path, $Tfm)
    if ($NoPause) { $argList += "-NoPause" }
    $proc = Start-Process -FilePath "powershell" -ArgumentList $argList -Verb RunAs -Wait -PassThru
    exit $proc.ExitCode
}

Write-Host "Deploying STBud Host ($Tfm) -> $dst"

$files = @(
    "STFormatter.Host.exe",
    "STFormatter.Host.exe.config",
    "STFormatter.Core.dll",
    "STFormatter.UI.dll",
    "STBud.Git.dll",
    "STBud.Git.Editor.dll",
    "Microsoft.VisualStudio.Interop.dll"
)
$optional = @(
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Buffers.dll",
    "System.Collections.Immutable.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encodings.Web.dll",
    "System.Text.Json.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll"
)

if (-not (Test-Path (Join-Path $src "STFormatter.Host.exe"))) {
    Write-Host "ERROR: build output not found at $src - run 'dotnet build src\STFormatter.Host\STFormatter.Host.csproj' first." -ForegroundColor Red
    Finish 1
}

# 1. Stop the Host so nothing holds the target files. Remember whether it ran.
$hostWasRunning = $false
$hostProc = Get-Process STFormatter.Host -ErrorAction SilentlyContinue
if ($hostProc) {
    $hostWasRunning = $true
    Write-Host "Stopping running Host (PID $($hostProc.Id -join ', '))..."
    $hostProc | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force $dst | Out-Null }

# 2. Copy.
$toVerify = @()
foreach ($f in $files) {
    Copy-Item (Join-Path $src $f) (Join-Path $dst $f) -Force
    $toVerify += $f
}
foreach ($f in $optional) {
    $s = Join-Path $src $f
    if (Test-Path $s) { Copy-Item $s (Join-Path $dst $f) -Force; $toVerify += $f }
}
$stgit = Join-Path $cliSrc "stgit.exe"
if (Test-Path $stgit) {
    Copy-Item $stgit (Join-Path $dst "stgit.exe") -Force
    Write-Host "Copied stgit.exe"
} else {
    Write-Host "NOTE: stgit.exe not found at $cliSrc - skipped (build STBud.Git.CLI to include it)."
}

# 3. Verify: every copied file must match the source timestamp and length.
$bad = @()
foreach ($f in $toVerify) {
    $s = Get-Item (Join-Path $src $f)
    $d = Get-Item (Join-Path $dst $f) -ErrorAction SilentlyContinue
    if ($null -eq $d -or $d.Length -ne $s.Length -or $d.LastWriteTimeUtc -ne $s.LastWriteTimeUtc) {
        $bad += $f
    }
}
if ($bad.Count -gt 0) {
    Write-Host "ERROR: deployment verification FAILED for: $($bad -join ', ')" -ForegroundColor Red
    Write-Host "The deployed files are stale. Close anything locking them and retry." -ForegroundColor Red
    Finish 1
}
Write-Host "Verified $($toVerify.Count) file(s) - deployed build is current." -ForegroundColor Green

# 4. Restart the Host if it was running before. Launch via explorer.exe so the Host
# starts UN-elevated even though this script runs elevated — an elevated Host cannot
# see the non-elevated TcXaeShell's DTE in the ROT (elevation mismatch kills the
# whole integration: no context menu, no Git, endless "no ROT moniker" scans).
if ($hostWasRunning) {
    Start-Process explorer.exe -ArgumentList ('"' + (Join-Path $dst "STFormatter.Host.exe") + '"')
    Write-Host "Host restarted (un-elevated)."
}

Write-Host "Deployment complete ($Tfm)."
Finish 0
