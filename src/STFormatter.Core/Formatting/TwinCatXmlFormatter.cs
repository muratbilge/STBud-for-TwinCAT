using System;
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
                    formatted = _engine.Format(cdata.Value);
                    if (string.IsNullOrEmpty(formatted))
                        formatted = null;
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

    public static bool LooksLikeDeclaration(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        string upper = text.ToUpperInvariant();
        bool hasVar = upper.Contains("VAR") && upper.Contains("END_VAR");
        bool hasProgram = upper.Contains("PROGRAM") || upper.Contains("FUNCTION_BLOCK") || upper.Contains("FUNCTION");
        bool hasBodyKeywords = upper.Contains("IF ") || upper.Contains("FOR ") || upper.Contains("WHILE ") ||
                                upper.Contains(":=") || upper.Contains("THEN");
        if (hasVar && !hasBodyKeywords) return true;
        if (hasBodyKeywords && !hasVar) return false;
        if (hasProgram) return true;
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