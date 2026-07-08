using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// The DUT name must STAY on the TYPE line ("TYPE U_Sample :"). The old layout moved it
// to the next line, leaving a bare "TYPE" first line - users read that as the name being
// deleted from their DUT (real report from a UNION). Synthetic names per the privacy rule.
public class TypeDeclarationLayoutTests
{
    private static string Format(string source) => new FormattingEngine().Format(source);

    [Fact]
    public void Union_name_stays_on_type_line()
    {
        var result = Format("TYPE U_Sample :\r\nUNION\r\n\ta \t:ST_A;\r\n\tb \t\t:ST_B;\r\n\r\nEND_UNION\r\nEND_TYPE\r\n");

        Assert.StartsWith("TYPE U_Sample :", result);
        Assert.Contains("UNION", result);
        Assert.Contains("END_UNION", result);
        Assert.Contains("END_TYPE", result);
    }

    [Fact]
    public void Struct_name_stays_on_type_line()
    {
        var result = Format("TYPE ST_Sample :\nSTRUCT\n a : INT;\nEND_STRUCT\nEND_TYPE");

        Assert.StartsWith("TYPE ST_Sample :", result);
    }

    [Fact]
    public void Alias_type_stays_inline()
    {
        var result = Format("TYPE T_Sample : INT; END_TYPE");

        Assert.Contains("TYPE T_Sample : INT;", result);
    }

    [Fact]
    public void Enum_name_stays_on_type_line()
    {
        var result = Format("TYPE E_Sample :\n(\n A := 0,\n B\n);\nEND_TYPE");

        Assert.StartsWith("TYPE E_Sample :", result);
    }

    [Fact]
    public void Union_format_is_idempotent()
    {
        var once = Format("TYPE U_Sample :\r\nUNION\r\n\ta :ST_A;\r\nEND_UNION\r\nEND_TYPE\r\n");
        var twice = Format(once);
        Assert.Equal(once, twice);
    }
}
