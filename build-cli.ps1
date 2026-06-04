#!/usr/bin/env pwsh
# Build script for STBud for TwinCAT - CLI Tool
# Creates a portable publish folder with all dependencies

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "STBud for TwinCAT - CLI Build" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Check .NET
$hasDotNet = $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $hasDotNet) {
    Write-Error ".NET SDK not found. Please install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

Write-Host "Found .NET: $(dotnet --version)" -ForegroundColor Green

# Publish (creates self-contained output with all dependencies)
Write-Host "`nPublishing CLI tool..." -ForegroundColor Yellow

$outputDir = "publish"

# Clean and create output directory
if (Test-Path $outputDir) {
    Remove-Item -Path $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# Publish the CLI with all dependencies
dotnet publish src/STFormatter.CLI/STFormatter.CLI.csproj `
    -c Release `
    -o $outputDir `
    --self-contained false `
    /p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

# Check what was created
$files = Get-ChildItem $outputDir
Write-Host "`nPublished files:" -ForegroundColor Gray
$files | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Gray }

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "CLI Build Complete!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Output folder: $outputDir" -ForegroundColor White
Write-Host "`nQuick start:" -ForegroundColor Cyan
Write-Host "  cd publish" -ForegroundColor White
Write-Host "  .\STFormatter.CLI.exe --help" -ForegroundColor White
Write-Host "  .\STFormatter.CLI.exe format ..\samples\SampleSTFiles\Sample1.st --dry-run" -ForegroundColor White
Write-Host "`nOr use from repo root:" -ForegroundColor Cyan
Write-Host "  .\publish\STFormatter.CLI.exe format samples\SampleSTFiles\Sample1.st" -ForegroundColor White
Write-Host "`nTo install globally:" -ForegroundColor Cyan
Write-Host "  dotnet tool install --global --add-source src/STFormatter.CLI/bin/Release STFormatter.CLI" -ForegroundColor White
