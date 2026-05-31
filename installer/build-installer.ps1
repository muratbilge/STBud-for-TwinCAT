#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build script for TwinCAT ST Formatter installer.

.DESCRIPTION
Builds all binary targets and creates an Inno Setup installer
that includes the CLI, VSIX extension, and TcXaeShell Host.

Prerequisites:
  - .NET 8 SDK (for CLI)
  - .NET Framework 4.6.2 + 4.8 targeting packs (for Host)
  - Visual Studio 2022 with VSSDK workload (for VSIX)
  - Inno Setup (ISCC.exe on PATH) - https://jrsoftware.org/isdl.php

.PARAMETER SkipCLI
Skip building the CLI tool.

.PARAMETER SkipVSIX
Skip building the VS 2022 extension.

.PARAMETER SkipHost
Skip building the TcXaeShell Host.

.PARAMETER SkipInstaller
Skip creating the installer (just build binaries).

.PARAMETER Configuration
Build configuration (Debug or Release). Default: Debug.

.PARAMETER Version
Version number for the installer. Default: 1.0.0.

.EXAMPLE
.\build-installer.ps1
Build everything and create installer.

.EXAMPLE
.\build-installer.ps1 -SkipVSIX
Build CLI and Host only (skip VSIX, e.g. no VS SDK installed).

.EXAMPLE
.\build-installer.ps1 -SkipInstaller -Configuration Release
Build all binaries but don't create the installer.
#>

param(
    [switch]$SkipCLI = $false,
    [switch]$SkipVSIX = $false,
    [switch]$SkipHost = $false,
    [switch]$SkipInstaller = $false,
    [string]$Configuration = "Debug",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$FilesDir = Join-Path $PSScriptRoot "files"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "TwinCAT ST Formatter Installer Builder" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Version:        $Version" -ForegroundColor Gray
Write-Host "Configuration:  $Configuration" -ForegroundColor Gray
Write-Host "Root:           $RootDir" -ForegroundColor Gray
Write-Host ""

# Clean and create output directories
if (Test-Path $FilesDir) {
    Remove-Item -Path $FilesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $FilesDir -Force | Out-Null

$buildErrors = @()

# =============================================
# 1. Build CLI (net8.0, framework-dependent)
# =============================================
if (-not $SkipCLI) {
    Write-Host "[1/4] Building CLI tool (net8.0)..." -ForegroundColor Yellow

    $cliDir = Join-Path $FilesDir "cli"
    New-Item -ItemType Directory -Path $cliDir -Force | Out-Null

    dotnet publish "$RootDir\src\STFormatter.CLI\STFormatter.CLI.csproj" `
        -c $Configuration `
        -o $cliDir `
        --self-contained false `
        /p:PublishSingleFile=false

    if ($LASTEXITCODE -ne 0) {
        Write-Error "CLI build failed!"
        $buildErrors += "CLI"
    } else {
        $cliFileCount = (Get-ChildItem $cliDir -File).Count
        Write-Host "  CLI: $cliFileCount files published to files\cli\" -ForegroundColor Green
    }
} else {
    Write-Host "[1/4] Skipping CLI build" -ForegroundColor Gray
}

# =============================================
# 2. Build Host - net48 (for .NET 4.8+, current TcXaeShell)
# =============================================
if (-not $SkipHost) {
    Write-Host "[2/4] Building Host (net48, x86)..." -ForegroundColor Yellow

    $hostNet48Dir = Join-Path $FilesDir "host-net48"
    New-Item -ItemType Directory -Path $hostNet48Dir -Force | Out-Null

    dotnet build "$RootDir\src\STFormatter.Host\STFormatter.Host.csproj" `
        -c $Configuration `
        -p:TargetFramework=net48

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Host net48 build failed!"
        $buildErrors += "Host-net48"
    } else {
        $srcDir = Join-Path $RootDir "src\STFormatter.Host\bin\$Configuration\net48"
        Copy-Item "$srcDir\STFormatter.Host.exe" $hostNet48Dir -Force
        Copy-Item "$srcDir\STFormatter.Core.dll" $hostNet48Dir -Force
        Copy-Item "$srcDir\STFormatter.UI.dll" $hostNet48Dir -Force
        Copy-Item "$srcDir\Microsoft.VisualStudio.Interop.dll" $hostNet48Dir -Force

        $hostFileCount = (Get-ChildItem $hostNet48Dir -File).Count
        Write-Host "  Host net48: $hostFileCount files copied to files\host-net48\" -ForegroundColor Green
    }

    # =============================================
    # 3. Build Host - net462 (for .NET 4.6.2, older TcXaeShell)
    # =============================================
    Write-Host "[3/4] Building Host (net462, x86)..." -ForegroundColor Yellow

    $hostNet462Dir = Join-Path $FilesDir "host-net462"
    New-Item -ItemType Directory -Path $hostNet462Dir -Force | Out-Null

    dotnet build "$RootDir\src\STFormatter.Host\STFormatter.Host.csproj" `
        -c $Configuration `
        -p:TargetFramework=net462

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Host net462 build failed!"
        $buildErrors += "Host-net462"
    } else {
        $srcDir462 = Join-Path $RootDir "src\STFormatter.Host\bin\$Configuration\net462"
        Copy-Item "$srcDir462\STFormatter.Host.exe" $hostNet462Dir -Force
        Copy-Item "$srcDir462\STFormatter.Core.dll" $hostNet462Dir -Force
        Copy-Item "$srcDir462\STFormatter.UI.dll" $hostNet462Dir -Force
        Copy-Item "$srcDir462\Microsoft.VisualStudio.Interop.dll" $hostNet462Dir -Force

        # net462 needs additional dependencies
        $net462Deps = @(
            "System.Text.Json.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "System.Buffers.dll",
            "System.Collections.Immutable.dll",
            "System.Memory.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.ValueTuple.dll"
        )
        foreach ($dep in $net462Deps) {
            $depPath = Join-Path $srcDir462 $dep
            if (Test-Path $depPath) {
                Copy-Item $depPath $hostNet462Dir -Force
            }
        }

        $host462FileCount = (Get-ChildItem $hostNet462Dir -File).Count
        Write-Host "  Host net462: $host462FileCount files copied to files\host-net462\" -ForegroundColor Green
    }
} else {
    Write-Host "[2/4] Skipping Host build" -ForegroundColor Gray
    Write-Host "[3/4] Skipping Host build" -ForegroundColor Gray
}

# =============================================
# 4. Build VSIX (VS 2022 Extension)
# =============================================
if (-not $SkipVSIX) {
    Write-Host "[4/4] Building VSIX extension..." -ForegroundColor Yellow

    $vsixDir = Join-Path $FilesDir "vsix"
    New-Item -ItemType Directory -Path $vsixDir -Force | Out-Null

    $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $hasVSSDK = $false

    if (Test-Path $vsWhere) {
        $vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VSSDK -property installationPath 2>$null
        if ($vsPath) {
            $hasVSSDK = $true
        }
    }

    if ($hasVSSDK) {
        $msBuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        & $msBuild "$RootDir\src\STFormatter.VSIX\STFormatter.VSIX.csproj" /t:Build /p:Configuration=$Configuration /p:DeployExtension=false /v:minimal

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "VSIX build via MSBuild failed. Trying dotnet build..."
            dotnet build "$RootDir\src\STFormatter.VSIX\STFormatter.VSIX.csproj" -c $Configuration
        }
    } else {
        Write-Host "  MSBuild not found, trying dotnet build..." -ForegroundColor Gray
        dotnet build "$RootDir\src\STFormatter.VSIX\STFormatter.VSIX.csproj" -c $Configuration
    }

    # Find the VSIX file
    $vsixPath = Get-ChildItem -Path "$RootDir\src\STFormatter.VSIX" -Filter "*.vsix" -Recurse |
        Where-Object { $_.DirectoryName -notlike "*obj*" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($vsixPath) {
        $targetVsix = Join-Path $vsixDir "TwinCAT.STFormatter.$Version.vsix"
        Copy-Item $vsixPath.FullName $targetVsix -Force
        Write-Host "  VSIX: $($vsixPath.Name) -> files\vsix\TwinCAT.STFormatter.$Version.vsix" -ForegroundColor Green
    } else {
        Write-Warning "VSIX file not found after build!"
        $buildErrors += "VSIX"
    }
} else {
    Write-Host "[4/4] Skipping VSIX build" -ForegroundColor Gray
}

# =============================================
# 5. Create EditorConfig preset templates
# =============================================
Write-Host ""
Write-Host "Creating EditorConfig preset templates..." -ForegroundColor Yellow

$presetsDir = Join-Path $FilesDir "editorconfig-templates"
New-Item -ItemType Directory -Path $presetsDir -Force | Out-Null

# STweep preset
@"
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
max_line_length = 120

[*.st]
st_keyword_casing = upper
st_brace_style = allman
st_space_around_operators = true
st_space_after_comma = true
st_space_before_semicolon = false
st_space_after_colon = true
st_align_variable_declarations = true
st_align_assignments = true
st_continuation_indent_size = 8
st_empty_lines_between_pous = 2
st_empty_lines_between_var_sections = 1
st_keep_single_line_blocks = false
st_format_on_save = true

[*.{TcPOU,TcDUT,TcGVL}]
st_keyword_casing = upper
st_brace_style = allman
"@ | Set-Content (Join-Path $presetsDir "stweep.editorconfig") -Encoding UTF8

# Compact preset
@"
root = true

[*]
indent_style = space
indent_size = 2
end_of_line = crlf
max_line_length = 120

[*.st]
st_keyword_casing = lower
st_brace_style = compact
st_space_around_operators = true
st_space_after_comma = true
st_space_before_semicolon = false
st_space_after_colon = true
st_align_variable_declarations = false
st_align_assignments = false
st_continuation_indent_size = 4
st_empty_lines_between_pous = 1
st_empty_lines_between_var_sections = 0
st_keep_single_line_blocks = true
st_format_on_save = true

[*.{TcPOU,TcDUT,TcGVL}]
st_keyword_casing = lower
st_brace_style = compact
"@ | Set-Content (Join-Path $presetsDir "compact.editorconfig") -Encoding UTF8

# Expanded preset
@"
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
max_line_length = 80

[*.st]
st_keyword_casing = upper
st_brace_style = allman
st_space_around_operators = true
st_space_after_comma = true
st_space_before_semicolon = false
st_space_after_colon = true
st_align_variable_declarations = true
st_align_assignments = true
st_continuation_indent_size = 8
st_empty_lines_between_pous = 3
st_empty_lines_between_var_sections = 2
st_keep_single_line_blocks = false
st_format_on_save = true

[*.{TcPOU,TcDUT,TcGVL}]
st_keyword_casing = upper
st_brace_style = allman
"@ | Set-Content (Join-Path $presetsDir "expanded.editorconfig") -Encoding UTF8

Write-Host "  Created 3 preset templates in files\editorconfig-templates\" -ForegroundColor Green

# =============================================
# 6. Check for errors
# =============================================
Write-Host ""
if ($buildErrors.Count -gt 0) {
    Write-Host "Build completed with errors:" -ForegroundColor Red
    foreach ($err in $buildErrors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Some components failed to build. Check the output above for details." -ForegroundColor Yellow
    Write-Host "You can skip failing components with -SkipCLI, -SkipVSIX, or -SkipHost." -ForegroundColor Yellow
}

# =============================================
# 7. Create the installer
# =============================================
if (-not $SkipInstaller -and $buildErrors.Count -eq 0) {
    Write-Host ""
    Write-Host "Creating installer..." -ForegroundColor Yellow

    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        # Check common Inno Setup install paths
        $isccPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
        )
        foreach ($p in $isccPaths) {
            if (Test-Path $p) {
                $iscc = $p
                break
            }
        }
    }

    if ($iscc) {
        $issFile = Join-Path $PSScriptRoot "STFormatter-Setup.iss"

        # Update version in ISS file
        $issContent = Get-Content $issFile -Raw
        $issContent = $issContent -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
        $tempIss = Join-Path $env:TEMP "STFormatter-Setup.iss"
        Set-Content $tempIss $issContent -Encoding UTF8

        & $iscc $tempIss

        if ($LASTEXITCODE -eq 0) {
            $installerPath = Join-Path $RootDir "publish\STFormatter-Setup-$Version.exe"
            if (Test-Path $installerPath) {
                $installerSize = [math]::Round((Get-Item $installerPath).Length / 1MB, 1)
                Write-Host ""
                Write-Host "==========================================" -ForegroundColor Green
                Write-Host "Installer created successfully!" -ForegroundColor Green
                Write-Host "==========================================" -ForegroundColor Green
                Write-Host "Output: $installerPath" -ForegroundColor White
                Write-Host "Size:   $installerSize MB" -ForegroundColor White
                Write-Host ""
                Write-Host "Components included:" -ForegroundColor Cyan
                if (-not $SkipCLI) { Write-Host "  [x] CLI Tool (stfmt)" -ForegroundColor White }
                if (-not $SkipHost) { Write-Host "  [x] TcXaeShell Host (net48 + net462)" -ForegroundColor White }
                if (-not $SkipVSIX) { Write-Host "  [x] VS 2022 Extension" -ForegroundColor White }
                Write-Host "  [x] EditorConfig presets (stweep, compact, expanded)" -ForegroundColor White
            } else {
                Write-Warning "Installer seems to have completed but output file not found."
            }
        } else {
            Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE"
        }

        Remove-Item $tempIss -Force -ErrorAction SilentlyContinue
    } else {
        Write-Warning "Inno Setup (ISCC.exe) not found on PATH or in common install locations."
        Write-Warning "Install Inno Setup from https://jrsoftware.org/isdl.php and re-run."
        Write-Warning ""
        Write-Warning "Alternatively, the binaries are ready in:" -ForegroundColor Yellow
        Write-Warning "  $FilesDir" -ForegroundColor Yellow
        Write-Warning ""
        Write-Warning "Deploy manually:" -ForegroundColor Yellow
        Write-Warning "  CLI:     Copy files\cli\* to any folder, add to PATH" -ForegroundColor Yellow
        Write-Warning "  Host:    Run deploy.bat [net48|net462]" -ForegroundColor Yellow
        Write-Warning "  VSIX:    Double-click files\vsix\*.vsix" -ForegroundColor Yellow
    }
} elseif ($SkipInstaller) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "Binaries built. Installer creation skipped." -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "Binary files are in: $FilesDir" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "Binaries built with errors. Skipping installer." -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "Fix the build errors and re-run, or use -SkipCLI/-SkipVSIX/-SkipHost" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan