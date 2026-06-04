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
    private static KeyboardHook? _keyboardHook;

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

        EnableHighDpiIfAvailable();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SettingsManager.EnsureLoaded();
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

        _keyboardHook = new KeyboardHook();
        _keyboardHook.FormatDocumentHotkey += pid => HandleFormatDocument(pid);
        _keyboardHook.FormatSelectionHotkey += pid => HandleFormatSelection(pid);

        Maintain();

        var mainForm = new MainForm(
            getInstances: () => GetInstanceInfos(),
            cleanup: () => CleanupStaleInstances(),
            maintainAction: () => Maintain(),
            getStatus: () => _lastScanStatus
        );
        _mainForm = mainForm;
        var dummy = mainForm.Handle; // force handle creation for Invoke

        mainForm.BeginInvoke((Action)(() => _keyboardHook.Start()));

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
                Title = kvp.Value.Title,
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

            _hostManager.RefreshAllTitles();
        }
        catch (Exception ex)
        {
            Log($"Maintain error: {ex.Message}");
        }
    }

    private static void Shutdown()
    {
        _keyboardHook?.Dispose();
        _keyboardHook = null;

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
            System.Windows.Forms.MessageBox.Show(message, "STBud for TwinCAT",
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

    public static void HandleAddPragma(int pid, string pragmaText)
    {
        Log($"HandleAddPragma: PID {pid} pragma=[{pragmaText}]");
        try
        {
            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddPragma: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddPragma: PID {pid} No active document");
                return;
            }

            string fullPragma = pragmaText.Contains("{")
                ? pragmaText
                : $"{{attribute '{pragmaText}'}}";

            bool success = LiveEditor.InsertLineAbove(instance.Dte, fullPragma);
            Log($"HandleAddPragma: PID {pid} result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddPragma: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddWarning(int pid)
    {
        Log($"HandleAddWarning: PID {pid}");
        try
        {
            string? warningText = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.WarningTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.WarningPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    warningText = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(warningText))
            {
                Log("HandleAddWarning: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddWarning: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddWarning: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{warning '{warningText}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddWarning: PID {pid} warning=[{warningText}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddWarning: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddRegion(int pid)
    {
        Log($"HandleAddRegion: PID {pid}");
        try
        {
            string? regionName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.StartRegionTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.StartRegionPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    regionName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(regionName))
            {
                Log("HandleAddRegion: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddRegion: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddRegion: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{region '{regionName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddRegion: PID {pid} region=[{regionName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddRegion: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddStartEndRegion(int pid)
    {
        Log($"HandleAddStartEndRegion: PID {pid}");
        try
        {
            string? regionName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.StartEndRegionTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.StartEndRegionPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    regionName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(regionName))
            {
                Log("HandleAddStartEndRegion: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddStartEndRegion: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddStartEndRegion: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{region '{regionName}'}}\r\n\r\n\r\n{{endregion}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddStartEndRegion: PID {pid} region=[{regionName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddStartEndRegion: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddNoExplicitCall(int pid)
    {
        Log($"HandleAddNoExplicitCall: PID {pid}");
        try
        {
            string? message = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.NoExplicitCallTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.NoExplicitCallPrompt"),
                    "do not call this POU directly");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    message = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(message))
            {
                Log("HandleAddNoExplicitCall: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddNoExplicitCall: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddNoExplicitCall: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'no_explicit_call' := '{message}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddNoExplicitCall: PID {pid} message=[{message}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddNoExplicitCall: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddOpcUaDa(int pid)
    {
        Log($"HandleAddOpcUaDa: PID {pid}");
        try
        {
            string? param = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.OpcUaDaTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.OpcUaDaPrompt"),
                    "1");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    param = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(param))
            {
                Log("HandleAddOpcUaDa: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddOpcUaDa: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddOpcUaDa: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'OPC.UA.DA' := '{param}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddOpcUaDa: PID {pid} param=[{param}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddOpcUaDa: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddAlwaysAverage(int pid)
    {
        Log($"HandleAddAlwaysAverage: PID {pid}");
        try
        {
            string? varName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.AlwaysAverageTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.AlwaysAveragePrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    varName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(varName))
            {
                Log("HandleAddAlwaysAverage: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddAlwaysAverage: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddAlwaysAverage: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'always_average' := '{varName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddAlwaysAverage: PID {pid} varName=[{varName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddAlwaysAverage: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddIOLinking(int pid)
    {
        Log($"HandleAddIOLinking: PID {pid}");
        try
        {
            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddIOLinking: PID {pid} instance not found/alive");
                return;
            }

            string? tsprojPath = null;
            try
            {
                string? solutionPath = instance.Dte.Solution?.FullName;
                if (!string.IsNullOrEmpty(solutionPath))
                    tsprojPath = STFormatter.Core.IoTree.IoTreeParser.FindTsprojFile(solutionPath);
            }
            catch (Exception ex)
            {
                Log($"HandleAddIOLinking: PID {pid} solution path lookup failed: {ex.Message}");
            }

            STFormatter.Core.IoTree.IoTreeNode? ioTree = null;
            if (!string.IsNullOrEmpty(tsprojPath))
            {
                try
                {
                    ioTree = STFormatter.Core.IoTree.IoTreeParser.ParseIoTree(tsprojPath);
                    Log($"HandleAddIOLinking: PID {pid} parsed I/O tree from {tsprojPath}, children={ioTree?.Children.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Log($"HandleAddIOLinking: PID {pid} I/O tree parse failed: {ex.Message}");
                    ioTree = null;
                }
            }

            string? ioPath = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                if (ioTree != null && ioTree.Children.Count > 0)
                {
                    using var dlg = new STFormatter.UI.IoTreeBrowserDialog(ioTree);
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        ioPath = dlg.SelectedPath;
                }
                else
                {
                    using var dlg = new STFormatter.UI.InputDialog(
                        STFormatter.UI.Strings.Get("AddMenu.IOLinkingTitle"),
                        STFormatter.UI.Strings.Get("AddMenu.IOLinkingPrompt"),
                        "");
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        ioPath = dlg.InputText;
                }
            }));

            if (string.IsNullOrEmpty(ioPath))
            {
                Log("HandleAddIOLinking: User cancelled or empty input");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddIOLinking: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'TcLinkTo' := '{ioPath}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddIOLinking: PID {pid} ioPath=[{ioPath}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddIOLinking: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddObsolete(int pid)
    {
        Log($"HandleAddObsolete: PID {pid}");
        try
        {
            string? message = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.ObsoleteTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.ObsoletePrompt"),
                    "use NewPou instead");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    message = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(message))
            {
                Log("HandleAddObsolete: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddObsolete: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddObsolete: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'obsolete' := '{message}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddObsolete: PID {pid} message=[{message}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddObsolete: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddTaskName(int pid)
    {
        Log($"HandleAddTaskName: PID {pid}");
        try
        {
            string? taskName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.TaskNameTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.TaskNamePrompt"),
                    "PlcTask");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    taskName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(taskName))
            {
                Log("HandleAddTaskName: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddTaskName: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddTaskName: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'task_name' := '{taskName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddTaskName: PID {pid} taskName=[{taskName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddTaskName: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallAfter(int pid)
    {
        Log($"HandleAddCallAfter: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallAfter: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallAfter: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallAfter: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_after' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallAfter: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallAfter: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallBefore(int pid)
    {
        Log($"HandleAddCallBefore: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforeTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforePrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallBefore: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallBefore: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallBefore: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_before' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallBefore: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallBefore: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallAfterInit(int pid)
    {
        Log($"HandleAddCallAfterInit: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterInitTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterInitPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallAfterInit: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallAfterInit: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallAfterInit: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_after_init' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallAfterInit: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallAfterInit: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallBeforeInit(int pid)
    {
        Log($"HandleAddCallBeforeInit: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforeInitTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforeInitPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallBeforeInit: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallBeforeInit: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallBeforeInit: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_before_init' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallBeforeInit: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallBeforeInit: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallAfterExit(int pid)
    {
        Log($"HandleAddCallAfterExit: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterExitTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallAfterExitPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallAfterExit: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallAfterExit: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallAfterExit: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_after_exit' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallAfterExit: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallAfterExit: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddCallBeforeExit(int pid)
    {
        Log($"HandleAddCallBeforeExit: PID {pid}");
        try
        {
            string? pouName = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforeExitTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.CallBeforeExitPrompt"),
                    "");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pouName = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(pouName))
            {
                Log("HandleAddCallBeforeExit: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddCallBeforeExit: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddCallBeforeExit: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'call_before_exit' := '{pouName}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddCallBeforeExit: PID {pid} pouName=[{pouName}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddCallBeforeExit: PID {pid} FAILED: {ex.Message}");
        }
    }

    public static void HandleAddPriority(int pid)
    {
        Log($"HandleAddPriority: PID {pid}");
        try
        {
            string? priority = null;
            _mainForm?.Invoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                    ShowWindow(consoleHandle, SW_HIDE);

                using var dlg = new STFormatter.UI.InputDialog(
                    STFormatter.UI.Strings.Get("AddMenu.PriorityTitle"),
                    STFormatter.UI.Strings.Get("AddMenu.PriorityPrompt"),
                    "5");
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    priority = dlg.InputText;
            }));

            if (string.IsNullOrEmpty(priority))
            {
                Log("HandleAddPriority: User cancelled or empty input");
                return;
            }

            var instance = _hostManager?.GetInstance(pid);
            if (instance == null || !_hostManager!.IsInstanceAlive(pid))
            {
                Log($"HandleAddPriority: PID {pid} instance not found/alive");
                return;
            }

            if (instance.Dte.ActiveDocument == null)
            {
                Log($"HandleAddPriority: PID {pid} No active document");
                return;
            }

            string pragmaText = $"{{attribute 'priority' := '{priority}'}}";
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddPriority: PID {pid} priority=[{priority}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddPriority: PID {pid} FAILED: {ex.Message}");
        }
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
            Title = inst?.Title ?? "",
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

    private static void EnableHighDpiIfAvailable()
    {
        try
        {
            var setHighDpiMode = typeof(Application).GetMethod(
                "SetHighDpiMode",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var enumType = Type.GetType("System.Windows.Forms.HighDpiMode, System.Windows.Forms")
                ?? Type.GetType("System.Windows.Forms.HighDpiMode");
            if (setHighDpiMode != null && enumType != null)
            {
                var value = Enum.Parse(enumType, "SystemAware");
                setHighDpiMode.Invoke(null, new[] { value });
            }
        }
        catch
        {
        }
    }

    // --- Logging ---

    private static string _logPath = HostLog.Path;

    private static void LogInit()
    {
        _logPath = HostLog.Path;
    }

    internal static void Log(string message)
    {
        HostLog.Append("Host", message);
    }
}
