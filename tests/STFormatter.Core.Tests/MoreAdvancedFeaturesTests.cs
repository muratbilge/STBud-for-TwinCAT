using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class PresetTests
{
    [Fact]
    public void DefaultPreset_HasCorrectValues()
    {
        var config = FormattingConfiguration.Default;

        Assert.Equal("spaces", config.IndentStyle);
        Assert.Equal(4, config.IndentSize);
        Assert.Equal("upper", config.KeywordCasing);
        Assert.True(config.AlignVariableDeclarations);
        Assert.True(config.AlignAssignments);
        Assert.Equal(120, config.MaxLineLength);
        Assert.Equal(2, config.EmptyLinesBetweenPOUs);
    }

    [Fact]
    public void CompactPreset_HasCorrectValues()
    {
        var config = FormattingConfiguration.CompactPreset;

        Assert.Equal("spaces", config.IndentStyle);
        Assert.Equal(2, config.IndentSize);
        Assert.Equal("lower", config.KeywordCasing);
        Assert.False(config.AlignVariableDeclarations);
        Assert.False(config.AlignAssignments);
        Assert.Equal(120, config.MaxLineLength);
        Assert.Equal(1, config.EmptyLinesBetweenPOUs);
    }

    [Fact]
    public void ExpandedPreset_HasCorrectValues()
    {
        var config = FormattingConfiguration.ExpandedPreset;

        Assert.Equal("spaces", config.IndentStyle);
        Assert.Equal(4, config.IndentSize);
        Assert.Equal("upper", config.KeywordCasing);
        Assert.True(config.AlignVariableDeclarations);
        Assert.True(config.AlignAssignments);
        Assert.Equal(80, config.MaxLineLength);
        Assert.Equal(3, config.EmptyLinesBetweenPOUs);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("compact")]
    [InlineData("expanded")]
    public void FromPreset_ReturnsCorrectPreset(string presetName)
    {
        var config = FormattingConfiguration.FromPreset(presetName);
        Assert.NotNull(config);
    }

    [Fact]
    public void FromPreset_UnknownReturnsDefault()
    {
        var config = FormattingConfiguration.FromPreset("unknown");
        Assert.Equal(FormattingConfiguration.Default.IndentSize, config.IndentSize);
    }
}

public class AssignmentAlignmentTests
{
    [Fact]
    public void Formatter_Aligns_ConsecutiveAssignments()
    {
        var source = @"PROGRAM Test
x := 1;
longName := 2;
y := 3;
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            AlignAssignments = true
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var assignmentLines = lines.Where(l => l.Contains(":=") && l.Contains(";")).ToList();

        Assert.True(assignmentLines.Count >= 3);

        // Check that := operators are aligned
        var operatorPositions = assignmentLines.Select(l => l.IndexOf(":=")).Where(p => p >= 0).ToList();
        Assert.True(operatorPositions.Count >= 3);

        var firstPos = operatorPositions[0];
        foreach (var pos in operatorPositions)
        {
            Assert.Equal(firstPos, pos);
        }
    }

    [Fact]
    public void Formatter_Does_Not_Align_Assignments_When_Disabled()
    {
        var source = @"PROGRAM Test
x := 1;
longName := 2;
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            AlignAssignments = false
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        var lines = formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var assignmentLines = lines.Where(l => l.Contains(":=") && l.Contains(";")).ToList();

        var operatorPositions = assignmentLines.Select(l => l.IndexOf(":=")).Where(p => p >= 0).ToList();
        Assert.True(operatorPositions.Count >= 2);

        // Positions should differ because of different name lengths
        Assert.NotEqual(operatorPositions[0], operatorPositions[1]);
    }

    [Fact]
    public void Formatter_Does_Not_Align_NonConsecutiveAssignments()
    {
        var source = @"PROGRAM Test
x := 1;
IF TRUE THEN
    y := 2;
END_IF;
z := 3;
END_PROGRAM";

        var config = new FormattingConfiguration
        {
            AlignAssignments = true
        };
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        // Should format without errors - assignments are not consecutive
        Assert.Contains("x := 1;", formatted);
        Assert.Contains("y := 2;", formatted);
        Assert.Contains("z := 3;", formatted);
    }
}

public class RegionPreservationTests
{
    [Fact]
    public void Formatter_Preserves_RegionPragmas()
    {
        var source = @"PROGRAM Test
VAR
    x : INT;
END_VAR

{region 'My Region'}
x := 1;
y := 2;
{endregion}

END_PROGRAM";

        var engine = new FormattingEngine();
        var formatted = engine.Format(source);

        // Region pragmas should be preserved
        Assert.Contains("{region 'My Region'}", formatted);
        Assert.Contains("{endregion}", formatted);
    }

    [Fact]
    public void Formatter_Preserves_AttributePragmas()
    {
        var source = @"PROGRAM Test
{attribute 'hide'}
VAR
    x : INT;
END_VAR
END_PROGRAM";

        var engine = new FormattingEngine();
        var formatted = engine.Format(source);

        // Attribute pragma should be preserved
        Assert.Contains("{attribute 'hide'}", formatted);
    }
}
