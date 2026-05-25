using System.Collections.Immutable;
using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public sealed class GenericSyntaxNode : SyntaxNode
{
    public GenericSyntaxNode(
        SyntaxKind kind,
        TextSpan span,
        ImmutableArray<SyntaxNode> childNodes,
        ImmutableArray<SyntaxToken> childTokens)
        : base(kind, span, childNodes, childTokens)
    {
    }

    public override SyntaxNode WithUpdatedChildren(
        ImmutableArray<SyntaxNode>? childNodes = null,
        ImmutableArray<SyntaxToken>? childTokens = null)
    {
        return new GenericSyntaxNode(
            Kind,
            Span,
            childNodes ?? ChildNodes,
            childTokens ?? ChildTokens);
    }
}

public sealed class CompilationUnitSyntax : SyntaxNode
{
    public CompilationUnitSyntax(
        TextSpan span,
        ImmutableArray<SyntaxNode> declarations,
        ImmutableArray<SyntaxToken> tokens)
        : base(SyntaxKind.CompilationUnit, span, declarations, tokens)
    {
    }

    public ImmutableArray<SyntaxNode> Declarations => ChildNodes;

    public override SyntaxNode WithUpdatedChildren(
        ImmutableArray<SyntaxNode>? childNodes = null,
        ImmutableArray<SyntaxToken>? childTokens = null)
    {
        return new CompilationUnitSyntax(
            Span,
            childNodes ?? ChildNodes,
            childTokens ?? ChildTokens);
    }
}
