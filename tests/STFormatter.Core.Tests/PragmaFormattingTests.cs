using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

/// <summary>
/// Pragmas are trivia to the formatter, which makes them easy to drop or
/// reorder silently. These tests assert exact, byte-for-byte preservation of
/// every documented pragma family in every structural position, using the
/// PragmaShowcase sample files plus focused inline cases.
/// </summary>
public class PragmaFormattingTests
{
    private static string SamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "samples")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "samples", "SampleSTFiles");
    }

    private static string FormatXml(string fileName)
    {
        var xml = File.ReadAllText(Path.Combine(SamplesDir(), fileName));
        new TwinCatXmlFormatter().FormatXmlContent(xml, out var formatted, out _, out _);
        return formatted;
    }

    [Fact]
    public void ShowcasePou_EveryPragmaSurvivesByteForByte()
    {
        var source = File.ReadAllText(Path.Combine(SamplesDir(), "PragmaShowcase.TcPOU"));
        var formatted = FormatXml("PragmaShowcase.TcPOU");

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(source, @"\{[^}]*\}"))
        {
            // Skip XML Id GUIDs, which also use braces
            if (m.Value.Contains("-") && !m.Value.Contains(" ") && !m.Value.Contains("'"))
                continue;
            Assert.Contains(m.Value, formatted);
        }
    }

    [Fact]
    public void ShowcaseDut_EveryPragmaSurvivesByteForByte()
    {
        var source = File.ReadAllText(Path.Combine(SamplesDir(), "PragmaShowcaseTypes.TcDUT"));
        var formatted = FormatXml("PragmaShowcaseTypes.TcDUT");

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(source, @"\{[^}]*\}"))
        {
            if (m.Value.Contains("-") && !m.Value.Contains(" ") && !m.Value.Contains("'"))
                continue;
            Assert.Contains(m.Value, formatted);
        }
    }

    [Fact]
    public void MemberPragma_StaysOnOwnLineAboveItsMember()
    {
        var formatted = FormatXml("PragmaShowcase.TcPOU");
        var lines = formatted.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        int pragmaIdx = lines.FindIndex(l => l.Trim() == "{attribute 'hide'}");
        Assert.True(pragmaIdx >= 0, "{attribute 'hide'} line not found");
        Assert.Contains("bHidden", lines[pragmaIdx + 1]);
    }

    [Fact]
    public void StackedPouPragmas_KeepTheirOrder()
    {
        var formatted = FormatXml("PragmaShowcase.TcPOU");
        int a = formatted.IndexOf("{attribute 'qualified_only'}", StringComparison.Ordinal);
        int b = formatted.IndexOf("{attribute 'strict'}", StringComparison.Ordinal);
        int c = formatted.IndexOf("{attribute 'reflection'}", StringComparison.Ordinal);
        int pou = formatted.IndexOf("FUNCTION_BLOCK FB_PragmaShowcase", StringComparison.Ordinal);
        Assert.True(a >= 0 && a < b && b < c && c < pou, $"order broken: {a},{b},{c},{pou}");
    }

    [Fact]
    public void ConditionalCompilation_BranchOrderPreserved()
    {
        var formatted = FormatXml("PragmaShowcase.TcPOU");

        // The declaration has an {IF}...{END_IF} block without ELSE
        int declIf = formatted.IndexOf("{IF defined (constant: GVL.bSimulation)}", StringComparison.Ordinal);
        int declEnd = formatted.IndexOf("{END_IF}", declIf, StringComparison.Ordinal);
        Assert.True(declIf >= 0 && declEnd > declIf, $"declaration conditional broken: {declIf},{declEnd}");

        // The implementation has a full {IF}...{ELSE}...{END_IF} block
        int implIf = formatted.IndexOf("{IF defined (constant: GVL.bSimulation)}", declEnd, StringComparison.Ordinal);
        int elseIdx = formatted.IndexOf("{ELSE}", implIf, StringComparison.Ordinal);
        int implEnd = formatted.IndexOf("{END_IF}", elseIdx, StringComparison.Ordinal);
        Assert.True(implIf > declEnd && elseIdx > implIf && implEnd > elseIdx,
            $"implementation conditional broken: {implIf},{elseIdx},{implEnd}");
    }

    [Fact]
    public void WarningDisableRestore_PairsSurroundCode()
    {
        var formatted = FormatXml("PragmaShowcase.TcPOU");
        int disable = formatted.IndexOf("{warning disable C0195}", StringComparison.Ordinal);
        int stmt = formatted.IndexOf("DINT_TO_INT", StringComparison.Ordinal);
        int restore = formatted.IndexOf("{warning restore C0195}", StringComparison.Ordinal);
        Assert.True(disable >= 0 && disable < stmt && stmt < restore,
            $"warning pair broken: {disable},{stmt},{restore}");
    }

    [Fact]
    public void ShowcaseFiles_AreIdempotent()
    {
        foreach (var file in new[] { "PragmaShowcase.TcPOU", "PragmaShowcaseTypes.TcDUT" })
        {
            var xml = File.ReadAllText(Path.Combine(SamplesDir(), file));
            var formatter = new TwinCatXmlFormatter();
            formatter.FormatXmlContent(xml, out var once, out _, out _);
            bool changedAgain = formatter.FormatXmlContent(once, out var twice, out _, out _);
            Assert.False(changedAgain, $"{file}: second pass changed the file");
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void PragmaWithDoubleQuotedBrace_DoesNotTruncate()
    {
        var src = "{attribute addProperty Name \"weird}value\"}\nPROGRAM P\nVAR\nx : BOOL;\nEND_VAR\nEND_PROGRAM";
        var formatted = new FormattingEngine().Format(src);
        Assert.Contains("{attribute addProperty Name \"weird}value\"}", formatted);
        Assert.Contains("END_PROGRAM", formatted);
    }

    [Fact]
    public void LibraryPragmas_Survive()
    {
        var src = "{library private}\nFUNCTION F_Internal : BOOL\nVAR_INPUT\nx : BOOL;\nEND_VAR\nF_Internal := x;\nEND_FUNCTION";
        var formatted = new FormattingEngine().Format(src);
        Assert.Contains("{library private}", formatted);
        Assert.Contains("END_FUNCTION", formatted);
    }
}
