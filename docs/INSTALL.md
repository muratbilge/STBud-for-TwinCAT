# TwinCAT ST Formatter - Installation Guide / Installationshandbuch

## Quick Start

Choose your installation method:

- [Option 1: CLI Tool (Command Line)](#option-1-cli-tool)
- [Option 2: Visual Studio 2022 Extension](#option-2-visual-studio-2022-extension)
- [Option 3: TwinCAT XAE Shell (TcXaeShell)](#option-3-twincat-xae-shell-tcxaeShell)
- [Option 4: All Platforms](#option-4-all-platforms)

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

## Option 2: Visual Studio 2022 Extension

### Prerequisites / Voraussetzungen
- Visual Studio 2022 (17.0 or later)
- Visual Studio SDK workload (for building)

### Method A: Download Pre-built VSIX

1. **Double-click** the `.vsix` file
2. Click **Install** in the dialog
3. Restart Visual Studio 2022

### Method B: Build from Source

```bash
dotnet build TwinCAT.STFormatter.sln -c Release
# The VSIX will be at:
# publish/TwinCAT.STFormatter.1.0.0.vsix
```

### Method C: Install from Visual Studio

1. Open Visual Studio 2022
2. Go to **Extensions** > **Manage Extensions**
3. Click the **Settings** (gear) icon > **Install from VSIX...**
4. Select the `.vsix` file
5. Restart Visual Studio

### Verify Installation / Installation prüfen

1. Open any `.st`, `.TcPOU`, `.TcDUT`, or `.TcGVL` file
2. Press `Ctrl+K, Ctrl+D` to format
3. Check **Tools** > **Options** > **TwinCAT** > **ST Formatter**

---

## Option 3: TwinCAT XAE Shell (TcXaeShell)

### Prerequisites / Voraussetzungen
- TwinCAT XAE Shell (Beckhoff TcXaeShell — version determines the VS Shell generation: 4024+ = 15.0, older builds = 14.0 or 12.0)
- .NET Framework 4.6.2+ (4.6.2 for older TcXaeShell versions; 4.8 for current TcXaeShell 15.0)
- Admin privileges for deployment

> **Note / Hinweis:** TcXaeShell is a 32-bit Visual Studio Isolated Shell application (VS 2017 v15, VS 2015 v14, or VS 2013 v12 depending on the TwinCAT build).
> The extension must be deployed manually — there is no VSIX installer for TcXaeShell.

### Build from Source / Quellcode erstellen

```bash
dotnet build src/STFormatter.TcXaeShell/STFormatter.TcXaeShell.csproj -c Release
# Use net48 target for machines with .NET 4.8+ (current TcXaeShell 15.0):
#   output: src/STFormatter.TcXaeShell/bin/Release/net48/
# Use net462 target for older machines with only .NET 4.6.2 (TcXaeShell 14.0/12.0):
#   output: src/STFormatter.TcXaeShell/bin/Release/net462/
```

Output files are in `src/STFormatter.TcXaeShell/bin/Release/net48/` (or `net462/` for older TcXaeShell).

### Deploy / Bereitstellung

Deployment requires Administrator privileges.

**Step 1: Stop TcXaeShell**

Close all instances of TcXaeShell before deploying.

**Step 2: Copy files**

```powershell
# Run as Administrator
$src = "src\STFormatter.TcXaeShell\bin\Release\net48"  # use net462 for older TcXaeShell
$dst = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"

# Create destination if needed
New-Item -ItemType Directory -Path $dst -Force

# Copy extension files
Copy-Item "$src\STFormatter.TcXaeShell.dll" $dst -Force
Copy-Item "$src\STFormatter.TcXaeShell.pdb" $dst -Force
Copy-Item "$src\STFormatter.Core.dll" $dst -Force
Copy-Item "$src\STFormatter.Core.pdb" $dst -Force
Copy-Item "$src\STFormatter.TcXaeShell.pkgdef" $dst -Force
```

> **Important:** Do NOT copy the Beckhoff DLLs (IECTextEditor.dll, TextDocument.dll, etc.).
> They are loaded from the TwinCAT installation at runtime via the binding path.

**Step 3: Register the extension**

Import the registry file `register_tcxae.reg` (requires admin):

```powershell
reg import register_tcxae.reg
```

This registers the package, menu commands, and options page.

**Step 4: Clear caches**

```powershell
# NOTE: Replace "15.0" with your TcXaeShell version (15.0=VS2017, 14.0=VS2015, 12.0=VS2013)
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache"
Remove-Item -Force "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0\Extensions\extensions.en-US.cache"
```

**Step 5: Start TcXaeShell**

Open TcXaeShell, open a TwinCAT project with a POU, and test formatting:
- **Edit** > **Format ST Document** (or `Ctrl+K, D`)
- **Edit** > **Format ST Selection** (or `Ctrl+K, F`)

### Verify Installation

1. Open a `.TcPOU` file in TcXaeShell
2. Go to **Edit** menu — you should see **Format ST Document** and **Format ST Selection**
3. Check **Tools** > **Options** > **TwinCAT** > **ST Formatter** for settings
4. Debug log is written to `%TEMP%\STFormatter_TcXaeShell.log`

### Troubleshooting (TcXaeShell) / Fehlerbehebung

| Problem / Problem | Solution / Losung |
|---|---|
| Commands don't appear | Clear caches (Step 4) and restart. Check registry entries match package GUID. |
| Extension loads but format doesn't work | Check log at `%TEMP%\STFormatter_TcXaeShell.log` |
| Build error: VSSDK not found | Install VS 2017 SDK or VSSDK BuildTools 17.x |
| Menu resource version error | Keep ctmenu version as 1 in register_tcxae.reg |
| Beckhoff DLLs not found at runtime | Verify TwinCAT binding path in registry |

For detailed TcXaeShell integration documentation, see [TcXaeShell-Integration.md](TcXaeShell-Integration.md).

---

## Option 4: All Platforms

```bash
# 1. Build everything
dotnet build TwinCAT.STFormatter.sln -c Release

# 2. Install CLI tool
cd src/STFormatter.CLI
dotnet pack -c Release
dotnet tool install --global --add-source bin/Release STFormatter.CLI

# 3. Install VSIX extension
# Double-click: src/STFormatter.VSIX/bin/Release/net8.0-windows/TwinCAT.STFormatter.vsix

# 4. Deploy TcXaeShell (see Option 3 above)
```

---

## Post-Installation Setup / Einrichtung nach der Installation

### Create Configuration File / Konfigurationsdatei erstellen

```bash
# Create .editorconfig with STweep preset
stfmt init . --preset stweep

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

### VSIX Extension

1. Open Visual Studio 2022
2. Go to **Extensions** > **Manage Extensions** > **Installed**
3. Find **TwinCAT ST Formatter**
4. Click **Uninstall**
5. Restart Visual Studio

### TcXaeShell Extension

1. Delete the extension folder: `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter`
2. Remove registry entries from `register_tcxae.reg` (remove the keys manually or create an uninstall .reg)
3. Clear caches (see Step 4 above)
4. Restart TcXaeShell

---

## Troubleshooting / Fehlerbehebung

### "dotnet command not found"
Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

### "VSIXInstaller not found"
Make sure Visual Studio 2022 is installed with the extension development workload.

### "Extension doesn't appear in Visual Studio"
- Check **Extensions** > **Manage Extensions** > **Installed**
- If not listed, try reinstalling the VSIX

### "Format command doesn't work"
- Ensure file extension is `.st`, `.TcPOU`, `.TcDUT`, `.TcGVL`, `.tcio`, or `.tcto`
- Check that the file contains valid ST code
- Try a simple test file first

### "Permission denied on Linux/Mac"
```bash
chmod +x src/STFormatter.CLI/bin/Release/net8.0/STFormatter.CLI
```

### TcXaeShell: "Commands don't appear"
- Clear both cache directories (see deployment Step 4)
- Verify registry entries match the package GUID `{b1c2d3e4-f5a6-7890-abcd-ef1234567890}`
- Ensure the .pkgdef file was copied alongside the DLLs
- Check `%TEMP%\STFormatter_TcXaeShell.log` for errors

---

## Next Steps / Nachste Schritte

- Read the [VSIX Packaging Guide](VSIX-Packaging.md) for distribution
- Read the [TcXaeShell Integration Guide](TcXaeShell-Integration.md) for advanced TcXaeShell topics
- See [ARCHITECTURE.md](ARCHITECTURE.md) for technical details
- See [FORMAT-OPTIONS.md](FORMAT-OPTIONS.md) for configuration reference
- See [README.md](../README.md) for usage examples
- Configure your style in `.editorconfig`