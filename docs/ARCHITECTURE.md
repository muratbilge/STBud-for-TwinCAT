# Technical Architecture

STBud for TwinCAT is a Structured Text formatter and editor helper for Beckhoff TwinCAT projects. The supported product surface is:

- `STFormatter.Core`: shared formatting engine.
- `STFormatter.CLI`: command-line formatter and CI checker.
- `STFormatter.Host`: external TcXaeShell integration through COM DTE.
- `STFormatter.UI`: tray UI, settings, instances, history, and diff viewer for the Host.

VSIX, VSPackage, MEF, and AddIn integration paths were removed from the product because TcXaeShell's isolated shell does not reliably load them. See `AGENTS.md` for the historical failure notes.

---

## Solution Overview

```
TwinCAT.STFormatter.sln
|
+-- src/
|   +-- STFormatter.Core/        net8.0;net48;net462   Formatting engine
|   +-- STFormatter.CLI/         net8.0                 Command-line interface
|   +-- STFormatter.Host/        net462;net48/x86        TcXaeShell external Host
|   +-- STFormatter.UI/          net462;net48/x86        Tray UI and diagnostics
|
+-- tests/
|   +-- STFormatter.Core.Tests/  net8.0                  xUnit tests
|
+-- installer/                                           Inno Setup installer
+-- docs/                                                Documentation
```

The Core project is multi-targeted so every supported consumer references the same formatter code compiled for its runtime.

| Project | Target Framework | Role |
|---|---|---|
| `STFormatter.Core` | net8.0;net48;net462 | Lexer, parser, syntax tree, formatter, configuration |
| `STFormatter.CLI` | net8.0 | Batch formatting, checks, presets, `.editorconfig` generation |
| `STFormatter.Host` | net462;net48/x86 | External process that connects to TcXaeShell via COM DTE |
| `STFormatter.UI` | net462;net48/x86 | Tray UI, settings, instances, history, diff viewer |
| `STFormatter.Core.Tests` | net8.0 | Unit tests for parser and formatter behavior |

---

## Formatting Pipeline

```
SourceText
    |
    v
Lexer
    |
    v
Parser
    |
    v
SyntaxTree
    |
    v
FormattingVisitor
    |
    v
FormattingWriter
    |
    v
Formatted output
```

Each stage is a pure transformation. The pipeline never mutates input; every stage produces a new object. This keeps the formatter testable and allows CLI, Host, and future integrations to share the same behavior.

---

## Core Engine

`STFormatter.Core` is split into logical subsystems:

```
STFormatter.Core/
+-- Lexing/          Lexer.cs
+-- Parsing/         Parser.cs
+-- Syntax/          SyntaxNode, SyntaxToken, concrete node types
+-- Formatting/      FormattingEngine, FormattingVisitor, FormattingWriter
+-- Configuration/   FormattingConfiguration, EditorConfigParser, presets
+-- Text/            SourceText, TextSpan
```

The lexer captures tokens plus trivia such as whitespace and comments. The parser builds an immutable syntax tree with error recovery. The formatter walks the tree and emits normalized text according to `FormattingConfiguration`.

Supported input shapes:

- Complete declarations through `FormattingEngine.Format()`.
- Implementation bodies through `FormattingEngine.FormatBody()`.
- Declaration sections through `FormattingEngine.FormatDeclaration()`.
- TwinCAT XML files through `TwinCatXmlFormatter`.

---

## CLI

`STFormatter.CLI` is the supported automation and CI target.

| Command | Description |
|---|---|
| `format` | Format one file in place or to an output path |
| `check` | Check files for formatting differences with CI-friendly exit codes |
| `batch` | Format all matching files in a directory tree |
| `init` | Generate an `.editorconfig` from a preset |
| `preset` | List or inspect presets |
| `export` | Export a configuration preset to JSON |
| `import` | Import a JSON configuration file and write `.editorconfig` |
| `version` | Print version information |
| `help` | Print usage |

CLI configuration resolution:

1. Command-line flags.
2. Nearest `.editorconfig`, walking upward.
3. Built-in preset/defaults.

---

## TcXaeShell Host

`STFormatter.Host` is the only supported TcXaeShell integration path. It is an external x86 .NET Framework process that connects to running TcXaeShell instances through COM DTE ROT monikers.

```
+------------------+       COM DTE / ROT       +----------------------+
| TcXaeShell.exe   | <------------------------> | STFormatter.Host.exe |
| PLC editor       |                            | hidden process       |
| context menus    |                            | tray UI              |
+------------------+                            +----------------------+
```

Runtime responsibilities:

- Scan the Running Object Table for `!TcXaeShell.DTE.{version}:{PID}` and fallback Visual Studio DTE monikers.
- Inject context-menu buttons into `PlcCodeWinContextMenu` and `Code Window`.
- Read active editor text with DTE commands and Win32 clipboard APIs.
- Detect declaration versus implementation sections.
- Format the active section using the appropriate Core entry point.
- Paste the formatted text back through DTE commands inside an undo context.
- Reconnect automatically when TcXaeShell restarts.
- Log diagnostics to `%TEMP%\STBud_Host.log`.

The Host must run at the same elevation level as TcXaeShell. The installer starts it through `explorer.exe` so it runs non-elevated after an elevated install.

---

## Multi-Targeting

`STFormatter.Core` compiles against `net8.0`, `net48`, and `net462`:

```
STFormatter.Core
+-- net8.0  -> STFormatter.CLI
+-- net48   -> STFormatter.Host / STFormatter.UI on current machines
+-- net462  -> STFormatter.Host / STFormatter.UI on older machines
```

Rationale:

- `net8.0` supports the modern CLI.
- `net48` supports current Windows/TcXaeShell deployments.
- `net462` supports older TcXaeShell environments.
- One shared engine prevents drift between CLI and Host behavior.

---

## Configuration Flow

CLI configuration:

```
Command-line args
    -> .editorconfig
    -> FormattingConfiguration.Default
```

Host configuration:

```
Tray settings
    -> .editorconfig
    -> FormattingConfiguration.Default
```

Team-wide style should be stored in `.editorconfig`. Host settings are user preferences stored under `%APPDATA%\STBud\settings.json`.

---

## Design Decisions

### Hand-Written Lexer And Parser

The lexer and parser are hand-written to keep full control over trivia handling, error recovery, and formatter-specific grammar behavior without generator dependencies.

### Immutable Syntax Tree

Syntax nodes are immutable after construction. This keeps formatting predictable and makes unit tests straightforward.

### FormatBody Wrapper

`FormatBody(string body)` wraps raw implementation text in a temporary `PROGRAM __BODY_WRAPPER__` envelope, formats it as a complete program, then strips the wrapper and common indent.

### Live Edit Over Disk Writes

The TcXaeShell Host formats active editor content through DTE commands and the Win32 clipboard. It avoids writing active files to disk because that triggers TwinCAT reload prompts.

### No In-Process TcXaeShell Extension

TcXaeShell does not reliably load custom VSPackages, MEF components, VSIX extensions, or AddIns. The external Host process is the supported architecture.
