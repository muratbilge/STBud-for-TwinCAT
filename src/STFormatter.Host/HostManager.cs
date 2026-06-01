using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using STFormatter.Core.Configuration;

namespace STFormatter.Host;

internal sealed class TcXaeInstance
{
    public int Pid { get; }
    public DTE Dte { get; private set; }
    public TcXaeShellVersionProfile? VersionProfile { get; set; }
    public HashSet<string> InjectedMenus { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CommandBarControl> InjectedControls { get; } = new();
    public int FormatCount { get; set; }
    public DateTime? LastFormatTime { get; set; }
    public string Title { get; set; } = "";

    public TcXaeInstance(int pid, DTE dte)
    {
        Pid = pid;
        Dte = dte;
    }

    public void UpdateDte(DTE dte)
    {
        Dte = dte;
    }

    public void Clear()
    {
        InjectedMenus.Clear();
        InjectedControls.Clear();
    }

    public void RefreshTitle()
    {
        try
        {
            if (Dte.Solution != null && !string.IsNullOrEmpty(Dte.Solution.FullName))
                Title = Path.GetFileNameWithoutExtension(Dte.Solution.FullName);
            else
                Title = "";
        }
        catch
        {
            Title = "";
        }
    }
}

internal sealed class HostManager
{
    private readonly Dictionary<int, TcXaeInstance> _instances = new();
    private string[] _targetMenus = TcXaeShellVersionProfile.VS2017.TargetContextMenuNames;

    private static void Log(string message)
    {
        STFormatter.Core.Configuration.HostLog.Append("HostManager", message);
    }

    // --- Public API ---

    public TcXaeInstance Register(int pid, DTE dte)
    {
        if (_instances.TryGetValue(pid, out var existing))
        {
            existing.UpdateDte(dte);
            Log($"Register: PID {pid} reconnected (existing instance)");
            return existing;
        }

        var instance = new TcXaeInstance(pid, dte);
        instance.RefreshTitle();
        _instances[pid] = instance;
        Log($"Register: PID {pid} new instance (total tracked: {_instances.Count}) title='{instance.Title}'");
        return instance;
    }

    public void Unregister(int pid)
    {
        if (_instances.Remove(pid))
            Log($"Unregister: PID {pid} removed (remaining: {_instances.Count})");
    }

    public TcXaeInstance? GetInstance(int pid)
    {
        _instances.TryGetValue(pid, out var instance);
        return instance;
    }

    public int InstanceCount => _instances.Count;

    public IReadOnlyDictionary<int, TcXaeInstance> GetAllInstances() => _instances;

    public void RefreshAllTitles()
    {
        foreach (var kvp in _instances)
        {
            try { kvp.Value.RefreshTitle(); }
            catch { }
        }
    }

    public void InjectButtons(TcXaeInstance instance)
    {
        if (instance.InjectedMenus.Count > 0)
        {
            Log($"InjectButtons: PID {instance.Pid} already has injected menus: {string.Join(", ", instance.InjectedMenus)}");
            return;
        }

        try
        {
            var commandBars = (CommandBars)instance.Dte.CommandBars;

            foreach (var menuName in _targetMenus)
            {
                try
                {
                    CommandBar? cb = commandBars[menuName];
                    if (cb != null && cb.Type == MsoBarType.msoBarTypePopup)
                    {
                        Log($"InjectButtons: PID {instance.Pid} found menu '{cb.Name}' (Controls={cb.Controls.Count})");

                        // Always remove stale buttons before injecting
                        int removed = RemoveStaleButtons(cb);
                        if (removed > 0)
                            Log($"InjectButtons: Removed {removed} stale button(s) from '{cb.Name}'");

                        AddButtons(instance, cb);
                        instance.InjectedMenus.Add(cb.Name);
                    }
                }
                catch (Exception ex)
                {
                    Log($"InjectButtons: PID {instance.Pid} skipped '{menuName}': {ex.Message}");
                }
            }

            if (instance.InjectedMenus.Count > 0)
            {
                Log($"InjectButtons: PID {instance.Pid} injected into: {string.Join(", ", instance.InjectedMenus)}");
            }
        }
        catch (Exception ex)
        {
            Log($"InjectButtons: PID {instance.Pid} FAILED: {ex.Message}");
        }
    }

    public void CleanupInstance(int pid)
    {
        var instance = GetInstance(pid);
        if (instance == null)
        {
            Log($"CleanupInstance: PID {pid} already cleaned up, skipping");
            return;
        }

        Log($"CleanupInstance: PID {pid} removing {instance.InjectedControls.Count} controls");

        foreach (var ctrl in instance.InjectedControls)
        {
            try { ctrl.Delete(false); } catch { }
        }

        instance.Clear();
        Unregister(pid);
        Log($"CleanupInstance: PID {pid} complete");
    }

    // --- ROT Scanning ---

    public (int pid, DTE dte, TcXaeShellVersionProfile profile)? FindNewTcXaeShell()
    {
        int hr = Ole32.GetRunningObjectTable(0, out IRunningObjectTable rot);
        if (hr != 0) return null;

        rot.EnumRunning(out IEnumMoniker enumMoniker);
        var monikers = new IMoniker[1];
        IntPtr fetched = IntPtr.Zero;
        Ole32.CreateBindCtx(0, out IBindCtx bindCtx);
        (int pid, DTE dte, TcXaeShellVersionProfile profile)? result = null;

        while (enumMoniker.Next(1, monikers, fetched) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(bindCtx, null, out string displayName);

                var detectedProfile = TcXaeShellVersionProfile.DetectFromRotMoniker(displayName);
                if (detectedProfile != null)
                {
                    int colonIdx = displayName.LastIndexOf(':');
                    if (colonIdx > 0 && int.TryParse(displayName.Substring(colonIdx + 1), out int pid))
                    {
                        if (_instances.ContainsKey(pid)) continue;

                        try
                        {
                            int hrObj = rot.GetObject(monikers[0], out object obj);
                            if (hrObj == 0 && obj is DTE dte && IsTcXaeShell(dte))
                            {
                                result = (pid, dte, detectedProfile);
                                Log($"FindNewTcXaeShell: Found PID {pid} profile={detectedProfile}");
                                break;
                            }
                            else
                            {
                                Marshal.ReleaseComObject(obj as DTE);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.ReleaseComObject(monikers[0]);
            }
        }

        Marshal.ReleaseComObject(enumMoniker);
        Marshal.ReleaseComObject(bindCtx);
        Marshal.ReleaseComObject(rot);

        return result;
    }

    public string GetScanDiagnostics()
    {
        int processCount = 0;
        string processPids = "none";
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(TcXaeShellVersionProfile.VS2017.ProcessName);
            processCount = processes.Length;
            if (processes.Length > 0)
                processPids = string.Join(", ", Array.ConvertAll(processes, p => p.Id.ToString()));
        }
        catch { }

        int rotDteCount = 0;
        int tcXaeRotCount = 0;
        var tcXaeMonikers = new List<string>();

        try
        {
            int hr = Ole32.GetRunningObjectTable(0, out IRunningObjectTable rot);
            if (hr != 0)
                return $"TcXaeShell processes={processCount} ({processPids}); ROT unavailable hr=0x{hr:X8}";

            rot.EnumRunning(out IEnumMoniker enumMoniker);
            Ole32.CreateBindCtx(0, out IBindCtx bindCtx);

            var monikers = new IMoniker[1];
            IntPtr fetched = IntPtr.Zero;
            while (enumMoniker.Next(1, monikers, fetched) == 0)
            {
                try
                {
                    monikers[0].GetDisplayName(bindCtx, null, out string displayName);
                    if (displayName.IndexOf("DTE", StringComparison.OrdinalIgnoreCase) >= 0)
                        rotDteCount++;

                    if (TcXaeShellVersionProfile.DetectFromRotMoniker(displayName) != null)
                    {
                        tcXaeRotCount++;
                        if (tcXaeMonikers.Count < 3)
                            tcXaeMonikers.Add(displayName);
                    }
                }
                catch { }
                finally
                {
                    if (monikers[0] != null)
                        Marshal.ReleaseComObject(monikers[0]);
                }
            }

            Marshal.ReleaseComObject(enumMoniker);
            Marshal.ReleaseComObject(bindCtx);
            Marshal.ReleaseComObject(rot);
        }
        catch (Exception ex)
        {
            return $"TcXaeShell processes={processCount} ({processPids}); ROT diagnostics failed: {ex.Message}";
        }

        string monikerSummary = tcXaeMonikers.Count > 0 ? string.Join(" | ", tcXaeMonikers) : "none";
        if (processCount > 0 && tcXaeRotCount == 0)
            return $"TcXaeShell process found ({processPids}), but no TcXaeShell DTE ROT moniker is visible. Likely elevation/session mismatch or TcXaeShell not ready. DTE ROT entries={rotDteCount}";

        return $"TcXaeShell processes={processCount} ({processPids}); TcXaeShell ROT entries={tcXaeRotCount}; DTE ROT entries={rotDteCount}; monikers={monikerSummary}";
    }

    public bool IsInstanceAlive(int pid)
    {
        try
        {
            var instance = GetInstance(pid);
            if (instance?.Dte == null) return false;

            // Touch DTE to check if COM object is still alive
            // Use a short timeout to avoid hanging on dead processes
            var name = instance.Dte.Name;
            return !string.IsNullOrEmpty(name);
        }
        catch (COMException)
        {
            // COM object disconnected — definitely dead
            return false;
        }
        catch
        {
            // Transient error — don't mark as dead, keep retrying
            return true;
        }
    }

    // --- Private helpers ---

    private static bool IsTcXaeShell(DTE dte)
    {
        try
        {
            string name = dte.Name ?? "";
            foreach (var profile in TcXaeShellVersionProfile.AllProfiles)
            {
                if (name.IndexOf(profile.DteNameMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (name.IndexOf("TcXae", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static int RemoveStaleButtons(CommandBar menu)
    {
        int removed = 0;
        // Iterate backwards since we modify the collection
        for (int i = menu.Controls.Count; i >= 1; i--)
        {
            try
            {
                var ctrl = menu.Controls[i];
                if (ctrl is CommandBarButton btn)
                {
                    string? tag = btn.Tag as string;
                    if (!string.IsNullOrEmpty(tag) &&
                        (tag.StartsWith("STFormatter.", StringComparison.OrdinalIgnoreCase)))
                    {
                        btn.Delete(false);
                        removed++;
                    }
                }
                else if (ctrl.Caption == "-" && ctrl.BeginGroup)
                {
                    // A separator that starts a group - could be ours
                    // But we can't reliably identify it, skip
                }
            }
            catch { }
        }
        return removed;
    }

    private void AddButtons(TcXaeInstance instance, CommandBar targetMenu)
    {
        try
        {
            // Separator
            var sep = (CommandBarControl)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            sep.BeginGroup = true;
            sep.Caption = "-";
            sep.Visible = false;
            instance.InjectedControls.Add(sep);

            // Format ST Document
            var docBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            docBtn.Caption = "Format ST Document";
            docBtn.TooltipText = "Format the entire ST document";
            docBtn.Tag = "STFormatter.FormatDocument";
            docBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleFormatDocument(instance.Pid);
            instance.InjectedControls.Add(docBtn);

            // Format Selected Code
            var selBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            selBtn.Caption = "Format Selected Code";
            selBtn.TooltipText = "Format the selected ST code (select text first)";
            selBtn.Tag = "STFormatter.FormatSelectedCode";
            selBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleFormatSelection(instance.Pid);
            instance.InjectedControls.Add(selBtn);

            // Open Formatter Settings
            var settingsBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            settingsBtn.Caption = "ST Formatter Settings";
            settingsBtn.TooltipText = "Open the ST Formatter settings window";
            settingsBtn.Tag = "STFormatter.OpenSettings";
            settingsBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.ShowSettingsGui();
            instance.InjectedControls.Add(settingsBtn);

            Log($"AddButtons: PID {instance.Pid} +3 buttons to '{targetMenu.Name}'");
        }
        catch (Exception ex)
        {
            Log($"AddButtons: PID {instance.Pid} FAILED for '{targetMenu.Name}': {ex.Message}");
        }
    }
}

// P/Invoke helpers
internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

    [DllImport("ole32.dll")]
    public static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
}
