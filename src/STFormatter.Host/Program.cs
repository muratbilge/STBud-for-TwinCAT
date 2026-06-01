using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using EnvDTE;
using STFormatter.Core.Formatting;
using STFormatter.Core.Configuration;
using STFormatter.UI;

namespace STFormatter.Host;

internal class Program
{
    private static HostManager? _hostManager;
    private static volatile bool _running = true;
    private static MainForm? _mainForm;
    private static Mutex? _singleInstanceMutex;
    private static string _lastScanStatus = "Not scanned yet";
    private static DateTime _lastNoInstanceLog = DateTime.MinValue;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private const int SW_HIDE = 0;

    [STAThread]
    static void Main(string[] args)
    {
        var consoleHandle = GetConsoleWindow();
        if (consoleHandle != IntPtr.Zero)
            ShowWindow(consoleHandle, SW_HIDE);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        LogInit();
        Log("=== STFormatter.Host started ===");

        _singleInstanceMutex = new Mutex(true, "Global\\STFormatter.Host", out bool createdNew);
        if (!createdNew)
        {
            Log("Another STFormatter.Host instance is already running; exiting duplicate process");
            return;
        }

        LogHostEnvironment();

        _hostManager = new HostManager();

        Maintain();

        var mainForm = new MainForm(
            getInstances: () => GetInstanceInfos(),
            cleanup: () => CleanupStaleInstances(),
            maintainAction: () => Maintain(),
            getStatus: () => _lastScanStatus
        );
        _mainForm = mainForm;

        Application.Run(mainForm);

        Log("Host shutting down");
        Shutdown();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
    }

    private static void LogHostEnvironment()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            Log($"Environment: pid={current.Id}, session={current.SessionId}, elevated={elevated}, user='{identity.Name}', exe='{Application.ExecutablePath}'");
        }
        catch (Exception ex)
        {
            Log($"Environment: failed to inspect elevation/session: {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<int, InstanceInfo> GetInstanceInfos()
    {
        var result = new Dictionary<int, InstanceInfo>();
        if (_hostManager == null) return result;

        foreach (var kvp in _hostManager.GetAllInstances())
        {
            bool alive = _hostManager.IsInstanceAlive(kvp.Key);
            result[kvp.Key] = new InstanceInfo
            {
                Connected = alive,
                InjectedMenus = string.Join(", ", kvp.Value.InjectedMenus),
                LastFormatTime = kvp.Value.LastFormatTime,
                FormatCount = kvp.Value.FormatCount
            };
        }
        return result;
    }

    private static void CleanupStaleInstances()
    {
        if (_hostManager == null) return;
        var snapshot = _hostManager.GetAllInstances();
        foreach (var kvp in snapshot)
        {
            if (!_hostManager.IsInstanceAlive(kvp.Key))
            {
                Log($"CleanupStale: Removing dead instance PID {kvp.Key}");
                _hostManager.CleanupInstance(kvp.Key);
            }
        }
    }

    private static void Maintain()
    {
        if (_hostManager == null) return;

        try
        {
            var found = _hostManager.FindNewTcXaeShell();
            if (found != null)
            {
                var (pid, dte, profile) = found.Value;
                var instance = _hostManager.Register(pid, dte);
                instance.VersionProfile = profile;

                dte.Events.DTEEvents.OnBeginShutdown += () =>
                {
                    Log($"DTE Shutdown: PID {pid}");
                    _hostManager.CleanupInstance(pid);
                };

                _hostManager.InjectButtons(instance);
                _lastScanStatus = $"Connected to TcXaeShell PID {pid}";
            }
            else if (_hostManager.InstanceCount == 0)
            {
                _lastScanStatus = _hostManager.GetScanDiagnostics();
                if ((DateTime.Now - _lastNoInstanceLog).TotalSeconds >= 10)
                {
                    Log($"Scan: {_lastScanStatus}");
                    _lastNoInstanceLog = DateTime.Now;
                }
            }

            var snapshot = _hostManager.GetAllInstances();
            foreach (var kvp in snapshot)
            {
                if (kvp.Value.InjectedMenus.Count == 0)
                {
                    _hostManager.InjectButtons(kvp.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Maintain error: {ex.Message}");
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

    // --- Format handlers ---

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
            var profile = instance.VersionProfile ?? TcXaeShellVersionProfile.VS2017;
            var extensions = profile.TwinCatFileExtensions;
            bool isKnownExt = false;
            foreach (var knownExt in extensions)
            {
                if (string.Equals(ext, knownExt, StringComparison.OrdinalIgnoreCase))
                {
                    isKnownExt = true;
                    break;
                }
            }
            if (!isKnownExt)
            {
                Log($"HandleFormatDocument: Not a TwinCAT XML file ({ext})");
                return;
            }

            string xmlContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var config = SettingsManager.Current;
            var engine = new FormattingEngine(config);

            if (!FormatTwinCatXml(xmlContent, engine, out string formattedXml, out string? formattedDecl, out string? formattedImpl))
            {
                Log("HandleFormatDocument: No changes needed");
                return;
            }

            string backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, true);

            // Tier 1: Automation API
            if (TryFormatViaAutomation(dte, filePath, formattedDecl, formattedImpl))
            {
                File.WriteAllText(filePath, formattedXml, System.Text.Encoding.UTF8);
                Log("HandleFormatDocument: Tier 1 (Automation API) succeeded");
                RecordFormat(pid, filePath, "Declaration+Implementation",
                    xmlContent, formattedXml, "Automation", true);
                return;
            }

            // Tier 2: DTE ExecuteCommand + Clipboard (live edit)
            if (LiveEditor.TryFormatViaExecuteCommand(dte, filePath, formattedDecl, formattedImpl,
                out string? orig2, out string? fmt2))
            {
                Log("HandleFormatDocument: Tier 2 (ExecuteCommand + Clipboard) succeeded");
                RecordFormat(pid, filePath, "LiveEdit",
                    orig2 ?? "", fmt2 ?? "", "ExecuteCommand", true);
                return;
            }

            // Tier 3: SendKeys fallback
            if (LiveEditor.TryFormatViaSendKeys(dte, filePath, formattedDecl, formattedImpl,
                out string? orig3, out string? fmt3))
            {
                Log("HandleFormatDocument: Tier 3 (SendKeys) succeeded");
                RecordFormat(pid, filePath, "LiveEdit",
                    orig3 ?? "", fmt3 ?? "", "SendKeys", true);
                return;
            }

            // Tier 4: IVsFileChangeEx + RDT
            if (LiveEditor.TryFormatViaRdtFileWrite(dte, filePath, formattedXml, engine))
            {
                Log("HandleFormatDocument: Tier 4 (RDT File Write) succeeded");
                RecordFormat(pid, filePath, "FileWrite",
                    xmlContent, formattedXml, "RdtFileWrite", true);
                return;
            }

            // Tier 5: Plain file write
            Log("HandleFormatDocument: All live-edit tiers failed, writing to disk");
            File.WriteAllText(filePath, formattedXml, System.Text.Encoding.UTF8);
            RecordFormat(pid, filePath, "FileWrite",
                xmlContent, formattedXml, "PlainFileWrite", true);
            Log("HandleFormatDocument: Written to disk — user must reload");
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

            string? original, formatted;
            bool success = LiveEditor.TryFormatSelectionViaExecuteCommand(
                instance.Dte, out original, out formatted);

            if (success && original != null && formatted != null)
            {
                RecordFormat(pid, instance.Dte.ActiveDocument?.FullName ?? "",
                    "Selection", original, formatted, "Clipboard-Selection", true);
            }
            else if (!success && original == null)
            {
                Log($"HandleFormatSelection: PID {pid} No text selected — showing info");
                ShowInfoMessage("No text selected.\n\nPlease select code first, then use Format Selected Code.");
            }
            else if (!success)
            {
                Log($"HandleFormatSelection: PID {pid} Could not format selection — showing info");
                ShowInfoMessage("Could not format the selected code.\n\nThe selection may be too small or contain incomplete ST syntax.\nTry selecting a larger code block or use Format Document instead.");
            }
        }
        catch (Exception ex)
        {
            Log($"HandleFormatSelection: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void ShowInfoMessage(string message)
    {
        _mainForm?.Invoke((Action)(() =>
        {
            var consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
                ShowWindow(consoleHandle, SW_HIDE);
            System.Windows.Forms.MessageBox.Show(message, "ST Formatter",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }));
    }

    public static void ShowSettingsGui()
    {
        _mainForm?.Invoke((Action)(() =>
        {
            _mainForm.ShowWindow(0);
            var consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
                ShowWindow(consoleHandle, SW_HIDE);
        }));
    }

    private static void RecordFormat(int pid, string filePath, string section,
        string original, string formatted, string method, bool success)
    {
        var inst = _hostManager?.GetInstance(pid);
        if (inst != null)
        {
            inst.FormatCount++;
            inst.LastFormatTime = DateTime.Now;
        }

        var record = new FormatRecord
        {
            Timestamp = DateTime.Now,
            FilePath = filePath,
            Section = section,
            OriginalText = original,
            FormattedText = formatted,
            Pid = pid,
            Success = success,
            Method = method
        };
        _mainForm?.AddFormatRecord(record);
    }

    // --- Three-tier live formatting ---

    private static bool FormatTwinCatXml(string xml, FormattingEngine engine,
        out string formattedXml, out string? formattedDecl, out string? formattedImpl)
    {
        var formatter = new TwinCatXmlFormatter(engine);
        return formatter.FormatXmlContent(xml, out formattedXml, out formattedDecl, out formattedImpl);
    }

    // Tier 1: Automation API via COM InvokeMember
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

            object? node = null;
            try
            {
                node = t.InvokeMember("Node",
                    BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public,
                    null, obj, null);
            }
            catch (MissingMethodException)
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

            Type nodeType = node.GetType();
            object? sysManItem = null;
            try
            {
                sysManItem = nodeType.InvokeMember("SysManTreeItem",
                    BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public,
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

            Type adapterType = sysManItem.GetType();

            if (!string.IsNullOrEmpty(formattedDecl))
            {
                try
                {
                    adapterType.InvokeMember("DeclarationText",
                        BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public,
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
                        BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public,
                        null, sysManItem, new object[] { formattedImpl });
                    Log("TryFormatViaAutomation: ImplementationText set");
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaAutomation: ImplementationText set failed: {ex.Message}");
                }
            }

            Log("TryFormatViaAutomation: SUCCESS");
            return true;
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaAutomation: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    // --- Logging ---

    private static string? _logPath;

    private static void LogInit()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "STFormatter_Host.log");
    }

    internal static void Log(string message)
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
