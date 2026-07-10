using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// TwinCAT convention writes a ';' after END_IF/END_WHILE/END_FOR/END_CASE. The formatter
// used to drop it (the ';' parses as an empty statement after the block), which churned
// nearly every file in the open-source corpus scan (TcUnit/TcOpen/struckig/TcBlack).
// Synthetic code per the privacy rule.
public class TrailingSemicolonTests
{
    private static string Format(string source) => new FormattingEngine().Format(source);

    private const string Source =
        "PROGRAM P_Sample\nVAR\n a : INT;\n b : BOOL;\nEND_VAR\n" +
        "WHILE a < 3 DO\n IF b THEN\n  a := a + 1;\n END_IF;\nEND_WHILE;\n" +
        "IF b THEN\n IF a > 1 THEN\n  a := 0;\n END_IF;\nEND_IF;\n" +
        "FOR a := 0 TO 3 DO\n b := TRUE;\nEND_FOR;\n" +
        "CASE a OF\n 1: b := FALSE;\nEND_CASE;\n";

    [Fact]
    public void Block_trailing_semicolons_are_preserved()
    {
        var result = Format(Source);

        Assert.Contains("END_IF;", result);
        Assert.Contains("END_WHILE;", result);
        Assert.Contains("END_FOR;", result);
        Assert.Contains("END_CASE;", result);
        // The ';' stays glued to the END keyword - never on its own line.
        Assert.DoesNotContain("\n;", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Nested_end_if_semicolons_survive()
    {
        var result = Format(Source);

        // Inner END_IF; directly before outer END_IF; - both keep their semicolon.
        var normalized = result.Replace("\r\n", "\n").Replace(" ", "");
        Assert.Contains("END_IF;\nEND_IF;", normalized);
    }

    [Fact]
    public void Blocks_without_semicolon_gain_none()
    {
        var result = Format("PROGRAM P_Sample\nVAR\n a : INT;\nEND_VAR\nIF a > 0 THEN\n a := 0;\nEND_IF\n");

        Assert.DoesNotContain("END_IF;", result);
    }

    [Fact]
    public void Format_with_trailing_semicolons_is_idempotent()
    {
        var once = Format(Source);
        var twice = Format(once);
        Assert.Equal(once, twice);
    }
}
