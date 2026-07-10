using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// FB/function calls with many named (:=) arguments wrap one argument per line, aligned
// under the first argument. Threshold: WrapCallArgumentsAt (default 4; 0 disables).
public class WrapCallArgumentsTests
{
    private const string FiveArgCall =
        "PROGRAM P_Sample\nVAR\n fbTest : FB_Test;\n s : ST_Sample;\nEND_VAR\n" +
        "fbTest(a:=233, b:='dfd', c:=s.a, othervar:=s.b.c.d, othervar2:=s.z);\n";

    [Fact]
    public void Five_named_arguments_wrap_one_per_line_aligned()
    {
        var result = new FormattingEngine().Format(FiveArgCall).Replace("\r\n", "\n");

        // First argument stays on the call line; the rest align under it.
        Assert.Contains("fbTest(a := 233,\n", result);
        int callLine = result.IndexOf("fbTest(");
        int argColumn = result.IndexOf("a := 233", callLine) - (result.LastIndexOf('\n', callLine) + 1);
        string pad = new string(' ', argColumn);
        Assert.Contains("\n" + pad + "b := 'dfd',\n", result);
        Assert.Contains("\n" + pad + "othervar := s.b.c.d,\n", result);
        Assert.Contains("\n" + pad + "othervar2 := s.z);", result);
    }

    [Fact]
    public void Three_named_arguments_stay_on_one_line()
    {
        var result = new FormattingEngine().Format(
            "PROGRAM P_Sample\nVAR\n fbTest : FB_Test;\nEND_VAR\nfbTest(a:=1, b:=2, c:=3);\n");

        Assert.Contains("fbTest(a := 1, b := 2, c := 3);", result);
    }

    [Fact]
    public void Threshold_zero_disables_wrapping()
    {
        var config = new FormattingConfiguration { WrapCallArgumentsAt = 0 };
        var result = new FormattingEngine(config).Format(FiveArgCall);

        Assert.Contains("fbTest(a := 233, b := 'dfd',", result);
    }

    [Fact]
    public void Wrapped_call_format_is_idempotent()
    {
        var engine = new FormattingEngine();
        var once = engine.Format(FiveArgCall);
        var twice = engine.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Positional_arguments_do_not_trigger_wrapping()
    {
        var result = new FormattingEngine().Format(
            "PROGRAM P_Sample\nVAR\n x : INT;\nEND_VAR\nx := F_Calc(1, 2, 3, 4, 5);\n");

        Assert.Contains("F_Calc(1, 2, 3, 4, 5);", result);
    }
}
