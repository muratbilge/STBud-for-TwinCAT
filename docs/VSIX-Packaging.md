# TwinCAT ST Formatter - VSIX Packaging Guide / VSIX-Verpackungsleitfaden

## Visual Studio 2022 Extension / Visual Studio 2022 Erweiterung

### Prerequisites / Voraussetzungen

- Visual Studio 2022 (Community, Professional, or Enterprise)
- Visual Studio SDK workload installed
- .NET 8 SDK

### Building the VSIX Package / VSIX-Paket erstellen

#### Option 1: Build from Command Line

```powershell
dotnet build TwinCAT.STFormatter.sln -c Release
# The VSIX package:
# src/STFormatter.VSIX/bin/Release/net8.0-windows/TwinCAT.STFormatter.vsix
```

#### Option 2: Build from Visual Studio

1. Open `TwinCAT.STFormatter.sln` in Visual Studio 2022
2. Set configuration to **Release**
3. Right-click `STFormatter.VSIX` project > **Build**
4. The `.vsix` file will be in `src/STFormatter.VSIX/bin/Release/`

#### Option 3: Create Installer Script

```powershell
$version = "1.0.0"
$config = "Release"

dotnet build TwinCAT.STFormatter.sln -c $config

$source = "src/STFormatter.VSIX/bin/$config/net8.0-windows/TwinCAT.STFormatter.vsix"
$output = "publish/TwinCAT.STFormatter.$version.vsix"
New-Item -ItemType Directory -Path "publish" -Force
Copy-Item $source $output -Force
Write-Host "Created: $output"
```

### Installing the VSIX

| Method / Methode | Steps |
|---|---|
| **Double-click** | Double-click the `.vsix` file, click Install, restart VS |
| **Extensions Manager** | VS > Extensions > Manage Extensions > Settings > Install from VSIX |
| **Command Line** | `VSIXInstaller.exe /q TwinCAT.STFormatter.vsix` |

### Testing / Testen

1. Open Visual Studio 2022 (or Experimental Instance for debugging)
2. Open any TwinCAT project
3. Open an `.st`, `.TcPOU`, `.TcDUT`, or `.TcGVL` file
4. Press `Ctrl+K, Ctrl+D` to format document
5. Or press `Ctrl+K, Ctrl+F` to format selection
6. Go to **Tools** > **Options** > **TwinCAT** > **ST Formatter** to configure

### Features / Funktionen

| Feature | How to Use / Verwendung |
|---|---|
| Format Document | `Ctrl+K, Ctrl+D` |
| Format Selection | `Ctrl+K, Ctrl+F` |
| Format on Save | Enable in Options |
| Configure Style | Tools > Options > TwinCAT > ST Formatter |

### Supported File Types / Unterstutzte Dateitypen

| Extension | Description |
|---|---|
| `.st` | Structured Text files |
| `.txt` | Plain text ST files |
| `.iecst` | IEC Structured Text |
| `.TcPOU` | TwinCAT Program Organization Unit |
| `.TcDUT` | TwinCAT Data Unit Type |
| `.TcGVL` | TwinCAT Global Variable List |
| `.TcIO` | TwinCAT IO mapping |
| `.TcTO` | TwinCAT Task Object |

### Troubleshooting / Fehlerbehebung

| Issue / Problem | Solution / Losung |
|---|---|
| Extension not loading | Ensure VS 2022 (17.0+) is installed with VS SDK |
| Format command not working | Verify file extension is supported |
| TwinCAT XML files not formatting | Ensure files are not read-only, check CDATA sections |
| Activity log errors | Check `%LocalAppData%\Microsoft\VisualStudio\17.0_xxxx\ActivityLog.xml` |

### Uninstalling / Deinstallation

1. Visual Studio 2022 > **Extensions** > **Manage Extensions** > **Installed**
2. Find **TwinCAT ST Formatter**
3. Click **Uninstall**
4. Restart Visual Studio

---

## TcXaeShell Extension / TcXaeShell-Erweiterung

### Overview / Uberblick

The TcXaeShell extension cannot be distributed as a VSIX. It requires manual deployment because TcXaeShell is a Visual Studio 2017 Isolated Shell that does not support standard VSIX installation.

### Key Differences / Wichtige Unterschiede

| Aspect | VS 2022 VSIX | TcXaeShell |
|---|---|---|
| Target framework | net48 | net462/net48 x86 (depends on TcXaeShell version) |
| Installation | VSIX installer | Manual copy + registry |
| DLL location | VS extension directory | `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\` |
| Menu resource | VSIX manifest | Registry entry (must be version 1) |
| Beckhoff DLLs | Not needed | Loaded via TwinCAT binding path (Private=false) |
| Architecture | AnyCPU/x64 | x86 (32-bit only) |

### Building / Erstellen

```powershell
dotnet build src/STFormatter.TcXaeShell/STFormatter.TcXaeShell.csproj -c Release
```

Output: `src/STFormatter.TcXaeShell/bin/Release/net48/` (or `net462/` for older TcXaeShell versions)

### Deployment / Bereitstellung

> **Requires Administrator privileges**

```powershell
# Stop TcXaeShell first!
$src = "src\STFormatter.TcXaeShell\bin\Release\net48"  # use net462 for older TcXaeShell
$dst = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"

New-Item -ItemType Directory -Path $dst -Force
Copy-Item "$src\STFormatter.TcXaeShell.dll" $dst -Force
Copy-Item "$src\STFormatter.TcXaeShell.pdb" $dst -Force
Copy-Item "$src\STFormatter.Core.dll" $dst -Force
Copy-Item "$src\STFormatter.Core.pdb" $dst -Force
Copy-Item "$src\STFormatter.TcXaeShell.pkgdef" $dst -Force

# DO NOT copy Beckhoff DLLs - they are loaded from the TwinCAT installation
```

Then import the registry file:

```powershell
reg import register_tcxae.reg
```

Clear caches:

```powershell
# NOTE: Replace "15.0" with your TcXaeShell version (15.0=VS2017, 14.0=VS2015, 12.0=VS2013)
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache"
Remove-Item -Force "$env:LOCALAPPDATA\Beckhoff\TcXaeShell\15.0\Extensions\extensions.en-US.cache"
```

### Registry Entries / Registrierungseintrage

Key entries in `register_tcxae.reg`:

| Registry Key | Purpose |
|---|---|
| `Menus\{pkg-guid}` | Menu resource registration (version must be 1) |
| `Packages\{pkg-guid}` | Package class, codebase, AllowsBackgroundLoad |
| `BindingPaths\{pkg-guid}` | Assembly probing path for Beckhoff DLLs |
| `InstalledProducts` | Product info in About dialog |
| `ToolsOptionsPages\TwinCAT\ST Formatter` | Options page registration |

### Important Notes / Wichtige Hinweise

- **Menu resource version MUST be 1** — changing to 2 breaks command registration
- **Beckhoff DLLs must have Private=false** — they are loaded from the TwinCAT installation via the binding path
- **GeneratePkgDefFile=false** in csproj — pkgdef is generated during build, copy manually
- **ProductArchitecture** must be a child element of `<InstallationTarget>`, not an XML attribute
- **TcXaeShell is 32-bit** — the project must target x86

### Debugging / Fehlerbehebung

Debug log: `%TEMP%\STFormatter_TcXaeShell.log`

Common issues:
- Commands don't appear: Clear caches and check registry
- Format doesn't work: Check log for errors
- Extension crashes: Try attaching a debugger to TcXaeShell.exe

For detailed technical documentation, see [TcXaeShell-Integration.md](TcXaeShell-Integration.md).

---

## Distribution / Verteilung

### Visual Studio Marketplace

1. Create a publisher account at https://marketplace.visualstudio.com/
2. Update `source.extension.vsixmanifest` with publisher info
3. Build the Release VSIX
4. Upload the `.vsix` file to the marketplace

### Internal Distribution / Interne Verteilung

For internal/enterprise distribution:

1. Build Release VSIX
2. Share the `.vsix` file with your team
3. Each developer installs via double-click or Extensions Manager

### TcXaeShell Distribution

For TcXaeShell, create a deployment package:

1. Build Release DLLs and pkgdef
2. Include `register_tcxae.reg`
3. Include an install script (PowerShell) that copies files and imports registry
4. Include an uninstall script that removes files and registry entries

### CI/CD Integration

#### Azure DevOps

```yaml
- task: VSBuild@1
  inputs:
    solution: 'TwinCAT.STFormatter.sln'
    configuration: 'Release'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: '$(Build.SourcesDirectory)/src/STFormatter.VSIX/bin/Release'
    artifactName: 'VSIX'
```

#### GitHub Actions

```yaml
- name: Build
  run: dotnet build --configuration Release

- name: Upload VSIX
  uses: actions/upload-artifact@v3
  with:
    name: VSIX
    path: src/STFormatter.VSIX/bin/Release/net8.0-windows/*.vsix

- name: Upload TcXaeShell
  uses: actions/upload-artifact@v3
  with:
    name: TcXaeShell
        path: src/STFormatter.TcXaeShell/bin/Release/net48/*.dll
```