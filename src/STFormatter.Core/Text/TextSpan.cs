namespace STFormatter.Core.Text;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool IsEmpty => Length == 0;

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    public bool OverlapsWith(TextSpan span) => span.Start < End && Start < span.End;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public override string ToString() => $"[{Start}..{End})";
}
