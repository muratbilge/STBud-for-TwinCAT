using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;

namespace STFormatter.Discover;

internal static class CommandBarDump
{
    private static readonly string[] StandardMenus =
    {
        "Menu Bar", "Standard", "Build", "Debug", "Edit", "File",
        "Format", "Help", "Insert", "Project", "Refactor", "Tools",
        "View", "Window", "Image", "Layout", "Table", "Text Editor",
        "Query Designer", "View Designer", "Diagram", "Database Diagram",
        "Class Designer", "Watch", "Autos", "Locals", "Call Stack",
        "Threads", "Memory", "Disassembly", "Registers", "Error List",
        "Output", "Task List", "Solution Explorer", "Server Explorer",
        "Toolbox", "Properties", "Find Results 1", "Find Results 2",
        "Find Symbol Results", "Bookmark Window", "CSS", "HTML Source Editing",
        "XML", "Data", "Smart Tag", "Inline", "Add Reference",
    };

    private static readonly string[] ContextKeywords =
    {
        "Code", "Window", "Editor", "IEC", "PLC", "ST ",
        "Beckhoff", "TwinCAT", "Pou", "POU", "Variable",
        "Declaration", "Implementation", "Body", "Statement",
        "Context", "Popup", "RightClick", "Shortcut",
    };

    public static void DumpAll(DTE dte, DualLogger log)
    {
        CommandBars? commandBars;
        try
        {
            commandBars = dte.CommandBars as CommandBars;
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get CommandBars from DTE", ex);
            return;
        }

        if (commandBars == null)
        {
            log.WriteError("CommandBars is null");
            return;
        }

        int count;
        try
        {
            count = commandBars.Count;
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get CommandBars.Count", ex);
            return;
        }

        log.WriteSection($"All CommandBars (Count={count})");

        var popups = new List<CommandBar>();
        var candidates = new List<CommandBar>();

        for (int i = 1; i <= count; i++)
        {
            CommandBar cb;
            try
            {
                cb = commandBars[i];
            }
            catch (Exception ex)
            {
                log.WriteLine($"  [{i:D3}] <error accessing: {ex.Message}>");
                continue;
            }

            if (cb == null)
            {
                log.WriteLine($"  [{i:D3}] <null>");
                continue;
            }

            string name = "";
            MsoBarType type = MsoBarType.msoBarTypeNormal;
            bool visible = false;
            int ctrlCount = 0;

            try { name = cb.Name ?? ""; } catch { }
            try { type = cb.Type; } catch { }
            try { visible = cb.Visible; } catch { }
            try { ctrlCount = cb.Controls.Count; } catch { }

            log.WriteLine($"  [{i:D3}] Name='{name}', Type={type}, Visible={visible}, Controls={ctrlCount}");

            if (type == MsoBarType.msoBarTypePopup)
            {
                popups.Add(cb);

                if (IsContextCandidate(name, ctrlCount))
                {
                    candidates.Add(cb);
                }
            }
        }

        log.WriteSection($"Popup-Only Bars ({popups.Count} popups, sorted by name)");
        popups.Sort((a, b) =>
        {
            try { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); }
            catch { return 0; }
        });

        foreach (var cb in popups)
        {
            string name = "";
            int ctrlCount = 0;
            bool visible = false;
            try { name = cb.Name ?? ""; } catch { }
            try { ctrlCount = cb.Controls.Count; } catch { }
            try { visible = cb.Visible; } catch { }

            log.WriteLine($"  Name='{name}', Visible={visible}, Controls={ctrlCount}");
            DumpControls(cb.Controls, "    ", log);
        }

        log.WriteSection($"Context Menu Candidates ({candidates.Count} matched)");
        foreach (var cb in candidates)
        {
            string name = "";
            int ctrlCount = 0;
            bool visible = false;
            try { name = cb.Name ?? ""; } catch { }
            try { ctrlCount = cb.Controls.Count; } catch { }
            try { visible = cb.Visible; } catch { }

            log.WriteLine($"  Name='{name}', Visible={visible}, Controls={ctrlCount}");
            DumpControls(cb.Controls, "    ", log);
        }

        Marshal.ReleaseComObject(commandBars);
    }

    private static bool IsContextCandidate(string name, int ctrlCount)
    {
        if (string.IsNullOrEmpty(name)) return false;

        // Skip well-known standard menus
        foreach (var std in StandardMenus)
        {
            if (string.Equals(name, std, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Must be a popup with a reasonable number of controls
        if (ctrlCount <= 0 || ctrlCount > 50) return false;

        // Check for context-related keywords
        foreach (var kw in ContextKeywords)
        {
            if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // Also include popups with few controls that aren't standard
        if (ctrlCount <= 15 && !StandardMenus.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static void DumpControls(Microsoft.VisualStudio.CommandBars.CommandBarControls controls, string indent, DualLogger log)
    {
        if (controls == null) return;

        int count;
        try { count = controls.Count; } catch { return; }

        for (int i = 1; i <= Math.Min(count, 50); i++)
        {
            CommandBarControl ctrl;
            try { ctrl = controls[i]; } catch { continue; }
            if (ctrl == null) continue;

            string caption = "";
            string ctrlType = "";
            int id = 0;
            bool beginGroup = false;
            string tag = "";

            try { caption = ctrl.Caption ?? ""; } catch { }
            try { ctrlType = ctrl.Type.ToString(); } catch { }
            try { id = ctrl.Id; } catch { }
            try { beginGroup = ctrl.BeginGroup; } catch { }
            try { tag = ctrl.Tag ?? ""; } catch { }

            string groupMarker = beginGroup ? " [BG]" : "";
            log.WriteLine($"{indent}[{i}] Caption='{caption}', Type={ctrlType}, Id={id}, Tag='{tag}'{groupMarker}");

            // Recurse into popup controls
            if (ctrl is CommandBarPopup popup)
            {
                try
                {
                    DumpControls(popup.Controls, indent + "  ", log);
                }
                catch { }

                Marshal.ReleaseComObject(popup);
            }

            Marshal.ReleaseComObject(ctrl);
        }
    }
}