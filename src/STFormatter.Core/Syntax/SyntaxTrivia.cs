using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public sealed class SyntaxTrivia
{
    public SyntaxTrivia(SyntaxKind kind, string text, TextSpan span)
    {
        Kind = kind;
        Text = text;
        Span = span;
    }

    public SyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }

    public int Width => Text.Length;
    public bool IsLineBreak => Kind == SyntaxKind.LineBreakTrivia;
    public bool IsWhitespace => Kind == SyntaxKind.WhitespaceTrivia;
    public bool IsComment => Kind == SyntaxKind.SingleLineCommentTrivia || Kind == SyntaxKind.MultiLineCommentTrivia;
    public bool IsPragma => Kind == SyntaxKind.PragmaTrivia;

    public override string ToString() => Text;
}
