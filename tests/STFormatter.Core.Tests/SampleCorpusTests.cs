using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

/// <summary>
/// Regression tests over the checked-in sample corpus: every sample file must
/// format without errors, and formatting must be idempotent (formatting already
/// formatted output changes nothing).
/// </summary>
public class SampleCorpusTests
{
    private static readonly string SamplesDir = FindSamplesDir();

    private static string FindSamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("samples/ directory not found above " + AppContext.BaseDirectory);
    }

    public static TheoryData<string> PlainStFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(SamplesDir, "*.st", SearchOption.AllDirectories))
            data.Add(Path.GetRelativePath(SamplesDir, f));
        return data;
    }

    public static TheoryData<string> TwinCatXmlFiles()
    {
        var data = new TheoryData<string>();
        foreach (var pattern in new[] { "*.TcPOU", "*.TcDUT", "*.TcGVL" })
            foreach (var f in Directory.GetFiles(SamplesDir, pattern, SearchOption.AllDirectories))
                data.Add(Path.GetRelativePath(SamplesDir, f));
        return data;
    }

    [Theory]
    [MemberData(nameof(PlainStFiles))]
    public void PlainStFile_FormatsAndIsIdempotent(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(SamplesDir, relativePath));
        var engine = new FormattingEngine();

        var once = engine.Format(source);
        Assert.False(string.IsNullOrWhiteSpace(once), "formatting produced empty output");

        var twice = engine.Format(once);
        Assert.Equal(once, twice);
    }

    [Theory]
    [MemberData(nameof(TwinCatXmlFiles))]
    public void TwinCatXmlFile_FormatsAndIsIdempotent(string relativePath)
    {
        var xml = File.ReadAllText(Path.Combine(SamplesDir, relativePath));
        var formatter = new TwinCatXmlFormatter();

        formatter.FormatXmlContent(xml, out var once, out _, out _);
        Assert.Contains("<![CDATA[", once);

        bool changedAgain = formatter.FormatXmlContent(once, out var twice, out _, out _);
        Assert.False(changedAgain, "second format pass still changed the file");
        Assert.Equal(once, twice);
    }
}
