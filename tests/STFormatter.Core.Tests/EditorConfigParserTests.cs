using STFormatter.Core.Configuration;

namespace STFormatter.Core.Tests;

public class EditorConfigParserTests : IDisposable
{
    private readonly string _root;

    public EditorConfigParserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "STBudEc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Write(string relativeDir, string content)
    {
        var dir = Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), content);
        return dir;
    }

    [Fact]
    public void TopLevelRootTrue_StopsUpwardWalk()
    {
        // Parent config that must NOT be picked up
        Write("", "[*]\nst_keyword_casing = lower\n");
        // Child declares root = true in the preamble (the spec-standard place)
        var child = Write("proj", "root = true\n\n[*]\nindent_size = 2\n");

        var config = EditorConfigParser.LoadFromDirectory(child, Path.Combine(child, "a.st"));

        Assert.NotNull(config);
        Assert.Equal(2, config!.IndentSize);
        Assert.Equal("upper", config.KeywordCasing); // parent's "lower" not applied
    }

    [Fact]
    public void WithoutRoot_ParentConfigApplies()
    {
        Write("", "[*.st]\nst_keyword_casing = lower\n");
        var child = Write("proj", "[*]\nindent_size = 2\n");

        var config = EditorConfigParser.LoadFromDirectory(child, Path.Combine(child, "a.st"));

        Assert.NotNull(config);
        Assert.Equal(2, config!.IndentSize);
        Assert.Equal("lower", config.KeywordCasing);
    }

    [Fact]
    public void LaterSectionsOverrideEarlierOnesWithinAFile()
    {
        var dir = Write("proj", "root = true\n\n[*]\nindent_size = 2\n\n[*.st]\nindent_size = 8\n");

        var config = EditorConfigParser.LoadFromDirectory(dir, Path.Combine(dir, "a.st"));

        Assert.NotNull(config);
        Assert.Equal(8, config!.IndentSize); // [*.st] comes later, must win
    }

    [Fact]
    public void InnerFileOverridesOuterFile()
    {
        Write("", "[*]\nindent_size = 2\nmax_line_length = 80\n");
        var child = Write("proj", "[*]\nindent_size = 4\n");

        var config = EditorConfigParser.LoadFromDirectory(child, Path.Combine(child, "a.st"));

        Assert.NotNull(config);
        Assert.Equal(4, config!.IndentSize);     // inner wins
        Assert.Equal(80, config.MaxLineLength);  // outer-only property survives
    }

    [Fact]
    public void StPropertiesParse()
    {
        var dir = Write("proj",
            "root = true\n[*.st]\nst_keyword_casing = pascal\nst_brace_style = kr\nst_align_assignments = false\n");

        var config = EditorConfigParser.LoadFromDirectory(dir, Path.Combine(dir, "a.st"));

        Assert.NotNull(config);
        Assert.Equal("pascal", config!.KeywordCasing);
        Assert.Equal("compact", config.BraceStyle); // kr is a compact alias
        Assert.False(config.AlignAssignments);
    }
}
