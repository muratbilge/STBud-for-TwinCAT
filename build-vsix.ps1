#!/usr/bin/env pwsh
# Build script for TwinCAT ST Formatter VSIX

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "TwinCAT ST Formatter Build Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Version: $Version"
Write-Host ""

# Verify prerequisites
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vsWhere)) {
    Write-Error "Visual Studio not found. Please install Visual Studio 2022 with the SDK workload."
    exit 1
}

$vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath
if (-not $vsPath) {
    Write-Warning ""
    Write-Warning "Visual Studio Extension Development workload not found!"
    Write-Warning ""
    Write-Warning "To build the VSIX extension, you need:"
    Write-Warning "  1. Visual Studio 2022 (Community/Professional/Enterprise)"
    Write-Warning "  2. 'Visual Studio extension development' workload"
    Write-Warning ""
    Write-Warning "Install it via:"
    Write-Warning "  Visual Studio Installer > Modify > Workloads > 'Visual Studio extension development'"
    Write-Warning ""
    Write-Warning "However, the CLI tool can still be built without Visual Studio!"
    Write-Warning ""
    
    $continue = Read-Host "Continue building CLI only? (Y/n)"
    if ($continue -eq '' -or $continue.ToLower() -eq 'y') {
        Write-Host "`nSkipping VSIX build. Building CLI only..." -ForegroundColor Yellow
        $skipVSIX = $true
    } else {
        exit 1
    }
}

Write-Host "Found Visual Studio at: $vsPath" -ForegroundColor Green

# Clean
Write-Host "`nCleaning solution..." -ForegroundColor Yellow
$msBuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
& $msBuild "TwinCAT.STFormatter.sln" /t:Clean /p:Configuration=$Configuration /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Clean failed!"
    exit 1
}

# Build
Write-Host "`nBuilding solution..." -ForegroundColor Yellow
& $msBuild "TwinCAT.STFormatter.sln" /t:Build /p:Configuration=$Configuration /v:minimal /p:DeployExtension=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Create output directory
$outputDir = "publish"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# Find and copy VSIX
$vsixPath = Get-ChildItem -Path "src\STFormatter.VSIX\bin\$Configuration" -Filter "*.vsix" -Recurse | Select-Object -First 1
if (-not $vsixPath) {
    $vsixPath = Get-ChildItem -Path "src\STFormatter.VSIX" -Filter "*.vsix" | Select-Object -First 1
}
if (-not $vsixPath) {
    Write-Error "VSIX file not found after build!"
    exit 1
}

$outputFile = Join-Path $outputDir "TwinCAT.STFormatter.$Version.vsix"
Copy-Item $vsixPath.FullName $outputFile -Force

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Output: $outputFile" -ForegroundColor White
Write-Host "`nTo install, double-click the .vsix file or run:" -ForegroundColor Gray
Write-Host "  .\publish\TwinCAT.STFormatter.$Version.vsix" -ForegroundColor Gray
