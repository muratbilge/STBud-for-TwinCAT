using STBud.Git.Diff;

namespace STBud.Git.Tests;

public class LineDiffTests
{
    [Fact]
    public void Compute_aligns_unchanged_and_marks_one_change()
    {
        var rows = LineDiff.Compute("a\nb\nc", "a\nx\nc");

        var (added, removed) = LineDiff.Stats(rows);
        Assert.Equal(1, added);
        Assert.Equal(1, removed);

        // first and last lines are equal
        Assert.Equal(DiffOp.Equal, rows[0].Op);
        Assert.Equal("a", rows[0].Left);
        Assert.Equal(DiffOp.Equal, rows[^1].Op);
        Assert.Equal("c", rows[^1].Right);
    }

    [Fact]
    public void AreEqual_is_true_for_identical_and_ignores_line_ending_style()
    {
        Assert.True(LineDiff.AreEqual("a\nb\nc", "a\r\nb\r\nc"));
        Assert.False(LineDiff.AreEqual("a\nb", "a\nB"));
    }

    [Fact]
    public void Compute_pure_insertions_and_deletions()
    {
        var insertOnly = LineDiff.Compute("a", "a\nb\nc");
        Assert.Equal((2, 0), LineDiff.Stats(insertOnly));

        var deleteOnly = LineDiff.Compute("a\nb\nc", "a");
        Assert.Equal((0, 2), LineDiff.Stats(deleteOnly));
    }

    [Fact]
    public void ToText_renders_prefixed_lines()
    {
        string text = LineDiff.ToText(LineDiff.Compute("a\nb", "a\nc"));
        Assert.Contains("  a", text);
        Assert.Contains("- b", text);
        Assert.Contains("+ c", text);
    }

    [Fact]
    public void Line_numbers_are_one_based_and_side_specific()
    {
        var rows = LineDiff.Compute("a\nb", "a\nb\nc");
        var inserted = rows.Single(r => r.Op == DiffOp.Insert);
        Assert.Equal("c", inserted.Right);
        Assert.Equal(3, inserted.RightLine);
        Assert.Equal(0, inserted.LeftLine);
    }

    [Fact]
    public void PairChangeRuns_collapses_adjacent_delete_insert_into_changed()
    {
        // one deleted line immediately followed by one inserted line → one Changed row
        var raw = LineDiff.Compute("a\nb\nc", "a\nB\nc");
        Assert.DoesNotContain(raw, r => r.Op == DiffOp.Changed); // raw has no Changed

        var paired = LineDiff.PairChangeRuns(raw);
        var changed = paired.Where(r => r.Op == DiffOp.Changed).ToList();
        Assert.Single(changed);
        Assert.Equal("b", changed[0].Left);
        Assert.Equal("B", changed[0].Right);
        Assert.Equal(2, changed[0].LeftLine);
        Assert.Equal(2, changed[0].RightLine);
    }

    [Fact]
    public void PairChangeRuns_balances_unequal_runs_and_keeps_leftovers()
    {
        // 2 deletes followed by 1 insert → 1 Changed + 1 leftover Delete
        var raw = LineDiff.Compute("a\nb\nc", "a\nB");
        var paired = LineDiff.PairChangeRuns(raw);

        Assert.Equal(DiffOp.Equal, paired[0].Op);          // a
        Assert.Equal(DiffOp.Changed, paired[1].Op);        // b ↔ B
        Assert.Equal(DiffOp.Delete, paired[2].Op);         // c (leftover)
        Assert.Equal(3, paired.Count);
    }

    [Fact]
    public void PairChangeRuns_preserves_separated_runs_when_unchanged_intervenes()
    {
        // Two separate change regions (delete a at start, insert e at end) separated
        // by unchanged b,c — not adjacent, so no pairing.
        var raw = LineDiff.Compute("a\nb\nc\nd", "b\nc\nd\ne");
        var paired = LineDiff.PairChangeRuns(raw);

        Assert.Contains(paired, r => r.Op == DiffOp.Delete);   // a (no insert follows it)
        Assert.Contains(paired, r => r.Op == DiffOp.Insert);   // e (no delete precedes it)
        Assert.DoesNotContain(paired, r => r.Op == DiffOp.Changed);
    }

    [Fact]
    public void FilterToChanges_keeps_context_and_inserts_snip_markers()
    {
        // Two change blocks (line 3 and line 7) separated by 3 unchanged lines;
        // with context=1 the middle unchanged line is excluded → a Snip appears.
        var rows = LineDiff.PairChangeRuns(LineDiff.Compute("a\nb\nX\nc\nd\ne\nY\nf", "a\nb\nx\nc\nd\ne\ny\nf"));
        var filtered = LineDiff.FilterToChanges(rows, contextLines: 1);

        Assert.Contains(filtered, r => r.Op == DiffOp.Snip);
        var changed = filtered.Where(r => r.Op == DiffOp.Changed).ToList();
        Assert.Equal(2, changed.Count);                  // X↔x and Y↔y
        Assert.Equal(DiffOp.Equal, filtered[0].Op);       // b context before first change
        Assert.Equal(DiffOp.Equal, filtered[^1].Op);      // f context after last change
    }

    [Fact]
    public void DetailedStats_counts_each_op_category()
    {
        var rows = LineDiff.PairChangeRuns(LineDiff.Compute("a\nb\nc\nd", "a\nB\nC\nd"));
        var (added, removed, changed, unchanged) = LineDiff.DetailedStats(rows);

        Assert.Equal(0, added);     // no pure inserts
        Assert.Equal(0, removed);  // no pure deletes
        Assert.Equal(2, changed);   // b↔B, c↔C
        Assert.Equal(2, unchanged); // a, d
    }

    [Fact]
    public void ToText_emits_tilde_for_changed_rows()
    {
        var rows = LineDiff.PairChangeRuns(LineDiff.Compute("a\nb", "a\nB"));
        string text = LineDiff.ToText(rows);

        Assert.Contains("  a", text);   // equal
        Assert.Contains("~ b", text);   // changed left
        Assert.Contains("~ B", text);   // changed right
    }

    [Fact]
    public void ToUnifiedText_emits_minus_then_plus_for_changed_rows()
    {
        var rows = LineDiff.PairChangeRuns(LineDiff.Compute("a\nb", "a\nB"));
        string text = LineDiff.ToUnifiedText(rows);

        Assert.Contains("  a", text);
        Assert.Contains("- b", text);
        Assert.Contains("+ B", text);
        Assert.DoesNotContain("~", text);
    }

    [Fact]
    public void SectionTag_survives_PairChangeRuns_from_delete_side()
    {
        var raw = LineDiff.Compute("a\nb", "a\nB");
        foreach (var r in raw) r.SectionTag = "decl";
        var paired = LineDiff.PairChangeRuns(raw);

        Assert.All(paired, r => Assert.Equal("decl", r.SectionTag));
    }

    [Fact]
    public void Compute_large_input_handles_change_in_the_middle()
    {
        // 600 unchanged lines + 1 changed line in the middle. The matrix LCS handles
        // this directly; the canvas renders only visible rows so UI stays responsive.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 600; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("line").Append(i);
        }
        string left = sb.ToString();
        string right = left.Replace("line300", "LINE300");

        var rows = LineDiff.PairChangeRuns(LineDiff.Compute(left, right));
        var (added, removed, changed, unchanged) = LineDiff.DetailedStats(rows);

        Assert.Equal(0, added);
        Assert.Equal(0, removed);
        Assert.Equal(1, changed);       // line300 ↔ LINE300
        Assert.Equal(599, unchanged);
    }

    [Fact]
    public void Compute_pure_insertion_on_large_input()
    {
        // 600 lines, all inserted (left empty, right 600 lines).
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 600; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("row").Append(i);
        }
        string right = sb.ToString();

        var rows = LineDiff.Compute("", right);
        var (added, removed) = LineDiff.Stats(rows);

        Assert.Equal(600, added);
        Assert.Equal(0, removed);
        Assert.All(rows, r => Assert.Equal(DiffOp.Insert, r.Op));
    }
}
