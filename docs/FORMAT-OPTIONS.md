# Formatting Options Reference / Formatierungsoptionen-Referenz

Complete reference for all configuration properties of `FormattingConfiguration`, with before/after examples, preset definitions, and integration guides.

Vollstaendige Referenz aller Konfigurationseigenschaften von `FormattingConfiguration` mit Vorher/Nachher-Beispielen, Preset-Definitionen und Integrationsleitfaeden.

---

## Table of Contents / Inhaltsverzeichnis

1. [Indentation / Einrueckung](#1-indentation--einrueckung)
2. [Line Endings / Zeilenumbrueche](#2-line-endings--zeilenumbrueche)
3. [Keyword Casing / Schluesselwort-Großschreibung](#3-keyword-casing--schluesselwort-grossschreibung)
4. [Brace Style / Klammernstil](#4-brace-style--klammernstil)
5. [Spacing / Leerzeichen](#5-spacing--leerzeichen)
6. [Alignment / Ausrichtung](#6-alignment--ausrichtung)
7. [Line Wrapping / Zeilenumbruch](#7-line-wrapping--zeilenumbruch)
8. [Empty Lines / Leerzeilen](#8-empty-lines--leerzeilen)
9. [Single-Line Blocks / Einzeilige Bloecke](#9-single-line-blocks--einzeilige-bloecke)
10. [Format on Save / Bei Speichern formatieren](#10-format-on-save--bei-speichern-formatieren)
11. [Presets / Vorlagen](#11-presets--vorlagen)
12. [EditorConfig Integration / EditorConfig-Integration](#12-editorconfig-integration--editorconfig-integration)
13. [CLI Configuration / CLI-Konfiguration](#13-cli-configuration--cli-konfiguration)
14. [Host Settings / Host-Einstellungen](#14-host-settings--host-einstellungen)

---

## 1. Indentation / Einrueckung

### IndentStyle

| Property | Value |
|----------|-------|
| Type     | `string` |
| Default  | `"spaces"` |
| Values   | `"spaces"` or `"tabs"` |

Controls whether indentation uses space characters or tab characters.

Bestimmt, ob Leerzeichen oder Tabulatoren fuer die Einrueckung verwendet werden.

**Before** (mixed indentation):

```st
PROGRAM Example
	VAR
		x : INT;
		y : INT;
	END_VAR
	IF x > 0 THEN
		y := x * 2;
	END_IF
END_PROGRAM
```

**After** with `IndentStyle = "spaces"` (default):

```st
PROGRAM Example
    VAR
        x : INT;
        y : INT;
    END_VAR
    IF x > 0 THEN
        y := x * 2;
    END_IF
END_PROGRAM
```

**After** with `IndentStyle = "tabs"` (shown as 4-space display):

```st
PROGRAM Example
	VAR
		x : INT;
		y : INT;
	END_VAR
	IF x > 0 THEN
		y := x * 2;
	END_IF
END_PROGRAM
```

### IndentSize

| Property | Value |
|----------|-------|
| Type     | `int` |
| Default  | `4` |

Number of spaces per indentation level when `IndentStyle` is `"spaces"`. Also determines the display width of a tab when `IndentStyle` is `"tabs"`.

Anzahl der Leerzeichen pro Einrueckungsebene bei `IndentStyle = "spaces"`.

**After** with `IndentSize = 2`:

```st
PROGRAM Example
  VAR
    x : INT;
    y : INT;
  END_VAR
  IF x > 0 THEN
    y := x * 2;
  END_IF
END_PROGRAM
```

**After** with `IndentSize = 4` (default):

```st
PROGRAM Example
    VAR
        x : INT;
        y : INT;
    END_VAR
    IF x > 0 THEN
        y := x * 2;
    END_IF
END_PROGRAM
```

### ContinuationIndentSize

| Property | Value |
|----------|-------|
| Type     | `int` |
| Default  | `8` |

Extra indentation applied to lines that are continued after a line break. When a statement exceeds `MaxLineLength` and wraps, the continuation line indents by `ContinuationIndentSize` spaces instead of the normal `IndentSize`. This visually distinguishes wrapped lines from nested blocks.

Zusaetzliche Einrueckung fuer Fortsetzungszeilen, die nach einem Zeilenumbruch folgen.

**Before** (unwrapped):

```st
IF bMotorRunning AND NOT bFaultDetected AND nCurrentSpeed < nMaxSpeed AND nTemperature < nTempLimit THEN
```

**After** with `ContinuationIndentSize = 8` (default, 4 base + 8 continuation = column 12):

```st
IF bMotorRunning AND NOT bFaultDetected
        AND nCurrentSpeed < nMaxSpeed
        AND nTemperature < nTempLimit THEN
    nCurrentSpeed := nCurrentSpeed + 1;
END_IF
```

**After** with `ContinuationIndentSize = 4`:

```st
IF bMotorRunning AND NOT bFaultDetected
    AND nCurrentSpeed < nMaxSpeed
    AND nTemperature < nTempLimit THEN
    nCurrentSpeed := nCurrentSpeed + 1;
END_IF
```

---

## 2. Line Endings / Zeilenumbrueche

### NewLineStyle

| Property | Value |
|----------|-------|
| Type     | `string` |
| Default  | `"crlf"` |
| Values   | `"crlf"`, `"lf"`, `"cr"` |

Controls the line ending sequence used in formatted output. Windows traditionally uses CRLF (`\r\n`), while Unix/Linux uses LF (`\n`). CR (`\r`) is rarely used but supported for legacy systems.

Steuert die Zeilenendesequenz im formatierten Output.

| Value    | Sequence | Platform              |
|----------|----------|-----------------------|
| `"crlf"` | `\r\n`   | Windows (default)     |
| `"lf"`   | `\n`     | Unix / Linux / macOS  |
| `"cr"`   | `\r`     | Legacy (classic Mac)  |

This setting normalises all line endings in the output regardless of the input format. Mixed line endings in the source file will be unified to the configured style.

Diese Einstellung normalisiert alle Zeilenenden im Output unabhaengig vom Eingabeformat.

---

## 3. Keyword Casing / Schluesselwort-Grossschreibung

### KeywordCasing

| Property | Value |
|----------|-------|
| Type     | `string` |
| Default  | `"upper"` |
| Values   | `"upper"`, `"lower"`, `"pascal"`, `"original"` |

Determines the capitalisation of IEC 61131-3 keywords (`IF`, `THEN`, `VAR`, `END_IF`, `PROGRAM`, etc.). Identifiers, variable names, and type names are never changed.

Bestimmt die Gross-/Kleinschreibung von IEC-61131-3-Schluesselwoertern.

| Value       | Example                        |
|-------------|--------------------------------|
| `"upper"`   | `IF ... THEN`, `END_IF`, `VAR` |
| `"lower"`   | `if ... then`, `end_if`, `var` |
| `"pascal"`  | `If ... Then`, `EndIf`, `Var`  |
| `"original"`| preserved from source          |

**Before**:

```st
PROGRAM MotorControl
var
    nSpeed : INT;
end_var
if nSpeed > 100 then
    nSpeed := 100;
END_IF
end_program
```

**After** with `KeywordCasing = "upper"`:

```st
PROGRAM MotorControl
VAR
    nSpeed : INT;
END_VAR
IF nSpeed > 100 THEN
    nSpeed := 100;
END_IF
END_PROGRAM
```

**After** with `KeywordCasing = "lower"`:

```st
program MotorControl
var
    nSpeed : int;
end_var
if nSpeed > 100 then
    nSpeed := 100;
end_if
end_program
```

**After** with `KeywordCasing = "pascal"`:

```st
Program MotorControl
Var
    nSpeed : Int;
EndVar
If nSpeed > 100 Then
    nSpeed := 100;
EndIf
EndProgram
```

**After** with `KeywordCasing = "original"` — keywords retain whatever casing was present in the source, only spacing and structural formatting are applied.

---

## 4. Brace Style / Klammernstil

### BraceStyle

| Property | Value |
|----------|-------|
| Type     | `string` |
| Default  | `"allman"` |
| Values   | `"allman"`, `"kandr"` |

Controls the placement of structural keywords in POU declarations and body blocks. In IEC 61131-3 ST, the equivalent of "braces" are the block delimiters such as `THEN`/`END_IF`, `DO`/`END_FOR`, `OF`/`END_CASE`, etc.

Steuert die Platzierung struktureller Schluesselwoerter.

**Allman style** (default) places the opening keyword on its own line:

```st
IF nErrorCode <> 0
THEN
    HandleError(nErrorCode);
END_IF
```

**K&R style** keeps the opening keyword on the same line as the condition:

```st
IF nErrorCode <> 0 THEN
    HandleError(nErrorCode);
END_IF
```

**Before** (inconsistent):

```st
IF bEnable THEN nResult := Calculate();
END_IF
```

**After** with `BraceStyle = "allman"`:

```st
IF bEnable
THEN
    nResult := Calculate();
END_IF
```

**After** with `BraceStyle = "kandr"`:

```st
IF bEnable THEN
    nResult := Calculate();
END_IF
```

---

## 5. Spacing / Leerzeichen

### SpaceAroundOperators

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Inserts a single space on both sides of operators such as `:=`, `+`, `-`, `*`, `=`, `<>`, `>=`, `<=`, `>`, `<`, `AND`, `OR`, `XOR`, `MOD`.

Fuegt ein Leerzeichen auf beiden Seiten von Operatoren ein.

**Before**:

```st
nResult:=nA+nB*nC;
IF nValue>100 THEN
    bFlag:=TRUE;
END_IF
```

**After** with `SpaceAroundOperators = true`:

```st
nResult := nA + nB * nC;
IF nValue > 100 THEN
    bFlag := TRUE;
END_IF
```

**After** with `SpaceAroundOperators = false`:

```st
nResult:=nA+nB*nC;
IF nValue>100 THEN
    bFlag:=TRUE;
END_IF
```

### SpaceAfterComma

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Inserts a single space after each comma in argument lists, array dimensions, and enumeration values.

Fuegt ein Leerzeichen nach jedem Komma ein.

**Before**:

```st
MoveAxis(1,2,3,nSpeed,nAccel);
aData : ARRAY[1..10] OF INT := [1,2,3,4,5];
```

**After** with `SpaceAfterComma = true`:

```st
MoveAxis(1, 2, 3, nSpeed, nAccel);
aData : ARRAY[1..10] OF INT := [1, 2, 3, 4, 5];
```

**After** with `SpaceAfterComma = false`:

```st
MoveAxis(1,2,3,nSpeed,nAccel);
aData : ARRAY[1..10] OF INT := [1,2,3,4,5];
```

### SpaceBeforeSemicolon

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `false` |

Inserts a space before the semicolon at the end of statements and declarations. This is unusual in standard ST but can improve readability in dense code.

Fuegt ein Leerzeichen vor dem Semikolon am Ende von Anweisungen und Deklarationen ein.

**Before**:

```st
nCounter := nCounter + 1;
nTotal := nA + nB;
```

**After** with `SpaceBeforeSemicolon = true`:

```st
nCounter := nCounter + 1 ;
nTotal := nA + nB ;
```

**After** with `SpaceBeforeSemicolon = false` (default):

```st
nCounter := nCounter + 1;
nTotal := nA + nB;
```

### SpaceAfterColon

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Inserts a space after the colon in variable declarations (the separator between name and type). The colon itself is always emitted; this option only controls the trailing space.

Fuegt ein Leerzeichen nach dem Doppelpunkt in Variablendeklarationen ein.

**Before**:

```st
VAR
    nCounter:INT;
    bEnable:BOOL;
    fTemperature:REAL;
END_VAR
```

**After** with `SpaceAfterColon = true`:

```st
VAR
    nCounter : INT;
    bEnable : BOOL;
    fTemperature : REAL;
END_VAR
```

**After** with `SpaceAfterColon = false`:

```st
VAR
    nCounter :INT;
    bEnable :BOOL;
    fTemperature :REAL;
END_VAR
```

---

## 6. Alignment / Ausrichtung

### AlignAssignments

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Aligns the `:=` operator in consecutive assignment statements within a statement block. Consecutive assignments (two or more in a row) are grouped and their assignment operators are aligned to the same column.

Richtet den Zuweisungsoperator `:=` in aufeinanderfolgenden Zuweisungen aus.

**Before**:

```st
nErrorCode := 0;
bMotorRunning := TRUE;
fTemperature := 22.5;
sErrorMessage := '';
```

**After** with `AlignAssignments = true`:

```st
nErrorCode     := 0;
bMotorRunning  := TRUE;
fTemperature   := 22.5;
sErrorMessage  := '';
```

**After** with `AlignAssignments = false`:

```st
nErrorCode := 0;
bMotorRunning := TRUE;
fTemperature := 22.5;
sErrorMessage := '';
```

### AlignVariableDeclarations

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Aligns variable names, types, and initializers in VAR declaration sections. When enabled, columns are aligned so that all colons, type names, and initializers line up vertically within a VAR block.

Richtet Variablennamen, Typen und Initialisierer in VAR-Deklarationsabschnitten aus.

**Before**:

```st
VAR_INPUT
    bEnable : BOOL;
    nMode : INT := 1;
    fSpeed : REAL := 50.0;
    sName : STRING(80) := 'Motor';
END_VAR
```

**After** with `AlignVariableDeclarations = true`:

```st
VAR_INPUT
    bEnable : BOOL;
    nMode   : INT          := 1;
    fSpeed  : REAL         := 50.0;
    sName   : STRING(80)   := 'Motor';
END_VAR
```

**After** with `AlignVariableDeclarations = false`:

```st
VAR_INPUT
    bEnable : BOOL;
    nMode : INT := 1;
    fSpeed : REAL := 50.0;
    sName : STRING(80) := 'Motor';
END_VAR
```

The alignment calculation considers:
- **Name column**: all colon (`:`) characters align to the widest variable name + 1 space
- **Type column**: all `:=` operators align to the widest type name when initializers are present
- Variables without initializers end after the type and semicolon; no trailing padding is added

Die Ausrichtungsberechnung beruecksichtigt:
- Namensspalte: alle Doppelpunkte richten sich nach dem breitesten Variablennamen aus
- Typspalte: alle `:=`-Operatoren richten sich nach dem breitesten Typnamen aus (nur wenn Initialisierer vorhanden)

---

## 7. Line Wrapping / Zeilenumbruch

### MaxLineLength

| Property | Value |
|----------|-------|
| Type     | `int` |
| Default  | `120` |

Maximum line length before the formatter wraps long lines. Set to `0` to disable line wrapping entirely. When a line exceeds this limit, continuation lines are indented by `ContinuationIndentSize` spaces from the current indentation level.

Maximale Zeilenlaenge bevor der Formatierer lange Zeilen umbricht. Auf `0` setzen, um den Zeilenumbruch zu deaktivieren.

**Before** (single long line):

```st
IF bMotorRunning AND NOT bFaultDetected AND nCurrentSpeed < nMaxSpeed AND nTemperature < nTempLimit THEN
```

**After** with `MaxLineLength = 80`:

```st
IF bMotorRunning AND NOT bFaultDetected
        AND nCurrentSpeed < nMaxSpeed
        AND nTemperature < nTempLimit THEN
    nCurrentSpeed := nCurrentSpeed + 1;
END_IF
```

**After** with `MaxLineLength = 120` (default, line fits):

```st
IF bMotorRunning AND NOT bFaultDetected AND nCurrentSpeed < nMaxSpeed AND nTemperature < nTempLimit THEN
    nCurrentSpeed := nCurrentSpeed + 1;
END_IF
```

**After** with `MaxLineLength = 0` (wrapping disabled):

```st
IF bMotorRunning AND NOT bFaultDetected AND nCurrentSpeed < nMaxSpeed AND nTemperature < nTempLimit THEN
    nCurrentSpeed := nCurrentSpeed + 1;
END_IF
```

---

## 8. Empty Lines / Leerzeilen

### EmptyLinesBetweenPOUs

| Property | Value |
|----------|-------|
| Type     | `int` |
| Default  | `2` |

Number of empty lines inserted between top-level Program Organization Units (PROGRAM, FUNCTION_BLOCK, FUNCTION, etc.). This controls the vertical spacing between major code sections.

Anzahl der Leerzeilen zwischen Program Organization Units.

**Before** (inconsistent spacing):

```st
PROGRAM Main
END_PROGRAM
FUNCTION Calculate : INT
END_FUNCTION
FUNCTION_BLOCK Motor
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenPOUs = 2` (default):

```st
PROGRAM Main
END_PROGRAM


FUNCTION Calculate : INT
END_FUNCTION


FUNCTION_BLOCK Motor
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenPOUs = 1`:

```st
PROGRAM Main
END_PROGRAM

FUNCTION Calculate : INT
END_FUNCTION

FUNCTION_BLOCK Motor
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenPOUs = 0`:

```st
PROGRAM Main
END_PROGRAM
FUNCTION Calculate : INT
END_FUNCTION
FUNCTION_BLOCK Motor
END_FUNCTION_BLOCK
```

### EmptyLinesBetweenVarSections

| Property | Value |
|----------|-------|
| Type     | `int` |
| Default  | `1` |

Number of empty lines between consecutive VAR sections (VAR, VAR_INPUT, VAR_OUTPUT, VAR_IN_OUT, etc.) within the same POU.

Anzahl der Leerzeilen zwischen aufeinanderfolgenden VAR-Abschnitten.

**Before** (no spacing):

```st
FUNCTION_BLOCK TempSensor
VAR_INPUT
    nSetpoint : INT;
END_VAR
VAR_OUTPUT
    fActualTemp : REAL;
END_VAR
VAR
    nOffset : INT := 0;
END_VAR
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenVarSections = 1` (default):

```st
FUNCTION_BLOCK TempSensor
    VAR_INPUT
        nSetpoint : INT;
    END_VAR

    VAR_OUTPUT
        fActualTemp : REAL;
    END_VAR

    VAR
        nOffset : INT := 0;
    END_VAR
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenVarSections = 0`:

```st
FUNCTION_BLOCK TempSensor
    VAR_INPUT
        nSetpoint : INT;
    END_VAR
    VAR_OUTPUT
        fActualTemp : REAL;
    END_VAR
    VAR
        nOffset : INT := 0;
    END_VAR
END_FUNCTION_BLOCK
```

**After** with `EmptyLinesBetweenVarSections = 2`:

```st
FUNCTION_BLOCK TempSensor
    VAR_INPUT
        nSetpoint : INT;
    END_VAR


    VAR_OUTPUT
        fActualTemp : REAL;
    END_VAR


    VAR
        nOffset : INT := 0;
    END_VAR
END_FUNCTION_BLOCK
```

---

## 9. Single-Line Blocks / Einzeilige Bloecke

### KeepSingleLineBlocks

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `false` |

When `true`, preserves IF/END_IF, FOR/END_FOR, etc. on a single line if the original source already has the body on the same line. When `false` (default), all block statements are expanded to multi-line format regardless of the original layout.

Wenn `true`, werden einzeilige Bloecke beibehalten, wenn sie im Quellcode bereits einzeilig sind.

**Before** (single-line IF in source):

```st
IF bEnable THEN nResult := 1; END_IF
```

**After** with `KeepSingleLineBlocks = false` (default, always expanded):

```st
IF bEnable THEN
    nResult := 1;
END_IF
```

**After** with `KeepSingleLineBlocks = true` (preserved):

```st
IF bEnable THEN nResult := 1; END_IF
```

This option only applies when the entire block (condition + body + end keyword) originally fits on one line. Multi-line blocks in the source are always reformatted.

Diese Option gilt nur, wenn der gesamte Block im Originalcode in eine Zeile passt.

---

## 10. Format on Save / Bei Speichern formatieren

### FormatOnSave

| Property | Value |
|----------|-------|
| Type     | `bool` |
| Default  | `true` |

Enables automatic formatting when an editor integration supports save events. The CLI does not use this setting.

Aktiviert die automatische Formatierung beim Speichern einer Datei.

When enabled by an integration, the formatter runs before or during save and the editor's undo stack should preserve the pre-format state.

When `FormatOnSave` is `true`:
- An editor integration may format `.st`, `.TcPOU`, `.TcDUT`, or `.TcGVL` files automatically.
- The formatting configuration is resolved from `.editorconfig` plus any integration-specific settings.

---

## 11. Presets / Vorlagen

Presets provide named collections of formatting settings for common coding styles. They are available via the CLI (`stfmt preset`), the `FormattingConfiguration.FromPreset()` API, and the `.editorconfig` init command.

Presets bieten benannte Sammlungen von Formatierungseinstellungen fuer ggf. gaengige Codestile.

### Default / Standard

Matches the built-in defaults of `FormattingConfiguration`.

| Property                      | Value       |
|-------------------------------|-------------|
| `IndentStyle`                 | `"spaces"`  |
| `IndentSize`                  | `4`         |
| `ContinuationIndentSize`      | `8`         |
| `NewLineStyle`                | `"crlf"`    |
| `KeywordCasing`               | `"upper"`   |
| `BraceStyle`                  | `"allman"`  |
| `SpaceAroundOperators`        | `true`      |
| `SpaceAfterComma`             | `true`      |
| `SpaceBeforeSemicolon`        | `false`     |
| `SpaceAfterColon`             | `true`      |
| `AlignAssignments`            | `true`      |
| `AlignVariableDeclarations`   | `true`      |
| `MaxLineLength`               | `120`       |
| `EmptyLinesBetweenPOUs`       | `2`         |
| `EmptyLinesBetweenVarSections`| `1`         |
| `KeepSingleLineBlocks`        | `false`     |
| `FormatOnSave`                | `true`      |

### Default (Preset)

Professional, readable style with generous vertical spacing and aligned declarations.

| Property                      | Value       |
|-------------------------------|-------------|
| `IndentStyle`                 | `"spaces"`  |
| `IndentSize`                  | `4`         |
| `ContinuationIndentSize`      | `8`         |
| `NewLineStyle`                | `"crlf"`    |
| `KeywordCasing`               | `"upper"`   |
| `BraceStyle`                  | `"allman"`  |
| `SpaceAroundOperators`        | `true`      |
| `SpaceAfterComma`             | `true`      |
| `SpaceBeforeSemicolon`        | `false`     |
| `SpaceAfterColon`             | `true`      |
| `AlignAssignments`            | `true`      |
| `AlignVariableDeclarations`   | `true`      |
| `MaxLineLength`               | `120`       |
| `EmptyLinesBetweenPOUs`       | `2`         |
| `EmptyLinesBetweenVarSections`| `1`         |
| `KeepSingleLineBlocks`        | `false`     |
| `FormatOnSave`                | `true`      |

**Example output** with Default preset:

```st
PROGRAM ConveyorControl
    VAR_INPUT
        bStart      : BOOL;
        bStop       : BOOL;
        nMode       : INT          := 1;
    END_VAR

    VAR_OUTPUT
        bRunning    : BOOL;
        nStatus     : INT;
    END_VAR

    VAR
        nCounter    : INT          := 0;
        fSpeed      : REAL         := 0.0;
        sErrorMsg   : STRING(80)   := '';
    END_VAR

    IF bStart AND NOT bStop THEN
        bRunning   := TRUE;
        nStatus    := nMode;
        nCounter   := nCounter + 1;
        fSpeed     := fSpeed + 10.5;
    END_IF
END_PROGRAM
```

### CompactPreset

Minimises vertical and horizontal space. 2-space indent, no alignment, single-line blocks preserved. Suitable for code reviews on small screens or deeply nested logic.

Minimiert vertikalen und horizontalen Platz. 2 Leerzeichen Einrueckung, keine Ausrichtung, einzeilige Bloecke erhalten.

| Property                      | Value       |
|-------------------------------|-------------|
| `IndentStyle`                 | `"spaces"`  |
| `IndentSize`                  | `2`         |
| `ContinuationIndentSize`      | `4`         |
| `NewLineStyle`                | `"crlf"`    |
| `KeywordCasing`               | `"lower"`   |
| `BraceStyle`                  | `"allman"`  |
| `SpaceAroundOperators`        | `true`      |
| `SpaceAfterComma`             | `true`      |
| `SpaceBeforeSemicolon`        | `false`     |
| `SpaceAfterColon`             | `true`      |
| `AlignAssignments`            | `false`     |
| `AlignVariableDeclarations`   | `false`     |
| `MaxLineLength`               | `120`       |
| `EmptyLinesBetweenPOUs`       | `1`         |
| `EmptyLinesBetweenVarSections`| `0`         |
| `KeepSingleLineBlocks`        | `true`      |
| `FormatOnSave`                | `true`      |

**Example output** with CompactPreset:

```st
program ConveyorControl
  var_input
    bStart : BOOL;
    nMode : INT := 1;
  end_var
  var_output
    bRunning : BOOL;
    nStatus : INT;
  end_var
  var
    nCounter : INT := 0;
    fSpeed : REAL := 0.0;
  end_var
  if bStart then
    bRunning := true;
    nCounter := nCounter + 1;
    if nMode = 1 then nStatus := 10; end_if
  end_if
end_program
```

### ExpandedPreset

Generous vertical spacing with an 80-character line limit. Best for printouts, code reviews, and strict style guides. Aligns declarations and assignments for maximum scanability.

Grosszuegiger vertikaler Abstand mit 80-Zeichen-Limit. Optimal fuer Ausdrucke und Code-Reviews.

| Property                      | Value       |
|-------------------------------|-------------|
| `IndentStyle`                 | `"spaces"`  |
| `IndentSize`                  | `4`         |
| `ContinuationIndentSize`      | `8`         |
| `NewLineStyle`                | `"crlf"`    |
| `KeywordCasing`               | `"upper"`   |
| `BraceStyle`                  | `"allman"`  |
| `SpaceAroundOperators`        | `true`      |
| `SpaceAfterComma`             | `true`      |
| `SpaceBeforeSemicolon`        | `false`     |
| `SpaceAfterColon`             | `true`      |
| `AlignAssignments`            | `true`      |
| `AlignVariableDeclarations`   | `true`      |
| `MaxLineLength`               | `80`        |
| `EmptyLinesBetweenPOUs`       | `3`         |
| `EmptyLinesBetweenVarSections`| `2`         |
| `KeepSingleLineBlocks`        | `false`     |
| `FormatOnSave`                | `true`      |

**Example output** with ExpandedPreset:

```st
PROGRAM ConveyorControl
    VAR_INPUT
        bStart      : BOOL;
        nMode       : INT          := 1;
    END_VAR


    VAR_OUTPUT
        bRunning    : BOOL;
        nStatus     : INT;
    END_VAR


    VAR
        nCounter    : INT          := 0;
        fSpeed      : REAL         := 0.0;
    END_VAR

    IF bStart
            AND NOT bStop THEN
        bRunning   := TRUE;
        nStatus    := nMode;
        nCounter   := nCounter + 1;
    END_IF
END_PROGRAM



FUNCTION CalculateSpeed : REAL
    VAR_INPUT
        nBase : INT;
        fFactor : REAL;
    END_VAR

    CalculateSpeed := nBase * fFactor;
END_FUNCTION
```

### Preset Comparison / Preset-Vergleich

| Option                         | Default | Compact | Expanded |
|--------------------------------|---------|--------|---------|----------|
| `IndentSize`                   | 4       | 4      | 2       | 4        |
| `ContinuationIndentSize`       | 8       | 8      | 4       | 8        |
| `KeywordCasing`                | upper   | upper  | lower   | upper    |
| `MaxLineLength`                | 120     | 120    | 120     | 80       |
| `AlignAssignments`             | true    | true   | false   | true     |
| `AlignVariableDeclarations`    | true    | true   | false   | true     |
| `EmptyLinesBetweenPOUs`        | 2       | 2      | 1       | 3        |
| `EmptyLinesBetweenVarSections` | 1       | 1      | 0       | 2        |
| `KeepSingleLineBlocks`         | false   | false  | true    | false    |

---

## 12. EditorConfig Integration / EditorConfig-Integration

The formatter reads `.editorconfig` files using the standard EditorConfig specification. Files are discovered by walking up the directory tree from the source file, merging sections that match the file pattern. A `.editorconfig` with `root = true` stops the upward search.

Der Formatierer liest `.editorconfig`-Dateien. Die Suche erfolgt aufwaerts im Verzeichnisbaum.

### Standard EditorConfig Properties / Standard-Eigenschaften

The following standard EditorConfig properties are mapped directly to `FormattingConfiguration`:

| .editorconfig Property | FormattingConfiguration Property |
|------------------------|-----------------------------------|
| `indent_style`         | `IndentStyle` (`"tab"` maps to `"tabs"`) |
| `indent_size`          | `IndentSize` |
| `tab_width`            | `IndentSize` (only when `indent_style = tab`) |
| `end_of_line`          | `NewLineStyle` (`"crlf"`, `"lf"`, `"cr"`) |
| `max_line_length`     | `MaxLineLength` (`"off"` sets to `0`) |

### ST-Specific Properties / ST-spezifische Eigenschaften

Custom properties use the `st_` prefix to avoid collisions with other language settings:

| .editorconfig Property             | FormattingConfiguration Property     |
|-------------------------------------|---------------------------------------|
| `st_keyword_casing`                | `KeywordCasing`                       |
| `st_space_around_operators`         | `SpaceAroundOperators`                |
| `st_align_variable_declarations`    | `AlignVariableDeclarations`           |
| `st_align_assignments`             | `AlignAssignments`                    |
| `st_empty_lines_between_pous`      | `EmptyLinesBetweenPOUs`               |
| `st_empty_lines_between_var_sections` | `EmptyLinesBetweenVarSections`     |
| `st_format_on_save`                | `FormatOnSave`                        |

Boolean values accept `true`, `yes`, `1`, `on` for truthy and any other value for falsy.

Boolesche Werte akzeptieren `true`, `yes`, `1`, `on` als wahrheitsgemaess.

### Example .editorconfig / Beispiel-.editorconfig

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

### Configuration Resolution / Konfigurationsaufloesung

Settings are resolved in the following priority order (highest to lowest):

Einstellungen werden in folgender Prioritaet aufgeloest (hoechste bis niedrigste):

1. **VS/TcXaeShell Options page** — overrides everything (IDE integrations only)
2. **CLI command-line flags** — overrides `.editorconfig` (CLI only)
3. **`.editorconfig` nearest to file** — merges with parent configs
4. **`.editorconfig` walking up directories** — each closer file overrides more distant ones
5. **`FormattingConfiguration.Default`** — built-in defaults

The `EditorConfigParser` walks the directory tree from the source file location upward, collecting `.editorconfig` files. Each file may contain multiple sections (e.g., `[*]`, `[*.st]`). Properties from sections matching the file pattern are merged, with closer files overriding more distant ones.

Der `EditorConfigParser` durchlaeuft den Verzeichnisbaum aufwaerts und sammelt `.editorconfig`-Dateien.

### Generating .editorconfig / .editorconfig erstellen

Use the CLI to generate an `.editorconfig` from a preset:

```bash
stfmt init . --preset default
stfmt init . --preset compact
stfmt init . --preset expanded
```

Or export/import configuration:

```bash
stfmt export myconfig.json --preset compact
stfmt import myconfig.json
```

---

## 13. CLI Configuration / CLI-Konfiguration

The `stfmt` command-line tool provides full configuration management.

Das Befehlszeilen-Tool `stfmt` bietet volle Konfigurationsverwaltung.

### Commands / Befehle

| Command                       | Description                                                        |
|-------------------------------|--------------------------------------------------------------------|
| `stfmt format <file>`        | Format a single ST file in place                                   |
| `stfmt format <file> -o <out>` | Format and write to a different file                              |
| `stfmt format <file> --dry-run` | Preview formatted output without writing                           |
| `stfmt check <path>`          | Check if files are formatted (exit code 0=clean, 1=differs)        |
| `stfmt check <path> --recursive` | Check all ST files in directory tree                             |
| `stfmt batch <dir>`           | Format all `.st`, `.txt`, `.iecst` files in a directory           |
| `stfmt batch <dir> --twincat` | Also process `.TcPOU`, `.TcDUT`, `.TcGVL` files                |
| `stfmt init [dir]`            | Create `.editorconfig` with formatting settings                   |
| `stfmt init [dir] --preset <name>` | Create `.editorconfig` from a named preset                     |
| `stfmt preset`                | List available presets                                              |
| `stfmt preset <name>`         | Show details for a specific preset                                 |
| `stfmt export [file]`        | Export configuration to JSON                                       |
| `stfmt import <json-file>`    | Import configuration from JSON and write `.editorconfig`          |

### Configuration Sources / Konfigurationsquellen

The CLI resolves configuration from multiple sources in priority order:

1. `.editorconfig` files (discovered by walking up from the target file's directory)
2. `FormattingConfiguration.Default` (built-in fallback)

The `stfmt init` command creates an `.editorconfig` file in the target directory. The `stfmt export` command writes the configuration as JSON for version control or sharing. The `stfmt import` command converts a previously exported JSON file back into an `.editorconfig`.

Der Befehl `stfmt init` erstellt eine `.editorconfig`-Datei. `stfmt export` schreibt die Konfiguration als JSON. `stfmt import` konvertiert eine JSON-Datei zurueck in `.editorconfig`.

### Examples / Beispiele

```bash
# Format a single file
stfmt format Main.st

# Format with output to a different file
stfmt format Main.st -o Main_formatted.st

# Preview formatting without modifying the file
stfmt format Main.st --dry-run

# Check if a file matches the formatting rules (CI pipeline)
stfmt check Main.st
echo $?  # 0 = formatted, 1 = needs formatting

# Batch format an entire project
stfmt batch ./POUs --recursive --twincat

# Initialize project with compact preset
stfmt init . --preset compact

# View preset details
stfmt preset default

# Export current configuration for version control
stfmt export team-style.json --preset default

# Import and apply configuration from JSON
stfmt import team-style.json
```

---

## 14. Host Settings / Host-Einstellungen

The TcXaeShell Host provides a tray settings window for runtime formatter options.

Der TcXaeShell Host bietet ein Einstellungsfenster im Tray fuer Formatter-Optionen zur Laufzeit.

### Access / Zugriff

- Right-click the `STFormatter.Host` tray icon.
- Select **Settings**.

Settings are stored in `%LOCALAPPDATA%\STFormatter\settings.json`. Team defaults should still live in `.editorconfig`.

### Available Options / Verfuegbare Optionen

| Category       | Option                          | Type    | Default     |
|----------------|---------------------------------|---------|-------------|
| Indentation    | Indent Style                    | string  | `spaces`    |
| Indentation    | Indent Size                     | int     | `4`         |
| Formatting     | Keyword Casing                  | string  | `upper`     |
| Formatting     | Space Around Operators          | bool    | `true`      |
| Formatting     | Format On Save                  | bool    | `true`      |
| Line Breaks    | Empty Lines Between POUs       | int     | `2`         |
| Line Breaks    | Empty Lines Between Var Sections| int     | `1`         |

The Indent Style dropdown offers `spaces` and `tabs`. The Keyword Casing dropdown offers `upper`, `lower`, `pascal`, and `original`.

Das Indent-Style-Dropdown bietet `spaces` und `tabs`. Das Keyword-Casing-Dropdown bietet `upper`, `lower`, `pascal` und `original`.

### Configuration Resolution in Host / Konfigurationsaufloesung im Host

When the formatter runs from the TcXaeShell Host, configuration is resolved as:

1. **Host settings** — user settings from the tray settings window
2. **`.editorconfig` files** — discovered from the file's directory upward
3. **`FormattingConfiguration.Default`** — fallback

Host settings override `.editorconfig`. This ensures the user's personal preferences take effect, while team-shared settings in `.editorconfig` provide sensible defaults for options the user has not explicitly configured.

Host-Einstellungen ueberschreiben `.editorconfig`. Dadurch haben persoenliche Einstellungen Vorrang, waehrend Team-Einstellungen in `.editorconfig` als Standardvorgaben dienen.

### Format on Save / Bei Speichern formatieren

The current production TcXaeShell Host exposes manual context-menu formatting commands. `FormatOnSave` remains a configuration hint for integrations that implement save-event formatting.

Der aktuelle Produktions-Host fuer TcXaeShell stellt manuelle Kontextmenue-Befehle bereit. `FormatOnSave` bleibt ein Konfigurationshinweis fuer Integrationen, die Speicher-Ereignisse implementieren.

### Format Commands / Formatierungsbefehle

| Command            | Shortcut    | Description                             |
|--------------------|-------------|-----------------------------------------|
| Format Document    | `Ctrl+K, D` | Format the entire active document        |
| Format Selection   | `Ctrl+K, F` | Format the selected text range           |

These commands are injected by `STFormatter.Host` into TcXaeShell context menus and invoke `FormattingEngine.Format()`, `FormatDeclaration()`, or `FormatBody()` depending on context.

Diese Befehle werden von `STFormatter.Host` in TcXaeShell-Kontextmenues eingefuegt und rufen je nach Kontext `FormattingEngine.Format()`, `FormatDeclaration()` oder `FormatBody()` auf.

---

## Complete Property Reference / Vollstaendige Eigenschaftsreferenz

| Property                        | Type    | Default     | CLI/.editorconfig                               |
|---------------------------------|---------|-------------|-------------------------------------------------|
| `IndentStyle`                   | string  | `"spaces"`  | `indent_style`                                  |
| `IndentSize`                    | int     | `4`         | `indent_size`                                   |
| `ContinuationIndentSize`        | int     | `8`         | `st_continuation_indent_size` (editorconfig only) |
| `NewLineStyle`                  | string  | `"crlf"`    | `end_of_line`                                   |
| `KeywordCasing`                 | string  | `"upper"`   | `st_keyword_casing`                             |
| `BraceStyle`                    | string  | `"allman"`   | `st_brace_style` (editorconfig only)            |
| `SpaceAroundOperators`          | bool    | `true`      | `st_space_around_operators`                      |
| `SpaceAfterComma`               | bool    | `true`      | `st_space_after_comma` (editorconfig only)       |
| `SpaceBeforeSemicolon`          | bool    | `false`     | `st_space_before_semicolon` (editorconfig only)  |
| `SpaceAfterColon`                | bool    | `true`      | `st_space_after_colon` (editorconfig only)        |
| `AlignAssignments`              | bool    | `true`      | `st_align_assignments`                           |
| `AlignVariableDeclarations`     | bool    | `true`      | `st_align_variable_declarations`                  |
| `MaxLineLength`                 | int     | `120`       | `max_line_length`                                |
| `EmptyLinesBetweenPOUs`         | int     | `2`         | `st_empty_lines_between_pous`                    |
| `EmptyLinesBetweenVarSections`  | int     | `1`         | `st_empty_lines_between_var_sections`            |
| `KeepSingleLineBlocks`          | bool    | `false`     | `st_keep_single_line_blocks` (editorconfig only)  |
| `FormatOnSave`                  | bool    | `true`      | `st_format_on_save`                              |

Properties marked "(editorconfig only)" for the last column are supported in `.editorconfig` files via the `st_` prefix but are not currently exposed as separate dropdowns in the VS/XAE Options page. The full set of options is always available through `.editorconfig`.

Eigenschaften, die als "(editorconfig only)" markiert sind, werden ueber den `st_`-Praefix in `.editorconfig`-Dateien unterstuetzt, sind aber aktuell nicht als separate Optionen auf der VS/XAE-Optionenseite verfuegbar.
