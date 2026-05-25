using System.IO;
using System.Text.Json;
using STFormatter.Core.Formatting;
using STFormatter.Core.Configuration;

namespace STFormatter.Core.Tests;

public class CliFeatureTests
{
    [Fact]
    public void FormattingConfiguration_CanBeSerialized_ToJson()
    {
        var config = FormattingConfiguration.STweepPreset;
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(json);
        Assert.Contains("\"indentStyle\":", json);
        Assert.Contains("\"indentSize\":", json);
        Assert.Contains("\"keywordCasing\":", json);
    }

    [Fact]
    public void FormattingConfiguration_CanBeDeserialized_FromJson()
    {
        var original = new FormattingConfiguration
        {
            IndentStyle = "tabs",
            IndentSize = 8,
            KeywordCasing = "pascal",
            AlignVariableDeclarations = false,
            MaxLineLength = 200
        };

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var deserialized = JsonSerializer.Deserialize<FormattingConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal(original.IndentStyle, deserialized.IndentStyle);
        Assert.Equal(original.IndentSize, deserialized.IndentSize);
        Assert.Equal(original.KeywordCasing, deserialized.KeywordCasing);
        Assert.Equal(original.AlignVariableDeclarations, deserialized.AlignVariableDeclarations);
        Assert.Equal(original.MaxLineLength, deserialized.MaxLineLength);
    }

    [Fact]
    public void BatchFormatting_FindsMultipleFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create test files
            File.WriteAllText(Path.Combine(tempDir, "file1.st"), "PROGRAM P1\nEND_PROGRAM");
            File.WriteAllText(Path.Combine(tempDir, "file2.st"), "PROGRAM P2\nEND_PROGRAM");
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "not st code");

            var files = Directory.GetFiles(tempDir, "*.st");
            Assert.Equal(2, files.Length);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EditorConfig_CanBeGenerated_FromPreset()
    {
        var config = FormattingConfiguration.CompactPreset;
        var editorConfig = $@"root = true

[*]
indent_style = {(config.IndentStyle == "tabs" ? "tab" : "space")}
indent_size = {config.IndentSize}
end_of_line = {config.NewLineStyle}
max_line_length = {config.MaxLineLength}

[*.st]
st_keyword_casing = {config.KeywordCasing}
st_space_around_operators = {(config.SpaceAroundOperators ? "true" : "false")}
st_align_variable_declarations = {(config.AlignVariableDeclarations ? "true" : "false")}
st_align_assignments = {(config.AlignAssignments ? "true" : "false")}
st_empty_lines_between_pous = {config.EmptyLinesBetweenPOUs}
st_empty_lines_between_var_sections = {config.EmptyLinesBetweenVarSections}
st_format_on_save = {(config.FormatOnSave ? "true" : "false")}
";

        Assert.Contains("indent_style = space", editorConfig);
        Assert.Contains("indent_size = 2", editorConfig);
        Assert.Contains("st_keyword_casing = lower", editorConfig);
        Assert.Contains("st_align_variable_declarations = false", editorConfig);
    }

    [Fact]
    public void Preset_ProducesDifferentOutput()
    {
        var source = @"PROGRAM Test
VAR
x:INT;
END_VAR
x:=1;
END_PROGRAM";

        // STweep preset
        var stweepConfig = FormattingConfiguration.STweepPreset;
        var stweepEngine = new FormattingEngine(stweepConfig);
        var stweepResult = stweepEngine.Format(source);

        // Compact preset
        var compactConfig = FormattingConfiguration.CompactPreset;
        var compactEngine = new FormattingEngine(compactConfig);
        var compactResult = compactEngine.Format(source);

        // They should produce different output
        Assert.NotEqual(stweepResult, compactResult);

        // Compact should have 2-space indent and lower case
        Assert.Contains("  x : int;", compactResult);

        // STweep should have 4-space indent and upper case
        Assert.Contains("    x : INT;", stweepResult);
    }
}
