using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using STFormatter.Core.Formatting;
using STFormatter.Core.Text;

namespace STFormatter.VSIX;

internal static class FormatHelper
{
    public static void FormatDocument(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var doc = GetActiveDocument(package);
        if (doc == null)
        {
            // Try to format the active text buffer directly
            FormatActiveTextBuffer(package, GetConfiguration(package), formatSelection: false);
            return;
        }

        var filePath = doc.FullName;
        if (string.IsNullOrEmpty(filePath))
        {
            FormatActiveTextBuffer(package, GetConfiguration(package), formatSelection: false);
            return;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var config = GetConfiguration(package);

        if (IsTwinCatXmlFile(extension))
        {
            FormatTwinCatXmlFile(filePath, config);
        }
        else if (IsStFile(extension))
        {
            FormatTextFile(filePath, config);
        }
        else
        {
            // Try to format the active text buffer directly
            FormatActiveTextBuffer(package, config, formatSelection: false);
        }
    }

    public static void FormatSelection(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        FormatActiveTextBuffer(package, GetConfiguration(package), formatSelection: true);
    }

    private static void FormatActiveTextBuffer(AsyncPackage package, FormattingConfiguration config, bool formatSelection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
        if (textManager == null) return;

        textManager.GetActiveView(1, null, out var textView);
        if (textView == null) return;

        textView.GetBuffer(out var textLines);
        if (textLines == null) return;

        var adapterService = Package.GetGlobalService(typeof(Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService))
            as Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService;
        if (adapterService == null) return;

        var wpfTextView = adapterService.GetWpfTextView(textView);
        if (wpfTextView == null) return;

        var textBuffer = wpfTextView.TextBuffer;
        var snapshot = textBuffer.CurrentSnapshot;

        string source;
        int startPosition = 0;
        int endPosition = snapshot.Length;

        if (formatSelection && wpfTextView.Selection.StreamSelectionSpan.Length > 0)
        {
            var selectionSpan = wpfTextView.Selection.StreamSelectionSpan;
            startPosition = selectionSpan.Start.Position;
            endPosition = selectionSpan.End.Position;
            source = snapshot.GetText(startPosition, endPosition - startPosition);
        }
        else
        {
            source = snapshot.GetText();
        }

        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        if (formatted != source)
        {
            var edit = textBuffer.CreateEdit();
            if (formatSelection)
            {
                edit.Replace(new Span(startPosition, endPosition - startPosition), formatted);
            }
            else
            {
                edit.Replace(new Span(0, snapshot.Length), formatted);
            }
            edit.Apply();
        }
    }

    private static void FormatTextFile(string filePath, FormattingConfiguration config)
    {
        var source = File.ReadAllText(filePath);
        var engine = new FormattingEngine(config);
        var formatted = engine.Format(source);

        if (formatted != source)
        {
            File.WriteAllText(filePath, formatted);
        }
    }

    private static void FormatTwinCatXmlFile(string filePath, FormattingConfiguration config)
    {
        var xmlContent = File.ReadAllText(filePath);
        var doc = XDocument.Parse(xmlContent);
        var formatter = new TwinCatXmlFormatter(config);

        bool modified = formatter.FormatXDocument(doc);

        if (modified)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                OmitXmlDeclaration = false,
                Encoding = new System.Text.UTF8Encoding(true)
            };

            using var writer = XmlWriter.Create(filePath, settings);
            doc.Save(writer);
        }
    }

    private static FormattingConfiguration GetConfiguration(AsyncPackage package)
    {
        var optionsPage = package.GetDialogPage(typeof(Options.STFormatterOptionPage)) as Options.STFormatterOptionPage;
        return optionsPage?.ToConfiguration() ?? FormattingConfiguration.Default;
    }

    private static EnvDTE.Document? GetActiveDocument(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        return dte?.ActiveDocument;
    }

    private static bool IsTwinCatXmlFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".tcpou" or ".tcdut" or ".tcgvl" or ".tcio" or ".tcto";
    }

    private static bool IsStFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".st" or ".txt" or ".iecst";
    }
}
