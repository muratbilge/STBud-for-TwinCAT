# TwinCAT ST Formatter - Installation Guide / Installationshandbuch

## Quick Start

Choose your installation method:

- [Option 0: One-Click Installer (All Platforms)](#option-0-one-click-installer)
- [Option 1: CLI Tool (Command Line)](#option-1-cli-tool)
- [Option 2: TwinCAT XAE Shell (TcXaeShell)](#option-2-twincat-xae-shell-tcxaeshell)
- [Option 3: All Supported Targets (Manual)](#option-3-all-supported-targets-manual)

---

## Option 0: One-Click Installer

The easiest way to install the TcXaeShell Host and optional CLI. Download `STFormatter-Setup-1.0.0.exe` and run it.

The installer lets you choose which components to install:

| Component | Requires | Description |
|---|---|---|
| TcXaeShell Host | Beckhoff TcXaeShell | Deploys Host to extensions folder, optional auto-start |
| CLI Tool (stfmt) | .NET 8 runtime | Command-line formatter |

Features:

- Detects TcXaeShell and the installed .NET Framework version
- Automatically picks net48 or net462 Host binaries based on your .NET Framework version
- Optional: "Start Host on login" for TcXaeShell
- Optional: add the CLI folder to the user PATH
- Clean uninstall via Add/Remove Programs
- Includes EditorConfig preset templates (default, compact, expanded)

### Building the Installer from Source

```powershell
# Build everything and create installer
.\installer\build-installer.ps1

# Build binaries only (skip Inno Setup)
.\installer\build-installer.ps1 -SkipInstaller

# Build Host only, without CLI
.\installer\build-installer.ps1 -SkipCLI

# Custom configuration and version
.\installer\build-installer.ps1 -Configuration Release -Version 1.0.0
```

Prerequisites for building: .NET 8 SDK, .NET Framework 4.6.2+4.8 targeting packs, and Inno Setup 6.

---

## Option 1: CLI Tool

### Prerequisites / Voraussetzungen
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Build from Source

```bash
cd TwinCAT.STFormatter

# Build the CLI
dotnet build src/STFormatter.CLI/STFormatter.CLI.csproj -c Release

# The executable will be at:
# src/STFormatter.CLI/bin/Release/net8.0/STFormatter.CLI.exe
```

### Create Global Tool (Recommended)

```bash
# Pack as a .NET tool
dotnet pack src/STFormatter.CLI/STFormatter.CLI.csproj -c Release

# Install globally
dotnet tool install --global --add-source src/STFormatter.CLI/bin/Release STFormatter.CLI

# Now you can use it from anywhere:
stfmt format MyProgram.st
stfmt batch ./POUs --recursive
```

### Or Use Directly

```bash
cd src/STFormatter.CLI/bin/Release/net8.0
./STFormatter.CLI.exe format MyProgram.st
```

---

## Option 2: TwinCAT XAE Shell (TcXaeShell)

### Prerequisites / Voraussetzungen
- TwinCAT XAE Shell (Beckhoff TcXaeShell — version determines the VS Shell generation: 4024+ = 15.0, older builds = 14.0 or 12.0)
- .NET Framework 4.6.2+ (4.6.2 for older TcXaeShell versions; 4.8 for current TcXaeShell 15.0)
- Admin privileges for deployment

> **Important:** TcXaeShell's VS 2017 Isolated Shell does **not** support standard VSIX
> extensions, VSPackages, or MEF components. The formatter works via an **external Host
> process** that connects to TcXaeShell via COM DTE (Running Object Table). This is the
> only approach that works — see [AGENTS.md](../AGENTS.md) for details.

### Build the Host / Quellcode erstellen

```bash
dotnet build src/STFormatter.Host -c Debug

# Use net48 target for machines with .NET 4.8+ (current TcXaeShell 15.0):
#   output: src/STFormatter.Host/bin/Debug/net48/
# Use net462 target for older machines with only .NET 4.6.2 (TcXaeShell 14.0/12.0):
dotnet build src/STFormatter.Host/STFormatter.Host.csproj -c Debug -p:TargetFramework=net462
```

### Deploy / Bereitstellung

Deployment requires Administrator privileges.

**Step 1: Run the deploy script**

```powershell
# For net48 (default — current TcXaeShell)
.\deploy.bat

# For net462 (older TcXaeShell)
.\deploy.bat net462
```

This copies the following files to `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\`:

| File | Purpose |
|---|---|
| `STFormatter.Host.exe` | Main host process — connects via COM DTE |
| `STFormatter.Core.dll` | Formatting engine |
| `STFormatter.UI.dll` | System tray icon and settings UI |
| `Microsoft.VisualStudio.Interop.dll` | VS interop assembly |

**Step 2: Start the Host**

```powershell
Start-Process "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
```

The Host auto-detects running TcXaeShell instances and auto-reconnects after restarts. It runs as a hidden background process with a system tray icon.

### Verify Installation

1. Open TcXaeShell and load a TwinCAT project
2. Open a POU and click in the declaration or implementation section
3. **Right-click** in the PLC editor — you should see three new menu items:
   - **Format ST Document** — Formats the active section
   - **Format ST Selection** — Formats only the selected text
   - **Format ST File** — Formats the entire file on disk
4. Click **Format ST Document** — the code should be reformatted instantly
5. Check the log: `Get-Content "$env:TEMP\STFormatter_Host.log" -Tail 10`

### Auto-Start on Login (Optional)

```powershell
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter.Host.lnk")
$Shortcut.TargetPath = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
$Shortcut.WindowStyle = 7
$Shortcut.Save()
```

### Troubleshooting (TcXaeShell) / Fehlerbehebung

| Problem / Problem | Solution / Losung |
|---|---|
| Host won't start | Ensure all 4 files are in the Extensions\STFormatter folder. Run from command prompt for error details. |
| No context menu items | Check `%TEMP%\STFormatter_Host.log`. The Host retries every 5 seconds if TcXaeShell is not found. |
| Format doesn't work | Check log for errors. Ensure no clipboard manager is locking the clipboard. Click inside the code editor first. |
| Host crashes | Use correct build (net48 for .NET 4.8+, net462 for .NET 4.6.2). Check Windows Event Viewer for .NET errors. |
| DTE not found | Host searches for both `!TcXaeShell.DTE` and `!VisualStudio.DTE` monikers. Ensure TcXaeShell is running. |

For detailed TcXaeShell integration documentation, see [HOW-TO-INSTALL.md](HOW-TO-INSTALL.md).

---

## Option 3: All Supported Targets (Manual)

```bash
# 1. Build everything supported by the installer
dotnet build TwinCAT.STFormatter.sln -c Release

# 2. Install CLI tool
cd src/STFormatter.CLI
dotnet pack -c Release
dotnet tool install --global --add-source bin/Release STFormatter.CLI

# 3. Deploy TcXaeShell Host (requires admin)
.\deploy.bat
```

---

## Post-Installation Setup / Einrichtung nach der Installation

### Create Configuration File / Konfigurationsdatei erstellen

```bash
# Create .editorconfig with Default preset
stfmt init . --preset default

# Or with compact preset / Oder mit Compact-Voreinstellung
stfmt init . --preset compact
```

### Verify CLI Works / CLI prüfen

```bash
stfmt --help
stfmt format samples/SampleSTFiles/Sample1.st --dry-run
stfmt check samples/SampleSTFiles/Sample1.st
```

---

## Uninstallation / Deinstallation

### CLI Tool

```bash
dotnet tool uninstall --global STFormatter.CLI
```

### TcXaeShell Host

1. Stop the Host process (right-click tray icon > Exit, or Task Manager)
2. Delete the extension folder:
   ```powershell
   Remove-Item -Recurse -Force "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"
   ```
3. Remove the Startup shortcut (if created):
   ```powershell
   Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter.Host.lnk" -ErrorAction SilentlyContinue
   ```
4. Restart TcXaeShell

---

## Troubleshooting / Fehlerbehebung

### "dotnet command not found"
Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

### "Format command doesn't work"
- Ensure file extension is `.st`, `.TcPOU`, `.TcDUT`, `.TcGVL`, `.tcio`, or `.tcto`
- Check that the file contains valid ST code
- Try a simple test file first

### "Permission denied on Linux/Mac"
```bash
chmod +x src/STFormatter.CLI/bin/Release/net8.0/STFormatter.CLI
```

### TcXaeShell: "Commands don't appear"
- Ensure STFormatter.Host.exe is running (check system tray or Task Manager)
- Check `%TEMP%\STFormatter_Host.log` for connection errors
- The Host auto-retries every 5 seconds — wait a moment and try again

---

## Next Steps / Nachste Schritte

- Read the [TcXaeShell Integration Guide](TcXaeShell-Integration.md) for advanced TcXaeShell topics
- See [ARCHITECTURE.md](ARCHITECTURE.md) for technical details
- See [FORMAT-OPTIONS.md](FORMAT-OPTIONS.md) for configuration reference
- See [README.md](../README.md) for usage examples
- Configure your style in `.editorconfig`
