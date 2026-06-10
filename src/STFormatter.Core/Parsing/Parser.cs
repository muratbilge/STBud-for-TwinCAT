using System.Collections.Immutable;
using STFormatter.Core.Syntax;
using STFormatter.Core.Text;

namespace STFormatter.Core.Parsing;

public sealed class Parser
{
    private readonly SourceText _text;
    private readonly SyntaxToken[] _tokens;
    private readonly List<Diagnostic> _diagnostics;
    private int _position;

    public Parser(SourceText text)
    {
        _text = text;
        _diagnostics = new List<Diagnostic>();

        var lexer = new Lexing.Lexer(text);
        var tokens = new List<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        } while (token.Kind != SyntaxKind.EndOfFile);

        _tokens = tokens.ToArray();
        _diagnostics.AddRange(lexer.Diagnostics);
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    private SyntaxToken Current => Peek(0);

    private SyntaxToken Peek(int offset)
    {
        var index = _position + offset;
        if (index >= _tokens.Length)
            return _tokens[_tokens.Length - 1];
        return _tokens[index];
    }

    private SyntaxToken NextToken()
    {
        var token = Current;
        _position++;
        return token;
    }

    private SyntaxToken MatchToken(SyntaxKind kind)
    {
        if (Current.Kind == kind)
            return NextToken();

        _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            Current.Span,
            $"Expected '{GetKindText(kind)}' but found '{Current.Text}'"));

        return SyntaxFactory.Token(kind, string.Empty, Current.Span.Start);
    }

    // Users name variables/enum members after soft keywords (dt, tod, time,
    // Reference, ...). A keyword token counts as a declaration name when the
    // token after it confirms the name position - then it is consumed and
    // re-typed as an identifier so downstream code treats it normally.
    private bool IsDeclarationNameStart()
    {
        if (Current.Kind == SyntaxKind.Identifier)
            return true;
        if (!IsWordToken(Current))
            return false;
        var next = Peek(1).Kind;
        return next == SyntaxKind.Colon || next == SyntaxKind.AtKeyword;
    }

    private SyntaxToken MatchDeclarationName()
    {
        if (Current.Kind == SyntaxKind.Identifier)
            return NextToken();

        if (IsWordToken(Current))
        {
            var token = NextToken();
            return new SyntaxToken(SyntaxKind.Identifier, token.Text, token.Span,
                token.LeadingTrivia, token.TrailingTrivia);
        }

        return MatchToken(SyntaxKind.Identifier);
    }

    private static bool IsWordToken(SyntaxToken token)
    {
        if (string.IsNullOrEmpty(token.Text))
            return false;
        foreach (var c in token.Text)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }
        return char.IsLetter(token.Text[0]) || token.Text[0] == '_';
    }

    public SyntaxTree Parse()
    {
        var start = Current.Span.Start;
        var declarations = new List<SyntaxNode>();
        var tokens = new List<SyntaxToken>();

        while (Current.Kind != SyntaxKind.EndOfFile)
        {
            if (Current.Kind == SyntaxKind.UsingKeyword)
            {
                declarations.Add(ParseUsingDirective());
            }
            else if (IsPouStart(Current.Kind))
            {
                declarations.Add(ParsePouDeclaration());
            }
            else if (Current.Kind == SyntaxKind.TypeKeyword)
            {
                declarations.Add(ParseTypeDeclaration());
            }
            else
            {
                // Skip unexpected tokens and try to recover
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    Current.Span,
                    $"Unexpected token '{Current.Text}' at top level"));
                tokens.Add(NextToken());
            }
        }

        tokens.Add(MatchToken(SyntaxKind.EndOfFile));

        var end = Current.Span.End;
        var span = TextSpan.FromBounds(start, end);
        var root = SyntaxFactory.CompilationUnit(span, declarations, tokens);

        return SyntaxTree.Create(_text, root, _diagnostics);
    }

    private static bool IsPouStart(SyntaxKind kind)
    {
        return kind is SyntaxKind.ProgramKeyword
            or SyntaxKind.FunctionBlockKeyword
            or SyntaxKind.FunctionKeyword
            or SyntaxKind.MethodKeyword
            or SyntaxKind.PropertyKeyword
            or SyntaxKind.ActionKeyword
            or SyntaxKind.TransitionKeyword
            or SyntaxKind.StepKeyword
            or SyntaxKind.InterfaceKeyword;
    }

    private SyntaxNode ParsePouDeclaration()
    {
        return Current.Kind switch
        {
            SyntaxKind.ProgramKeyword => ParseProgram(),
            SyntaxKind.FunctionBlockKeyword => ParseFunctionBlock(),
            SyntaxKind.FunctionKeyword => ParseFunction(),
            SyntaxKind.MethodKeyword => ParseMethod(),
            SyntaxKind.PropertyKeyword => ParseProperty(),
            SyntaxKind.ActionKeyword => ParseAction(),
            SyntaxKind.TransitionKeyword => ParseTransition(),
            SyntaxKind.StepKeyword => ParseStep(),
            SyntaxKind.InterfaceKeyword => ParseInterface(),
            _ => throw new InvalidOperationException($"Unexpected POU start: {Current.Kind}")
        };
    }

    private SyntaxNode ParseProgram()
    {
        var start = Current.Span.Start;
        var programKeyword = MatchToken(SyntaxKind.ProgramKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var attributes = ParseOptionalAttributes();
        var varSections = ParseVarSections();
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndProgramKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        return SyntaxFactory.Node(SyntaxKind.ProgramDeclaration, span,
            attributes.Concat(varSections).Concat(new[] { body }),
            new[] { programKeyword, name, endKeyword }.Concat(endName != null ? new[] { endName } : Array.Empty<SyntaxToken>()));
    }

    private SyntaxNode ParseFunctionBlock()
    {
        var start = Current.Span.Start;
        var fbKeyword = MatchToken(SyntaxKind.FunctionBlockKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var extends = ParseOptionalExtends();
        var implements = ParseOptionalImplements();
        var attributes = ParseOptionalAttributes();
        var varSections = ParseVarSections();
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndFunctionBlockKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        children.AddRange(attributes);
        if (extends != null) children.Add(extends);
        if (implements != null) children.Add(implements);
        children.AddRange(varSections);
        children.Add(body);

        var tokens = new List<SyntaxToken> { fbKeyword, name, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.FunctionBlockDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseFunction()
    {
        var start = Current.Span.Start;
        var funcKeyword = MatchToken(SyntaxKind.FunctionKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var colon = MatchToken(SyntaxKind.Colon);
        var returnType = ParseType();
        var varSections = ParseVarSections();
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndFunctionKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        children.AddRange(varSections);
        children.Add(returnType);
        children.Add(body);

        var tokens = new List<SyntaxToken> { funcKeyword, name, colon, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.FunctionDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseMethod()
    {
        var start = Current.Span.Start;
        var access = TryMatchAccessModifier();
        var methodKeyword = MatchToken(SyntaxKind.MethodKeyword);
        var modifiers = MatchPostKeywordModifiers(); // METHOD PROTECTED ABSTRACT Foo
        var name = MatchToken(SyntaxKind.Identifier);

        SyntaxToken? colon = null;
        SyntaxNode? returnType = null;
        if (Current.Kind == SyntaxKind.Colon)
        {
            colon = MatchToken(SyntaxKind.Colon);
            returnType = ParseType();
        }

        var varSections = ParseVarSections();
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndMethodKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        children.AddRange(varSections);
        if (returnType != null) children.Add(returnType);
        children.Add(body);

        var tokens = new List<SyntaxToken> { methodKeyword };
        tokens.AddRange(modifiers);
        tokens.Add(name);
        if (colon != null) tokens.Add(colon);
        tokens.Add(endKeyword);
        if (access != null) tokens.Insert(0, access);
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.MethodDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseProperty()
    {
        var start = Current.Span.Start;
        var access = TryMatchAccessModifier();
        var propKeyword = MatchToken(SyntaxKind.PropertyKeyword);
        var modifiers = MatchPostKeywordModifiers(); // PROPERTY PUBLIC P_Info
        var name = MatchToken(SyntaxKind.Identifier);
        var colon = MatchToken(SyntaxKind.Colon);
        var type = ParseType();
        var varSections = ParseVarSections();
        var getters = ParsePropertyAccessors();
        var endKeyword = MatchToken(SyntaxKind.EndPropertyKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode> { type };
        children.AddRange(varSections);
        children.AddRange(getters);

        var tokens = new List<SyntaxToken> { propKeyword };
        tokens.AddRange(modifiers);
        tokens.Add(name);
        tokens.Add(colon);
        tokens.Add(endKeyword);
        if (access != null) tokens.Insert(0, access);
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.PropertyDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseAction()
    {
        var start = Current.Span.Start;
        var actionKeyword = MatchToken(SyntaxKind.ActionKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndActionKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var tokens = new List<SyntaxToken> { actionKeyword, name, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.ActionDeclaration, span, new[] { body }, tokens);
    }

    private SyntaxNode ParseTransition()
    {
        var start = Current.Span.Start;
        var transitionKeyword = MatchToken(SyntaxKind.TransitionKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var colon = MatchToken(SyntaxKind.Colon);
        var fromExpr = ParseExpression();
        var toKeyword = MatchToken(SyntaxKind.ToKeyword);
        var toExpr = ParseExpression();
        var semicolon = MatchToken(SyntaxKind.Semicolon);
        var endKeyword = MatchToken(SyntaxKind.EndTransitionKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode> { fromExpr, toExpr };
        var tokens = new List<SyntaxToken> { transitionKeyword, name, colon, toKeyword, semicolon, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.TransitionDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseStep()
    {
        var start = Current.Span.Start;
        var stepKeyword = MatchToken(SyntaxKind.StepKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var varSections = ParseVarSections();
        var body = ParseStatementList();
        var endKeyword = MatchToken(SyntaxKind.EndStepKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        children.AddRange(varSections);
        children.Add(body);

        var tokens = new List<SyntaxToken> { stepKeyword, name, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.StepDeclaration, span, children, tokens);
    }

    private SyntaxNode ParseInterface()
    {
        var start = Current.Span.Start;
        var interfaceKeyword = MatchToken(SyntaxKind.InterfaceKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var extends = ParseOptionalExtends();
        var methods = ParseInterfaceMethods();
        var endKeyword = MatchToken(SyntaxKind.EndInterfaceKeyword);
        var endName = TryMatchIdentifier();

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        if (extends != null) children.Add(extends);
        children.AddRange(methods);

        var tokens = new List<SyntaxToken> { interfaceKeyword, name, endKeyword };
        if (endName != null) tokens.Add(endName);

        return SyntaxFactory.Node(SyntaxKind.InterfaceDeclaration, span, children, tokens);
    }

    private SyntaxNode? ParseOptionalExtends()
    {
        if (Current.Kind != SyntaxKind.ExtendsKeyword)
            return null;

        // Base names may be namespace-qualified (EXTENDS TcUnit.FB_TestSuite)
        // and interfaces may extend several bases (EXTENDS I_A, I_B).
        var tokens = new List<SyntaxToken> { NextToken() }; // EXTENDS
        AddDottedName(tokens);
        while (Current.Kind == SyntaxKind.Comma)
        {
            tokens.Add(NextToken()); // comma
            AddDottedName(tokens);
        }

        return SyntaxFactory.Node(SyntaxKind.ExtendsClause,
            TextSpan.FromBounds(tokens[0].Span.Start, tokens[tokens.Count - 1].Span.End),
            tokens.ToArray());
    }

    private SyntaxNode? ParseOptionalImplements()
    {
        if (Current.Kind != SyntaxKind.ImplementsKeyword)
            return null;

        var tokens = new List<SyntaxToken> { NextToken() }; // IMPLEMENTS
        AddDottedName(tokens);
        while (Current.Kind == SyntaxKind.Comma)
        {
            tokens.Add(NextToken()); // comma
            AddDottedName(tokens);
        }

        return SyntaxFactory.Node(SyntaxKind.ImplementsClause,
            TextSpan.FromBounds(tokens[0].Span.Start, tokens[tokens.Count - 1].Span.End),
            ImmutableArray<SyntaxNode>.Empty,
            tokens);
    }

    // Appends Identifier (. Identifier)* to the token list.
    private void AddDottedName(List<SyntaxToken> tokens)
    {
        tokens.Add(MatchToken(SyntaxKind.Identifier));
        while (Current.Kind == SyntaxKind.Dot)
        {
            tokens.Add(NextToken()); // dot
            tokens.Add(MatchToken(SyntaxKind.Identifier));
        }
    }

    private SyntaxNode? ParseOptionalReturnType()
    {
        if (Current.Kind != SyntaxKind.Colon)
            return null;

        var colon = NextToken();
        var type = ParseType();
        return type;
    }

    private ImmutableArray<SyntaxNode> ParsePropertyAccessors()
    {
        var accessors = new List<SyntaxNode>();

        while (Current.Kind is SyntaxKind.GetKeyword or SyntaxKind.SetKeyword)
        {
            var start = Current.Span.Start;
            var keyword = NextToken();
            var body = ParseStatementList();
            var endKeyword = MatchToken(SyntaxKind.EndPropertyKeyword); // Actually properties use END_PROPERTY for both, but in TwinCAT each accessor has its own body
            // For simplicity, treat until END_PROPERTY

            var span = TextSpan.FromBounds(start, body.Span.End);
            accessors.Add(SyntaxFactory.Node(
                keyword.Kind == SyntaxKind.GetKeyword ? SyntaxKind.GetAccessor : SyntaxKind.SetAccessor,
                span, new[] { body }, new[] { keyword }));
        }

        return accessors.ToImmutableArray();
    }

    private ImmutableArray<SyntaxNode> ParseInterfaceMethods()
    {
        var methods = new List<SyntaxNode>();
        while (Current.Kind == SyntaxKind.MethodKeyword)
        {
            methods.Add(ParseMethodSignature());
        }
        return methods.ToImmutableArray();
    }

    private SyntaxNode ParseMethodSignature()
    {
        var start = Current.Span.Start;
        var methodKeyword = MatchToken(SyntaxKind.MethodKeyword);
        var name = MatchToken(SyntaxKind.Identifier);

        SyntaxToken? colon = null;
        SyntaxNode? returnType = null;
        if (Current.Kind == SyntaxKind.Colon)
        {
            colon = MatchToken(SyntaxKind.Colon);
            returnType = ParseType();
        }

        var varSections = ParseVarSections();
        var endKeyword = MatchToken(SyntaxKind.EndMethodKeyword);

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>();
        children.AddRange(varSections);
        if (returnType != null) children.Add(returnType);

        var tokens = new List<SyntaxToken> { methodKeyword, name };
        if (colon != null) tokens.Add(colon);
        tokens.Add(endKeyword);

        return SyntaxFactory.Node(SyntaxKind.MethodDeclaration, span, children, tokens);
    }

    private ImmutableArray<SyntaxNode> ParseOptionalAttributes()
    {
        var attributes = new List<SyntaxNode>();

        while (Current.Kind == SyntaxKind.OpenBrace)
        {
            // Pragma/attribute trivia was already consumed by lexer as leading trivia
            // But standalone attributes like {attribute '...'} may appear as tokens if not in trivia
            // For now, attributes in braces before declarations are typically pragmas handled as trivia
            break;
        }

        return attributes.ToImmutableArray();
    }

    private SyntaxToken? TryMatchAccessModifier()
    {
        return Current.Kind switch
        {
            SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
                or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword => NextToken(),
            _ => null
        };
    }

    // TwinCAT places modifiers after the POU keyword: METHOD PROTECTED ABSTRACT Foo,
    // PROPERTY PUBLIC Bar. Collect any run of access/inheritance modifiers.
    private List<SyntaxToken> MatchPostKeywordModifiers()
    {
        var modifiers = new List<SyntaxToken>();
        while (Current.Kind is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
               or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword
               or SyntaxKind.FinalKeyword or SyntaxKind.AbstractKeyword
               or SyntaxKind.OverrideKeyword)
        {
            modifiers.Add(NextToken());
        }
        return modifiers;
    }

    private SyntaxToken? TryMatchIdentifier()
    {
        if (Current.Kind == SyntaxKind.Identifier)
            return NextToken();
        return null;
    }

    private List<SyntaxNode> ParseVarSections()
    {
        var sections = new List<SyntaxNode>();

        while (IsVarSectionStart(Current.Kind))
        {
            sections.Add(ParseVarSection());
        }

        return sections;
    }

    private static bool IsVarSectionStart(SyntaxKind kind)
    {
        return kind is SyntaxKind.VarKeyword
            or SyntaxKind.VarInputKeyword
            or SyntaxKind.VarOutputKeyword
            or SyntaxKind.VarInOutKeyword
            or SyntaxKind.VarTempKeyword
            or SyntaxKind.VarStatKeyword
            or SyntaxKind.VarGlobalKeyword
            or SyntaxKind.VarAccessKeyword
            or SyntaxKind.VarExternalKeyword
            or SyntaxKind.VarConfigKeyword
            or SyntaxKind.VarInstKeyword;
    }

    private SyntaxNode ParseVarSection()
    {
        var start = Current.Span.Start;
        var varKeyword = NextToken();
        var modifiers = ParseVarModifiers();
        var declarations = new List<SyntaxNode>();

        while (IsDeclarationNameStart())
        {
            declarations.Add(ParseVarDeclaration());
        }

        var endKeyword = MatchToken(SyntaxKind.EndVarKeyword);
        var span = TextSpan.FromBounds(start, endKeyword.Span.End);

        return SyntaxFactory.Node(SyntaxKind.VarSection, span, declarations,
            new[] { varKeyword, endKeyword }.Concat(modifiers));
    }

    private List<SyntaxToken> ParseVarModifiers()
    {
        var modifiers = new List<SyntaxToken>();
        while (Current.Kind is SyntaxKind.ConstantKeyword
            or SyntaxKind.RetainKeyword
            or SyntaxKind.PersistentKeyword
            or SyntaxKind.ReadOnlyKeyword
            or SyntaxKind.ReadWriteKeyword)
        {
            modifiers.Add(NextToken());
        }
        return modifiers;
    }

    private SyntaxNode ParseVarDeclaration()
    {
        var start = Current.Span.Start;
        var name = MatchDeclarationName();
        var atClause = ParseOptionalAtClause();
        var colon = MatchToken(SyntaxKind.Colon);
        var type = ParseType();
        var initializer = ParseOptionalInitializer();
        var semicolon = MatchToken(SyntaxKind.Semicolon);

        var end = semicolon.Span.End;
        var span = TextSpan.FromBounds(start, end);

        var children = new List<SyntaxNode> { type };
        if (atClause != null) children.Insert(0, atClause);
        if (initializer != null) children.Add(initializer);

        var tokens = new List<SyntaxToken> { name, colon, semicolon };

        return SyntaxFactory.Node(SyntaxKind.VariableDeclaration, span, children, tokens);
    }

    private SyntaxNode? ParseOptionalAtClause()
    {
        if (Current.Kind != SyntaxKind.AtKeyword)
            return null;

        var atKeyword = NextToken();
        var variable = MatchToken(SyntaxKind.DirectVariable);
        return SyntaxFactory.Node(SyntaxKind.AtClause, TextSpan.FromBounds(atKeyword.Span.Start, variable.Span.End),
            new[] { atKeyword, variable });
    }

    private SyntaxNode? ParseOptionalInitializer()
    {
        if (Current.Kind != SyntaxKind.AssignmentOperator)
            return null;

        var assign = NextToken();
        var value = ParseExpression();
        return SyntaxFactory.Node(SyntaxKind.VariableInitializer,
            TextSpan.FromBounds(assign.Span.Start, value.Span.End),
            new[] { value }, new[] { assign });
    }

    private SyntaxNode ParseType()
    {
        var start = Current.Span.Start;

        if (Current.Kind == SyntaxKind.ArrayKeyword)
        {
            return ParseArrayType();
        }

        if (Current.Kind == SyntaxKind.StructKeyword)
        {
            return ParseStructType();
        }

        if (Current.Kind == SyntaxKind.UnionKeyword)
        {
            return ParseUnionType();
        }

        if (Current.Kind == SyntaxKind.EnumKeyword)
        {
            return ParseEnumType();
        }

        // Standard IEC enum form without the ENUM keyword:
        // TYPE E : (A, B := 1, C) [baseType]; END_TYPE  (also inline in VAR sections)
        if (Current.Kind == SyntaxKind.OpenParen)
        {
            return ParseParenEnumType();
        }

        if (Current.Kind == SyntaxKind.StringKeyword || Current.Kind == SyntaxKind.WStringKeyword)
        {
            return ParseStringType();
        }

        if (Current.Kind == SyntaxKind.PointerKeyword || Current.Kind == SyntaxKind.RefToKeyword)
        {
            return ParsePointerType();
        }

        if (Current.Kind == SyntaxKind.ReferenceKeyword)
        {
            return ParseReferenceType();
        }

        // Simple or user-defined type, possibly namespace-qualified
        // (TcoCore.ITcoTask) - keep all name parts as tokens.
        var nameTokens = new List<SyntaxToken> { NextToken() };
        while (Current.Kind == SyntaxKind.Dot)
        {
            nameTokens.Add(NextToken()); // dot
            nameTokens.Add(MatchToken(SyntaxKind.Identifier));
        }
        var span = TextSpan.FromBounds(start, nameTokens[nameTokens.Count - 1].Span.End);
        return SyntaxFactory.Node(SyntaxKind.NamedType, span, nameTokens.ToArray());
    }

    private SyntaxNode ParseArrayType()
    {
        var start = Current.Span.Start;
        var arrayKeyword = MatchToken(SyntaxKind.ArrayKeyword);
        var openBracket = MatchToken(SyntaxKind.OpenBracket);
        var ranges = new List<SyntaxNode>();
        var commas = new List<SyntaxToken>();
        ranges.Add(ParseRange());

        while (Current.Kind == SyntaxKind.Comma)
        {
            commas.Add(NextToken());
            ranges.Add(ParseRange());
        }

        var closeBracket = MatchToken(SyntaxKind.CloseBracket);
        var ofKeyword = MatchToken(SyntaxKind.OfKeyword);
        var elementType = ParseType();

        var span = TextSpan.FromBounds(start, elementType.Span.End);
        var tokens = new List<SyntaxToken> { arrayKeyword, openBracket };
        tokens.AddRange(commas);
        tokens.Add(closeBracket);
        tokens.Add(ofKeyword);

        return SyntaxFactory.Node(SyntaxKind.ArrayType, span,
            ranges.Concat(new[] { elementType }),
            tokens);
    }

    private SyntaxNode ParseRange()
    {
        var start = Current.Span.Start;
        var from = MatchToken(SyntaxKind.NumericLiteral);
        var dotDot = MatchToken(SyntaxKind.DotDot);
        var to = MatchToken(SyntaxKind.NumericLiteral);
        var span = TextSpan.FromBounds(start, to.Span.End);
        return SyntaxFactory.Node(SyntaxKind.ArrayRange, span,
            new[] { from, dotDot, to });
    }

    private SyntaxNode ParseStructType()
    {
        var start = Current.Span.Start;
        var structKeyword = MatchToken(SyntaxKind.StructKeyword);
        var elements = new List<SyntaxNode>();

        while (IsDeclarationNameStart())
        {
            elements.Add(ParseVarDeclaration());
        }

        var endKeyword = MatchToken(SyntaxKind.EndStructKeyword);
        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        return SyntaxFactory.Node(SyntaxKind.StructuredType, span, elements,
            new[] { structKeyword, endKeyword });
    }

    private SyntaxNode ParseUnionType()
    {
        var start = Current.Span.Start;
        var unionKeyword = MatchToken(SyntaxKind.UnionKeyword);
        var elements = new List<SyntaxNode>();

        while (IsDeclarationNameStart())
        {
            elements.Add(ParseVarDeclaration());
        }

        var endKeyword = MatchToken(SyntaxKind.EndUnionKeyword);
        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        return SyntaxFactory.Node(SyntaxKind.UnionType, span, elements,
            new[] { unionKeyword, endKeyword });
    }

    private SyntaxNode ParseEnumType()
    {
        var start = Current.Span.Start;
        var enumKeyword = MatchToken(SyntaxKind.EnumKeyword);
        var baseType = TryParseEnumBaseType();
        var values = new List<SyntaxNode>();

        if (Current.Kind == SyntaxKind.Identifier || Current.Kind == SyntaxKind.OpenParen)
        {
            values.Add(ParseEnumValue());
            while (Current.Kind == SyntaxKind.Comma)
            {
                NextToken(); // comma
                values.Add(ParseEnumValue());
            }
        }

        var endKeyword = MatchToken(SyntaxKind.EndEnumKeyword);
        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var children = new List<SyntaxNode>(values);
        if (baseType != null) children.Insert(0, baseType);

        return SyntaxFactory.Node(SyntaxKind.EnumerationType, span, children,
            new[] { enumKeyword, endKeyword });
    }

    private SyntaxNode ParseParenEnumType()
    {
        var start = Current.Span.Start;
        var tokens = new List<SyntaxToken> { MatchToken(SyntaxKind.OpenParen) };

        var values = new List<SyntaxNode>();
        if (IsEnumValueStart())
        {
            values.Add(ParseEnumValue());
            while (Current.Kind == SyntaxKind.Comma)
            {
                tokens.Add(NextToken()); // comma
                values.Add(ParseEnumValue());
            }
        }

        var closeParen = MatchToken(SyntaxKind.CloseParen);
        tokens.Add(closeParen);

        // Optional base type after the closing paren: TYPE E : (A, B) USINT; END_TYPE
        SyntaxNode? baseType = null;
        if (Current.Kind == SyntaxKind.Identifier || IsElementaryTypeKeyword(Current.Kind))
        {
            var typeName = NextToken();
            baseType = SyntaxFactory.Node(SyntaxKind.NamedType, typeName.Span, new[] { typeName });
        }

        var end = baseType?.Span.End ?? closeParen.Span.End;
        var children = new List<SyntaxNode>(values);
        if (baseType != null) children.Add(baseType);

        return SyntaxFactory.Node(SyntaxKind.EnumerationType,
            TextSpan.FromBounds(start, end), children, tokens);
    }

    private static bool IsElementaryTypeKeyword(SyntaxKind kind)
    {
        return kind >= SyntaxKind.BoolKeyword && kind <= SyntaxKind.DateAndTimeTypeKeyword;
    }

    private SyntaxNode? TryParseEnumBaseType()
    {
        if (Current.Kind == SyntaxKind.OpenParen)
        {
            var open = NextToken();
            var type = ParseType();
            var close = MatchToken(SyntaxKind.CloseParen);
            return type;
        }
        return null;
    }

    // Enum member names may collide with soft keywords (Reference, Error, ...);
    // the following token disambiguates.
    private bool IsEnumValueStart()
    {
        if (Current.Kind == SyntaxKind.Identifier)
            return true;
        if (!IsWordToken(Current))
            return false;
        var next = Peek(1).Kind;
        return next == SyntaxKind.Comma || next == SyntaxKind.CloseParen ||
               next == SyntaxKind.AssignmentOperator || next == SyntaxKind.Equal;
    }

    private SyntaxNode ParseEnumValue()
    {
        var start = Current.Span.Start;
        var name = MatchDeclarationName();
        SyntaxNode? init = null;
        if (Current.Kind == SyntaxKind.AssignmentOperator || Current.Kind == SyntaxKind.Equal)
        {
            var op = NextToken();
            var value = ParseExpression();
            init = SyntaxFactory.Node(SyntaxKind.EnumValueInitializer,
                TextSpan.FromBounds(op.Span.Start, value.Span.End),
                new[] { value }, new[] { op });
        }

        var end = init?.Span.End ?? name.Span.End;
        var span = TextSpan.FromBounds(start, end);
        var children = new List<SyntaxNode>();
        if (init != null) children.Add(init);

        return SyntaxFactory.Node(SyntaxKind.EnumValue, span, children, new[] { name });
    }

    private SyntaxNode ParseStringType()
    {
        var start = Current.Span.Start;
        var stringKeyword = NextToken();
        var length = TryParseStringLength();
        var span = TextSpan.FromBounds(start, length?.Span.End ?? stringKeyword.Span.End);
        var children = new List<SyntaxNode>();
        if (length != null) children.Add(length);

        return SyntaxFactory.Node(SyntaxKind.StringType, span, children, new[] { stringKeyword });
    }

    private SyntaxNode? TryParseStringLength()
    {
        // STRING[80] (IEC bracket form) and STRING(255) (TwinCAT paren form);
        // the length may be a literal or a constant identifier.
        if (Current.Kind == SyntaxKind.OpenBracket || Current.Kind == SyntaxKind.OpenParen)
        {
            var closeKind = Current.Kind == SyntaxKind.OpenBracket
                ? SyntaxKind.CloseBracket : SyntaxKind.CloseParen;
            var open = NextToken();
            var length = Current.Kind == SyntaxKind.Identifier
                ? NextToken() : MatchToken(SyntaxKind.NumericLiteral);
            var close = MatchToken(closeKind);
            return SyntaxFactory.Node(SyntaxKind.StringLength,
                TextSpan.FromBounds(open.Span.Start, close.Span.End),
                new[] { open, length, close });
        }
        return null;
    }

    private SyntaxNode ParsePointerType()
    {
        var start = Current.Span.Start;
        var ptrKeyword = NextToken();
        SyntaxToken? toKeyword = null;
        if (Current.Kind == SyntaxKind.ToKeyword)
        {
            toKeyword = NextToken();
        }
        var elementType = ParseType();
        var span = TextSpan.FromBounds(start, elementType.Span.End);
        var tokens = new List<SyntaxToken> { ptrKeyword };
        if (toKeyword != null) tokens.Add(toKeyword);

        return SyntaxFactory.Node(SyntaxKind.PointerType, span, new[] { elementType }, tokens);
    }

    private SyntaxNode ParseReferenceType()
    {
        var start = Current.Span.Start;
        var refKeyword = MatchToken(SyntaxKind.ReferenceKeyword);
        var toKeyword = MatchToken(SyntaxKind.ToKeyword);
        var elementType = ParseType();
        var span = TextSpan.FromBounds(start, elementType.Span.End);
        return SyntaxFactory.Node(SyntaxKind.ReferenceType, span, new[] { elementType },
            new[] { refKeyword, toKeyword });
    }

    private SyntaxNode ParseTypeDeclaration()
    {
        var start = Current.Span.Start;
        var typeKeyword = MatchToken(SyntaxKind.TypeKeyword);
        var name = MatchToken(SyntaxKind.Identifier);
        var colon = MatchToken(SyntaxKind.Colon);
        var type = ParseType();
        var semicolon = TryMatchSemicolon();
        var endKeyword = MatchToken(SyntaxKind.EndTypeKeyword);

        var span = TextSpan.FromBounds(start, endKeyword.Span.End);
        var tokens = new List<SyntaxToken> { typeKeyword, name, colon, endKeyword };
        if (semicolon != null) tokens.Insert(3, semicolon);

        return SyntaxFactory.Node(SyntaxKind.TypeDeclaration, span, new[] { type }, tokens);
    }

    private SyntaxToken? TryMatchSemicolon()
    {
        if (Current.Kind == SyntaxKind.Semicolon)
            return NextToken();
        return null;
    }

    private SyntaxNode ParseUsingDirective()
    {
        var start = Current.Span.Start;
        var usingKeyword = MatchToken(SyntaxKind.UsingKeyword);
        var namespaceToken = MatchToken(SyntaxKind.Identifier);

        var dottedParts = new List<SyntaxToken> { namespaceToken };
        while (Current.Kind == SyntaxKind.Dot)
        {
            var dot = NextToken();
            var part = MatchToken(SyntaxKind.Identifier);
            dottedParts.Add(dot);
            dottedParts.Add(part);
        }

        var semicolon = MatchToken(SyntaxKind.Semicolon);
        var end = semicolon.Span.End;
        var span = TextSpan.FromBounds(start, end);
        var allTokens = new List<SyntaxToken> { usingKeyword };
        allTokens.AddRange(dottedParts);
        allTokens.Add(semicolon);

        return SyntaxFactory.Node(SyntaxKind.UsingDirective, span,
            ImmutableArray<SyntaxNode>.Empty, allTokens);
    }

    // Statements
    private SyntaxNode ParseStatementList()
    {
        var start = Current.Span.Start;
        var statements = new List<SyntaxNode>();

        while (IsStatementStart(Current.Kind))
        {
            statements.Add(ParseStatement());
        }

        var end = statements.Count > 0 ? statements[statements.Count - 1].Span.End : start;
        var span = TextSpan.FromBounds(start, end);

        if (statements.Count == 0)
            return SyntaxFactory.Node(SyntaxKind.StatementList, new TextSpan(start, 0),
                ImmutableArray<SyntaxNode>.Empty, ImmutableArray<SyntaxToken>.Empty);

        return SyntaxFactory.Node(SyntaxKind.StatementList, span, statements);
    }

    private static bool IsStatementStart(SyntaxKind kind)
    {
        if (kind == SyntaxKind.EndOfFile)
            return false;

        // End keywords terminate statement lists
        if (kind.ToString().StartsWith("End"))
            return false;

        // ELSE, ELSIF, UNTIL, ELSE_CASE are handled by their parent statements
        if (kind is SyntaxKind.ElseKeyword or SyntaxKind.ElsIfKeyword
            or SyntaxKind.UntilKeyword or SyntaxKind.ElseCaseClause)
            return false;

        return kind is SyntaxKind.Identifier
            or SyntaxKind.IfKeyword
            or SyntaxKind.CaseKeyword
            or SyntaxKind.ForKeyword
            or SyntaxKind.WhileKeyword
            or SyntaxKind.RepeatKeyword
            or SyntaxKind.ExitKeyword
            or SyntaxKind.ContinueKeyword
            or SyntaxKind.ReturnKeyword
            or SyntaxKind.GotoKeyword
            or SyntaxKind.TryKeyword
            or SyntaxKind.Semicolon;
    }

    private SyntaxNode ParseStatement()
    {
        return Current.Kind switch
        {
            SyntaxKind.Semicolon => ParseEmptyStatement(),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.CaseKeyword => ParseCaseStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.RepeatKeyword => ParseRepeatStatement(),
            SyntaxKind.ExitKeyword => ParseSimpleStatement(SyntaxKind.ExitStatement),
            SyntaxKind.ContinueKeyword => ParseSimpleStatement(SyntaxKind.ContinueStatement),
            SyntaxKind.ReturnKeyword => ParseSimpleStatement(SyntaxKind.ReturnStatement),
            SyntaxKind.GotoKeyword => ParseGotoStatement(),
            SyntaxKind.TryKeyword => ParseTryStatement(),
            _ => ParseAssignmentOrCallStatement()
        };
    }

    private SyntaxNode ParseEmptyStatement()
    {
        var semicolon = MatchToken(SyntaxKind.Semicolon);
        return SyntaxFactory.Node(SyntaxKind.EmptyStatement, semicolon.Span, new[] { semicolon });
    }

    private SyntaxNode ParseSimpleStatement(SyntaxKind kind)
    {
        var keyword = NextToken();
        var semicolon = MatchToken(SyntaxKind.Semicolon);
        var span = TextSpan.FromBounds(keyword.Span.Start, semicolon.Span.End);
        return SyntaxFactory.Node(kind, span, new[] { keyword, semicolon });
    }

    private SyntaxNode ParseGotoStatement()
    {
        var start = Current.Span.Start;
        var gotoKeyword = MatchToken(SyntaxKind.GotoKeyword);
        var label = MatchToken(SyntaxKind.Identifier);
        var semicolon = MatchToken(SyntaxKind.Semicolon);
        var span = TextSpan.FromBounds(start, semicolon.Span.End);
        return SyntaxFactory.Node(SyntaxKind.GotoStatement, span,
            new[] { gotoKeyword, label, semicolon });
    }

    private SyntaxNode ParseIfStatement()
    {
        var start = Current.Span.Start;
        var ifKeyword = MatchToken(SyntaxKind.IfKeyword);
        var condition = ParseExpression();
        var thenKeyword = MatchToken(SyntaxKind.ThenKeyword);
        var thenBody = ParseStatementList();

        var elseIfClauses = new List<SyntaxNode>();
        while (Current.Kind == SyntaxKind.ElsIfKeyword)
        {
            elseIfClauses.Add(ParseElsIfClause());
        }

        SyntaxNode? elseClause = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseClause = ParseElseClause();
        }

        var endIfKeyword = MatchToken(SyntaxKind.EndIfKeyword);
        var span = TextSpan.FromBounds(start, endIfKeyword.Span.End);

        var children = new List<SyntaxNode> { condition, thenBody };
        children.AddRange(elseIfClauses);
        if (elseClause != null) children.Add(elseClause);

        return SyntaxFactory.Node(SyntaxKind.IfStatement, span, children,
            new[] { ifKeyword, thenKeyword, endIfKeyword });
    }

    private SyntaxNode ParseElsIfClause()
    {
        var start = Current.Span.Start;
        var elsifKeyword = MatchToken(SyntaxKind.ElsIfKeyword);
        var condition = ParseExpression();
        var thenKeyword = MatchToken(SyntaxKind.ThenKeyword);
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        return SyntaxFactory.Node(SyntaxKind.ElsIfClause, span,
            new[] { condition, body },
            new[] { elsifKeyword, thenKeyword });
    }

    private SyntaxNode ParseElseClause()
    {
        var start = Current.Span.Start;
        var elseKeyword = MatchToken(SyntaxKind.ElseKeyword);
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        return SyntaxFactory.Node(SyntaxKind.ElseClause, span,
            new[] { body }, new[] { elseKeyword });
    }

    private SyntaxNode ParseCaseStatement()
    {
        var start = Current.Span.Start;
        var caseKeyword = MatchToken(SyntaxKind.CaseKeyword);
        var selector = ParseExpression();
        var ofKeyword = MatchToken(SyntaxKind.OfKeyword);

        var clauses = new List<SyntaxNode>();
        while (Current.Kind != SyntaxKind.ElseKeyword && Current.Kind != SyntaxKind.EndCaseKeyword && Current.Kind != SyntaxKind.EndOfFile)
        {
            clauses.Add(ParseCaseClause());
        }

        SyntaxNode? elseClause = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseClause = ParseCaseElseClause();
        }

        var endCaseKeyword = MatchToken(SyntaxKind.EndCaseKeyword);
        var span = TextSpan.FromBounds(start, endCaseKeyword.Span.End);

        var children = new List<SyntaxNode> { selector };
        children.AddRange(clauses);
        if (elseClause != null) children.Add(elseClause);

        return SyntaxFactory.Node(SyntaxKind.CaseStatement, span, children,
            new[] { caseKeyword, ofKeyword, endCaseKeyword });
    }

    private SyntaxNode ParseCaseClause()
    {
        var start = Current.Span.Start;
        var values = new List<SyntaxNode>();
        var commas = new List<SyntaxToken>();
        values.Add(ParseExpression());

        while (Current.Kind == SyntaxKind.Comma)
        {
            commas.Add(NextToken());
            values.Add(ParseExpression());
        }

        var colon = MatchToken(SyntaxKind.Colon);
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        var tokens = new List<SyntaxToken>();
        tokens.AddRange(commas);
        tokens.Add(colon);

        return SyntaxFactory.Node(SyntaxKind.CaseClause, span, values.Concat(new[] { body }), tokens);
    }

    private SyntaxNode ParseCaseElseClause()
    {
        var start = Current.Span.Start;
        var elseKeyword = MatchToken(SyntaxKind.ElseKeyword);
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        return SyntaxFactory.Node(SyntaxKind.ElseCaseClause, span,
            new[] { body }, new[] { elseKeyword });
    }

    private SyntaxNode ParseForStatement()
    {
        var start = Current.Span.Start;
        var forKeyword = MatchToken(SyntaxKind.ForKeyword);
        var variable = MatchToken(SyntaxKind.Identifier);
        var assignOp = MatchToken(SyntaxKind.AssignmentOperator);
        var fromExpr = ParseExpression();
        var toKeyword = MatchToken(SyntaxKind.ToKeyword);
        var toExpr = ParseExpression();

        SyntaxNode? byClause = null;
        if (Current.Kind == SyntaxKind.ByKeyword)
        {
            var byKeyword = NextToken();
            var stepExpr = ParseExpression();
            byClause = SyntaxFactory.Node(SyntaxKind.ForByClause,
                TextSpan.FromBounds(byKeyword.Span.Start, stepExpr.Span.End),
                new[] { stepExpr }, new[] { byKeyword });
        }

        var doKeyword = MatchToken(SyntaxKind.DoKeyword);
        var body = ParseStatementList();
        var endForKeyword = MatchToken(SyntaxKind.EndForKeyword);
        var span = TextSpan.FromBounds(start, endForKeyword.Span.End);

        var children = new List<SyntaxNode> { fromExpr, toExpr, body };
        if (byClause != null) children.Insert(2, byClause);

        return SyntaxFactory.Node(SyntaxKind.ForStatement, span, children,
            new[] { forKeyword, variable, assignOp, toKeyword, doKeyword, endForKeyword });
    }

    private SyntaxNode ParseWhileStatement()
    {
        var start = Current.Span.Start;
        var whileKeyword = MatchToken(SyntaxKind.WhileKeyword);
        var condition = ParseExpression();
        var doKeyword = MatchToken(SyntaxKind.DoKeyword);
        var body = ParseStatementList();
        var endWhileKeyword = MatchToken(SyntaxKind.EndWhileKeyword);
        var span = TextSpan.FromBounds(start, endWhileKeyword.Span.End);

        return SyntaxFactory.Node(SyntaxKind.WhileStatement, span,
            new[] { condition, body },
            new[] { whileKeyword, doKeyword, endWhileKeyword });
    }

    private SyntaxNode ParseRepeatStatement()
    {
        var start = Current.Span.Start;
        var repeatKeyword = MatchToken(SyntaxKind.RepeatKeyword);
        var body = ParseStatementList();
        var untilKeyword = MatchToken(SyntaxKind.UntilKeyword);
        var condition = ParseExpression();
        var endRepeatKeyword = MatchToken(SyntaxKind.EndRepeatKeyword);
        var span = TextSpan.FromBounds(start, endRepeatKeyword.Span.End);

        return SyntaxFactory.Node(SyntaxKind.RepeatStatement, span,
            new[] { body, condition },
            new[] { repeatKeyword, untilKeyword, endRepeatKeyword });
    }

    private SyntaxNode ParseTryStatement()
    {
        var start = Current.Span.Start;
        var tryKeyword = MatchToken(SyntaxKind.TryKeyword);
        var body = ParseStatementList();

        var catchClauses = new List<SyntaxNode>();
        while (Current.Kind == SyntaxKind.CatchKeyword)
        {
            catchClauses.Add(ParseCatchClause());
        }

        SyntaxNode? finallyClause = null;
        if (Current.Kind == SyntaxKind.FinallyKeyword)
        {
            finallyClause = ParseFinallyClause();
        }

        var endTryKeyword = MatchToken(SyntaxKind.EndTryKeyword);
        var span = TextSpan.FromBounds(start, endTryKeyword.Span.End);

        var children = new List<SyntaxNode> { body };
        children.AddRange(catchClauses);
        if (finallyClause != null) children.Add(finallyClause);

        return SyntaxFactory.Node(SyntaxKind.TryStatement, span, children,
            new[] { tryKeyword, endTryKeyword });
    }

    private SyntaxNode ParseCatchClause()
    {
        var start = Current.Span.Start;
        var catchKeyword = MatchToken(SyntaxKind.CatchKeyword);
        SyntaxToken? exceptionVar = null;
        if (Current.Kind == SyntaxKind.Identifier)
        {
            exceptionVar = NextToken();
        }
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        var tokens = new List<SyntaxToken> { catchKeyword };
        if (exceptionVar != null) tokens.Add(exceptionVar);

        return SyntaxFactory.Node(SyntaxKind.CatchClause, span,
            new[] { body }, tokens);
    }

    private SyntaxNode ParseFinallyClause()
    {
        var start = Current.Span.Start;
        var finallyKeyword = MatchToken(SyntaxKind.FinallyKeyword);
        var body = ParseStatementList();
        var span = TextSpan.FromBounds(start, body.Span.End);

        return SyntaxFactory.Node(SyntaxKind.FinallyClause, span,
            new[] { body }, new[] { finallyKeyword });
    }

    private SyntaxNode ParseAssignmentOrCallStatement()
    {
        var start = Current.Span.Start;

        // Check for label: Identifier followed by Colon (not :=)
        if (Current.Kind == SyntaxKind.Identifier && Peek(1).Kind == SyntaxKind.Colon)
        {
            var label = NextToken(); // identifier
            var colon = NextToken(); // colon
            var statement = ParseStatement();
            var end = statement.Span.End;
            var span = TextSpan.FromBounds(start, end);
            return SyntaxFactory.Node(SyntaxKind.LabelStatement, span,
                new[] { statement }, new[] { label, colon });
        }

        var left = ParseExpression();

        if (Current.Kind == SyntaxKind.AssignmentOperator)
        {
            var assignOp = NextToken();
            var right = ParseExpression();
            var semicolon = MatchToken(SyntaxKind.Semicolon);
            var span = TextSpan.FromBounds(start, semicolon.Span.End);

            return SyntaxFactory.Node(SyntaxKind.AssignmentStatement, span,
                new[] { left, right },
                new[] { assignOp, semicolon });
        }
        else if (Current.Kind == SyntaxKind.ArrowOperator)
        {
            var arrow = NextToken();
            var right = ParseExpression();
            var semicolon = MatchToken(SyntaxKind.Semicolon);
            var span = TextSpan.FromBounds(start, semicolon.Span.End);

            return SyntaxFactory.Node(SyntaxKind.OutputAssignmentStatement, span,
                new[] { left, right },
                new[] { arrow, semicolon });
        }
        else
        {
            var semicolon = MatchToken(SyntaxKind.Semicolon);
            var span = TextSpan.FromBounds(start, semicolon.Span.End);

            return SyntaxFactory.Node(SyntaxKind.CallStatement, span,
                new[] { left }, new[] { semicolon });
        }
    }

    // Expressions
    private SyntaxNode ParseExpression()
    {
        return ParseOrExpression();
    }

    private SyntaxNode ParseOrExpression()
    {
        var left = ParseXorExpression();
        while (Current.Kind is SyntaxKind.OrKeyword)
        {
            var op = NextToken();
            var right = ParseXorExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseXorExpression()
    {
        var left = ParseAndExpression();
        while (Current.Kind is SyntaxKind.XorKeyword)
        {
            var op = NextToken();
            var right = ParseAndExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseAndExpression()
    {
        var left = ParseEqualityExpression();
        while (Current.Kind is SyntaxKind.AndKeyword or SyntaxKind.Ampersand)
        {
            var op = NextToken();
            var right = ParseEqualityExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseEqualityExpression()
    {
        var left = ParseRelationalExpression();
        while (Current.Kind is SyntaxKind.Equal or SyntaxKind.NotEqual)
        {
            var op = NextToken();
            var right = ParseRelationalExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseRelationalExpression()
    {
        var left = ParseAdditiveExpression();
        while (Current.Kind is SyntaxKind.LessThan or SyntaxKind.GreaterThan
            or SyntaxKind.LessThanOrEqual or SyntaxKind.GreaterThanOrEqual)
        {
            var op = NextToken();
            var right = ParseAdditiveExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseAdditiveExpression()
    {
        var left = ParseMultiplicativeExpression();
        while (Current.Kind is SyntaxKind.Plus or SyntaxKind.Minus)
        {
            var op = NextToken();
            var right = ParseMultiplicativeExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseMultiplicativeExpression()
    {
        var left = ParsePowerExpression();
        while (Current.Kind is SyntaxKind.Asterisk or SyntaxKind.Slash
            or SyntaxKind.ModKeyword)
        {
            var op = NextToken();
            var right = ParsePowerExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParsePowerExpression()
    {
        var left = ParseUnaryExpression();
        while (Current.Kind is SyntaxKind.Power)
        {
            var op = NextToken();
            var right = ParseUnaryExpression();
            left = MakeBinary(left, op, right);
        }
        return left;
    }

    private SyntaxNode ParseUnaryExpression()
    {
        if (Current.Kind is SyntaxKind.Minus or SyntaxKind.NotKeyword
            or SyntaxKind.Plus)
        {
            var op = NextToken();
            var operand = ParseUnaryExpression();
            var span = TextSpan.FromBounds(op.Span.Start, operand.Span.End);
            return SyntaxFactory.Node(SyntaxKind.UnaryExpression, span,
                new[] { operand }, new[] { op });
        }
        return ParsePrimaryExpression();
    }

    private SyntaxNode ParsePrimaryExpression()
    {
        var start = Current.Span.Start;

        switch (Current.Kind)
        {
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.RealLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.BoolLiteral:
            case SyntaxKind.TimeLiteral:
            case SyntaxKind.DateLiteral:
            case SyntaxKind.TimeOfDayLiteral:
            case SyntaxKind.DateAndTimeLiteral:
            case SyntaxKind.BitStringLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
                var literalToken = NextToken();
                return SyntaxFactory.Node(SyntaxKind.LiteralExpression, literalToken.Span,
                    new[] { literalToken });

            case SyntaxKind.Identifier:
                return ParseIdentifierOrMemberExpression();

            case SyntaxKind.OpenParen:
                var openParen = NextToken();
                var expr = ParseExpression();
                var closeParen = MatchToken(SyntaxKind.CloseParen);
                var parenSpan = TextSpan.FromBounds(start, closeParen.Span.End);
                return SyntaxFactory.Node(SyntaxKind.ParenthesizedExpression, parenSpan,
                    new[] { expr }, new[] { openParen, closeParen });

            case SyntaxKind.DirectVariable:
                var directVar = NextToken();
                return SyntaxFactory.Node(SyntaxKind.DirectVariableExpression, directVar.Span,
                    new[] { directVar });

            case SyntaxKind.OpenBracket:
                // Array initializer: [expr, expr, ...] (used in VAR initializers)
                return ParseArrayInitializer();

            default:
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    Current.Span,
                    $"Unexpected token '{Current.Text}' in expression"));
                var badToken = NextToken();
                return SyntaxFactory.Node(SyntaxKind.LiteralExpression, badToken.Span, new[] { badToken });
        }
    }

    private SyntaxNode ParseArrayInitializer()
    {
        var start = Current.Span.Start;
        var tokens = new List<SyntaxToken> { MatchToken(SyntaxKind.OpenBracket) };
        var elements = new List<SyntaxNode>();

        if (Current.Kind != SyntaxKind.CloseBracket)
        {
            elements.Add(ParseExpression());
            while (Current.Kind == SyntaxKind.Comma)
            {
                tokens.Add(NextToken()); // comma
                elements.Add(ParseExpression());
            }
        }

        var closeBracket = MatchToken(SyntaxKind.CloseBracket);
        tokens.Add(closeBracket);

        return SyntaxFactory.Node(SyntaxKind.ArrayInitializer,
            TextSpan.FromBounds(start, closeBracket.Span.End), elements, tokens);
    }

    private SyntaxNode ParseIdentifierOrMemberExpression()
    {
        var start = Current.Span.Start;
        var name = MatchToken(SyntaxKind.Identifier);
        var result = SyntaxFactory.Node(SyntaxKind.IdentifierExpression, name.Span, new[] { name });

        while (true)
        {
            if (Current.Kind == SyntaxKind.Dot)
            {
                var dot = NextToken();
                var memberName = MatchToken(SyntaxKind.Identifier);
                var span = TextSpan.FromBounds(start, memberName.Span.End);
                result = SyntaxFactory.Node(SyntaxKind.MemberAccessExpression, span,
                    new[] { result }, new[] { dot, memberName });
            }
            else if (Current.Kind == SyntaxKind.Caret)
            {
                // Pointer dereference: ptr^ / THIS^.Method()
                var caret = NextToken();
                result = SyntaxFactory.Node(SyntaxKind.DereferenceExpression,
                    TextSpan.FromBounds(start, caret.Span.End),
                    new[] { result }, new[] { caret });
            }
            else if (Current.Kind == SyntaxKind.OpenBracket)
            {
                var openBracket = NextToken();
                var index = ParseExpression();
                var closeBracket = MatchToken(SyntaxKind.CloseBracket);
                var span = TextSpan.FromBounds(start, closeBracket.Span.End);
                result = SyntaxFactory.Node(SyntaxKind.ElementAccessExpression, span,
                    new[] { result, index },
                    new[] { openBracket, closeBracket });
            }
            else if (Current.Kind == SyntaxKind.OpenParen)
            {
                var openParen = NextToken();
                var args = new List<SyntaxNode>();
                var commaTokens = new List<SyntaxToken>();
                if (Current.Kind != SyntaxKind.CloseParen)
                {
                    args.Add(ParseArgument());
                    while (Current.Kind == SyntaxKind.Comma)
                    {
                        commaTokens.Add(NextToken());
                        args.Add(ParseArgument());
                    }
                }
                var closeParen = MatchToken(SyntaxKind.CloseParen);
                var span = TextSpan.FromBounds(start, closeParen.Span.End);
                var allTokens = new List<SyntaxToken> { openParen };
                allTokens.AddRange(commaTokens);
                allTokens.Add(closeParen);
                result = SyntaxFactory.Node(SyntaxKind.InvocationExpression, span,
                    new[] { result }.Concat(args).ToList(),
                    allTokens);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private SyntaxNode ParseArgument()
    {
        var start = Current.Span.Start;
        var expr = ParseExpression();

        if (Current.Kind == SyntaxKind.AssignmentOperator || Current.Kind == SyntaxKind.Equal)
        {
            var op = NextToken();
            var value = ParseExpression();
            var span = TextSpan.FromBounds(start, value.Span.End);
            return SyntaxFactory.Node(SyntaxKind.NamedArgument, span,
                new[] { expr, value }, new[] { op });
        }
        else if (Current.Kind == SyntaxKind.ArrowOperator)
        {
            var arrow = NextToken();
            if (Current.Kind != SyntaxKind.CloseParen &&
                Current.Kind != SyntaxKind.Comma &&
                Current.Kind != SyntaxKind.Semicolon)
            {
                var value = ParseExpression();
                var span2 = TextSpan.FromBounds(start, value.Span.End);
                return SyntaxFactory.Node(SyntaxKind.NamedArgument, span2,
                    new[] { expr, value }, new[] { arrow });
            }
            var span = TextSpan.FromBounds(start, arrow.Span.End);
            return SyntaxFactory.Node(SyntaxKind.NamedArgument, span,
                new[] { expr }, new[] { arrow });
        }

        return SyntaxFactory.Node(SyntaxKind.Argument, expr.Span, new[] { expr });
    }

    private static SyntaxNode MakeBinary(SyntaxNode left, SyntaxToken op, SyntaxNode right)
    {
        var span = TextSpan.FromBounds(left.Span.Start, right.Span.End);
        return SyntaxFactory.Node(SyntaxKind.BinaryExpression, span,
            new[] { left, right }, new[] { op });
    }

    private static string GetKindText(SyntaxKind kind)
    {
        return kind.ToString();
    }
}
