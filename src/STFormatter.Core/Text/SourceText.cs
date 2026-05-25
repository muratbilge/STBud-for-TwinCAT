namespace STFormatter.Core.Text;

public sealed class SourceText
{
    private readonly string _text;

    public SourceText(string text)
    {
        _text = text ?? string.Empty;
        Lines = ParseLines();
    }

    public int Length => _text.Length;

    public char this[int index] => _text[index];

    public IReadOnlyList<TextLine> Lines { get; }

    public string ToString(TextSpan span) => _text.Substring(span.Start, span.Length);

    public override string ToString() => _text;

    public int GetLineIndex(int position)
    {
        var lower = 0;
        var upper = Lines.Count - 1;

        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var line = Lines[middle];

            if (position >= line.Start && position <= line.EndIncludingLineBreak)
                return middle;

            if (position < line.Start)
                upper = middle - 1;
            else
                lower = middle + 1;
        }

        return lower - 1;
    }

    public TextLine GetLine(int lineIndex) => Lines[lineIndex];

    public static SourceText From(string text) => new(text);

    private IReadOnlyList<TextLine> ParseLines()
    {
        var lines = new List<TextLine>();
        var lineStart = 0;

        for (var i = 0; i < _text.Length; i++)
        {
            var c = _text[i];
            var lineBreakWidth = GetLineBreakWidth(_text, i);

            if (lineBreakWidth > 0)
            {
                var lineEnd = i + lineBreakWidth;
                lines.Add(new TextLine(lines.Count, lineStart, i, lineEnd));
                lineStart = lineEnd;
                i += lineBreakWidth - 1;
            }
        }

        if (lineStart <= _text.Length)
        {
            lines.Add(new TextLine(lines.Count, lineStart, _text.Length, _text.Length));
        }

        return lines;
    }

    private static int GetLineBreakWidth(string text, int i)
    {
        var c = text[i];
        var l = i + 1 < text.Length ? text[i + 1] : '\0';

        if (c == '\r' && l == '\n')
            return 2;
        if (c == '\r' || c == '\n')
            return 1;

        return 0;
    }
}

public readonly record struct TextLine(int LineNumber, int Start, int End, int EndIncludingLineBreak)
{
    public TextSpan Span => new(Start, End - Start);
    public TextSpan SpanIncludingLineBreak => new(Start, EndIncludingLineBreak - Start);
}
