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
                else if (ctrl is CommandBarPopup popup)
                {
                    string? tag = popup.Tag as string;
                    if (!string.IsNullOrEmpty(tag) &&
                        tag.StartsWith("STFormatter.", StringComparison.OrdinalIgnoreCase))
                    {
                        popup.Delete(false);
                        removed++;
                    }
                }
                else if (ctrl.Caption == "-" && ctrl.BeginGroup)
                {
                }
            }
            catch { }
        }
        return removed;
    }

    private static void TrySetIcon(CommandBarButton btn, int faceId)
    {
        try
        {
            btn.Style = MsoButtonStyle.msoButtonIconAndCaption;
            btn.FaceId = faceId;
        }
        catch
        {
            try { btn.Style = MsoButtonStyle.msoButtonCaption; } catch { }
        }
    }

    private void AddButtons(TcXaeInstance instance, CommandBar targetMenu)
    {
        try
        {
            var sep = (CommandBarControl)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            sep.BeginGroup = true;
            sep.Caption = "-";
            sep.Visible = false;
            instance.InjectedControls.Add(sep);

            var docBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            docBtn.Caption = "Format ST Document";
            docBtn.TooltipText = "Format the entire ST document";
            docBtn.Tag = "STFormatter.FormatDocument";
            TrySetIcon(docBtn, 58);
            docBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleFormatDocument(instance.Pid);
            instance.InjectedControls.Add(docBtn);

            var selBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            selBtn.Caption = "Format Selected Code";
            selBtn.TooltipText = "Format the selected ST code (select text first)";
            selBtn.Tag = "STFormatter.FormatSelectedCode";
            TrySetIcon(selBtn, 593);
            selBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleFormatSelection(instance.Pid);
            instance.InjectedControls.Add(selBtn);

            var settingsBtn = (CommandBarButton)targetMenu.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            settingsBtn.Caption = "ST Formatter Settings";
            settingsBtn.TooltipText = "Open the ST Formatter settings window";
            settingsBtn.Tag = "STFormatter.OpenSettings";
            TrySetIcon(settingsBtn, 277);
            settingsBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.ShowSettingsGui();
            instance.InjectedControls.Add(settingsBtn);

            AddPragmaSubmenu(instance, targetMenu);

            Log($"AddButtons: PID {instance.Pid} +buttons to '{targetMenu.Name}'");
        }
        catch (Exception ex)
        {
            Log($"AddButtons: PID {instance.Pid} FAILED for '{targetMenu.Name}': {ex.Message}");
        }
    }

    private void AddPragmaSubmenu(TcXaeInstance instance, CommandBar targetMenu)
    {
        var addPopup = (CommandBarPopup)targetMenu.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        addPopup.Caption = STFormatter.UI.Strings.Get("AddMenu.Add");
        addPopup.Tag = "STFormatter.Add";
        addPopup.TooltipText = "Insert pragmas, attributes, regions, and warnings";
        instance.InjectedControls.Add(addPopup);

        // -- Attribute submenu --
        var attrPopup = (CommandBarPopup)addPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        attrPopup.Caption = STFormatter.UI.Strings.Get("AddMenu.Attribute");
        attrPopup.Tag = "STFormatter.Add.Attribute";
        instance.InjectedControls.Add(attrPopup);

        string[] attributes = {
            "qualified_only", "strict", "hide", "to_string",
            "no_analysis", "linkalways", "TcGenerated", "const_non_replaced",
        };
        foreach (var attr in attributes)
        {
            var btn = (CommandBarButton)attrPopup.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            btn.Caption = "{attribute '" + attr + "'}";
            btn.Tag = "STFormatter.Add.Attr." + attr;
            TrySetIcon(btn, 1722);
            var capturedAttr = attr;
            btn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleAddPragma(instance.Pid, capturedAttr);
            instance.InjectedControls.Add(btn);
        }

        var monitoringBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        monitoringBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.monitoring");
        monitoringBtn.Tag = "STFormatter.Add.Attr.monitoring";
        TrySetIcon(monitoringBtn, 1722);
        monitoringBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "{attribute 'monitoring' := 'call'}");
        instance.InjectedControls.Add(monitoringBtn);

        var rpcBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        rpcBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.TcRpcEnable");
        rpcBtn.Tag = "STFormatter.Add.Attr.TcRpcEnable";
        TrySetIcon(rpcBtn, 1722);
        rpcBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "{attribute 'TcRpcEnable' := '1'}");
        instance.InjectedControls.Add(rpcBtn);

        // Separator before parameterized attributes
        var attrSepBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        attrSepBtn.BeginGroup = true;
        attrSepBtn.Caption = "-";
        attrSepBtn.Tag = "STFormatter.Add.Attr.Sep";
        attrSepBtn.Visible = false;
        instance.InjectedControls.Add(attrSepBtn);

        var noExplicitCallBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        noExplicitCallBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.NoExplicitCall");
        noExplicitCallBtn.Tag = "STFormatter.Add.Attr.NoExplicitCall";
        TrySetIcon(noExplicitCallBtn, 1722);
        noExplicitCallBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddNoExplicitCall(instance.Pid);
        instance.InjectedControls.Add(noExplicitCallBtn);

        var opcUaDaBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        opcUaDaBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.OpcUaDa");
        opcUaDaBtn.Tag = "STFormatter.Add.Attr.OpcUaDa";
        TrySetIcon(opcUaDaBtn, 1722);
        opcUaDaBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddOpcUaDa(instance.Pid);
        instance.InjectedControls.Add(opcUaDaBtn);

        var alwaysAverageBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        alwaysAverageBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.AlwaysAverage");
        alwaysAverageBtn.Tag = "STFormatter.Add.Attr.AlwaysAverage";
        TrySetIcon(alwaysAverageBtn, 1722);
        alwaysAverageBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddAlwaysAverage(instance.Pid);
        instance.InjectedControls.Add(alwaysAverageBtn);

        var obsoleteBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        obsoleteBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.Obsolete");
        obsoleteBtn.Tag = "STFormatter.Add.Attr.Obsolete";
        TrySetIcon(obsoleteBtn, 1722);
        obsoleteBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddObsolete(instance.Pid);
        instance.InjectedControls.Add(obsoleteBtn);

        var noloadBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        noloadBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.NoLoad");
        noloadBtn.Tag = "STFormatter.Add.Attr.noload";
        TrySetIcon(noloadBtn, 1722);
        noloadBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "noload");
        instance.InjectedControls.Add(noloadBtn);

        var dynCreateBtn = (CommandBarButton)attrPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        dynCreateBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.EnableDynamicCreation");
        dynCreateBtn.Tag = "STFormatter.Add.Attr.enable_dynamic_creation";
        TrySetIcon(dynCreateBtn, 1722);
        dynCreateBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "enable_dynamic_creation");
        instance.InjectedControls.Add(dynCreateBtn);

        // -- I/O Linking --
        var ioLinkBtn = (CommandBarButton)addPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        ioLinkBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.IOLinking");
        ioLinkBtn.Tag = "STFormatter.Add.IOLinking";
        TrySetIcon(ioLinkBtn, 303);
        ioLinkBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddIOLinking(instance.Pid);
        instance.InjectedControls.Add(ioLinkBtn);

        // -- Region submenu --
        var regionPopup = (CommandBarPopup)addPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        regionPopup.Caption = STFormatter.UI.Strings.Get("AddMenu.Region");
        regionPopup.Tag = "STFormatter.Add.Region";
        instance.InjectedControls.Add(regionPopup);

        var startRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        startRegionBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.StartRegion");
        startRegionBtn.Tag = "STFormatter.Add.Region.Start";
        TrySetIcon(startRegionBtn, 309);
        startRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddRegion(instance.Pid);
        instance.InjectedControls.Add(startRegionBtn);

        var endRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        endRegionBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.EndRegion");
        endRegionBtn.Tag = "STFormatter.Add.Region.End";
        TrySetIcon(endRegionBtn, 309);
        endRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "{endregion}");
        instance.InjectedControls.Add(endRegionBtn);

        var startEndRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        startEndRegionBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.StartEndRegion");
        startEndRegionBtn.Tag = "STFormatter.Add.Region.StartEnd";
        TrySetIcon(startEndRegionBtn, 309);
        startEndRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddStartEndRegion(instance.Pid);
        instance.InjectedControls.Add(startEndRegionBtn);

        // -- Warning --
        var warningBtn = (CommandBarButton)addPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        warningBtn.Caption = STFormatter.UI.Strings.Get("AddMenu.Warning");
        warningBtn.Tag = "STFormatter.Add.Warning";
        TrySetIcon(warningBtn, 308);
        warningBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddWarning(instance.Pid);
        instance.InjectedControls.Add(warningBtn);

        Log($"AddPragmaSubmenu: PID {instance.Pid} added Add submenu");
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
