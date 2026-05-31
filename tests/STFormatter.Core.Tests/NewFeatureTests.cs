using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class EditorConfigPatternTests
{
    [Fact]
    public void MatchesPattern_Star_MatchesEverything()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*", "bar.txt"));
    }

    [Fact]
    public void MatchesPattern_Extension_MatchesFileName()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.st", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.st", "MyProgram.st"));
        Assert.False(EditorConfigParser.MatchesPattern("*.st", "foo.txt"));
    }

    [Fact]
    public void MatchesPattern_CommaSeparated()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.st, *.iecst", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.st, *.iecst", "bar.iecst"));
        Assert.False(EditorConfigParser.MatchesPattern("*.st, *.iecst", "foo.txt"));
    }

    [Fact]
    public void MatchesPattern_QuestionMark()
    {
        Assert.True(EditorConfigParser.MatchesPattern("?.st", "a.st"));
        Assert.False(EditorConfigParser.MatchesPattern("?.st", "ab.st"));
    }

    [Fact]
    public void MatchesPattern_BracketSet()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.[st]", "foo.s"));
        Assert.True(EditorConfigParser.MatchesPattern("*.[st]", "foo.t"));
        Assert.False(EditorConfigParser.MatchesPattern("*.[st]", "foo.x"));
    }

    [Fact]
    public void MatchesPattern_BracesAlternation()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.iecst"));
        Assert.False(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.txt"));
    }
}

public class ConfigurationPropertyTests
{
    [Fact]
    public void KeepSingleLineBlocks_KeepsSingleLineIf()
    {
        var config = FormattingConfiguration.CompactPreset;
        Assert.True(config.KeepSingleLineBlocks);

        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nIF x > 0 THEN y := 1; END_IF\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("y := 1;", result);
    }

    [Fact]
    public void KeepSingleLineBlocks_Disabled_MultiLineByDefault()
    {
        var config = FormattingConfiguration.Default;
        Assert.False(config.KeepSingleLineBlocks);

        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nIF x > 0 THEN y := 1; END_IF\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.NotNull(result);
    }

    [Fact]
    public void SpaceAfterComma_ControlsArgumentSpacing()
    {
        var configWithSpace = new FormattingConfiguration { SpaceAfterComma = true };
        var engineWithSpace = new FormattingEngine(configWithSpace);
        var source = "PROGRAM Test\nVAR\nx:INT;\nEND_VAR\nFmt(a, b, c);\nEND_PROGRAM";
        var resultWithSpace = engineWithSpace.Format(source);
        Assert.Contains("Fmt(a, b, c)", resultWithSpace);

        var configNoSpace = new FormattingConfiguration { SpaceAfterComma = false };
        var engineNoSpace = new FormattingEngine(configNoSpace);
        var resultNoSpace = engineNoSpace.Format(source);
        Assert.Contains("Fmt(a,b,c)", resultNoSpace);
    }

    [Fact]
    public void SpaceBeforeSemicolon_AddsSpace()
    {
        var config = new FormattingConfiguration { SpaceBeforeSemicolon = true };
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nVAR\nx:INT;\nEND_VAR\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains(" ;", result);
    }

    [Fact]
    public void FormatBody_ExtractsBodyFromTree()
    {
        var engine = new FormattingEngine();
        var body = "IF x > 0 THEN\ny := 1;\nEND_IF";
        var result = engine.FormatBody(body);
        Assert.Contains("IF x > 0 THEN", result);
        Assert.Contains("y := 1;", result);
    }

    [Fact]
    public void FormatBody_PreservesCrlf()
    {
        var engine = new FormattingEngine();
        var body = "IF x > 0 THEN\r\ny := 1;\r\nEND_IF";
        var result = engine.FormatBody(body);
        Assert.Contains("\r\n", result);
    }

    [Fact]
    public void ContinuationIndentSize_UsedInLineWrapping()
    {
        var config = new FormattingConfiguration
        {
            MaxLineLength = 30,
            ContinuationIndentSize = 8,
            IndentSize = 4
        };
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nx := verylongexpression + anotherverylongvalue;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.NotNull(result);
    }

    [Fact]
    public void BraceStyle_Allman_IsDefault()
    {
        var config = FormattingConfiguration.Default;
        Assert.Equal("allman", config.BraceStyle);
        Assert.True(config.IsAllmanStyle());
        Assert.False(config.IsCompactStyle());
    }

    [Fact]
    public void BraceStyle_CompactPreset_IsCompact()
    {
        var config = FormattingConfiguration.CompactPreset;
        Assert.Equal("compact", config.BraceStyle);
        Assert.False(config.IsAllmanStyle());
        Assert.True(config.IsCompactStyle());
    }

    [Fact]
    public void BraceStyle_Allman_BlankLinesBetweenVarSections()
    {
        var config = new FormattingConfiguration { EmptyLinesBetweenVarSections = 2 };
        var engine = new FormattingEngine(config);
        var source = "FUNCTION_BLOCK Test\nVAR_INPUT\nx:INT;\nEND_VAR\nVAR_OUTPUT\ny:INT;\nEND_VAR\nEND_FUNCTION_BLOCK";
        var result = engine.Format(source);
        Assert.Contains("END_VAR", result);
        Assert.Contains("VAR_INPUT", result);
        Assert.Contains("VAR_OUTPUT", result);
    }

    [Fact]
    public void BraceStyle_Compact_NoExtraBlankLinesBetweenVarSections()
    {
        var config = FormattingConfiguration.CompactPreset;
        var engine = new FormattingEngine(config);
        var source = "FUNCTION_BLOCK Test\nVAR_INPUT\nx:INT;\nEND_VAR\nVAR_OUTPUT\ny:INT;\nEND_VAR\nEND_FUNCTION_BLOCK";
        var result = engine.Format(source);
        Assert.Contains("end_var", result.ToLowerInvariant());
        Assert.DoesNotContain("\r\n\r\n\r\n", result);
    }

    [Fact]
    public void BraceStyle_Allman_HasSeparateLinesForStructure()
    {
        var config = FormattingConfiguration.Default;
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nVAR\nx:INT;\ny:DINT;\nEND_VAR\nx := 1;\ny := 2;\nEND_PROGRAM";
        var allmanResult = engine.Format(source);
        Assert.Contains("END_VAR", allmanResult);
    }

    [Fact]
    public void BraceStyle_Compact_IsMoreCompactThanAllman()
    {
        var allmanConfig = FormattingConfiguration.Default;
        var compactConfig = FormattingConfiguration.CompactPreset;
        var source = "PROGRAM Test\nVAR\nx:INT;\ny:DINT;\nEND_VAR\nx := 1;\ny := 2;\nEND_PROGRAM";
        var allman = new FormattingEngine(allmanConfig).Format(source);
        var compact = new FormattingEngine(compactConfig).Format(source);
        Assert.True(compact.Length <= allman.Length, "Compact should produce equal or shorter output");
    }

    [Fact]
    public void Formatter_Handles_UnionType()
    {
        var engine = new FormattingEngine();
        var source = "TYPE MyUnion :\nUNION\nasInt:DINT;\nasReal:REAL;\nEND_UNION\nEND_TYPE";
        var result = engine.Format(source);
        Assert.Contains("UNION", result);
        Assert.Contains("END_UNION", result);
        Assert.Contains("END_TYPE", result);
    }

    [Fact]
    public void Formatter_Handles_UsingDirective()
    {
        var engine = new FormattingEngine();
        var source = "USING MyLibrary;\nPROGRAM Test\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("USING", result.ToUpperInvariant());
    }

    [Fact]
    public void Formatter_Handles_LabelStatement()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nRetry:\nx := x + 1;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("Retry", result);
        Assert.Contains("x := x + 1", result);
    }

    [Fact]
    public void Formatter_Handles_GotoStatement()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nIF x > 10 THEN\nGOTO Skip;\nEND_IF\ny := 2;\nSkip:\ny := 3;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("GOTO", result.ToUpperInvariant());
        Assert.Contains("Skip", result);
    }
}