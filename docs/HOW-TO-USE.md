# How to Use — TwinCAT ST Formatter

Practical usage guide for all three deployment targets.

---

## Quick Reference

| Task | CLI | VS 2022 | TcXaeShell |
|---|---|---|---|
| Format current file | `stfmt format file.st` | Ctrl+K, D | Right-click > Format ST Document |
| Format selection | `stfmt format file.st` (whole file only) | Ctrl+K, F | Right-click > Format ST Selection |
| Format on save | — | Automatic (when enabled) | — |
| Format entire project | `stfmt batch ./src --recursive` | — | — |
| Check formatting | `stfmt check file.st` | — | — |
| Change settings | `.editorconfig` | Tools > Options or `.editorconfig` | Tray icon > Settings or `.editorconfig` |
| View log | — | Output window | `%TEMP%\STFormatter_Host.log` |

---

## 1. CLI Usage

### Format a Single File

```bash
# Format in place (overwrites the original file)
stfmt format MyProgram.st

# Preview without changing the file
stfmt format MyProgram.st --dry-run

# Format to a different file
stfmt format MyProgram.st -o MyProgram_formatted.st
```

### Format TwinCAT XML Files

The CLI automatically detects TwinCAT XML files (`.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO`, `.TcTO`)
and formats the ST code inside CDATA sections:

```bash
# Format a single POU
stfmt format Main.TcPOU

# Format with backup (creates .bak file)
stfmt format Motor.TcDUT

# Preview formatting of a TwinCAT file
stfmt format Sensor.TcGVL --dry-run
```

### Batch Format a Project

```bash
# Format all .st files in a directory
stfmt batch ./POUs --recursive

# Format all ST and TwinCAT XML files
stfmt batch ./MyTwinCATProject --recursive --twincat

# Check which files need formatting (CI-friendly)
stfmt check ./src --recursive
echo $?  # 0 = all formatted, 1 = some need formatting
```

### CI Pipeline Integration

```bash
# In a CI pipeline (GitHub Actions, Azure DevOps, etc.)
stfmt check ./src --recursive
if [ $? -ne 0 ]; then
  echo "Some files are not formatted!"
  exit 1
fi
```

The `check` command exits with:
- **0** — all files match formatting rules
- **1** — one or more files differ from formatted output

### Configuration

```bash
# Generate .editorconfig from a preset
stfmt init . --preset stweep

# List available presets
stfmt preset

# Show preset details
stfmt preset compact

# Export configuration to JSON
stfmt export my-style.json --preset stweep

# Import JSON configuration as .editorconfig
stfmt import my-style.json
```

### Configuration Priority (CLI)

1. `.editorconfig` closest to the source file
2. `.editorconfig` walking up the directory tree
3. Built-in defaults (`FormattingConfiguration.Default`)

Example `.editorconfig` for a TwinCAT project:

```ini
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
st_align_variable_declarations = true
st_align_assignments = true
st_empty_lines_between_pous = 2
st_empty_lines_between_var_sections = 1
st_format_on_save = true

[*.{TcPOU,TcDUT,TcGVL}]
st_keyword_casing = upper
```

---

## 2. TcXaeShell Usage

### Starting the Host

The Host process must be running to provide formatting in TcXaeShell. You can:

- **Manual start**: Double-click `STFormatter.Host.exe` or run from PowerShell
- **Auto-start**: Add a shortcut to the Windows Startup folder (see [HOW-TO-INSTALL.md](HOW-TO-INSTALL.md))

```powershell
# Start the Host
Start-Process "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\STFormatter.Host.exe"
```

The Host auto-detects running TcXaeShell instances and auto-reconnects after TcXaeShell restarts.

### Formatting Code

1. Open a POU in TcXaeShell's PLC editor
2. Click in the **Declaration** section (VAR...END_VAR) or **Implementation** section (ST code)
3. **Right-click** to open the context menu
4. Click one of the formatting commands:

| Menu Item | What It Formats | Shortcut |
|---|---|---|
| **Format ST Document** | The active section (declaration or implementation) | — |
| **Format ST Selection** | Only the selected text | — |
| **Format ST File** | The entire .TcPOU/.TcDUT/.TcGVL file on disk | — |

The formatter automatically detects whether you're in the declaration or implementation
section based on the content (VAR/END_VAR keywords → declaration, IF/FOR/:= → implementation).

### How the Live Edit Works

When you click **Format ST Document**:

1. The Host reads the active section from TcXaeShell using clipboard-based DTE commands
   (`Edit.SelectAll` → `Edit.Copy` → Win32 clipboard read)
2. It detects the section type (declaration vs. implementation)
3. It formats the code using `FormattingEngine.Format()` or `.FormatBody()`
4. It writes the formatted code back using `Edit.Delete` → clipboard write → `Edit.Paste`
5. The entire operation is wrapped in a DTE `UndoContext`, so Ctrl+Z reverts it

> **Note**: During formatting, your clipboard content is saved and restored. Avoid copying
> or pasting while formatting is in progress (typically under 100ms).

### Format ST File (Disk-Based)

The **Format ST File** command reads the TwinCAT XML file from disk, formats the ST code
inside the CDATA sections, and writes it back. This creates a `.bak` backup file first.

> **Warning**: This approach may trigger TcXaeShell's "file changed on disk" reload dialog.
> Use **Format ST Document** (clipboard-based) for a seamless experience.

### System Tray Icon

The Host provides a system tray icon with these options:

| Menu Item | Description |
|---|---|
| **Settings** | Opens a settings dialog where you can change all formatting options |
| **Instances** | Shows connected TcXaeShell processes and their DTE version |
| **History** | Shows a list of recent format operations with before/after diffs |
| **Log** | Opens the live log file (`%TEMP%\STFormatter_Host.log`) |
| **Exit** | Stops the Host process |

### Configuration (TcXaeShell)

The Host reads configuration from two sources, in priority order:

1. **Settings dialog** (tray icon > Settings) — saved to `%LOCALAPPDATA%\STFormatter\settings.json`
2. **`.editorconfig`** files — discovered from the TwinCAT project directory upward
3. **Built-in defaults**

For project-specific formatting, create an `.editorconfig` in your TwinCAT project root:

```
MyTwinCATProject/
├── .editorconfig          ← ST Formatter reads this
├── MyPlc/
│   ├── MAIN.TcPOU
│   ├── Motor.TcDUT
│   └── ...
```

### Log File

The Host writes detailed logs to `%TEMP%\STFormatter_Host.log`. Check this file if formatting isn't working:

```powershell
# View the last 20 lines
Get-Content "$env:TEMP\STFormatter_Host.log" -Tail 20

# Search for errors
Select-String "ERROR|FAIL|Exception" "$env:TEMP\STFormatter_Host.log"
```

### Supported File Types

| Extension | Format Method | Notes |
|---|---|---|
| `.TcPOU` | Live-edit (clipboard) or file-based | Programs, function blocks, functions |
| `.TcDUT` | Live-edit (clipboard) or file-based | Data type definitions |
| `.TcGVL` | Live-edit (clipboard) or file-based | Global variable lists |
| `.TcIO` | File-based only | IO mappings |
| `.TcTO` | File-based only | Task objects |

---

## 3. Visual Studio 2022 Usage

### Keyboard Shortcuts

| Command | Shortcut | Description |
|---|---|---|
| Format Document | **Ctrl+K, Ctrl+D** | Format the entire active file |
| Format Selection | **Ctrl+K, Ctrl+F** | Format only the selected text |

You can also access these from the menu: **Edit** > **Advanced** > **Format Document** / **Format Selection**.

### Format on Save

When enabled, files are automatically formatted when you save (Ctrl+S). This works for:

- `.st`, `.txt`, `.iecst` — plain ST files (formatted via text buffer)
- `.TcPOU`, `.TcDUT`, `.TcGVL` — TwinCAT XML files (CDATA sections formatted)

To toggle Format on Save:
- **Tools** > **Options** > **TwinCAT** > **ST Formatter** > **Format On Save**
- Or set `st_format_on_save = false` in `.editorconfig`

### Options Page

Access via **Tools** > **Options** > **TwinCAT** > **ST Formatter**:

| Category | Option | Values | Default |
|---|---|---|---|
| Indentation | Indent Style | `spaces`, `tabs` | `spaces` |
| Indentation | Indent Size | 1–8 | 4 |
| Indentation | Continuation Indent Size | 1–16 | 8 |
| Formatting | Keyword Casing | `upper`, `lower`, `pascal`, `original` | `upper` |
| Formatting | Brace Style | `allman`, `compact` | `allman` |
| Formatting | Space Around Operators | on/off | on |
| Formatting | Space After Comma | on/off | on |
| Formatting | Space Before Semicolon | on/off | off |
| Formatting | Space After Colon | on/off | on |
| Formatting | Align Assignments | on/off | on |
| Formatting | Align Variable Declarations | on/off | on |
| Formatting | Max Line Length | 0–999 (0 = unlimited) | 120 |
| Formatting | Keep Single-Line Blocks | on/off | off |
| Formatting | Format On Save | on/off | on |
| Line Breaks | Empty Lines Between POUs | 0–10 | 2 |
| Line Breaks | Empty Lines Between Var Sections | 0–10 | 1 |
| Line Breaks | New Line Style | `crlf`, `lf`, `cr` | `crlf` |

Options page settings override `.editorconfig`.

### Configuration Priority (VS 2022)

1. **VS Options page** → highest priority
2. **`.editorconfig`** closest to the source file
3. **Built-in defaults**

---

## 4. Presets

Three built-in presets cover common coding styles:

### STweep (Recommended for TwinCAT)

Professional, readable style with generous vertical spacing and aligned declarations.
Best for teams and large projects.

```ini
# Generated by: stfmt init . --preset stweep
indent_style = space
indent_size = 4
st_keyword_casing = upper
st_brace_style = allman
st_space_around_operators = true
st_align_variable_declarations = true
st_align_assignments = true
st_empty_lines_between_pous = 2
st_empty_lines_between_var_sections = 1
```

### Compact

Minimises vertical and horizontal space. 2-space indent, lowercase keywords,
single-line blocks. Good for code reviews on small screens.

```ini
# Generated by: stfmt init . --preset compact
indent_style = space
indent_size = 2
st_keyword_casing = lower
st_brace_style = compact
st_space_around_operators = true
st_align_variable_declarations = false
st_align_assignments = false
st_empty_lines_between_pous = 1
st_empty_lines_between_var_sections = 0
st_keep_single_line_blocks = true
```

### Expanded

Maximum readability with 80-character line limit. Good for printouts and strict style guides.

```ini
# Generated by: stfmt init . --preset expanded
indent_style = space
indent_size = 4
st_keyword_casing = upper
st_brace_style = allman
st_space_around_operators = true
st_align_variable_declarations = true
st_align_assignments = true
max_line_length = 80
st_empty_lines_between_pous = 3
st_empty_lines_between_var_sections = 2
st_keep_single_line_blocks = false
```

---

## 5. Common Workflows

### Format a Single POU in TcXaeShell

1. Open the POU in TcXaeShell
2. Click in the code editor
3. Right-click → **Format ST Document**
4. The code is reformatted instantly
5. Press Ctrl+Z to undo if you don't like the result

### Batch-Format an Entire TwinCAT Project

```bash
# Format all ST and TwinCAT XML files in the project
stfmt batch "./MyTwinCATProject" --recursive --twincat

# Check which files need formatting (CI mode)
stfmt check "./MyTwinCATProject" --recursive --twincat
```

### Set Up Consistent Formatting for a Team

1. Create an `.editorconfig` at the project root:
   ```bash
   cd MyTwinCATProject
   stfmt init . --preset stweep
   ```

2. Commit the `.editorconfig` to version control.

3. All team members using the CLI, VS 2022 extension, or TcXaeShell Host will
   automatically pick up the same settings.

### Configure Different Styles for Different File Types

```ini
root = true

# All files: basic indent
[*]
indent_style = space
indent_size = 4
end_of_line = crlf

# ST files: full ST formatting
[*.st]
st_keyword_casing = upper
st_brace_style = allman
st_align_variable_declarations = true

# TwinCAT XML files: same settings
[*.{TcPOU,TcDUT,TcGVL}]
st_keyword_casing = upper
st_brace_style = allman
```

### Use Different Settings for Legacy vs New Code

```ini
root = true

# New code directory: strict formatting
[new-code/**]
st_keyword_casing = upper
st_empty_lines_between_pous = 2

# Legacy code directory: minimal changes
[legacy-code/**]
st_keyword_casing = original
st_empty_lines_between_pous = 1
st_keep_single_line_blocks = true
```

### Troubleshooting: File Not Formatted

1. **Check the file extension**: Only `.st`, `.txt`, `.iecst`, `.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO`, `.TcTO` are processed.

2. **Check the log file** (TcXaeShell): `%TEMP%\STFormatter_Host.log`

3. **Verify `.editorconfig` location**: Must be in or above the source file's directory.

4. **Try with defaults**:
   ```bash
   # Format with built-in defaults (ignoring .editorconfig)
   stfmt format MyProgram.st --dry-run
   ```

5. **Check for syntax errors**: Files with parse errors are skipped with a warning. Fix the syntax and retry.

---

## 6. Example: Before and After

### Before

```st
PROGRAM MAIN
VAR_INPUT
bStart:BOOL;nMode:INT:=1;fSpeed:REAL:=50.0;
END_VAR
VAR_OUTPUT
bRunning:BOOL;nStatus:INT;
END_VAR
VAR
nCounter:INT:=0;
fTemp:REAL;
END_VAR
if bStart and not bStop then
bRunning:=true;
nStatus:=nMode;
if nMode=1 then nCounter:=nCounter+1;end_if
CASE nMode OF
1:nStatus:=10;
2,3:nStatus:=20;
ELSE nStatus:=0;
END_CASE
end_if
END_PROGRAM
```

### After (default / STweep preset)

```st
PROGRAM MAIN
    VAR_INPUT
        bStart  : BOOL;
        nMode   : INT          := 1;
        fSpeed  : REAL         := 50.0;
    END_VAR

    VAR_OUTPUT
        bRunning : BOOL;
        nStatus  : INT;
    END_VAR

    VAR
        nCounter : INT  := 0;
        fTemp    : REAL;
    END_VAR

    IF bStart AND NOT bStop THEN
        bRunning := TRUE;
        nStatus  := nMode;

        IF nMode = 1 THEN
            nCounter := nCounter + 1;
        END_IF

        CASE nMode OF
            1:
                nStatus := 10;
            2, 3:
                nStatus := 20;
            ELSE
                nStatus := 0;
        END_CASE
    END_IF
END_PROGRAM
```

### After (compact preset)

```st
program MAIN
  var_input
    bStart : bool
    nMode : int := 1
    fSpeed : real := 50.0
  end_var
  var_output
    bRunning : bool
    nStatus : int
  end_var
  var
    nCounter : int := 0
    fTemp : real
  end_var
  if bStart and not bStop then
    bRunning := true
    nStatus := nMode
    if nMode = 1 then nCounter := nCounter + 1; end_if
    case nMode of
      1: nStatus := 10
      2, 3: nStatus := 20
      else nStatus := 0
    end_case
  end_if
end_program
```