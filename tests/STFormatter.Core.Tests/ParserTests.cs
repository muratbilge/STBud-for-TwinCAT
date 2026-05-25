using STFormatter.Core.Parsing;
using STFormatter.Core.Syntax;
using STFormatter.Core.Text;

namespace STFormatter.Core.Tests;

public class ParserTests
{
    [Fact]
    public void Parser_Parses_SimpleProgram()
    {
        var source = @"PROGRAM MainProgram
VAR
    counter : INT;
END_VAR
counter := 0;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(SyntaxKind.CompilationUnit, tree.Root.Kind);
        Assert.Single(tree.Root.ChildNodes);
        Assert.Equal(SyntaxKind.ProgramDeclaration, tree.Root.ChildNodes[0].Kind);
    }

    [Fact]
    public void Parser_Parses_FunctionBlock()
    {
        var source = @"FUNCTION_BLOCK FB_Motor
VAR_INPUT
    speed : INT;
END_VAR
END_FUNCTION_BLOCK";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(tree.Root.ChildNodes);
        Assert.Equal(SyntaxKind.FunctionBlockDeclaration, tree.Root.ChildNodes[0].Kind);
    }

    [Fact]
    public void Parser_Parses_IfStatement()
    {
        var source = @"PROGRAM Test
IF x > 0 THEN
    y := 1;
ELSIF x < 0 THEN
    y := -1;
ELSE
    y := 0;
END_IF;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_ForStatement()
    {
        var source = @"PROGRAM Test
FOR i := 0 TO 9 DO
    arr[i] := i;
END_FOR;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_CaseStatement()
    {
        var source = @"PROGRAM Test
CASE x OF
    1:
        y := 1;
    2, 3:
        y := 2;
ELSE
    y := 0;
END_CASE;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_ArrayType()
    {
        var source = @"PROGRAM Test
VAR
    values : ARRAY[0..9] OF INT;
END_VAR
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_StringType()
    {
        var source = @"PROGRAM Test
VAR
    msg : STRING[80];
END_VAR
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_FunctionCall()
    {
        var source = @"PROGRAM Test
fbMotor.Run(speed := 100);
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Parses_MemberAccess()
    {
        var source = @"PROGRAM Test
x := fbMotor.speed;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Parser_Recovers_From_MissingSemicolon()
    {
        var source = @"PROGRAM Test
x := 1
y := 2;
END_PROGRAM";

        var text = SourceText.From(source);
        var parser = new Parser(text);
        var tree = parser.Parse();

        // Should have a diagnostic but still parse
        Assert.NotEmpty(tree.Diagnostics);
        Assert.Single(tree.Root.ChildNodes);
    }
}
