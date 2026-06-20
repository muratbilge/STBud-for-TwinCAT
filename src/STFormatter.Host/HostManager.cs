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
                            if (hrObj == 0 && obj is DTE dte && IsTwinCatEngineering(dte))
                            {
                                result = (pid, dte, detectedProfile);
                                Log($"FindNewTcXaeShell: Found PID {pid} profile={detectedProfile}");
                                break;
                            }
                            else if (obj != null)
                            {
                                Marshal.ReleaseComObject(obj);
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
            // Scan every shell variant (32-bit TcXaeShell and 4026 TcXaeShell64).
            var pids = new List<string>();
            foreach (var name in TcXaeShellVersionProfile.ShellProcessNames)
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    using (p) pids.Add($"{p.Id}");
                }
            }
            processCount = pids.Count;
            if (pids.Count > 0)
                processPids = string.Join(", ", pids);
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

    // A DTE is a TwinCAT engineering environment if it is the standalone shell
    // (name contains "TcXae") OR any Visual Studio / devenv with TwinCAT loaded.
    // The latter is detected by the Beckhoff PLC editor command bar, which plain
    // Visual Studio does not have - so we connect to TwinCAT-in-VS2022 without
    // injecting into a regular VS doing unrelated work.
    private static bool IsTwinCatEngineering(DTE dte)
    {
        try
        {
            string name = dte.Name ?? "";
            if (name.IndexOf("TcXae", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return HasPlcContextMenu(dte);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPlcContextMenu(DTE dte)
    {
        try
        {
            var bars = dte.CommandBars as CommandBars;
            if (bars == null) return false;
            // Indexer throws if the bar is absent (plain VS) -> caught below.
            return bars["PlcCodeWinContextMenu"] != null;
        }
        catch
        {
            return false;
        }
    }

    // Matches both current ("STBud.*") and legacy ("STFormatter.*") tags so a
    // crashed Host's leftover controls are removed before re-injection.
    private static bool IsOurTag(string? tag)
    {
        return !string.IsNullOrEmpty(tag) &&
               (tag!.StartsWith("STBud.", StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith("STFormatter.", StringComparison.OrdinalIgnoreCase));
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
                    if (IsOurTag(tag))
                    {
                        btn.Delete(false);
                        removed++;
                    }
                }
                else if (ctrl is CommandBarPopup popup)
                {
                    string? tag = popup.Tag as string;
                    if (IsOurTag(tag))
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

            var mainPopup = (CommandBarPopup)targetMenu.Controls.Add(
                MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
            mainPopup.Caption = "STBud for TwinCAT";
            mainPopup.Tag = "STBud.MainMenu";
            mainPopup.TooltipText = "STBud for TwinCAT tools";
            instance.InjectedControls.Add(mainPopup);

            // Common actions surfaced to the top level (one click): the two
            // format commands, then I/O Linking. Long-tail pragma helpers stay
            // nested in the Add submenus below.
            AddFormatButtons(instance, mainPopup);
            AddIoLinkingButton(instance, mainPopup, beginGroup: true);

            var attrPopupGroup = AddAttributeSubmenu(instance, mainPopup);
            try { attrPopupGroup.BeginGroup = true; } catch { }
            AddTaskSubmenu(instance, mainPopup);
            AddRegionSubmenu(instance, mainPopup);

            var warningBtn = (CommandBarButton)mainPopup.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            warningBtn.Caption = "Warning...";
            warningBtn.Tag = "STBud.Add.Warning";
            TrySetIcon(warningBtn, 308);
            warningBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.HandleAddWarning(instance.Pid);
            instance.InjectedControls.Add(warningBtn);

            var settingsBtn = (CommandBarButton)mainPopup.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            settingsBtn.Caption = "Settings...";
            settingsBtn.Tag = "STBud.OpenSettings";
            settingsBtn.BeginGroup = true;
            TrySetIcon(settingsBtn, 277);
            settingsBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
                Program.ShowSettingsGui();
            instance.InjectedControls.Add(settingsBtn);

            Log($"AddButtons: PID {instance.Pid} +buttons to '{targetMenu.Name}'");
        }
        catch (Exception ex)
        {
            Log($"AddButtons: PID {instance.Pid} FAILED for '{targetMenu.Name}': {ex.Message}");
        }
    }

    // Format Document / Selection added directly to the main popup (top level)
    // so the most-used commands are one click away.
    private void AddFormatButtons(TcXaeInstance instance, CommandBarPopup parent)
    {
        var docBtn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        docBtn.Caption = "Format Document";
        docBtn.TooltipText = "Format the entire ST document (Ctrl+Shift+F)";
        docBtn.Tag = "STBud.FormatDocument";
        docBtn.ShortcutText = "Ctrl+Shift+F";
        TrySetIcon(docBtn, 58);
        docBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleFormatDocument(instance.Pid);
        instance.InjectedControls.Add(docBtn);

        var selBtn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        selBtn.Caption = "Format Selection";
        selBtn.TooltipText = "Format the selected ST code (Ctrl+Shift+D)";
        selBtn.Tag = "STBud.FormatSelection";
        selBtn.ShortcutText = "Ctrl+Shift+D";
        TrySetIcon(selBtn, 593);
        selBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleFormatSelection(instance.Pid);
        instance.InjectedControls.Add(selBtn);
    }

    // I/O Linking opens the I/O tree browser. Surfaced to the top level (was
    // buried under Add Attribute > Binding); also reusable from any parent.
    private void AddIoLinkingButton(TcXaeInstance instance, CommandBarPopup parent, bool beginGroup = false)
    {
        var ioLinkBtn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        ioLinkBtn.Caption = "I/O Linking...";
        ioLinkBtn.Tag = "STBud.Add.IOLinking";
        if (beginGroup) ioLinkBtn.BeginGroup = true;
        TrySetIcon(ioLinkBtn, 303);
        ioLinkBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddIOLinking(instance.Pid);
        instance.InjectedControls.Add(ioLinkBtn);
    }

    private CommandBarPopup AddAttributeSubmenu(TcXaeInstance instance, CommandBarPopup parent)
    {
        var attrPopup = (CommandBarPopup)parent.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        attrPopup.Caption = "Add Attribute";
        attrPopup.Tag = "STBud.Add.Attribute";
        instance.InjectedControls.Add(attrPopup);

        // -- Visibility --
        var visibilityPopup = (CommandBarPopup)attrPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        visibilityPopup.Caption = "Visibility";
        visibilityPopup.Tag = "STBud.Add.Attribute.Visibility";
        instance.InjectedControls.Add(visibilityPopup);

        AddPragmaButton(instance, visibilityPopup, "qualified_only", "qualified_only");
        AddPragmaButton(instance, visibilityPopup, "hide", "hide");
        AddPragmaButtonWithPrompt(instance, visibilityPopup, "no_explicit_call...", "no_explicit_call",
            "Enter call restriction message:", "no_explicit_call");

        // -- Binding --
        var bindingPopup = (CommandBarPopup)attrPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        bindingPopup.Caption = "Binding";
        bindingPopup.Tag = "STBud.Add.Attribute.Binding";
        instance.InjectedControls.Add(bindingPopup);

        AddPragmaButton(instance, bindingPopup, "linkalways", "linkalways");
        // I/O Linking... is now a top-level menu item (see AddIoLinkingButton).

        // -- Monitoring --
        var monitoringPopup = (CommandBarPopup)attrPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        monitoringPopup.Caption = "Monitoring";
        monitoringPopup.Tag = "STBud.Add.Attribute.Monitoring";
        instance.InjectedControls.Add(monitoringPopup);

        AddPragmaButton(instance, monitoringPopup, "monitoring := 'call'", "{attribute 'monitoring' := 'call'}", "monitoring");
        AddPragmaButton(instance, monitoringPopup, "TcRpcEnable := '1'", "{attribute 'TcRpcEnable' := '1'}", "TcRpcEnable");

        // -- OPC UA --
        var opcUaPopup = (CommandBarPopup)attrPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        opcUaPopup.Caption = "OPC UA";
        opcUaPopup.Tag = "STBud.Add.Attribute.OpcUa";
        instance.InjectedControls.Add(opcUaPopup);

        AddOpcUaDaButton(instance, opcUaPopup);
        AddOpcUaPropertyButton(instance, opcUaPopup);

        // -- Code Generation --
        var codeGenPopup = (CommandBarPopup)attrPopup.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        codeGenPopup.Caption = "Code Generation";
        codeGenPopup.Tag = "STBud.Add.Attribute.CodeGeneration";
        instance.InjectedControls.Add(codeGenPopup);

        AddPragmaButton(instance, codeGenPopup, "strict", "strict");
        AddPragmaButton(instance, codeGenPopup, "to_string", "to_string");
        AddPragmaButton(instance, codeGenPopup, "no-analysis", "no_analysis");
        AddPragmaButton(instance, codeGenPopup, "const_non_replaced", "const_non_replaced");
        AddPragmaButton(instance, codeGenPopup, "TcGenerated", "TcGenerated");
        AddPragmaButton(instance, codeGenPopup, "enable_dynamic_creation", "enable_dynamic_creation");
        AddPragmaButtonWithPrompt(instance, codeGenPopup, "always_average...", "always_average",
            "Enter variable name for averaging:", "always_average");
        AddPragmaButton(instance, codeGenPopup, "noload", "noload");
        AddPragmaButtonWithPrompt(instance, codeGenPopup, "obsolete...", "obsolete",
            "Enter deprecation message:", "obsolete");
        AddPragmaButton(instance, codeGenPopup, "no_check", "no_check");

        return attrPopup;
    }

    private void AddPragmaButton(TcXaeInstance instance, CommandBarPopup parent, string caption, string pragmaText, string? tagSuffix = null)
    {
        var btn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        btn.Caption = caption;
        btn.Tag = "STBud.Add.Attr." + (tagSuffix ?? pragmaText);
        TrySetIcon(btn, 1722);
        var capturedText = pragmaText;
        btn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, capturedText);
        instance.InjectedControls.Add(btn);
    }

    private void AddPragmaButtonWithPrompt(TcXaeInstance instance, CommandBarPopup parent, string caption, string pragmaKind, string prompt, string title)
    {
        var btn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        btn.Caption = caption;
        btn.Tag = "STBud.Add.Attr." + pragmaKind;
        TrySetIcon(btn, 1722);
        btn.Click += (CommandBarButton ctrl, ref bool cancel) =>
        {
            switch (pragmaKind)
            {
                case "no_explicit_call":
                    Program.HandleAddNoExplicitCall(instance.Pid);
                    break;
                case "always_average":
                    Program.HandleAddAlwaysAverage(instance.Pid);
                    break;
                case "obsolete":
                    Program.HandleAddObsolete(instance.Pid);
                    break;
            }
        };
        instance.InjectedControls.Add(btn);
    }

    private void AddOpcUaDaButton(TcXaeInstance instance, CommandBarPopup parent)
    {
        var btn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        btn.Caption = "OPC.UA.DA...";
        btn.Tag = "STBud.Add.Attr.OpcUaDa";
        TrySetIcon(btn, 1722);
        btn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddOpcUaDa(instance.Pid);
        instance.InjectedControls.Add(btn);
    }

    private void AddOpcUaPropertyButton(TcXaeInstance instance, CommandBarPopup parent)
    {
        var btn = (CommandBarButton)parent.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        btn.Caption = "OPC.UA.DA.Property := '1'";
        btn.Tag = "STBud.Add.Attr.OpcUaDaProperty";
        TrySetIcon(btn, 1722);
        btn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, "{attribute 'OPC.UA.DA.Property' := '1'}");
        instance.InjectedControls.Add(btn);
    }

    private void AddTaskSubmenu(TcXaeInstance instance, CommandBarPopup parent)
    {
        var taskPopup = (CommandBarPopup)parent.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        taskPopup.Caption = "Add Task Attribute";
        taskPopup.Tag = "STBud.Add.Task";
        instance.InjectedControls.Add(taskPopup);

        var taskNameBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        taskNameBtn.Caption = "Task Name...";
        taskNameBtn.Tag = "STBud.Add.Task.TaskName";
        TrySetIcon(taskNameBtn, 1722);
        taskNameBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddTaskName(instance.Pid);
        instance.InjectedControls.Add(taskNameBtn);

        AddPragmaButton(instance, taskPopup, "call_always", "call_always");

        var callAfterBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callAfterBtn.Caption = "call_after...";
        callAfterBtn.Tag = "STBud.Add.Task.CallAfter";
        TrySetIcon(callAfterBtn, 1722);
        callAfterBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallAfter(instance.Pid);
        instance.InjectedControls.Add(callAfterBtn);

        var callBeforeBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callBeforeBtn.Caption = "call_before...";
        callBeforeBtn.Tag = "STBud.Add.Task.CallBefore";
        TrySetIcon(callBeforeBtn, 1722);
        callBeforeBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallBefore(instance.Pid);
        instance.InjectedControls.Add(callBeforeBtn);

        var callAfterInitBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callAfterInitBtn.Caption = "call_after_init...";
        callAfterInitBtn.Tag = "STBud.Add.Task.CallAfterInit";
        TrySetIcon(callAfterInitBtn, 1722);
        callAfterInitBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallAfterInit(instance.Pid);
        instance.InjectedControls.Add(callAfterInitBtn);

        var callBeforeInitBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callBeforeInitBtn.Caption = "call_before_init...";
        callBeforeInitBtn.Tag = "STBud.Add.Task.CallBeforeInit";
        TrySetIcon(callBeforeInitBtn, 1722);
        callBeforeInitBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallBeforeInit(instance.Pid);
        instance.InjectedControls.Add(callBeforeInitBtn);

        var callAfterExitBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callAfterExitBtn.Caption = "call_after_exit...";
        callAfterExitBtn.Tag = "STBud.Add.Task.CallAfterExit";
        TrySetIcon(callAfterExitBtn, 1722);
        callAfterExitBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallAfterExit(instance.Pid);
        instance.InjectedControls.Add(callAfterExitBtn);

        var callBeforeExitBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        callBeforeExitBtn.Caption = "call_before_exit...";
        callBeforeExitBtn.Tag = "STBud.Add.Task.CallBeforeExit";
        TrySetIcon(callBeforeExitBtn, 1722);
        callBeforeExitBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddCallBeforeExit(instance.Pid);
        instance.InjectedControls.Add(callBeforeExitBtn);

        var priorityBtn = (CommandBarButton)taskPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        priorityBtn.Caption = "priority...";
        priorityBtn.Tag = "STBud.Add.Task.Priority";
        TrySetIcon(priorityBtn, 1722);
        priorityBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPriority(instance.Pid);
        instance.InjectedControls.Add(priorityBtn);

        AddPragmaButton(instance, taskPopup, "no_check", "no_check");
    }

    private void AddRegionSubmenu(TcXaeInstance instance, CommandBarPopup parent)
    {
        var regionPopup = (CommandBarPopup)parent.Controls.Add(
            MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
        regionPopup.Caption = "Add Region";
        regionPopup.Tag = "STBud.Add.Region";
        instance.InjectedControls.Add(regionPopup);

        var startRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        startRegionBtn.Caption = "Start Region...";
        startRegionBtn.Tag = "STBud.Add.Region.Start";
        TrySetIcon(startRegionBtn, 309);
        startRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddRegion(instance.Pid);
        instance.InjectedControls.Add(startRegionBtn);

        var endRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        endRegionBtn.Caption = "End Region";
        endRegionBtn.Tag = "STBud.Add.Region.End";
        TrySetIcon(endRegionBtn, 309);
        endRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddPragma(instance.Pid, STFormatter.Core.Toolbox.PragmaTemplates.EndRegion);
        instance.InjectedControls.Add(endRegionBtn);

        var startEndRegionBtn = (CommandBarButton)regionPopup.Controls.Add(
            MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
        startEndRegionBtn.Caption = "Start + End Region...";
        startEndRegionBtn.Tag = "STBud.Add.Region.StartEnd";
        TrySetIcon(startEndRegionBtn, 309);
        startEndRegionBtn.Click += (CommandBarButton ctrl, ref bool cancel) =>
            Program.HandleAddStartEndRegion(instance.Pid);
        instance.InjectedControls.Add(startEndRegionBtn);
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
