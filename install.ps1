#!/usr/bin/env pwsh
# Local CLI installation script for TwinCAT ST Formatter.
# For TcXaeShell Host installation, use installer\build-installer.ps1 or deploy.bat.

param(
    [switch]$CLI = $true
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "TwinCAT ST Formatter CLI Installer" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "Found .NET SDK: $dotnetVersion" -ForegroundColor Green

Write-Host "`nBuilding solution..." -ForegroundColor Yellow
dotnet build TwinCAT.STFormatter.sln -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

Write-Host "`nInstalling CLI tool..." -ForegroundColor Yellow

$existingTool = dotnet tool list --global | Select-String "STFormatter.CLI"
if ($existingTool) {
    Write-Host "Removing existing CLI installation..." -ForegroundColor Gray
    dotnet tool uninstall --global STFormatter.CLI | Out-Null
}

$cliProject = "src/STFormatter.CLI/STFormatter.CLI.csproj"
dotnet pack $cliProject -c Release --no-build | Out-Null

$nupkg = Get-ChildItem -Path "src/STFormatter.CLI/bin/Release" -Filter "*.nupkg" | Select-Object -First 1
if (-not $nupkg) {
    Write-Error "Could not find NuGet package for CLI tool."
    exit 1
}

dotnet tool install --global --add-source "src/STFormatter.CLI/bin/Release" STFormatter.CLI

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "CLI Installation Complete" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "stfmt --help          Show help" -ForegroundColor White
Write-Host "stfmt format file.st  Format a file" -ForegroundColor White
Write-Host "stfmt init .          Create .editorconfig" -ForegroundColor White
