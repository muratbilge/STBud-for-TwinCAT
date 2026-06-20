# How to Use — STBud for TwinCAT

Practical usage guide for the CLI and TcXaeShell Host deployment targets.

---

## Quick Reference

| Task | CLI | TcXaeShell / VS 2022 |
|---|---|---|
| Format current file | `stfmt format file.st` | Right-click > Format Document |
| Format selection | `stfmt format file.st` (whole file only) | Right-click > Format Selection |
| Format entire project | `stfmt batch ./src --recursive` | — |
| Add I/O linking attribute | — | Right-click > I/O Linking... |
| Insert TwinCAT pragmas/attributes | — | Right-click > Add Attribute / Add Task Attribute / Add Region |
| Check formatting | `stfmt check file.st` | — |
| Check a TwinCAT machine | `stfmt ping <host>` | Tray icon > Toolbox |
| Environment diagnostics | `stfmt doctor` | Tray icon > Toolbox |
| Change settings | `.editorconfig` | Tray icon > Settings or `.editorconfig` |
| View log | — | `%TEMP%\STBud_Host.log` |

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
stfmt init . --preset default

# List available presets
stfmt preset

# Show preset details
stfmt preset compact

# Export configuration to JSON
stfmt export my-style.json --preset default

# Import JSON configuration as .editorconfig
stfmt import my-style.json
```

### Connectivity & Diagnostics

```bash
# Check whether a TwinCAT machine is reachable (ICMP + ADS ports 48898/8016)
stfmt ping 192.168.0.10
stfmt ping plc-cell-3 --timeout 1000

# Report the local TwinCAT/TcXaeShell environment: install + build, running
# shells with their ROT monikers, deployed Host, and a local ADS check
stfmt doctor

# Save the report (e.g. to diff before/after a TwinCAT upgrade)
stfmt doctor --save before-upgrade.txt
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

The Host process must be running to provide STBud tools in TcXaeShell. You can:

- **Manual start**: Double-click `STFormatter.Host.exe` or run from PowerShell
- **Auto-start**: Add a shortcut to the Windows Startup folder (see [HOW-TO-INSTALL.md](HOW-TO-INSTALL.md))

```powershell
# Start the Host
Start-Process "C:\Program Files (x86)\STBud\STFormatter.Host.exe"
```

The Host auto-detects running engineering environments and auto-reconnects after a restart.
It works with the standalone **TcXaeShell** (all generations, incl. Build 4026's DTE 17.0
and the 64-bit `TcXaeShell64`) **and with TwinCAT loaded inside Visual Studio 2022** —
detected by the presence of the TwinCAT PLC editor menu, so a plain (non-TwinCAT) VS is
left untouched.

### Formatting Code

1. Open a POU in the PLC editor (TcXaeShell or VS 2022)
2. Click in the **Declaration** section (VAR...END_VAR) or **Implementation** section (ST code)
3. **Right-click** to open the context menu
4. Click one of the formatting commands (now at the top level of the **STBud for TwinCAT** menu):

| Menu Item | What It Formats | Shortcut |
|---|---|---|
| **Format Document** | The active section (declaration or implementation) | Ctrl+Shift+F |
| **Format Selection** | Only the selected text | Ctrl+Shift+D |

The formatter automatically detects whether you're in the declaration or implementation
section based on the content (VAR/END_VAR keywords → declaration, IF/FOR/:= → implementation).
Keyboard shortcuts work when the editor has focus. If a section contains ST syntax errors,
formatting is refused with a message rather than silently doing nothing.

### Editor Helpers

The **STBud for TwinCAT** context menu also surfaces **I/O Linking…** at the top level (it
opens the I/O tree browser with TIID/TIIB link styles), plus **Add Attribute**, **Add Task
Attribute**, **Add Region**, and **Warning…** submenus that insert common TwinCAT
attributes/pragmas, regions, task attributes, and warnings — all without an in-process
TcXaeShell extension.

### How the Live Edit Works

When you click **Format Document**:

1. The Host reads the active section from TcXaeShell using clipboard-based DTE commands
   (`Edit.SelectAll` → `Edit.Copy` → Win32 clipboard read)
2. It detects the section type (declaration vs. implementation)
3. It formats the code using `FormattingEngine.Format()` or `.FormatBody()`
4. It writes the formatted code back by setting the Win32 clipboard, then running `Edit.Delete` → `Edit.Paste`
5. The entire operation is wrapped in a DTE `UndoContext`, so Ctrl+Z reverts it

> **Note**: During formatting, your clipboard content is saved and restored. Avoid copying
> or pasting while formatting is in progress (typically under 100ms).

### Disk-Based Fallback

If the live clipboard edit cannot be applied, the Host automatically falls back to reading
the TwinCAT XML file from disk, formatting the ST inside the CDATA sections, and writing it
back (creating a `.bak` backup first). This is an internal fallback, not a separate menu
command, and only runs when the editor content actually changed.

> **Note**: The disk-write fallback may trigger the "file changed on disk" reload dialog.
> The normal clipboard-based path avoids it.

### System Tray Icon

The Host provides a system tray icon with these options:

| Menu Item | Description |
|---|---|
| **Settings** | Opens a settings dialog where you can change formatting and Host options |
| **Instances** | Shows connected engineering environments (TcXaeShell / VS 2022) and their DTE version |
| **History** | Shows a list of recent format operations with before/after diffs |
| **Log** | Opens the live log file (`%TEMP%\STBud_Host.log`) |
| **Toolbox** | TwinCAT machine pinger and a copyable environment-diagnostics report |
| **Exit** | Stops the Host process |

### Configuration (TcXaeShell)

The Host reads configuration from two sources, in priority order:

1. **Settings dialog** (tray icon > Settings) — saved to `%APPDATA%\STBud\settings.json`
2. **`.editorconfig`** files — discovered from the TwinCAT project directory upward
3. **Built-in defaults**

For project-specific formatting, create an `.editorconfig` in your TwinCAT project root:

```
MyTwinCATProject/
├── .editorconfig          ← STBud reads this
├── MyPlc/
│   ├── MAIN.TcPOU
│   ├── Motor.TcDUT
│   └── ...
```

### Log File

The Host writes detailed logs to `%TEMP%\STBud_Host.log`. Check this file if formatting isn't working:

```powershell
# View the last 20 lines
Get-Content "$env:TEMP\STBud_Host.log" -Tail 20

# Search for errors
Select-String "ERROR|FAIL|Exception" "$env:TEMP\STBud_Host.log"
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

## 3. Presets

Three built-in presets cover common coding styles:

### Default (Recommended for TwinCAT)

Professional, readable style with generous vertical spacing and aligned declarations.
Best for teams and large projects.

```ini
# Generated by: stfmt init . --preset default
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

## 4. Common Workflows

### Format a Single POU in TcXaeShell

1. Open the POU in TcXaeShell or VS 2022
2. Click in the code editor
3. Right-click → **Format Document** (or press Ctrl+Shift+F)
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
   stfmt init . --preset default
   ```

2. Commit the `.editorconfig` to version control.

3. All team members using the CLI or TcXaeShell Host will
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

2. **Check the log file** (TcXaeShell): `%TEMP%\STBud_Host.log`

3. **Verify `.editorconfig` location**: Must be in or above the source file's directory.

4. **Try with defaults**:
   ```bash
   # Format with built-in defaults (ignoring .editorconfig)
   stfmt format MyProgram.st --dry-run
   ```

5. **Check for syntax errors**: Files with parse errors are skipped with a warning. Fix the syntax and retry.

---

## 5. Example: Before and After

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

### After (default preset)

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
