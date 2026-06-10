using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using STFormatter.Core.Syntax;
using STFormatter.Core.Text;

namespace STFormatter.Core.Lexing;

public sealed class Lexer
{
    private readonly SourceText _text;
    private readonly List<Diagnostic> _diagnostics;
    private int _position;
    private int _start;
    private SyntaxKind _kind;
    private object? _value;
    private readonly List<SyntaxTrivia> _leadingTrivia;

    public Lexer(SourceText text)
    {
        _text = text;
        _diagnostics = new List<Diagnostic>();
        _position = 0;
        _start = 0;
        _kind = SyntaxKind.None;
        _value = null;
        _leadingTrivia = new List<SyntaxTrivia>();
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public SyntaxToken Lex()
    {
        _leadingTrivia.Clear();
        ReadTrivia();

        _start = _position;
        _kind = SyntaxKind.BadToken;
        _value = null;

        if (_position >= _text.Length)
        {
            return new SyntaxToken(
                SyntaxKind.EndOfFile,
                string.Empty,
                new TextSpan(_position, 0),
                _leadingTrivia.ToImmutableArray(),
                ImmutableArray<SyntaxTrivia>.Empty);
        }

        var c = Current;
        var next = Peek(1);

        switch (c)
        {
            case '\0':
                _kind = SyntaxKind.EndOfFile;
                break;

            case '+':
                _kind = SyntaxKind.Plus;
                _position++;
                break;
            case '-':
                _kind = SyntaxKind.Minus;
                _position++;
                break;
            case '*':
                if (next == '*')
                {
                    _kind = SyntaxKind.Power;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.Asterisk;
                    _position++;
                }
                break;
            case '/':
                _kind = SyntaxKind.Slash;
                _position++;
                break;
            case '=':
                if (next == '>')
                {
                    _kind = SyntaxKind.ArrowOperator;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.Equal;
                    _position++;
                }
                break;
            case '<':
                if (next == '>')
                {
                    _kind = SyntaxKind.NotEqual;
                    _position += 2;
                }
                else if (next == '=')
                {
                    _kind = SyntaxKind.LessThanOrEqual;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.LessThan;
                    _position++;
                }
                break;
            case '>':
                if (next == '=')
                {
                    _kind = SyntaxKind.GreaterThanOrEqual;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.GreaterThan;
                    _position++;
                }
                break;
            case ':':
                if (next == '=')
                {
                    _kind = SyntaxKind.AssignmentOperator;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.Colon;
                    _position++;
                }
                break;
            case ';':
                _kind = SyntaxKind.Semicolon;
                _position++;
                break;
            case ',':
                _kind = SyntaxKind.Comma;
                _position++;
                break;
            case '.':
                if (next == '.')
                {
                    _kind = SyntaxKind.DotDot;
                    _position += 2;
                }
                else
                {
                    _kind = SyntaxKind.Dot;
                    _position++;
                }
                break;
            case '(':
                _kind = SyntaxKind.OpenParen;
                _position++;
                break;
            case ')':
                _kind = SyntaxKind.CloseParen;
                _position++;
                break;
            case '[':
                _kind = SyntaxKind.OpenBracket;
                _position++;
                break;
            case ']':
                _kind = SyntaxKind.CloseBracket;
                _position++;
                break;
            case '{':
                _kind = SyntaxKind.OpenBrace;
                _position++;
                break;
            case '}':
                _kind = SyntaxKind.CloseBrace;
                _position++;
                break;
            case '#':
                // Check if this is part of a time/date literal prefix
                if (_position > 0 && char.IsLetter(_text[_position - 1]))
                {
                    // This should have been handled by ReadIdentifierOrKeyword if followed by time value
                    // But if we're here, it might be a standalone #
                    _kind = SyntaxKind.Hash;
                    _position++;
                }
                else
                {
                    _kind = SyntaxKind.Hash;
                    _position++;
                }
                break;
            case '&':
                _kind = SyntaxKind.Ampersand;
                _position++;
                break;
            case '^':
                _kind = SyntaxKind.Caret;
                _position++;
                break;
            case '%':
                ReadDirectVariable();
                break;
            case '\'':
                ReadStringLiteral();
                break;
            case '"':
                ReadWStringLiteral();
                break;

            default:
                if (char.IsLetter(c) || c == '_')
                {
                    ReadIdentifierOrKeyword();
                }
                else if (char.IsDigit(c))
                {
                    ReadNumber();
                }
                else
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        new TextSpan(_position, 1),
                        $"Unexpected character '{c}'"));
                    _position++;
                }
                break;
        }

        var length = _position - _start;
        var text = _text.ToString(new TextSpan(_start, length));
        var span = new TextSpan(_start, length);

        if (_kind == SyntaxKind.Identifier && text.StartsWith("__"))
        {
            // Check for TwinCAT special identifiers
            _kind = text.ToUpperInvariant() switch
            {
                "__TRY" => SyntaxKind.TryKeyword,
                "__CATCH" => SyntaxKind.CatchKeyword,
                "__FINALLY" => SyntaxKind.FinallyKeyword,
                "__ENDTRY" => SyntaxKind.EndTryKeyword,
                "__GET_SYS" or "__SET_SYS" or "__NEW" or "__DELETE" => SyntaxKind.Identifier,
                _ => SyntaxKind.Identifier
            };
        }

        return new SyntaxToken(
            _kind,
            text,
            span,
            _leadingTrivia.ToImmutableArray(),
            ImmutableArray<SyntaxTrivia>.Empty,
            _value);
    }

    private char Current => _position < _text.Length ? _text[_position] : '\0';

    private char Peek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private void ReadTrivia()
    {
        while (true)
        {
            var c = Current;

            if (c == ' ' || c == '\t')
            {
                ReadWhitespace();
            }
            else if (c == '\r' || c == '\n')
            {
                ReadLineBreak();
            }
            else if (c == '(' && Peek(1) == '*')
            {
                ReadMultiLineComment();
            }
            else if (c == '/' && Peek(1) == '/')
            {
                ReadSingleLineComment();
            }
            else if (c == '/' && Peek(1) == '*')
            {
                ReadCStyleComment();
            }
            else if (c == '{')
            {
                ReadPragma();
            }
            else
            {
                break;
            }
        }
    }

    private void ReadWhitespace()
    {
        var start = _position;
        while (Current == ' ' || Current == '\t')
            _position++;

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.WhitespaceTrivia, text, new TextSpan(start, length)));
    }

    private void ReadLineBreak()
    {
        var start = _position;
        if (Current == '\r' && Peek(1) == '\n')
            _position += 2;
        else if (Current == '\r' || Current == '\n')
            _position++;

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.LineBreakTrivia, text, new TextSpan(start, length)));
    }

    private void ReadSingleLineComment()
    {
        var start = _position;
        _position += 2; // skip //

        while (Current != '\r' && Current != '\n' && Current != '\0')
            _position++;

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.SingleLineCommentTrivia, text, new TextSpan(start, length)));
    }

    private void ReadMultiLineComment()
    {
        var start = _position;
        _position += 2; // skip (*

        while (true)
        {
            if (Current == '\0')
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    new TextSpan(start, _position - start),
                    "Unterminated comment"));
                break;
            }

            if (Current == '*' && Peek(1) == ')')
            {
                _position += 2;
                break;
            }

            _position++;
        }

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, text, new TextSpan(start, length)));
    }

    private void ReadCStyleComment()
    {
        var start = _position;
        _position += 2; // skip /*

        while (true)
        {
            if (Current == '\0')
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    new TextSpan(start, _position - start),
                    "Unterminated comment"));
                break;
            }

            if (Current == '*' && Peek(1) == '/')
            {
                _position += 2;
                break;
            }

            _position++;
        }

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, text, new TextSpan(start, length)));
    }

    private void ReadPragma()
    {
        var start = _position;
        _position++; // skip {

        while (true)
        {
            if (Current == '\0')
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    new TextSpan(start, _position - start),
                    "Unterminated pragma"));
                break;
            }

            if (Current == '}')
            {
                _position++;
                break;
            }

            _position++;
        }

        var length = _position - start;
        var text = _text.ToString(new TextSpan(start, length));
        _leadingTrivia.Add(new SyntaxTrivia(SyntaxKind.PragmaTrivia, text, new TextSpan(start, length)));
    }

    private void ReadIdentifierOrKeyword()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_')
            _position++;

        var length = _position - _start;
        var text = _text.ToString(new TextSpan(_start, length));
        _kind = GetKeywordKind(text);

        // Check for time/date literals: T#, TIME#, D#, DATE#, TOD#, TIME_OF_DAY#, DT#, DATE_AND_TIME#
        if (Current == '#' && IsTimeDatePrefix(text))
        {
            _position++; // skip #
            ReadTimeDateLiteralValue(text);
        }
    }

    private static bool IsTimeDatePrefix(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "T" or "TIME" or "D" or "DATE" or "TOD" or "TIME_OF_DAY" or "DT" or "DATE_AND_TIME" => true,
            _ => false
        };
    }

    private void ReadTimeDateLiteralValue(string prefix)
    {
        var isTime = prefix.ToUpperInvariant() is "T" or "TIME" or "TOD" or "TIME_OF_DAY";
        var isDate = prefix.ToUpperInvariant() is "D" or "DATE" or "DT" or "DATE_AND_TIME";

        while (Current != '\0' && Current != ';' && Current != '\r' && Current != '\n' && Current != ' ' && Current != '\t')
        {
            _position++;
        }

        _kind = isTime ? SyntaxKind.TimeLiteral : SyntaxKind.DateLiteral;
    }

    private void ReadNumber()
    {
        if (Current == '0')
        {
            var next = char.ToUpperInvariant(Peek(1));
            if (next == 'X')
            {
                ReadHexNumber();
                return;
            }
            if (next == 'B')
            {
                ReadBinaryNumber();
                return;
            }
            if (next == 'O')
            {
                ReadOctalNumber();
                return;
            }
        }

        // Check for IEC 61131-3 base#value format (e.g., 16#FF, 2#1010, 8#77)
        ReadDecimalOrRealNumberWithBase();
    }

    private void ReadDecimalOrRealNumberWithBase()
    {
        // Read the base or integer part
        while (char.IsDigit(Current))
            _position++;

        // Check for base#value format
        if (Current == '#' && _position > _start)
        {
            var baseText = _text.ToString(new TextSpan(_start, _position - _start));
            if (int.TryParse(baseText, out var baseValue) && baseValue >= 2 && baseValue <= 36)
            {
                _position++; // skip #
                var valueStart = _position;
                while (char.IsDigit(Current) || (Current >= 'A' && Current <= 'F') || (Current >= 'a' && Current <= 'f'))
                    _position++;

                _kind = SyntaxKind.NumericLiteral;
                var valueText = _text.ToString(new TextSpan(valueStart, _position - valueStart));
                try
                {
                    _value = Convert.ToInt32(valueText, baseValue);
                }
                catch
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        new TextSpan(_start, _position - _start),
                        $"Invalid number in base {baseValue}"));
                }
                return;
            }
        }

        // Continue with decimal/real parsing from current position
        if (Current == '.' && char.IsDigit(Peek(1)))
        {
            _position++;
            while (char.IsDigit(Current))
                _position++;

            if (Current == 'E' || Current == 'e')
            {
                _position++;
                if (Current == '+' || Current == '-')
                    _position++;
                while (char.IsDigit(Current))
                    _position++;
            }

            _kind = SyntaxKind.RealLiteral;
            if (double.TryParse(_text.ToString(new TextSpan(_start, _position - _start)),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                _value = d;
            }
        }
        else if (Current == 'E' || Current == 'e')
        {
            _position++;
            if (Current == '+' || Current == '-')
                _position++;
            while (char.IsDigit(Current))
                _position++;

            _kind = SyntaxKind.RealLiteral;
            if (double.TryParse(_text.ToString(new TextSpan(_start, _position - _start)),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                _value = d;
            }
        }
        else
        {
            _kind = SyntaxKind.NumericLiteral;
            if (int.TryParse(_text.ToString(new TextSpan(_start, _position - _start)), out var i))
            {
                _value = i;
            }
            else if (long.TryParse(_text.ToString(new TextSpan(_start, _position - _start)), out var l))
            {
                _value = l;
            }
        }
    }

    private void ReadHexNumber()
    {
        _position += 2; // skip 0x
        while (char.IsDigit(Current) || (Current >= 'A' && Current <= 'F') || (Current >= 'a' && Current <= 'f'))
            _position++;

        _kind = SyntaxKind.NumericLiteral;
        var hexText = _text.ToString(new TextSpan(_start + 2, _position - _start - 2));
        if (int.TryParse(hexText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var i))
        {
            _value = i;
        }
    }

    private void ReadBinaryNumber()
    {
        _position += 2; // skip 0b
        while (Current == '0' || Current == '1')
            _position++;

        _kind = SyntaxKind.NumericLiteral;
        var binText = _text.ToString(new TextSpan(_start + 2, _position - _start - 2));
        try
        {
            _value = Convert.ToInt32(binText, 2);
        }
        catch
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                new TextSpan(_start, _position - _start),
                "Invalid binary number"));
        }
    }

    private void ReadOctalNumber()
    {
        _position += 2; // skip 0o
        while (Current >= '0' && Current <= '7')
            _position++;

        _kind = SyntaxKind.NumericLiteral;
        var octText = _text.ToString(new TextSpan(_start + 2, _position - _start - 2));
        try
        {
            _value = Convert.ToInt32(octText, 8);
        }
        catch
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                new TextSpan(_start, _position - _start),
                "Invalid octal number"));
        }
    }

    private void ReadStringLiteral()
    {
        _position++; // skip '
        var sb = new StringBuilder();

        while (true)
        {
            if (Current == '\0')
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    new TextSpan(_start, _position - _start),
                    "Unterminated string literal"));
                break;
            }

            if (Current == '\'')
            {
                _position++;
                break;
            }

            sb.Append(Current);
            _position++;
        }

        _kind = SyntaxKind.StringLiteral;
        _value = sb.ToString();
    }

    private void ReadWStringLiteral()
    {
        _position++; // skip "
        var sb = new StringBuilder();

        while (true)
        {
            if (Current == '\0')
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    new TextSpan(_start, _position - _start),
                    "Unterminated wide string literal"));
                break;
            }

            if (Current == '"')
            {
                _position++;
                break;
            }

            sb.Append(Current);
            _position++;
        }

        _kind = SyntaxKind.StringLiteral;
        _value = sb.ToString();
    }

    private void ReadDirectVariable()
    {
        _position++; // skip %

        var loc = char.ToUpperInvariant(Current);
        if (loc != 'I' && loc != 'Q' && loc != 'M')
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                new TextSpan(_start, _position - _start),
                "Invalid direct variable location"));
            return;
        }
        _position++;

        var size = char.ToUpperInvariant(Current);
        if (size == '*')
        {
            _position++;
            _kind = SyntaxKind.DirectVariable;
            return;
        }

        if (size != 'X' && size != 'B' && size != 'W' && size != 'D' && size != 'L')
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                new TextSpan(_start, _position - _start),
                "Invalid direct variable size"));
            return;
        }
        _position++;

        while (char.IsDigit(Current))
            _position++;

        if (Current == '.')
        {
            _position++;
            while (char.IsDigit(Current))
                _position++;
        }

        _kind = SyntaxKind.DirectVariable;
    }

    private static SyntaxKind GetKeywordKind(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "PROGRAM" => SyntaxKind.ProgramKeyword,
            "FUNCTION_BLOCK" => SyntaxKind.FunctionBlockKeyword,
            "FUNCTION" => SyntaxKind.FunctionKeyword,
            "METHOD" => SyntaxKind.MethodKeyword,
            "PROPERTY" => SyntaxKind.PropertyKeyword,
            "ACTION" => SyntaxKind.ActionKeyword,
            "TRANSITION" => SyntaxKind.TransitionKeyword,
            "STEP" => SyntaxKind.StepKeyword,
            "INITIAL_STEP" => SyntaxKind.InitialStepKeyword,
            "END_PROGRAM" => SyntaxKind.EndProgramKeyword,
            "END_FUNCTION_BLOCK" => SyntaxKind.EndFunctionBlockKeyword,
            "END_FUNCTION" => SyntaxKind.EndFunctionKeyword,
            "END_METHOD" => SyntaxKind.EndMethodKeyword,
            "END_PROPERTY" => SyntaxKind.EndPropertyKeyword,
            "END_ACTION" => SyntaxKind.EndActionKeyword,
            "END_TRANSITION" => SyntaxKind.EndTransitionKeyword,
            "END_STEP" => SyntaxKind.EndStepKeyword,
            "VAR" => SyntaxKind.VarKeyword,
            "VAR_INPUT" => SyntaxKind.VarInputKeyword,
            "VAR_OUTPUT" => SyntaxKind.VarOutputKeyword,
            "VAR_IN_OUT" => SyntaxKind.VarInOutKeyword,
            "VAR_TEMP" => SyntaxKind.VarTempKeyword,
            "VAR_STAT" => SyntaxKind.VarStatKeyword,
            "VAR_GLOBAL" => SyntaxKind.VarGlobalKeyword,
            "VAR_ACCESS" => SyntaxKind.VarAccessKeyword,
            "VAR_EXTERNAL" => SyntaxKind.VarExternalKeyword,
            "VAR_CONFIG" => SyntaxKind.VarConfigKeyword,
            "VAR_INST" => SyntaxKind.VarInstKeyword,
            "END_VAR" => SyntaxKind.EndVarKeyword,
            "CONSTANT" => SyntaxKind.ConstantKeyword,
            "RETAIN" => SyntaxKind.RetainKeyword,
            "PERSISTENT" => SyntaxKind.PersistentKeyword,
            "READ_ONLY" => SyntaxKind.ReadOnlyKeyword,
            "READ_WRITE" => SyntaxKind.ReadWriteKeyword,
            "IF" => SyntaxKind.IfKeyword,
            "THEN" => SyntaxKind.ThenKeyword,
            "ELSE" => SyntaxKind.ElseKeyword,
            "ELSIF" => SyntaxKind.ElsIfKeyword,
            "END_IF" => SyntaxKind.EndIfKeyword,
            "CASE" => SyntaxKind.CaseKeyword,
            "OF" => SyntaxKind.OfKeyword,
            "END_CASE" => SyntaxKind.EndCaseKeyword,
            "FOR" => SyntaxKind.ForKeyword,
            "TO" => SyntaxKind.ToKeyword,
            "BY" => SyntaxKind.ByKeyword,
            "DO" => SyntaxKind.DoKeyword,
            "END_FOR" => SyntaxKind.EndForKeyword,
            "WHILE" => SyntaxKind.WhileKeyword,
            "END_WHILE" => SyntaxKind.EndWhileKeyword,
            "REPEAT" => SyntaxKind.RepeatKeyword,
            "UNTIL" => SyntaxKind.UntilKeyword,
            "END_REPEAT" => SyntaxKind.EndRepeatKeyword,
            "EXIT" => SyntaxKind.ExitKeyword,
            "CONTINUE" => SyntaxKind.ContinueKeyword,
            "RETURN" => SyntaxKind.ReturnKeyword,
            "GOTO" => SyntaxKind.GotoKeyword,
            "ARRAY" => SyntaxKind.ArrayKeyword,
            "STRUCT" => SyntaxKind.StructKeyword,
            "END_STRUCT" => SyntaxKind.EndStructKeyword,
            "TYPE" => SyntaxKind.TypeKeyword,
            "END_TYPE" => SyntaxKind.EndTypeKeyword,
            "UNION" => SyntaxKind.UnionKeyword,
            "END_UNION" => SyntaxKind.EndUnionKeyword,
            "ENUM" => SyntaxKind.EnumKeyword,
            "END_ENUM" => SyntaxKind.EndEnumKeyword,
            "STRING" => SyntaxKind.StringKeyword,
            "WSTRING" => SyntaxKind.WStringKeyword,
            "POINTER" => SyntaxKind.PointerKeyword,
            "REF" => SyntaxKind.RefToKeyword,
            "REFERENCE" => SyntaxKind.ReferenceKeyword,
            "AT" => SyntaxKind.AtKeyword,
            "EDGE" => SyntaxKind.EdgeKeyword,
            "R_EDGE" => SyntaxKind.REdgeKeyword,
            "F_EDGE" => SyntaxKind.FEdgeKeyword,
            "THIS" => SyntaxKind.ThisKeyword,
            "SUPER" => SyntaxKind.SuperKeyword,
            "PUBLIC" => SyntaxKind.PublicKeyword,
            "PRIVATE" => SyntaxKind.PrivateKeyword,
            "PROTECTED" => SyntaxKind.ProtectedKeyword,
            "INTERNAL" => SyntaxKind.InternalKeyword,
            "FINAL" => SyntaxKind.FinalKeyword,
            "ABSTRACT" => SyntaxKind.AbstractKeyword,
            "OVERRIDE" => SyntaxKind.OverrideKeyword,
            "EXTENDS" => SyntaxKind.ExtendsKeyword,
            "IMPLEMENTS" => SyntaxKind.ImplementsKeyword,
            "INTERFACE" => SyntaxKind.InterfaceKeyword,
            "END_INTERFACE" => SyntaxKind.EndInterfaceKeyword,
            "GET" => SyntaxKind.GetKeyword,
            "SET" => SyntaxKind.SetKeyword,
            "USING" => SyntaxKind.UsingKeyword,
            "FROM" => SyntaxKind.FromKeyword,
            "WITH" => SyntaxKind.WithKeyword,
            "TRY" or "__TRY" => SyntaxKind.TryKeyword,
            "CATCH" or "__CATCH" => SyntaxKind.CatchKeyword,
            "FINALLY" or "__FINALLY" => SyntaxKind.FinallyKeyword,
            "END_TRY" or "__ENDTRY" => SyntaxKind.EndTryKeyword,
            "RAISE" => SyntaxKind.RaiseKeyword,
            "TRUE" => SyntaxKind.TrueKeyword,
            "FALSE" => SyntaxKind.FalseKeyword,
            "NULL" => SyntaxKind.NullKeyword,
            "VOID" => SyntaxKind.VoidKeyword,
            "BOOL" => SyntaxKind.BoolKeyword,
            "BYTE" => SyntaxKind.ByteKeyword,
            "WORD" => SyntaxKind.WordKeyword,
            "DWORD" => SyntaxKind.DWordKeyword,
            "LWORD" => SyntaxKind.LWordKeyword,
            "SINT" => SyntaxKind.SIntKeyword,
            "INT" => SyntaxKind.IntKeyword,
            "DINT" => SyntaxKind.DIntKeyword,
            "LINT" => SyntaxKind.LIntKeyword,
            "USINT" => SyntaxKind.USIntKeyword,
            "UINT" => SyntaxKind.UIntKeyword,
            "UDINT" => SyntaxKind.UDIntKeyword,
            "ULINT" => SyntaxKind.ULIntKeyword,
            "REAL" => SyntaxKind.RealKeyword,
            "LREAL" => SyntaxKind.LRealKeyword,
            "TIME" => SyntaxKind.TimeKeyword,
            "LTIME" => SyntaxKind.LTimeKeyword,
            "DATE" => SyntaxKind.DateKeyword,
            "TOD" => SyntaxKind.TODKeyword,
            "TIME_OF_DAY" => SyntaxKind.TimeOfDayTypeKeyword,
            "DT" => SyntaxKind.DTKeyword,
            "DATE_AND_TIME" => SyntaxKind.DateAndTimeTypeKeyword,
            "MOD" => SyntaxKind.ModKeyword,
            "AND" => SyntaxKind.AndKeyword,
            "OR" => SyntaxKind.OrKeyword,
            "XOR" => SyntaxKind.XorKeyword,
            "NOT" => SyntaxKind.NotKeyword,
            "SHL" => SyntaxKind.ShlKeyword,
            "SHR" => SyntaxKind.ShrKeyword,
            "ROL" => SyntaxKind.RolKeyword,
            "ROR" => SyntaxKind.RorKeyword,
            "=>" => SyntaxKind.ArrowOperator,
            _ => SyntaxKind.Identifier
        };
    }
}
