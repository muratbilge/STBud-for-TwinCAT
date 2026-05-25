using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace STFormatter.TcXaeShell;

using Package = Microsoft.VisualStudio.Shell.Package;

[PackageRegistration(UseManagedResourcesOnly = true)]
[Guid(STFormatterPackage.PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideBindingPath]
[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
public sealed class STFormatterPackage : Package, IVsSolutionEvents
{
    public const string PackageGuidString = "b1c2d3e4-f5a6-7890-abcd-ef1234567890";

    private uint _solutionEventsCookie;
    private bool _injected;
    private readonly List<CommandBarControl> _injectedControls = new();

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "STFormatter_TcXaeShell.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] Pkg: {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void Initialize()
    {
        Log("Initialize: START");
        base.Initialize();

        // Subscribe to solution events to detect when projects load
        var solution = GetService(typeof(SVsSolution)) as IVsSolution;
        if (solution != null)
        {
            solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
            Log("Initialize: Subscribed to solution events");
        }

        // Try to inject context menu buttons
        TryInjectContextMenu();
        Log("Initialize: END");
    }

    private void TryInjectContextMenu()
    {
        if (_injected) return;

        try
        {
            var dte = GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte?.CommandBars == null)
            {
                Log("TryInject: DTE not available yet");
                return;
            }

            var commandBars = (CommandBars)dte.CommandBars;
            InjectIntoMenu(commandBars, "PlcCodeWinContextMenu");
            InjectIntoMenu(commandBars, "Code Window");

            if (_injectedControls.Count > 0)
            {
                _injected = true;
                Log("TryInject: Context menu buttons injected");
            }
        }
        catch (Exception ex)
        {
            Log($"TryInject FAILED: {ex.Message}");
        }
    }

    private void InjectIntoMenu(CommandBars commandBars, string menuName)
    {
        try
        {
            CommandBar? cb = commandBars[menuName];
            if (cb == null || cb.Type != MsoBarType.msoBarTypePopup) return;

            // Remove any stale STFormatter buttons (from previous loads)
            for (int i = cb.Controls.Count; i >= 1; i--)
            {
                try
                {
                    var ctrl = cb.Controls[i];
                    if (ctrl is CommandBarButton btn)
                    {
                        string? tag = btn.Tag as string;
                        if (!string.IsNullOrEmpty(tag) && tag.StartsWith("STFormatter.", StringComparison.OrdinalIgnoreCase))
                            btn.Delete(false);
                    }
                }
                catch { }
            }

            // Separator
            var sep = (CommandBarControl)cb.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            sep.BeginGroup = true;
            sep.Caption = "-";
            sep.Visible = false;
            _injectedControls.Add(sep);

            // Format Document
            var docBtn = (CommandBarButton)cb.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            docBtn.Caption = "Format ST Document";
            docBtn.TooltipText = "Format the entire ST document";
            docBtn.Tag = "STFormatter.FormatDocument";
            docBtn.Click += OnFormatDocument;
            _injectedControls.Add(docBtn);

            // Format Selection
            var selBtn = (CommandBarButton)cb.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            selBtn.Caption = "Format ST Selection";
            selBtn.TooltipText = "Format the selected ST code";
            selBtn.Tag = "STFormatter.FormatSelection";
            selBtn.Click += OnFormatSelection;
            _injectedControls.Add(selBtn);

            Log($"InjectIntoMenu: +2 buttons to '{cb.Name}'");
        }
        catch (Exception ex)
        {
            Log($"InjectIntoMenu FAILED for '{menuName}': {ex.Message}");
        }
    }

    private void OnFormatDocument(CommandBarButton ctrl, ref bool cancelDefault)
    {
        Log("OnFormatDocument: invoked");
        try { FormatHelper.FormatDocument(); }
        catch (Exception ex) { Log($"OnFormatDocument FAILED: {ex.Message}"); }
    }

    private void OnFormatSelection(CommandBarButton ctrl, ref bool cancelDefault)
    {
        Log("OnFormatSelection: invoked");
        try { FormatHelper.FormatSelection(); }
        catch (Exception ex) { Log($"OnFormatSelection FAILED: {ex.Message}"); }
    }

    // IVsSolutionEvents — retry injection when projects load
    public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
    {
        TryInjectContextMenu();
        return VSConstants.S_OK;
    }

    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;
    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
    {
        TryInjectContextMenu();
        return VSConstants.S_OK;
    }
    public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
    public int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                var solution = GetService(typeof(SVsSolution)) as IVsSolution;
                if (solution != null && _solutionEventsCookie != 0)
                    solution.UnadviseSolutionEvents(_solutionEventsCookie);

                foreach (var ctrl in _injectedControls)
                    try { ctrl.Delete(false); } catch { }
                _injectedControls.Clear();
            }
            catch { }
        }
        base.Dispose(disposing);
    }
}
