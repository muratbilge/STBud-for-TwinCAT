using System.Collections.Immutable;
using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public sealed class SyntaxToken
{
    public SyntaxToken(
        SyntaxKind kind,
        string text,
        TextSpan span,
        ImmutableArray<SyntaxTrivia> leadingTrivia,
        ImmutableArray<SyntaxTrivia> trailingTrivia,
        object? value = null)
    {
        Kind = kind;
        Text = text;
        Span = span;
        LeadingTrivia = leadingTrivia.IsDefault ? ImmutableArray<SyntaxTrivia>.Empty : leadingTrivia;
        TrailingTrivia = trailingTrivia.IsDefault ? ImmutableArray<SyntaxTrivia>.Empty : trailingTrivia;
        Value = value;
    }

    public SyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public ImmutableArray<SyntaxTrivia> LeadingTrivia { get; }
    public ImmutableArray<SyntaxTrivia> TrailingTrivia { get; }
    public object? Value { get; }

    public int Width => Text.Length;
    public int FullWidth => LeadingTrivia.Sum(t => t.Width) + Width + TrailingTrivia.Sum(t => t.Width);

    public bool IsMissing => Kind == SyntaxKind.MissingToken;

    public SyntaxToken WithLeadingTrivia(ImmutableArray<SyntaxTrivia> trivia)
        => new(Kind, Text, Span, trivia, TrailingTrivia, Value);

    public SyntaxToken WithTrailingTrivia(ImmutableArray<SyntaxTrivia> trivia)
        => new(Kind, Text, Span, LeadingTrivia, trivia, Value);

    public SyntaxToken WithKind(SyntaxKind kind)
        => new(kind, Text, Span, LeadingTrivia, TrailingTrivia, Value);

    public SyntaxToken WithText(string text)
        => new(Kind, text, Span, LeadingTrivia, TrailingTrivia, Value);

    public override string ToString()
    {
        var leading = string.Concat(LeadingTrivia.Select(t => t.Text));
        var trailing = string.Concat(TrailingTrivia.Select(t => t.Text));
        return $"{leading}{Text}{trailing}";
    }

    public string ToStringWithoutTrivia() => Text;

    public static SyntaxToken Missing(SyntaxKind kind, int position)
        => new(kind, string.Empty, new TextSpan(position, 0), ImmutableArray<SyntaxTrivia>.Empty, ImmutableArray<SyntaxTrivia>.Empty);
}
