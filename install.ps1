#!/usr/bin/env pwsh
# Installation script for TwinCAT ST Formatter
# Usage: .\install.ps1 [-CLI] [-VSIX] [-Both]

param(
    [switch]$CLI = $false,
    [switch]$VSIX = $false,
    [switch]$Both = $false
)

$ErrorActionPreference = "Stop"

# Default to both if nothing specified
if (-not $CLI -and -not $VSIX -and -not $Both) {
    $Both = $true
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "TwinCAT ST Formatter Installer" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Check prerequisites
$hasDotNet = $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $hasDotNet) {
    Write-Error ".NET SDK not found. Please install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

$dotnetVersion = (dotnet --version)
Write-Host "Found .NET SDK: $dotnetVersion" -ForegroundColor Green

# Build solution
Write-Host "`nBuilding solution..." -ForegroundColor Yellow
$buildResult = dotnet build TwinCAT.STFormatter.sln -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

# Install CLI
if ($CLI -or $Both) {
    Write-Host "`nInstalling CLI tool..." -ForegroundColor Yellow
    
    # Uninstall if exists
    $existingTool = dotnet tool list --global | Select-String "STFormatter.CLI"
    if ($existingTool) {
        Write-Host "Removing existing installation..." -ForegroundColor Gray
        dotnet tool uninstall --global STFormatter.CLI 2>&1 | Out-Null
    }
    
    # Pack and install
    $cliProject = "src/STFormatter.CLI/STFormatter.CLI.csproj"
    dotnet pack $cliProject -c Release --no-build | Out-Null
    
    $nupkg = Get-ChildItem -Path "src/STFormatter.CLI/bin/Release" -Filter "*.nupkg" | Select-Object -First 1
    if ($nupkg) {
        dotnet tool install --global --add-source "src/STFormatter.CLI/bin/Release" STFormatter.CLI
        Write-Host "CLI tool installed successfully!" -ForegroundColor Green
        Write-Host "Usage: stfmt --help" -ForegroundColor Gray
    } else {
        Write-Warning "Could not find NuGet package for CLI tool"
    }
}

# Install VSIX
if ($VSIX -or $Both) {
    Write-Host "`nInstalling VSIX extension..." -ForegroundColor Yellow
    
    $vsixPath = Get-ChildItem -Path "src/STFormatter.VSIX/bin/Release" -Filter "*.vsix" -Recurse | Select-Object -First 1
    
    if (-not $vsixPath) {
        Write-Warning "VSIX file not found. Building..."
        # Try to find MSBuild
        $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vsWhere) {
            $vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath
            if ($vsPath) {
                $msBuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
                & $msBuild "TwinCAT.STFormatter.sln" /t:STFormatter.VSIX /p:Configuration=Release /p:DeployExtension=false | Out-Null
                $vsixPath = Get-ChildItem -Path "src/STFormatter.VSIX/bin/Release" -Filter "*.vsix" -Recurse | Select-Object -First 1
            }
        }
    }
    
    if ($vsixPath) {
        Write-Host "Found VSIX: $($vsixPath.FullName)" -ForegroundColor Green
        
        # Try to install via VSIXInstaller
        $vsixInstaller = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe"
        if (-not (Test-Path $vsixInstaller)) {
            $vsixInstaller = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\VSIXInstaller.exe"
        }
        if (-not (Test-Path $vsixInstaller)) {
            $vsixInstaller = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\VSIXInstaller.exe"
        }
        
        if (Test-Path $vsixInstaller) {
            Write-Host "Installing via VSIXInstaller..." -ForegroundColor Gray
            & $vsixInstaller /q "$($vsixPath.FullName)"
            Write-Host "VSIX extension installed successfully!" -ForegroundColor Green
            Write-Host "Please restart Visual Studio 2022" -ForegroundColor Yellow
        } else {
            Write-Host "VSIXInstaller not found. Please install manually:" -ForegroundColor Yellow
            Write-Host "  Double-click: $($vsixPath.FullName)" -ForegroundColor White
        }
    } else {
        Write-Warning "Could not build VSIX. Visual Studio 2022 with SDK workload may be required."
    }
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "Installation Complete!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

if ($CLI -or $Both) {
    Write-Host "`nCLI Tool:" -ForegroundColor Cyan
    Write-Host "  stfmt --help          Show help" -ForegroundColor White
    Write-Host "  stfmt format file.st  Format a file" -ForegroundColor White
    Write-Host "  stfmt init .          Create .editorconfig" -ForegroundColor White
}

if ($VSIX -or $Both) {
    Write-Host "`nVSIX Extension:" -ForegroundColor Cyan
    Write-Host "  Ctrl+K, Ctrl+D        Format Document" -ForegroundColor White
    Write-Host "  Ctrl+K, Ctrl+F        Format Selection" -ForegroundColor White
    Write-Host "  Tools > Options > TwinCAT > ST Formatter  Settings" -ForegroundColor White
}

Write-Host "`nPress any key to continue..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
