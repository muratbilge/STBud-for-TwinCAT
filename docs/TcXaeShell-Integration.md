# TcXaeShell Integration / TcXaeShell-Integration

This document captures hard-won technical knowledge about integrating the ST Formatter into Beckhoff's TwinCAT XAE Shell (TcXaeShell). The PLC editor in TcXaeShell is fundamentally different from a standard Visual Studio text editor. Most conventional VS SDK approaches fail. This document records what fails, why it fails, and the working solutions.

> **Important**: The VSPackage / in-process approach documented in sections "The Working Solution: TwinCAT Automation API" and "VS Package Configuration" only works when the package CAN be loaded. Since TcXaeShell's isolated shell **does not load custom VSPackages**, those sections are preserved for historical reference only. The **production approach** is the **external Host process** described in the "External Host Approach" section.
>
> Also see [HOW-TO-INSTALL.md](HOW-TO-INSTALL.md) for step-by-step deployment and [HOW-TO-USE.md](HOW-TO-USE.md) for usage instructions.

---

## Architecture Overview / Architekturuebersicht

TcXaeShell is a **32-bit Visual Studio Isolated Shell** used for PLC programming. The specific shell version varies by TwinCAT build. Key characteristics vary by version — see the Cross-Version Compatibility section and `TcXaeShellVersionProfile` for details:

| TcXaeShell Version | VS Shell | DTE Version | ROT Moniker | .NET FW | Registry Root |
|---|---|---|---|---|---|
| TC3 Build 4024+ (current) | VS 2017 | 15.0 | `!TcXaeShell.DTE.15.0:{PID}` | 4.6+ | `Software\Beckhoff\TcXaeShell\15.0` |
| TC3 Build ~4020 | VS 2015 | 14.0 | `!TcXaeShell.DTE.14.0:{PID}` | 4.6+ | `Software\Beckhoff\TcXaeShell\14.0` |
| TC3 Build <4020 | VS 2013 | 12.0 | `!TcXaeShell.DTE.12.0:{PID}` | 4.5.1+ | `Software\Beckhoff\TcXaeShell\12.0` |

- **Process**: 32-bit (x86), even on 64-bit Windows (all versions)
- **Install path**: `C:\Program Files (x86)\Beckhoff\TcXaeShell\`
- **Shell type**: VS Isolated Shell — not a standard VS installation

The PLC editor is a CODESYS-based component embedded in the VS shell. It does not use standard VS editor infrastructure for its text content. The CODESYS engine is the source of truth, not the VS text buffer.

---

## The Live Update Problem / Das Live-Update-Problem

The central challenge: the PLC editor renders text from the CODESYS engine, not from the VS text buffer. Writing to `IVsTextLines`, the file on disk, or any standard VS mechanism does **not** update what the user sees on screen.

The editor pipeline works as follows:

```
CODESYS Engine (source of truth)
        |
        v
  PlcDocDataObject (DocData, wrapper only)
        |
        v
  PlcControl (visual rendering)
        |
        v
  User sees text on screen
```

Writing to `IVsTextLines` merely updates a buffer that the CODESYS engine overwrites on its next sync cycle (~8 seconds). The visual editor remains unchanged.

---

## Failed Approaches / Gescheiterte Ansaetze

Every approach below was tested and failed. The reasons are documented so that future developers do not revisit dead ends.

### 1. IECTextEditor / ISingleLineIECTextEditor

**Interfaces**: `IECTextEditor`, `ISingleLineIECTextEditor`  
**Outcome**: Not accessible from the VS shell.

These CODESYS editor interfaces exist in `IECTextEditor.dll`, but they are not reachable from the running DocData. The `PlcDocDataObject` does not implement them, and there is no service or provider that yields them.

```csharp
// FAILED: These casts always return null
var iecEditor = docData as IECTextEditor;           // null
var singleLine = docData as ISingleLineIECTextEditor; // null
```

### 2. ITextDocument

**Interface**: `ITextDocument`  
**Outcome**: Not on DocData, not on DocView.

Neither `PlcDocDataObject` nor the DocView (which is an `IntPtr` / window handle, not a managed object) implements `ITextDocument`. The CODESYS editor does not participate in the VS editor framework's text management.

```csharp
// FAILED: ITextDocument is not available
var textDoc = docData as ITextDocument; // null
```

### 3. IVsTextLines.ReplaceLines

**Interface**: `IVsTextLines.ReplaceLines`  
**Outcome**: Returns `S_OK` but the editor reverts after ~8 seconds.

`ReplaceLines` succeeds at the COM level — the buffer contents change — but the CODESYS engine detects the mismatch on its next sync cycle and overwrites the buffer with the original text. The editor visually reverts.

```csharp
// FAILED: Appears to work, reverts after ~8 seconds
int hr = textLines.ReplaceLines(
    startLine, startIdx, endLine, endIdx,
    newText, newText.Length, out var span);
// hr == S_OK, but the visual editor reverts
```

#### Why it reverts

The CODESYS engine polls its internal model periodically. When it detects that the VS text buffer diverges from its internal representation, it overwrites the buffer with its own copy. This is by design — the engine is the authority.

### 4. IVsPersistDocData.ReloadDocData(0)

**Interface**: `IVsPersistDocData.ReloadDocData`  
**Outcome**: Closes the editor window entirely.

Calling `ReloadDocData(0)` does not reload the document in place. Instead, it closes the editor tab. This is destructive to the user's workflow.

```csharp
// FAILED: Closes the editor window
var persistDocData = docData as IVsPersistDocData;
int hr = persistDocData.ReloadDocData(0);
// The editor tab closes
```

### 5. IVsUserData

**Interface**: `IVsUserData`  
**Outcome**: Returns `E_FAIL` (0x80004001).

No CODESYS objects are available through the `IVsUserData` interface on the text buffer. The GUID-based lookups that work in standard VS editors return `E_FAIL` here.

```csharp
// FAILED: Always returns E_FAIL
var userData = textLines as IVsUserData;
var guid = someKnownGuid;
int hr = userData.GetData(ref guid, out var data);
// hr == 0x80004001 (E_FAIL)
```

### 6. IGetManagedObject

**Interface**: `IGetManagedObject`  
**Outcome**: Returns a `GCHandle` to the same `PlcDocDataObject`. No inner CODESYS object is revealed.

The managed object behind the DocData is `PlcDocDataObject` itself — there is no inner CODESYS object accessible through this path.

```csharp
// FAILED: Returns the same wrapper, not an inner object
var getManaged = docData as IGetManagedObject;
object managed = getManaged.GetManagedObject();
// managed is PlcDocDataObject — no CODESYS internals
```

### 7. IVsUIShell.GetDocumentWindowEnum

**Interface**: `IVsUIShell.GetDocumentWindowEnum`  
**Outcome**: Returns zero frames.

TcXaeShell's PLC editor does not use standard VS document window frames. It renders within a custom window managed by Beckhoff's `PlcControl`. The frame enumeration returns nothing.

```csharp
// FAILED: No frames returned
var uiShell = GetService(typeof(SVsUIShell)) as IVsUIShell;
uiShell.GetDocumentWindowEnum(out var frameEnum);
// frameEnum returns 0 frames
```

### 8. Context Menu Integration / Kontextmenue-Integration

**Beckhoff custom context menu GUID**: `{3b11520b-7e70-4008-a6cf-b60ae84e12b1}`  
**Outcome**: Adding commands to the VSCT does not make them appear in the PLC editor's context menu.

The PLC editor uses Beckhoff's custom context menu, not the standard VS editor context menu. Commands defined in the VSCT with standard editor context menu placements are invisible here.

To add a command to the PLC editor context menu, you must target the Beckhoff menu GUID directly, or use a top-level menu placement (e.g., the **Edit** menu) instead.

---

## The Working Solution: TwinCAT Automation API / Die funktionierende Loesung: TwinCAT Automation API

The path that works uses the **TwinCAT Automation Interface** (TCatSysManagerLib), accessed through the `IPLCData` interface on the DocData.

### Step-by-Step Path / Schritt-fuer-Schritt-Pfad

```
IVsRunningDocumentTable.FindAndLockDocument()
        |
        v
  DocData: PlcDocDataObject
        |
        v  (cast to IPLCData)
  IPLCData.Node  [on runtime type, NOT the interface]
        |
        v
  PlcFileNode
        |
        v  (.SysManTreeItem)
  TcPouItemAdapter
        |
        +---> ITcPlcDeclaration.DeclarationText {get; set;}
        |         (VAR section, declarations)
        |
        +---> ITcPlcImplementation.ImplementationText {get; set;}
                  (ST code body)
```

### Step 1: Get DocData from Running Document Table / DocData aus der RDT holen

```csharp
IVsRunningDocumentTable rdt =
    GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;

uint docCookie;
IVsHierarchy hierarchy;
uint itemid;
IntPtr docDataPtr;

int hr = rdt.FindAndLockDocument(
    (uint)_VSRDTFLAGS.RDT_NoLock,
    documentMoniker,  // e.g., "C:\TwinCAT\...\MyPou.tcw"
    out hierarchy,
    out itemid,
    out docDataPtr,
    out docCookie);

if (hr != VSConstants.S_OK)
    return;

object docData = Marshal.GetObjectForIUnknown(docDataPtr);
Marshal.Release(docDataPtr);
```

The resulting `docData` is `Beckhoff.TwinCAT.VS.TextImage.PlcDocDataObject`.

### Step 2: Cast to IPLCData and Get Node / Zu IPLCData casten und Node abrufen

**Critical**: The `Node` property exists on the **runtime type** (`PlcDocDataObject`), not on the `IPLCData` interface definition. The `IPLCData` interface only exposes `ProjectHandle`, `ObjectGuid`, and `IsReadOnly`. You must cast to `IPLCData` and then access `Node` via the runtime type.

```csharp
IPLCData plcData = docData as IPLCData;
if (plcData == null)
    return;

// Node is on the runtime type, not the IPLCData interface
object node = plcData.GetType().GetProperty("Node")?.GetValue(plcData);
if (node == null)
    return;

// node is Beckhoff.TwinCAT.VS.PlcFileNode
// Implements: IVsHierarchy, IPlcHierarchyNode, ITcHierarchyNode, etc.
```

### Step 3: Get TcPouItemAdapter via SysManTreeItem / TcPouItemAdapter ueber SysManTreeItem abrufen

```csharp
// SysManTreeItem is on PlcFileNode's runtime type
object adapter = node.GetType().GetProperty("SysManTreeItem")?.GetValue(node);
if (adapter == null)
    return;

// adapter is TwinCAT.XAE.Automation.TcPouItemAdapter
```

### Step 4: Read, Format, and Write Text / Text lesen, formatieren und schreiben

`TcPouItemAdapter` implements both `TCatSysManagerLib.ITcPlcDeclaration` and `TCatSysManagerLib.ITcPlcImplementation`. Both must be formatted for a complete live update.

```csharp
// Cast to both interfaces
object tcDeclaration  = adapter as ITcPlcDeclaration;
object tcImplementation = adapter as ITcPlcImplementation;

if (tcDeclaration == null || tcImplementation == null)
    return;

// --- Format DeclarationText (VAR section) ---
string declarationText = ((ITcPlcDeclaration)tcDeclaration).DeclarationText;
string formattedDeclaration = FormatStCode(declarationText);
((ITcPlcDeclaration)tcDeclaration).DeclarationText = formattedDeclaration;

// --- Format ImplementationText (ST code body) ---
string implementationText = ((ITcPlcImplementation)tcImplementation).ImplementationText;
string formattedImplementation = FormatStCode(implementationText);
((ITcPlcImplementation)tcImplementation).ImplementationText = formattedImplementation;
```

#### Why Both Must Be Formatted / Warum beide formatiert werden muessen

`TcPouItemAdapter` implements **both** interfaces for a single POU. If you only set `DeclarationText`, only the declaration (VAR) section updates visually. If you only set `ImplementationText`, only the code body updates. You **must** format and write both for the editor to render the complete formatted result.

### Step 5: Persist to Disk / Auf Festplatte speichern

The Automation API updates the in-memory CODESYS model and the visual editor, but the `.TcPOU` / `.TcDUT` / `.TcGVL` files on disk must also be updated for persistence across solution reloads.

```csharp
/// <summary>
/// Updates the CDATA content in a TwinCAT POU file on disk.
/// </summary>
void UpdateFileOnDisk(string filePath, string formattedDeclaration, string formattedImplementation)
{
    string content = File.ReadAllText(filePath);

    // Replace declaration CDATA
    content = ReplaceCdataSection(content, "Declaration", formattedDeclaration);

    // Replace implementation CDATA
    content = ReplaceCdataSection(content, "Implementation", formattedImplementation);

    File.WriteAllText(filePath, content);
}

/// <summary>
/// Replaces a CDATA section within the TwinCAT XML file.
/// </summary>
static string ReplaceCdataSection(string content, string sectionName, string newText)
{
    // Pattern: <SectionName><![CDATA[...]]></SectionName>
    string pattern = $@"(<{sectionName}>)\s*<!\[CDATA\[.*?\]\]>\s*(</{sectionName}>)";
    string replacement = $"$1<![CDATA[{newText}]]>$2";
    return Regex.Replace(content, pattern, replacement, RegexOptions.Singleline);
}
```

### Complete Integration Method / Vollstaendige Integrationsmethode

```csharp
public int FormatActiveDocument()
{
    // 1. Get the active document's moniker
    var monitorSelection = GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
    monitorSelection.GetCurrentSelection(
        out _, out _, out var multiItemSelect, out var selectionContainer);

    // Get document moniker from RDT
    IVsRunningDocumentTable rdt = GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;

    // Get DocData for active document
    uint cookie;
    IVsHierarchy hier;
    uintitemid;
    IntPtr docDataPtr;
    int hr = rdt.FindAndLockDocument(
        (uint)_VSRDTFLAGS.RDT_NoLock,
        activeDocumentPath,
        out hier, out itemid, out docDataPtr, out cookie);

    if (hr != VSConstants.S_OK) return hr;

    object docData = Marshal.GetObjectForIUnknown(docDataPtr);
    Marshal.Release(docDataPtr);

    // 2. Navigate: DocData -> IPLCData -> Node -> SysManTreeItem -> TcPouItemAdapter
    IPLCData plcData = docData as IPLCData;
    if (plcData == null) return VSConstants.E_NOINTERFACE;

    object node = plcData.GetType().GetProperty("Node")?.GetValue(plcData);
    if (node == null) return VSConstants.E_FAIL;

    object adapter = node.GetType().GetProperty("SysManTreeItem")?.GetValue(node);
    if (adapter == null) return VSConstants.E_FAIL;

    // 3. Format both Declaration and Implementation
    var decl = adapter as ITcPlcDeclaration;
    var impl = adapter as ITcPlcImplementation;

    if (decl != null)
    {
        string rawDecl = decl.DeclarationText;
        decl.DeclarationText = FormatStCode(rawDecl);
    }

    if (impl != null)
    {
        string rawImpl = impl.ImplementationText;
        impl.ImplementationText = FormatStCode(rawImpl);
    }

    // 4. Persist to disk via CDATA replacement
    UpdateFileOnDisk(activeDocumentPath, decl?.DeclarationText, impl?.ImplementationText);

    return VSConstants.S_OK;
}
```

---

## Reflection Details / Reflexions-Details

Since `IPLCData.Node` and `PlcFileNode.SysManTreeItem` are not on the declared interfaces, reflection is required. Key types and their assemblies:

| Type | Assembly | Property | Return Type |
|------|----------|----------|-------------|
| `PlcDocDataObject` | Beckhoff.TwinCAT.VS.TextImage | (various) | — |
| `IPLCData` | Beckhoff.TwinCAT.VS.TextImage | `ProjectHandle`, `ObjectGuid`, `IsReadOnly` | — |
| `PlcDocDataObject` (runtime) | Beckhoff.TwinCAT.VS.TextImage | `Node` | `PlcFileNode` |
| `PlcFileNode` | Beckhoff.TwinCAT.VS | `SysManTreeItem` | `TcPouItemAdapter` |
| `TcPouItemAdapter` | TwinCAT.XAE.Automation | `DeclarationText` | `string` |
| `TcPouItemAdapter` | TwinCAT.XAE.Automation | `ImplementationText` | `string` |

Reflection access pattern:

```csharp
static object GetPropertyValue(object target, string propertyName)
{
    return target?.GetType().GetProperty(propertyName)?.GetValue(target);
}

// Usage chain:
object node    = GetPropertyValue(plcData, "Node");
object adapter = GetPropertyValue(node, "SysManTreeItem");

string declText = ((ITcPlcDeclaration)adapter).DeclarationText;
string implText = ((ITcPlcImplementation)adapter).ImplementationText;
```

---

---

## External Host Approach / Externer Host-Ansatz (PRODUCTION)

Since TcXaeShell's isolated shell blocks VSPackage/MEF/AddIn loading, the
production format tool runs as an **external process** that connects via COM DTE.

### Architecture

```
+------------------+     COM DTE (ROT)     +---------------------------+
|  TcXaeShell.exe  | <------------------> |  STFormatter.Host.exe     |
|  (VS 2017 Shell) |                      |  (net48, x86, hidden)    |
|                  |                      |                           |
|  PlcCodeWinCtx   |  inject buttons via  |  - Connects via ROT      |
|  Menu (127 ctrl) |  DTE.CommandBars     |  - Injects buttons        |
|                  |                      |  - Handles click events   |
|  .TcPOU files    |  read/write via      |  - Live-format via DTE   |
|  (XML on disk)   |  System.IO (backup)  |  - Auto-reconnects       |
+------------------+                      +---------------------------+
```

### Connection via ROT / Verbindung ueber ROT

TcXaeShell registers DTE in the Running Object Table with version-specific monikers. The DTE version is **auto-detected** at runtime via `TcXaeShellVersionProfile.DetectFromRotMoniker()` — do not hard-code a specific version:

- TC3 Build 4024+: `!TcXaeShell.DTE.15.0:{PID}`
- TC3 Build ~4020: `!TcXaeShell.DTE.14.0:{PID}`
- TC3 Build <4020: `!TcXaeShell.DTE.12.0:{PID}`

Search for both `!TcXaeShell.DTE.` and `!VisualStudio.DTE.` prefixes in the ROT enumeration — the version number varies by TcXaeShell build. Only `TcXaeShell.DTE` monikers match TcXaeShell; `VisualStudio.DTE` monikers match regular Visual Studio installations.

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
        // profile contains version-specific values (DTE version, registry root, etc.)
    }
}
```

### Context Menu Injection / Kontextmenue-Injection

Buttons are injected via `DTE.CommandBars` into both:
- `PlcCodeWinContextMenu` -- Beckhoff's PLC editor right-click menu (~127 controls)
- `Code Window` -- standard VS text editor context menu (~61 controls)

```csharp
var commandBars = (CommandBars)dte.CommandBars;

// Beckhoff PLC editor context menu
var plcMenu = commandBars["PlcCodeWinContextMenu"];
var btn1 = (CommandBarButton)plcMenu.Controls.Add(
    MsoControlType.msoControlButton, Missing.Value, Missing.Value,
    Missing.Value, true);
btn1.Caption = "Format ST Document";
btn1.Tag = "STFormatter_FormatDocument";
btn1.Click += OnFormatDocumentClick;

// Standard VS editor context menu (fallback)
var codeMenu = commandBars["Code Window"];
var btn2 = (CommandBarButton)codeMenu.Controls.Add(
    MsoControlType.msoControlButton, Missing.Value, Missing.Value,
    Missing.Value, true);
btn2.Caption = "Format ST Document";
btn2.Tag = "STFormatter_FormatDocument_Std";
btn2.Click += OnFormatDocumentClick;
```

**Cleanup**: Buttons must be removed when the host exits or the TcXaeShell
instance dies. Use per-PID tracking in `HostManager`:

```csharp
// Remove stale buttons from previous connection
CommandBarControl[] stale = FindButtonsWithTag(menu, "STFormatter_FormatDocument");
foreach (var btn in stale)
    btn.Delete(false);
```

### The Live-Edit Challenge / Die Live-Edit-Herausforderung

The PLC editor has **two separate tabs** within a single document:
- **Declaration**: The VAR section (variables, type declarations)
- **Implementation**: The ST code body (logic, assignments, control flow)

`DTE.ActiveDocument` sees one document regardless of which tab is active.
There is **no DTE API** to determine which tab is currently shown. The
`TextSelection.Text` property returns empty for the CODESYS-based PLC editor.

The window caption only shows the POU name (e.g., `"MAIN"`), not the active tab.

**Tab-switching DTE commands do NOT work** in TcXaeShell:
- `Project.OpenImplementation` -- "not a valid command"
- `View.Declaration` -- "not a valid command"
- `OtherContextMenus.PlCCodeWinContextMenu.OpenImplementation` -- "not a valid command"

### Section-Aware Live Edit / Abschnittsbewusstes Live-Edit

The working approach formats only the **currently visible tab** by reading
it via clipboard, detecting its type, and pasting back the formatted result:

```
1. Save clipboard
2. Edit.SelectAll → Edit.Copy → read clipboard
3. Detect: Declaration or Implementation?
4. Format with the correct method
5. Set clipboard with formatted text
6. Edit.Delete → Edit.Paste (selection is still SelectAll)
7. Restore clipboard
```

#### Step 1: Read the Active Section Text

`TextSelection.Text` returns empty in the PLC editor. Use SelectAll+Copy
to read the text onto the clipboard instead:

```csharp
// Save original clipboard content
string? savedClipboard = null;
try { savedClipboard = GetClipboardText(); } catch { }

// SelectAll then Copy to get text onto clipboard
dte.ExecuteCommand("Edit.SelectAll", "");
Thread.Sleep(50);
dte.ExecuteCommand("Edit.Copy", "");
Thread.Sleep(100);

// Read the clipboard
string currentText = GetClipboardText() ?? "";
```

**Critical**: Use Win32 clipboard API for both read and write.
`System.Windows.Forms.Clipboard` fails from MTA COM callback threads.

```csharp
[DllImport("user32.dll")]
static extern bool OpenClipboard(IntPtr hWndNewOwner);
[DllImport("user32.dll")]
static extern bool CloseClipboard();
[DllImport("user32.dll")]
static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
[DllImport("user32.dll")]
static extern IntPtr GetClipboardData(uint uFormat);
[DllImport("kernel32.dll")]
static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);
[DllImport("kernel32.dll")]
static extern IntPtr GlobalLock(IntPtr hMem);
[DllImport("kernel32.dll")]
static extern bool GlobalUnlock(IntPtr hMem);
[DllImport("kernel32.dll")]
static extern uint GlobalSize(IntPtr hMem);

const uint CF_UNICODETEXT = 13;
const uint GMEM_MOVEABLE = 0x0002;

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

#### Step 2: Detect Declaration vs Implementation

Heuristic detection based on the ST code content:

```csharp
static bool LooksLikeDeclaration(string text)
{
    if (string.IsNullOrEmpty(text)) return true; // default
    string upper = text.ToUpperInvariant();
    bool hasVar = upper.Contains("VAR") && upper.Contains("END_VAR");
    bool hasBodyKeywords = upper.Contains("IF ") || upper.Contains("FOR ") ||
                            upper.Contains("WHILE ") || upper.Contains(":=") ||
                            upper.Contains("THEN");

    // VAR/END_VAR without body keywords → declaration
    if (hasVar && !hasBodyKeywords) return true;
    // Body keywords without VAR → implementation
    if (hasBodyKeywords && !hasVar) return false;
    // Both present → likely a full POU (starts with PROGRAM/FUNCTION)
    bool hasPouHeader = upper.Contains("PROGRAM") || upper.Contains("FUNCTION_BLOCK") ||
                         upper.Contains("FUNCTION");
    if (hasPouHeader) return true;
    // Default: treat as implementation
    return false;
}
```

**Note**: The PLC editor only shows one section per tab, so the text
typically contains either pure VAR blocks (declaration) or pure
logic (implementation). Ambiguous cases are rare.

#### Step 3: Format and Paste

Use `Format()` for declaration text and `FormatBody()` for implementation text:

```csharp
var engine = new FormattingEngine(FormattingConfiguration.Default);
bool isDecl = LooksLikeDeclaration(currentText);

string formatted;
if (isDecl)
    formatted = engine.Format(currentText);      // parses as full ST POU
else
    formatted = engine.FormatBody(currentText);   // parses as body only

// Skip if no changes
if (formatted == currentText) return true; // already formatted

// Paste: selection is still SelectAll from the read step
SetClipboardText(formatted);
dte.ExecuteCommand("Edit.Delete", "");
dte.ExecuteCommand("Edit.Paste", "");

// Restore original clipboard
if (savedClipboard != null)
    SetClipboardText(savedClipboard);
```

### Undo Support / Rueckgaengig-Unterstützung

Wrap the entire edit in a single undo context so the user can undo
the format operation with one Ctrl+Z:

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

### Formatting Tier Cascade / Formatierungs-Tier-Kaskade

The host tries multiple approaches in order:

| Tier | Method | Status |
|------|--------|--------|
| 1 | Automation API (IPLCData.Node.SysManTreeItem) | FAILS — `DISP_E_UNKNOWNNAME` from external process |
| 2 | DTE ExecuteCommand + Clipboard (live edit) | **WORKS** — reads active section, formats, pastes |
| 3 | SendKeys fallback | Works but less reliable |
| 4 | IVsFileChangeEx + RDT file write | FAILS — `E_NOINTERFACE` from external process, reload dialog |
| 5 | Plain file write (CDATA replacement) | Works but triggers "file changed on disk" reload dialog |

**Tier 2 is the production approach.** Never use Tier 5 after a live edit —
the editor and the file are already in sync.

### Why File-System Approaches Fail for Live Edit / Warum Dateisystem-Ansaetze fuer Live-Edit versagen

| Approach | Result | Why |
|----------|--------|-----|
| Write .TcPOU file then reload | "File changed on disk" dialog | Editor detects external modification |
| IVsFileChangeEx.IgnoreFile + SyncFile | Returns S_OK, no effect | Not processed from external process |
| IVsRunningDocumentTable | E_NOINTERFACE | Shell service not reachable externally |
| IVsPersistDocData.ReloadDocData | Closes the editor tab | Destructive side effect |
| IVsTextLines.ReplaceLines | Reverts after ~8s | CODESYS engine overwrites the buffer |

### Process Lifetime / Prozess-Lebensdauer

- Host runs as a **hidden console process** (`<OutputType>Exe</OutputType>` + P/Invoke `ShowWindow(GetConsoleWindow(), 0)`)
- NOT `WinExe` — that output type causes immediate silent exit
- Must survive TcXaeShell restarts (auto-reconnect loop polling the ROT)
- `STAThread` + `Application.DoEvents()` message pump for COM event dispatch
- Per-PID tracking via `HostManager` prevents duplicate buttons
- Clean up injected `CommandBarControl` via `.Delete(false)` on shutdown
- Set `_dte = null` when TcXaeShell exits; poll ROT for new instance

### Deployment / Bereitstellung

Deploy these files (use `deploy.bat`, requires admin privileges):

```
C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\
  STFormatter.Host.exe
  STFormatter.Core.dll
  STFormatter.UI.dll
  Microsoft.VisualStudio.Interop.dll    ← NuGet: v17.0.32112.339
```

> **Target framework**: The Host is built for `net48` by default (for machines with .NET 4.8+). For older machines running .NET 4.6.2, build with the `net462` target instead. The `TcXaeShellVersionProfile.RequiredFramework` property indicates the minimum .NET Framework version for each TcXaeShell version.

Launch (runs as hidden background process with system tray icon):
```powershell
Start-Process "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
```

Log file: `%TEMP%\STFormatter_Host.log`

---

## VS Package Configuration / VS-Paketkonfiguration

### Package and Command GUIDs

| Item | GUID |
|------|------|
| Package | `{b1c2d3e4-f5a6-7890-abcd-ef1234567890}` |
| Command set | `{c2d3e4f5-a6b7-8901-cdef-234567890abc}` |
| Beckhoff PLC editor package | `{c1622824-2c1e-45ec-bb11-1448d0b0a2e8}` |
| Editor factory | `{cff47bb1-5559-4bde-a2af-06b30bc64f6c}` |
| Beckhoff context menu | `{3b11520b-7e70-4008-a6cf-b60ae84e12b1}` |

### Menu Resource Version

**The `.vsct` menu resource version must be `1`.** Setting it to `2` breaks command registration entirely. This is a TcXaeShell-specific constraint — the isolated shell does not support version 2 menu resources.

```xml
<!-- CORRECT -->
<CommandTable xmlns="http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable">
  <!-- ... -->
</CommandTable>

<!-- The menu resource version is implicit in TcXaeShell. -->
<!-- Do NOT set: <Extern href="stdidcmd.h" /> with version 2 overrides -->
```

### VSCT Command Definitions / VSCT-Befehlsdefinitionen

```xml
<?xml version="1.0" encoding="utf-8"?>
<CommandTable xmlns="http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable">

  <Commands package="guidSTFormatterPkg">
    <Groups>
      <Group guid="guidSTFormatterCmdSet" id="FormatGroup" priority="0x0100">
        <Parent guid="guidSHLMainMenu" id="IDM_EDIT"/>
      </Group>
    </Groups>

    <Buttons>
      <Button guid="guidSTFormatterCmdSet" id="cmdidFormatDocument"
              priority="0x0100" type="Button">
        <Parent guid="guidSTFormatterCmdSet" id="FormatGroup"/>
        <CommandFlag>TextOnly</CommandFlag>
        <Strings>
          <ButtonText>Format ST Document</ButtonText>
        </Strings>
      </Button>

      <Button guid="guidSTFormatterCmdSet" id="cmdidFormatSelection"
              priority="0x0101" type="Button">
        <Parent guid="guidSTFormatterCmdSet" id="FormatGroup"/>
        <CommandFlag>TextOnly</CommandFlag>
        <Strings>
          <ButtonText>Format ST Selection</ButtonText>
        </Strings>
      </Button>
    </Buttons>

    <KeyBindings>
      <KeyBinding guid="guidSTFormatterCmdSet" id="cmdidFormatDocument"
                  editor="guidVSStd97" key1="VK_D" mod1="Control"
                  mod2="Control"/>
      <KeyBinding guid="guidSTFormatterCmdSet" id="cmdidFormatSelection"
                  editor="guidVSStd97" key1="VK_F" mod1="Control"
                  mod2="Control"/>
    </KeyBindings>
  </Commands>

  <Symbols>
    <GuidSymbol name="guidSTFormatterPkg"
                value="{b1c2d3e4-f5a6-7890-abcd-ef1234567890}" />
    <GuidSymbol name="guidSTFormatterCmdSet"
                value="{c2d3e4f5-a6b7-8901-cdef-234567890abc}">
      <IDSymbol name="FormatGroup" value="0x0100"/>
      <IDSymbol name="cmdidFormatDocument" value="0x0100"/>
      <IDSymbol name="cmdidFormatSelection" value="0x0101"/>
    </GuidSymbol>
  </Symbols>

</CommandTable>
```

> **Keybindings**: Ctrl+K,D for **Format ST Document** (0x0100) and Ctrl+K,F for **Format ST Selection** (0x0101), matching Visual Studio's built-in format document/selection shortcuts.

---

## Beckhoff Assembly References / Beckhoff-Assemblyreferenzen

All Beckhoff PLC DLLs must be referenced with `Private=false` and loaded via the TwinCAT binding path. They are **not** redistributed with the extension.

### Required References

| Assembly | Path |
|----------|------|
| `IECTextEditor.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `TextDocument.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `Core.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `STObject.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `ImplementationObject.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `POUObject.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |
| `TwinCATPlcControl.dll` | `C:\TwinCAT\3.1\Components\Plc\Common\` |

Also required (from Automation Interface):

| Assembly | Path |
|----------|------|
| `TCatSysManagerLib.dll` | GAC / Interop |

### MSBuild Configuration

```xml
<ItemGroup>
  <Reference Include="IECTextEditor">
    <Private>false</Private>
    <HintPath>C:\TwinCAT\3.1\Components\Plc\Common\IECTextEditor.dll</HintPath>
  </Reference>
  <Reference Include="TextDocument">
    <Private>false</Private>
    <HintPath>C:\TwinCAT\3.1\Components\Plc\Common\TextDocument.dll</HintPath>
  </Reference>
  <!-- ... other Beckhoff references ... -->
</ItemGroup>

<!-- Binding redirect for TwinCAT assemblies -->
<ItemGroup>
  <PackageReference Include="Microsoft.VisualStudio.SDK" Version="15.9.3" />
</ItemGroup>
```

### TwinCAT Binding Path

The binding path GUID for the TcXaeShell probing path:

```
{A36B7FC5-341E-444E-820C-1191A14324D6}
```

This is configured in the `.pkgdef` file to ensure TcXaeShell can locate the Beckhoff assemblies at runtime.

---

## Building & Deployment / Erstellen und Bereitstellung

### Build / Erstellen

```powershell
dotnet build src\STFormatter.Host -c Debug

# For net462 target (older TcXaeShell):
dotnet build src\STFormatter.Host\STFormatter.Host.csproj -c Debug -p:TargetFramework=net462
```

### Deploy / Bereitstellen

Use the provided deployment script (requires administrator privileges):

```powershell
# For net48 (default — current TcXaeShell)
.\deploy.bat

# For net462 (older TcXaeShell)
.\deploy.bat net462
```

This deploys the following files to `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\`:

| File | Purpose |
|---|---|
| `STFormatter.Host.exe` | Main host process — connects via COM DTE |
| `STFormatter.Core.dll` | Formatting engine |
| `STFormatter.UI.dll` | System tray icon and settings UI |
| `Microsoft.VisualStudio.Interop.dll` | VS interop assembly |

### Start / Starten

```powershell
Start-Process "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
```

The Host auto-detects running TcXaeShell instances and auto-reconnects. No cache clearing or registry import is needed.

### Uninstall / Deinstallieren

```powershell
# Stop the Host process
Stop-Process -Name "STFormatter.Host" -ErrorAction SilentlyContinue

# Remove the extension folder
Remove-Item -Recurse -Force "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"
```

---

## VS SDK Compatibility / VS-SDK-Kompatibilitaet

TcXaeShell is built on VS 2017 Isolated Shell, which imposes specific SDK version constraints.

### SDK Versions / SDK-Versionen

| Component | Version | Notes |
|-----------|---------|-------|
| Visual Studio SDK | 15.9.3 | Matches shell version |
| VSSDK BuildTools | 17.12.2069 | Required for SDK-style project (.csproj) |

### ProductArchitecture Constraint / ProductArchitecture-Einschraenkung

When using SDK-style projects, `ProductArchitecture` must be a **child element** of `InstallationTarget`, not an attribute. Using it as an attribute causes build failures:

```xml
<!-- CORRECT: ProductArchitecture as child element -->
<InstallationTarget Version="[15.0,16.0)" Id="TcXaeShell">
  <ProductArchitecture>x86</ProductArchitecture>
</InstallationTarget>

<!-- INCORRECT: ProductArchitecture as attribute (fails) -->
<InstallationTarget Version="[15.0,16.0)" Id="TcXaeShell"
                    ProductArchitecture="x86" />
```

> **Note**: The `Version="[15.0,16.0)"` range targets VS 2017 (DTE 15.0). For multi-version TcXaeShell support, the external Host approach is used instead of VSIX — see the Cross-Version Compatibility section and `TcXaeShellVersionProfile`.
```

### Target Framework / Zielframework

```xml
<TargetFramework>net46</TargetFramework>
```

The extension targets .NET Framework 4.6 to match TcXaeShell's runtime.

---

## Cross-Version Compatibility / Versionsuebergreifende Kompatibilitaet

TcXaeShell ships in multiple versions depending on the TwinCAT 3 build. The external Host must detect and adapt to the running version at runtime. All version-specific values are encapsulated in `TcXaeShellVersionProfile` (in `STFormatter.Core/Configuration/`).

### TcXaeShellVersionProfile

| Property | Purpose | Varies by Version? |
|---|---|---|
| `DteVersion` | DTE version string (e.g. `"15.0"`, `"14.0"`, `"12.0"`) | Yes |
| `VsShellGeneration` | VS shell generation (e.g. `"2017"`, `"2015"`, `"2013"`) | Yes |
| `PrimaryRotMonikerPrefix` | `!TcXaeShell.DTE.{version}:` | Yes |
| `FallbackRotMonikerPrefix` | `!VisualStudio.DTE.{version}:` | Yes |
| `RegistryRoot` | Registry path (e.g. `Software\Beckhoff\TcXaeShell\15.0`) | Yes |
| `RequiredFramework` | Minimum .NET Framework (e.g. `"4.6"`, `"4.5.1"`) | Yes |
| `TargetContextMenuNames` | Context menu names | No (consistent) |
| `TwinCatFileExtensions` | File extensions (.TcPOU, .TcDUT, etc.) | No (consistent) |
| `ProcessName` | Always `"TcXaeShell"` | No |
| `InstallPathPattern` | Always `Beckhoff\TcXaeShell\Common7\IDE\` | No |

### Predefined Profiles

```csharp
TcXaeShellVersionProfile.VS2017  // TC3 Build 4024+, DTE 15.0, .NET 4.6+
TcXaeShellVersionProfile.VS2015  // TC3 Build ~4020, DTE 14.0, .NET 4.6+
TcXaeShellVersionProfile.VS2013  // TC3 Build <4020, DTE 12.0, .NET 4.5.1+
```

### Auto-Detection

`TcXaeShellVersionProfile.DetectFromRotMoniker(string displayName)` automatically identifies the TcXaeShell version from the ROT moniker:

1. Checks all known profiles (VS2017, VS2015, VS2013) for prefix matches
2. Falls back to dynamic version parsing for unrecognized `!TcXaeShell.DTE.{version}:{PID}` or `!VisualStudio.DTE.{version}:{PID}` monikers
3. Returns `null` for non-TcXaeShell monikers

The Host scans the ROT for all known TcXaeShell moniker patterns at startup and on each reconnection cycle. This ensures forward compatibility with future TcXaeShell versions.

### Stable Cross-Version Constants

These are consistent across all TcXaeShell versions and safe to use without version branching:

- DTE commands: `Edit.SelectAll`, `Edit.Copy`, `Edit.Delete`, `Edit.Paste`, `Edit.SelectionCancel`
- Context menu names: `PlcCodeWinContextMenu`, `Code Window`
- File extensions: `.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO`, `.TcTO`
- Process name: `TcXaeShell`
- Bitness: Always x86 (32-bit)
- `Microsoft.VisualStudio.Interop` v17.0.32112.339 — backward-compatible for DTE COM access

---

## SPlcControl Service / SPlcControl-Dienst

| Item | Value |
|------|-------|
| Service GUID | `{AEA6C474-B058-46CB-8F1E-768B55B59B53}` |
| Interface | `IPlcControl` |
| Primary method | `Initialize()` |

The `SPlcControl` service provides access to `IPlcControl`, but this interface only exposes `Initialize()`. It is not useful for text manipulation. Documented here for completeness — do not spend time exploring this path.

---

## File Persistence Details / Dateipersistenz-Details

TwinCAT stores POU content in XML files with CDATA sections:

| File Extension | Content | CDATA Sections |
|----------------|---------|----------------|
| `.TcPOU` | Programs, functions, function blocks | `Declaration`, `Implementation` |
| `.TcDUT` | Data type definitions (DUT) | `Declaration` |
| `.TcGVL` | Global variable lists (GVL) | `Declaration` |

### .TcPOU File Structure

```xml
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject>
  <POU Name="MyFunctionBlock">
    <Declaration><![CDATA[
VAR
    xInput : BOOL;
    nCounter : INT;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[
xOutput := xInput AND nCounter > 0;
nCounter := nCounter + 1;
]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>
```

### CDATA Replacement Strategy / CDATA-Ersetzungsstrategie

When persisting formatted code to disk, use regex-based CDATA replacement to avoid corrupting the XML structure:

```csharp
static string ReplaceCdataSection(string content, string sectionName, string newText)
{
    string pattern = $@"(<{sectionName}>)\s*<!\[CDATA\[.*?\]\]>\s*(</{sectionName}>)";
    string replacement = $"$1<![CDATA[{newText}]]>$2";
    return Regex.Replace(content, pattern, replacement, RegexOptions.Singleline);
}

// For .TcPOU files: replace both Declaration and Implementation
content = ReplaceCdataSection(content, "Declaration", formattedDeclaration);

// Implementation uses a nested structure: <Implementation><ST><![CDATA[...]]></ST></Implementation>
string implPattern = @"(<ST>)\s*<!\[CDATA\[.*?\]\]>\s*(</ST>)";
content = Regex.Replace(content, implPattern,
    $"$1<![CDATA[{formattedImplementation}]]>$2", RegexOptions.Singleline);

// For .TcDUT and .TcGVL files: replace only Declaration
content = ReplaceCdataSection(content, "Declaration", formattedDeclaration);
```

---

## Logging / Protokollierung

Debug log file:

```
%TEMP%\STFormatter_Host.log
```

### Log Configuration

```csharp
static readonly string LogPath =
    Path.Combine(Path.GetTempPath(), "STFormatter_Host.log");

    static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best-effort; do not crash the extension
        }
    }
```

### Diagnostic Steps / Diagnoseschritte

1. **Host not connecting**: Check `%TEMP%\STFormatter_Host.log` for ROT scan results. Verify TcXaeShell is running.
2. **Buttons not appearing**: Check log for "InjectButtons" entries. Verify the Host is running (system tray icon or Task Manager).
3. **Format does nothing**: Check log for "Read 0 chars" — clipboard read may have failed. Verify Win32 clipboard API works.
4. **Wrong section formatted**: Check log for "Detected as Declaration/Implementation" — the heuristic may need tuning.
5. **Reload dialog appears**: A file write happened after live edit — verify no fallback CDATA replacement is being triggered.
6. **Editor reverts changes**: Ensure you are using the ExecuteCommand live edit, not IVsTextLines or file writes.

---

## Quick Reference / Kurzreferenz

### Object Navigation Chain

**In-process only** (does NOT work from external Host):
```
RDT → FindAndLockDocument()
  → DocData (PlcDocDataObject)
    → as IPLCData
      → .Node (reflection) → PlcFileNode
        → .SysManTreeItem (reflection) → TcPouItemAdapter
          → as ITcPlcDeclaration  → .DeclarationText {get; set;}
          → as ITcPlcImplementation → .ImplementationText {get; set;}
```

**External Host** (production approach):
```
ROT → !TcXaeShell.DTE.{version}:{PID}  (version auto-detected via TcXaeShellVersionProfile)
  → DTE.CommandBars["PlcCodeWinContextMenu"] → Inject buttons
  → Button click:
    → Edit.SelectAll → Edit.Copy → Win32 clipboard read
    → LooksLikeDeclaration() → Format() or FormatBody()
    → SetClipboardText → Edit.Delete → Edit.Paste
    → UndoContext (single undo)
```

### Deployment Checklist / Bereitstellungscheckliste

1. Build: `dotnet build src\STFormatter.Host -c Debug`
2. Deploy: `.\deploy.bat` (or `.\deploy.bat net462` for older TcXaeShell)
3. Start: Run `STFormatter.Host.exe` — it auto-connects to TcXaeShell
4. Verify: Right-click in PLC editor → "Format ST Document" should appear

### Failed Approaches Summary / Zusammenfassung der gescheiterten Ansaetze

| Approach | Result | Why |
|----------|--------|-----|
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

---

## Architecture Diagram / Architekturdiagramm

```
+----------------------------------------------------------+
|                    TcXaeShell (VS Isolated Shell)         |
|                    Version varies by TC3 build:           |
|                    Build 4024+ → VS 2017 (DTE 15.0)      |
|                    Build ~4020  → VS 2015 (DTE 14.0)      |
|                    Build <4020  → VS 2013 (DTE 12.0)      |
|                                                          |
|  +-----------------------------------------------------+ |
|  |              PLC Editor (CODESYS-based)              | |
|  |                                                     | |
|  |  Visual Rendering <--- CODESYS Engine <--------+   | |
|  |        ^                                       |   | |
|  |        |                                       |   | |
|  |   IVsTextLines (IGNORED by visual layer)      |   | |
|  |        ^                                       |   | |
|  |        |                                       |   | |
|  |   IVsUserData (E_FAIL)                         |   | |
|  |                                                 |   | |
|  +-------------------------------------------------+---+ |
|                                                          |
|  +-----------------------------------------------------+ |
|  |     External Host (STFormatter.Host.exe)             | |
|  |                                                     | |
|  |  COM DTE via ROT                                    | |
|  |    Rot → !TcXaeShell.DTE.{version}:{PID}            | |
|  |    (version auto-detected via TcXaeShellVersionProfile)
|  |    → DTE.CommandBars["PlcCodeWinContextMenu"]       | |
|  |    → Inject Format buttons                          | |
|  |                                                     | |
|  |  Section-Aware Live Edit:                           | |
|  |    Edit.SelectAll → Edit.Copy → Win32 clipboard    | |
|  |    → LooksLikeDeclaration() heuristic              | |
|  |    → Format() or FormatBody()                      | |
|  |    → SetClipboardText → Edit.Delete → Edit.Paste  | |
|  |    → UndoContext wraps as single undo               | |
|  |                                                     | |
|  |                                 writes to CODESYS --+ | |
|  +-----------------------------------------------------+ |
|                                                          |
|  +-----------------------------------------------------+ |
|  |           File Persistence (CDATA replacement)       | |
|  |           (FALLBACK — triggers reload dialog)         | |
|  |                                                     | |
|  |  .TcPOU ──> CDATA ──> Declaration + Implementation  | |
|  |  .TcDUT ──> CDATA ──> Declaration                  | |
|  |  .TcGVL ──> CDATA ──> Declaration                  | |
|  +-----------------------------------------------------+ |
+----------------------------------------------------------+
```