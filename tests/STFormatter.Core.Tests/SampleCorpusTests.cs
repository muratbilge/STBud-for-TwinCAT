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

    // Same data-loss guard as for XML files: formatting plain ST may only
    // change whitespace and keyword casing, never the token sequence.
    [Theory]
    [MemberData(nameof(PlainStFiles))]
    public void PlainStFile_FormattingPreservesAllCodeTokens(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(SamplesDir, relativePath));
        var formatted = new FormattingEngine().Format(source);

        var before = WordTokens(source);
        var after = WordTokens(formatted);

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.True(string.Equals(before[i], after[i], StringComparison.OrdinalIgnoreCase),
                $"token {i} changed: '{before[i]}' -> '{after[i]}'");
        }
    }

    private static List<string> WordTokens(string code)
    {
        var tokens = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(code, @"[A-Za-z0-9_]+"))
            tokens.Add(m.Value);
        return tokens;
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

    // Formatting only rearranges whitespace and changes keyword casing, so the
    // sequence of code tokens inside every CDATA section must survive unchanged
    // (case-insensitively). This is the data-loss guard: a formatter bug that
    // drops or invents code fails here with the exact position of the difference.
    [Theory]
    [MemberData(nameof(TwinCatXmlFiles))]
    public void TwinCatXmlFile_FormattingPreservesAllCodeTokens(string relativePath)
    {
        var xml = File.ReadAllText(Path.Combine(SamplesDir, relativePath));
        var formatter = new TwinCatXmlFormatter();
        formatter.FormatXmlContent(xml, out var formatted, out _, out _);

        var before = ExtractCdataTokens(xml);
        var after = ExtractCdataTokens(formatted);

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.True(string.Equals(before[i], after[i], StringComparison.OrdinalIgnoreCase),
                $"token {i} changed: '{before[i]}' -> '{after[i]}'");
        }
    }

    private static List<string> ExtractCdataTokens(string xml)
    {
        var tokens = new List<string>();
        int pos = 0;
        while ((pos = xml.IndexOf("<![CDATA[", pos, StringComparison.Ordinal)) >= 0)
        {
            int start = pos + 9;
            int end = xml.IndexOf("]]>", start, StringComparison.Ordinal);
            if (end < 0) break;

            var code = xml.Substring(start, end - start);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(code, @"[A-Za-z0-9_]+"))
            {
                tokens.Add(m.Value);
            }
            pos = end + 3;
        }
        return tokens;
    }
}
