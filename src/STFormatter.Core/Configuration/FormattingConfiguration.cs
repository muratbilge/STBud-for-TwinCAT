namespace STFormatter.Core.Formatting;

public sealed class FormattingConfiguration
{
    public static FormattingConfiguration Default => new();

    public string IndentStyle { get; set; } = "spaces";
    public int IndentSize { get; set; } = 4;
    public int ContinuationIndentSize { get; set; } = 8;
    public string NewLineStyle { get; set; } = "crlf";
    public string KeywordCasing { get; set; } = "upper";
    public string BraceStyle { get; set; } = "allman";
    public bool SpaceAroundOperators { get; set; } = true;

    public bool IsAllmanStyle() => BraceStyle.Equals("allman", StringComparison.OrdinalIgnoreCase);
    public bool IsCompactStyle() => !IsAllmanStyle();
    public bool SpaceAfterComma { get; set; } = true;
    public bool SpaceBeforeSemicolon { get; set; } = false;
    public bool SpaceAfterColon { get; set; } = true;
    public bool AlignAssignments { get; set; } = true;
    public bool AlignVariableDeclarations { get; set; } = true;
    public int MaxLineLength { get; set; } = 120;
    public int EmptyLinesBetweenPOUs { get; set; } = 2;
    public int EmptyLinesBetweenVarSections { get; set; } = 1;
    public bool KeepSingleLineBlocks { get; set; } = false;
    public bool FormatOnSave { get; set; } = true;

    public string GetNewLine() => NewLineStyle.ToLowerInvariant() switch
    {
        "lf" => "\n",
        "cr" => "\r",
        _ => "\r\n"
    };

    public string GetIndentString(int level)
    {
        var unit = IndentStyle.ToLowerInvariant() == "tabs" ? "\t" : new string(' ', IndentSize);
        return string.Concat(Enumerable.Repeat(unit, level));
    }

    public string FormatKeyword(string keyword)
    {
        return KeywordCasing.ToLowerInvariant() switch
        {
            "lower" => keyword.ToLowerInvariant(),
            "pascal" => char.ToUpperInvariant(keyword[0]) + keyword.Substring(1).ToLowerInvariant(),
            "original" => keyword,
            _ => keyword.ToUpperInvariant()
        };
    }

    // Presets
    public static FormattingConfiguration CompactPreset => new()
    {
        IndentStyle = "spaces",
        IndentSize = 2,
        ContinuationIndentSize = 4,
        NewLineStyle = "crlf",
        KeywordCasing = "lower",
        BraceStyle = "compact",
        SpaceAroundOperators = true,
        SpaceAfterComma = true,
        SpaceBeforeSemicolon = false,
        SpaceAfterColon = true,
        AlignAssignments = false,
        AlignVariableDeclarations = false,
        MaxLineLength = 120,
        EmptyLinesBetweenPOUs = 1,
        EmptyLinesBetweenVarSections = 0,
        KeepSingleLineBlocks = true,
        FormatOnSave = true
    };

    public static FormattingConfiguration ExpandedPreset => new()
    {
        IndentStyle = "spaces",
        IndentSize = 4,
        ContinuationIndentSize = 8,
        NewLineStyle = "crlf",
        KeywordCasing = "upper",
        BraceStyle = "allman",
        SpaceAroundOperators = true,
        SpaceAfterComma = true,
        SpaceBeforeSemicolon = false,
        SpaceAfterColon = true,
        AlignAssignments = true,
        AlignVariableDeclarations = true,
        MaxLineLength = 80,
        EmptyLinesBetweenPOUs = 3,
        EmptyLinesBetweenVarSections = 2,
        KeepSingleLineBlocks = false,
        FormatOnSave = true
    };

    public static FormattingConfiguration FromPreset(string presetName)
    {
        return presetName.ToLowerInvariant() switch
        {
            "compact" => CompactPreset,
            "expanded" => ExpandedPreset,
            _ => Default
        };
    }
}
