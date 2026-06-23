using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class ChainedAssignmentTests
{
    private static string FormatBody(string body) => new FormattingEngine().FormatBody(body);

    [Fact]
    public void SimpleChain_FormatsAndRoundTrips()
    {
        var result = FormatBody("a:=b:=c;");
        Assert.Contains("a := b := c;", result);
        // idempotent
        Assert.Equal(result, FormatBody(result.Trim()));
    }

    [Fact]
    public void MemberAndPointerDerefChain_Preserved()
    {
        var result = FormatBody("x.m := y.n := p^.f.g;");
        Assert.Contains("x.m := y.n := p^.f.g;", result);
    }

    [Fact]
    public void ThreeLevelChain_AllTargetsPreserved()
    {
        var result = FormatBody("w := x := y := z;");
        Assert.Contains("w := x := y := z;", result);
    }

    [Fact]
    public void ChainedAssignment_BodyParsesCleanly()
    {
        // The data-loss guard must treat a chained-assignment body as valid
        // (this is what made a real-world IF block format-able again).
        var engine = new FormattingEngine();
        Assert.True(engine.BodyParsesCleanly(
            "a.f1 := b.g.f1 := p^.f1;\na.f2 := p^.f2;"));
    }

    [Fact]
    public void SingleAssignment_Unaffected()
    {
        Assert.Contains("a := b;", FormatBody("a:=b;"));
    }
}
