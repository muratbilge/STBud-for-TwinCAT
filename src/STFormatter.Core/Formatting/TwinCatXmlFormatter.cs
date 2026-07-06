using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace STFormatter.Core.Formatting;

public sealed class TwinCatXmlFormatter
{
    private readonly FormattingEngine _engine;

    public TwinCatXmlFormatter(FormattingEngine engine)
    {
        _engine = engine;
    }

    public TwinCatXmlFormatter(FormattingConfiguration? config = null)
    {
        _engine = new FormattingEngine(config);
    }

    public bool FormatXmlContent(string xml, out string formattedXml, out string? formattedDecl, out string? formattedImpl)
    {
        formattedDecl = null;
        formattedImpl = null;
        string result = xml;
        bool changed = false;

        int pos = 0;
        while ((pos = result.IndexOf("<![CDATA[", pos, StringComparison.Ordinal)) >= 0)
        {
            int cdataStart = pos + 9;
            int cdataEnd = result.IndexOf("]]>", cdataStart, StringComparison.Ordinal);
            if (cdataEnd < 0) break;

            string stCode = result.Substring(cdataStart, cdataEnd - cdataStart);

            if (string.IsNullOrWhiteSpace(stCode))
            {
                pos = cdataEnd + 3;
                continue;
            }

            string parentElement = FindParentElement(result, pos);
            bool isDeclaration = parentElement.IndexOf("Declaration", StringComparison.OrdinalIgnoreCase) >= 0;

            var inputUsesCrLf = stCode.Contains("\r\n");
            string formatted;
            if (isDeclaration)
            {
                formatted = _engine.FormatDeclaration(stCode);
                if (string.IsNullOrEmpty(formatted))
                    formatted = stCode;

                // FormatDeclaration returns header-only declarations (no VAR
                // section, no TYPE) unchanged; give those a full-format pass.
                // An unchanged result for content it does handle just means the
                // text is already formatted - never re-run Format() on it, and
                // never accept empty output (that would wipe the section).
                if (formatted == stCode &&
                    stCode.IndexOf("VAR", StringComparison.OrdinalIgnoreCase) < 0 &&
                    stCode.IndexOf("TYPE", StringComparison.OrdinalIgnoreCase) < 0 &&
                    ParsesWithoutErrors(stCode))
                {
                    var full = _engine.Format(stCode);
                    if (!string.IsNullOrWhiteSpace(full))
                        formatted = full;
                }
                formattedDecl = formatted;
            }
            else
            {
                formatted = _engine.FormatBody(stCode) ?? stCode;
                formattedImpl = formatted;
            }

            if (inputUsesCrLf && !formatted.Contains("\r\n"))
            {
                formatted = formatted.Replace("\n", "\r\n");
            }

            if (formatted != stCode)
            {
                result = result.Substring(0, cdataStart) + formatted + result.Substring(cdataEnd);
                pos = cdataStart + formatted.Length + 3;
                changed = true;
            }
            else
            {
                pos = cdataEnd + 3;
            }
        }

        formattedXml = result;
        return changed;
    }

    public bool FormatXDocument(XDocument doc)
    {
        bool modified = false;

        foreach (var element in doc.Descendants())
        {
            var cdataNodes = element.Nodes().OfType<XCData>();
            foreach (var cdata in cdataNodes.ToList())
            {
                if (string.IsNullOrWhiteSpace(cdata.Value))
                    continue;

                string parentName = element.Name.LocalName;
                bool isDeclaration = parentName.IndexOf("Declaration", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSt = string.Equals(parentName, "ST", StringComparison.OrdinalIgnoreCase);

                var inputUsesCrLf = cdata.Value.Contains("\r\n");
                string? formatted = null;

                if (isDeclaration)
                {
                    formatted = _engine.FormatDeclaration(cdata.Value);
                    if (string.IsNullOrEmpty(formatted) || formatted == cdata.Value)
                    {
                        formatted = _engine.Format(cdata.Value);
                        if (string.IsNullOrEmpty(formatted))
                            formatted = null;
                    }
                }

                if (formatted == null && (isSt || !isDeclaration))
                {
                    formatted = _engine.FormatBody(cdata.Value);
                    if (string.IsNullOrEmpty(formatted))
                        formatted = null;
                }

                if (formatted == null)
                    continue;

                if (inputUsesCrLf && !formatted.Contains("\r\n"))
                {
                    formatted = formatted.Replace("\n", "\r\n");
                }

                if (formatted != cdata.Value)
                {
                    cdata.Value = formatted;
                    modified = true;
                }
            }
        }

        return modified;
    }

    // Formatting a tree that has parse errors loses the content the parser
    // skipped during recovery - only full-format when the parse is clean.
    public static bool ParsesWithoutErrors(string source)
    {
        var tree = new Parsing.Parser(Text.SourceText.From(source)).Parse();
        return !tree.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error);
    }

    public static bool LooksLikeDeclaration(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        string upper = text!.ToUpperInvariant();
        bool hasVar = upper.Contains("VAR") && upper.Contains("END_VAR");
        bool hasProgram = upper.Contains("PROGRAM") || upper.Contains("FUNCTION_BLOCK") || upper.Contains("FUNCTION");

        string stripped = StripPragmas(text);

        // Highest-confidence signal: if the first meaningful token is a VAR-section or
        // POU/TYPE keyword, this is a declaration — even when it contains ':=' (which in a
        // declaration is an initializer, e.g. FB instance args, not a body assignment).
        // Implementation bodies never start with VAR/PROGRAM/FUNCTION_BLOCK/TYPE.
        if (StartsWithDeclarationKeyword(stripped))
            return true;

        string strippedUpper = stripped.ToUpperInvariant();
        bool hasBodyKeywords = strippedUpper.Contains("IF ") || strippedUpper.Contains("FOR ") || strippedUpper.Contains("WHILE ") ||
                                strippedUpper.Contains(":=") || strippedUpper.Contains("THEN");

        if (hasVar && !hasBodyKeywords) return true;
        if (hasBodyKeywords && !hasVar) return false;
        if (hasProgram) return true;

        if (!hasVar && !hasBodyKeywords)
        {
            return HasVariableDeclarationLines(text);
        }

        return false;
    }

    private static readonly string[] DeclarationStartKeywords =
    {
        "VAR", "VAR_GLOBAL", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP",
        "VAR_STAT", "VAR_STATIC", "VAR_CONSTANT", "VAR_PERSISTENT", "VAR_EXTERNAL",
        "PROGRAM", "FUNCTION_BLOCK", "FUNCTION", "INTERFACE", "TYPE", "STRUCT", "UNION",
    };

    // True when the first meaningful token (skipping leading whitespace, // line comments
    // and (* block comments *)) is a declaration keyword. '_' is part of the word so
    // VAR_GLOBAL reads as one token.
    private static bool StartsWithDeclarationKeyword(string text)
    {
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (c == '(' && i + 1 < n && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == ')')) i++;
                i += 2;
                continue;
            }
            break;
        }

        int start = i;
        while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
        if (i == start) return false;

        string word = text.Substring(start, i - start).ToUpperInvariant();
        return Array.IndexOf(DeclarationStartKeywords, word) >= 0;
    }

    private static string StripPragmas(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                int depth = 1;
                i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                    i++;
                }
                continue;
            }
            result.Append(text[i]);
            i++;
        }
        return result.ToString();
    }

    private static readonly string[] CommonTypes = new[]
    {
        "BOOL", "INT", "SINT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT",
        "REAL", "LREAL", "STRING", "WSTRING", "TIME", "LTIME", "DATE", "TOD", "DT",
        "BYTE", "WORD", "DWORD", "LWORD", "TON", "TOF", "CTU", "CTD", "CTUD",
        "R_TRIG", "F_TRIG", "TP"
    };

    private static bool HasVariableDeclarationLines(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return false;

        int declLines = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (IsVariableDeclarationLine(trimmed))
                declLines++;
        }

        int nonEmpty = lines.Count(l => !string.IsNullOrWhiteSpace(l));
        return nonEmpty > 0 && declLines >= nonEmpty * 0.5;
    }

    private static bool IsVariableDeclarationLine(string line)
    {
        var match = Regex.Match(line, @"^\w+\s*:\s*(\w+)");
        if (!match.Success) return false;

        string typePart = match.Groups[1].Value.ToUpperInvariant();
        foreach (var ct in CommonTypes)
        {
            if (typePart.StartsWith(ct))
                return true;
        }

        if (line.Contains(';'))
            return true;

        return false;
    }

    public static bool LooksLikeImplementation(string text)
    {
        return !LooksLikeDeclaration(text);
    }

    public static string FindParentElement(string xml, int cdataOffset)
    {
        int tagStart = xml.LastIndexOf('<', cdataOffset - 1);
        if (tagStart < 0) return "";
        int tagEnd = xml.IndexOf('>', tagStart);
        if (tagEnd < 0) return "";
        return xml.Substring(tagStart, tagEnd - tagStart + 1);
    }

    public enum StReplaceResult { Replaced, NotFound, Ambiguous }

    /// <summary>
    /// Replace the unique occurrence of the <paramref name="working"/> line block with
    /// <paramref name="committed"/> inside the CDATA of the requested section
    /// (Declaration when <paramref name="declaration"/> is true, otherwise Implementation).
    /// Line matching ignores trailing whitespace. Everything outside the matched block is
    /// preserved byte-for-byte. Returns <see cref="StReplaceResult.NotFound"/> when the block
    /// isn't present in that section, or <see cref="StReplaceResult.Ambiguous"/> when it
    /// occurs in more than one place (so we never guess). Used by the diff viewer's restore
    /// disk-write fallback (when the matching editor tab isn't active to live-edit).
    /// </summary>
    public static StReplaceResult ReplaceStBlockInSection(
        string xml, bool declaration, string working, string committed, out string newXml)
    {
        newXml = xml;
        if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(working))
            return StReplaceResult.NotFound;

        // First pass: count matches across the requested section's CDATA blocks.
        int total = 0;
        int pos = 0;
        while (pos < xml.Length)
        {
            int cdataPos = xml.IndexOf("<![CDATA[", pos, StringComparison.Ordinal);
            if (cdataPos < 0) break;
            int cdataStart = cdataPos + 9;
            int cdataEnd = xml.IndexOf("]]>", cdataStart, StringComparison.Ordinal);
            if (cdataEnd < 0) break;

            if (IsDeclarationCdata(xml, cdataPos) == declaration)
            {
                string body = xml.Substring(cdataStart, cdataEnd - cdataStart);
                total += CountLineBlockMatches(body, working);
                if (total > 1) return StReplaceResult.Ambiguous;
            }
            pos = cdataEnd + 3;
        }
        if (total == 0) return StReplaceResult.NotFound;
        if (total > 1) return StReplaceResult.Ambiguous;

        // Second pass: perform the single replacement.
        var result = new System.Text.StringBuilder(xml.Length + committed.Length);
        pos = 0;
        bool done = false;
        while (pos < xml.Length)
        {
            int cdataPos = xml.IndexOf("<![CDATA[", pos, StringComparison.Ordinal);
            if (cdataPos < 0) { result.Append(xml, pos, xml.Length - pos); break; }
            result.Append(xml, pos, cdataPos - pos);

            int cdataStart = cdataPos + 9;
            int cdataEnd = xml.IndexOf("]]>", cdataStart, StringComparison.Ordinal);
            if (cdataEnd < 0) { result.Append(xml, cdataPos, xml.Length - cdataPos); break; }

            string body = xml.Substring(cdataStart, cdataEnd - cdataStart);
            if (!done && IsDeclarationCdata(xml, cdataPos) == declaration &&
                TryReplaceLineBlock(body, working, committed, out string replacedBody))
            {
                result.Append("<![CDATA[").Append(replacedBody).Append("]]>");
                done = true;
            }
            else
            {
                result.Append(xml, cdataPos, cdataEnd + 3 - cdataPos);
            }
            pos = cdataEnd + 3;
        }

        if (!done) return StReplaceResult.NotFound;
        newXml = result.ToString();
        return StReplaceResult.Replaced;
    }

    private static bool IsDeclarationCdata(string xml, int cdataPos) =>
        FindParentElement(xml, cdataPos).IndexOf("Declaration", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string[] SplitLinesPreserve(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static int CountLineBlockMatches(string body, string working)
    {
        var ed = SplitLinesPreserve(body);
        var wk = SplitLinesPreserve(working);
        if (wk.Length == 0 || ed.Length < wk.Length) return 0;

        int count = 0;
        for (int i = 0; i + wk.Length <= ed.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < wk.Length; j++)
                if (!string.Equals(ed[i + j].TrimEnd(), wk[j].TrimEnd(), StringComparison.Ordinal)) { match = false; break; }
            if (match) count++;
        }
        return count;
    }

    private static bool TryReplaceLineBlock(string body, string working, string committed, out string replaced)
    {
        replaced = body;
        string nl = body.Contains("\r\n") ? "\r\n" : "\n";
        var ed = SplitLinesPreserve(body);
        var wk = SplitLinesPreserve(working);
        if (wk.Length == 0 || ed.Length < wk.Length) return false;

        int found = -1, count = 0;
        for (int i = 0; i + wk.Length <= ed.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < wk.Length; j++)
                if (!string.Equals(ed[i + j].TrimEnd(), wk[j].TrimEnd(), StringComparison.Ordinal)) { match = false; break; }
            if (match) { count++; if (found < 0) found = i; }
        }
        if (count != 1) return false;

        // Empty committed = delete the located block entirely (no blank-line residue).
        var committedLines = committed.Length == 0 ? System.Array.Empty<string>() : SplitLinesPreserve(committed);
        var outLines = new System.Collections.Generic.List<string>(ed.Length - wk.Length + committedLines.Length);
        for (int i = 0; i < found; i++) outLines.Add(ed[i]);
        outLines.AddRange(committedLines);
        for (int i = found + wk.Length; i < ed.Length; i++) outLines.Add(ed[i]);
        replaced = string.Join(nl, outLines);
        return true;
    }
}