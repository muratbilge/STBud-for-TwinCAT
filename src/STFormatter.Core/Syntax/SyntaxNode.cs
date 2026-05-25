using System.Collections.Immutable;
using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public abstract class SyntaxNode
{
    private readonly ImmutableArray<SyntaxNode> _childNodes;
    private readonly ImmutableArray<SyntaxToken> _childTokens;

    protected SyntaxNode(
        SyntaxKind kind,
        TextSpan span,
        ImmutableArray<SyntaxNode> childNodes,
        ImmutableArray<SyntaxToken> childTokens)
    {
        Kind = kind;
        Span = span;
        _childNodes = childNodes.IsDefault ? ImmutableArray<SyntaxNode>.Empty : childNodes;
        _childTokens = childTokens.IsDefault ? ImmutableArray<SyntaxToken>.Empty : childTokens;
    }

    public SyntaxKind Kind { get; }
    public TextSpan Span { get; }

    public ImmutableArray<SyntaxNode> ChildNodes => _childNodes;
    public ImmutableArray<SyntaxToken> ChildTokens => _childTokens;

    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        foreach (var child in _childNodes)
        {
            yield return child;
            foreach (var descendant in child.DescendantNodes())
                yield return descendant;
        }
    }

    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        foreach (var token in _childTokens)
            yield return token;

        foreach (var child in _childNodes)
        {
            foreach (var token in child.DescendantTokens())
                yield return token;
        }
    }

    public IEnumerable<SyntaxTrivia> DescendantTrivia()
    {
        foreach (var token in DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
                yield return trivia;
            foreach (var trivia in token.TrailingTrivia)
                yield return trivia;
        }
    }

    public virtual SyntaxNode? Parent { get; internal set; }

    public abstract SyntaxNode WithUpdatedChildren(
        ImmutableArray<SyntaxNode>? childNodes = null,
        ImmutableArray<SyntaxToken>? childTokens = null);

    public override string ToString()
    {
        return string.Concat(
            _childTokens.Select(t => t.ToString())
            .Concat(_childNodes.Select(n => n.ToString())));
    }

    public string ToStringWithoutTrivia()
    {
        return string.Concat(
            _childTokens.Select(t => t.ToStringWithoutTrivia())
            .Concat(_childNodes.Select(n => n.ToStringWithoutTrivia())));
    }
}
