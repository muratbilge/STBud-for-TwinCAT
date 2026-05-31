# How to Install — TwinCAT ST Formatter

Step-by-step installation instructions for all deployment targets.

---

## 0. One-Click Installer (Easiest)

Download and run the installer — it covers all three platforms:

```
STFormatter-Setup-1.0.0.exe
```

The installer lets you choose which components to install:

- **CLI Tool** — `stfmt` command (requires .NET 8 runtime)
- **VS 2022 Extension** — installs VSIX silently
- **TcXaeShell Host** — deploys to TcXaeShell extensions folder

### Building the Installer from Source

Prerequisites: .NET 8 SDK, .NET Framework 4.6.2/4.8 targeting packs, Inno Setup 6, VS 2022 with VSSDK workload (for VSIX)

```powershell
.\installer\build-installer.ps1                    # Build everything + create installer
.\installer\build-installer.ps1 -SkipVSIX           # Skip VSIX if no VS SDK
.\installer\build-installer.ps1 -SkipInstaller       # Build binaries only, no installer
.\installer\build-installer.ps1 -Configuration Release -Version 1.0.0  # Custom config/version
```

Output: `publish\STFormatter-Setup-1.0.0.exe`

---

## 1. CLI Tool (Command Line)

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Option A: Install as Global Tool (Recommended)

```bash
# Build and pack
dotnet pack src/STFormatter.CLI/STFormatter.CLI.csproj -c Release

# Install globally
dotnet tool install --global --add-source src/STFormatter.CLI/bin/Release STFormatter.CLI

# Verify
stfmt --help
```

After installation, `stfmt` is available from any directory. Update with:

```bash
dotnet tool update --global STFormatter.CLI
```

### Option B: Run Directly (No Install)

```bash
# Build
dotnet build src/STFormatter.CLI -c Release

# Run
dotnet run --project src/STFormatter.CLI -- format MyProgram.st
```

### Uninstall

```bash
dotnet tool uninstall --global STFormatter.CLI
```

---

## 2. Visual Studio 2022 Extension (VSIX)

### Prerequisites

- Visual Studio 2022 (Community, Professional, or Enterprise) v17.0+
- The "Visual Studio extension development" workload (for building from source)

### Option A: Install Pre-built VSIX

1. Navigate to `publish/TwinCAT.STFormatter.1.0.0.vsix`
2. **Double-click** the `.vsix` file
3. Click **Install** in the VSIX Installer dialog
4. Restart Visual Studio 2022

### Option B: Build from Source

```powershell
# Build using the provided script (requires VS 2022 SDK workload)
.\build-vsix.ps1 -Configuration Release

# The output will be at:
# publish/TwinCAT.STFormatter.1.0.0.vsix
```

If the VS SDK workload is not installed, the script will offer to build CLI-only. To install the workload:

1. Open **Visual Studio Installer**
2. Click **Modify** on your VS 2022 installation
3. Select **Visual Studio extension development**
4. Click **Modify**

### Option C: Install from Within Visual Studio

1. Open Visual Studio 2022
2. Go to **Extensions** > **Manage Extensions**
3. Click the gear icon > **Install from VSIX...**
4. Select `publish/TwinCAT.STFormatter.1.0.0.vsix`
5. Restart Visual Studio

### Verify Installation

1. Open or create a `.st`, `.TcPOU`, `.TcDUT`, or `.TcGVL` file
2. Press **Ctrl+K, Ctrl+D** — the file should be formatted
3. Check **Tools** > **Options** > **TwinCAT** > **ST Formatter** for settings

### Uninstall

1. **Extensions** > **Manage Extensions** > **Installed**
2. Find **TwinCAT ST Formatter**
3. Click **Uninstall**
4. Restart Visual Studio

---

## 3. TwinCAT XAE Shell (TcXaeShell)

> **Important**: TcXaeShell's VS 2017 Isolated Shell does **not** support standard VSIX
> extensions or VSPackages. The formatter must be deployed as an **external Host process**
> that connects via COM DTE (Running Object Table). This is the only working approach.

### Prerequisites

- Beckhoff TwinCAT XAE Shell (any version: TC3 Build 4024+ = VS 2017, older = VS 2015/2013)
- .NET Framework 4.6.2+ (4.8 recommended for current builds)
- Administrator privileges for deployment

### Step 1: Build the Host

```bash
# Build for current TcXaeShell (net48 — requires .NET 4.8+)
dotnet build src/STFormatter.Host -c Debug

# Or build for older TcXaeShell (net462 — requires .NET 4.6.2+)
dotnet build src/STFormatter.Host/STFormatter.Host.csproj -c Debug -p:TargetFramework=net462
```

### Step 2: Deploy the Files

Run the provided deployment script as Administrator:

```powershell
# For net48 (default — current TcXaeShell)
.\deploy.bat

# For net462 (older TcXaeShell)
.\deploy.bat net462
```

This copies the following files to
`C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\`:

| File | Purpose |
|---|---|
| `STFormatter.Host.exe` | Main host process — connects to TcXaeShell via COM DTE |
| `STFormatter.Core.dll` | Formatting engine |
| `STFormatter.UI.dll` | System tray icon and settings UI |
| `Microsoft.VisualStudio.Interop.dll` | VS interop assembly |

To deploy manually:

```powershell
# Run as Administrator
$src = "src\STFormatter.Host\bin\Debug\net48"
$dst = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"
New-Item -ItemType Directory -Path $dst -Force
Copy-Item "$src\STFormatter.Host.exe" $dst -Force
Copy-Item "$src\STFormatter.Core.dll" $dst -Force
Copy-Item "$src\STFormatter.UI.dll" $dst -Force
Copy-Item "$src\Microsoft.VisualStudio.Interop.dll" $dst -Force
```

### Step 3: Start the Host

You can start the Host before or after TcXaeShell — it will auto-detect and auto-reconnect.

```powershell
# Start the Host (it runs as a hidden background process)
Start-Process "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
```

The Host appears as a **system tray icon** (a small "ST" icon). Right-click it for:

- **Settings** — Change formatting options at runtime (indentation, keyword casing, etc.)
- **Instances** — View connected TcXaeShell processes
- **History** — Review past format operations with before/after diffs
- **Log** — Open the live log file

### Step 4: Verify It Works

1. Open TcXaeShell and load a TwinCAT project
2. Open a POU (program, function block, etc.) and click in the declaration or implementation section
3. **Right-click** in the PLC editor — you should see three new menu items at the bottom:
   - **Format ST Document** — Formats the active declaration or implementation section
   - **Format ST Selection** — Formats only the selected text
   - **Format ST File** — Formats the entire `.TcPOU`/`.TcDUT`/`.TcGVL` file on disk

4. Click **Format ST Document** — the code should be reformatted instantly
5. Check the log file for confirmation:

```powershell
Get-Content "$env:TEMP\STFormatter_Host.log" -Tail 10
```

You should see output like:

```
[16:06:27.827] HostManager: FindNewTcXaeShell: Found PID 37464 profile=TC3-VS2017 (DTE 15.0, VS 2017)
[16:06:28.799] HostManager: AddButtons: PID 37464 +3 buttons to 'PlcCodeWinContextMenu'
[16:06:29.144] HostManager: InjectButtons: PID 37464 injected into: PlcCodeWinContextMenu, Code Window
```

### Auto-Start on Login (Optional)

To have the Host start automatically when you log in:

```powershell
# Create a shortcut in the Startup folder
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter.Host.lnk")
$Shortcut.TargetPath = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
$Shortcut.WindowStyle = 7  # Minimized
$Shortcut.Save()
```

### Troubleshooting

| Problem | Solution |
|---|---|
| **Host won't start** | Check that all 4 DLLs/EXEs are in the Extensions\STFormatter folder. Run `"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"` from a command prompt and check for errors. |
| **No context menu items** | The Host could not connect to TcXaeShell. Check the log for errors. Ensure TcXaeShell is running before or after starting the Host. The Host auto-reconnects every 5 seconds. |
| **Format doesn't work** | Check `%TEMP%\STFormatter_Host.log` for errors. The Host uses the clipboard-based live-edit approach — ensure no clipboard manager is locking the clipboard. Try clicking in the code editor first, then right-click. |
| **"Format ST Document" is grayed out** | This means no active text editor was detected. Click inside the PLC code editor first. |
| **Host crashes on startup** | Ensure you're using the correct build (net48 for .NET 4.8+, net462 for .NET 4.6.2). Check the Windows Event Viewer for .NET runtime errors. |
| **Error: DTE not found** | The Host searches for both `!TcXaeShell.DTE` and `!VisualStudio.DTE` monikers. Ensure TcXaeShell is actually running. The Host retries every 5 seconds. |
| **Error: Access denied** | The Host needs to be run from a location where it can access TcXaeShell's COM objects. If you moved it, ensure the folder has read/execute permissions. |
| **Format works but changes are not applied** | The live-edit approach uses `Edit.SelectAll` > `Edit.Copy` > `Edit.Delete` > `Edit.Paste` via DTE commands. If the PLC editor doesn't respond to these commands, try using "Format ST File" instead, which writes directly to disk. |

### How Live-Edit Works

The TcXaeShell PLC editor (CODESYS-based) does **not** support standard VS automation APIs
like `TextSelection.Text` or `IVsTextBuffer` from an external process. The Host works around
this limitation using a clipboard-based approach:

1. **Read** the active section: `Edit.SelectAll` → `Edit.Copy` → read clipboard via Win32 API
2. **Detect** the section type: `LooksLikeDeclaration()` heuristic checks for `VAR`/`END_VAR` keywords
3. **Format** the content: `FormattingEngine.Format()` for declarations, `.FormatBody()` for implementations
4. **Write** back: Set clipboard to formatted text → `Edit.Delete` → `Edit.Paste`
5. **Restore**: Original clipboard content is saved and restored after the operation

The entire operation is wrapped in a DTE `UndoContext`, so pressing Ctrl+Z reverts the formatting.

### Uninstall

1. Stop the Host process (right-click tray icon > Exit, or Task Manager)
2. Delete the extension folder:
   ```powershell
   # Run as Administrator
   Remove-Item -Recurse -Force "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"
   ```
3. Remove the Startup shortcut if created:
   ```powershell
   Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter.Host.lnk" -ErrorAction SilentlyContinue
   ```
4. Restart TcXaeShell

---

## 4. Install All Platforms

```bash
# 1. Build everything
dotnet build TwinCAT.STFormatter.sln -c Release

# 2. Install CLI
dotnet pack src/STFormatter.CLI -c Release
dotnet tool install --global --add-source src/STFormatter.CLI/bin/Release STFormatter.CLI

# 3. Install VSIX
#    Double-click: publish\TwinCAT.STFormatter.1.0.0.vsix

# 4. Deploy TcXaeShell Host (requires admin)
.\deploy.bat
```

---

## 5. Configuration After Installation

Create an `.editorconfig` in your project directory:

```bash
# Generate from Default preset (recommended for TwinCAT projects)
stfmt init . --preset default

# Or from Compact preset
stfmt init . --preset compact

# Or from Expanded preset
stfmt init . --preset expanded
```

This creates a `.editorconfig` file that all three deployment targets (CLI, VSIX, Host) read automatically.

For VS 2022, you can also configure settings via **Tools > Options > TwinCAT > ST Formatter**. These settings override `.editorconfig`.

For TcXaeShell, right-click the system tray icon > **Settings** to change formatting options at runtime. These settings are saved to `%LOCALAPPDATA%\STFormatter\settings.json` and persist across restarts.