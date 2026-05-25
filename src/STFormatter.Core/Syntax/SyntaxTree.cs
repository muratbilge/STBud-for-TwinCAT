using STFormatter.Core.Text;

namespace STFormatter.Core.Syntax;

public sealed class SyntaxTree
{
    public SyntaxTree(SourceText text, SyntaxNode root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Root = root;
        Diagnostics = diagnostics;
    }

    public SourceText Text { get; }
    public SyntaxNode Root { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public override string ToString()
    {
        return Root.ToString();
    }

    public static SyntaxTree Create(SourceText text, SyntaxNode root, IReadOnlyList<Diagnostic> diagnostics)
        => new(text, root, diagnostics);
}

public sealed class Diagnostic
{
    public Diagnostic(DiagnosticSeverity severity, TextSpan span, string message)
    {
        Severity = severity;
        Span = span;
        Message = message;
    }

    public DiagnosticSeverity Severity { get; }
    public TextSpan Span { get; }
    public string Message { get; }

    public override string ToString() => $"[{Severity}] {Span}: {Message}";
}

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}
