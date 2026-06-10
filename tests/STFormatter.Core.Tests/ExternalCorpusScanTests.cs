using STFormatter.Core.Formatting;
using Xunit.Abstractions;

namespace STFormatter.Core.Tests;

/// <summary>
/// Opt-in mass scan over external TwinCAT projects: point STBUD_EXTRA_CORPUS
/// (or %TEMP%\stbud-corpus) at a directory of cloned repos, e.g.
///   git clone --depth 1 https://github.com/tcunit/TcUnit
///   git clone --depth 1 https://github.com/TcOpenGroup/TcOpen
///   git clone --depth 1 https://github.com/stefanbesler/struckig
/// Every .TcPOU/.TcDUT/.TcGVL found is checked for token preservation and
/// idempotency; failures are grouped by the construct at the first diff.
/// Skips silently when no corpus directory exists (normal CI runs).
/// </summary>
public class ExternalCorpusScanTests
{
    private readonly ITestOutputHelper _out;
    public ExternalCorpusScanTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ScanExternalCorpus_NoTokenLossNoIdempotencyDrift()
    {
        var root = Environment.GetEnvironmentVariable("STBUD_EXTRA_CORPUS")
                   ?? Path.Combine(Path.GetTempPath(), "stbud-corpus");
        if (!Directory.Exists(root)) { _out.WriteLine("no corpus dir"); return; }

        var files = new List<string>();
        foreach (var pat in new[] { "*.TcPOU", "*.TcDUT", "*.TcGVL" })
            files.AddRange(Directory.GetFiles(root, pat, SearchOption.AllDirectories));

        int ok = 0, tokenLoss = 0, notIdempotent = 0, crashed = 0;
        var samples = new Dictionary<string, List<string>>(); // context -> files

        foreach (var f in files)
        {
            try
            {
                var xml = File.ReadAllText(f);
                var formatter = new TwinCatXmlFormatter();
                formatter.FormatXmlContent(xml, out var once, out _, out _);

                var before = Tokens(xml);
                var after = Tokens(once);
                int i = 0;
                while (i < before.Count && i < after.Count &&
                       string.Equals(before[i], after[i], StringComparison.OrdinalIgnoreCase)) i++;

                if (i < before.Count || i < after.Count)
                {
                    tokenLoss++;
                    var ctx = string.Join(" ", before.Skip(Math.Max(0, i - 3)).Take(7));
                    var key = string.Join(" ", before.Skip(i).Take(3));
                    if (!samples.TryGetValue(key, out var list)) samples[key] = list = new List<string>();
                    if (list.Count < 3) list.Add($"{Path.GetFileName(f)} | B:{ctx} | A:{string.Join(" ", after.Skip(Math.Max(0, i - 3)).Take(7))}");
                    continue;
                }

                formatter.FormatXmlContent(once, out var twice, out _, out _);
                if (once != twice) { notIdempotent++;
                    var key = "IDEM:" + FirstDiffLine(once, twice);
                    if (!samples.TryGetValue(key, out var list)) samples[key] = list = new List<string>();
                    if (list.Count < 3) list.Add(Path.GetFileName(f));
                    continue; }

                ok++;
            }
            catch (Exception ex)
            {
                crashed++;
                var key = "CRASH:" + ex.GetType().Name + ":" + ex.Message;
                if (!samples.TryGetValue(key, out var list)) samples[key] = list = new List<string>();
                if (list.Count < 3) list.Add(Path.GetFileName(f));
            }
        }

        _out.WriteLine($"TOTAL={files.Count} OK={ok} TOKENLOSS={tokenLoss} NOTIDEM={notIdempotent} CRASH={crashed}");
        foreach (var kvp in samples.OrderByDescending(k => k.Value.Count))
        {
            _out.WriteLine($"--- [{kvp.Key}]");
            foreach (var s in kvp.Value) _out.WriteLine($"    {s}");
        }

        Assert.Equal(0, tokenLoss);
        Assert.Equal(0, notIdempotent);
        Assert.Equal(0, crashed);
    }

    private static string FirstDiffLine(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Min(la.Length, lb.Length); i++)
            if (la[i] != lb[i]) return $"{la[i].Trim()} => {lb[i].Trim()}";
        return "length";
    }

    private static List<string> Tokens(string xml)
    {
        var tokens = new List<string>();
        int pos = 0;
        while ((pos = xml.IndexOf("<![CDATA[", pos, StringComparison.Ordinal)) >= 0)
        {
            int start = pos + 9;
            int end = xml.IndexOf("]]>", start, StringComparison.Ordinal);
            if (end < 0) break;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(xml.Substring(start, end - start), @"[A-Za-z0-9_]+"))
                tokens.Add(m.Value);
            pos = end + 3;
        }
        return tokens;
    }
}

