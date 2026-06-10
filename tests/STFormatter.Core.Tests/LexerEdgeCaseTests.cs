using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class LexerEdgeCaseTests
{
    private static string Format(string source) => new FormattingEngine().Format(source);

    [Fact]
    public void TimeLiteral_FollowedByComma_DoesNotSwallowArguments()
    {
        var src = "PROGRAM P\nVAR\nx : BOOL;\nEND_VAR\nx := F_Calc(T#1S, x);\nEND_PROGRAM";
        var result = Format(src);

        Assert.Contains("F_Calc(T#1S, x);", result);
        Assert.Contains("END_PROGRAM", result);
    }

    [Fact]
    public void TimeLiteral_FollowedByCloseParen_StaysIntact()
    {
        var src = "PROGRAM P\nVAR\nt : TON;\nx : BOOL;\nEND_VAR\nt(IN := x, PT := T#2S);\nEND_PROGRAM";
        var result = Format(src);

        Assert.Contains("PT := T#2S);", result);
        Assert.Contains("END_PROGRAM", result);
    }

    [Fact]
    public void TimeLiteral_InArrayInitializer_StaysIntact()
    {
        var src = "PROGRAM P\nVAR\na : ARRAY[1..2] OF TIME := [T#1S, T#2S];\nEND_VAR\nEND_PROGRAM";
        var result = Format(src);

        Assert.Contains("T#1S", result);
        Assert.Contains("T#2S]", result);
        Assert.Contains("END_PROGRAM", result);
    }

    [Fact]
    public void TodLiteral_ColonsAreNotDelimiters()
    {
        var src = "PROGRAM P\nVAR\ntod : TOD := TOD#06:30:00;\nEND_VAR\nEND_PROGRAM";
        var result = Format(src);

        Assert.Contains("TOD#06:30:00;", result);
    }

    [Fact]
    public void Pragma_WithBraceInsideQuotedString_IsNotTruncated()
    {
        var src = "{warning 'do not use } here'}\nPROGRAM P\nVAR\nEND_VAR\nEND_PROGRAM";
        var result = Format(src);

        Assert.Contains("{warning 'do not use } here'}", result);
        Assert.Contains("END_PROGRAM", result);
    }
}
