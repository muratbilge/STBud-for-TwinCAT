# Technical Architecture / Technische Architektur

TwinCAT ST Formatter — Structured Text (IEC 61131-3) code formatter for Beckhoff TwinCAT projects.

---

## Table of Contents / Inhaltsverzeichnis

1. [Solution Overview / Loesungsueberblick](#1-solution-overview--loesungsueberblick)
2. [Project Descriptions / Projektbeschreibungen](#2-project-descriptions--projektbeschreibungen)
3. [Formatting Pipeline / Formatierungspipeline](#3-formatting-pipeline--formatierungspipeline)
4. [STFormatter.Core — The Engine / Die Engine](#4-stformattercore----the-engine--die-engine)
   - 4.1 [Lexer](#41-lexer--lexer)
   - 4.2 [Parser](#42-parser--parser)
   - 4.3 [Syntax Tree](#43-syntax-tree--syntaxbaum)
   - 4.4 [Formatting Engine](#44-formatting-engine--formatierungsengine)
   - 4.5 [Configuration](#45-configuration--konfiguration)
   - 4.6 [Text Infrastructure](#46-text-infrastructure--textinfrastruktur)
5. [STFormatter.CLI — Command Line Interface / Kommandozeile](#5-stformattercli----command-line-interface--kommandozeile)
6. [STFormatter.VSIX — Visual Studio Extension / VS-Erweiterung](#6-stformattervsix----visual-studio-extension--vs-erweiterung)
7. [STFormatter.TcXaeShell — TwinCAT Extension / TcXaeShell-Erweiterung](#7-stformattertcxaeshell----twincat-extension--tcxaeshell-erweiterung)
8. [Multi-Targeting Strategy / Multi-Targeting-Strategie](#8-multi-targeting-strategy--multi-targeting-strategie)
9. [Configuration Flow / Konfigurationsablauf](#9-configuration-flow--konfigurationsablauf)
10. [Design Decisions / Entwurfsentscheidungen](#10-design-decisions--entwurfsentscheidungen)

---

## 1. Solution Overview / Loesungsueberblick

```
TwinCAT.STFormatter.sln
|
+-- src/
|   +-- STFormatter.Core/        net8.0;net48;net462   Formatting engine
|   +-- STFormatter.CLI/         net8.0                 Command-line interface
|   +-- STFormatter.VSIX/        net48                  Visual Studio 2022 extension
|   +-- STFormatter.TcXaeShell/  net462;net48/x86        TcXaeShell extension
|
+-- tests/
|   +-- STFormatter.Core.Tests/  net8.0, xUnit          57 unit tests
|
+-- docs/                                               Documentation
+-- samples/                                            Sample ST files
```

The solution contains four projects sharing a single formatting engine (`STFormatter.Core`). The Core project is multi-targeted so that every consumer — CLI (.NET 8), Visual Studio (.NET Framework 4.8), and TcXaeShell (.NET Framework 4.6.2 or 4.8, x86) — references the same code compiled for its runtime.

---

## 2. Project Descriptions / Projektbeschreibungen

| Project / Projekt                | Target Framework    | Role / Rolle                                                            |
|-----------------------------------|---------------------|-------------------------------------------------------------------------|
| `STFormatter.Core`               | net8.0;net48;net462 | Immutable formatting engine: lexer, parser, syntax tree, visitor      |
| `STFormatter.CLI`                | net8.0              | Command-line tool for batch formatting, checking, and config management |
| `STFormatter.VSIX`               | net48               | Visual Studio 2022 extension with commands, options page, format-on-save |
| `STFormatter.TcXaeShell`         | net462;net48/x86    | TwinCAT XAE Shell extension with identical features as VSIX            |
| `STFormatter.Core.Tests`         | net8.0              | xUnit test suite (57 tests) covering lexer, parser, and formatting    |

---

## 3. Formatting Pipeline / Formatierungspipeline

```
 SourceText
    |
    v
 +--------+
 |  Lexer |  Tokenisation with trivia
 +--------+
    |
    v
 +---------+
 | Parser  |  Recursive descent, error recovery
 +---------+
    |
    v
 +-------------+
 | SyntaxTree  |  Immutable, full-fidelity tree
 +-------------+
    |
    v
 +--------------------+
 | FormattingVisitor  |  Walks tree, emits formatting ops
 +--------------------+
    |
    v
 +------------------+
 | FormattingWriter |  Applies indentation, spacing, newlines
 +------------------+
    |
    v
 Output (string)
```

Each stage is a pure transformation. The pipeline never mutates input; every stage produces a new object. This design enables:

- **Incremental formatting** — the tree can be reused when only configuration changes.
- **Round-trip fidelity** — trivia preservation ensures no information loss.
- **Testability** — each stage can be unit-tested in isolation.

---

## 4. STFormatter.Core — The Engine / Die Engine

The Core project is structured into five logical subsystems:

```
STFormatter.Core/
+-- Lexing/
|   +-- Lexer.cs
|
+-- Parsing/
|   +-- Parser.cs
|
+-- Syntax/
|   +-- SyntaxNode.cs
|   +-- SyntaxToken.cs
|   +-- SyntaxTrivia.cs
|   +-- SyntaxKind.cs
|   +-- ConcreteNodes.cs
|
+-- Formatting/
|   +-- FormattingEngine.cs
|   +-- FormattingVisitor.cs
|   +-- FormattingWriter.cs
|
+-- Configuration/
|   +-- FormattingConfiguration.cs
|   +-- EditorConfigParser.cs
|   +-- Presets.cs
|
+-- Text/
    +-- SourceText.cs
    +-- TextSpan.cs
```

### 4.1 Lexer / Lexer

**File**: `Lexing/Lexer.cs`

A hand-written lexer that produces `SyntaxToken` objects with attached `SyntaxTrivia`. It is responsible for:

- Scanning the raw source text character by character
- Classifying lexemes into token kinds (keywords, identifiers, literals, operators, punctuation)
- Capturing leading and trailing trivia (whitespace, line comments `//`, block comments `(* ... *)`, newlines)
- Supporting the full set of IEC 61131-3 ST keywords plus TwinCAT-specific extensions

```
 SourceText
    |
    v
 Lexer.Scan()
    |
    +---> SyntaxToken { Kind, Text, Position, LeadingTrivia[], TrailingTrivia[] }
    |
    v
 Token stream (IEnumerable<SyntaxToken>)
```

**Trivia rules**:

- Leading trivia: whitespace and comments between the previous token and the current token.
- Trailing trivia: whitespace and comments on the same line after a token, before the next newline.
- Newline trivia is attached to the preceding token as trailing trivia or to the following token as leading trivia, depending on context.

### 4.2 Parser / Parser

**File**: `Parsing/Parser.cs`

A recursive descent parser that consumes the token stream produced by the Lexer and constructs an immutable `SyntaxTree`.

Key characteristics:

- **Error recovery**: When a syntax error is encountered, the parser inserts missing tokens and continues, rather than aborting. This ensures partial formatting is still possible on malformed input.
- **Full fidelity**: Every character of the input is represented in the tree, including trivia.
- **Immutability**: All nodes are created via factory methods; no node is ever mutated after construction.

```
 Token stream
    |
    v
 Parser.Parse()
    |
    +---> ProgramNode
          +-- Usings[]
          +-- PouDeclarations[] (PROGRAM, FUNCTION, FUNCTION_BLOCK, etc.)
              +-- VarSections[] (VAR, VAR_INPUT, VAR_OUTPUT, VAR_IN_OUT, VAR CONSTANT, etc.)
              +-- Body (statement list)
              +-- EndKeyword
```

**Grammar coverage (IEC 61131-3 ST)**:

- POU declarations: `PROGRAM`, `FUNCTION`, `FUNCTION_BLOCK`, `ACTION`, `METHOD`
- Variable sections: `VAR`, `VAR_INPUT`, `VAR_OUTPUT`, `VAR_IN_OUT`, `VAR_CONSTANT`, `VAR_RETAIN`, `VAR_PERSISTENT`, and combinations
- Statements: assignment, if/elsif/else, case, for, while, repeat, exit, return, continue, function calls
- Expressions: binary operators, unary operators, parenthesised, function calls, array access, struct/REF access
- Pragmas and attributes: `{attribute ...}`, `{pragma ...}`

### 4.3 Syntax Tree / Syntaxbaum

**Files**: `Syntax/SyntaxNode.cs`, `Syntax/SyntaxToken.cs`, `Syntax/SyntaxTrivia.cs`, `Syntax/SyntaxKind.cs`, `Syntax/ConcreteNodes.cs`

The syntax tree is an immutable, full-fidelity representation of the source program.

#### Node Hierarchy / Knotenhierarchie

```
SyntaxNode (abstract)
    |
    +-- ProgramNode
    |     +-- Usings
    |     +-- Declarations
    |     +-- EndKeyword
    |
    +-- FunctionBlockNode
    +-- FunctionNode
    +-- ActionNode
    +-- MethodNode
    +-- VarSectionNode
    +-- VarDeclarationNode
    +-- StatementNode (abstract)
    |     +-- AssignmentStatementNode
    |     +-- IfStatementNode
    |     +-- CaseStatementNode
    |     +-- ForStatementNode
    |     +-- WhileStatementNode
    |     +-- RepeatStatementNode
    |     +-- ExitStatementNode
    |     +-- ReturnStatementNode
    |     +-- ContinueStatementNode
    |     +-- ExpressionStatementNode
    |
    +-- ExpressionNode (abstract)
    |     +-- BinaryExpressionNode
    |     +-- UnaryExpressionNode
    |     +-- LiteralExpressionNode
    |     +-- IdentifierExpressionNode
    |     +-- ParenthesizedExpressionNode
    |     +-- CallExpressionNode
    |     +-- ArrayAccessExpressionNode
    |     +-- MemberAccessExpressionNode
    |
    +-- UsingDirectiveNode
    +-- PragmaNode

SyntaxToken (leaf)
    +-- Kind: SyntaxKind enum
    +-- Text: string
    +-- Position: int
    +-- LeadingTrivia: SyntaxTrivia[]
    +-- TrailingTrivia: SyntaxTrivia[]

SyntaxTrivia
    +-- Kind: SyntaxKind (Whitespace, EndOfLine, SingleLineComment, MultiLineComment)
    +-- Text: string
```

#### SyntaxKind Enum

The `SyntaxKind` enum enumerates every node type, token type, and trivia type in the grammar. Examples:

- Node kinds: `Program`, `FunctionBlock`, `Function`, `VarSection`, `AssignmentStatement`, `IfStatement`, ...
- Token kinds: `ProgramKeyword`, `EndProgramKeyword`, `IfKeyword`, `ElseKeyword`, `Identifier`, `NumericLiteral`, `StringLiteral`, `PlusToken`, `MinusToken`, `EqualsToken`, ...
- Trivia kinds: `WhitespaceTrivia`, `EndOfLineTrivia`, `SingleLineCommentTrivia`, `MultiLineCommentTrivia`

### 4.4 Formatting Engine / Formatierungsengine

**Files**: `Formatting/FormattingEngine.cs`, `Formatting/FormattingVisitor.cs`, `Formatting/FormattingWriter.cs`

The formatting engine provides three entry points:

| Method / Methode                     | Purpose / Zweck                                                          |
|---------------------------------------|--------------------------------------------------------------------------|
| `Format(string source)`              | Full document formatting. Wraps bare bodies in `PROGRAM`/`END_PROGRAM` if needed. |
| `FormatBody(string body)`            | Standalone body formatting. Wraps in `PROGRAM __BODY_WRAPPER__`, formats, then strips the wrapper and common indent. |
| `Format(SyntaxTree tree)`            | Format from an already-parsed syntax tree.                              |

#### FormattingVisitor

The `FormattingVisitor` walks the syntax tree depth-first and emits formatting operations to the `FormattingWriter`. For each node kind, the visitor:

1. Writes leading trivia (preserving or normalising based on configuration).
2. Writes the node's own tokens (applying keyword casing, spacing rules).
3. Recursively visits child nodes.
4. Writes trailing trivia and line breaks per configuration.

```
 SyntaxTree
    |
    v
 FormattingVisitor.Visit(root)
    |
    +-- VisitProgramNode(node)
    |     +-- Write keyword "PROGRAM" (apply casing)
    |     +-- Write space
    |     +-- Visit identifier
    |     +-- Write newline
    |     +-- Indent + Visit children
    |     +-- Write keyword "END_PROGRAM" (apply casing)
    |
    +-- VisitVarSectionNode(node)
    |     +-- Write keyword "VAR_INPUT" (apply casing)
    |     +-- Write newline
    |     +-- Indent + Visit declarations
    |     +-- Write keyword "END_VAR" (apply casing)
    |
    +-- VisitIfStatementNode(node)
          +-- Write keyword "IF" + space + condition + space + "THEN"
          +-- Write newline + indent + Visit body
          +-- Write "ELSIF" / "ELSE" branches
          +-- Write "END_IF"
```

#### FormattingWriter

The `FormattingWriter` is a stateful text writer that maintains:

- **Current indentation level** — incremented/decremented on block entry/exit.
- **Current line position** — tracks column for line-length decisions.
- **Pending newline state** — collapses multiple blank lines to the configured maximum.

It translates abstract formatting operations into concrete text output:

```
 FormattingWriter Operations
 +---------------------------+--------------------------------------------------+
 | Operation                 | Effect                                           |
 +---------------------------+--------------------------------------------------+
 | Write(text)               | Appends text at current position                 |
 | WriteKeyword(kind, text)  | Appends keyword with configured casing           |
 | WriteSpace()              | Appends a single space                           |
 | WriteLine()               | Appends newline, respecting NewLineStyle         |
 | WriteLineBreak()          | Appends newline with indentation                 |
 | Indent()                  | Increases indentation level by 1                 |
 | Outdent()                 | Decreases indentation level by 1                 |
 | BlankLines(count)         | Ensures exactly `count` blank lines              |
 +---------------------------+--------------------------------------------------+
```

### 4.5 Configuration / Konfiguration

**Files**: `Configuration/FormattingConfiguration.cs`, `Configuration/EditorConfigParser.cs`, `Configuration/Presets.cs`

#### FormattingConfiguration

All formatting settings are held in `FormattingConfiguration`, an immutable options object. Every setting has a default value.

| Property / Eigenschaft             | Type             | Default     | Description / Beschreibung                              |
|--------------------------------------|------------------|-------------|---------------------------------------------------------|
| `IndentStyle`                       | `IndentStyle`    | `Space`     | Spaces or tabs                                          |
| `IndentSize`                        | `int`            | `4`         | Columns per indent level                                |
| `ContinuationIndentSize`            | `int`            | `8`         | Indent for wrapped/continued lines                      |
| `NewLineStyle`                      | `NewLineStyle`   | `LF`        | Line ending style (LF, CRLF, CR, Auto)                  |
| `KeywordCasing`                     | `KeywordCasing`  | `Upper`     | Keyword capitalisation (Upper, Lower, Pascal)            |
| `BraceStyle`                        | `BraceStyle`     | `NextLine`  | Placement of structural braces                         |
| `SpaceAroundOperators`              | `bool`           | `true`      | Spaces around binary operators                          |
| `SpaceAfterComma`                   | `bool`           | `true`      | Space after commas in argument lists                    |
| `SpaceBeforeSemicolon`              | `bool`           | `false`     | Space before semicolons                                 |
| `SpaceAfterColon`                   | `bool`           | `true`      | Space after colons in declarations                      |
| `AlignAssignments`                  | `bool`           | `false`     | Align `:=` in variable declarations                     |
| `AlignVariableDeclarations`         | `bool`           | `false`     | Align names, types, and initialisers in VAR sections    |
| `MaxLineLength`                     | `int`            | `120`       | Maximum line length before wrapping                     |
| `EmptyLinesBetweenPOUs`             | `int`            | `1`         | Blank lines between POU declarations                    |
| `EmptyLinesBetweenVarSections`      | `int`            | `1`         | Blank lines between VAR sections                        |
| `KeepSingleLineBlocks`              | `bool`           | `true`      | Keep IF/CASE blocks on one line if originally single-line |
| `FormatOnSave`                      | `bool`           | `false`     | Auto-format on file save (VS/TcXaeShell only)          |

#### Presets

| Preset / Vorlage      | Description / Beschreibung                                           |
|------------------------|----------------------------------------------------------------------|
| `Default`             | Balanced readability: 4-space indent, upper-case keywords, next-line braces |
| `Default`              | Default formatting (4-space indent, uppercase keywords, aligned declarations) |
| `CompactPreset`       | Minimal vertical spacing, reduced indentation                        |
| `ExpandedPreset`      | Generous vertical spacing, expanded layout                          |

#### EditorConfigParser

Reads `.editorconfig` files and maps relevant properties to `FormattingConfiguration` values:

```
 .editorconfig
    |
    v
 EditorConfigParser.Parse(filePath)
    |
    +-- indent_style             -> IndentStyle
    +-- indent_size              -> IndentSize
    +-- end_of_line               -> NewLineStyle
    +-- max_line_length           -> MaxLineLength
    +-- st_formatter_*           -> Custom properties (prefix)
    v
 FormattingConfiguration
```

Custom properties use the `st_formatter_` prefix to avoid collisions, e.g.:

```ini
st_formatter_keyword_casing = upper
st_formatter_brace_style = next_line
st_formatter_space_around_operators = true
st_formatter_empty_lines_between_pous = 2
```

### 4.6 Text Infrastructure / Textinfrastruktur

**Files**: `Text/SourceText.cs`, `Text/TextSpan.cs`

#### SourceText

An immutable text container with line tracking. Provides:

- `SourceText.From(string)` — create from a string
- `Lines` — indexed line collection with start positions and line lengths
- `GetLinePosition(int position)` — convert absolute offset to `(line, column)`
- `GetTextSpan(TextSpan)` — extract substring for a span

#### TextSpan

A value type representing a contiguous range of characters:

```
 TextSpan { Start: int, Length: int }
```

Used throughout the pipeline to annotate tokens and nodes with their source positions, enabling accurate diagnostics, round-trip mapping, and incremental scenarios.

---

## 5. STFormatter.CLI — Command Line Interface / Kommandozeile

**Target**: net8.0
**Entry point**: `Program.cs`

### Commands / Befehle

| Command   | Description / Beschreibung                                        |
|-----------|-------------------------------------------------------------------|
| `format`  | Format one or more ST files in place                              |
| `check`   | Check files for formatting differences (dry run, exit code)       |
| `batch`   | Format all ST files in a directory tree                           |
| `init`    | Create an `.editorconfig` with default ST formatter settings     |
| `preset`  | List or apply a preset configuration                              |
| `export`  | Export current configuration as `.editorconfig`                  |
| `import`  | Import configuration from an `.editorconfig` file                |

### Configuration Resolution / Konfigurationsaufloesung

```
 1. Command-line flags (--indent-size, --keyword-casing, ...)
 2. .editorconfig in file directory or ancestors
 3. .editorconfig in home directory
 4. FormattingConfiguration.Default
```

Command-line flags override `.editorconfig`, which overrides built-in defaults.

---

## 6. STFormatter.VSIX — Visual Studio Extension / VS-Erweiterung

**Target**: net48
**Host**: Visual Studio 2022

### Architecture / Architektur

```
 +-----------------------+
 | Visual Studio 2022    |
 |                       |
 |  +-----------------+  |
 |  | IVsTextView     |  |
 |  | ITextBuffer     |  |
 |  +--------+--------+  |
 |           |            |
 |  +--------v--------+  |
 |  | FormatHelper     |  |
 |  |  - Detect format |  |
 |  |  - Parse .TcPOU  |  |
 |  |  - Format body   |  |
 |  |  - Write back    |  |
 |  +--------+--------+  |
 |           |            |
 |  +--------v--------+  |
 |  | STFormatter.Core|  |
 |  +-----------------+  |
 |                       |
 |  +-----------------+  |
 |  | FormatOnSave     |  |
 |  |  Helper          |  |
 |  +-----------------+  |
 |                       |
 |  +-----------------+  |
 |  | Options Page     |  |
 |  |  (Tools->Options)| |
 |  +-----------------+  |
 |                       |
 |  +-----------------+  |
 |  | FormatCommands   |  |
 |  |  Ctrl+K,D        |  |
 |  |  Ctrl+K,F        |  |
 |  +-----------------+  |
 +-----------------------+
```

### Key Components / Wichtige Komponenten

| Component / Komponente  | File                       | Responsibility / Verantwortlichkeit                              |
|--------------------------|----------------------------|------------------------------------------------------------------|
| `FormatHelper`           | `FormatHelper.cs`         | Detect file type, read `.TcPOU` XML or `.st` plain text, format, write back |
| `FormatOnSaveHelper`    | `FormatOnSaveHelper.cs`   | Intercept save event, invoke `FormatHelper` if `FormatOnSave` is enabled |
| `STFormatterOptionPage` | `STFormatterOptionPage.cs` | Tools -> Options dialog page mirroring `FormattingConfiguration`   |
| `FormatCommands`        | `FormatCommands.cs`       | Command handlers for Ctrl+K,D (Format Document) and Ctrl+K,F (Format Selection) |

### .TcPOU File Handling / .TcPOU-Dateiverarbeitung

TwinCAT stores ST source in `.TcPOU` XML files:

```xml
<TcPlcObject>
  <POU Name="Main">
    <Declaration><![CDATA[ ... VAR declarations ... ]]></Declaration>
    <Implementation>
      <ST><![CDATA[ ... ST body ... ]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>
```

`FormatHelper` extracts the `CDATA` sections, formats them individually with `FormatBody()`, and replaces the original content while preserving the XML envelope unchanged. This ensures TwinCAT project file integrity.

---

## 7. STFormatter.TcXaeShell — TwinCAT Extension / TcXaeShell-Erweiterung

**Target**: net462 or net48, x86 (32-bit)
**Host**: Beckhoff TcXaeShell (TwinCAT engineering environment)

The TcXaeShell extension mirrors the VSIX architecture but targets the TwinCAT-specific IDE. It is compiled as x86 because TcXaeShell runs as a 32-bit process. The target framework depends on the TcXaeShell version: net48 for current TcXaeShell 15.0 (VS 2017 shell), net462 for older TcXaeShell 14.0/12.0 (VS 2015/2013 shells).

Key differences from the VSIX:

- References `STFormatter.Core` as `net462` or `net48` (depending on TcXaeShell version)
- Runs in a 32-bit host process (all TcXaeShell versions are x86)
- Uses TcXaeShell-specific VS SDK APIs where they differ from Visual Studio
- Shares the same `FormatHelper`, `FormatOnSaveHelper`, and `STFormatterOptionPage` logic adapted for the TcXaeShell environment

---

## 8. Multi-Targeting Strategy / Multi-Targeting-Strategie

`STFormatter.Core` compiles against three target frameworks to serve all consumers from a single codebase:

```
 STFormatter.Core
 +-- net8.0   ------> STFormatter.CLI
 +-- net48    ------> STFormatter.VSIX
  +-- net462   ------> STFormatter.TcXaeShell x86 (older TcXaeShell 14.0/12.0)
  +-- net48    ------> STFormatter.TcXaeShell x86 (current TcXaeShell 15.0)
```

### How It Works / Funktionsweise

The Core `.csproj` uses `<TargetFrameworks>net8.0;net48;net462</TargetFrameworks>`. When built, MSBuild produces three output assemblies — one per target framework — from the same source files.

Conditional compilation is used where APIs differ:

```
#if NET8_0
    // .NET 8-specific code (e.g., System.Text.Json)
#else
    // .NET Framework fallback (e.g., Newtonsoft.Json)
#endif
```

Each consumer project references Core with a `ProjectReference` and MSBuild resolves the correct target framework automatically:

```xml
<!-- STFormatter.CLI.csproj -->
<ProjectReference Include="..\STFormatter.Core\STFormatter.Core.csproj" />

<!-- STFormatter.VSIX.csproj (implicitly picks net48) -->
<ProjectReference Include="..\STFormatter.Core\STFormatter.Core.csproj" />
```

### Rationale / Begruendung

- **Single source of truth**: All formatting logic lives in one project. Bug fixes and new features propagate to every consumer automatically.
- **API compatibility**: net462 supports older TcXaeShell (x86, VS 2015/2013 shell), net48 supports current TcXaeShell and VSIX (VS 2017 shell / VS 2022), net8.0 supports CLI (cross-platform).
- **No duplicate code**: No copy-paste between VSIX and TcXaeShell; both reference the same engine.

---

## 9. Configuration Flow / Konfigurationsablauf

Configuration is resolved by cascading from the most specific source to the least specific:

```
 +-------------------+
 | Command-line args  |  (CLI only)
 | / VS Options page  |  (VSIX/TcXaeShell only)
 +--------+----------+
          |
          v
 +-------------------+
 | .editorconfig      |  (nearest to file, walking up to root)
 +--------+----------+
          |
          v
 +-------------------+
 | FormattingConfiguration.Default
 +--------+----------+
          |
          v
 +-------------------+
 | FormattingEngine   |
 +-------------------+
```

### CLI Configuration Resolution / CLI-Konfigurationsaufloesung

```
 stformatter format myfile.st --indent-size=2 --keyword-casing=lower
              |                         |
              |                         v
              |            Override .editorconfig values
              v
    Read .editorconfig from myfile.st directory upward
              |
              v
    Merge: CLI args > .editorconfig > Default
              |
              v
    FormattingEngine.Format(source, mergedConfig)
```

### VSIX / TcXaeShell Configuration Resolution / VS/TcXaeShell-Konfigurationsaufloesung

```
 Tools -> Options -> ST Formatter
              |
              v
 STFormatterOptionPage saves to VS settings store
              |
              v
 FormatHelper reads:
   1. .editorconfig (file directory -> solution root -> home)
   2. VS Options page values (override .editorconfig)
   3. FormattingConfiguration.Default (fallback)
              |
              v
 Merge: Options page > .editorconfig > Default
              |
              v
 FormattingEngine.Format(source, mergedConfig)
```

---

## 10. Design Decisions / Entwurfsentscheidungen

### Hand-Written Lexer and Parser / Handgeschriebener Lexer und Parser

The lexer and parser are hand-written rather than generated. This decision provides:

- **Full control over trivia handling** — comments and whitespace are first-class citizens in a formatter, not noise to discard.
- **Error recovery** — the parser can skip bad tokens and continue, enabling partial formatting of syntactically invalid code.
- **No build-time dependency on generator tools** — simpler build and CI pipeline.
- **Performance** — hand-written lexers are typically faster than generated ones for domain-specific grammars.

### Immutable Syntax Tree / Unveraenderlicher Syntaxbaum

All syntax tree nodes are immutable after construction. Benefits:

- **Thread safety** — the tree can be shared across threads (e.g., formatting on a background thread in VS).
- **Snapshot semantics** — diffing old and new trees is straightforward.
- **No defensive copies** — no need to clone nodes when passing them around.

### FormattingVisitor Pattern / Formatierungsvisitor-Muster

The `FormattingVisitor` decouples tree traversal from output generation. Adding a new formatting rule requires only a visitor change, not a parser or tree change. This keeps the formatting logic localised and testable.

### FormatBody with Wrapper Stripping / FormatBody mit Wrapper-Entfernung

`FormatBody(string body)` wraps the raw body text in a minimal `PROGRAM __BODY_WRAPPER__` ... `END_PROGRAM` envelope, formats it as a complete program, then strips the wrapper lines and removes the common indent. This approach:

- Reuses the full pipeline for partial content (function bodies, VAR sections).
- Avoids a separate "body-only" parser.
- Guarantees consistent indentation handling.

```
 Input: "a := 1;\nb := 2;"
          |
          v
 Wrap: "PROGRAM __BODY_WRAPPER__\na := 1;\nb := 2;\nEND_PROGRAM"
          |
          v
 Format with full pipeline
          |
          v
 Strip wrapper lines and common indent
          |
          v
 Output: "a := 1;\n    b := 2;"  (properly indented)
```

### .TcPOU XML Preservation / .TcPOU-XML-Erhaltung

The VSIX and TcXaeShell extensions parse `.TcPOU` files as XML, extract `CDATA` sections, format only the ST content within, and replace it without modifying any surrounding XML structure. This preserves:

- TwinCAT project metadata (POU names, interface declarations)
- XML comments and processing instructions
- File encoding and line endings outside CDATA sections
- TwinCAT version compatibility markers

---

*End of Architecture Documentation / Ende der Architekturdokumentation*