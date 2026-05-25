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
    END_IF
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
    END_FOR
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
    END_CASE
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
    END_IF
END_PROGRAM
";

        var engine = new FormattingEngine();
        var result = engine.Format(source);

        Assert.Equal(expected.NormalizeLineEndings(), result.NormalizeLineEndings());
    }
}

public static class StringExtensions
{
    public static string NormalizeLineEndings(this string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
