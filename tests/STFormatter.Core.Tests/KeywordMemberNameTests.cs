using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// Members named after keywords (stFb.Test.Var.x - 'Var' lexes as VarKeyword) must parse:
// the parse error used to silently truncate the formatted output mid-expression.
public class KeywordMemberNameTests
{
    [Theory]
    [InlineData("Var")]
    [InlineData("Type")]
    [InlineData("Program")]
    [InlineData("Do")]
    public void Keyword_named_members_parse_and_survive_formatting(string member)
    {
        string source =
            "PROGRAM P_Sample\nVAR\n a : INT;\n s : ST_A;\nEND_VAR\n" +
            $"a := s.Test.{member}.x;\n" +
            "a := a + 1;\nEND_PROGRAM\n";

        Assert.True(TwinCatXmlFormatter.ParsesWithoutErrors(source));

        var result = new FormattingEngine().Format(source);
        Assert.Contains($"s.Test.{member}.x", result);
        Assert.Contains("a := a + 1;", result); // nothing after the member access is lost
    }

    [Fact]
    public void Keyword_member_in_named_call_argument_survives()
    {
        string source =
            "PROGRAM P_Sample\nVAR\n fb : FB_Test;\n s : ST_A;\nEND_VAR\n" +
            "fb(a := 1, b := 2, c := 3, other := s.Test.Var.ttt, other2 := s.Test.Var.zz);\nEND_PROGRAM\n";

        Assert.True(TwinCatXmlFormatter.ParsesWithoutErrors(source));

        var result = new FormattingEngine().Format(source);
        Assert.Contains("s.Test.Var.ttt", result);
        Assert.Contains("other2 := s.Test.Var.zz);", result);
    }
}
