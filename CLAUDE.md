# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read AGENTS.md First

[AGENTS.md](AGENTS.md) is the TcXaeShell integration survival guide. It documents, with evidence, why
VSPackage/MEF/AddIn approaches **do not work** in TcXaeShell and must never be retried, and how the
working external-process COM DTE approach functions (ROT monikers, clipboard live-edit, deployment
rules). Any work touching `STFormatter.Host`, `STFormatter.UI`, deployment, or the installer requires
reading it.

Non-negotiable rules from it:
- TcXaeShell integration is an **external process via COM DTE** only — never VSPackage, MEF, VSIX, or AddIn.
- Never deploy STBud files into Beckhoff's TcXaeShell `Extensions` tree; the Host lives in `C:\Program Files (x86)\STBud\`.
- Live edit uses DTE `Edit.SelectAll/Copy/Delete/Paste` + Win32 clipboard API (not `System.Windows.Forms.Clipboard`, not `TextSelection.Text`).
- Search the ROT for both `!TcXaeShell.DTE.*` and `!VisualStudio.DTE.*` monikers.

## Commands

```powershell
# Build everything
dotnet build TwinCAT.STFormatter.sln

# Run all tests (net8.0, xUnit)
dotnet test tests/STFormatter.Core.Tests

# Run a single test class / test
dotnet test tests/STFormatter.Core.Tests --filter "FullyQualifiedName~FormatterTests"
dotnet test tests/STFormatter.Core.Tests --filter "DisplayName~MethodName"

# Run the CLI
dotnet run --project src/STFormatter.CLI -- format <file> [--dry-run]
dotnet run --project src/STFormatter.CLI -- batch ./samples/RealTcFiles --twincat --dry-run

# Build + deploy the Host to C:\Program Files (x86)\STBud (requires admin)
dotnet build src/STFormatter.Host/STFormatter.Host.csproj -c Debug
deploy.bat            # net48 (default)
deploy.bat net462     # older machines

# Host log when debugging TcXaeShell integration
Get-Content "$env:TEMP\STBud_Host.log" -Tail 20

# Build the installer (Inno Setup)
installer\build-installer.ps1
```

## Architecture

STBud for TwinCAT is a toolbox for Beckhoff TwinCAT Structured Text; the core tool is an
IEC 61131-3 ST formatter. Four projects in one solution (assemblies keep the legacy
`STFormatter.*` prefix; the product name is STBud):

- **STFormatter.Core** (net8.0 / net48 / net462) — the formatting engine. Pure pipeline:
  `SourceText → Lexer → Parser → SyntaxTree → FormattingVisitor → FormattingWriter`.
  Hand-written lexer and recursive-descent parser produce an immutable syntax tree covering the
  full ST grammar plus TwinCAT extensions (`__TRY`, pragmas, access modifiers, actions).
  `FormattingEngine` exposes three entry points: `Format()` for full compilation units,
  `FormatBody()` for bare implementation bodies (wraps them in a temporary `PROGRAM`), and
  `FormatDeclaration()` for bare VAR sections. Configuration comes from `FormattingConfiguration`
  (presets: Default/Compact/Expanded) or `.editorconfig` (`st_*` properties, parsed by
  `EditorConfigParser` walking up from the source file). `TwinCatXmlFormatter` handles
  `.TcPOU/.TcDUT/.TcGVL` files by formatting the ST inside CDATA sections.
  `IoTree/` parses `.tsproj` files for the I/O linking browser.

- **STFormatter.CLI** (net8.0) — `stfmt` command-line tool: `format`, `check` (CI, exit 1 on
  mismatch), `batch`, `init`/`preset`/`export`/`import` for configuration.

- **STFormatter.Host** (net48/net462, x86, `Exe` with hidden console — `WinExe` breaks COM) —
  the production TcXaeShell integration. Connects from outside the process via COM DTE ROT
  moniker, injects the context menu into `PlcCodeWinContextMenu` / `Code Window` via
  `DTE.CommandBars`, registers Ctrl+Shift+F/D via a low-level keyboard hook (`KeyboardHook.cs`),
  and formats the active editor section through the clipboard live-edit pipeline
  (`LiveEditor.cs`). Detects Declaration vs Implementation heuristically from content — there is
  no DTE API for the active tab. Auto-reconnects across TcXaeShell restarts.

- **STFormatter.UI** (net48/net462) — tray UI: settings, instance list, history, diff viewer.

Tests live only against Core (`tests/STFormatter.Core.Tests`, xUnit on net8.0). Host/UI are
COM/Win32-bound and untested; verify them manually against a running TcXaeShell.

`samples/RealTcFiles/` (real TwinCAT project files) and `samples/SampleSTFiles/` are the
regression corpus — `stfmt batch <dir> --twincat --dry-run` is the quick smoke test.

## Version Compatibility

All TcXaeShell version-specific values (DTE versions 12.0/14.0/15.0, moniker prefixes, paths)
are centralized in `TcXaeShellVersionProfile` (`STFormatter.Core/Configuration/`) and
auto-detected from the ROT moniker at runtime. Don't hard-code version strings elsewhere.
