using System;
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
                if (string.IsNullOrEmpty(formatted) || formatted == stCode)
                    formatted = _engine.Format(stCode) ?? stCode;
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

    public static bool LooksLikeDeclaration(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        string upper = text!.ToUpperInvariant();
        bool hasVar = upper.Contains("VAR") && upper.Contains("END_VAR");
        bool hasProgram = upper.Contains("PROGRAM") || upper.Contains("FUNCTION_BLOCK") || upper.Contains("FUNCTION");

        string stripped = StripPragmas(text);
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
}