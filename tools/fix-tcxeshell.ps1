# Emergency recovery only: restore TcXaeShell extension folder separation after failed VSPackage attempts.
# This script intentionally modifies Beckhoff-owned TcXaeShell folders and should not be used for normal deployment.
# Must be run as Administrator with TcXaeShell closed
# Historical backup may exist at: %TEMP%\STFormatter_backup

$ErrorActionPreference = "Stop"

$extBase = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions"
$plcExtDir = "$extBase\Beckhoff Automation GmbH\TwinCAT XAE Plc"
$plcRuntimeDir = "C:\TwinCAT\3.1\Components\Plc\Common"
$stfDir = "$extBase\STFormatter"
$stfBghDir = "$extBase\Beckhoff Automation GmbH\STFormatter"
$cacheDir = "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache"

$plcMetadataKeepFiles = @("extension.vsixmanifest", "TwinCAT XAE Plc.ico", "TwinCAT XAE Plc.pkgdef", "TwinCAT XAE Plc.png")

Write-Host "=== TcXaeShell Fix Script ===" -ForegroundColor Cyan
Write-Host ""

# Check TcXaeShell is not running
$tcProcess = Get-Process -Name "TcXaeShell" -ErrorAction SilentlyContinue
if ($tcProcess) {
    Write-Host "ERROR: TcXaeShell is running (PID $($tcProcess.Id)). Please close it first." -ForegroundColor Red
    exit 1
}

# Check admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Must be run as Administrator." -ForegroundColor Red
    exit 1
}

# Step 1: Restore TwinCAT XAE Plc extension folder to metadata-only state.
# The actual PLC binaries are loaded from C:\TwinCAT\3.1\Components\Plc\Common,
# not from Common7\IDE\Extensions\Beckhoff Automation GmbH\TwinCAT XAE Plc.
Write-Host "Step 1: Cleaning TwinCAT XAE Plc extension metadata folder..." -ForegroundColor Yellow
$removedPlcExtFiles = 0
if (Test-Path -LiteralPath $plcExtDir) {
    foreach ($f in (Get-ChildItem $plcExtDir -File)) {
        if ($f.Name -notin $plcMetadataKeepFiles) {
            Remove-Item -LiteralPath $f.FullName -Force
            $removedPlcExtFiles++
        }
    }
    Write-Host "  Removed $removedPlcExtFiles extra files from TwinCAT XAE Plc extension folder" -ForegroundColor Green
} else {
    Write-Host "  TwinCAT XAE Plc extension folder not found: $plcExtDir" -ForegroundColor Yellow
}

# Step 2: Remove duplicate "Beckhoff Automation GmbH\STFormatter" directory
Write-Host "Step 2: Removing duplicate 'Beckhoff Automation GmbH\STFormatter' directory..." -ForegroundColor Yellow
if (Test-Path $stfBghDir) {
    Remove-Item -LiteralPath $stfBghDir -Recurse -Force
    Write-Host "  Removed" -ForegroundColor Green
} else {
    Write-Host "  Not found (already removed)" -ForegroundColor Gray
}

# Step 3: Remove old Extensions\STFormatter deployment directory entirely.
Write-Host "Step 3: Removing old 'Extensions\STFormatter' directory..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $stfDir) {
    Remove-Item -LiteralPath $stfDir -Recurse -Force
    Write-Host "  Removed" -ForegroundColor Green
} else {
    Write-Host "  Not found (already removed)" -ForegroundColor Gray
}

# Step 4: Clear MEF cache
Write-Host "Step 4: Clearing MEF cache..." -ForegroundColor Yellow
if (Test-Path $cacheDir) {
    Get-ChildItem $cacheDir -File | ForEach-Object { Remove-Item $_.FullName -Force }
    Write-Host "  Cleared" -ForegroundColor Green
} else {
    Write-Host "  Cache directory not found" -ForegroundColor Gray
}

# Step 5: Clean up old auto-start state
Write-Host "Step 5: Cleaning auto-start state..." -ForegroundColor Yellow
$runPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$autoVal = Get-ItemProperty -LiteralPath $runPath -Name "STFormatter" -ErrorAction SilentlyContinue
if ($autoVal) {
    Remove-ItemProperty -LiteralPath $runPath -Name "STFormatter" -Force
    Write-Host "  Removed auto-start registry entry" -ForegroundColor Green
}

# Remove stale VSPackage extension registration entries
$stfExtId = "TwinCAT.STFormatter.TcXaeShell.c5d6e7f8-a9b0-4c1d-8e2f-3a4b5c6d7e8f"
$regPaths = @(
    "HKCU:\Software\Beckhoff\TcXaeShell\15.0\ExtensionManager\ExtensionAutoUpdateEnrollment",
    "HKCU:\Software\Beckhoff\TcXaeShell\15.0_IsoShell\ExtensionManager\ExtensionAutoUpdateEnrollment"
)
foreach ($regPath in $regPaths) {
    if (Test-Path $regPath) {
        $props = Get-ItemProperty -LiteralPath $regPath -ErrorAction SilentlyContinue
        $keys = $props.PSObject.Properties.Name | Where-Object { $_ -match "STFormatter" }
        foreach ($key in $keys) {
            Remove-ItemProperty -LiteralPath $regPath -Name $key -Force -ErrorAction SilentlyContinue
            Write-Host "  Removed extension registration: $key" -ForegroundColor Green
        }
    }
}

# Clear extension cache hashes to force TcXaeShell to rebuild extension list
$hashPaths = @(
    "HKCU:\Software\Beckhoff\TcXaeShell\15.0\ExtensionManager\ExtensionsCacheHash",
    "HKCU:\Software\Beckhoff\TcXaeShell\15.0_IsoShell\ExtensionManager\ExtensionsCacheHash"
)
foreach ($hashPath in $hashPaths) {
    if (Test-Path $hashPath) {
        $hashes = Get-ItemProperty -LiteralPath $hashPath -ErrorAction SilentlyContinue
        $hashKeys = $hashes.PSObject.Properties.Name | Where-Object { $_ -match "ExtensionsCacheHash" }
        foreach ($hk in $hashKeys) {
            Remove-ItemProperty -LiteralPath $hashPath -Name $hk -Force -ErrorAction SilentlyContinue
        }
        Write-Host "  Cleared extension cache hash" -ForegroundColor Green
    }
}

# Clear extension cache files
$extCacheDir = "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0_IsoShell\Extensions"
if (Test-Path $extCacheDir) {
    Remove-Item "$extCacheDir\*.cache" -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleared extension cache files" -ForegroundColor Green
}

# Step 6: Verify TwinCAT XAE Plc extension and runtime directories
Write-Host "Step 6: Verifying..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $plcExtDir) {
    $plcExtFiles = Get-ChildItem $plcExtDir -File
    Write-Host "  TwinCAT XAE Plc extension metadata files: $($plcExtFiles.Count)" -ForegroundColor Green
    foreach ($f in $plcExtFiles) { Write-Host "    $($f.Name)" -ForegroundColor Gray }
}

$keyDlls = @("TwinCAT XAE Plc.dll", "TwinCATPlcControl.dll", "Core.dll", "POUObject.dll", "License.dll")
foreach ($dll in $keyDlls) {
    if (Test-Path "$plcRuntimeDir\$dll") {
        Write-Host "    OK: $dll" -ForegroundColor Green
    } else {
        Write-Host "    MISSING from runtime dir: $dll" -ForegroundColor Red
    }
}

if (Test-Path -LiteralPath $stfDir) {
    Write-Host "  WARNING: old STFormatter extension directory still exists: $stfDir" -ForegroundColor Yellow
} else {
    Write-Host "  Old STFormatter extension directory removed" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Fix complete! ===" -ForegroundColor Cyan
Write-Host "Please start TcXaeShell and try adding a PLC project." -ForegroundColor White
