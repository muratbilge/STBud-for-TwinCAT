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
using STFormatter.Core.Toolbox;
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
        // Lets Ctrl+Shift+F/D fire inside TwinCAT-in-VS2022 (devenv) windows the
        // Host has registered, without hijacking shortcuts in a plain VS. The
        // try/catch tolerates a rare cross-thread read of the instance map.
        _keyboardHook.IsRegisteredTarget = pid =>
        {
            try { return _hostManager?.GetInstance(pid) != null; }
            catch { return false; }
        };

        Maintain();

        var mainForm = new MainForm(
            getInstances: () => GetInstanceInfos(),
            cleanup: () => CleanupStaleInstances(),
            maintainAction: () => Maintain(),
            getStatus: () => _lastScanStatus
        );
        _mainForm = mainForm;
        var dummy = mainForm.Handle; // force handle creation for Invoke

        // Let the UI's Git tab reach the live editor without taking a COM dependency.
        STFormatter.UI.GitEditorBridge.GetActiveFilePath = TryGetActiveFilePathAny;
        STFormatter.UI.GitEditorBridge.GetActiveSolutionDir = TryGetActiveSolutionDir;
        STFormatter.UI.GitEditorBridge.RestoreToEditor = RestoreLinesToEditor;
        STFormatter.UI.GitEditorBridge.ReadEditorSection = ReadEditorSection;
        STFormatter.UI.GitEditorBridge.WriteAcceptsToDisk = WriteAcceptsToDisk;
        STFormatter.UI.GitEditorBridge.UndoLastSave = UndoLastSave;

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
            // Version in every startup line: without it, log forensics can't tell which
            // build produced a given session (bit us diagnosing a work-machine log).
            string version = typeof(Program).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                is System.Reflection.AssemblyInformationalVersionAttribute[] { Length: > 0 } attrs
                ? attrs[0].InformationalVersion : "unknown";
            Log($"Environment: version={version}, pid={current.Id}, session={current.SessionId}, elevated={elevated}, user='{identity.Name}', exe='{Application.ExecutablePath}'");
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
        // GetAllInstances is a snapshot, so removing during the sweep is safe; still
        // guard each removal so one wedged DTE can't abort the whole cleanup.
        foreach (var kvp in _hostManager.GetAllInstances())
        {
            try
            {
                if (!_hostManager.IsInstanceAlive(kvp.Key))
                {
                    Log($"CleanupStale: Removing dead instance PID {kvp.Key}");
                    _hostManager.CleanupInstance(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                Log($"CleanupStale: PID {kvp.Key} cleanup failed: {ex.Message}");
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
            // Method/action tabs report "<file>.TcPOU;POU.Member" - strip to the real file
            // (the raw pseudo-path made Format Document fail with "File not found").
            string filePath = STFormatter.Core.Configuration.DocumentPath.Normalize(
                dte.ActiveDocument.FullName);
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

            // The on-disk file may be stale when the editor buffer has unsaved
            // changes - only short-circuit on "disk already formatted" when the
            // document is saved; otherwise fall through to the live-edit tier,
            // which reads the editor content itself.
            bool docDirty = false;
            try { docDirty = !dte.ActiveDocument.Saved; } catch { }

            bool diskChanged = FormatTwinCatXml(xmlContent, engine,
                out string formattedXml, out string? formattedDecl, out string? formattedImpl);

            if (!diskChanged && !docDirty)
            {
                Log("HandleFormatDocument: No changes needed");
                return;
            }

            // Tier 1: Automation API (no disk backup needed - the editor buffer
            // is updated; the file write below snapshots first)
            if (diskChanged && TryFormatViaAutomation(dte, filePath, formattedDecl, formattedImpl))
            {
                CreateBackup(filePath);
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

            // Disk-writing tiers below only make sense when the disk content
            // actually changed (they would otherwise clobber unsaved edits with
            // stale disk-derived content)
            if (!diskChanged)
            {
                Log("HandleFormatDocument: live-edit tiers failed and disk is already formatted - giving up");
                return;
            }

            // Tier 4: IVsFileChangeEx + RDT
            CreateBackup(filePath);
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
            ShowInfoMessage("Formatted on disk — reload the file if TwinCAT prompts.");
        }
        catch (Exception ex)
        {
            Log($"HandleFormatDocument: PID {pid} FAILED: {ex.Message}");
            ShowInfoMessage("STBud could not complete the format. See the Host log for details.");
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
                RecordFormat(pid, STFormatter.Core.Configuration.DocumentPath.Normalize(
                        instance.Dte.ActiveDocument?.FullName),
                    "Selection", original, formatted, "Clipboard-Selection", true);
            }
            else if (!success && original == null)
            {
                Log($"HandleFormatSelection: PID {pid} No text selected — showing info");
                ShowInfoMessage("No code selected — select a complete statement block first.");
            }
            else if (!success)
            {
                Log($"HandleFormatSelection: PID {pid} Could not format selection — showing info");
                ShowInfoMessage("Could not format the selection — ST syntax errors or an incomplete fragment. Fix compiler errors, or select a complete statement block.");
            }
        }
        catch (Exception ex)
        {
            Log($"HandleFormatSelection: PID {pid} FAILED: {ex.Message}");
            ShowInfoMessage("STBud could not complete the format. See the Host log for details.");
        }
    }

    // Dialogs must be OWNED by the TcXaeShell main window: an ownerless
    // TopMost dialog hands focus to the next topmost-band window when it
    // closes - e.g. an always-on-top Task Manager - instead of the editor.
    private static System.Windows.Forms.DialogResult ShowDialogOwned(
        System.Windows.Forms.Form dlg, int pid)
    {
        try
        {
            var instance = _hostManager?.GetInstance(pid);
            if (instance != null)
            {
                var hwnd = (IntPtr)(long)instance.Dte.MainWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    dlg.TopMost = false; // owned dialogs stay above their owner
                    return dlg.ShowDialog(new Win32Owner(hwnd));
                }
            }
        }
        catch (Exception ex)
        {
            Log($"ShowDialogOwned: owner lookup failed, showing unowned: {ex.Message}");
        }
        return dlg.ShowDialog();
    }

    private sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public Win32Owner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }

    // Transient format feedback (e.g. "could not format"). A non-blocking tray
    // balloon - NOT a modal MessageBox: an unsolicited modal owned by the wrong
    // editor window (across multiple TcXaeShell/VS2022 instances) could render
    // off-screen and block the IDE with no visible dialog.
    public static void ShowInfoMessage(string message)
    {
        _mainForm?.ShowNotification("STBud for TwinCAT", message);
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

    // ---- Git tools --------------------------------------------------------------

    public static void HandleGitFileHistory(int pid)
    {
        Log($"HandleGitFileHistory: PID {pid}");
        try
        {
            if (!EnsureGitAvailable()) return;
            if (!TryGetActiveFilePath(pid, "HandleGitFileHistory", out string filePath)) return;
            string? repoRoot = ResolveAndLogRepoRoot(pid, "HandleGitFileHistory", filePath);
            _mainForm?.ShowGitForFile(filePath, 0, repoRoot);
        }
        catch (Exception ex)
        {
            Log($"HandleGitFileHistory: PID {pid} FAILED: {ex.Message}");
            ShowInfoMessage("STBud could not open Git history. See the Host log for details.");
        }
    }

    public static void HandleGitCommit(int pid)
    {
        Log($"HandleGitCommit: PID {pid}");
        try
        {
            if (!EnsureGitAvailable()) return;
            if (!TryGetActiveFilePath(pid, "HandleGitCommit", out string filePath)) return;
            string? repoRoot = ResolveAndLogRepoRoot(pid, "HandleGitCommit", filePath);
            // Open the Git tab on the Status sub-tab where staging + commit live.
            _mainForm?.ShowGitForFile(filePath, 2, repoRoot);
        }
        catch (Exception ex)
        {
            Log($"HandleGitCommit: PID {pid} FAILED: {ex.Message}");
            ShowInfoMessage("STBud could not open Git commit. See the Host log for details.");
        }
    }

    public static void HandleGitCompareHead(int pid)
    {
        Log($"HandleGitCompareHead: PID {pid}");
        try
        {
            if (!EnsureGitAvailable()) return;
            if (!TryGetActiveFilePath(pid, "HandleGitCompareHead", out string filePath)) return;

            string? repo = ResolveAndLogRepoRoot(pid, "HandleGitCompareHead", filePath);
            if (repo == null)
            {
                ShowInfoMessage("This file is not inside a git repository.");
                return;
            }

            string rel = STBud.Git.GitClient.RelativePath(repo, filePath);
            var committedSections = STFormatter.Core.Formatting.TwinCatStExtractor.Extract(
                STBud.Git.GitClient.ShowFile(repo, "HEAD", rel));
            var workingSections = System.IO.File.Exists(filePath)
                ? STFormatter.Core.Formatting.TwinCatStExtractor.Extract(System.IO.File.ReadAllText(filePath))
                : new STFormatter.Core.Formatting.TwinCatStExtractor.StSections();

            // For a non-TwinCAT-XML file (.st), Extract returns empty sections; fall
            // back to the raw text so the diff still shows the plain source.
            string committedCombined = committedSections.IsEmpty
                ? STBud.Git.GitClient.ShowFile(repo, "HEAD", rel)
                : committedSections.Combined();
            string workingCombined = workingSections.IsEmpty
                ? (System.IO.File.Exists(filePath) ? System.IO.File.ReadAllText(filePath) : "")
                : workingSections.Combined();

            if (STBud.Git.Diff.LineDiff.AreEqual(committedCombined, workingCombined))
            {
                ShowInfoMessage("No ST changes between HEAD and the working file.");
                return;
            }

            // BeginInvoke (non-blocking) so the TcXaeShell COM/click thread is released
            // immediately — the editor is not frozen while the diff dialog is open. The
            // dialog is owned by the Host main form so it doesn't render ownerless.
            _mainForm?.BeginInvoke((Action)(() =>
            {
                var consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero) ShowWindow(consoleHandle, SW_HIDE);

                STFormatter.UI.DiffViewerForm diff;
                if (!committedSections.IsEmpty || !workingSections.IsEmpty)
                {
                    // Section-aware diff: decl/impl as separate tagged blocks.
                    diff = new STFormatter.UI.DiffViewerForm(
                        $"{System.IO.Path.GetFileName(rel)} @ HEAD <-> working",
                        committedSections, workingSections,
                        STFormatter.UI.GitEditorBridge.RestoreToEditor,
                        STFormatter.UI.Strings.Get("Git.Diff.Committed"),
                        STFormatter.UI.Strings.Get("Git.Diff.Working"),
                        pid,
                        filePath);
                }
                else
                {
                    // Plain .st file — no sections.
                    diff = new STFormatter.UI.DiffViewerForm(
                        $"{System.IO.Path.GetFileName(rel)} @ HEAD <-> working",
                        committedCombined, workingCombined,
                        STFormatter.UI.GitEditorBridge.RestoreToEditor,
                        STFormatter.UI.Strings.Get("Git.Diff.Committed"),
                        STFormatter.UI.Strings.Get("Git.Diff.Working"),
                        pid,
                        filePath);
                }
                using (diff) diff.ShowDialog(_mainForm);
            }));
        }
        catch (Exception ex)
        {
            Log($"HandleGitCompareHead: PID {pid} FAILED: {ex.Message}");
            ShowInfoMessage("STBud could not compare with HEAD. See the Host log for details.");
        }
    }

    /// <summary>Resolve the active document's on-disk path for a given instance.</summary>
    private static bool TryGetActiveFilePath(int pid, string who, out string filePath)
    {
        filePath = "";
        var instance = _hostManager?.GetInstance(pid);
        if (instance == null || !_hostManager!.IsInstanceAlive(pid))
        {
            Log($"{who}: PID {pid} instance not found/alive");
            return false;
        }
        if (instance.Dte.ActiveDocument == null)
        {
            Log($"{who}: PID {pid} No active document");
            ShowInfoMessage("No active editor — open a POU first.");
            return false;
        }
        try
        {
            // Method/action tabs report "<file>.TcPOU;POU.Member" - strip to the real file.
            filePath = STFormatter.Core.Configuration.DocumentPath.Normalize(
                instance.Dte.ActiveDocument.FullName);
        }
        catch (Exception ex)
        {
            Log($"{who}: PID {pid} ActiveDocument.FullName failed: {ex.Message}");
            ShowInfoMessage("Could not determine the active file.");
            return false;
        }
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
        {
            Log($"{who}: PID {pid} file not on disk: {filePath}");
            ShowInfoMessage("Save the file first — STBud's Git tools work on the file on disk.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Resolve the git repo root from the active file, falling back to the solution
    /// folder (where `git init` is typically run) — a POU can live in a subtree that
    /// doesn't walk up to the repo. Logs every anchor so wrong-folder reports are easy
    /// to diagnose.
    /// </summary>
    private static string? ResolveAndLogRepoRoot(int pid, string who, string filePath)
    {
        string? solutionPath = null;
        try { solutionPath = _hostManager?.GetInstance(pid)?.Dte.Solution?.FullName; } catch { }
        string? solutionDir = string.IsNullOrEmpty(solutionPath)
            ? null
            : System.IO.Path.GetDirectoryName(solutionPath);

        string? fromFile = STBud.Git.GitClient.FindRepoRoot(filePath);
        string? fromSln = string.IsNullOrEmpty(solutionDir) ? null : STBud.Git.GitClient.FindRepoRoot(solutionDir);

        // The solution IS the project, so its repo is canonical. Prefer it over the
        // file anchor, which can stop at a stray nested .git below the project.
        string? repo = fromSln ?? fromFile;

        Log($"{who}: PID {pid} repo resolve: file='{filePath}' sln='{solutionPath}' " +
            $"repoFromFile='{fromFile ?? "<none>"}' repoFromSln='{fromSln ?? "<none>"}' -> repo='{repo ?? "<none>"}'");

        // A nested .git under the project shadows the main repo — tell the user once
        // so they can remove it; we still use the main project repo.
        if (fromSln != null && fromFile != null &&
            !string.Equals(fromSln, fromFile, StringComparison.OrdinalIgnoreCase))
        {
            ShowInfoMessage($"Using the main project repo '{fromSln}'. A separate .git was " +
                $"found under the project ('{fromFile}') — remove it if it's unintended.");
        }
        return repo;
    }

    /// <summary>The active solution's directory (anchor for repo-root discovery).</summary>
    private static string? TryGetActiveSolutionDir()
    {
        try
        {
            if (_hostManager == null) return null;
            foreach (var kvp in _hostManager.GetAllInstances())
            {
                if (!_hostManager.IsInstanceAlive(kvp.Key)) continue;
                try
                {
                    string? sln = kvp.Value.Dte.Solution?.FullName;
                    if (!string.IsNullOrEmpty(sln))
                        return System.IO.Path.GetDirectoryName(sln);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"TryGetActiveSolutionDir failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>The active document path of any alive instance (for the Git tab opened from the tray).</summary>
    private static string? TryGetActiveFilePathAny()
    {
        try
        {
            if (_hostManager == null) return null;
            foreach (var kvp in _hostManager.GetAllInstances())
            {
                if (!_hostManager.IsInstanceAlive(kvp.Key)) continue;
                try
                {
                    var doc = kvp.Value.Dte.ActiveDocument;
                    if (doc != null)
                    {
                        // Method tabs report "<file>.TcPOU;POU.Member" - strip to the file.
                        string path = STFormatter.Core.Configuration.DocumentPath.Normalize(doc.FullName);
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            return path;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"TryGetActiveFilePathAny failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Apply a committed block into the editor of the TcXaeShell instance identified by
    /// <paramref name="pid"/>. The git-restore logic lives in the isolated
    /// <see cref="STBud.Git.Editor.EditorRestore"/> (separate from the formatter); this
    /// just resolves the instance, invokes it, and turns the outcome into a user balloon.
    /// </summary>
    private static bool RestoreLinesToEditor(string committed, string working, string? sectionTag, int pid)
    {
        try
        {
            var instance = ResolveInstanceForRestore(pid);
            if (instance == null)
            {
                ShowInfoMessage("No active editor - open the POU in TcXaeShell and try again.");
                return false;
            }

            string wantLabel = sectionTag == "decl" ? "Declaration"
                             : sectionTag == "impl" ? "Implementation" : "section";
            var outcome = STBud.Git.Editor.EditorRestore.Apply(instance.Dte, committed, working, sectionTag);
            switch (outcome)
            {
                case STBud.Git.Editor.RestoreOutcome.AppliedLive:
                    return true;
                case STBud.Git.Editor.RestoreOutcome.AppliedDisk:
                    ShowInfoMessage(
                        $"Restored into the {wantLabel} section on disk.\n\n" +
                        $"TwinCAT will prompt to reload the file — accept it. Save other editor edits first.");
                    return true;
                case STBud.Git.Editor.RestoreOutcome.WrongTabClipboard:
                    ShowInfoMessage(
                        $"STBud couldn't apply the {wantLabel} lines automatically. They're on your " +
                        $"clipboard — switch to the {wantLabel} tab in TcXaeShell and paste (Ctrl+V).");
                    return false;
                case STBud.Git.Editor.RestoreOutcome.NotFoundInEditor:
                    ShowInfoMessage("STBud couldn't find these lines in the editor (it may have unsaved edits). " +
                        "The committed text is on your clipboard — paste it where you want.");
                    return false;
                case STBud.Git.Editor.RestoreOutcome.Ambiguous:
                    ShowInfoMessage("These lines appear more than once, so STBud won't guess which to change. " +
                        "The committed text is on your clipboard — paste it at the right place.");
                    return false;
                default:
                    ShowInfoMessage("STBud could not apply the lines. See the Host log for details.");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log($"RestoreLinesToEditor FAILED: {ex.Message}");
            return false;
        }
    }

    // One-level undo snapshot of the last staged-accept save, keyed by file path.
    private static readonly System.Collections.Generic.Dictionary<string, byte[]> _undoSnapshots =
        new System.Collections.Generic.Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private static string? _lastSavePath;

    // Stable "Save": write the staged accept blocks straight to the working file on disk
    // (the diff's working side was read from this same file, so each block locates cleanly even
    // when the open editor has diverged or the wrong tab is active). Returns (applied, failed).
    private static (int applied, int failed) WriteAcceptsToDisk(
        string filePath,
        System.Collections.Generic.IReadOnlyList<(string committed, string working, string? section)> blocks,
        int pid)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Log($"WriteAcceptsToDisk: file not found: {filePath}");
                return (0, blocks?.Count ?? 0);
            }

            var diskBlocks = new System.Collections.Generic.List<STBud.Git.Editor.EditorRestore.DiskBlock>(blocks.Count);
            foreach (var b in blocks)
                diskBlocks.Add(new STBud.Git.Editor.EditorRestore.DiskBlock(b.committed, b.working, b.section));

            var r = STBud.Git.Editor.EditorRestore.ApplyBlocksToDisk(filePath, diskBlocks);
            if (r.Applied > 0 && r.UndoSnapshot != null)
            {
                _undoSnapshots[filePath] = r.UndoSnapshot;
                _lastSavePath = filePath;
                NudgeEditorReload(pid);
            }
            Log($"WriteAcceptsToDisk: applied={r.Applied} failed={r.Failed} file={System.IO.Path.GetFileName(filePath)}");
            return (r.Applied, r.Failed);
        }
        catch (Exception ex)
        {
            Log($"WriteAcceptsToDisk FAILED: {ex.Message}");
            return (0, blocks?.Count ?? 0);
        }
    }

    // Undo the last WriteAcceptsToDisk by writing the stashed snapshot back to disk.
    private static bool UndoLastSave(int pid)
    {
        try
        {
            string? path = _lastSavePath;
            if (path == null || !_undoSnapshots.TryGetValue(path, out var snap))
            {
                Log("UndoLastSave: nothing to undo");
                return false;
            }
            bool ok = STBud.Git.Editor.EditorRestore.RestoreBytes(path, snap);
            if (ok) { _undoSnapshots.Remove(path); _lastSavePath = null; NudgeEditorReload(pid); }
            return ok;
        }
        catch (Exception ex)
        {
            Log($"UndoLastSave FAILED: {ex.Message}");
            return false;
        }
    }

    // After a Git disk write, foreground TcXaeShell so it detects the external file change and
    // shows its reload prompt (it only re-checks files when its window is activated, and our
    // diff is a modal dialog that keeps it in the background). Git-only — never the formatter.
    private static void NudgeEditorReload(int pid)
    {
        try
        {
            var instance = ResolveInstanceForRestore(pid);
            if (instance != null) STBud.Git.Editor.EditorRestore.BringToForeground(instance.Dte);
        }
        catch (Exception ex) { Log($"NudgeEditorReload: {ex.Message}"); }
    }

    /// <summary>
    /// Read the active editor section's text for the TcXaeShell instance identified by
    /// <paramref name="pid"/>. Used by the diff viewer to refresh after a restore.
    /// </summary>
    private static string? ReadEditorSection(int pid)
    {
        try
        {
            var instance = ResolveInstanceForRestore(pid);
            if (instance == null) return null;
            return STBud.Git.Editor.EditorRestore.ReadActiveSectionText(instance.Dte);
        }
        catch (Exception ex)
        {
            Log($"ReadEditorSection FAILED: {ex.Message}");
            return null;
        }
    }

    // Resolve the instance the diff was opened from. Falls back to the old
    // FindActiveInstance behavior only when the pid is unknown (0/-1), which happens
    // for the format-history diff viewer that isn't tied to a specific editor.
    private static TcXaeInstance? ResolveInstanceForRestore(int pid)
    {
        if (_hostManager == null) return null;

        if (pid > 0)
        {
            if (_hostManager.IsInstanceAlive(pid))
            {
                var inst = _hostManager.GetInstance(pid);
                if (inst != null)
                {
                    try { if (inst.Dte.ActiveDocument != null) return inst; }
                    catch { }
                }
            }
            // If the originating instance died, fall through to FindActiveInstance
            // rather than refusing outright — the user may have restarted TcXaeShell.
        }
        return FindActiveInstance();
    }

    private static bool EnsureGitAvailable()
    {
        if (STBud.Git.GitClient.IsGitAvailable(out _)) return true;
        ShowInfoMessage(STFormatter.UI.Strings.Get("Git.GitMissing"));
        return false;
    }

    private static TcXaeInstance? FindActiveInstance()
    {
        if (_hostManager == null) return null;

        foreach (var kvp in _hostManager.GetAllInstances())
        {
            if (!_hostManager.IsInstanceAlive(kvp.Key)) continue;
            try { if (kvp.Value.Dte.ActiveDocument != null) return kvp.Value; }
            catch { }
        }
        foreach (var kvp in _hostManager.GetAllInstances())
            if (_hostManager.IsInstanceAlive(kvp.Key)) return kvp.Value;
        return null;
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

            string fullPragma = PragmaTemplates.WrapMenuPragma(pragmaText);

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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Warning(warningText);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.RegionStart(regionName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.RegionBlock(regionName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("no_explicit_call", message);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("OPC.UA.DA", param);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("always_average", varName);
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
                    if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
                        ioPath = dlg.SelectedPath;
                }
                else
                {
                    using var dlg = new STFormatter.UI.InputDialog(
                        STFormatter.UI.Strings.Get("AddMenu.IOLinkingTitle"),
                        STFormatter.UI.Strings.Get("AddMenu.IOLinkingPrompt"),
                        "");
                    if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("TcLinkTo", ioPath);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("obsolete", message);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("task_name", taskName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_after", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_before", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_after_init", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_before_init", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_after_exit", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("call_before_exit", pouName);
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
                if (ShowDialogOwned(dlg, pid) == System.Windows.Forms.DialogResult.OK)
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

            string pragmaText = PragmaTemplates.Attribute("priority", priority);
            bool success = LiveEditor.InsertLineAbove(instance.Dte, pragmaText);
            Log($"HandleAddPriority: PID {pid} priority=[{priority}] result={(success ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            Log($"HandleAddPriority: PID {pid} FAILED: {ex.Message}");
        }
    }

    // Backup only before tiers that write to disk (AGENTS.md: backups are for
    // the file-write fallback, not for live edits)
    private static void CreateBackup(string filePath)
    {
        try
        {
            File.Copy(filePath, filePath + ".bak", true);
        }
        catch (Exception ex)
        {
            Log($"CreateBackup: failed for {filePath}: {ex.Message}");
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

            // Only report success when every required setter actually worked -
            // a false positive here makes the caller overwrite the file on disk
            // while the editor still holds the unformatted buffer.
            bool declOk = string.IsNullOrEmpty(formattedDecl);
            if (!declOk)
            {
                try
                {
                    adapterType.InvokeMember("DeclarationText",
                        BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public,
                        null, sysManItem, new object[] { formattedDecl! });
                    Log("TryFormatViaAutomation: DeclarationText set");
                    declOk = true;
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaAutomation: DeclarationText set failed: {ex.Message}");
                }
            }

            bool implOk = string.IsNullOrEmpty(formattedImpl);
            if (!implOk)
            {
                try
                {
                    adapterType.InvokeMember("ImplementationText",
                        BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public,
                        null, sysManItem, new object[] { formattedImpl! });
                    Log("TryFormatViaAutomation: ImplementationText set");
                    implOk = true;
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaAutomation: ImplementationText set failed: {ex.Message}");
                }
            }

            bool attemptedAny = !string.IsNullOrEmpty(formattedDecl) || !string.IsNullOrEmpty(formattedImpl);
            if (attemptedAny && declOk && implOk)
            {
                Log("TryFormatViaAutomation: SUCCESS");
                return true;
            }

            Log("TryFormatViaAutomation: setters incomplete - reporting failure");
            return false;
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
