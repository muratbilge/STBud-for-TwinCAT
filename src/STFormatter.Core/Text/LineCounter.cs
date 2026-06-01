namespace STFormatter.Core.Text;

public static class LineCounter
{
    private static readonly string[] _separators = { "\r\n", "\r", "\n" };

    public static int Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Split(_separators, System.StringSplitOptions.None).Length;
    }
}
