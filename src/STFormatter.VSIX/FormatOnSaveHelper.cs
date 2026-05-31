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
        var formatter = new TwinCatXmlFormatter(config);

        bool modified = formatter.FormatXDocument(doc);

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
}
