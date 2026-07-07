#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build script for the STBud for TwinCAT toolbox installer.

.DESCRIPTION
Builds the TcXaeShell toolbox Host and optional formatter CLI payload, then creates an Inno Setup
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
Installer version. Default: derived from Directory.Build.props (VersionPrefix),
the single source of truth. Pass an explicit value only to override.
#>

param(
    [switch]$SkipCLI = $false,
    [switch]$SkipHost = $false,
    [switch]$SkipInstaller = $false,
    [string]$Configuration = "Debug",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")

# Single source of truth: take the version from Directory.Build.props unless
# explicitly overridden. Releases produce a clean number (e.g. 1.0.0); dev
# installers carry the same numeric prefix.
if ([string]::IsNullOrEmpty($Version)) {
    $propsPath = Join-Path $RootDir "Directory.Build.props"
    $m = Select-String -Path $propsPath -Pattern '<VersionPrefix[^>]*>([^<]+)</VersionPrefix>' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($m) { $Version = $m.Matches[0].Groups[1].Value }
    if ([string]::IsNullOrEmpty($Version)) { $Version = "1.0.0" }
    Write-Host "Version (from Directory.Build.props): $Version" -ForegroundColor Cyan
}
$FilesDir = Join-Path $PSScriptRoot "files"

$HostPayloadFiles = @(
    "STFormatter.Host.exe",
    "STFormatter.Host.exe.config",
    "STFormatter.Core.dll",
    "STFormatter.UI.dll",
    "STBud.Git.dll",
    "STBud.Git.Editor.dll",
    "Microsoft.VisualStudio.Interop.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Buffers.dll",
    "System.Collections.Immutable.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encodings.Web.dll",
    "System.Text.Json.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll",
    "stgit.exe"
)

$ForbiddenHostPayloadPatterns = @(
    "*.vsix",
    "*.pkgdef",
    "extension.vsixmanifest",
    "STFormatter.TcXaeShell.*",
    "PackageIcon.png",
    "PreviewImage.png",
    "Microsoft.VisualStudio.CommandBars.dll",
    "stdole.dll",
    "TwinCAT*.dll",
    "*Object.dll",
    "*Editor.dll"
)

function Copy-HostPayload {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    foreach ($pattern in $ForbiddenHostPayloadPatterns) {
        # The broad patterns (e.g. *Editor.dll, meant for Beckhoff/VS interop) can also match
        # our own allowlisted payload (STBud.Git.Editor.dll) - never flag an allowlisted file.
        $matches = Get-ChildItem -LiteralPath $SourceDir -File -Filter $pattern -ErrorAction SilentlyContinue |
            Where-Object { $HostPayloadFiles -notcontains $_.Name }
        if ($matches) {
            $names = ($matches | ForEach-Object { $_.Name }) -join ", "
            throw "Forbidden Host payload artifact(s) in ${SourceDir}: $names"
        }
    }

    foreach ($file in $HostPayloadFiles) {
        $path = Join-Path $SourceDir $file
        if (Test-Path -LiteralPath $path) {
            Copy-Item -LiteralPath $path -Destination $DestinationDir -Force
        }
    }

    # stgit.exe is not in the Host bin; it's built from a separate project and copied (with its
    # own presence check) right after this call, so it isn't required here.
    $required = @("STFormatter.Host.exe", "STFormatter.Host.exe.config", "STFormatter.Core.dll", "STFormatter.UI.dll", "STBud.Git.dll", "STBud.Git.Editor.dll", "Microsoft.VisualStudio.Interop.dll")
    foreach ($file in $required) {
        $path = Join-Path $DestinationDir $file
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Required Host payload file missing after copy: $file"
        }
    }
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "STBud for TwinCAT Installer Builder" -ForegroundColor Cyan
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
    # Build stgit (net48) so the installer can ship it alongside the Host.
    Write-Host "  Building stgit (net48)..." -ForegroundColor Gray
    dotnet build "$RootDir\src\STBud.Git.CLI\STBud.Git.CLI.csproj" `
        -c $Configuration `
        -p:TargetFramework=net48 `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version.0 `
        -p:FileVersion=$Version.0 `
        -p:InformationalVersion=$Version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "stgit net48 build failed - the installer will ship without stgit."
    }
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
        Copy-HostPayload -SourceDir $srcDir -DestinationDir $hostNet48Dir

        # stgit.exe builds to its own bin folder; copy it into the Host payload so the
        # installer ships it. The net48 build of stgit runs without .NET 8 on the target.
        $stgitSrc = Join-Path $RootDir "src\STBud.Git.CLI\bin\$Configuration\net48\stgit.exe"
        if (Test-Path -LiteralPath $stgitSrc) {
            Copy-Item -LiteralPath $stgitSrc -Destination $hostNet48Dir -Force
        } else {
            Write-Warning "stgit.exe not found at $stgitSrc - build the CLI before the installer."
        }

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
        Copy-HostPayload -SourceDir $srcDir462 -DestinationDir $hostNet462Dir

        # stgit only targets net48/net8.0; copy the net48 build when available so
        # net462 hosts that also have .NET 4.8 can still use stgit.
        $stgitSrc462 = Join-Path $RootDir "src\STBud.Git.CLI\bin\$Configuration\net48\stgit.exe"
        if (Test-Path -LiteralPath $stgitSrc462) {
            Copy-Item -LiteralPath $stgitSrc462 -Destination $hostNet462Dir -Force
        }

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
            $installerPath = Join-Path $RootDir "publish\STBud-for-TwinCAT-Setup-$Version.exe"
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
        Write-Warning "Manual Host deployment: copy files\host-net48\* to C:\Program Files (x86)\STBud\."
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
