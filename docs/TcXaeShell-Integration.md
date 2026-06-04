# TcXaeShell Integration

Technical reference for integrating STBud for TwinCAT into Beckhoff's TwinCAT XAE Shell (TcXaeShell).

> **Production approach**: The **external Host process** is the only working integration path. In-process approaches (VSPackage, MEF, AddIn, Automation API) all fail in TcXaeShell's isolated shell. See [AGENTS.md](../AGENTS.md) for the historical failure notes.

---

## Architecture Overview

TcXaeShell is a **32-bit Visual Studio Isolated Shell** used for PLC programming. The shell version varies by TwinCAT build:

| TcXaeShell Version | VS Shell | DTE Version | ROT Moniker | .NET FW |
|---|---|---|---|---|
| TC3 Build 4024+ (current) | VS 2017 | 15.0 | `!TcXaeShell.DTE.15.0:{PID}` | 4.6+ |
| TC3 Build ~4020 | VS 2015 | 14.0 | `!TcXaeShell.DTE.14.0:{PID}` | 4.6+ |
| TC3 Build <4020 | VS 2013 | 12.0 | `!TcXaeShell.DTE.12.0:{PID}` | 4.5.1+ |

- **Process**: 32-bit (x86), even on 64-bit Windows (all versions)
- **TcXaeShell product path**: `C:\Program Files (x86)\Beckhoff\TcXaeShell\` (STBud is not installed here)
- **Shell type**: VS Isolated Shell — not a standard VS installation

The PLC editor is a CODESYS-based component embedded in the VS shell. It does not use standard VS editor infrastructure for its text content. The CODESYS engine is the source of truth, not the VS text buffer.

---

## Failed Approaches

Every approach below was tested and failed. Documented here so future developers do not revisit dead ends.

| Approach | Result | Why |
|---|---|---|
| IECTextEditor / ISingleLineIECTextEditor | Null cast | Not on DocData |
| ITextDocument | Null cast | Not on DocData or DocView |
| IVsTextLines.ReplaceLines | Reverts after ~8s | CODESYS overwrites buffer |
| IVsPersistDocData.ReloadDocData(0) | Closes editor | Destructive to workflow |
| IVsUserData | E_FAIL | No CODESYS objects available |
| IGetManagedObject | Same wrapper | No inner CODESYS object |
| IVsUIShell.GetDocumentWindowEnum | 0 frames | Custom window hosting |
| VSCT context menu commands | Invisible | Beckhoff custom menu GUID |
| IPLCData.Node from external process | DISP_E_UNKNOWNNAME | Runtime props not accessible via COM proxy |
| IVsRunningDocumentTable from external | E_NOINTERFACE | Shell service not reachable externally |
| IVsFileChangeEx.IgnoreFile+SyncFile external | S_OK, no effect | Not processed from external process |
| TextSelection.Text in PLC editor | Empty string | CODESYS editor doesn't use VS text buffer |
| System.Windows.Forms.Clipboard MTA | Exception | Fails from COM callback threads |
| Tab-switching DTE commands | "not valid command" | Not supported in TcXaeShell |
| File write after live edit | Reload dialog | Editor detects external file change |
| VSPackage AutoLoad | Never loaded | Isolated shell doesn't fire UI context events |
| MEF composition | Assembly never in catalog | Extension manager ignores custom MEF components |
| VS AddIn | Never loaded | No AddIn manager in isolated shell |
| TcXaeShell.exe /setup | Exit code -1 | Setup never succeeds |

### Why File-System Approaches Fail for Live Edit

The PLC editor renders text from the CODESYS engine, not from the VS text buffer. Writing to `IVsTextLines`, the file on disk, or any standard VS mechanism does **not** update what the user sees on screen. The CODESYS engine polls its internal model periodically; when it detects the VS text buffer diverges, it overwrites the buffer with its own copy.

Writing the `.TcPOU` file after a live edit triggers TcXaeShell's "file changed on disk" reload dialog. `IVsFileChangeEx.IgnoreFile` + `SyncFile` returns S_OK but has no effect from an external process.

---

## External Host Approach (Production)

Since TcXaeShell's isolated shell blocks VSPackage/MEF/AddIn loading, the production format tool runs as an **external process** that connects via COM DTE.

### Architecture

```
+------------------+     COM DTE (ROT)     +---------------------------+
|  TcXaeShell.exe  | <------------------> |  STFormatter.Host.exe     |
|  (VS 2017 Shell) |                      |  (net48, x86, hidden)    |
|                  |                      |                           |
|  PlcCodeWinCtx   |  inject buttons via  |  - Connects via ROT      |
|  Menu (127 ctrl) |  DTE.CommandBars     |  - Injects buttons        |
|                  |                      |  - Handles click events   |
|  Active text     |  live edit via       |  - Live-format via DTE   |
|  in PLC editor   |  DTE + clipboard     |  - Auto-reconnects       |
+------------------+                      +---------------------------+
```

### Connection via ROT

TcXaeShell registers DTE in the Running Object Table with version-specific monikers. The DTE version is **auto-detected** at runtime via `TcXaeShellVersionProfile.DetectFromRotMoniker()` — do not hard-code a specific version.

Search for both `!TcXaeShell.DTE.` and `!VisualStudio.DTE.` prefixes in the ROT enumeration — only `TcXaeShell.DTE` monikers match TcXaeShell; `VisualStudio.DTE` monikers match regular Visual Studio installations.

```csharp
[DllImport("ole32.dll")]
static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);
[DllImport("ole32.dll")]
static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

IRunningObjectTable rot;
GetRunningObjectTable(0, out rot);
IEnumMoniker enumMoniker;
rot.EnumRunning(out enumMoniker);

IMoniker[] monikers = new IMoniker[1];
while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
{
    IBindCtx ctx;
    CreateBindCtx(0, out ctx);
    monikers[0].GetDisplayName(ctx, null, out string displayName);

    var profile = TcXaeShellVersionProfile.DetectFromRotMoniker(displayName);
    if (profile != null)
    {
        object comObj = rot.GetObject(monikers[0]);
        EnvDTE.DTE dte = comObj as EnvDTE.DTE;
    }
}
```

### Context Menu Injection

Buttons are injected via `DTE.CommandBars` into both:
- `PlcCodeWinContextMenu` — Beckhoff's PLC editor right-click menu (~127 controls)
- `Code Window` — standard VS text editor context menu (~61 controls)

```csharp
var commandBars = (CommandBars)dte.CommandBars;

var plcMenu = commandBars["PlcCodeWinContextMenu"];
var btn1 = (CommandBarButton)plcMenu.Controls.Add(
    MsoControlType.msoControlButton, Missing.Value, Missing.Value,
    Missing.Value, true);
btn1.Caption = "Format ST Document";
btn1.Tag = "STFormatter_FormatDocument";
btn1.Click += OnFormatDocumentClick;

var codeMenu = commandBars["Code Window"];
var btn2 = (CommandBarButton)codeMenu.Controls.Add(
    MsoControlType.msoControlButton, Missing.Value, Missing.Value,
    Missing.Value, true);
btn2.Caption = "Format ST Document";
btn2.Tag = "STFormatter_FormatDocument_Std";
btn2.Click += OnFormatDocumentClick;
```

**Cleanup**: Buttons must be removed when the host exits or the TcXaeShell instance dies. Use per-PID tracking in `HostManager`:

```csharp
CommandBarControl[] stale = FindButtonsWithTag(menu, "STFormatter_FormatDocument");
foreach (var btn in stale)
    btn.Delete(false);
```

### Section-Aware Live Edit

The PLC editor has **two separate tabs** within a single document:
- **Declaration**: The VAR section (variables, type declarations)
- **Implementation**: The ST code body (logic, assignments, control flow)

`DTE.ActiveDocument` sees one document regardless of which tab is active. There is **no DTE API** to determine which tab is currently shown. `TextSelection.Text` returns empty for the CODESYS-based PLC editor. Tab-switching DTE commands (`View.Declaration`, `Project.OpenImplementation`) all fail in TcXaeShell.

The working approach formats only the **currently visible tab** by reading it via clipboard, detecting its type, and pasting back the formatted result:

```
1. Save clipboard
2. Edit.SelectAll -> Edit.Copy -> read clipboard (Win32 API)
3. Detect: Declaration or Implementation?
4. Format with the correct method
5. Set clipboard with formatted text
6. Edit.Delete -> Edit.Paste (selection is still SelectAll)
7. UndoContext wraps as single undo
8. Restore clipboard
```

#### Read the Active Section Text

`TextSelection.Text` returns empty in the PLC editor. Use SelectAll+Copy to read the text onto the clipboard instead. **Critical**: Use Win32 clipboard API for both read and write. `System.Windows.Forms.Clipboard` fails from MTA COM callback threads.

```csharp
static string? GetClipboardText()
{
    if (!OpenClipboard(IntPtr.Zero)) return null;
    try
    {
        IntPtr handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;
        IntPtr ptr = GlobalLock(handle);
        int size = (int)GlobalSize(handle);
        string text = Marshal.PtrToStringUni(ptr);
        GlobalUnlock(handle);
        return text;
    }
    finally { CloseClipboard(); }
}

static bool SetClipboardText(string text)
{
    if (!OpenClipboard(IntPtr.Zero)) return false;
    try
    {
        EmptyClipboard();
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (IntPtr)bytes.Length);
        IntPtr ptr = GlobalLock(hMem);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        GlobalUnlock(hMem);
        SetClipboardData(CF_UNICODETEXT, hMem);
        return true;
    }
    finally { CloseClipboard(); }
}
```

#### Detect Declaration vs Implementation

Heuristic detection based on the ST code content:

```csharp
static bool LooksLikeDeclaration(string text)
{
    if (string.IsNullOrEmpty(text)) return true;
    string upper = text.ToUpperInvariant();
    bool hasVar = upper.Contains("VAR") && upper.Contains("END_VAR");
    bool hasBodyKeywords = upper.Contains("IF ") || upper.Contains("FOR ") ||
                            upper.Contains("WHILE ") || upper.Contains(":=") ||
                            upper.Contains("THEN");

    if (hasVar && !hasBodyKeywords) return true;
    if (hasBodyKeywords && !hasVar) return false;
    bool hasPouHeader = upper.Contains("PROGRAM") || upper.Contains("FUNCTION_BLOCK") ||
                         upper.Contains("FUNCTION");
    if (hasPouHeader) return true;
    return false;
}
```

#### Format and Paste

Use `Format()` for declaration text and `FormatBody()` for implementation text:

```csharp
var engine = new FormattingEngine(FormattingConfiguration.Default);
bool isDecl = LooksLikeDeclaration(currentText);

string formatted;
if (isDecl)
    formatted = engine.Format(currentText);
else
    formatted = engine.FormatBody(currentText);

if (formatted == currentText) return true;

SetClipboardText(formatted);
dte.ExecuteCommand("Edit.Delete", "");
dte.ExecuteCommand("Edit.Paste", "");

if (savedClipboard != null)
    SetClipboardText(savedClipboard);
```

### Undo Support

Wrap the entire edit in a single undo context so the user can undo the format operation with one Ctrl+Z:

```csharp
bool undoContextOpened = false;
try
{
    if (!dte.UndoContext.IsOpen)
    {
        dte.UndoContext.Open("Format ST Document");
        undoContextOpened = true;
    }
    // ... SelectAll + Copy + Detect + Format + Delete + Paste ...
}
finally
{
    if (undoContextOpened)
        dte.UndoContext.Close();
}
```

### Formatting Tier Cascade

| Tier | Method | Status |
|---|---|---|
| 1 | Automation API (IPLCData.Node.SysManTreeItem) | FAILS — `DISP_E_UNKNOWNNAME` from external process |
| 2 | DTE ExecuteCommand + Clipboard (live edit) | **WORKS** — production approach |
| 3 | SendKeys fallback | Works but less reliable |
| 4 | IVsFileChangeEx + RDT file write | FAILS — `E_NOINTERFACE` from external process |
| 5 | Plain file write (CDATA replacement) | Works but triggers reload dialog |

**Tier 2 is the production approach.** Never use Tier 5 after a live edit — the editor and the file are already in sync.

### Process Lifetime

- Host runs as a **hidden console process** (`<OutputType>Exe</OutputType>` + P/Invoke `ShowWindow(GetConsoleWindow(), 0)`)
- NOT `WinExe` — that output type causes immediate silent exit
- Must survive TcXaeShell restarts (auto-reconnect loop polling the ROT)
- `STAThread` + `Application.DoEvents()` message pump for COM event dispatch
- Per-PID tracking via `HostManager` prevents duplicate buttons
- Clean up injected `CommandBarControl` via `.Delete(false)` on shutdown
- Set `_dte = null` when TcXaeShell exits; poll ROT for new instance

---

## Cross-Version Compatibility

TcXaeShell ships in multiple versions depending on the TwinCAT 3 build. The external Host must detect and adapt to the running version at runtime. All version-specific values are encapsulated in `TcXaeShellVersionProfile` (in `STFormatter.Core/Configuration/`).

### TcXaeShellVersionProfile

| Property | Purpose | Varies by Version? |
|---|---|---|
| `DteVersion` | DTE version string (e.g. `"15.0"`, `"14.0"`, `"12.0"`) | Yes |
| `VsShellGeneration` | VS shell generation (e.g. `"2017"`, `"2015"`, `"2013"`) | Yes |
| `PrimaryRotMonikerPrefix` | `!TcXaeShell.DTE.{version}:` | Yes |
| `FallbackRotMonikerPrefix` | `!VisualStudio.DTE.{version}:` | Yes |
| `RequiredFramework` | Minimum .NET Framework (e.g. `"4.6"`, `"4.5.1"`) | Yes |
| `TargetContextMenuNames` | Context menu names | No (consistent) |
| `TwinCatFileExtensions` | File extensions (.TcPOU, .TcDUT, etc.) | No (consistent) |
| `ProcessName` | Always `"TcXaeShell"` | No |
| `InstallPathPattern` | Always `Beckhoff\TcXaeShell\Common7\IDE\` | No |

### Auto-Detection

`TcXaeShellVersionProfile.DetectFromRotMoniker(string displayName)` automatically identifies the TcXaeShell version from the ROT moniker:

1. Checks all known profiles (VS2017, VS2015, VS2013) for prefix matches
2. Falls back to dynamic version parsing for unrecognized `!TcXaeShell.DTE.{version}:{PID}` or `!VisualStudio.DTE.{version}:{PID}` monikers
3. Returns `null` for non-TcXaeShell monikers

### Stable Cross-Version Constants

These are consistent across all TcXaeShell versions:

- DTE commands: `Edit.SelectAll`, `Edit.Copy`, `Edit.Delete`, `Edit.Paste`, `Edit.SelectionCancel`
- Context menu names: `PlcCodeWinContextMenu`, `Code Window`
- File extensions: `.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO`, `.TcTO`
- Process name: `TcXaeShell`
- Bitness: Always x86 (32-bit)
- `Microsoft.VisualStudio.Interop` v17.0.32112.339 — backward-compatible for DTE COM access

---

## File Persistence

TwinCAT stores POU content in XML files with CDATA sections:

| File Extension | Content | CDATA Sections |
|---|---|---|
| `.TcPOU` | Programs, functions, function blocks | `Declaration`, `Implementation` |
| `.TcDUT` | Data type definitions (DUT) | `Declaration` |
| `.TcGVL` | Global variable lists (GVL) | `Declaration` |

### CDATA Replacement Strategy

When persisting formatted code to disk (fallback only — triggers reload dialog), use regex-based CDATA replacement:

```csharp
static string ReplaceCdataSection(string content, string sectionName, string newText)
{
    string pattern = $@"(<{sectionName}>)\s*<!\[CDATA\[.*?\]\]>\s*(</{sectionName}>)";
    string replacement = $"$1<![CDATA[{newText}]]>$2";
    return Regex.Replace(content, pattern, replacement, RegexOptions.Singleline);
}

// For .TcPOU files: replace both Declaration and Implementation
content = ReplaceCdataSection(content, "Declaration", formattedDeclaration);

// Implementation uses nested structure: <Implementation><ST><![CDATA[...]]></ST></Implementation>
string implPattern = @"(<ST>)\s*<!\[CDATA\[.*?\]\]>\s*(</ST>)";
content = Regex.Replace(content, implPattern,
    $"$1<![CDATA[{formattedImplementation}]]>$2", RegexOptions.Singleline);
```

---

## Deployment

Deploy these files (use `deploy.bat`, requires admin privileges):

```
C:\Program Files (x86)\STBud\
  STFormatter.Host.exe
  STFormatter.Core.dll
  STFormatter.UI.dll
  Microsoft.VisualStudio.Interop.dll
```

> **Target framework**: The Host is built for `net48` by default. For older machines running .NET 4.6.2, build with the `net462` target instead.

```powershell
Start-Process "C:\Program Files (x86)\STBud\STFormatter.Host.exe"
```

Log file: `%TEMP%\STBud_Host.log`

---

## Logging

Debug log file: `%TEMP%\STBud_Host.log`

### Diagnostic Steps

1. **Host not connecting**: Check log for ROT scan results. Verify TcXaeShell is running.
2. **Buttons not appearing**: Check log for "InjectButtons" entries. Verify the Host is running.
3. **Format does nothing**: Check log for "Read 0 chars" — clipboard read may have failed.
4. **Wrong section formatted**: Check log for "Detected as Declaration/Implementation".
5. **Reload dialog appears**: A file write happened after live edit — verify no fallback CDATA replacement is being triggered.
6. **Editor reverts changes**: Ensure you are using the ExecuteCommand live edit, not IVsTextLines or file writes.

---

## Quick Reference

### External Host (Production)

```
ROT -> !TcXaeShell.DTE.{version}:{PID}  (version auto-detected via TcXaeShellVersionProfile)
  -> DTE.CommandBars["PlcCodeWinContextMenu"] -> Inject buttons
  -> Button click:
    -> Edit.SelectAll -> Edit.Copy -> Win32 clipboard read
    -> LooksLikeDeclaration() -> Format() or FormatBody()
    -> SetClipboardText -> Edit.Delete -> Edit.Paste
    -> UndoContext (single undo)
```

### Deployment Checklist

1. Build: `dotnet build src\STFormatter.Host -c Debug`
2. Deploy: `.\deploy.bat` (or `.\deploy.bat net462` for older TcXaeShell)
3. Start: Run `STFormatter.Host.exe` — it auto-connects to TcXaeShell
4. Verify: Right-click in PLC editor — "Format ST Document" should appear
