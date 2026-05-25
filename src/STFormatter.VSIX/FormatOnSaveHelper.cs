using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using STFormatter.Core.Formatting;

namespace STFormatter.VSIX;

internal static class FormatOnSaveHelper
{
    public static void Attach(ITextView textView, ITextDocumentFactoryService textDocumentFactoryService)
    {
        if (!textDocumentFactoryService.TryGetTextDocument(textView.TextBuffer, out var textDocument))
            return;

        textDocument.FileActionOccurred += (sender, args) =>
        {
            if (args.FileActionType != FileActionTypes.ContentSavedToDisk)
                return;

            var filePath = args.FilePath;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (!IsSupportedFile(extension))
                return;

            var package = STFormatterPackage.Instance;
            if (package == null) return;

            var optionsPage = package.GetDialogPage(typeof(Options.STFormatterOptionPage)) as Options.STFormatterOptionPage;
            if (optionsPage?.FormatOnSave != true)
                return;

            var config = optionsPage.ToConfiguration();

            if (IsTwinCatXmlFile(extension))
            {
                FormatTwinCatXmlFile(filePath, config);
            }
            else
            {
                var source = textView.TextBuffer.CurrentSnapshot.GetText();
                var engine = new FormattingEngine(config);
                var formatted = engine.Format(source);

                if (formatted != source)
                {
                    var edit = textView.TextBuffer.CreateEdit();
                    edit.Replace(new Span(0, textView.TextBuffer.CurrentSnapshot.Length), formatted);
                    edit.Apply();
                }
            }
        };
    }

    private static bool IsSupportedFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".st" or ".txt" or ".iecst" or ".tcpou" or ".tcdut" or ".tcgvl" or ".tcio";
    }

    private static bool IsTwinCatXmlFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".tcpou" or ".tcdut" or ".tcgvl" or ".tcio";
    }

    private static void FormatTwinCatXmlFile(string filePath, FormattingConfiguration config)
    {
        var xmlContent = File.ReadAllText(filePath);
        var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
        var engine = new FormattingEngine(config);

        bool modified = false;

        if (filePath.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase))
        {
            modified |= FormatTcPou(doc, engine);
        }
        else if (filePath.EndsWith(".TcDUT", StringComparison.OrdinalIgnoreCase))
        {
            modified |= FormatTcDut(doc, engine);
        }
        else if (filePath.EndsWith(".TcGVL", StringComparison.OrdinalIgnoreCase))
        {
            modified |= FormatTcGvl(doc, engine);
        }

        if (modified)
        {
            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                OmitXmlDeclaration = false,
                Encoding = new System.Text.UTF8Encoding(true)
            };

            using var writer = System.Xml.XmlWriter.Create(filePath, settings);
            doc.Save(writer);
        }
    }

    private static bool FormatTcPou(System.Xml.Linq.XDocument doc, FormattingEngine engine)
    {
        bool modified = false;

        var implementationSt = doc.Descendants()
            .Where(e => e.Name == "Implementation" || e.Name.LocalName == "Implementation")
            .SelectMany(e => e.Descendants())
            .FirstOrDefault(e => e.Name == "ST" || e.Name.LocalName == "ST");

        if (implementationSt != null)
        {
            var cdata = implementationSt.Nodes().OfType<System.Xml.Linq.XCData>().FirstOrDefault();
            if (cdata != null)
            {
                var formatted = engine.Format(cdata.Value);
                if (formatted != cdata.Value)
                {
                    cdata.Value = formatted;
                    modified = true;
                }
            }
        }

        var declaration = doc.Descendants()
            .Where(e => e.Name == "Declaration" || e.Name.LocalName == "Declaration")
            .FirstOrDefault();

        if (declaration != null)
        {
            var cdata = declaration.Nodes().OfType<System.Xml.Linq.XCData>().FirstOrDefault();
            if (cdata != null)
            {
                var formatted = engine.Format(cdata.Value);
                if (formatted != cdata.Value)
                {
                    cdata.Value = formatted;
                    modified = true;
                }
            }
        }

        return modified;
    }

    private static bool FormatTcDut(System.Xml.Linq.XDocument doc, FormattingEngine engine)
    {
        bool modified = false;

        var declaration = doc.Descendants()
            .Where(e => e.Name == "Declaration" || e.Name.LocalName == "Declaration")
            .FirstOrDefault();

        if (declaration != null)
        {
            var cdata = declaration.Nodes().OfType<System.Xml.Linq.XCData>().FirstOrDefault();
            if (cdata != null)
            {
                var formatted = engine.Format(cdata.Value);
                if (formatted != cdata.Value)
                {
                    cdata.Value = formatted;
                    modified = true;
                }
            }
        }

        return modified;
    }

    private static bool FormatTcGvl(System.Xml.Linq.XDocument doc, FormattingEngine engine)
    {
        bool modified = false;

        var declaration = doc.Descendants()
            .Where(e => e.Name == "Declaration" || e.Name.LocalName == "Declaration")
            .FirstOrDefault();

        if (declaration != null)
        {
            var cdata = declaration.Nodes().OfType<System.Xml.Linq.XCData>().FirstOrDefault();
            if (cdata != null)
            {
                var formatted = engine.Format(cdata.Value);
                if (formatted != cdata.Value)
                {
                    cdata.Value = formatted;
                    modified = true;
                }
            }
        }

        return modified;
    }
}
