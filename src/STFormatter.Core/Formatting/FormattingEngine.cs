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

        var writer = new FormattingWriter(_config);
        var visitor = new FormattingVisitor(writer, _config);

        // Find the StatementList inside the wrapper PROGRAM and format it directly
        var pouDecl = tree.Root.ChildNodes.FirstOrDefault();
        if (pouDecl != null)
        {
            var stmtList = pouDecl.ChildNodes.FirstOrDefault(n => n.Kind == SyntaxKind.StatementList);
            if (stmtList != null)
            {
                visitor.Visit(stmtList);
                var extracted = writer.ToString();
                extracted = StripCommonIndent(extracted);

                var inputUsesCrLf = body.Contains("\r\n");
                if (inputUsesCrLf && !extracted.Contains("\r\n"))
                    extracted = extracted.Replace("\n", "\r\n");

                return extracted;
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
            // Skip original whitespace/line breaks - formatter controls spacing
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
        return _config.MaxLineLength > 0 && _currentLineLength + additionalChars > _config.MaxLineLength;
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
        if (_config.MaxLineLength > 0 && !_atLineStart && _currentLineLength + estimatedLength > _config.MaxLineLength)
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
        if (_config.MaxLineLength > 0 && !_atLineStart && _currentLineLength + text.Length > _config.MaxLineLength)
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

        _writer.IndentLevel++;

        // Write variable sections
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

        // VAR keyword with optional modifiers
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

        _writer.IndentLevel++;

        // Compute alignment if enabled
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

        // END_VAR
        var endVar = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.EndVarKeyword);
        if (endVar != null)
        {
            WriteTokenWithCasing(endVar);
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
        if (_config.MaxLineLength > 0)
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

    private void VisitParenthesizedExpression(SyntaxNode node)
    {
        var tokens = node.ChildTokens.ToList();
        var nodes = node.ChildNodes.ToList();

        WriteToken(tokens[0]); // (
        Visit(nodes[0]);
        WriteToken(tokens[1]); // )
    }

    private void VisitType(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.NamedType:
                WriteTokenWithCasing(node.ChildTokens[0]);
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
        WriteTokenWithCasing(tokens[0]); // POINTER/REF/REFERENCE
        if (tokens.Count > 1)
        {
            _writer.WriteSpace();
            WriteTokenWithCasing(tokens[1]); // TO
            _writer.WriteSpace();
        }
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
            WriteToken(node.ChildTokens[0]); // := or =
            _writer.WriteSpace();
            Visit(node.ChildNodes[1]); // value
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
        foreach (var trivia in triviaList)
        {
            _writer.WriteTrivia(trivia);
            lastTrivia = trivia;
            // If this is a region pragma, ensure it gets its own line after
            if (IsRegionPragma(trivia))
            {
                _writer.EnsureNewLine();
            }
        }

        // If the last trivia was a region pragma, ensure we start on a new line
        if (lastTrivia != null && IsRegionPragma(lastTrivia))
        {
            _writer.EnsureNewLine();
        }
    }

    private void WriteTrailingTrivia(IEnumerable<SyntaxTrivia> triviaList)
    {
        foreach (var trivia in triviaList)
        {
            // If this is a region pragma, ensure it's on its own line
            if (IsRegionPragma(trivia))
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
