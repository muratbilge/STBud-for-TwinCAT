#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build script for the TwinCAT ST Formatter installer.

.DESCRIPTION
Builds the TcXaeShell Host and optional CLI payload, then creates an Inno Setup
installer. TcXaeShell integration is handled by the external Host process; no
VSIX or in-process TcXaeShell extension is built or packaged.

.PARAMETER SkipCLI
Skip building the CLI tool.

.PARAMETER SkipHost
Skip building the TcXaeShell Host.

.PARAMETER SkipInstaller
Skip creating the installer; only populate installer/files.

.PARAMETER Configuration
Build configuration. Default: Debug.

.PARAMETER Version
Version number for the installer. Default: 1.0.0.
#>

param(
    [switch]$SkipCLI = $false,
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

if (Test-Path $FilesDir) {
    Remove-Item -Path $FilesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $FilesDir -Force | Out-Null
Set-Content (Join-Path $FilesDir ".gitignore") -Value "*`n!.gitignore" -NoNewline

$buildErrors = @()

if (-not $SkipCLI) {
    Write-Host "[1/4] Building CLI tool (net8.0)..." -ForegroundColor Yellow

    $cliDir = Join-Path $FilesDir "cli"
    New-Item -ItemType Directory -Path $cliDir -Force | Out-Null

    dotnet publish "$RootDir\src\STFormatter.CLI\STFormatter.CLI.csproj" `
        -c $Configuration `
        -o $cliDir `
        --self-contained false `
        /p:Version=$Version `
        /p:AssemblyVersion=$Version.0 `
        /p:FileVersion=$Version.0 `
        /p:InformationalVersion=$Version `
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

if (-not $SkipHost) {
    Write-Host "[2/4] Building Host (net48, x86)..." -ForegroundColor Yellow

    $hostNet48Dir = Join-Path $FilesDir "host-net48"
    New-Item -ItemType Directory -Path $hostNet48Dir -Force | Out-Null

    dotnet build "$RootDir\src\STFormatter.Host\STFormatter.Host.csproj" `
        -c $Configuration `
        -p:TargetFramework=net48 `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version.0 `
        -p:FileVersion=$Version.0 `
        -p:InformationalVersion=$Version

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Host net48 build failed!"
        $buildErrors += "Host-net48"
    } else {
        $srcDir = Join-Path $RootDir "src\STFormatter.Host\bin\$Configuration\net48"
        Copy-Item "$srcDir\STFormatter.Host.exe" $hostNet48Dir -Force
        Copy-Item "$srcDir\*.dll" $hostNet48Dir -Force

        $hostFileCount = (Get-ChildItem $hostNet48Dir -File).Count
        Write-Host "  Host net48: $hostFileCount files copied to files\host-net48\" -ForegroundColor Green
    }

    Write-Host "[3/4] Building Host (net462, x86)..." -ForegroundColor Yellow

    $hostNet462Dir = Join-Path $FilesDir "host-net462"
    New-Item -ItemType Directory -Path $hostNet462Dir -Force | Out-Null

    dotnet build "$RootDir\src\STFormatter.Host\STFormatter.Host.csproj" `
        -c $Configuration `
        -p:TargetFramework=net462 `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version.0 `
        -p:FileVersion=$Version.0 `
        -p:InformationalVersion=$Version

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Host net462 build failed!"
        $buildErrors += "Host-net462"
    } else {
        $srcDir462 = Join-Path $RootDir "src\STFormatter.Host\bin\$Configuration\net462"
        Copy-Item "$srcDir462\STFormatter.Host.exe" $hostNet462Dir -Force
        Copy-Item "$srcDir462\*.dll" $hostNet462Dir -Force

        $host462FileCount = (Get-ChildItem $hostNet462Dir -File).Count
        Write-Host "  Host net462: $host462FileCount files copied to files\host-net462\" -ForegroundColor Green
    }
} else {
    Write-Host "[2/4] Skipping Host build" -ForegroundColor Gray
    Write-Host "[3/4] Skipping Host build" -ForegroundColor Gray
}

Write-Host "[4/4] Creating EditorConfig preset templates..." -ForegroundColor Yellow

$presetsDir = Join-Path $FilesDir "editorconfig-templates"
New-Item -ItemType Directory -Path $presetsDir -Force | Out-Null

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
"@ | Set-Content (Join-Path $presetsDir "default.editorconfig") -Encoding UTF8

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

Write-Host "  Created presets: default, compact, expanded" -ForegroundColor Green

Write-Host ""
if ($buildErrors.Count -gt 0) {
    Write-Host "Build completed with errors:" -ForegroundColor Red
    foreach ($err in $buildErrors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Fix the build errors and re-run, or use -SkipCLI/-SkipHost." -ForegroundColor Yellow
}

if (-not $SkipInstaller -and $buildErrors.Count -eq 0) {
    Write-Host ""
    Write-Host "Creating installer..." -ForegroundColor Yellow

    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $isccPaths = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe",
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
        $issContent = Get-Content $issFile -Raw
        $issContent = $issContent -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
        $tempIss = Join-Path $PSScriptRoot "STFormatter-Setup.generated.iss"
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
                if (-not $SkipHost) { Write-Host "  [x] TcXaeShell Host (net48 + net462)" -ForegroundColor White }
                if (-not $SkipCLI) { Write-Host "  [x] CLI Tool (stfmt)" -ForegroundColor White }
                Write-Host "  [x] EditorConfig presets (default, compact, expanded)" -ForegroundColor White
            } else {
                Write-Warning "Installer seems to have completed but output file was not found."
            }
        } else {
            Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE"
        }

        Remove-Item $tempIss -Force -ErrorAction SilentlyContinue
    } else {
        Write-Warning "Inno Setup (ISCC.exe) not found on PATH or in common install locations."
        Write-Warning "Install Inno Setup from https://jrsoftware.org/isdl.php and re-run."
        Write-Warning ""
        Write-Warning "Binaries are ready in: $FilesDir"
        Write-Warning "Manual Host deployment: copy files\host-net48\* to the TcXaeShell Extensions\STFormatter folder."
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
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
