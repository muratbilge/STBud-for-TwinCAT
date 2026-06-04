# STBud for TwinCAT

A toolbox for Beckhoff TwinCAT Structured Text work: formatting, editor helpers,
pragma insertion, I/O linking assistance, and workflow utilities inside TcXaeShell.

------------------------------------------------------------------------

## Overview

STBud for TwinCAT is a growing collection of tools for TwinCAT developers. The
current core is a Structured Text formatter, backed by a TcXaeShell Host that
also injects practical editor helpers such as pragma insertion, region helpers,
and I/O linking assistance. The formatter covers the full IEC 61131-3 ST grammar
plus TwinCAT-specific extensions (actions, multi-instance FBs, `__TRY`/`__ENDTRY`,
access modifiers, pragmas, and more).

| | CLI | TcXaeShell Host |
|---|---|---|
| **Project** | `STFormatter.CLI` | `STFormatter.Host` |
| **Target framework** | net8.0 | net462;net48 |
| **Platform** | Any CPU | x86 |
| **Host** | Terminal | External process |
| **Editor integration** | -- | COM DTE + live edit |
| **Options UI** | .editorconfig / JSON | Tray UI + .editorconfig |

The internal assemblies still use the `STFormatter.*` prefix because the first
major tool is the formatter. The product name is broader: **STBud for TwinCAT**.

------------------------------------------------------------------------

## Components

### STFormatter.Core

The formatting engine, multi-targeting `net8.0`, `net48`, and `net462`.

- **Lexer** -- hand-written scanner producing `SyntaxToken` + `SyntaxTrivia`
  streams. Supports all IEC 61131-3 literals (integer, real, string, time,
  date, bit-string), TwinCAT pragmas (`{pragma ...}`), and region directives.
- **Parser** -- recursive descent parser producing an immutable `SyntaxTree`.
  Handles PROGRAM, FUNCTION_BLOCK, FUNCTION, METHOD, PROPERTY, ACTION,
  INTERFACE declarations, VAR sections (INPUT, OUTPUT, IN_OUT, TEMP, GLOBAL,
  PERSISTENT, etc.), all control-flow statements, expressions, and type
  definitions.
- **Formatter** -- visitor-based `FormattingVisitor` walks the tree and emits
  reformatted text through `FormattingWriter`. Keyword casing, indentation,
  spacing, alignment, and line-break decisions are all driven by
  `FormattingConfiguration`.
- **Configuration** -- `FormattingConfiguration` with built-in presets (Default,
  Compact, Expanded) plus `.editorconfig` file support (`EditorConfigParser`
  walks up from the source file's directory to find applicable settings).

Architecture diagram:

```
SourceText --> Lexer --> Parser --> SyntaxTree --> FormattingVisitor --> FormattingWriter
```

### STFormatter.CLI

Standalone .NET 8 command-line tool.

```
Usage:
  stfmt format <file> [-o <output>] [--dry-run]
  stfmt check <file|directory> [--recursive]
  stfmt batch <directory> [--recursive] [--twincat] [--dry-run]
  stfmt init [directory] [--preset <name>]
  stfmt preset [name]
  stfmt export [file] [--preset <name>]
  stfmt import <json-file>
```

| Command | Description |
|---|---|
| `format` | Format a single ST file in place or to a new path |
| `check` | Verify files match formatting rules (CI-friendly, exit code 1 on mismatch) |
| `batch` | Format all ST files in a directory tree |
| `init` | Generate an `.editorconfig` from a preset |
| `preset` | List or inspect available presets |
| `export` | Export a configuration preset to JSON |
| `import` | Import a JSON configuration file and write an `.editorconfig` |

### STFormatter.Host (external process — production)

Standalone .NET Framework 4.8 x86 executable that injects context menu buttons
into TcXaeShell via COM DTE from outside the process.

- **Context menu injection** — hooks into `PlcCodeWinContextMenu` (Beckhoff PLC editor
  right-click menu) and `Code Window` (standard VS text editor menu) via
  `DTE.CommandBars` programmatic injection
- **Format Document** / **Format Selection** — uses DTE editor commands and the Win32
  clipboard to format the active declaration or implementation section in place
- **Format File** — reads `.TcPOU` / `.TcDUT` / `.TcGVL` XML files, formats the ST code
  inside CDATA sections, and writes them back
- **Editor helpers** — inserts common TwinCAT attributes/pragmas, regions, task
  attributes, warnings, and I/O-linking paths from context menus
- **Auto-reconnect** — survives TcXaeShell restarts, reconnects automatically
- **Hidden background process** — no console window, runs silently, logs to
  `%TEMP%\STBud_Host.log`
- **Independent deployment** — copies to `C:\Program Files (x86)\STBud\` alongside the
  Core DLL, requires `Microsoft.VisualStudio.Interop.dll`

**Why an external process?** TcXaeShell's isolated shell blocks all three standard
VS extension mechanisms:
- VSPackage: `ProvideAutoLoad` doesn't fire, package never loads
- MEF: custom `MefComponent` entries are not added to the composition catalog
- AddIn: `.AddIn` files in the AddIns folder are ignored

The external Host connects via COM DTE's Running Object Table (ROT) moniker
`!TcXaeShell.DTE.15.0:{PID}`, which is always available while TcXaeShell is
running. See [AGENTS.md](AGENTS.md) for full details.

------------------------------------------------------------------------

## Supported File Types

| Extension | Format | Notes |
|---|---|---|
| `.st` | Plain ST source | Standalone files |
| `.txt` | Plain ST source | Common TwinCAT convention |
| `.iecst` | Plain ST source | IEC 61131-3 convention |
| `.TcPOU` | TwinCAT XML | Contains `<Declaration>` and `<Implementation>` CDATA |
| `.TcDUT` | TwinCAT XML | Data type definition |
| `.TcGVL` | TwinCAT XML | Global variable list |
| `.TcIO` | TwinCAT XML | IO mapping |
| `.TcTO` | TwinCAT XML | Task object |

------------------------------------------------------------------------

## Formatting Pipeline

```
SourceText
    |
    v
  Lexer          (hand-written scanner)
    |
    v
  Parser         (recursive descent, immutable tree)
    |
    v
 SyntaxTree      (Root + Diagnostics)
    |
    v
 FormattingVisitor  (walks tree, applies configuration)
    |
    v
 FormattingWriter   (emits indented, re-spaced text)
    |
    v
 Formatted output string
```

The pipeline is stateless: `FormattingEngine.Format(source)` lexes, parses,
and formats a complete compilation unit. `FormattingEngine.FormatBody(body)`
wraps a free-standing statement list in a temporary `PROGRAM __BODY_WRAPPER__`
declaration so that code fragments (implementation bodies) can also be
formatted.

------------------------------------------------------------------------

## Configuration

### Options

| Option | Default | Description |
|---|---|---|
| `IndentStyle` | `spaces` | `spaces` or `tabs` |
| `IndentSize` | `4` | Columns per indent level |
| `ContinuationIndentSize` | `8` | Columns for continuation lines |
| `NewLineStyle` | `crlf` | `crlf`, `lf`, or `cr` |
| `KeywordCasing` | `upper` | `upper`, `lower`, `pascal`, or `original` |
| `BraceStyle` | `allman` | `allman` or `compact` — controls vertical spacing between sections |
| `SpaceAroundOperators` | `true` | Spaces around binary operators |
| `SpaceAfterComma` | `true` | Space after commas in argument lists |
| `SpaceBeforeSemicolon` | `false` | Space before semicolons |
| `SpaceAfterColon` | `true` | Space after colons in declarations |
| `AlignAssignments` | `true` | Align `:=` in consecutive assignment blocks |
| `AlignVariableDeclarations` | `true` | Pad names and types in VAR sections |
| `MaxLineLength` | `120` | Line-length limit for wrapping (0 = unlimited) |
| `EmptyLinesBetweenPOUs` | `2` | Blank lines between top-level declarations |
| `EmptyLinesBetweenVarSections` | `1` | Blank lines between VAR blocks |
| `KeepSingleLineBlocks` | `false` | Keep single-statement blocks on one line |
| `FormatOnSave` | `true` | Auto-format hint for editor integrations |

### Presets

| | Default | Compact | Expanded |
|---|---|---|---|
| Indent | 4 spaces | 2 spaces | 4 spaces |
| Continuation | 8 | 4 | 8 |
| Keywords | UPPER | lower | UPPER |
| Brace style | allman | compact | allman |
| Align assignments | Yes | No | Yes |
| Align declarations | Yes | No | Yes |
| Max line length | 120 | 120 | 80 |
| Empty lines between POUs | 2 | 1 | 3 |
| Single-line blocks | No | Yes | No |

### .editorconfig

The formatter reads `.editorconfig` files. ST-specific options use the
`st_` prefix:

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
st_align_variable_declarations = true
st_align_assignments = true
st_empty_lines_between_pous = 2
st_empty_lines_between_var_sections = 1
st_format_on_save = true
```

All ST-specific `.editorconfig` properties:

| Property | Maps to | Values |
|---|---|---|
| `st_keyword_casing` | `KeywordCasing` | `upper`, `lower`, `pascal`, `original` |
| `st_brace_style` | `BraceStyle` | `allman`, `compact` (also: `kr`, `k&r`, `stroustrup`) |
| `st_space_around_operators` | `SpaceAroundOperators` | `true`, `false` |
| `st_space_after_comma` | `SpaceAfterComma` | `true`, `false` |
| `st_space_before_semicolon` | `SpaceBeforeSemicolon` | `true`, `false` |
| `st_space_after_colon` | `SpaceAfterColon` | `true`, `false` |
| `st_align_variable_declarations` | `AlignVariableDeclarations` | `true`, `false` |
| `st_align_assignments` | `AlignAssignments` | `true`, `false` |
| `st_empty_lines_between_pous` | `EmptyLinesBetweenPOUs` | integer |
| `st_empty_lines_between_var_sections` | `EmptyLinesBetweenVarSections` | integer |
| `st_keep_single_line_blocks` | `KeepSingleLineBlocks` | `true`, `false` |
| `st_continuation_indent_size` | `ContinuationIndentSize` | integer |
| `st_format_on_save` | `FormatOnSave` | `true`, `false` |

Use `stfmt init . --preset default` to generate an `.editorconfig` from a
preset, or `stfmt import myconfig.json` to convert a JSON export.

------------------------------------------------------------------------

## Grammar Coverage

The parser handles the full IEC 61131-3 ST grammar plus TwinCAT extensions:

- **POU declarations**: PROGRAM, FUNCTION_BLOCK, FUNCTION, METHOD, PROPERTY,
  ACTION, INTERFACE
- **VAR sections**: VAR, VAR_INPUT, VAR_OUTPUT, VAR_IN_OUT, VAR_TEMP,
  VAR_STAT, VAR_GLOBAL, VAR_ACCESS, VAR_EXTERNAL, VAR_CONFIG, VAR_INST
  with modifiers CONSTANT, RETAIN, PERSISTENT, READ_ONLY, READ_WRITE
- **Data types**: elementary types (BOOL, INT, DINT, REAL, ...), ARRAY ...
  OF, STRUCT, ENUM, STRING/WSTRING, POINTER TO, REFERENCE TO, UNION
- **Control flow**: IF/ELSIF/ELSE/END_IF, CASE/ELSE/END_CASE,
  FOR/TO/BY/DO/END_FOR, WHILE/DO/END_WHILE, REPEAT/UNTIL/END_REPEAT,
  EXIT, CONTINUE, RETURN, GOTO
- **TwinCAT extensions**: `__TRY`/`__CATCH`/`__FINALLY`/`__ENDTRY`,
  {pragma ...} directives, access modifiers (PUBLIC, PRIVATE, PROTECTED,
  INTERNAL, FINAL, ABSTRACT, OVERRIDE), THIS, SUPER
- **Expressions**: binary/unary operators, member access `.`, element access
  `[]`, invocations, named arguments, initializers
- **Comments**: single-line `//`, multi-line `(* ... *)` and `/* ... */`
- ** pragmas**: `{pragma ...}`, `{region ...}`, `{endregion}`

## Using STBud for TwinCAT

### 1. CLI — Format Files from the Command Line

```shell
# Build the CLI
dotnet build src/STFormatter.CLI -c Release

# Format a single file (overwrites in place)
dotnet run --project src/STFormatter.CLI format MyProgram.st

# Dry-run (print result without changing the file)
dotnet format MyProgram.st --dry-run

# Format to a different output file
dotnet format MyProgram.st -o MyProgram_formatted.st

# Batch-format all .st files in a directory
dotnet format batch ./POUs --recursive

# Batch-format TwinCAT XML files (.TcPOU, .TcDUT, .TcGVL)
dotnet format batch ./MyProject --recursive --twincat

# Check if files are formatted (CI mode — exit code 1 on mismatch)
dotnet format check ./src --recursive

# Generate .editorconfig from a preset
dotnet format init . --preset default

# View preset details
dotnet format preset default
```

### 2. TcXaeShell — Format Inside the PLC Editor

```shell
# Build the Host
dotnet build src/STFormatter.Host -c Debug

# Deploy the external Host (requires admin)
deploy.bat

# Start the Host (auto-detects running TcXaeShell)
Start-Process "C:\Program Files (x86)\STBud\STFormatter.Host.exe"
```

Once running, the Host injects **Format ST Document** and **Format ST Selection** buttons
into the PLC editor's right-click context menu. Click either to format the active
declaration or implementation section.

The Host also provides a system tray icon with:
- **Settings** — change formatting options at runtime
- **Instances** — view connected TcXaeShell processes
- **History** — review past format operations
- **Log** — live log viewer (`%TEMP%\STBud_Host.log`)

### 3. Configuration via .editorconfig

The formatter reads `.editorconfig` files walking up from each source file's directory. Create one:

```shell
# Generate from a preset
dotnet format init . --preset default
```

This creates:

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
max_line_length = 120

[*.st]
st_keyword_casing = upper
st_space_around_operators = true
st_align_variable_declarations = true
st_align_assignments = true
st_empty_lines_between_pous = 2
st_empty_lines_between_var_sections = 1
st_format_on_save = true
```

All configuration options work as `.editorconfig` properties with the `st_` prefix.

### 4. Sample Files

The `samples/` directory contains test files:

| Directory | Contents |
|---|---|
| `SampleSTFiles/` | 20 hand-crafted `.st` files + 5 synthetic TwinCAT XML files covering STRUCT, ENUM, ARRAY, POINTER, FUNCTION_BLOCK, etc. |
| `RealTcFiles/` | 27 real TwinCAT project files (12 `.TcDUT`, 5 `.TcGVL`, 10 `.TcPOU`) from State Pattern, Observer, Factory, and Component projects |

```shell
# Format all real samples as a dry-run
dotnet format batch ./samples/RealTcFiles --twincat --dry-run

# Format a single real file
dotnet format ./samples/RealTcFiles/Execute.TcPOU --dry-run
```

------------------------------------------------------------------------

## Documentation

| Document | Description |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Engine internals: lexer, parser, tree, visitor |
| [docs/TcXaeShell-Integration.md](docs/TcXaeShell-Integration.md) | TcXaeShell COM DTE integration reference |
| [docs/FORMAT-OPTIONS.md](docs/FORMAT-OPTIONS.md) | Complete configuration option reference |
| [docs/HOW-TO-INSTALL.md](docs/HOW-TO-INSTALL.md) | Installation instructions for all targets |
| [docs/HOW-TO-USE.md](docs/HOW-TO-USE.md) | Practical usage guide |
| [docs/CHANGELOG.md](docs/CHANGELOG.md) | Release history |
| [docs/API-REFERENCE.md](docs/API-REFERENCE.md) | Public API reference for STFormatter.Core |

------------------------------------------------------------------------

## Project Structure

```
CodeFormatter/
  TwinCAT.STFormatter.sln
  src/
    STFormatter.Core/          Formatting engine (net8.0 / net48 / net462)
      Lexing/                  Lexer.cs, diagnostics
      Parsing/                 Parser.cs
      Syntax/                  SyntaxTree, SyntaxNode, SyntaxToken, SyntaxKind, ...
      Text/                    SourceText, TextSpan
      Configuration/           FormattingConfiguration, EditorConfigParser
      Formatting/              FormattingEngine, FormattingVisitor, FormattingWriter
    STFormatter.CLI/           Command-line interface (net8.0)
    STFormatter.Host/          TcXaeShell external host (net48 / x86, production)
      Program.cs               Main host executable — DTE connection, context menu injection,
                               formatting engine integration, auto-reconnect
      STFormatter.Host.csproj  Project file — references Microsoft.VisualStudio.Interop
    STFormatter.UI/            Tray UI, settings, instances, history, diff viewer
  tests/
    STFormatter.Core.Tests/    Unit tests
  docs/                        Documentation
```

------------------------------------------------------------------------

## License

See [LICENSE](LICENSE) for details.
