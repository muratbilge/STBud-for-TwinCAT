using System;
using System.Linq;
using System.Text;
using STFormatter.Core.Syntax;

namespace STFormatter.Core.Formatting;

public sealed class FormattingEngine
{
    private readonly FormattingConfiguration _config;

    public FormattingEngine(FormattingConfiguration? config = null)
    {
        _config = config ?? FormattingConfiguration.Default;
    }

    public string Format(SyntaxTree tree)
    {
        var writer = new FormattingWriter(_config);
        var visitor = new FormattingVisitor(writer, _config);
        visitor.Visit(tree.Root);
        return writer.ToString();
    }

    public string Format(string source)
    {
        var text = Text.SourceText.From(source);
        var parser = new Parsing.Parser(text);
        var tree = parser.Parse();
        return Format(tree);
    }

    public string FormatBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return body;

        var wrapper = $"PROGRAM __BODY_WRAPPER__\n{body}\nEND_PROGRAM";
        var text = Text.SourceText.From(wrapper);
        var parser = new Parsing.Parser(text);
        var tree = parser.Parse();

        // Emitting from a tree with parse errors silently drops the statements
        // the parser skipped during recovery; leave the body untouched instead.
        if (tree.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error))
            return body;

        var writer = new FormattingWriter(_config);
        var visitor = new FormattingVisitor(writer, _config);

        // Find the StatementList inside the wrapper PROGRAM and format it directly
        var pouDecl = tree.Root.ChildNodes.FirstOrDefault();
        if (pouDecl != null)
        {
            var varSections = pouDecl.ChildNodes.Where(n => n.Kind == SyntaxKind.VarSection).ToList();
            var stmtList = pouDecl.ChildNodes.FirstOrDefault(n => n.Kind == SyntaxKind.StatementList);

            if (stmtList != null && stmtList.ChildNodes.Any())
            {
                visitor.Visit(stmtList);
                EmitWrapperEndTrivia(pouDecl, visitor);
                var extracted = writer.ToString();
                extracted = StripCommonIndent(extracted);

                var inputUsesCrLf = body.Contains("\r\n");
                if (inputUsesCrLf && !extracted.Contains("\r\n"))
                    extracted = extracted.Replace("\n", "\r\n");

                return extracted;
            }

            if (varSections.Count > 0)
            {
                return body;
            }
        }

        // Fallback: format the whole wrapper and extract via string matching
        var formatted = Format(tree);
        string? extractedFallback = null;

        var markerStart = "PROGRAM __BODY_WRAPPER__\n";
        var markerEnd = "\nEND_PROGRAM";

        if (formatted.StartsWith(markerStart) && formatted.EndsWith(markerEnd))
        {
            extractedFallback = formatted.Substring(markerStart.Length, formatted.Length - markerStart.Length - markerEnd.Length);
        }

        if (extractedFallback == null)
            return body;

        extractedFallback = StripCommonIndent(extractedFallback);

        var usesCrLf = body.Contains("\r\n");
        if (usesCrLf && !extractedFallback.Contains("\r\n"))
            extractedFallback = extractedFallback.Replace("\n", "\r\n");

        return extractedFallback;
    }

    public string FormatDeclaration(string declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
            return declaration;

        // TYPE...END_TYPE declarations (DUTs: enums, structs, unions) are complete
        // compilation units; the synthetic VAR wrapper below would garble them.
        // Only emit when the parse is clean - error recovery drops content.
        if (ContainsTypeDeclaration(declaration))
        {
            var typeTree = new Parsing.Parser(Text.SourceText.From(declaration)).Parse();
            if (typeTree.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error))
                return declaration;
            return Format(typeTree);
        }

        string pouHeader;
        string varContent;
        SplitPouHeaderAndVars(declaration, out pouHeader, out varContent);

        if (string.IsNullOrWhiteSpace(varContent))
            return declaration;

        // A trailing END_PROGRAM/END_FUNCTION_BLOCK/... would duplicate the
        // synthetic wrapper's own terminator and break the parse; split it off
        // and re-append after formatting.
        string pouFooter = StripPouFooter(ref varContent);

        bool hasVarSection = HasVarSectionKeyword(varContent);
        if (!hasVarSection)
        {
            varContent = $"VAR_INPUT\n{varContent}\nEND_VAR";
        }

        var wrapper = $"PROGRAM __DECL_WRAPPER__\n{varContent}\nEND_PROGRAM";
        var text = Text.SourceText.From(wrapper);
        var parser = new Parsing.Parser(text);
        var tree = parser.Parse();

        // A wrapper that fails to parse would format to garbage (dropped
        // declarations); leave the original text untouched instead.
        if (tree.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error))
            return declaration;

        var writer = new FormattingWriter(_config);
        var visitor = new FormattingVisitor(writer, _config);

        var pouDecl = tree.Root.ChildNodes.FirstOrDefault();
        if (pouDecl != null)
        {
            var varSections = pouDecl.ChildNodes.Where(n => n.Kind == SyntaxKind.VarSection).ToList();
            if (varSections.Count > 0)
            {
                for (var i = 0; i < varSections.Count; i++)
                {
                    visitor.Visit(varSections[i]);
                    if (i < varSections.Count - 1)
                    {
                        if (_config.IsAllmanStyle())
                            writer.WriteNewLine(_config.EmptyLinesBetweenVarSections);
                        else
                            writer.EnsureNewLine();
                    }
                }

                EmitWrapperEndTrivia(pouDecl, visitor);
                var extracted = writer.ToString();
                extracted = StripCommonIndent(extracted);

                if (!hasVarSection)
                {
                    extracted = StripSyntheticVarWrapper(extracted);
                }

                if (!string.IsNullOrEmpty(pouHeader))
                    extracted = NormalizeNewLines(pouHeader, _config.GetNewLine()) + _config.GetNewLine() + extracted;
                if (!string.IsNullOrEmpty(pouFooter))
                    extracted = extracted.TrimEnd('\r', '\n') + _config.GetNewLine() + pouFooter;

                var inputUsesCrLf = declaration.Contains("\r\n");
                if (inputUsesCrLf && !extracted.Contains("\r\n"))
                    extracted = extracted.Replace("\n", "\r\n");

                return extracted;
            }
        }

        // Fallback: format the whole wrapper and extract via string matching
        var formatted = Format(tree);
        var markerStart = "PROGRAM __DECL_WRAPPER__\n";
        var markerEnd = "\nEND_PROGRAM";

        string? extractedFallback = null;
        if (formatted.StartsWith(markerStart) && formatted.EndsWith(markerEnd))
        {
            extractedFallback = formatted.Substring(markerStart.Length, formatted.Length - markerStart.Length - markerEnd.Length);
        }

        if (extractedFallback == null)
            return declaration;

        extractedFallback = StripCommonIndent(extractedFallback);

        if (!hasVarSection)
        {
            extractedFallback = StripSyntheticVarWrapper(extractedFallback);
        }

        if (!string.IsNullOrEmpty(pouHeader))
            extractedFallback = NormalizeNewLines(pouHeader, _config.GetNewLine()) + _config.GetNewLine() + extractedFallback;
        if (!string.IsNullOrEmpty(pouFooter))
            extractedFallback = extractedFallback.TrimEnd('\r', '\n') + _config.GetNewLine() + pouFooter;

        var usesCrLf = declaration.Contains("\r\n");
        if (usesCrLf && !extractedFallback.Contains("\r\n"))
            extractedFallback = extractedFallback.Replace("\n", "\r\n");

        return extractedFallback;
    }

    // Trailing comments/pragmas in body/declaration fragments end up as leading
    // trivia of the synthetic wrapper's END_PROGRAM token; emit them so they
    // survive formatting.
    private static void EmitWrapperEndTrivia(SyntaxNode pouDecl, FormattingVisitor visitor)
    {
        var endToken = pouDecl.ChildTokens
            .FirstOrDefault(t => t.Kind == SyntaxKind.EndProgramKeyword);
        if (endToken != null)
            visitor.EmitDanglingTrivia(endToken);
    }

    private static string NormalizeNewLines(string text, string newLine)
    {
        return text.Replace("\r\n", "\n").Replace("\n", newLine);
    }

    private static readonly string[] PouFooterKeywords = new[]
    {
        "END_PROGRAM", "END_FUNCTION_BLOCK", "END_FUNCTION", "END_METHOD",
        "END_PROPERTY", "END_ACTION", "END_INTERFACE"
    };

    private static string StripPouFooter(ref string varContent)
    {
        var lines = varContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int lastIdx = lines.Length - 1;
        while (lastIdx >= 0 && string.IsNullOrWhiteSpace(lines[lastIdx]))
            lastIdx--;
        if (lastIdx < 0)
            return "";

        var last = lines[lastIdx].Trim();
        foreach (var kw in PouFooterKeywords)
        {
            if (string.Equals(last, kw, StringComparison.OrdinalIgnoreCase))
            {
                var nl = varContent.Contains("\r\n") ? "\r\n" : "\n";
                varContent = string.Join(nl, lines.Take(lastIdx));
                return last;
            }
        }

        return "";
    }

    private static readonly string[] VarSectionKeywords = new[]
    {
        "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP", "VAR_STAT",
        "VAR_GLOBAL", "VAR_ACCESS", "VAR_EXTERNAL", "VAR_CONFIG", "VAR_INST", "VAR"
    };

    private static readonly System.Text.RegularExpressions.Regex TypeDeclarationRegex =
        new(@"(^|\n)\s*TYPE\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool ContainsTypeDeclaration(string text)
    {
        // Matches TYPE at the start of a line; END_TYPE does not match because
        // '_' is a word character.
        return TypeDeclarationRegex.IsMatch(text);
    }

    private static bool HasVarSectionKeyword(string text)
    {
        string upper = text.ToUpperInvariant();
        foreach (var kw in VarSectionKeywords)
        {
            if (upper.Contains(kw))
                return true;
        }
        return false;
    }

    private static string StripSyntheticVarWrapper(string extracted)
    {
        var lines = extracted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length < 3) return extracted;

        string first = lines[0].Trim();
        bool isVarStart = false;
        foreach (var kw in VarSectionKeywords)
        {
            if (first == kw || first.StartsWith(kw + " "))
            {
                isVarStart = true;
                break;
            }
        }
        if (!isVarStart) return extracted;

        int lastIdx = lines.Length - 1;
        while (lastIdx > 0 && string.IsNullOrWhiteSpace(lines[lastIdx]))
            lastIdx--;

        string last = lines[lastIdx].Trim();
        if (last != "END_VAR") return extracted;

        var innerLines = new List<string>();
        for (int i = 1; i < lastIdx; i++)
            innerLines.Add(lines[i]);

        var result = string.Join("\n", innerLines);
        result = StripCommonIndent(result);
        return result;
    }

    private static void SplitPouHeaderAndVars(string declaration, out string pouHeader, out string varContent)
    {
        pouHeader = "";
        varContent = declaration;

        var splits = declaration.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int varStartLine = -1;

        for (int i = 0; i < splits.Length; i++)
        {
            var trimmed = splits[i].TrimStart();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var upper = trimmed.ToUpperInvariant();
            bool isVarStart = false;
            foreach (var kw in VarSectionKeywords)
            {
                if (upper == kw || upper.StartsWith(kw + " "))
                {
                    isVarStart = true;
                    break;
                }
            }

            if (isVarStart)
            {
                varStartLine = i;
                break;
            }
        }

        if (varStartLine <= 0)
            return;

        var nl = declaration.Contains("\r\n") ? "\r\n" : "\n";
        pouHeader = string.Join(nl, splits.Take(varStartLine));
        varContent = string.Join(nl, splits.Skip(varStartLine));
    }

    private static string StripCommonIndent(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
                indent++;
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent == int.MaxValue || minIndent == 0)
            return text;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                sb.Append("\n");
            if (string.IsNullOrWhiteSpace(lines[i]))
                sb.Append(lines[i].Trim());
            else if (lines[i].Length >= minIndent)
                sb.Append(lines[i].Substring(minIndent));
            else
                sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    public static FormattingConfiguration LoadConfiguration(string? filePath = null)
    {
        var config = FormattingConfiguration.Default;

        if (!string.IsNullOrEmpty(filePath))
        {
            var editorConfig = Configuration.EditorConfigParser.LoadForFile(filePath!);
            if (editorConfig != null)
            {
                config = editorConfig;
            }
        }

        return config;
    }
}

internal sealed class FormattingWriter
{
    private readonly StringBuilder _builder;
    private readonly FormattingConfiguration _config;
    private int _indentLevel;
    private bool _atLineStart = true;
    private int _currentLineLength;

    public FormattingWriter(FormattingConfiguration config)
    {
        _builder = new StringBuilder();
        _config = config;
    }

    public int IndentLevel
    {
        get => _indentLevel;
        set => _indentLevel = Math.Max(0, value);
    }

    public bool AtLineStart => _atLineStart;

    public void UndoLastNewLineAndIndent()
    {
        var nl = _config.GetNewLine();
        int nlLen = nl.Length;
        if (_builder.Length < nlLen)
            return;

        int pos = _builder.Length;
        if (nlLen == 2 && pos >= 2 && _builder[pos - 2] == '\r' && _builder[pos - 1] == '\n')
            pos -= 2;
        else if (pos >= 1 && _builder[pos - 1] == '\n')
            pos -= 1;
        else
            return;

        while (pos > 0 && (_builder[pos - 1] == ' ' || _builder[pos - 1] == '\t'))
            pos--;

        _builder.Length = pos;
        _atLineStart = false;
        _currentLineLength = 0;
    }

    public void WriteToken(SyntaxToken token, string? overrideText = null)
    {
        var text = overrideText ?? token.Text;
        if (_atLineStart && !string.IsNullOrEmpty(text))
        {
            var indent = _config.GetIndentString(_indentLevel);
            _builder.Append(indent);
            _currentLineLength = indent.Length;
            _atLineStart = false;
        }

        _builder.Append(text);
        _currentLineLength += text.Length;
    }

    public void WriteSpace()
    {
        if (!_atLineStart)
        {
            _builder.Append(' ');
            _currentLineLength++;
        }
    }

    public void WriteNewLine()
    {
        _builder.Append(_config.GetNewLine());
        _atLineStart = true;
        _currentLineLength = 0;
    }

    public void WriteNewLine(int count)
    {
        for (var i = 0; i < count; i++)
            WriteNewLine();
    }

    public void WriteTrivia(SyntaxTrivia trivia)
    {
        if (trivia.IsLineBreak || trivia.IsWhitespace)
        {
            return;
        }
        else if (trivia.IsComment || trivia.IsPragma)
        {
            if (_atLineStart)
            {
                var indent = _config.GetIndentString(_indentLevel);
                _builder.Append(indent);
                _atLineStart = false;
            }
            else if (_builder.Length > 0 && _builder[_builder.Length - 1] != ' ' && _builder[_builder.Length - 1] != '\t')
            {
                _builder.Append(' ');
                _currentLineLength++;
            }

            _builder.Append(trivia.Text);
            _currentLineLength += trivia.Text.Length;

            if (trivia.Kind == SyntaxKind.SingleLineCommentTrivia)
                EnsureNewLine();
        }
        else
        {
            _builder.Append(trivia.Text);
            _currentLineLength += trivia.Text.Length;
        }
    }

    public void EnsureSpace()
    {
        if (_builder.Length > 0 && !_atLineStart)
        {
            char last = _builder[_builder.Length - 1];
            if (last != ' ' && last != '\t')
            {
                WriteSpace();
            }
        }
    }

    public void EnsureNewLine()
    {
        if (!_atLineStart)
        {
            WriteNewLine();
        }
    }

    public int CurrentLineLength => _currentLineLength;

    public bool WouldExceedLineLength(int additionalChars)
    {
        return _config.EffectiveMaxLineLength > 0 && _currentLineLength + additionalChars > _config.EffectiveMaxLineLength;
    }

    public void WriteSpaces(int count)
    {
        if (count > 0)
        {
            _builder.Append(new string(' ', count));
            _currentLineLength += count;
        }
    }

    public void WriteLineBreakIfNeeded(int estimatedLength)
    {
        if (_config.EffectiveMaxLineLength > 0 && !_atLineStart && _currentLineLength + estimatedLength > _config.EffectiveMaxLineLength)
        {
            WriteNewLine();
            var continuationLevel = _indentLevel + Math.Max(1, _config.ContinuationIndentSize / Math.Max(1, _config.IndentSize));
            var continuationIndent = _config.GetIndentString(continuationLevel);
            _builder.Append(continuationIndent);
            _currentLineLength = continuationIndent.Length;
            _atLineStart = false;
        }
    }

    public void WriteTokenWithLineBreakCheck(string text)
    {
        if (_config.EffectiveMaxLineLength > 0 && !_atLineStart && _currentLineLength + text.Length > _config.EffectiveMaxLineLength)
        {
            WriteNewLine();
            var continuationLevel = _indentLevel + Math.Max(1, _config.ContinuationIndentSize / Math.Max(1, _config.IndentSize));
            var continuationIndent = _config.GetIndentString(continuationLevel);
            _builder.Append(continuationIndent);
            _currentLineLength = continuationIndent.Length;
            _atLineStart = false;
        }
        WriteToken(null!, text);
    }

    public override string ToString() => _builder.ToString();
}

internal sealed class FormattingVisitor
{
    private readonly FormattingWriter _writer;
    private readonly FormattingConfiguration _config;

    public FormattingVisitor(FormattingWriter writer, FormattingConfiguration config)
    {
        _writer = writer;
        _config = config;
    }

    // Emits comments/pragmas that belong to a token the caller is not going to
    // visit (e.g. trivia attached to a synthetic wrapper's END_PROGRAM).
    public void EmitDanglingTrivia(SyntaxToken token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.IsComment || trivia.IsPragma)
            {
                _writer.EnsureNewLine();
                _writer.WriteTrivia(trivia);
                _writer.EnsureNewLine();
            }
        }
    }

    public void Visit(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.CompilationUnit:
                VisitCompilationUnit(node);
                break;
            case SyntaxKind.ProgramDeclaration:
            case SyntaxKind.FunctionBlockDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.ActionDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TransitionDeclaration:
            case SyntaxKind.StepDeclaration:
                VisitPouDeclaration(node);
                break;
            case SyntaxKind.VarSection:
                VisitVarSection(node);
                break;
            case SyntaxKind.VariableDeclaration:
                VisitVariableDeclaration(node);
                break;
            case SyntaxKind.StatementList:
                VisitStatementList(node);
                break;
            case SyntaxKind.IfStatement:
                VisitIfStatement(node);
                break;
            case SyntaxKind.CaseStatement:
                VisitCaseStatement(node);
                break;
            case SyntaxKind.ForStatement:
                VisitForStatement(node);
                break;
            case SyntaxKind.WhileStatement:
                VisitWhileStatement(node);
                break;
            case SyntaxKind.RepeatStatement:
                VisitRepeatStatement(node);
                break;
            case SyntaxKind.TryStatement:
                VisitTryStatement(node);
                break;
            case SyntaxKind.AssignmentStatement:
            case SyntaxKind.OutputAssignmentStatement:
                VisitAssignmentStatement(node);
                break;
            case SyntaxKind.CallStatement:
                VisitCallStatement(node);
                break;
            case SyntaxKind.ExitStatement:
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.ReturnStatement:
                VisitSimpleStatement(node);
                break;
            case SyntaxKind.GotoStatement:
                VisitGotoStatement(node);
                break;
            case SyntaxKind.LabelStatement:
                VisitLabelStatement(node);
                break;
            case SyntaxKind.BinaryExpression:
                VisitBinaryExpression(node);
                break;
            case SyntaxKind.UnaryExpression:
                VisitUnaryExpression(node);
                break;
            case SyntaxKind.MemberAccessExpression:
                VisitMemberAccessExpression(node);
                break;
            case SyntaxKind.DereferenceExpression:
                Visit(node.ChildNodes[0]);
                WriteToken(node.ChildTokens[0]); // ^
                break;
            case SyntaxKind.ArrayInitializer:
                VisitArrayInitializer(node);
                break;
            case SyntaxKind.ElementAccessExpression:
                VisitElementAccessExpression(node);
                break;
            case SyntaxKind.InvocationExpression:
                VisitInvocationExpression(node);
                break;
            case SyntaxKind.ParenthesizedExpression:
                VisitParenthesizedExpression(node);
                break;
            case SyntaxKind.LiteralExpression:
            case SyntaxKind.IdentifierExpression:
            case SyntaxKind.DirectVariableExpression:
                VisitExpression(node);
                break;
            case SyntaxKind.NamedType:
            case SyntaxKind.ArrayType:
            case SyntaxKind.StructuredType:
            case SyntaxKind.EnumerationType:
            case SyntaxKind.StringType:
            case SyntaxKind.PointerType:
            case SyntaxKind.ReferenceType:
            case SyntaxKind.UnionType:
                VisitType(node);
                break;
            case SyntaxKind.ArrayRange:
                VisitArrayRange(node);
                break;
            case SyntaxKind.VariableInitializer:
                VisitVariableInitializer(node);
                break;
            case SyntaxKind.AtClause:
                VisitAtClause(node);
                break;
            case SyntaxKind.Argument:
            case SyntaxKind.NamedArgument:
                VisitArgument(node);
                break;
            case SyntaxKind.TypeDeclaration:
                VisitTypeDeclaration(node);
                break;
            case SyntaxKind.UsingDirective:
                VisitUsingDirective(node);
                break;
            case SyntaxKind.ElsIfClause:
                VisitElsIfClause(node);
                break;
            case SyntaxKind.ElseClause:
                VisitElseClause(node);
                break;
            case SyntaxKind.CaseClause:
                VisitCaseClause(node);
                break;
            case SyntaxKind.ElseCaseClause:
                VisitElseCaseClause(node);
                break;
            case SyntaxKind.CatchClause:
                VisitCatchClause(node);
                break;
            case SyntaxKind.FinallyClause:
                VisitFinallyClause(node);
                break;
            case SyntaxKind.EmptyStatement:
                VisitEmptyStatement(node);
                break;
            case SyntaxKind.ExtendsClause:
            case SyntaxKind.ImplementsClause:
                VisitExtendsOrImplementsClause(node);
                break;
            case SyntaxKind.ForByClause:
                VisitForByClause(node);
                break;
            default:
                VisitDefault(node);
                break;
        }
    }

    private void VisitCompilationUnit(SyntaxNode node)
    {
        var declarations = node.ChildNodes.ToList();
        var sep = _config.IsAllmanStyle()
            ? Math.Max(1, _config.EmptyLinesBetweenPOUs)
            : Math.Max(1, _config.EmptyLinesBetweenPOUs - 1);
        for (var i = 0; i < declarations.Count; i++)
        {
            Visit(declarations[i]);
            if (i < declarations.Count - 1)
            {
                _writer.WriteNewLine(sep);
            }
        }
    }

    private void VisitPouDeclaration(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        // Write access modifier if present
        var tokenIndex = 0;
        if (tokens.Count > 0 && IsAccessModifier(tokens[0].Kind))
        {
            WriteTokenWithCasing(tokens[0]);
            _writer.WriteSpace();
            tokenIndex++;
        }

        // Write POU keyword and name
        if (tokenIndex < tokens.Count)
        {
            WriteTokenWithCasing(tokens[tokenIndex++]); // PROGRAM/FUNCTION_BLOCK/etc
            _writer.WriteSpace();
        }
        // TwinCAT modifiers after the keyword: METHOD PROTECTED Foo, PROPERTY PUBLIC Bar
        while (tokenIndex < tokens.Count &&
               (IsAccessModifier(tokens[tokenIndex].Kind) ||
                tokens[tokenIndex].Kind is SyntaxKind.FinalKeyword
                    or SyntaxKind.AbstractKeyword or SyntaxKind.OverrideKeyword))
        {
            WriteTokenWithCasing(tokens[tokenIndex++]);
            _writer.WriteSpace();
        }
        if (tokenIndex < tokens.Count && tokens[tokenIndex].Kind == SyntaxKind.Identifier)
        {
            WriteToken(tokens[tokenIndex++]); // name
        }

        // Write extends/implements
        foreach (var child in nodes)
        {
            if (child.Kind is SyntaxKind.ExtendsClause or SyntaxKind.ImplementsClause)
            {
                _writer.WriteSpace();
                Visit(child);
            }
        }

        // Write return type for functions
        if (tokenIndex < tokens.Count && tokens[tokenIndex].Kind == SyntaxKind.Colon)
        {
            _writer.WriteSpace();
            WriteToken(tokens[tokenIndex++]); // :
            _writer.WriteSpace();

            // Visit return type node
            var returnType = nodes.FirstOrDefault(n => IsTypeNode(n.Kind));
            if (returnType != null)
            {
                Visit(returnType);
            }
        }

        _writer.EnsureNewLine();

        // Write variable sections at POU level (no extra indent)
        var varSections = nodes.Where(n => n.Kind == SyntaxKind.VarSection).ToList();
        for (var i = 0; i < varSections.Count; i++)
        {
            Visit(varSections[i]);
            if (i < varSections.Count - 1)
            {
                if (_config.IsAllmanStyle())
                    _writer.WriteNewLine(_config.EmptyLinesBetweenVarSections);
                else
                    _writer.EnsureNewLine();
            }
        }

        _writer.IndentLevel++;

        var bodyNode = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.StatementList);
        var hasBodyStatements = bodyNode != null && bodyNode.ChildNodes.Length > 0;
        if (varSections.Count > 0 && hasBodyStatements)
        {
            if (_config.IsAllmanStyle())
                _writer.WriteNewLine();
            else
                _writer.EnsureNewLine();
        }

        // Write body
        var body = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.StatementList);
        if (body != null)
        {
            Visit(body);
        }

        _writer.IndentLevel--;
        _writer.EnsureNewLine();

        // Write END_xxx
        while (tokenIndex < tokens.Count)
        {
            if (tokens[tokenIndex].Kind.ToString().StartsWith("End"))
            {
                WriteTokenWithCasing(tokens[tokenIndex]);
                tokenIndex++;
                if (tokenIndex < tokens.Count && tokens[tokenIndex].Kind == SyntaxKind.Identifier)
                {
                    _writer.WriteSpace();
                    WriteToken(tokens[tokenIndex]);
                    tokenIndex++;
                }
            }
            else
            {
                tokenIndex++;
            }
        }

        _writer.EnsureNewLine();
    }

    private void VisitVarSection(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var declarations = node.ChildNodes.ToList();
        bool isEmpty = !declarations.Any(d => d.Kind == SyntaxKind.VariableDeclaration);

        WriteTokenWithCasing(tokens[0]);
        for (var i = 1; i < tokens.Count; i++)
        {
            if (IsVarModifier(tokens[i].Kind))
            {
                _writer.WriteSpace();
                WriteTokenWithCasing(tokens[i]);
            }
        }
        _writer.EnsureNewLine();

        if (isEmpty)
        {
            var endVar = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.EndVarKeyword);
            if (endVar != null)
            {
                WriteTokenWithCasing(endVar);
                _writer.EnsureNewLine();
            }
            return;
        }

        _writer.IndentLevel++;

        var alignment = _config.AlignVariableDeclarations ? ComputeVarAlignment(declarations) : null;

        foreach (var decl in declarations)
        {
            if (alignment != null)
            {
                VisitVariableDeclarationAligned(decl, alignment);
            }
            else
            {
                Visit(decl);
            }
        }
        _writer.IndentLevel--;

        var endVar2 = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.EndVarKeyword);
        if (endVar2 != null)
        {
            WriteTokenWithCasing(endVar2);
            _writer.EnsureNewLine();
        }
    }

    private record VarAlignmentInfo(int MaxNameWidth, int MaxTypeWidth);

    private VarAlignmentInfo? ComputeVarAlignment(List<SyntaxNode> declarations)
    {
        int maxNameWidth = 0;
        int maxTypeWidth = 0;
        bool hasAnyInitializer = false;

        foreach (var decl in declarations)
        {
            if (decl.Kind != SyntaxKind.VariableDeclaration) continue;

            var tokens = decl.ChildTokens.ToList();
            var nodes = decl.ChildNodes.ToList();

            // Name width
            var nameToken = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.Identifier);
            int nameWidth = nameToken?.Text.Length ?? 0;

            // AT clause width
            var atClause = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.AtClause);
            if (atClause != null)
            {
                nameWidth += 1 + atClause.ToStringWithoutTrivia().Length;
            }

            maxNameWidth = Math.Max(maxNameWidth, nameWidth);

            // Type width
            var type = nodes.FirstOrDefault(n => IsTypeNode(n.Kind));
            if (type != null)
            {
                int typeWidth = type.ToStringWithoutTrivia().Length;
                maxTypeWidth = Math.Max(maxTypeWidth, typeWidth);
            }

            // Check if any declaration has an initializer
            var init = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.VariableInitializer);
            if (init != null)
                hasAnyInitializer = true;
        }

        // Only align type width if there are initializers to align
        if (!hasAnyInitializer)
            maxTypeWidth = 0;

        return new VarAlignmentInfo(maxNameWidth, maxTypeWidth);
    }

    private void VisitVariableDeclarationAligned(SyntaxNode node, VarAlignmentInfo alignment)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        // Name
        var nameToken = tokens.First(t => t.Kind == SyntaxKind.Identifier);
        WriteToken(nameToken);

        // AT clause
        var atClause = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.AtClause);
        if (atClause != null)
        {
            _writer.WriteSpace();
            Visit(atClause);
        }

        // Pad to align colon
        var nameWidth = nameToken.Text.Length + (atClause != null ? 1 + atClause.ToStringWithoutTrivia().Length : 0);
        var namePadding = alignment.MaxNameWidth - nameWidth;
        _writer.WriteSpaces(namePadding + 1); // +1 for single space before colon

        // :
        WriteToken(tokens.First(t => t.Kind == SyntaxKind.Colon));
        if (_config.SpaceAfterColon)
            _writer.WriteSpace();

        // Type
        var type = nodes.FirstOrDefault(n => IsTypeNode(n.Kind));
        if (type != null)
        {
            Visit(type);
        }

        // Initializer (only pad type if this declaration has one)
        var init = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.VariableInitializer);
        if (init != null)
        {
            // Pad to align :=
            var typeWidth = type?.ToStringWithoutTrivia().Length ?? 0;
            var typePadding = alignment.MaxTypeWidth - typeWidth;
            _writer.WriteSpaces(typePadding);

            _writer.WriteSpace();
            Visit(init);
        }

        // ;
        WriteSemicolon(tokens.First(t => t.Kind == SyntaxKind.Semicolon));
        _writer.EnsureNewLine();
    }

    private void VisitVariableDeclaration(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        // Name
        WriteToken(tokens.First(t => t.Kind == SyntaxKind.Identifier));

        // AT clause
        var atClause = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.AtClause);
        if (atClause != null)
        {
            _writer.WriteSpace();
            Visit(atClause);
        }

        _writer.WriteSpace();

        // :
        WriteToken(tokens.First(t => t.Kind == SyntaxKind.Colon));
        if (_config.SpaceAfterColon)
            _writer.WriteSpace();

        // Type
        var type = nodes.FirstOrDefault(n => IsTypeNode(n.Kind));
        if (type != null)
        {
            Visit(type);
        }

        // Initializer
        var init = nodes.FirstOrDefault(n => n.Kind == SyntaxKind.VariableInitializer);
        if (init != null)
        {
            _writer.WriteSpace();
            Visit(init);
        }

        // ;
        WriteSemicolon(tokens.First(t => t.Kind == SyntaxKind.Semicolon));
        _writer.EnsureNewLine();
    }

    private void VisitTypeDeclaration(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // TYPE
        _writer.WriteNewLine();
        _writer.IndentLevel++;

        WriteToken(tokens[1]); // name
        _writer.WriteSpace();
        WriteToken(tokens[2]); // :
        if (_config.SpaceAfterColon)
            _writer.WriteSpace();

        var type = nodes.FirstOrDefault(n => IsTypeNode(n.Kind));
        if (type != null)
            Visit(type);

        var semicolon = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.Semicolon);
        if (semicolon != null)
        {
            WriteSemicolon(semicolon);
        }
        _writer.EnsureNewLine();
        _writer.IndentLevel--;

        var endType = tokens.First(t => t.Kind == SyntaxKind.EndTypeKeyword);
        WriteTokenWithCasing(endType); // END_TYPE
        _writer.EnsureNewLine();
    }

    private void VisitStatementList(SyntaxNode node)
    {
        var statements = node.ChildNodes.ToList();

        // If alignment is enabled, group consecutive assignments
        if (_config.AlignAssignments)
        {
            VisitStatementListWithAlignment(statements);
        }
        else
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var stmt = statements[i];

                // Skip empty statements that immediately follow block statements
                if (stmt.Kind == SyntaxKind.EmptyStatement && i > 0 && IsBlockStatement(statements[i - 1].Kind))
                {
                    continue;
                }

                Visit(stmt);
            }
        }
    }

    private void VisitStatementListWithAlignment(List<SyntaxNode> statements)
    {
        var i = 0;
        while (i < statements.Count)
        {
            var stmt = statements[i];

            // Skip empty statements that immediately follow block statements
            if (stmt.Kind == SyntaxKind.EmptyStatement && i > 0 && IsBlockStatement(statements[i - 1].Kind))
            {
                i++;
                continue;
            }

            // Check if this starts an assignment group
            if (IsAssignmentStatement(stmt.Kind))
            {
                // Collect consecutive assignments
                var assignmentGroup = new List<SyntaxNode> { stmt };
                var j = i + 1;
                while (j < statements.Count && IsAssignmentStatement(statements[j].Kind))
                {
                    assignmentGroup.Add(statements[j]);
                    j++;
                }

                if (assignmentGroup.Count >= 2)
                {
                    // Compute alignment
                    var maxLeftWidth = 0;
                    foreach (var assignment in assignmentGroup)
                    {
                        var leftWidth = GetAssignmentLeftWidth(assignment);
                        maxLeftWidth = Math.Max(maxLeftWidth, leftWidth);
                    }

                    foreach (var assignment in assignmentGroup)
                    {
                        VisitAssignmentStatementAligned(assignment, maxLeftWidth);
                    }

                    i = j;
                    continue;
                }
            }

            Visit(stmt);
            i++;
        }
    }

    private static bool IsAssignmentStatement(SyntaxKind kind)
    {
        return kind is SyntaxKind.AssignmentStatement or SyntaxKind.OutputAssignmentStatement;
    }

    private static int GetAssignmentLeftWidth(SyntaxNode assignment)
    {
        var nodes = assignment.ChildNodes.ToList();
        if (nodes.Count == 0) return 0;
        return nodes[0].ToStringWithoutTrivia().Length;
    }

    private void VisitAssignmentStatementAligned(SyntaxNode node, int maxLeftWidth)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]); // left side

        // Pad to align :=
        var leftWidth = nodes[0].ToStringWithoutTrivia().Length;
        var padding = maxLeftWidth - leftWidth;
        _writer.WriteSpaces(padding);

        _writer.WriteSpace();
        WriteToken(tokens[0]); // := or =>
        _writer.WriteSpace();
        Visit(nodes[1]); // right side
        WriteSemicolon(tokens[1]); // ;
        _writer.EnsureNewLine();
    }

    private static bool IsBlockStatement(SyntaxKind kind)
    {
        return kind is SyntaxKind.IfStatement or SyntaxKind.CaseStatement
            or SyntaxKind.ForStatement or SyntaxKind.WhileStatement
            or SyntaxKind.RepeatStatement or SyntaxKind.TryStatement;
    }

    private static bool IsSingleStatementBody(SyntaxNode body)
    {
        if (body.Kind != SyntaxKind.StatementList) return false;
        var children = body.ChildNodes.ToList();
        if (children.Count != 1) return false;
        var stmt = children[0];
        // Don't collapse if the single statement is itself a block
        return !IsBlockStatement(stmt.Kind) && stmt.Kind != SyntaxKind.EmptyStatement;
    }

    private void VisitIfStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // IF
        _writer.WriteSpace();
        Visit(nodes[0]); // condition
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[1]); // THEN

        var thenBody = nodes[1];
        bool keepSingleLine = _config.KeepSingleLineBlocks && IsSingleStatementBody(thenBody) &&
                              !nodes.Any(n => n.Kind == SyntaxKind.ElsIfClause || n.Kind == SyntaxKind.ElseClause);

        if (keepSingleLine)
        {
            _writer.WriteSpace();
            _writer.IndentLevel++;
            Visit(thenBody);
            _writer.IndentLevel--;
            _writer.WriteSpace();
            WriteTokenWithCasing(tokens[2]); // END_IF
            _writer.EnsureNewLine();
            return;
        }

        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(thenBody); // then body
        _writer.IndentLevel--;

        var index = 2;
        while (index < nodes.Count && nodes[index].Kind == SyntaxKind.ElsIfClause)
        {
            Visit(nodes[index]);
            index++;
        }

        if (index < nodes.Count && nodes[index].Kind == SyntaxKind.ElseClause)
        {
            Visit(nodes[index]);
            index++;
        }

        WriteTokenWithCasing(tokens[2]); // END_IF
        _writer.EnsureNewLine();
    }

    private void VisitCaseStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // CASE
        _writer.WriteSpace();
        Visit(nodes[0]); // selector
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[1]); // OF
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        var index = 1;
        while (index < nodes.Count && nodes[index].Kind == SyntaxKind.CaseClause)
        {
            Visit(nodes[index]);
            index++;
        }
        _writer.IndentLevel--;

        if (index < nodes.Count && nodes[index].Kind == SyntaxKind.ElseCaseClause)
        {
            Visit(nodes[index]);
            index++;
        }

        WriteTokenWithCasing(tokens[2]); // END_CASE
        _writer.EnsureNewLine();
    }

    private void VisitForStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // FOR
        _writer.WriteSpace();
        WriteToken(tokens[1]); // variable
        _writer.WriteSpace();
        WriteToken(tokens[2]); // :=
        _writer.WriteSpace();
        Visit(nodes[0]); // from
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[3]); // TO
        _writer.WriteSpace();
        Visit(nodes[1]); // to

        if (nodes.Count > 3 && nodes[2].Kind == SyntaxKind.ForByClause)
        {
            _writer.WriteSpace();
            Visit(nodes[2]);
        }

        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[4]); // DO

        var bodyIndex = nodes.Count > 3 && nodes[2].Kind == SyntaxKind.ForByClause ? 3 : 2;
        var forBody = nodes[bodyIndex];
        bool keepSingleLine = _config.KeepSingleLineBlocks && IsSingleStatementBody(forBody);

        if (keepSingleLine)
        {
            _writer.WriteSpace();
            _writer.IndentLevel++;
            Visit(forBody);
            _writer.IndentLevel--;
            _writer.WriteSpace();
            WriteTokenWithCasing(tokens[5]); // END_FOR
            _writer.EnsureNewLine();
            return;
        }

        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(forBody); // body
        _writer.IndentLevel--;

        WriteTokenWithCasing(tokens[5]); // END_FOR
        _writer.EnsureNewLine();
    }

    private void VisitWhileStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // WHILE
        _writer.WriteSpace();
        Visit(nodes[0]); // condition
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[1]); // DO

        bool keepSingleLine = _config.KeepSingleLineBlocks && IsSingleStatementBody(nodes[1]);

        if (keepSingleLine)
        {
            _writer.WriteSpace();
            _writer.IndentLevel++;
            Visit(nodes[1]);
            _writer.IndentLevel--;
            _writer.WriteSpace();
            WriteTokenWithCasing(tokens[2]); // END_WHILE
            _writer.EnsureNewLine();
            return;
        }

        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[1]); // body
        _writer.IndentLevel--;

        WriteTokenWithCasing(tokens[2]); // END_WHILE
        _writer.EnsureNewLine();
    }

    private void VisitRepeatStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // REPEAT
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;

        WriteTokenWithCasing(tokens[1]); // UNTIL
        _writer.WriteSpace();
        Visit(nodes[1]); // condition
        _writer.EnsureNewLine();

        WriteTokenWithCasing(tokens[2]); // END_REPEAT
        _writer.EnsureNewLine();
    }

    private void VisitTryStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // __TRY
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;

        var index = 1;
        while (index < nodes.Count && nodes[index].Kind == SyntaxKind.CatchClause)
        {
            Visit(nodes[index]);
            index++;
        }

        if (index < nodes.Count && nodes[index].Kind == SyntaxKind.FinallyClause)
        {
            Visit(nodes[index]);
            index++;
        }

        WriteTokenWithCasing(tokens[1]); // __ENDTRY
        _writer.EnsureNewLine();
    }

    private void VisitAssignmentStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]); // left
        _writer.WriteSpace();
        WriteToken(tokens[0]); // := or =>
        _writer.WriteSpace();
        Visit(nodes[1]); // right
        WriteSemicolon(tokens[1]); // ;
        _writer.EnsureNewLine();
    }

    private void VisitCallStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]); // call expression
        WriteSemicolon(tokens[0]); // ;
        _writer.EnsureNewLine();
    }

    private void VisitSimpleStatement(SyntaxNode node)
    {
        WriteTokenWithCasing(node.ChildTokens[0]);
        WriteSemicolon(node.ChildTokens[1]); // ;
        _writer.EnsureNewLine();
    }

    private void VisitGotoStatement(SyntaxNode node)
    {
        WriteTokenWithCasing(node.ChildTokens[0]); // GOTO
        _writer.WriteSpace();
        WriteToken(node.ChildTokens[1]); // label
        WriteSemicolon(node.ChildTokens[2]); // ;
        _writer.EnsureNewLine();
    }

    private void VisitEmptyStatement(SyntaxNode node)
    {
        WriteSemicolon(node.ChildTokens[0]); // ;
        _writer.EnsureNewLine();
    }

    private void VisitLabelStatement(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteToken(tokens[0]); // label name
        WriteToken(tokens[1]); // :
        _writer.WriteSpace();
        Visit(nodes[0]); // labeled statement
    }

    private void VisitUsingDirective(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        WriteTokenWithCasing(tokens[0]); // USING
        _writer.WriteSpace();
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == SyntaxKind.Dot)
            {
                WriteToken(tokens[i]);
            }
            else if (tokens[i].Kind == SyntaxKind.Identifier)
            {
                WriteToken(tokens[i]);
            }
            else if (tokens[i].Kind == SyntaxKind.Semicolon)
            {
                WriteSemicolon(tokens[i]);
            }
        }
        _writer.EnsureNewLine();
    }

    private void VisitExpression(SyntaxNode node)
    {
        WriteToken(node.ChildTokens[0]);
    }

    private void VisitBinaryExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]);

        if (_config.SpaceAroundOperators)
            _writer.WriteSpace();

        // Check if operator would exceed line length
        if (_config.EffectiveMaxLineLength > 0)
        {
            _writer.WriteLineBreakIfNeeded(tokens[0].Text.Length + (_config.SpaceAroundOperators ? 1 : 0) + EstimateNodeLength(nodes[1]));
        }

        WriteTokenWithCasing(tokens[0]);

        if (_config.SpaceAroundOperators)
            _writer.WriteSpace();

        Visit(nodes[1]);
    }

    private void VisitUnaryExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]);
        if (_config.SpaceAroundOperators)
            _writer.WriteSpace();
        Visit(nodes[0]);
    }

    private void VisitMemberAccessExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]);
        WriteToken(tokens[0]); // .
        WriteToken(tokens[1]); // member
    }

    private void VisitElementAccessExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]);
        WriteToken(tokens[0]); // [
        Visit(nodes[1]);
        WriteToken(tokens[1]); // ]
    }

    private void VisitInvocationExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        Visit(nodes[0]); // function
        WriteToken(tokens[0]); // (

        for (var i = 1; i < nodes.Count; i++)
        {
            if (i > 1)
            {
                WriteToken(tokens[i - 1]); // comma
                if (_config.SpaceAfterComma)
                    _writer.WriteSpace();
            }
            Visit(nodes[i]);
        }

        WriteToken(tokens[tokens.Count - 1]); // )
    }

    private void VisitArrayInitializer(SyntaxNode node)
    {
        // [a, b, c] - tokens are [ commas... ]
        var tokens = node.ChildTokens.ToList();
        var elements = node.ChildNodes.ToList();

        WriteToken(tokens[0]); // [
        for (var i = 0; i < elements.Count; i++)
        {
            if (i > 0)
            {
                WriteToken(tokens[i]); // comma
                if (_config.SpaceAfterComma)
                    _writer.WriteSpace();
            }
            Visit(elements[i]);
        }
        WriteToken(tokens[tokens.Count - 1]); // ]
    }

    private void VisitParenthesizedExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteToken(tokens[0]); // (
        Visit(nodes[0]);
        WriteToken(tokens[1]); // )
    }

    private void VisitExtendsOrImplementsClause(SyntaxNode node)
    {
        // EXTENDS/IMPLEMENTS Name(.Part)* (, Name(.Part)*)*
        var tokens = node.ChildTokens.ToList();
        WriteTokenWithCasing(tokens[0]); // EXTENDS / IMPLEMENTS
        _writer.WriteSpace();
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == SyntaxKind.Comma)
            {
                WriteToken(tokens[i]);
                if (_config.SpaceAfterComma)
                    _writer.WriteSpace();
            }
            else
            {
                WriteToken(tokens[i]); // identifier or dot, written tight
            }
        }
    }

    private void VisitForByClause(SyntaxNode node)
    {
        WriteTokenWithCasing(node.ChildTokens[0]); // BY
        _writer.WriteSpace();
        Visit(node.ChildNodes[0]); // step expression
    }

    private void VisitType(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.NamedType:
                // All name-part tokens (identifiers and dots) written tight:
                // INT, TcoCore.ITcoTask
                foreach (var token in node.ChildTokens)
                    WriteTokenWithCasing(token);
                break;
            case SyntaxKind.ArrayType:
                VisitArrayType(node);
                break;
            case SyntaxKind.StructuredType:
                VisitStructuredType(node);
                break;
            case SyntaxKind.UnionType:
                VisitUnionType(node);
                break;
            case SyntaxKind.EnumerationType:
                VisitEnumerationType(node);
                break;
            case SyntaxKind.StringType:
                VisitStringType(node);
                break;
            case SyntaxKind.PointerType:
            case SyntaxKind.ReferenceType:
                VisitPointerOrReferenceType(node);
                break;
            default:
                VisitDefault(node);
                break;
        }
    }

    private void VisitArrayType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        // tokens: ARRAY, [, commas..., ], OF
        // nodes: ranges..., elementType
        var rangeCount = nodes.Count - 1; // last node is element type
        var commaCount = rangeCount > 1 ? rangeCount - 1 : 0;

        WriteTokenWithCasing(tokens[0]); // ARRAY
        WriteToken(tokens[1]); // [

        for (var i = 0; i < rangeCount; i++)
        {
            if (i > 0)
            {
                var commaIndex = 2 + (i - 1);
                WriteToken(tokens[commaIndex]); // comma
                if (_config.SpaceAfterComma)
                    _writer.WriteSpace();
            }
            Visit(nodes[i]); // range
        }

        WriteToken(tokens[2 + commaCount]); // ]
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[3 + commaCount]); // OF
        _writer.WriteSpace();
        Visit(nodes[nodes.Count - 1]); // element type
    }

    private void VisitArrayRange(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        WriteToken(tokens[0]);
        WriteToken(tokens[1]); // ..
        WriteToken(tokens[2]);
    }

    private void VisitStructuredType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // STRUCT
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        foreach (var member in nodes)
        {
            Visit(member);
        }
        _writer.IndentLevel--;

        if (!_config.IsAllmanStyle() && tokens.Count > 1 && tokens[1].Kind != SyntaxKind.EndStructKeyword)
        {
            _writer.EnsureSpace();
        }
        WriteTokenWithCasing(tokens[tokens.Count - 1]); // END_STRUCT
    }

    private void VisitUnionType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // UNION
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        foreach (var member in nodes)
        {
            Visit(member);
        }
        _writer.IndentLevel--;

        WriteTokenWithCasing(tokens[tokens.Count - 1]); // END_UNION
    }

    private void VisitEnumerationType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        // Paren form: (A, B := 1, C) [baseType] - tokens are ( commas... )
        if (tokens[0].Kind == SyntaxKind.OpenParen)
        {
            VisitParenEnumType(tokens, nodes);
            return;
        }

        WriteTokenWithCasing(tokens[0]); // ENUM
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                WriteToken(tokens[1 + i]); // comma - approximate
                if (_config.IsAllmanStyle())
                {
                    _writer.EnsureNewLine();
                }
                else
                {
                    _writer.WriteSpace();
                }
            }
            Visit(nodes[i]);
        }
        _writer.IndentLevel--;

        _writer.EnsureNewLine();
        WriteTokenWithCasing(tokens[tokens.Count - 1]); // END_ENUM
    }

    private void VisitParenEnumType(List<SyntaxToken> tokens, List<SyntaxNode> nodes)
    {
        // Last node may be the optional base type: (A, B) USINT
        var baseType = nodes.Count > 0 && nodes[nodes.Count - 1].Kind == SyntaxKind.NamedType
            ? nodes[nodes.Count - 1] : null;
        var valueCount = baseType != null ? nodes.Count - 1 : nodes.Count;

        // Preserve the author's layout: members that were on separate lines (or
        // carry comments/pragmas) stay one-per-line; compact enums stay compact.
        // The decision is derivable from the formatted output too, keeping
        // formatting idempotent.
        bool multiline = false;
        for (var i = 0; i < valueCount && !multiline; i++)
        {
            foreach (var trivia in nodes[i].ChildTokens[0].LeadingTrivia)
            {
                if (trivia.IsLineBreak || trivia.IsComment || trivia.IsPragma)
                {
                    multiline = true;
                    break;
                }
            }
        }

        WriteToken(tokens[0]); // (
        if (multiline)
        {
            _writer.EnsureNewLine();
            _writer.IndentLevel++;
            for (var i = 0; i < valueCount; i++)
            {
                VisitEnumValue(nodes[i]);
                if (i < valueCount - 1)
                    WriteToken(tokens[1 + i]); // comma
                _writer.EnsureNewLine();
            }
            _writer.IndentLevel--;
        }
        else
        {
            for (var i = 0; i < valueCount; i++)
            {
                if (i > 0)
                {
                    WriteToken(tokens[i]); // comma
                    if (_config.SpaceAfterComma)
                        _writer.WriteSpace();
                }
                VisitEnumValue(nodes[i]);
            }
        }
        WriteToken(tokens[tokens.Count - 1]); // )

        if (baseType != null)
        {
            _writer.WriteSpace();
            Visit(baseType);
        }
    }

    private void VisitEnumValue(SyntaxNode node)
    {
        WriteToken(node.ChildTokens[0]); // name

        var init = node.ChildNodes.FirstOrDefault(n => n.Kind == SyntaxKind.EnumValueInitializer);
        if (init != null)
        {
            if (_config.SpaceAroundOperators)
                _writer.WriteSpace();
            WriteToken(init.ChildTokens[0]); // := or =
            if (_config.SpaceAroundOperators)
                _writer.WriteSpace();
            Visit(init.ChildNodes[0]); // value
        }
    }

    private void VisitStringType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        WriteTokenWithCasing(tokens[0]); // STRING/WSTRING
        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    private void VisitPointerOrReferenceType(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        WriteTokenWithCasing(tokens[0]); // POINTER/REF_TO/REFERENCE
        if (tokens.Count > 1)
        {
            _writer.WriteSpace();
            WriteTokenWithCasing(tokens[1]); // TO
        }
        _writer.WriteSpace(); // REF_TO has no TO token but still needs the separator
        Visit(node.ChildNodes[0]);
    }

    private void VisitVariableInitializer(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        WriteToken(tokens[0]); // :=
        _writer.WriteSpace();
        Visit(node.ChildNodes[0]);
    }

    private void VisitAtClause(SyntaxNode node)
    {
        foreach (var token in node.ChildTokens)
        {
            WriteTokenWithCasing(token);
            _writer.WriteSpace();
        }
    }

    private void VisitArgument(SyntaxNode node)
    {
        if (node.Kind == SyntaxKind.NamedArgument)
        {
            Visit(node.ChildNodes[0]); // name
            _writer.WriteSpace();
            WriteToken(node.ChildTokens[0]); // := or = or =>
            if (node.ChildNodes.Length > 1)
            {
                _writer.WriteSpace();
                Visit(node.ChildNodes[1]); // value (optional for =>)
            }
        }
        else
        {
            Visit(node.ChildNodes[0]);
        }
    }

    private void VisitElsIfClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // ELSIF
        _writer.WriteSpace();
        Visit(nodes[0]); // condition
        _writer.WriteSpace();
        WriteTokenWithCasing(tokens[1]); // THEN
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[1]); // body
        _writer.IndentLevel--;
    }

    private void VisitElseClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // ELSE
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;
    }

    private void VisitCaseClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        var valueCount = nodes.Count - 1; // last node is body
        var commaCount = valueCount > 1 ? valueCount - 1 : 0;

        // Values (all except last node which is body)
        for (var i = 0; i < valueCount; i++)
        {
            if (i > 0)
            {
                WriteToken(tokens[i - 1]); // comma
                _writer.WriteSpace();
            }
            Visit(nodes[i]);
        }

        WriteToken(tokens[commaCount]); // :
        _writer.WriteSpace();

        _writer.IndentLevel++;
        Visit(nodes[nodes.Count - 1]); // body
        _writer.IndentLevel--;
    }

    private void VisitElseCaseClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // ELSE
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;
    }

    private void VisitCatchClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // CATCH
        if (tokens.Count > 1)
        {
            _writer.WriteSpace();
            WriteToken(tokens[1]); // exception variable
        }
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;
    }

    private void VisitFinallyClause(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteTokenWithCasing(tokens[0]); // FINALLY
        _writer.EnsureNewLine();

        _writer.IndentLevel++;
        Visit(nodes[0]); // body
        _writer.IndentLevel--;
    }

    private void VisitDefault(SyntaxNode node)
    {
        foreach (var token in node.ChildTokens)
        {
            WriteTokenWithCasing(token);
        }
        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    private void WriteToken(SyntaxToken token)
    {
        WriteLeadingTrivia(token.LeadingTrivia);
        _writer.WriteToken(token);
        WriteTrailingTrivia(token.TrailingTrivia);
    }

    private void WriteSemicolon(SyntaxToken token)
    {
        WriteLeadingTrivia(token.LeadingTrivia);
        if (_config.SpaceBeforeSemicolon)
            _writer.WriteSpace();
        _writer.WriteToken(token);
        WriteTrailingTrivia(token.TrailingTrivia);
    }

    private void WriteTokenWithCasing(SyntaxToken token)
    {
        WriteLeadingTrivia(token.LeadingTrivia);
        var text = IsKeyword(token.Kind) ? _config.FormatKeyword(token.Text) : token.Text;
        _writer.WriteToken(token, text);
        WriteTrailingTrivia(token.TrailingTrivia);
    }

    private void WriteLeadingTrivia(IEnumerable<SyntaxTrivia> triviaList)
    {
        SyntaxTrivia? lastTrivia = null;
        bool prevWasLineBreak = false;
        foreach (var trivia in triviaList)
        {
            if (trivia.IsLineBreak)
            {
                prevWasLineBreak = true;
                lastTrivia = trivia;
                continue;
            }

            if (trivia.IsWhitespace)
            {
                // Indentation between a line break and a comment doesn't make the
                // comment "inline" - keep the line-break state so own-line
                // comments stay on their own line.
                lastTrivia = trivia;
                continue;
            }

            if (prevWasLineBreak && (trivia.IsComment || trivia.IsPragma))
            {
                _writer.EnsureNewLine();
            }

            // A comment with no line break before it in the source was at the end
            // of the previous line - pull it back up instead of gluing it to the
            // next token.
            bool pulledInline = false;
            if (!prevWasLineBreak && _writer.AtLineStart &&
                (trivia.Kind == SyntaxKind.SingleLineCommentTrivia ||
                 trivia.Kind == SyntaxKind.MultiLineCommentTrivia))
            {
                _writer.UndoLastNewLineAndIndent();
                _writer.WriteSpace();
                pulledInline = trivia.Kind == SyntaxKind.MultiLineCommentTrivia;
            }

            var wasLineBreak = prevWasLineBreak;
            prevWasLineBreak = false;
            _writer.WriteTrivia(trivia);
            lastTrivia = trivia;

            if (trivia.Kind == SyntaxKind.MultiLineCommentTrivia && (wasLineBreak || pulledInline))
            {
                _writer.EnsureNewLine();
            }

            if (trivia.IsPragma)
            {
                _writer.EnsureNewLine();
            }
        }

        if (lastTrivia != null && lastTrivia.IsPragma)
        {
            _writer.EnsureNewLine();
        }
    }

    private void WriteTrailingTrivia(IEnumerable<SyntaxTrivia> triviaList)
    {
        foreach (var trivia in triviaList)
        {
            if (trivia.IsPragma)
            {
                _writer.EnsureNewLine();
            }
            _writer.WriteTrivia(trivia);
        }
    }

    private static bool IsRegionPragma(SyntaxTrivia trivia)
    {
        if (!trivia.IsPragma) return false;
        var text = trivia.Text.ToLowerInvariant();
        return text.Contains("region") || text.Contains("endregion");
    }

    private static bool IsKeyword(SyntaxKind kind)
    {
        return kind.ToString().EndsWith("Keyword") || kind.ToString().EndsWith("Operator");
    }

    private static bool IsAccessModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword;
    }

    private static bool IsVarModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.ConstantKeyword or SyntaxKind.RetainKeyword
            or SyntaxKind.PersistentKeyword or SyntaxKind.ReadOnlyKeyword
            or SyntaxKind.ReadWriteKeyword;
    }

    private static bool IsTypeNode(SyntaxKind kind)
    {
        return kind is SyntaxKind.NamedType or SyntaxKind.ArrayType
            or SyntaxKind.StructuredType or SyntaxKind.EnumerationType
            or SyntaxKind.StringType or SyntaxKind.PointerType
            or SyntaxKind.ReferenceType or SyntaxKind.SubrangeType
            or SyntaxKind.UnionType;
    }

    private static int EstimateNodeLength(SyntaxNode node)
    {
        // Rough estimate of node length for line wrapping decisions
        return node.ToStringWithoutTrivia().Length;
    }
}
