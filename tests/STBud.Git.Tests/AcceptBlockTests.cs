using System.Collections.Generic;
using STBud.Git.Diff;

namespace STBud.Git.Tests;

// Pure logic behind the compare viewer's "accept from HEAD" context menu / gutter arrows.
public class AcceptBlockTests
{
    private static List<DiffRow> Rows() => LineDiff.PairChangeRuns(
        LineDiff.Compute("a\nb\nc\nd\ne", "a\nB\nc\nX\nd\ne"));
    // Compute("a b c d e", "a B c X d e"):
    //   a(Eq) b→B(Changed) c(Eq) +X(Insert) d(Eq) e(Eq)

    [Fact]
    public void BlockRangeAt_finds_single_changed_block()
    {
        var rows = Rows();
        // Row 1 is the Changed (b↔B).
        var (s, e) = LineDiff.BlockRangeAt(rows, 1);
        Assert.Equal(1, s);
        Assert.Equal(1, e);
    }

    [Fact]
    public void BlockRangeAt_on_equal_row_is_empty()
    {
        var rows = Rows();
        Assert.Equal((-1, -1), LineDiff.BlockRangeAt(rows, 0)); // 'a' equal
    }

    [Fact]
    public void BlockRangeAt_out_of_range_is_safe()
    {
        var rows = Rows();
        Assert.Equal((-1, -1), LineDiff.BlockRangeAt(rows, -5));
        Assert.Equal((-1, -1), LineDiff.BlockRangeAt(rows, 9999));
    }

    [Fact]
    public void BlockRangeAt_spans_adjacent_changes()
    {
        // delete+insert adjacent → one block of two rows after PairChangeRuns may collapse;
        // use a raw delete-then-insert that stays two rows.
        var rows = LineDiff.Compute("a\nx\ny\nb", "a\nb"); // a(Eq) x(Del) y(Del) b(Eq)
        var (s, e) = LineDiff.BlockRangeAt(rows, 1);
        Assert.Equal(1, s);
        Assert.Equal(2, e); // both deletes are one block
    }

    [Fact]
    public void ExtractAcceptBlock_changed_gives_left_as_committed_right_as_working()
    {
        var rows = Rows();
        var blk = LineDiff.ExtractAcceptBlock(rows, 1, 1); // b↔B
        Assert.Equal("b", blk.Committed);   // HEAD line to write
        Assert.Equal("B", blk.Working);     // current line to locate
        Assert.True(blk.HasDelOrChg);
        Assert.False(blk.HasInsert);
    }

    [Fact]
    public void ExtractAcceptBlock_insert_has_no_committed_text()
    {
        var rows = Rows();
        // Find the Insert row (X).
        int xi = rows.FindIndex(r => r.Op == DiffOp.Insert);
        Assert.True(xi >= 0);
        var blk = LineDiff.ExtractAcceptBlock(rows, xi, xi);
        Assert.Equal("", blk.Committed);    // nothing in HEAD → accept would delete it
        Assert.Equal("X", blk.Working);
        Assert.True(blk.HasInsert);
        Assert.False(blk.HasDelOrChg);
    }

    [Fact]
    public void ExtractAcceptBlock_carries_majority_section_tag()
    {
        var rows = new List<DiffRow>
        {
            new DiffRow { Op = DiffOp.Changed, Left = "a", Right = "A", SectionTag = "impl" },
            new DiffRow { Op = DiffOp.Changed, Left = "b", Right = "B", SectionTag = "impl" },
            new DiffRow { Op = DiffOp.Changed, Left = "c", Right = "C", SectionTag = "decl" },
        };
        var blk = LineDiff.ExtractAcceptBlock(rows, 0, 2);
        Assert.Equal("impl", blk.Section);
        Assert.Equal("a\r\nb\r\nc", blk.Committed);
        Assert.Equal("A\r\nB\r\nC", blk.Working);
    }

    [Fact]
    public void ExtractAcceptBlock_skips_snip_rows()
    {
        var rows = new List<DiffRow>
        {
            new DiffRow { Op = DiffOp.Changed, Left = "a", Right = "A" },
            new DiffRow { Op = DiffOp.Snip },
            new DiffRow { Op = DiffOp.Changed, Left = "b", Right = "B" },
        };
        var blk = LineDiff.ExtractAcceptBlock(rows, 0, 2);
        Assert.Equal("a\r\nb", blk.Committed);
        Assert.Equal("A\r\nB", blk.Working);
    }
}
