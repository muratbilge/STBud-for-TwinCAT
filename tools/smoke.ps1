# Smoke test: build, unit tests, and formatter regression over the sample corpus.
# Usage: powershell -File tools\smoke.ps1
# Exit code 0 = all green, 1 = something failed.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failed = @()

function Invoke-Step {
    param([string]$Name, [string[]]$CommandArgs)
    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & dotnet @CommandArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name" -ForegroundColor Red
        $script:failed += $Name
    }
}

Invoke-Step "Build solution" @('build', "$root\TwinCAT.STFormatter.sln", '--nologo')
Invoke-Step "Unit tests" @('test', "$root\tests\STFormatter.Core.Tests", '--no-build', '--nologo')

# Dry-run the formatter over the sample corpus; batch exits 1 if any file fails to format.
$cli = "$root\src\STFormatter.CLI"
Invoke-Step "Format samples (plain ST)" @('run', '--project', $cli, '--no-build', '--', 'batch', "$root\samples\SampleSTFiles", '--recursive', '--twincat', '--dry-run')
Invoke-Step "Format samples (real TwinCAT files)" @('run', '--project', $cli, '--no-build', '--', 'batch', "$root\samples\RealTcFiles", '--recursive', '--twincat', '--dry-run')

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "Smoke test FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "Smoke test passed." -ForegroundColor Green
exit 0
