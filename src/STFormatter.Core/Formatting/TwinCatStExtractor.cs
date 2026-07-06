using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace STFormatter.Core.Formatting;

/// <summary>
/// Read-only extraction of Structured Text out of TwinCAT XML files
/// (.TcPOU/.TcDUT/.TcGVL). The ST lives in CDATA sections; this pulls it out so
/// callers (e.g. the STBud.Git diff) can compare ST-to-ST instead of the noisy
/// XML/CDATA wrapper. It never formats or mutates anything.
///
/// A POU may carry several CDATA blocks (the main Declaration/Implementation plus
/// methods, properties and actions). All declaration-side blocks are concatenated
/// in document order, and all implementation-side blocks likewise, so the same
/// input always yields the same ST stream — which is what makes version diffs line
/// up regardless of XML attribute churn (GUIDs, line endings).
/// </summary>
public static class TwinCatStExtractor
{
    public sealed class StSections
    {
        public string? Declaration { get; set; }
        public string? Implementation { get; set; }

        /// <summary>True when no CDATA/ST was found (input was not TwinCAT XML).</summary>
        public bool IsEmpty =>
            string.IsNullOrEmpty(Declaration) && string.IsNullOrEmpty(Implementation);

        /// <summary>Declaration followed by implementation, for a single combined diff view.</summary>
        public string Combined()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(Declaration))
                sb.Append(Declaration!.TrimEnd('\r', '\n'));
            if (!string.IsNullOrEmpty(Implementation))
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(Implementation!.TrimEnd('\r', '\n'));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Pull the Declaration and Implementation ST from TwinCAT XML. Returns empty
    /// sections when the input has no CDATA (e.g. a plain .st file or arbitrary text).
    /// </summary>
    public static StSections Extract(string? xml)
    {
        var result = new StSections();
        if (string.IsNullOrWhiteSpace(xml))
            return result;

        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var decl = new StringBuilder();
            var impl = new StringBuilder();

            foreach (var element in doc.Descendants())
            {
                foreach (var cdata in element.Nodes().OfType<XCData>())
                {
                    if (string.IsNullOrEmpty(cdata.Value))
                        continue;

                    // Exact element-name match — a substring match misclassifies any
                    // future <MethodDeclaration>/<FieldDeclaration>/... as declaration.
                    bool isDecl = string.Equals(element.Name.LocalName, "Declaration", StringComparison.OrdinalIgnoreCase);
                    var target = isDecl ? decl : impl;
                    if (target.Length > 0) target.Append('\n');
                    target.Append(cdata.Value);
                }
            }

            result.Declaration = decl.Length > 0 ? decl.ToString() : null;
            result.Implementation = impl.Length > 0 ? impl.ToString() : null;
        }
        catch
        {
            // Malformed or non-XML input: fall back to a raw CDATA scan so a
            // partially-broken file still yields its ST rather than nothing.
            ScanCData(xml!, result);
        }

        return result;
    }

    /// <summary>Convenience: the combined ST, or the raw text when there is no CDATA.</summary>
    public static string ExtractCombinedOrRaw(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var sections = Extract(text);
        return sections.IsEmpty ? text! : sections.Combined();
    }

    // Raw CDATA scan for malformed XML. Classifies each CDATA block by the nearest
    // enclosing element's open tag, found by a real backward scan that tracks element
    // depth and skips the <![CDATA[ ... ]]> region itself. The naive LastIndexOf('<')
    // approach finds the '<' of the CDATA opener, which classifies every block as
    // Implementation — see ScanCData bug in the audit. This is the fix.
    private static void ScanCData(string xml, StSections result)
    {
        var decl = new StringBuilder();
        var impl = new StringBuilder();

        int pos = 0;
        while ((pos = xml.IndexOf("<![CDATA[", pos, StringComparison.Ordinal)) >= 0)
        {
            int start = pos + 9;
            int end = xml.IndexOf("]]>", start, StringComparison.Ordinal);
            if (end < 0) break;

            string body = xml.Substring(start, end - start);

            // Find the enclosing element's open tag by scanning backwards from the
            // CDATA opener. Skip over the '<![CDATA[' opener and any text content;
            // the first real '<' that opens an element (not '</' close or '<!' meta)
            // is the parent we want.
            string parent = FindEnclosingElementName(xml, pos);
            bool isDecl = string.Equals(parent, "Declaration", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(body))
            {
                var target = isDecl ? decl : impl;
                if (target.Length > 0) target.Append('\n');
                target.Append(body);
            }
            pos = end + 3;
        }

        if (decl.Length > 0) result.Declaration = decl.ToString();
        if (impl.Length > 0) result.Implementation = impl.ToString();
    }

    // Backward scan from a CDATA opener position to find the name of the enclosing
    // element. Returns "" when none can be determined. Handles nested elements by
    // tracking depth: each '<name' (not '</', '<!', '<?', '<%') increases, each '</name>'
    // decreases; the enclosing element is the one at depth 0 when we reach the CDATA.
    private static string FindEnclosingElementName(string xml, int cdataPos)
    {
        int depth = 0;
        int i = cdataPos - 1;
        while (i >= 0)
        {
            if (xml[i] != '<') { i--; continue; }

            // What kind of tag is this '<'?
            if (i + 1 < xml.Length && xml[i + 1] == '/')
            {
                // '</name>' close tag — increases depth (we're going backwards).
                depth++;
                i--;
                continue;
            }
            if (i + 1 < xml.Length && (xml[i + 1] == '!' || xml[i + 1] == '?' || xml[i + 1] == '%'))
            {
                // '<![CDATA[', '<?xml ...?>', '<% ... %>' — meta, not an element; skip.
                i--;
                continue;
            }

            // Open tag '<name ...>'. If depth == 0, this is the enclosing element.
            if (depth == 0)
            {
                return ReadElementName(xml, i + 1);
            }
            depth--;
            i--;
        }
        return "";
    }

    // Read the element name after '<' (skip whitespace, read until whitespace, '>', '/').
    private static string ReadElementName(string xml, int afterLt)
    {
        int j = afterLt;
        while (j < xml.Length && char.IsWhiteSpace(xml[j])) j++;
        int nameStart = j;
        while (j < xml.Length && !char.IsWhiteSpace(xml[j]) && xml[j] != '>' && xml[j] != '/' && xml[j] != '<')
            j++;
        return xml.Substring(nameStart, j - nameStart);
    }
}
