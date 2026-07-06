using STBud.Git.Diff;

namespace STBud.Git.Tests;

public class IntraLineHighlightTests
{
    [Fact]
    public void Tokenize_splits_identifiers_numbers_operators_and_whitespace()
    {
        var tokens = IntraLineHighlight.Tokenize("a := 12 + 'x';");

        // Tokens: a | sp | := | sp | 12 | sp | + | sp | 'x' | ;
        Assert.Equal(10, tokens.Count);
        Assert.Equal("a", tokens[0].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.Identifier, tokens[0].Kind);

        Assert.Equal(" ", tokens[1].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.Whitespace, tokens[1].Kind);

        Assert.Equal(":=", tokens[2].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.Other, tokens[2].Kind);

        Assert.Equal("12", tokens[4].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.Number, tokens[4].Kind);

        Assert.Equal("'x'", tokens[8].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.String, tokens[8].Kind);

        Assert.Equal(";", tokens[9].Text);
        Assert.Equal(IntraLineHighlight.TokenKind.Other, tokens[9].Kind);
    }

    [Fact]
    public void Compute_marks_swapped_tokens_as_changed_not_one_big_block()
    {
        // The old prefix/suffix highlight lit up the whole middle; the token-level
        // diff identifies that a and b swapped (each becomes a changed segment).
        var segments = IntraLineHighlight.Compute("IF a AND b THEN", "IF b AND a THEN");

        // The LCS picks an alignment where both a and b are treated as changed:
        // a (deleted) + b (inserted) + b (deleted) + a (inserted) — 4 changed segments.
        // The point of this test vs the old prefix/suffix highlight is that the change
        // is broken into token-sized segments, not one big middle block.
        int equal = 0, changed = 0;
        foreach (var s in segments)
            if (s.Equal) equal++; else changed++;

        Assert.Equal(4, changed);     // a→b and b→a, each as a delete+insert pair
        Assert.True(equal >= 5);      // IF, spaces, AND, THEN
    }

    [Theory]
    [InlineData("bInit : BOOL := False;", "bSimulationTriggerOLD : BOOL;")]
    [InlineData("IF a AND b THEN", "IF b AND a THEN")]
    [InlineData("    nSubCount : UDINT;", "nSubCount:UDINT;")]
    [InlineData("a := 1;", "")]
    [InlineData("", "x := 2;")]
    public void Compute_each_side_tokens_reconstruct_that_line_verbatim(string left, string right)
    {
        // The diff renderer draws ONLY one side's tokens per pane, in order. For that to show
        // the line correctly (spaces intact, no tokens bleeding in from the other side), the
        // concatenation of each side's non-null tokens must equal that side's original text.
        var segments = IntraLineHighlight.Compute(left, right);

        var leftSb = new System.Text.StringBuilder();
        var rightSb = new System.Text.StringBuilder();
        foreach (var s in segments)
        {
            if (s.Left.HasValue) leftSb.Append(s.Left.Value.Text);
            if (s.Right.HasValue) rightSb.Append(s.Right.Value.Text);
        }

        Assert.Equal(left, leftSb.ToString());
        Assert.Equal(right, rightSb.ToString());
    }

    [Fact]
    public void Compute_returns_all_equal_for_identical_lines()
    {
        var segments = IntraLineHighlight.Compute("a := 1;", "a := 1;");

        Assert.All(segments, s => Assert.True(s.Equal));
        // Tokens: a | sp | := | sp | 1 | ;  = 6
        Assert.Equal(6, segments.Count);
    }

    [Fact]
    public void Compute_handles_pure_insertion()
    {
        var segments = IntraLineHighlight.Compute("", "a := 1;");

        Assert.All(segments, s => Assert.False(s.Equal));
        Assert.Equal(6, segments.Count);
    }

    [Fact]
    public void Compute_handles_pure_deletion()
    {
        var segments = IntraLineHighlight.Compute("a := 1;", "");

        Assert.All(segments, s => Assert.False(s.Equal));
        Assert.Equal(6, segments.Count);
    }

    [Fact]
    public void Compute_distinguishes_number_from_identifier()
    {
        var tokens = IntraLineHighlight.Tokenize("fb123 456name");

        Assert.Equal(IntraLineHighlight.TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("fb123", tokens[0].Text);

        Assert.Equal(IntraLineHighlight.TokenKind.Whitespace, tokens[1].Kind);

        // Tokenizer reads the longest word run; "456name" starts with a digit so it's
        // classified as a Number (it's not valid ST, but the classifier is simple).
        Assert.Equal(IntraLineHighlight.TokenKind.Number, tokens[2].Kind);
        Assert.Equal("456name", tokens[2].Text);
    }

    [Fact]
    public void Compute_caps_token_count_to_bound_worst_case()
    {
        // 500 tokens of distinct identifiers — tokenizer caps at MaxTokens (400).
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 500; i++) { if (i > 0) sb.Append(' '); sb.Append("id").Append(i); }
        var tokens = IntraLineHighlight.Tokenize(sb.ToString());

        Assert.True(tokens.Count <= 400);
    }
}