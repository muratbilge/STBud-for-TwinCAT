using System.IO;
using STFormatter.Core.Formatting;
using STFormatter.Core.Configuration;
using STFormatter.Core.Text;

namespace STFormatter.Core.Tests;

public class AlignmentTests
{
    [Fact]
    public void Formatter_Aligns_VariableDeclarations()
    {
        var source = @"PROGRAM Test
VAR
    x : INT;
    longVariableName : BOOL;
    y : REAL := 1.0;
    z : STRING[80];
END_VAR
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            AlignVariableDeclarations = true
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        // Check that declarations are aligned
        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var declLines = lines.Where(l => l.Contains(":") && l.Contains(";") && !l.Contains("PROGRAM") && !l.Contains("END_VAR")).ToList();

        Assert.True(declLines.Count >= 3);

        // Check that colons are roughly aligned (they should be at similar positions)
        var colonPositions = declLines.Select(l => l.IndexOf(':')).Where(p => p >= 0).ToList();
        Assert.True(colonPositions.Count >= 3);

        // All colons should be at the same position when alignment is enabled
        var firstColonPos = colonPositions[0];
        foreach (var pos in colonPositions)
        {
            Assert.Equal(firstColonPos, pos);
        }
    }

    [Fact]
    public void Formatter_Does_Not_Align_When_Disabled()
    {
        var source = @"PROGRAM Test
VAR
    x : INT;
    longVariableName : BOOL;
END_VAR
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            AlignVariableDeclarations = false
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var declLines = lines.Where(l => l.Contains(":") && l.Contains(";") && !l.Contains("PROGRAM")).ToList();

        // Colons should NOT be aligned when disabled
        var colonPositions = declLines.Select(l => l.IndexOf(':')).Where(p => p >= 0).ToList();
        Assert.True(colonPositions.Count >= 2);

        // Positions should differ because of different name lengths
        Assert.NotEqual(colonPositions[0], colonPositions[1]);
    }
}

public class LineWrappingTests
{
    [Fact]
    public void Formatter_Wraps_Long_Lines()
    {
        var source = @"PROGRAM Test
veryLongVariableName := anotherVeryLongVariableName + yetAnotherVeryLongVariableName;
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            MaxLineLength = 40,
            IndentSize = 4
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        // Should contain line breaks because the line exceeds 40 chars
        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var statementLines = lines.Where(l => l.Contains(":=") || l.Contains("+")).ToList();

        // The statement should be split across multiple lines
        Assert.True(statementLines.Count >= 2, $"Expected wrapping but got:\n{formatted}");
    }

    [Fact]
    public void Formatter_Does_Not_Wrap_When_Disabled()
    {
        var source = @"PROGRAM Test
x := a + b + c;
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            MaxLineLength = 0 // disabled
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        // Should be on a single line (plus PROGRAM/END_PROGRAM lines)
        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var assignmentLines = lines.Where(l => l.Contains(":=")).ToList();

        Assert.Single(assignmentLines);
    }
}

public class EditorConfigTests
{
    [Fact]
    public void EditorConfig_Parses_IndentSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var editorConfigPath = Path.Combine(tempDir, ".editorconfig");
            File.WriteAllText(editorConfigPath, @"
root = true

[*]
indent_style = tab
indent_size = 4
end_of_line = lf
max_line_length = 80

[*.st]
st_keyword_casing = lower
st_space_around_operators = true
st_align_variable_declarations = true
st_empty_lines_between_pous = 3
");

            var testFile = Path.Combine(tempDir, "test.st");
            File.WriteAllText(testFile, "PROGRAM Test\nEND_PROGRAM");

            var config = EditorConfigParser.LoadForFile(testFile);
            Assert.NotNull(config);
            Assert.Equal("tabs", config.IndentStyle);
            Assert.Equal(4, config.IndentSize);
            Assert.Equal("lf", config.NewLineStyle);
            Assert.Equal(80, config.MaxLineLength);
            Assert.Equal("lower", config.KeywordCasing);
            Assert.True(config.SpaceAroundOperators);
            Assert.True(config.AlignVariableDeclarations);
            Assert.Equal(3, config.EmptyLinesBetweenPOUs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EditorConfig_Merges_Settings_From_Parent()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subDir = Path.Combine(rootDir, "sub");
        Directory.CreateDirectory(subDir);

        try
        {
            // Root .editorconfig
            File.WriteAllText(Path.Combine(rootDir, ".editorconfig"), @"
root = true
[*]
indent_size = 2
max_line_length = 120
st_keyword_casing = upper
");

            // Subdirectory .editorconfig overrides some settings
            File.WriteAllText(Path.Combine(subDir, ".editorconfig"), @"
[*.st]
indent_size = 4
st_keyword_casing = lower
");

            var testFile = Path.Combine(subDir, "test.st");
            File.WriteAllText(testFile, "PROGRAM Test\nEND_PROGRAM");

            var config = EditorConfigParser.LoadForFile(testFile);
            Assert.NotNull(config);
            // From subdirectory
            Assert.Equal(4, config.IndentSize);
            Assert.Equal("lower", config.KeywordCasing);
            // From root (not overridden)
            Assert.Equal(120, config.MaxLineLength);
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
    }

    [Fact]
    public void EditorConfig_Returns_Null_When_Not_Found()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var testFile = Path.Combine(tempDir, "test.st");
            File.WriteAllText(testFile, "PROGRAM Test\nEND_PROGRAM");

            var config = EditorConfigParser.LoadForFile(testFile);
            Assert.Null(config);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
