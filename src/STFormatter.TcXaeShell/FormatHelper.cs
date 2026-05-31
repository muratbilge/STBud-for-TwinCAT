using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using _3S.CoDeSys.IECTextEditor;
using _3S.CoDeSys.TextDocument;
using STFormatter.Core.Formatting;

namespace STFormatter.TcXaeShell;

internal static class FormatHelper
{
    private static Package package = null!;
    private static FormattingConfiguration config = null!;
    private static EnvDTE._DTE dte = null!;

    public static void Initialize(Package pkg, FormattingConfiguration cfg, EnvDTE._DTE dteInstance)
    {
        package = pkg;
        config = cfg;
        dte = dteInstance;
    }

    public static void FormatDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var engine = new FormattingEngine(config);
            var filePath = dte?.ActiveDocument?.FullName;
            Log($"FormatDocument: FilePath={(filePath ?? "(null)")}");

            // Try automation API first (live editor update)
            if (TryFormatViaAutomation(engine, filePath))
                return;

            var iecEditor = TryGetIecEditorFromRdt(filePath);
            Log($"FormatDocument: iecEditor={(iecEditor != null ? "found" : "null")}");

            if (iecEditor != null)
            {
                FormatDocumentViaIecEditor(engine, iecEditor, filePath);
                return;
            }

            var textDoc = TryGetTextDocumentFromRdt(filePath);
            Log($"FormatDocument: textDoc={(textDoc != null ? "found" : "null")}");

            if (textDoc != null)
            {
                FormatDocumentViaTextDocument(engine, textDoc, filePath);
                return;
            }

            Log("FormatDocument: No CODESYS editor interface available, falling back to file-only.");
            FormatDocumentFileOnly(engine);
        }
        catch (Exception ex)
        {
            Log("FormatDocument FAILED: " + ex.GetType().Name + " - " + ex.Message + Environment.NewLine + ex.StackTrace);
            ShowStatus("ST Formatter: Error - " + ex.Message);
        }
    }

    public static void FormatSelection()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var engine = new FormattingEngine(config);
            var filePath = dte?.ActiveDocument?.FullName;
            Log($"FormatSelection: FilePath={(filePath ?? "(null)")}");

            // Try automation API first (live editor update)
            if (TryFormatViaAutomation(engine, filePath))
                return;

            var iecEditor = TryGetIecEditorFromRdt(filePath);
            Log($"FormatSelection: iecEditor={(iecEditor != null ? "found" : "null")}");

            if (iecEditor != null)
            {
                FormatSelectionViaIecEditor(engine, iecEditor, filePath);
                return;
            }

            var textDoc = TryGetTextDocumentFromRdt(filePath);
            Log($"FormatSelection: textDoc={(textDoc != null ? "found" : "null")}");

            if (textDoc != null)
            {
                FormatDocumentViaTextDocument(engine, textDoc, filePath);
                return;
            }

            Log("FormatSelection: No CODESYS editor interface available, falling back to file-only.");
            FormatDocumentFileOnly(engine);
        }
        catch (Exception ex)
        {
            Log("FormatSelection FAILED: " + ex.GetType().Name + " - " + ex.Message + Environment.NewLine + ex.StackTrace);
            ShowStatus("ST Formatter: Error - " + ex.Message);
        }
    }

    public static void FormatSelectedCode()
    {
        FormatDocument();
    }

    private static IIECTextEditor? TryGetIecEditorFromRdt(string? filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrEmpty(filePath)) return null;

        try
        {
            var docDataObj = GetDocDataFromRdt(filePath);
            if (docDataObj == null) return null;

            var iecEditor = docDataObj as IIECTextEditor;
            if (iecEditor != null)
            {
                Log("TryGetIecEditorFromRdt: Found IIECTextEditor on DocData.");
                return iecEditor;
            }

            var singleLine = docDataObj as ISingleLineIECTextEditor;
            if (singleLine != null)
            {
                Log("TryGetIecEditorFromRdt: Found ISingleLineIECTextEditor on DocData.");
                return singleLine as IIECTextEditor;
            }

            Log($"TryGetIecEditorFromRdt: DocData type={docDataObj.GetType().FullName}, no IIECTextEditor.");

            // Try to find IGetManagedObject interface via reflection
            try
            {
                foreach (var iface in docDataObj.GetType().GetInterfaces())
                {
                    if (iface.Name == "IGetManagedObject")
                    {
                        var getManagedMethod = iface.GetMethod("GetManagedObject");
                        if (getManagedMethod != null)
                        {
                            var managedObj = getManagedMethod.Invoke(docDataObj, null);
                            Log($"TryGetIecEditorFromRdt: GetManagedObject returned type={managedObj?.GetType().FullName ?? "null"}");
                            if (managedObj != null)
                            {
                                var foundIec = managedObj as IIECTextEditor;
                                if (foundIec != null)
                                {
                                    Log("TryGetIecEditorFromRdt: Found IIECTextEditor via GetManagedObject!");
                                    return foundIec;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"TryGetIecEditorFromRdt: GetManagedObject inspection failed: {ex.GetType().Name} - {ex.Message}");
            }

            return null;
        }
        catch (Exception ex)
        {
            Log("TryGetIecEditorFromRdt FAILED: " + ex.GetType().Name + " - " + ex.Message);
            return null;
        }
    }

    private static ITextDocument? TryGetTextDocumentFromRdt(string? filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrEmpty(filePath)) return null;

        try
        {
            var docDataObj = GetDocDataFromRdt(filePath);
            if (docDataObj != null)
            {
                var textDoc = docDataObj as ITextDocument;
                if (textDoc != null)
                {
                    Log("TryGetTextDocumentFromRdt: Found ITextDocument on DocData.");
                    return textDoc;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log("TryGetTextDocumentFromRdt FAILED: " + ex.GetType().Name + " - " + ex.Message);
            return null;
        }
    }

    private static object? GetDocDataFromRdt(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var rdt = Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
            {
                Log("GetDocDataFromRdt: No IVsRunningDocumentTable service.");
                return null;
            }

            uint cookie;
            IVsHierarchy hier;
            uint itemid;
            IntPtr docDataPtr = IntPtr.Zero;

            int hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, filePath, out hier, out itemid, out docDataPtr, out cookie);
            Log($"GetDocDataFromRdt: FindAndLockDocument hr=0x{hr:X8}, docDataPtr={(docDataPtr != IntPtr.Zero ? "non-zero" : "zero")}");

            if (hr != 0 || docDataPtr == IntPtr.Zero)
            {
                Log("GetDocDataFromRdt: Document not found in RDT.");
                return null;
            }

            try
            {
                var docDataObj = Marshal.GetObjectForIUnknown(docDataPtr);
                Log($"GetDocDataFromRdt: DocData type={docDataObj.GetType().FullName}");
                return docDataObj;
            }
            finally
            {
                Marshal.Release(docDataPtr);
            }
        }
        catch (Exception ex)
        {
            Log("GetDocDataFromRdt FAILED: " + ex.GetType().Name + " - " + ex.Message);
            return null;
        }
    }

    private static bool TryFormatViaAutomation(FormattingEngine engine, string? filePath) 
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrEmpty(filePath)) return false;

        try
        {
            var docDataObj = GetDocDataFromRdt(filePath);
            if (docDataObj == null) return false;

            bool hasPlcData = docDataObj.GetType().GetInterfaces().Any(i => i.Name == "IPLCData");
            if (!hasPlcData)
            {
                Log("TryFormatViaAutomation: No IPLCData interface on DocData.");
                return false;
            }

            Log("TryFormatViaAutomation: Found IPLCData, accessing PlcFileNode...");

            var nodeProp = docDataObj.GetType().GetProperty("Node");
            if (nodeProp == null)
            {
                Log("TryFormatViaAutomation: No Node property on DocData runtime type.");
                return false;
            }

            var node = nodeProp.GetValue(docDataObj);
            if (node == null)
            {
                Log("TryFormatViaAutomation: Node property is null.");
                return false;
            }

            Log($"TryFormatViaAutomation: Node type={node.GetType().FullName}");
            return TryFormatViaNode(node, engine, filePath);
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaAutomation FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private static bool TryFormatViaNode(object node, FormattingEngine engine, string filePath) 
    {
        var nodeType = node.GetType();

        // Find the SysManTreeItem property which gives us the TcPouItemAdapter
        // This adapter implements both ITcPlcDeclaration and ITcPlcImplementation
        var sysManProp = nodeType.GetProperty("SysManTreeItem");
        if (sysManProp == null)
        {
            Log("TryFormatViaNode: No SysManTreeItem property on PlcFileNode.");
            return false;
        }

        object adapter;
        try
        {
            adapter = sysManProp.GetValue(node);
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaNode: SysManTreeItem access failed: {ex.GetType().Name} - {ex.Message}");
            return false;
        }

        if (adapter == null)
        {
            Log("TryFormatViaNode: SysManTreeItem is null.");
            return false;
        }

        Log($"TryFormatViaNode: SysManTreeItem type={adapter.GetType().FullName}");
        var adapterType = adapter.GetType();

        // Find DeclarationText and ImplementationText properties via interfaces
        PropertyInfo? declTextProp = null;
        PropertyInfo? implTextProp = null;
        Type? declIface = null;
        Type? implIface = null;

        foreach (var iface in adapterType.GetInterfaces())
        {
            foreach (var prop in iface.GetProperties())
            {
                if (prop.Name == "DeclarationText" && prop.CanRead && prop.CanWrite)
                {
                    declTextProp = prop;
                    declIface = iface;
                }
                else if (prop.Name == "ImplementationText" && prop.CanRead && prop.CanWrite)
                {
                    implTextProp = prop;
                    implIface = iface;
                }
            }
        }

        // Also check direct properties
        if (declTextProp == null)
        {
            var p = adapterType.GetProperty("DeclarationText");
            if (p != null && p.CanRead && p.CanWrite) declTextProp = p;
        }
        if (implTextProp == null)
        {
            var p = adapterType.GetProperty("ImplementationText");
            if (p != null && p.CanRead && p.CanWrite) implTextProp = p;
        }

        Log($"TryFormatViaNode: DeclarationText via {declIface?.FullName ?? "null"}, ImplementationText via {implIface?.FullName ?? "null"}");

        bool anyFormatted = false;

        // Format declaration section using ITcPlcDeclaration.DeclarationText
        if (declTextProp != null)
        {
            try
            {
                var source = declTextProp.GetValue(adapter) as string;
                Log($"TryFormatViaNode: DeclarationText length={source?.Length ?? -1}");

                if (!string.IsNullOrWhiteSpace(source))
                {
                    var formatted = engine.Format(source);
                    if (!string.IsNullOrEmpty(formatted) && formatted != source)
                    {
                        if (source.Contains("\r\n") && !formatted.Contains("\r\n"))
                            formatted = formatted.Replace("\n", "\r\n");

                        declTextProp.SetValue(adapter, formatted);
                        Log($"TryFormatViaNode: DeclarationText formatted ({source.Length} -> {formatted.Length}).");
                        anyFormatted = true;
                    }
                    else if (formatted == source)
                    {
                        Log("TryFormatViaNode: DeclarationText already formatted.");
                    }
                }
                else
                {
                    Log("TryFormatViaNode: DeclarationText is empty or null, skipping.");
                }
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaNode: DeclarationText formatting failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        // Format implementation body using ITcPlcImplementation.ImplementationText
        if (implTextProp != null)
        {
            try
            {
                var source = implTextProp.GetValue(adapter) as string;
                Log($"TryFormatViaNode: ImplementationText length={source?.Length ?? -1}");

                if (!string.IsNullOrWhiteSpace(source))
                {
                    var formatted = engine.FormatBody(source);
                    if (!string.IsNullOrEmpty(formatted) && formatted != source)
                    {
                        if (source.Contains("\r\n") && !formatted.Contains("\r\n"))
                            formatted = formatted.Replace("\n", "\r\n");

                        implTextProp.SetValue(adapter, formatted);
                        Log($"TryFormatViaNode: ImplementationText formatted ({source.Length} -> {formatted.Length}).");
                        anyFormatted = true;
                    }
                    else if (formatted == source)
                    {
                        Log("TryFormatViaNode: ImplementationText already formatted.");
                    }
                }
                else
                {
                    Log("TryFormatViaNode: ImplementationText is empty or null, skipping.");
                }
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaNode: ImplementationText formatting failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        if (!anyFormatted)
        {
            Log("TryFormatViaNode: No changes made via automation API.");
            ShowStatus("ST Formatter: Document is already formatted.");
            return false;
        }

        // Also update the .TcPOU file on disk
        if (IsTwinCatXmlFile(filePath))
            WriteFormattedToXmlFile(filePath, engine);

        ShowStatus("ST Formatter: Document formatted successfully.");
        return true;
    }

    private static void FormatDocumentViaIecEditor(FormattingEngine engine, IIECTextEditor iecEditor, string? filePath) 
    {
        var singleLine = iecEditor as ISingleLineIECTextEditor;
        if (singleLine == null)
        {
            Log("FormatDocumentViaIecEditor: IIECTextEditor does not implement ISingleLineIECTextEditor.");
            FormatDocumentFileOnly(engine);
            return;
        }

        var source = singleLine.Text;
        Log($"FormatDocumentViaIecEditor: Text length={source.Length}");

        if (string.IsNullOrWhiteSpace(source))
        {
            Log("FormatDocumentViaIecEditor: Empty text, skipping.");
            return;
        }

        var formatted = TwinCatXmlFormatter.LooksLikeDeclaration(source) ? engine.Format(source) : engine.FormatBody(source);

        if (string.IsNullOrEmpty(formatted))
        {
            Log("FormatDocumentViaIecEditor: Formatter returned empty, skipping.");
            return;
        }

        if (formatted == source)
        {
            Log("FormatDocumentViaIecEditor: No changes needed.");
            ShowStatus("ST Formatter: Document is already formatted.");
            return;
        }

        Log($"FormatDocumentViaIecEditor: Applying formatted text (length={formatted.Length}).");
        singleLine.Text = formatted;

        if (!string.IsNullOrEmpty(filePath) && IsTwinCatXmlFile(filePath))
        {
            WriteFormattedToXmlFile(filePath, engine);
        }

        ShowStatus("ST Formatter: Document formatted successfully.");
    }

    private static void FormatSelectionViaIecEditor(FormattingEngine engine, IIECTextEditor iecEditor, string? filePath) 
    {
        var singleLine = iecEditor as ISingleLineIECTextEditor;
        if (singleLine == null)
        {
            Log("FormatSelectionViaIecEditor: No ISingleLineIECTextEditor, falling back to FormatDocument.");
            FormatDocumentViaIecEditor(engine, iecEditor, filePath);
            return;
        }

        int selStart = singleLine.SelectionStart;
        int selLength = singleLine.SelectionLength;
        Log($"FormatSelectionViaIecEditor: SelectionStart={selStart}, SelectionLength={selLength}");

        if (selLength <= 0)
        {
            Log("FormatSelectionViaIecEditor: No selection, falling back to FormatDocument.");
            FormatDocumentViaIecEditor(engine, iecEditor, filePath);
            return;
        }

        var fullText = singleLine.Text;
        if (selStart < 0 || selStart + selLength > fullText.Length)
        {
            Log($"FormatSelectionViaIecEditor: Selection out of range (start={selStart}, length={selLength}, textLen={fullText.Length}).");
            FormatDocumentViaIecEditor(engine, iecEditor, filePath);
            return;
        }

        var selectedText = fullText.Substring(selStart, selLength);
        Log($"FormatSelectionViaIecEditor: Selected text length={selectedText.Length}");

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            Log("FormatSelectionViaIecEditor: Whitespace-only selection, skipping.");
            return;
        }

        string formatted;
        if (TwinCatXmlFormatter.LooksLikeDeclaration(selectedText))
        {
            Log("FormatSelectionViaIecEditor: Formatting as declaration.");
            formatted = engine.Format(selectedText);
        }
        else
        {
            Log("FormatSelectionViaIecEditor: Formatting as body.");
            formatted = engine.FormatBody(selectedText);
        }

        if (string.IsNullOrEmpty(formatted))
        {
            Log("FormatSelectionViaIecEditor: Formatter returned empty, skipping.");
            return;
        }

        if (formatted == selectedText)
        {
            Log("FormatSelectionViaIecEditor: No changes needed.");
            ShowStatus("ST Formatter: Selection is already formatted.");
            return;
        }

        Log($"FormatSelectionViaIecEditor: Replacing selection (orig={selectedText.Length}, fmt={formatted.Length}).");
        var newText = fullText.Substring(0, selStart) + formatted + fullText.Substring(selStart + selLength);
        singleLine.Text = newText;

        if (!string.IsNullOrEmpty(filePath) && IsTwinCatXmlFile(filePath))
        {
            WriteFormattedToXmlFile(filePath, engine);
        }

        ShowStatus("ST Formatter: Selection formatted successfully.");
    }

    private static void FormatDocumentViaTextDocument(FormattingEngine engine, ITextDocument textDoc, string? filePath) 
    {
        var source = textDoc.Text;
        Log($"FormatDocumentViaTextDocument: Text length={source.Length}");

        if (string.IsNullOrWhiteSpace(source))
        {
            Log("FormatDocumentViaTextDocument: Empty text, skipping.");
            return;
        }

        var formatted = TwinCatXmlFormatter.LooksLikeDeclaration(source) ? engine.Format(source) : engine.FormatBody(source);

        if (string.IsNullOrEmpty(formatted))
        {
            Log("FormatDocumentViaTextDocument: Formatter returned empty, skipping.");
            return;
        }

        if (formatted == source)
        {
            Log("FormatDocumentViaTextDocument: No changes needed.");
            ShowStatus("ST Formatter: Document is already formatted.");
            return;
        }

        Log($"FormatDocumentViaTextDocument: Applying formatted text (length={formatted.Length}).");
        textDoc.Text = formatted;

        if (!string.IsNullOrEmpty(filePath) && IsTwinCatXmlFile(filePath))
        {
            WriteFormattedToXmlFile(filePath, engine);
        }

        ShowStatus("ST Formatter: Document formatted successfully.");
    }

    private static void FormatDocumentFileOnly(FormattingEngine engine) 
    {
        var filePath = dte?.ActiveDocument?.FullName;
        Log($"FormatDocumentFileOnly: FilePath={(filePath ?? "(null)")}");

        if (string.IsNullOrEmpty(filePath) || !IsTwinCatXmlFile(filePath))
        {
            Log("FormatDocumentFileOnly: Not a TwinCAT XML file.");
            ShowStatus("ST Formatter: Not a TwinCAT project file (.TcPOU/.TcDUT/.TcGVL).");
            return;
        }

        WriteFormattedToXmlFile(filePath, engine);
    }

    private static void WriteFormattedToXmlFile(string filePath, FormattingEngine engine) 
    {
        Log($"WriteFormattedToXmlFile: {filePath}");

        var xmlContent = File.ReadAllText(filePath, Encoding.UTF8);
        Log($"File content length={xmlContent.Length}");

        if (!FormatStSectionsInXml(xmlContent, engine, out var newXmlContent))
        {
            Log("No changes made to file.");
            return;
        }

        var fileChangeEx = Package.GetGlobalService(typeof(SVsFileChangeEx)) as IVsFileChangeEx;
        try
        {
            if (fileChangeEx != null)
            {
                int hr = fileChangeEx.IgnoreFile(0, filePath, 1);
                Log($"IgnoreFile(1) hr=0x{hr:X8}");
            }

            var backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, true);
            Log($"Backup created: {backupPath}");

            File.WriteAllText(filePath, newXmlContent, new UTF8Encoding(false));
            Log("File saved successfully.");

            if (fileChangeEx != null)
            {
                int hr = fileChangeEx.SyncFile(filePath);
                Log($"SyncFile hr=0x{hr:X8}");
            }

            if (fileChangeEx != null)
            {
                int hr = fileChangeEx.IgnoreFile(0, filePath, 0);
                Log($"IgnoreFile(0) hr=0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            if (fileChangeEx != null) { try { fileChangeEx.IgnoreFile(0, filePath, 0); } catch { } }
            Log("WriteFormattedToXmlFile write FAILED: " + ex.GetType().Name + " - " + ex.Message);
        }
    }

    private static bool FormatStSectionsInXml(string xmlContent, FormattingEngine engine, out string newContent)
    {
        var formatter = new TwinCatXmlFormatter(engine);
        return formatter.FormatXmlContent(xmlContent, out newContent, out _, out _);
    }

    private static void ShowStatus(string message) 
    {
        try
        {
            var statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            if (statusBar != null)
            {
                statusBar.SetText(message);
            }
        }
        catch { }
    }

    private static bool IsTwinCatXmlFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".tcpou" or ".tcdut" or ".tcgvl" or ".tcio" or ".tcto";
    }

    private static FormattingConfiguration GetConfiguration()
    {
        return config ?? FormattingConfiguration.Default;
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "STFormatter_TcXaeShell.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}