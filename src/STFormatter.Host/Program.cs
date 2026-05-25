using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using STFormatter.Core.Formatting;

namespace STFormatter.Host;

internal class Program
{
    private static HostManager? _hostManager;
    private static volatile bool _running = true;

    [STAThread]
    static void Main(string[] args)
    {
        FreeConsole();

        LogInit();
        Log("=== STFormatter.Host started ===");

        _hostManager = new HostManager();

        // Main loop: discover, connect, inject, maintain
        while (_running)
        {
            System.Windows.Forms.Application.DoEvents();

            try
            {
                Maintain();
            }
            catch (Exception ex)
            {
                Log($"Maintain error: {ex.Message}");
            }

            System.Threading.Thread.Sleep(500);
        }

        Log("Host shutting down");
        Shutdown();
    }

    // --- Maintenance loop ---

    private static void Maintain()
    {
        if (_hostManager == null) return;

        // Discover new TcXaeShell instances (or re-register after document reload)
        var newInstance = _hostManager.FindNewTcXaeShell();
        if (newInstance != null)
        {
            var (pid, dte) = newInstance.Value;
            var instance = _hostManager.Register(pid, dte);

            dte.Events.DTEEvents.OnBeginShutdown += () =>
            {
                Log($"DTE Shutdown: PID {pid}");
                _hostManager.CleanupInstance(pid);
            };

            _hostManager.InjectButtons(instance);
        }

        // Re-inject for instances that lost their buttons (after reconnect/restart)
        var snapshot = _hostManager.GetAllInstances();
        foreach (var kvp in snapshot)
        {
            if (kvp.Value.InjectedMenus.Count == 0)
            {
                _hostManager.InjectButtons(kvp.Value);
            }
        }
    }

    private static void Shutdown()
    {
        if (_hostManager == null) return;

        foreach (var kvp in _hostManager.GetAllInstances())
        {
            _hostManager.CleanupInstance(kvp.Key);
        }
    }

    // --- Format handlers (called from HostManager button click events) ---

    public static void HandleFormatDocument(int pid)
    {
        Log($"HandleFormatDocument: PID {pid}");
        try
        {
            HandleFormatDocumentCore(pid);
        }
        catch (Exception ex)
        {
            Log($"HandleFormatDocument: PID {pid} FAILED: {ex.Message}");
        }
    }

    private static void HandleFormatDocumentCore(int pid)
    {
        Log($"HandleFormatDocument: PID {pid}");
        try
        {
            var instance = _hostManager?.GetInstance(pid);
            if (instance == null)
            {
                Log($"HandleFormatDocument: PID {pid} instance not found");
                return;
            }

            if (!_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleFormatDocument: PID {pid} instance not alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleFormatDocument: PID {pid} No active document");
                return;
            }

            var dte = instance.Dte;
            string filePath = dte.ActiveDocument.FullName;
            Log($"HandleFormatDocument: PID {pid} Formatting {filePath}");

            if (!File.Exists(filePath))
            {
                Log($"HandleFormatDocument: File not found: {filePath}");
                return;
            }

            string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
            if (ext != ".tcpou" && ext != ".tcdut" && ext != ".tcgvl" &&
                ext != ".tcio" && ext != ".tcto")
            {
                Log($"HandleFormatDocument: Not a TwinCAT XML file ({ext})");
                return;
            }

            string xmlContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var engine = FormattingEngineFactory.Create();

            if (!FormatTwinCatXml(xmlContent, engine, out string formattedXml, out string? formattedDecl, out string? formattedImpl))
            {
                Log("HandleFormatDocument: No changes needed");
                return;
            }

            // Create backup before modifying
            string backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, true);

            // Tier 1: Automation API via COM InvokeMember (live update, preserves undo)
            if (TryFormatViaAutomation(dte, filePath, formattedDecl, formattedImpl))
            {
                File.WriteAllText(filePath, formattedXml, System.Text.Encoding.UTF8);
                Log("HandleFormatDocument: Tier 1 (Automation API) succeeded - live update");
                return;
            }

            // Tier 2: DTE.ExecuteCommand + Clipboard (SelectAll → Delete → Paste)
            // Tries to replace editor content in-place, avoiding file reload prompt
            if (LiveEditor.TryFormatViaExecuteCommand(dte, filePath, formattedDecl, formattedImpl))
            {
                Log("HandleFormatDocument: Tier 2 (ExecuteCommand + Clipboard) succeeded");
                return;
            }

            // Tier 3: SendKeys fallback (Ctrl+A → Delete → Ctrl+V)
            if (LiveEditor.TryFormatViaSendKeys(dte, filePath, formattedDecl, formattedImpl))
            {
                Log("HandleFormatDocument: Tier 3 (SendKeys) succeeded");
                return;
            }

            // Tier 4: File write with IVsFileChangeEx suppression (may still show reload dialog)
            if (LiveEditor.TryFormatViaRdtFileWrite(dte, filePath, formattedXml, engine))
            {
                Log("HandleFormatDocument: Tier 4 (RDT File Write) - file written, editor may need reload");
                return;
            }

            // Tier 5: Plain file write (user must reload manually)
            Log("HandleFormatDocument: All live-edit tiers failed, writing formatted file to disk");

            File.WriteAllText(filePath, formattedXml, System.Text.Encoding.UTF8);
            Log("HandleFormatDocument: Formatted XML written to disk — user must reload the file");
        }
        catch (Exception ex)
        {
            Log($"HandleFormatDocument: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleFormatSelection(int pid)
    {
        Log($"HandleFormatSelection: PID {pid}");
        try
        {
            HandleFormatSelectionCore(pid);
        }
        catch (Exception ex)
        {
            Log($"HandleFormatSelection: PID {pid} FAILED: {ex.Message}");
        }
    }

    private static void HandleFormatSelectionCore(int pid)
    {
        Log($"HandleFormatSelection: PID {pid}");
        try
        {
            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleFormatSelection: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleFormatSelection: PID {pid} No active document");
                return;
            }

            var selection = instance.Dte.ActiveDocument.Selection as EnvDTE.TextSelection;
            if (selection == null || string.IsNullOrEmpty(selection.Text))
            {
                HandleFormatDocument(pid);
                return;
            }

            string selectedText = selection.Text;
            Log($"HandleFormatSelection: PID {pid} Formatting selection ({selectedText.Length} chars)");
            var engine = FormattingEngineFactory.Create();

            string? formatted = engine.FormatBody(selectedText);
            if (formatted == selectedText)
                formatted = engine.Format(selectedText);

            if (formatted != null && formatted != selectedText)
            {
                selection.Delete();
                selection.Insert(formatted);
                Log($"HandleFormatSelection: PID {pid} Selection formatted successfully");
            }
            else
            {
                Log($"HandleFormatSelection: PID {pid} No changes needed");
            }
        }
        catch (Exception ex)
        {
            Log($"HandleFormatSelection: PID {pid} FAILED: {ex.Message}");
        }
    }

    // --- Three-tier live formatting ---

    private static bool FormatTwinCatXml(string xml, FormattingEngine engine,
        out string formattedXml, out string? formattedDecl, out string? formattedImpl)
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

            string parentElement = FindParentElement(result, pos);
            bool isDeclaration = parentElement.IndexOf("Declaration", StringComparison.OrdinalIgnoreCase) >= 0;

            string formatted;
            if (isDeclaration)
            {
                formatted = engine.Format(stCode) ?? stCode;
                formattedDecl = formatted;
            }
            else
            {
                formatted = engine.FormatBody(stCode) ?? stCode;
                formattedImpl = formatted;
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

    // Tier 1: Automation API via COM InvokeMember (live update, preserves undo)
    private static bool TryFormatViaAutomation(EnvDTE.DTE dte, string filePath,
        string? formattedDecl, string? formattedImpl)
    {
        Log("TryFormatViaAutomation: Attempting COM InvokeMember...");
        try
        {
            var projItem = dte.Solution?.FindProjectItem(filePath);
            if (projItem == null)
            {
                Log("TryFormatViaAutomation: No ProjectItem found");
                return false;
            }

            object obj = null!;
            try { obj = projItem.Object; } catch { }
            if (obj == null)
            {
                Log("TryFormatViaAutomation: ProjectItem.Object is null");
                return false;
            }

            Type t = obj.GetType();
            Log($"TryFormatViaAutomation: ProjectItem type={t.FullName}");

            // Access Node via COM IDispatch reflection
            object? node = null;
            try
            {
                node = t.InvokeMember("Node",
                    System.Reflection.BindingFlags.GetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public,
                    null, obj, null);
            }
            catch (System.MissingMethodException)
            {
                Log("TryFormatViaAutomation: 'Node' property not found on COM object");
                return false;
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaAutomation: 'Node' access failed: {ex.GetType().Name} - {ex.Message}");
                return false;
            }

            if (node == null)
            {
                Log("TryFormatViaAutomation: Node is null");
                return false;
            }
            Log($"TryFormatViaAutomation: Node found, type={node.GetType().FullName}");

            // Access SysManTreeItem
            Type nodeType = node.GetType();
            object? sysManItem = null;
            try
            {
                sysManItem = nodeType.InvokeMember("SysManTreeItem",
                    System.Reflection.BindingFlags.GetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public,
                    null, node, null);
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaAutomation: 'SysManTreeItem' failed: {ex.GetType().Name} - {ex.Message}");
                return false;
            }

            if (sysManItem == null)
            {
                Log("TryFormatViaAutomation: SysManTreeItem is null");
                return false;
            }
            Log($"TryFormatViaAutomation: SysManTreeItem found, type={sysManItem.GetType().FullName}");

            // Set DeclarationText and ImplementationText
            Type adapterType = sysManItem.GetType();

            if (!string.IsNullOrEmpty(formattedDecl))
            {
                try
                {
                    adapterType.InvokeMember("DeclarationText",
                        System.Reflection.BindingFlags.SetProperty |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public,
                        null, sysManItem, new object[] { formattedDecl });
                    Log("TryFormatViaAutomation: DeclarationText set");
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaAutomation: DeclarationText set failed: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(formattedImpl))
            {
                try
                {
                    adapterType.InvokeMember("ImplementationText",
                        System.Reflection.BindingFlags.SetProperty |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public,
                        null, sysManItem, new object[] { formattedImpl });
                    Log("TryFormatViaAutomation: ImplementationText set");
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaAutomation: ImplementationText set failed: {ex.Message}");
                }
            }

            Log("TryFormatViaAutomation: SUCCESS - live update applied");
            return true;
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaAutomation: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    // Tier 2: DTE Text Selection manipulation (preserves undo history)
    private static bool TryFormatViaTextSelection(EnvDTE.DTE dte,
        string? formattedDecl, string? formattedImpl)
    {
        Log("TryFormatViaTextSelection: Attempting...");
        try
        {
            if (dte.ActiveDocument == null) return false;

            var selection = dte.ActiveDocument.Selection as EnvDTE.TextSelection;
            if (selection == null)
            {
                Log("TryFormatViaTextSelection: No TextSelection available");
                return false;
            }

            // Combine declaration and implementation
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(formattedDecl))
                sb.Append(formattedDecl);
            if (!string.IsNullOrEmpty(formattedImpl))
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(formattedImpl);
            }

            string combined = sb.ToString();
            if (string.IsNullOrEmpty(combined))
            {
                Log("TryFormatViaTextSelection: Empty formatted text");
                return false;
            }

            string currentText;
            try { currentText = selection.Text; } catch { currentText = ""; }

            if (combined == currentText)
            {
                Log("TryFormatViaTextSelection: Text already matches");
                return true;
            }

            selection.SelectAll();
            selection.Delete();
            selection.Insert(combined);
            Log("TryFormatViaTextSelection: SUCCESS - text updated via DTE selection");
            return true;
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaTextSelection: {ex.Message}");
            return false;
        }
    }

    private static string FindParentElement(string xml, int cdataOffset)
    {
        int tagStart = xml.LastIndexOf('<', cdataOffset - 1);
        if (tagStart < 0) return "";
        int tagEnd = xml.IndexOf('>', tagStart);
        if (tagEnd < 0) return "";
        return xml.Substring(tagStart, tagEnd - tagStart + 1);
    }

    // --- Factory ---

    private static class FormattingEngineFactory
    {
        public static FormattingEngine Create()
        {
            return new FormattingEngine(FormattingConfiguration.Default);
        }
    }

    // --- Console hiding ---

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    // --- Logging ---

    private static string? _logPath;

    private static void LogInit()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "STFormatter_Host.log");
    }

    private static void Log(string message)
    {
        try
        {
            if (_logPath != null)
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
