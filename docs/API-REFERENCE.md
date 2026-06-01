# API Reference

**STFormatter.Core** — multi-targets `net8.0`, `net48`, `net462`

Namespace: `STFormatter.Core`

---

## Table of Contents

- [FormattingEngine](#formattingengine)
- [FormattingConfiguration](#formattingconfiguration)
- [SourceText](#sourcetext)
- [TextSpan](#textspan)
- [SyntaxTree](#syntaxtree)
- [SyntaxNode, SyntaxToken, SyntaxTrivia](#syntaxnode-syntaxtoken-syntaxtrivia)
- [Lexer](#lexer)
- [Parser](#parser)

---

## FormattingEngine

_Namespace: `STFormatter.Core.Formatting`_

The primary entry point for formatting ST source code. Accepts raw source text, a parsed `SyntaxTree`, or a standalone body, and returns formatted output.

```csharp
public sealed class FormattingEngine
{
    public FormattingEngine(FormattingConfiguration? config = null);

    public string Format(string source);
    public string Format(SyntaxTree tree);
    public string FormatBody(string body);

    public static FormattingConfiguration LoadConfiguration(string filePath);
}
```

### FormattingEngine(FormattingConfiguration?)

Creates a new engine instance. When `config` is `null`, `FormattingConfiguration.Default` is used.

```csharp
using STFormatter.Core.Formatting;

var engine = new FormattingEngine();

var customConfig = new FormattingConfiguration
{
    IndentStyle = "tabs",
    KeywordCasing = "lower"
};
var customEngine = new FormattingEngine(customConfig);
```

### Format(string source)

Formats a complete ST document. If the source does not begin with a POU keyword (`PROGRAM`, `FUNCTION`, `FUNCTION_BLOCK`, `ACTION`, `METHOD`), the engine wraps it in a temporary `PROGRAM __BODY_WRAPPER__` / `END_PROGRAM`, formats, then strips the wrapper and common indent.

```csharp
var engine = new FormattingEngine();

string source = """
PROGRAM Main
VAR
x : INT;
END_VAR
x:=x+1;
END_PROGRAM
""";

string formatted = engine.Format(source);
// PROGRAM Main
// VAR
//     x : INT;
// END_VAR
//     x := x + 1;
// END_PROGRAM
```

### Format(SyntaxTree tree)

Formats from an already-parsed syntax tree. Useful when the same tree must be inspected and then formatted, avoiding a redundant parse step.

```csharp
using STFormatter.Core.Syntax;
using STFormatter.Core.Formatting;

var engine = new FormattingEngine();
SyntaxTree tree = SyntaxTree.Parse(source);

// Inspect the tree before formatting...
var root = tree.Root;

string formatted = engine.Format(tree);
```

### FormatBody(string body)

Formats a standalone body without requiring `PROGRAM`/`END_PROGRAM` wrappers. The engine internally wraps the body, formats it, then strips the wrapper and removes any common leading indent from every line.

```csharp
var engine = new FormattingEngine();

string body = """
IF x > 0 THEN
y := x * 2;
END_IF
""";

string formattedBody = engine.FormatBody(body);
// IF x > 0 THEN
//     y := x * 2;
// END_IF
```

### LoadConfiguration(string filePath)

Static method. Reads an `.editorconfig` file and produces a `FormattingConfiguration` by mapping standard EditorConfig properties (plus ST-specific conventions) to formatter settings.

```csharp
FormattingConfiguration config = FormattingEngine.LoadConfiguration(
    @"C:\Projects\TwinCAT\Plc\.editorconfig"
);

var engine = new FormattingEngine(config);
```

---

## FormattingConfiguration

_Namespace: `STFormatter.Core.Formatting`_

Immutable options object controlling every aspect of formatting. Each setting has a sensible default. Instances are created via the default constructor, a preset, or by loading an `.editorconfig` file.

```csharp
public sealed class FormattingConfiguration
{
    public static FormattingConfiguration Default { get; }
    public static FormattingConfiguration CompactPreset { get; }
    public static FormattingConfiguration ExpandedPreset { get; }

    // Properties
    public string IndentStyle { get; set; }           // "spaces" or "tabs"
    public int IndentSize { get; set; }                // default: 4
    public int ContinuationIndentSize { get; set; }   // default: 8
    public string NewLineStyle { get; set; }           // "crlf", "lf", "cr"
    public string KeywordCasing { get; set; }         // "upper", "lower", "pascal", "original"
    public string BraceStyle { get; set; }            // "allman" or "kandr"
    public bool SpaceAroundOperators { get; set; }    // default: true
    public bool SpaceAfterComma { get; set; }          // default: true
    public bool SpaceBeforeSemicolon { get; set; }     // default: false
    public bool SpaceAfterColon { get; set; }          // default: true
    public bool AlignAssignments { get; set; }          // default: true
    public bool AlignVariableDeclarations { get; set; } // default: true
    public int MaxLineLength { get; set; }               // default: 120
    public int EmptyLinesBetweenPOUs { get; set; }       // default: 2
    public int EmptyLinesBetweenVarSections { get; set; } // default: 1
    public bool KeepSingleLineBlocks { get; set; }        // default: false
    public bool FormatOnSave { get; set; }                // default: true

    // Methods
    public string GetNewLine();
    public string GetIndentString(int level);
    public string FormatKeyword(string keyword);
    public static FormattingConfiguration FromPreset(string presetName);
}
```

### Presets

Four built-in preset configurations are available:

| Preset | Description |
|---|---|
| `Default` | Balanced defaults: 4-space indent, upper-case keywords, Allman braces, alignment enabled. |
| `CompactPreset` | Minimal whitespace: 2-space indent, K&R braces, no alignment, single blank line between POU declarations. |
| `ExpandedPreset` | Generous spacing: 4-space indent, Allman braces, alignment enabled, 2 blank lines between var sections. |

```csharp
// Use a preset directly
var config = FormattingConfiguration.CompactPreset;
var engine = new FormattingEngine(config);

// Or load a preset by name (case-insensitive)
config = FormattingConfiguration.FromPreset("default");
config = FormattingConfiguration.FromPreset("compact");
config = FormattingConfiguration.FromPreset("expanded");

// "default" returns Default
config = FormattingConfiguration.FromPreset("default");
```

### Property Reference

#### IndentStyle

Controls whether indentation uses spaces or tabs.

```csharp
var config = new FormattingConfiguration { IndentStyle = "spaces" }; // default
// or
var config = new FormattingConfiguration { IndentStyle = "tabs" };
```

#### IndentSize

Number of columns per indentation level. Default: `4`.

```csharp
var config = new FormattingConfiguration { IndentSize = 2 }; // compact
```

#### ContinuationIndentSize

Indentation for wrapped or continued lines (e.g., multi-line function call arguments). Default: `8`.

```csharp
var config = new FormattingConfiguration { ContinuationIndentSize = 4 };
```

#### NewLineStyle

Line ending style for output.

| Value | Platform |
|---|---|
| `"crlf"` | Windows (`\r\n`) |
| `"lf"` | Unix/Linux (`\n`) |
| `"cr"` | Legacy Mac (`\r`) |

```csharp
var config = new FormattingConfiguration { NewLineStyle = "crlf" }; // Windows default
```

#### KeywordCasing

Controls capitalisation of IEC 61131-3 keywords in the formatted output.

| Value | Effect |
|---|---|
| `"upper"` | `PROGRAM`, `END_VAR`, `IF` |
| `"lower"` | `program`, `end_var`, `if` |
| `"pascal"` | `Program`, `EndVar`, `If` |
| `"original"` | Preserves source casing |

```csharp
var config = new FormattingConfiguration { KeywordCasing = "upper" }; // default
```

#### BraceStyle

Controls placement of structural braces. `"allman"` places opening braces on a new line; `"kandr"` places them on the same line as the preceding keyword.

```csharp
// Allman (default)
// IF x > 0 THEN
//     ...
// END_IF

// K&R
// IF x > 0 THEN
//     ...
// END_IF
// (Note: ST uses keywords rather than braces, but BraceStyle
//  controls analogous block formatting in extended ST dialects.)
var config = new FormattingConfiguration { BraceStyle = "allman" };
```

#### SpaceAroundOperators

When `true`, inserts spaces around binary operators (`:=`, `+`, `-`, `*`, `=`, `<>`, etc.). Default: `true`.

```csharp
// SpaceAroundOperators = true  (default)
x := a + b * c;

// SpaceAroundOperators = false
x:=a+b*c;
```

#### SpaceAfterComma

When `true`, inserts a space after each comma in argument lists and variable declarations. Default: `true`.

```csharp
// SpaceAfterComma = true  (default)
MyFunc(a, b, c);

// SpaceAfterComma = false
MyFunc(a,b,c);
```

#### SpaceBeforeSemicolon

When `true`, inserts a space before semicolons. Default: `false`.

```csharp
// SpaceBeforeSemicolon = false  (default)
x := 1;

// SpaceBeforeSemicolon = true
x := 1 ;
```

#### SpaceAfterColon

When `true`, inserts a space after colons in declarations. Default: `true`.

```csharp
// SpaceAfterColon = true  (default)
x : INT;

// SpaceAfterColon = false
x :INT;
```

#### AlignAssignments

When `true`, aligns the `:=` operator across consecutive assignment statements. Default: `true`.

```csharp
// AlignAssignments = true  (default)
counter   := counter + 1;
isActive  := TRUE;
result    := Calculate(counter);

// AlignAssignments = false
counter := counter + 1;
isActive := TRUE;
result := Calculate(counter);
```

#### AlignVariableDeclarations

When `true`, aligns variable declaration columns (name, type, initialiser) in `VAR` sections. Default: `true`.

```csharp
// AlignVariableDeclarations = true  (default)
VAR
    counter    : INT  := 0;
    isActive   : BOOL := TRUE;
    result     : DINT;
END_VAR

// AlignVariableDeclarations = false
VAR
    counter : INT := 0;
    isActive : BOOL := TRUE;
    result : DINT;
END_VAR
```

#### MaxLineLength

Maximum line length before the formatter attempts to break lines. Default: `120`.

#### EmptyLinesBetweenPOUs

Number of blank lines inserted between consecutive POU declarations. Default: `2`.

#### EmptyLinesBetweenVarSections

Number of blank lines between `VAR` sections within a POU. Default: `1`.

#### KeepSingleLineBlocks

When `true`, keeps short `IF`/`FOR`/`WHILE` blocks on a single line if they were written that way. Default: `false`.

#### FormatOnSave

Hint for editor integrations. When `true`, integrations may run the formatter automatically on save. Default: `true`.

### Methods

#### GetNewLine()

Returns the newline string corresponding to `NewLineStyle`.

```csharp
var config = new FormattingConfiguration { NewLineStyle = "crlf" };
string nl = config.GetNewLine(); // "\r\n"

config = new FormattingConfiguration { NewLineStyle = "lf" };
nl = config.GetNewLine(); // "\n"
```

#### GetIndentString(int level)

Returns the indentation string for the given level (e.g., `"    "` for level 1 with 4-space indent).

```csharp
var config = new FormattingConfiguration { IndentStyle = "spaces", IndentSize = 4 };
string indent1 = config.GetIndentString(1); // "    " (4 spaces)
string indent2 = config.GetIndentString(2); // "        " (8 spaces)

config = new FormattingConfiguration { IndentStyle = "tabs", IndentSize = 4 };
string tab1 = config.GetIndentString(1); // "\t"
```

#### FormatKeyword(string keyword)

Applies the `KeywordCasing` rule to the given keyword and returns the result.

```csharp
var config = new FormattingConfiguration { KeywordCasing = "upper" };
config.FormatKeyword("program");  // "PROGRAM"

config = new FormattingConfiguration { KeywordCasing = "pascal" };
config.FormatKeyword("program");  // "Program"

config = new FormattingConfiguration { KeywordCasing = "original" };
config.FormatKeyword("Program");  // "Program" (unchanged)
```

#### FromPreset(string presetName)

Static factory. Returns a preset configuration by name. Case-insensitive. Recognised names: `"default"`, `"compact"`, `"expanded"`.

```csharp
FormattingConfiguration config = FormattingConfiguration.FromPreset("compact");
```

---

## SourceText

_Namespace: `STFormatter.Core.Text`_

Immutable, line-tracked representation of source text. Used as the input to the lexer and parser.

```csharp
public sealed class SourceText
{
    public static SourceText From(string text);
    public int Length { get; }
    public int LineCount { get; }
    public string GetText();
    public TextLine GetLine(int lineNumber);
}
```

### From(string text)

Creates a `SourceText` from a string. The text is parsed into lines on construction.

```csharp
using STFormatter.Core.Text;

SourceText source = SourceText.From(
    "PROGRAM Main\n" +
    "VAR\n" +
    "    x : INT;\n" +
    "END_VAR\n" +
    "END_PROGRAM"
);

Console.WriteLine(source.Length);    // 49
Console.WriteLine(source.LineCount); // 5
Console.WriteLine(source.GetText()); // full original text
```

### Length

Total character count of the source text.

### LineCount

Number of lines in the source text.

### GetText()

Returns the full original source text as a string.

### GetLine(int lineNumber)

Returns a `TextLine` for the given 0-based line number.

```csharp
TextLine line = source.GetLine(0);
// line.Text    -> "PROGRAM Main"
// line.Start   -> 0
// line.Length  -> 12
```

> **Note**: The `TextLine` type exposes `Text` (string), `Start` (int), and `Length` (int) properties describing one line of the source.

---

## TextSpan

_Namespace: `STFormatter.Core.Text`_

A value type representing a contiguous range of characters within a `SourceText`. Used throughout the syntax API to denote token positions and node spans.

```csharp
public readonly struct TextSpan
{
    public int Start { get; }
    public int Length { get; }
    public int End { get; }
    public bool IsEmpty { get; }

    public bool Contains(int position);
    public bool OverlapsWith(TextSpan other);
}
```

### Properties

| Property | Type | Description |
|---|---|---|
| `Start` | `int` | Inclusive start position (0-based). |
| `Length` | `int` | Number of characters in the span. |
| `End` | `int` | Exclusive end position (`Start + Length`). |
| `IsEmpty` | `bool` | `true` when `Length == 0`. |

### Contains(int position)

Returns `true` if `position` falls within `[Start, End)`.

```csharp
var span = new TextSpan { Start = 10, Length = 5 }; // covers 10..14

span.Contains(10); // true
span.Contains(14); // true
span.Contains(15); // false
```

### OverlapsWith(TextSpan other)

Returns `true` if this span and `other` share at least one character position.

```csharp
var a = new TextSpan { Start = 0, Length = 10 };
var b = new TextSpan { Start = 5, Length = 10 };

a.OverlapsWith(b); // true (overlap at 5..9)
```

---

## SyntaxTree

_Namespace: `STFormatter.Core.Syntax`_

Immutable, full-fidelity representation of a parsed ST program. Every character of the source (including whitespace and comments) is represented in the tree.

```csharp
public sealed class SyntaxTree
{
    public SyntaxNode Root { get; }
    public static SyntaxTree Parse(string source);
}
```

### Root

The root `SyntaxNode` of the tree (typically a `ProgramNode` or a `CompilationUnitNode`, depending on the input).

### Parse(string source)

Parses ST source text into a `SyntaxTree`. Recoverable syntax errors produce missing tokens in the tree; the parse completes even on invalid input.

```csharp
using STFormatter.Core.Syntax;

SyntaxTree tree = SyntaxTree.Parse("""
    PROGRAM Main
    VAR
        x : INT := 0;
    END_VAR
    x := x + 1;
    END_PROGRAM
    """);

SyntaxNode root = tree.Root;
Console.WriteLine(root.Kind); // Program
```

Combining with formatting:

```csharp
using STFormatter.Core.Syntax;
using STFormatter.Core.Formatting;

SyntaxTree tree = SyntaxTree.Parse(source);

// Inspect the tree, query nodes, etc.
// ...

var engine = new FormattingEngine();
string formatted = engine.Format(tree);
```

---

## SyntaxNode, SyntaxToken, SyntaxTrivia

_Namespace: `STFormatter.Core.Syntax`_

The syntax object model consists of three fundamental types that together form the tree.

### SyntaxNode

Abstract base class for all non-terminal nodes in the syntax tree. Each node exposes its children, span, and kind.

```csharp
public abstract class SyntaxNode
{
    public SyntaxKind Kind { get; }
    public TextSpan Span { get; }
    public IEnumerable<SyntaxNode> ChildNodes { get; }
    public IEnumerable<SyntaxToken> ChildTokens { get; }
    public IEnumerable<SyntaxNode> DescendantNodes();
    public IEnumerable<SyntaxToken> DescendantTokens();
    public string ToFullString();    // includes trivia
    public string ToString();         // excludes trivia
}
```

**Node hierarchy**:

```
SyntaxNode (abstract)
    +-- ProgramNode
    |     +-- Usings
    |     +-- Declarations
    |     +-- EndKeyword
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
    +-- ExpressionNode (abstract)
    |     +-- BinaryExpressionNode
    |     +-- UnaryExpressionNode
    |     +-- LiteralExpressionNode
    |     +-- IdentifierExpressionNode
    |     +-- ParenthesizedExpressionNode
    |     +-- CallExpressionNode
    |     +-- ArrayAccessExpressionNode
    |     +-- MemberAccessExpressionNode
    +-- UsingDirectiveNode
    +-- PragmaNode
```

### SyntaxToken

Represents a terminal symbol in the syntax tree (keywords, identifiers, literals, operators, punctuation). Each token may carry leading and trailing trivia.

```csharp
public sealed class SyntaxToken
{
    public SyntaxKind Kind { get; }
    public string Text { get; }
    public int Position { get; }
    public TextSpan Span { get; }
    public SyntaxTrivia[] LeadingTrivia { get; }
    public SyntaxTrivia[] TrailingTrivia { get; }
    public bool IsMissing { get; }    // true when inserted by error recovery
}
```

```csharp
SyntaxTree tree = SyntaxTree.Parse("PROGRAM Main END_PROGRAM");
SyntaxToken firstToken = tree.Root.ChildTokens().First();

Console.WriteLine(firstToken.Kind);     // ProgramKeyword
Console.WriteLine(firstToken.Text);    // "PROGRAM"
Console.WriteLine(firstToken.Position); // 0
```

### SyntaxTrivia

Represents non-essential syntax: whitespace, line comments (`//`), and block comments (`(* ... *)`). Trivia is attached to tokens and is never orphaned.

```csharp
public sealed class SyntaxTrivia
{
    public SyntaxKind Kind { get; }
    public string Text { get; }
}
```

**Trivia kinds**:

| SyntaxKind | Description |
|---|---|
| `WhitespaceTrivia` | Spaces and tabs |
| `EndOfLineTrivia` | Newline characters |
| `SingleLineCommentTrivia` | `// comment` |
| `MultiLineCommentTrivia` | `(* comment *)` |

### SyntaxKind

Enum of all syntax element kinds in the grammar. Covers node kinds, token kinds, and trivia kinds.

```csharp
// Node kinds
SyntaxKind.Program
SyntaxKind.FunctionBlock
SyntaxKind.Function
SyntaxKind.VarSection
SyntaxKind.AssignmentStatement
SyntaxKind.IfStatement
SyntaxKind.CaseStatement
// ...

// Token kinds
SyntaxKind.ProgramKeyword
SyntaxKind.EndProgramKeyword
SyntaxKind.IfKeyword
SyntaxKind.ElseKeyword
SyntaxKind.Identifier
SyntaxKind.NumericLiteral
SyntaxKind.StringLiteral
SyntaxKind.PlusToken
SyntaxKind.MinusToken
SyntaxKind.EqualsToken
// ...

// Trivia kinds
SyntaxKind.WhitespaceTrivia
SyntaxKind.EndOfLineTrivia
SyntaxKind.SingleLineCommentTrivia
SyntaxKind.MultiLineCommentTrivia
```

---

## Lexer

_Namespace: `STFormatter.Core.Lexing`_

Hand-written lexer that tokenizes `SourceText` into a stream of `SyntaxToken` objects with attached `SyntaxTrivia`.

```csharp
public sealed class Lexer
{
    public Lexer(SourceText text);
    public TokenList Tokenize();
}
```

### Lexer(SourceText text)

Creates a lexer for the given source text. Does not perform tokenisation until `Tokenize()` is called.

### Tokenize()

Scans the entire source text and returns a `TokenList` (an immutable, indexed collection of all tokens including end-of-file token).

```csharp
using STFormatter.Core.Text;
using STFormatter.Core.Lexing;

SourceText source = SourceText.From("x := 42; (* assign *)");
var lexer = new Lexer(source);
TokenList tokens = lexer.Tokenize();

foreach (SyntaxToken token in tokens)
{
    Console.WriteLine($"{token.Kind,25} | {token.Text}");
}
//                Identifier | x
//           EqualsToken | :=
//            NumericLiteral | 42
//           SemicolonToken | ;
//          MultiLineCommentTrivia | (* assign *)
//              EndOfFileToken |
```

> **Note**: `TokenList` provides indexed access (`tokens[index]`) and enumeration. The final token is always `EndOfFileToken`.

---

## Parser

_Namespace: `STFormatter.Core.Parsing`_

Recursive descent parser that consumes a token stream and produces an immutable `SyntaxTree`. Supports error recovery: on syntax errors, missing tokens are inserted so formatting can proceed on partial programs.

```csharp
public sealed class Parser
{
    public Parser(SourceText text);
    public SyntaxTree Parse();
}
```

### Parser(SourceText text)

Creates a parser for the given source text. Internally constructs a `Lexer` for tokenisation.

### Parse()

Parses the source text and returns a `SyntaxTree`. The tree is immutable and full-fidelity: every character in the source (including whitespace and comments) is represented.

```csharp
using STFormatter.Core.Text;
using STFormatter.Core.Parsing;

string source = """
    PROGRAM Counter
    VAR
        count : INT := 0;
    END_VAR
    count := count + 1;
    END_PROGRAM
    """;

SourceText text = SourceText.From(source);
var parser = new Parser(text);
SyntaxTree tree = parser.Parse();

// Access the root node for inspection
SyntaxNode root = tree.Root;

// Or use the shortcut:
SyntaxTree tree2 = SyntaxTree.Parse(source);
// SyntaxTree.Parse is equivalent to:
// new Parser(SourceText.From(source)).Parse()
```

---

## Common Patterns

### Basic Formatting

```csharp
using STFormatter.Core.Formatting;

var engine = new FormattingEngine();
string formatted = engine.Format(rawSource);
```

### Custom Configuration

```csharp
using STFormatter.Core.Formatting;

var config = new FormattingConfiguration
{
    IndentStyle = "spaces",
    IndentSize = 3,
    NewLineStyle = "crlf",
    KeywordCasing = "upper",
    SpaceAroundOperators = true,
    AlignAssignments = true,
    MaxLineLength = 100,
    EmptyLinesBetweenPOUs = 1
};

var engine = new FormattingEngine(config);
string formatted = engine.Format(rawSource);
```

### Format from .editorconfig

```csharp
using STFormatter.Core.Formatting;

FormattingConfiguration config = FormattingEngine.LoadConfiguration(".editorconfig");
var engine = new FormattingEngine(config);
string formatted = engine.Format(rawSource);
```

### Inspect then Format

```csharp
using STFormatter.Core.Syntax;
using STFormatter.Core.Formatting;

SyntaxTree tree = SyntaxTree.Parse(rawSource);

// Walk the tree
foreach (SyntaxNode node in tree.Root.DescendantNodes())
{
    Console.WriteLine($"{node.Kind} at {node.Span.Start}..{node.Span.End}");
}

// Format from the parsed tree (avoids re-parsing)
var engine = new FormattingEngine();
string formatted = engine.Format(tree);
```

### Low-level Pipeline

```csharp
using STFormatter.Core.Text;
using STFormatter.Core.Lexing;
using STFormatter.Core.Parsing;
using STFormatter.Core.Formatting;

SourceText text = SourceText.From(rawSource);
var lexer = new Lexer(text);
TokenList tokens = lexer.Tokenize();

var parser = new Parser(text);
SyntaxTree tree = parser.Parse();

var engine = new FormattingEngine();
string formatted = engine.Format(tree);
```

---

## Multi-Targeting Compatibility

`STFormatter.Core` targets `net8.0`, `net48`, and `net462`. All public APIs are identical across targets. The library uses only BCL types available on all three runtimes. No conditional API surface exists; the same source compiles for all three targets.