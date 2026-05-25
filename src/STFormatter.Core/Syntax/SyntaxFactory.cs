using System.Collections.Immutable;
using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public static class SyntaxFactory
{
    public static SyntaxToken Token(SyntaxKind kind, string text, int position)
        => new(kind, text, new TextSpan(position, text.Length),
            ImmutableArray<SyntaxTrivia>.Empty,
            ImmutableArray<SyntaxTrivia>.Empty);

    public static SyntaxToken Token(SyntaxKind kind, string text, TextSpan span)
        => new(kind, text, span,
            ImmutableArray<SyntaxTrivia>.Empty,
            ImmutableArray<SyntaxTrivia>.Empty);

    public static SyntaxToken Token(SyntaxKind kind, string text, int position,
        ImmutableArray<SyntaxTrivia> leading,
        ImmutableArray<SyntaxTrivia> trailing)
        => new(kind, text, new TextSpan(position, text.Length), leading, trailing);

    public static SyntaxTrivia Trivia(SyntaxKind kind, string text, int position)
        => new(kind, text, new TextSpan(position, text.Length));

    public static SyntaxNode Node(SyntaxKind kind, TextSpan span,
        ImmutableArray<SyntaxNode> childNodes,
        ImmutableArray<SyntaxToken> childTokens)
        => new GenericSyntaxNode(kind, span, childNodes, childTokens);

    public static SyntaxNode Node(SyntaxKind kind, TextSpan span,
        IEnumerable<SyntaxNode> childNodes,
        IEnumerable<SyntaxToken> childTokens)
        => new GenericSyntaxNode(kind, span,
            childNodes.ToImmutableArray(),
            childTokens.ToImmutableArray());

    public static SyntaxNode Node(SyntaxKind kind, TextSpan span, params SyntaxNode[] childNodes)
        => new GenericSyntaxNode(kind, span, childNodes.ToImmutableArray(), ImmutableArray<SyntaxToken>.Empty);

    public static SyntaxNode Node(SyntaxKind kind, TextSpan span, params SyntaxToken[] childTokens)
        => new GenericSyntaxNode(kind, span, ImmutableArray<SyntaxNode>.Empty, childTokens.ToImmutableArray());

    public static SyntaxNode Node(SyntaxKind kind, TextSpan span,
        IEnumerable<SyntaxNode> childNodes,
        params SyntaxToken[] childTokens)
        => new GenericSyntaxNode(kind, span,
            childNodes.ToImmutableArray(),
            childTokens.ToImmutableArray());

    public static CompilationUnitSyntax CompilationUnit(
        TextSpan span,
        IEnumerable<SyntaxNode> declarations,
        IEnumerable<SyntaxToken> tokens)
        => new(span, declarations.ToImmutableArray(), tokens.ToImmutableArray());
}
