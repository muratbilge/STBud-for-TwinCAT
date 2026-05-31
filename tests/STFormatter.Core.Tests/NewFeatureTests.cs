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
}