# AGENTS.md — TcXaeShell Extension Development Survival Guide

This file captures hard-won knowledge about integrating with Beckhoff's TwinCAT XAE Shell
that **every AI agent and developer working on this project must know**.

## The Golden Rule

**DO NOT try to build a VSPackage, MEF component, or VS AddIn for TcXaeShell.**
All three mechanisms are broken in TcXaeShell's VS 2017 Isolated Shell.
The ONLY approach that works is an **external process that connects via COM DTE**.

## Why Standard VS Extension Approaches Fail

TcXaeShell is a **VS 2017 Isolated Shell (32-bit, .NET Framework 4.6)** at:
`C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\`

### VSPackage (with [PackageRegistration], [ProvideAutoLoad], etc.)

| What we tried | Result | Why |
|---|---|---|
| Package with `ProvideAutoLoad(UIContextGuids.NoSolution)` | Package never loaded | TcXaeShell's isolated shell doesn't fire UI context events |
| Package with AutoLoad registry entries | Package never loaded | Shell ignores HKLM AutoLoad subkeys |
| Package with `SupportsDynamicToolOwner` | Package never loaded | No dynamic tool window triggers |
| Package with Edit menu button placement | Package never loaded | Menu placement queries don't trigger package load |

### MEF Composition

| What we tried | Result | Why |
|---|---|---|
| `<MefComponent>` in extension.vsixmanifest | Assembly never added to MEF catalog | Catalog contains only ~81 assemblies from Microsoft bundles |
| v1 and v2 vsixmanifest formats | Same result | Extension manager ignores custom MEF components |
| Absolute paths for MefComponent | Same result | TcXaeShell doesn't process MefComponent from custom extensions |
| Registry binding paths | Assembly still not in catalog | Binding path only helps AFTER package loads (chicken-and-egg) |

The MEF catalog file is at:
```
%LOCALAPPDATA%\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache\Microsoft.VisualStudio.Default.catalogs
```
Our assembly was **never** in this catalog — even though the extension manager showed our
extension as "Successfully loaded" and "Enabled" in the Activity Log.

### VS AddIn (IDTExtensibility2)

| What we tried | Result | Why |
|---|---|---|
| `.AddIn` file in IDE\AddIns folder | AddIn never loaded | TcXaeShell doesn't process .AddIn files |
| Registry AddIn registration | AddIn never loaded | No AddIn manager service in isolated shell |

### TcXaeShell /setup

Running `TcXaeShell.exe /setup` returns exit code **-1** (failure). It never regenerates
the ComponentModelCache, pkgdef merge, or MEF catalog. This means **new custom extensions
cannot be properly registered** in TcXaeShell.

## The Working Solution: External Host Process via COM DTE

### Architecture

```
+------------------+     COM DTE (ROT)     +---------------------------+
|  TcXaeShell.exe  | <------------------> |  STFormatter.Host.exe     |
|  (VS 2017 Shell) |                      |  (net48, x86, hidden)    |
|                  |                      |                           |
|  PlcCodeWinCtx   |  inject buttons via  |  - Connects via ROT      |
|  Menu (127 ctrl) |  DTE.CommandBars     |  - Injects buttons        |
|                  |                      |  - Handles click events   |
|  .TcPOU files    |  backup via          |  - Formats active section |
|  (XML on disk)   |  System.IO           |  - Live edit via DTE cmds |
|                  |                      |  - Auto-reconnects       |
+------------------+                      +---------------------------+
```

### Key Technical Details

#### ROT Moniker Name (CRITICAL)
TcXaeShell registers in the Running Object Table with version-specific monikers:
```
!TcXaeShell.DTE.15.0:{PID}  (VS 2017 shell, current)
!TcXaeShell.DTE.14.0:{PID}  (VS 2015 shell, older)
!TcXaeShell.DTE.12.0:{PID}  (VS 2013 shell, oldest)
```
The `TcXaeShellVersionProfile.DetectFromRotMoniker()` method handles all versions.
Searching for `!VisualStudio.DTE` alone will miss TcXaeShell — always also search `!TcXaeShell.DTE`.

**Build 4026 / Visual Studio 2022 (verified):** 4026 adds two more environments.
The 4026 standalone shell still uses `!TcXaeShell.DTE.17.0:{PID}` (handled by the
dynamic fallback). TwinCAT can also load into **Visual Studio 2022** (`devenv`), which
registers `!VisualStudio.DTE.17.0:{PID}` with `DTE.Name = "Microsoft Visual Studio"` —
so a **name-based check does NOT recognize it**. Detect TwinCAT-in-VS2022 by the
presence of the `PlcCodeWinContextMenu` command bar (the Beckhoff PLC editor menu);
a plain VS 2022 does not have it. This is `HostManager.IsTwinCatEngineering()` /
`HasPlcContextMenu()`. Injection is otherwise identical across all three environments.

#### DTE Connection Code
```csharp
[DllImport("ole32.dll")]
static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);
[DllImport("ole32.dll")]
static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

// Search for both monikers:
if (displayName.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase) ||
    displayName.StartsWith("!TcXaeShell.DTE.", StringComparison.OrdinalIgnoreCase))
{
    // Get COM object and check IsTcXaeShell()
}
```

#### Context Menu Injection
```csharp
// The Beckhoff PLC editor context menu name:
var cb = commandBars["PlcCodeWinContextMenu"]; // 127 controls

// Standard VS editor context menu:
var cb2 = commandBars["Code Window"]; // ~47 controls

// Add buttons:
var btn = (CommandBarButton)cb.Controls.Add(MsoControlType.msoControlButton, ...);
btn.Caption = "Format ST Document";
btn.Click += OnFormatDocumentClick;
```

#### Formatting ST Code from External Process (LIVE EDIT)
The Automation API (`IPLCData.Node.SysManTreeItem` → `DeclarationText`/`ImplementationText`)
does NOT work from an external COM process. External DTE access cannot resolve
the runtime-only properties on Beckhoff's COM objects.

**Working approach**: Section-aware live edit via DTE ExecuteCommand + Win32 clipboard:

The PLC editor has two tabs (Declaration and Implementation) in one document.
There is no DTE API to detect which tab is active. The solution:

1. **Read** active text: `Edit.SelectAll` → `Edit.Copy` → read via Win32 clipboard API
2. **Detect** section: `LooksLikeDeclaration()` heuristic (VAR/END_VAR → decl, IF/FOR/:= → impl)
3. **Format** inline: `FormattingEngine.Format()` for declaration, `.FormatBody()` for implementation
4. **Paste** back: `SetClipboardText(formatted)` → `Edit.Delete` → `Edit.Paste`
5. **Restore** original clipboard content

```
User clicks "Format ST Document"
  → Host reads active section via SelectAll+Copy+clipboard
  → Detects Declaration vs Implementation from content
  → Formats with correct method (Format vs FormatBody)
  → SelectAll (still active) → Delete → Paste formatted text
  → UndoContext wraps as single undo action
```

**Critical constraints**:
- `TextSelection.Text` returns empty in the CODESYS PLC editor — must use clipboard
- `System.Windows.Forms.Clipboard` fails from MTA COM callback threads — must use Win32 API
- Never write to disk after live edit — triggers "file changed on disk" reload dialog
- Tab-switching DTE commands (`View.Declaration`, `Project.OpenImplementation`) all fail
- UndoContext wraps the entire edit as a single undoable operation

**Fallback approach**: File-system CDATA replacement (causes reload dialog, not recommended):
```csharp
File.Copy(filePath, filePath + ".bak", true); // backup
File.WriteAllText(filePath, formattedXml);
```

#### Process Lifetime
- The Host MUST run as a **hidden console process** (Exe output type, hide window via P/Invoke)
- It must survive TcXaeShell restart (set `_dte = null` on shutdown, poll for new instance)
- Uses `STAThread` + `Application.DoEvents()` message pump for COM event handling
- Cleanup: `CommandBarControl.Delete(false)` when exiting

#### Dependencies for Deployment
```
C:\Program Files (x86)\STBud\
  STFormatter.Host.exe
  STFormatter.Core.dll
  STFormatter.UI.dll
  Microsoft.VisualStudio.Interop.dll    ← MUST be deployed alongside
```

### Project Reference
```
NuGet: Microsoft.VisualStudio.Interop 17.0.32112.339
  (unified interop = EnvDTE + CommandBars + VS interop in one DLL)
```

### Why "WinExe" Output Type Fails
Setting `<OutputType>WinExe</OutputType>` causes immediate exit without error.
Instead use `<OutputType>Exe</OutputType>` and hide the console window:
```csharp
var handle = GetConsoleWindow();
ShowWindow(handle, 0); // SW_HIDE
```

## Files Created/Modified for This Approach

| File | Purpose |
|---|---|
| `src/STFormatter.Host/Program.cs` | Main host executable |
| `src/STFormatter.Host/STFormatter.Host.csproj` | Project (net48, x86, Exe) |
| Deleted: `AutoStartup.cs` | Failed MEF export attempt |
| Deleted: `Commands/FormatCommands.cs` | Old VSPackage command classes |
| Deleted: `Commands/ContextMenuInjector.cs` | Old in-process injector |
| Deleted: `STFormatterPackage.cs` | Old VSPackage class |
| Deleted: `Connect.cs` | Failed AddIn attempt |

## Never Waste Time On These Again

1. ❌ VSPackage loading via `ProvideAutoLoad` or registry AutoLoad keys in TcXaeShell
2. ❌ MEF composition via `extension.vsixmanifest` `<MefComponent>` in TcXaeShell
3. ❌ VS AddIn via `.AddIn` files in TcXaeShell
4. ❌ `TcXaeShell.exe /setup` — it always fails with exit code -1
5. ❌ `IPLCData.Node.SysManTreeItem` reflection from external COM DTE process
6. ❌ WinExe output type for background processes
7. ❌ `!VisualStudio.DTE.15.0` moniker — use `!TcXaeShell.DTE.15.0`
8. ❌ `TextSelection.Text` in CODESYS PLC editor — always returns empty
9. ❌ `System.Windows.Forms.Clipboard` from MTA COM callback threads — use Win32 API
10. ❌ Tab-switching DTE commands (`View.Declaration`, `Project.OpenImplementation`) in TcXaeShell
11. ❌ Writing to disk after live edit — triggers "file changed on disk" reload dialog
12. ❌ IVsFileChangeEx.IgnoreFile + SyncFile from external process — returns S_OK but no effect
13. ❌ IVsRunningDocumentTable from external process — returns E_NOINTERFACE

## Always Do These

1. ✅ Use external process + COM DTE for TcXaeShell context menu injection
2. ✅ Use DTE ExecuteCommand live edit (SelectAll+Copy+clipboard+Delete+Paste) for formatting
3. ✅ Read active section via clipboard, detect Declaration vs Implementation from content
4. ✅ Use `FormattingEngine.Format()` for declaration, `.FormatBody()` for implementation
5. ✅ Use Win32 clipboard API (OpenClipboard/SetClipboardData) — NOT System.Windows.Forms.Clipboard
6. ✅ Wrap live edits in UndoContext for single undo
7. ✅ Search ROT for both `!TcXaeShell.DTE` and `!VisualStudio.DTE` monikers
8. ✅ Deploy `Microsoft.VisualStudio.Interop.dll` alongside the Host.exe
9. ✅ Hide console window via P/Invoke (not WinExe)
10. ✅ Implement auto-reconnect when TcXaeShell restarts
11. ✅ Create `.bak` backup before overwriting TwinCAT XML files (fallback only)
12. ✅ Log everything to `%TEMP%\STBud_Host.log`

## How to Build and Test

```powershell
# Build (net48 for modern machines, net462 for older TcXaeShell)
dotnet build src/STFormatter.Host/STFormatter.Host.csproj -c Debug

# Deploy net48 (default — for machines with .NET 4.8+)
deploy.bat

# Deploy net462 (for machines with only .NET 4.6.2)
deploy.bat net462

# Run
Start-Process "C:\Program Files (x86)\STBud\STFormatter.Host.exe"

# Check log
Get-Content "$env:TEMP\STBud_Host.log" -Tail 20
```

## Cross-Version Compatibility

### TcXaeShell Version Matrix

| TcXaeShell Generation | VS Shell | DTE Version | ROT Moniker | .NET FW | Registry Root |
|---|---|---|---|---|---|
| TC3 Build 4026 (VS 2022) | VS 2022 / devenv | 17.0 | `!VisualStudio.DTE.17.0:{PID}` (name "Microsoft Visual Studio" — detect via `PlcCodeWinContextMenu`) | 4.8 | n/a |
| TC3 Build 4026 (shell) | VS 2022-based shell | 17.0 | `!TcXaeShell.DTE.17.0:{PID}` (dynamic fallback) | 4.8 | `Beckhoff\TcXaeShell\17.0` |
| TC3 Build 4024+ | VS 2017 | 15.0 | `!TcXaeShell.DTE.15.0:{PID}` | 4.6+ | `Beckhoff\TcXaeShell\15.0` |
| TC3 Build ~4020 | VS 2015 | 14.0 | `!TcXaeShell.DTE.14.0:{PID}` | 4.6+ | `Beckhoff\TcXaeShell\14.0` |
| TC3 Build <4020 | VS 2013 | 12.0 | `!TcXaeShell.DTE.12.0:{PID}` | 4.5.1+ | `Beckhoff\TcXaeShell\12.0` |

### TcXaeShellVersionProfile

All version-specific values are encapsulated in `TcXaeShellVersionProfile` (in `STFormatter.Core/Configuration/`):

- **DTE version strings** (12.0, 14.0, 15.0) — auto-detected from ROT moniker at runtime
- **ROT moniker prefixes** — `!TcXaeShell.DTE.{version}.` and `!VisualStudio.DTE.{version}.`
- **Context menu names** — `PlcCodeWinContextMenu`, `Code Window` (consistent across versions)
- **Target framework** — net462 or net48 (Host/UI projects multi-target both)
- **File extensions** — `.TcPOU/.TcDUT/.TcGVL/.TcIO/.TcTO` (consistent across versions)
- **Install path** — `Beckhoff\TcXaeShell\Common7\IDE\`
- **Process name** — `TcXaeShell`
- **Bitness** — Always x86 (32-bit)

### Auto-Detection

The Host scans the ROT for ALL known TcXaeShell moniker patterns:
1. `!TcXaeShell.DTE.{version}.{PID}` (primary)
2. `!VisualStudio.DTE.{version}.{PID}` (fallback)
3. Any unrecognized TcXaeShell/VisualStudio DTE moniker (dynamic version parsing)

### Safe Hard-Codings (DO NOT change)

- `Edit.SelectAll`, `Edit.Copy`, `Edit.Delete`, `Edit.Paste`, `Edit.SelectionCancel` — standard VS DTE commands, stable across all versions
- COM service GUIDs — VS SDK GUIDs, stable across all versions
- `Microsoft.VisualStudio.Interop` v17.0.32112.339 — backward-compatible for DTE COM access
- x86 platform target — all TcXaeShell versions are 32-bit

## CRITICAL: External Host Directory Separation

**STBud Host files must live outside Beckhoff's TcXaeShell installation.** STBud is a toolbox, not a TcXaeShell extension package.

Current deployment target:
```
C:\Program Files (x86)\STBud\
  STFormatter.Host.exe
  STFormatter.Core.dll
  STFormatter.UI.dll
  Microsoft.VisualStudio.Interop.dll
```

TcXaeShell's extension folders are Beckhoff-owned and must not be used for STFormatter deployment.

TcXaeShell's extensions directory structure:
```
Extensions\
  Beckhoff Automation GmbH\
    TwinCAT XAE Plc\      ← Beckhoff metadata only (manifest/icon/pkgdef/png)
    TwinCAT XAE Base\     ← Base extension
    ...
```

**What went wrong**: During earlier development, Beckhoff PLC DLLs were accidentally copied into `Extensions\STFormatter\`, and a duplicate `Beckhoff Automation GmbH\STFormatter\` directory was created with VSPackage artifacts (`STFormatter.TcXaeShell.dll`, `.pkgdef`, `extension.vsixmanifest`). We then made the problem worse by copying old PLC DLLs into `Extensions\Beckhoff Automation GmbH\TwinCAT XAE Plc\`. This broke TcXaeShell's PLC project creation because:
1. `Extensions\Beckhoff Automation GmbH\TwinCAT XAE Plc\` is supposed to contain only four metadata files: `extension.vsixmanifest`, `TwinCAT XAE Plc.ico`, `TwinCAT XAE Plc.pkgdef`, and `TwinCAT XAE Plc.png`
2. The actual PLC binaries load from `C:\TwinCAT\3.1\Components\Plc\Common\` via pkgdef `CodeBase`, not from the TcXaeShell `Extensions` folder
3. The VSPackage `.pkgdef` tried to register a broken package, corrupting the extension/cache state

**Recovery**: `tools\fix-tcxeshell.ps1` restores the correct state. It:
1. Removes extra DLLs from `Beckhoff Automation GmbH\TwinCAT XAE Plc\`, restoring it to metadata-only state
2. Removes the duplicate `Beckhoff Automation GmbH\STFormatter\` directory
3. Removes the old `Extensions\STFormatter\` deployment directory
4. Verifies PLC runtime files under `C:\TwinCAT\3.1\Components\Plc\Common\`
5. Clears the MEF cache and extension caches to force rebuild
6. Removes stale VSPackage extension registration entries from `HKCU\Software\Beckhoff\TcXaeShell\15.0*\ExtensionManager\ExtensionAutoUpdateEnrollment` (the key `TwinCAT.STFormatter.TcXaeShell.c5d6e7f8-a9b0-4c1d-8e2f-3a4b5c6d7e8f`)
7. Clears extension cache hashes to force TcXaeShell to rebuild its extension list
8. Removes stale old-install autostart state if present

**Rules for deployment**:
- `deploy.bat` copies ONLY Host runtime files into `C:\Program Files (x86)\STBud\`
- NEVER deploy STFormatter files into TcXaeShell's `Extensions` tree
- NEVER copy PLC DLLs into `Extensions\Beckhoff Automation GmbH\TwinCAT XAE Plc\`; keep it metadata-only
- NEVER create directories under `Extensions\Beckhoff Automation GmbH\`
- The installer (`STFormatter-Setup.iss`) does not inspect or clean TcXaeShell folders; use `tools\fix-tcxeshell.ps1` manually for historical cleanup
