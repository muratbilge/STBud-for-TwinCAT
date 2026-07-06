using System;
using System.Collections.Generic;
using System.Text;

namespace STBud.Git.Diff;

/// <summary>
/// One aligned row of a line diff. Consumed by the <c>stgit</c> CLI and the tray
/// DiffViewer (which renders it). Both share this single implementation.
/// </summary>
public enum DiffOp { Equal, Insert, Delete, Changed, Snip }

/// <summary>One aligned row of a line diff.</summary>
public sealed class DiffRow
{
    public DiffOp Op { get; set; }

    /// <summary>Left (old) line text; null for inserts/snips.</summary>
    public string? Left { get; set; }

    /// <summary>Right (new) line text; null for deletes/snips.</summary>
    public string? Right { get; set; }

    /// <summary>1-based line number on the left, or 0 when not present.</summary>
    public int LeftLine { get; set; }

    /// <summary>1-based line number on the right, or 0 when not present.</summary>
    public int RightLine { get; set; }

    /// <summary>
    /// Optional section tag ("decl"/"impl") used by section-aware diff viewers so
    /// restore-to-editor can target the right editor tab. Null for non-section diffs.
    /// </summary>
    public string? SectionTag { get; set; }

    /// <summary>
    /// UI-only flag: set after the row's committed text has been restored into the
    /// working file, so the viewer can mark it (a green ✓ + stripe). Not used by the CLI.
    /// </summary>
    public bool Restored { get; set; }
}

/// <summary>
/// UI-agnostic line diff built on Longest Common Subsequence. Lives in STBud.Git so
/// the <c>stgit</c> CLI and the tray DiffViewer share one implementation.
///
/// <see cref="Compute"/> returns raw Equal/Insert/Delete rows. Call
/// <see cref="PairChangeRuns"/> to collapse adjacent Delete+Insert runs into
/// <see cref="DiffOp.Changed"/> rows, and <see cref="FilterToChanges"/> to trim to
/// changes with surrounding context (inserting <see cref="DiffOp.Snip"/> markers).
///
/// The O(n*m) full-matrix LCS is the single engine. The custom-drawn canvas renders
/// only visible rows so the UI stays responsive regardless of diff size; for typical
/// TwinCAT POUs (well under 4000 lines) the matrix is cheap. A custom Myers O((n+m)d)
/// path was attempted but reverted — the backtrace is fiddly and the matrix is good
/// enough for the real input sizes here.
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// Compute the raw line diff (Equal/Insert/Delete). Adjacent delete+insert runs
    /// are NOT paired here — call <see cref="PairChangeRuns"/> to produce Changed rows.
    /// </summary>
    public static List<DiffRow> Compute(string? leftText, string? rightText)
    {
        var left = SplitLines(leftText);
        var right = SplitLines(rightText);
        int[,] lcs = ComputeLcs(left, right);

        var rows = new List<DiffRow>();
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j])
            {
                rows.Add(new DiffRow { Op = DiffOp.Equal, Left = left[i], Right = right[j], LeftLine = i + 1, RightLine = j + 1 });
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                rows.Add(new DiffRow { Op = DiffOp.Delete, Left = left[i], LeftLine = i + 1 });
                i++;
            }
            else
            {
                rows.Add(new DiffRow { Op = DiffOp.Insert, Right = right[j], RightLine = j + 1 });
                j++;
            }
        }
        while (i < left.Length)
        {
            rows.Add(new DiffRow { Op = DiffOp.Delete, Left = left[i], LeftLine = i + 1 });
            i++;
        }
        while (j < right.Length)
        {
            rows.Add(new DiffRow { Op = DiffOp.Insert, Right = right[j], RightLine = j + 1 });
            j++;
        }
        return rows;
    }

    /// <summary>
    /// Collapse adjacent Delete runs immediately followed by Insert runs into
    /// <see cref="DiffOp.Changed"/> rows, pairing them positionally (min of the two
    /// run lengths). Leftover deletes/inserts stay as their original op. Returns a
    /// new list; the input is not mutated.
    /// </summary>
    public static List<DiffRow> PairChangeRuns(List<DiffRow> rows)
    {
        var result = new List<DiffRow>(rows.Count);
        int i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Op != DiffOp.Delete) { result.Add(rows[i]); i++; continue; }

            int delStart = i;
            int delEnd = i;
            while (delEnd + 1 < rows.Count && rows[delEnd + 1].Op == DiffOp.Delete)
                delEnd++;

            int insStart = delEnd + 1;
            int insEnd = insStart;
            if (insStart < rows.Count && rows[insStart].Op == DiffOp.Insert)
            {
                while (insEnd + 1 < rows.Count && rows[insEnd + 1].Op == DiffOp.Insert)
                    insEnd++;

                int delCount = delEnd - delStart + 1;
                int insCount = insEnd - insStart + 1;
                int pairCount = Math.Min(delCount, insCount);

                for (int p = 0; p < pairCount; p++)
                    result.Add(new DiffRow
                    {
                        Op = DiffOp.Changed,
                        Left = rows[delStart + p].Left,
                        LeftLine = rows[delStart + p].LeftLine,
                        Right = rows[insStart + p].Right,
                        RightLine = rows[insStart + p].RightLine,
                        SectionTag = rows[delStart + p].SectionTag ?? rows[insStart + p].SectionTag,
                    });

                for (int p = pairCount; p < delCount; p++)
                    result.Add(rows[delStart + p]);
                for (int p = pairCount; p < insCount; p++)
                    result.Add(rows[insStart + p]);

                i = insEnd + 1;
            }
            else
            {
                for (int k = delStart; k <= delEnd; k++) result.Add(rows[k]);
                i = delEnd + 1;
            }
        }
        return result;
    }

    /// <summary>
    /// Trim the diff to change blocks plus <paramref name="contextLines"/> of
    /// surrounding context on each side. Removed regions are replaced by a single
    /// <see cref="DiffOp.Snip"/> marker row. Returns a new list.
    /// </summary>
    public static List<DiffRow> FilterToChanges(List<DiffRow> rows, int contextLines)
    {
        var changeIndices = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Op != DiffOp.Equal) changeIndices.Add(i);

        var include = new HashSet<int>();
        foreach (var idx in changeIndices)
            for (int c = -contextLines; c <= contextLines; c++)
            {
                int t = idx + c;
                if (t >= 0 && t < rows.Count) include.Add(t);
            }

        var result = new List<DiffRow>();
        bool prevIncluded = false;
        for (int i = 0; i < rows.Count; i++)
        {
            if (include.Contains(i))
            {
                if (!prevIncluded && result.Count > 0)
                    result.Add(new DiffRow { Op = DiffOp.Snip });
                result.Add(rows[i]);
                prevIncluded = true;
            }
            else prevIncluded = false;
        }
        return result;
    }

    /// <summary>Count of inserted and deleted lines (Changed counts as neither here).</summary>
    public static (int added, int removed) Stats(IEnumerable<DiffRow> rows)
    {
        int added = 0, removed = 0;
        foreach (var r in rows)
        {
            if (r.Op == DiffOp.Insert) added++;
            else if (r.Op == DiffOp.Delete) removed++;
        }
        return (added, removed);
    }

    /// <summary>Full breakdown: added, removed, changed, unchanged.</summary>
    public static (int added, int removed, int changed, int unchanged) DetailedStats(IEnumerable<DiffRow> rows)
    {
        int added = 0, removed = 0, changed = 0, unchanged = 0;
        foreach (var r in rows)
        {
            switch (r.Op)
            {
                case DiffOp.Insert: added++; break;
                case DiffOp.Delete: removed++; break;
                case DiffOp.Changed: changed++; break;
                case DiffOp.Equal: unchanged++; break;
            }
        }
        return (added, removed, changed, unchanged);
    }

    /// <summary>True when the two texts are line-for-line identical.</summary>
    public static bool AreEqual(string? leftText, string? rightText)
    {
        var (added, removed) = Stats(Compute(leftText, rightText));
        return added == 0 && removed == 0;
    }

    public static bool IsChange(DiffOp op) =>
        op == DiffOp.Insert || op == DiffOp.Delete || op == DiffOp.Changed;

    /// <summary>
    /// The change-block range (consecutive Insert/Delete/Changed rows) containing
    /// <paramref name="row"/>, or (-1,-1) when the row isn't part of a change. Drives the
    /// compare viewer's "accept this change" (gutter arrow / context menu).
    /// </summary>
    public static (int start, int end) BlockRangeAt(IReadOnlyList<DiffRow> rows, int row)
    {
        if (rows == null || row < 0 || row >= rows.Count || !IsChange(rows[row].Op)) return (-1, -1);
        int s = row; while (s - 1 >= 0 && IsChange(rows[s - 1].Op)) s--;
        int e = row; while (e + 1 < rows.Count && IsChange(rows[e + 1].Op)) e++;
        return (s, e);
    }

    /// <summary>Result of <see cref="ExtractAcceptBlock"/>.</summary>
    public readonly struct AcceptBlock
    {
        public AcceptBlock(string committed, string working, bool hasInsert, bool hasDelOrChg, string? section)
        { Committed = committed; Working = working; HasInsert = hasInsert; HasDelOrChg = hasDelOrChg; Section = section; }
        public string Committed { get; }   // HEAD/left text to write
        public string Working { get; }     // current/right text to locate in the editor
        public bool HasInsert { get; }
        public bool HasDelOrChg { get; }
        public string? Section { get; }    // majority "decl"/"impl" tag, or null
    }

    /// <summary>
    /// Build the committed (HEAD) and working (current) text for a row range, plus flags and
    /// the majority section tag. "Accept from HEAD" then asks the Host to locate
    /// <see cref="AcceptBlock.Working"/> in the editor and replace it with
    /// <see cref="AcceptBlock.Committed"/>. Snip rows are skipped; committed excludes Insert
    /// rows (no HEAD line), working excludes Delete rows (no current line).
    /// </summary>
    public static AcceptBlock ExtractAcceptBlock(IReadOnlyList<DiffRow> rows, int start, int end)
    {
        var committed = new StringBuilder();
        var working = new StringBuilder();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool hasInsert = false, hasDelOrChg = false;

        for (int r = Math.Max(0, start); r <= end && r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Op == DiffOp.Snip) continue;

            if (row.Op != DiffOp.Insert && row.Left != null)
            {
                if (committed.Length > 0) committed.Append("\r\n");
                committed.Append(row.Left);
            }
            if (row.Op != DiffOp.Delete && row.Right != null)
            {
                if (working.Length > 0) working.Append("\r\n");
                working.Append(row.Right);
            }
            if (row.Op == DiffOp.Insert) hasInsert = true;
            if (row.Op == DiffOp.Delete || row.Op == DiffOp.Changed) hasDelOrChg = true;

            if (!string.IsNullOrEmpty(row.SectionTag))
            {
                counts.TryGetValue(row.SectionTag!, out var n);
                counts[row.SectionTag!] = n + 1;
            }
        }

        string? section = null;
        int best = 0;
        foreach (var kv in counts)
            if (kv.Value > best) { best = kv.Value; section = kv.Key; }

        return new AcceptBlock(committed.ToString(), working.ToString(), hasInsert, hasDelOrChg, section);
    }

    /// <summary>
    /// Render a git-style prefixed diff. Equal → "  ", Delete → "- ", Insert → "+ ",
    /// Changed → "~ " on two lines (left then right), Snip → "...".
    /// </summary>
    public static string ToText(IEnumerable<DiffRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            switch (r.Op)
            {
                case DiffOp.Equal: sb.Append("  ").AppendLine(r.Left); break;
                case DiffOp.Delete: sb.Append("- ").AppendLine(r.Left); break;
                case DiffOp.Insert: sb.Append("+ ").AppendLine(r.Right); break;
                case DiffOp.Changed:
                    sb.Append("~ ").AppendLine(r.Left);
                    sb.Append("~ ").AppendLine(r.Right);
                    break;
                case DiffOp.Snip: sb.AppendLine("  ...  ...  ..."); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Render a unified-diff-style text (alternating - / + for changed pairs). Useful
    /// for clipboard export and CI tooling that expects the conventional form.
    /// </summary>
    public static string ToUnifiedText(IEnumerable<DiffRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            switch (r.Op)
            {
                case DiffOp.Equal: sb.Append("  ").AppendLine(r.Left); break;
                case DiffOp.Delete: sb.Append("- ").AppendLine(r.Left); break;
                case DiffOp.Insert: sb.Append("+ ").AppendLine(r.Right); break;
                case DiffOp.Changed:
                    sb.Append("- ").AppendLine(r.Left);
                    sb.Append("+ ").AppendLine(r.Right);
                    break;
                case DiffOp.Snip: sb.AppendLine("  ...  ...  ..."); break;
            }
        }
        return sb.ToString();
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static int[,] ComputeLcs(string[] left, string[] right)
    {
        int m = left.Length, n = right.Length;
        var dp = new int[m + 1, n + 1];
        for (int a = m - 1; a >= 0; a--)
        {
            for (int b = n - 1; b >= 0; b--)
            {
                dp[a, b] = left[a] == right[b]
                    ? dp[a + 1, b + 1] + 1
                    : Math.Max(dp[a + 1, b], dp[a, b + 1]);
            }
        }
        return dp;
    }
}