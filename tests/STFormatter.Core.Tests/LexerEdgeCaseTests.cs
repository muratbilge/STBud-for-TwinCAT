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
    public void TimeLiteral_AsArgumentExpression_ParsesWithoutErrors()
    {
        // Time literals in expressions previously produced parser errors that
        // recovery papered over; the FormatBody error guard then refused to
        // format such bodies at all (field report: CASE body with TON calls).
        var body = "TON_Fill(IN := TRUE, PT := T#10s);\nIF tonX.ET > T#1S THEN\n  x := TRUE;\nEND_IF";
        var engine = new FormattingEngine();
        var result = engine.FormatBody(body);

        Assert.NotEqual(body, result); // guard must not block formatting
        Assert.Contains("T#10s", result);
        Assert.Contains("T#1S", result);
    }

    [Fact]
    public void LongIfCondition_WrapsWhenEnabled_StaysSingleLineWhenDisabled()
    {
        var src = "PROGRAM P\nVAR\na : BOOL; b : BOOL; c : BOOL; d : BOOL; e : BOOL; f : BOOL; g : BOOL;\nEND_VAR\n" +
                  "IF aVeryLongCondition1 AND aVeryLongCondition2 AND aVeryLongCondition3 AND aVeryLongCondition4 AND aVeryLongCondition5 AND aVeryLongCondition6 THEN\n" +
                  "    a := TRUE;\nEND_IF\nEND_PROGRAM";

        var wrapped = new FormattingEngine(
            new STFormatter.Core.Formatting.FormattingConfiguration { MaxLineLength = 80 }).Format(src);
        var ifLine = wrapped.Split('\n').First(l => l.Contains("IF aVeryLongCondition1"));
        Assert.True(ifLine.TrimEnd().Length <= 90, $"IF line not wrapped: {ifLine.Length} chars");

        var unwrapped = new FormattingEngine(
            new STFormatter.Core.Formatting.FormattingConfiguration { MaxLineLength = 80, WrapLongLines = false }).Format(src);
        var ifLine2 = unwrapped.Split('\n').First(l => l.Contains("IF aVeryLongCondition1"));
        Assert.Contains("aVeryLongCondition6 THEN", ifLine2); // single line, no wrap
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
