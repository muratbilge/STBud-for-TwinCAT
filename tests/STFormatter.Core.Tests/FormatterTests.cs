using STFormatter.Core.Formatting;
using STFormatter.Core.Text;

namespace STFormatter.Core.Tests;

public class FormatterTests
{
    [Fact]
    public void Formatter_Formats_SimpleProgram()
    {
        var source = @"PROGRAM   MainProgram
VAR
counter:INT:=0;
END_VAR
END_PROGRAM";

        var expected = @"PROGRAM MainProgram
VAR
    counter : INT := 0;
END_VAR
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }

    [Fact]
    public void Formatter_Formats_IfStatement()
    {
        var source = @"PROGRAM Test
IF x>0 THEN
y:=1;
ELSE
y:=0;
END_IF;
END_PROGRAM";

        var expected = @"PROGRAM Test
    IF x > 0 THEN
        y := 1;
    ELSE
        y := 0;
    END_IF;
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }

    [Fact]
    public void Formatter_Formats_ForStatement()
    {
        var source = @"PROGRAM Test
FOR i:=0 TO 9 DO
arr[i]:=i;
END_FOR;
END_PROGRAM";

        var expected = @"PROGRAM Test
    FOR i := 0 TO 9 DO
        arr[i] := i;
    END_FOR;
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }

    [Fact]
    public void Formatter_Formats_CaseStatement()
    {
        var source = @"PROGRAM Test
CASE x OF
1:y:=1;
2,3:y:=2;
ELSE
y:=0;
END_CASE;
END_PROGRAM";

        var expected = @"PROGRAM Test
    CASE x OF
        1: y := 1;
        2, 3: y := 2;
    ELSE
        y := 0;
    END_CASE;
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }

    [Fact]
    public void Formatter_Formats_ArrayDeclaration()
    {
        var source = @"PROGRAM Test
VAR
values:ARRAY[0..9]OF INT;
END_VAR
END_PROGRAM";

        var expected = @"PROGRAM Test
VAR
    values : ARRAY[0..9] OF INT;
END_VAR
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }

    [Fact]
    public void Formatter_Preserves_Comments()
    {
        var source = @"PROGRAM Test
VAR
(* This is a counter *)
counter:INT;
END_VAR
END_PROGRAM";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Contains("(* This is a counter *)", result);
    }

    [Fact]
    public void Formatter_Converts_KeywordCasing()
    {
        var source = @"program Test
if x > 0 then
y := 1;
end_if;
end_program";

        var expected = @"PROGRAM Test
    IF x > 0 THEN
        y := 1;
    END_IF;
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }
    [Fact]
    public void Formatter_InlineComment_StaysOnSameLine()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nVAR\nx:INT; // comment\ny:INT;\nEND_VAR\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("x : INT;", result);
        Assert.Contains("// comment", result);
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var commentLine = lines.FirstOrDefault(l => l.Contains("// comment"));
        Assert.NotNull(commentLine);
        Assert.True(commentLine.Contains("INT"), "Comment should be on same line as the variable declaration");
    }

    [Fact]
    public void Formatter_VarSectionsAtPouLevel()
    {
        var engine = new FormattingEngine();
        var source = "FUNCTION_BLOCK Test\nVAR_INPUT\nx:INT;\nEND_VAR\nEND_FUNCTION_BLOCK";
        var result = engine.Format(source);
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal("VAR_INPUT", lines[1]);
        Assert.Equal("END_VAR", lines[3]);
        Assert.True(!lines[1].StartsWith(" ") && !lines[1].StartsWith("\t"),
            "VAR_INPUT should not be indented");
    }

    [Fact]
    public void Formatter_MethodReturnType()
    {
        var engine = new FormattingEngine();
        var source = "METHOD FB_init : BOOL\nVAR_INPUT\nx:INT;\nEND_VAR\nEND_METHOD";
        var result = engine.Format(source);
        Assert.Contains("METHOD", result);
        Assert.Contains("FB_init", result);
        Assert.Contains(": BOOL", result);
        Assert.DoesNotContain(":BOOL", result);
    }

    [Fact]
    public void Formatter_OutputArgument_NoValue()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nfbtest(in := 112.1, out =>);\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("=>", result);
        Assert.DoesNotContain("= >", result);
    }
}

public static class StringExtensions
{
    public static string NormalizeLineEndings(this string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
