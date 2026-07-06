using System;
using System.Collections.Generic;
using System.Text;

namespace STBud.Git.Diff;

/// <summary>
/// Word-level intra-line diff for the diff viewer's intra-line highlight. Tokenizes
/// a single ST line on natural boundaries (identifiers, numbers, whitespace, strings,
/// operator/punctuation runs) and runs an LCS over the token streams to produce
/// per-token changed spans. This is the granularity the audit flagged as missing
/// (the old <c>CommonPrefix</c>/<c>CommonSuffix</c> approach highlighted one big
/// middle block; this identifies that <c>a</c> and <c>b</c> swapped in
/// <c>IF a AND b THEN</c> vs <c>IF b AND a THEN</c>).
///
/// Pure and framework-free so it is unit-testable without WinForms.
/// </summary>
public static class IntraLineHighlight
{
    /// <summary>Cap on tokens per line to bound worst case on pathological input.</summary>
    private const int MaxTokens = 400;

    public enum TokenKind { Whitespace, Identifier, Number, String, Other }

    public readonly struct Token
    {
        public readonly int Start;
        public readonly int Length;
        public readonly string Text;
        public readonly TokenKind Kind;

        public Token(int start, int length, string text, TokenKind kind)
        {
            Start = start; Length = length; Text = text; Kind = kind;
        }

        public int End => Start + Length;
    }

    /// <summary>
    /// One segment of an aligned token diff. When <c>Equal</c>, both spans are present
    /// and the text matched. When not equal, exactly one of <c>Left</c>/<c>Right</c> is
    /// non-null (a deleted or inserted token).
    /// </summary>
    public readonly struct Segment
    {
        public readonly Token? Left;
        public readonly Token? Right;
        public readonly bool Equal;

        public Segment(Token? left, Token? right, bool equal)
        {
            Left = left; Right = right; Equal = equal;
        }
    }

    /// <summary>Tokenize a single line into ST-aware tokens.</summary>
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(text)) return tokens;

        int i = 0;
        int n = text.Length;
        while (i < n && tokens.Count < MaxTokens)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < n && char.IsWhiteSpace(text[i])) i++;
                tokens.Add(new Token(start, i - start, text.Substring(start, i - start), TokenKind.Whitespace));
                continue;
            }

            if (c == '\'' )
            {
                int start = i; i++; // skip opening quote
                while (i < n && text[i] != '\'') i++;
                if (i < n) i++; // skip closing quote
                tokens.Add(new Token(start, i - start, text.Substring(start, i - start), TokenKind.String));
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                int start = i;
                bool startsDigit = char.IsDigit(c);
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                // Distinguish numbers from identifiers: a token starting with a digit
                // is a number; one starting with a letter/underscore is an identifier.
                tokens.Add(new Token(start, i - start, text.Substring(start, i - start),
                    startsDigit ? TokenKind.Number : TokenKind.Identifier));
                continue;
            }

            // Operator / punctuation run: group consecutive non-word, non-space, non-quote
            // chars into one token so ":=", ":", ";", "+", "(", ")" each become one token.
            {
                int start = i;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '\''
                       && !char.IsLetterOrDigit(text[i]) && text[i] != '_')
                    i++;
                tokens.Add(new Token(start, i - start, text.Substring(start, i - start), TokenKind.Other));
            }
        }
        return tokens;
    }

    /// <summary>
    /// Compute the per-token aligned diff of two lines. Equal segments carry both
    /// tokens; non-equal segments carry the deleted (Left only) or inserted (Right only)
    /// token. Adjacent delete+insert runs are paired into the order they appear (no
    /// similarity matching) — good enough for the highlight; a char-level fallback
    /// inside a single changed token can be added later if needed.
    /// </summary>
    public static List<Segment> Compute(string? leftText, string? rightText)
    {
        var left = Tokenize(leftText ?? "");
        var right = Tokenize(rightText ?? "");
        int[,] lcs = ComputeTokenLcs(left, right);

        var segments = new List<Segment>();
        int i = 0, j = 0;
        while (i < left.Count && j < right.Count)
        {
            if (left[i].Text == right[j].Text)
            {
                segments.Add(new Segment(left[i], right[j], true));
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                segments.Add(new Segment(left[i], null, false));  // deleted
                i++;
            }
            else
            {
                segments.Add(new Segment(null, right[j], false));  // inserted
                j++;
            }
        }
        while (i < left.Count) { segments.Add(new Segment(left[i], null, false)); i++; }
        while (j < right.Count) { segments.Add(new Segment(null, right[j], false)); j++; }
        return segments;
    }

    private static int[,] ComputeTokenLcs(List<Token> left, List<Token> right)
    {
        int m = left.Count, n = right.Count;
        var dp = new int[m + 1, n + 1];
        for (int a = m - 1; a >= 0; a--)
            for (int b = n - 1; b >= 0; b--)
                dp[a, b] = left[a].Text == right[b].Text
                    ? dp[a + 1, b + 1] + 1
                    : Math.Max(dp[a + 1, b], dp[a, b + 1]);
        return dp;
    }
}