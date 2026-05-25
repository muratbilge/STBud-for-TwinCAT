using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;

namespace STFormatter.Discover;

internal sealed class DteEntry
{
    public int Pid { get; set; }
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public DTE? Dte { get; set; }
}

internal static class DteAttacher
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    public static List<DteEntry> EnumerateAllDteInRot()
    {
        var results = new List<DteEntry>();

        int hr = GetRunningObjectTable(0, out IRunningObjectTable rot);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        rot.EnumRunning(out IEnumMoniker enumMoniker);
        var monikers = new IMoniker[1];
        IntPtr fetched = IntPtr.Zero;

        CreateBindCtx(0, out IBindCtx bindCtx);

        while (enumMoniker.Next(1, monikers, fetched) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(bindCtx, null, out string displayName);

                var entry = new DteEntry
                {
                    DisplayName = displayName
                };

                // Try to parse PID from VisualStudio.DTE monikers
                if (displayName.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIdx = displayName.LastIndexOf(':');
                    if (colonIdx > 0 && int.TryParse(displayName.Substring(colonIdx + 1), out int pid))
                    {
                        entry.Pid = pid;
                        entry.Version = displayName.Substring(1, colonIdx - 1);
                    }
                }

                // Try to get the COM object for any entry
                try
                {
                    int hrObj = rot.GetObject(monikers[0], out object obj);
                    if (hrObj == 0 && obj != null)
                    {
                        if (obj is DTE dte)
                        {
                            entry.Dte = dte;
                        }
                        else
                        {
                            // Not a DTE, but still useful to know what's in the ROT
                            Marshal.ReleaseComObject(obj);
                        }
                    }
                }
                catch (Exception)
                {
                    // Can't access this object
                }

                results.Add(entry);
            }
            catch (Exception)
            {
            }
            finally
            {
                Marshal.ReleaseComObject(monikers[0]);
            }
        }

        Marshal.ReleaseComObject(enumMoniker);
        Marshal.ReleaseComObject(bindCtx);
        Marshal.ReleaseComObject(rot);

        return results;
    }

    /// <summary>
    /// Dump all ROT entries to the log (not just VisualStudio ones)
    /// </summary>
    public static void DumpAllRotEntries(DualLogger log)
    {
        log.WriteSection("All ROT Entries");

        int hr = GetRunningObjectTable(0, out IRunningObjectTable rot);
        if (hr != 0)
        {
            log.WriteError($"Failed to get ROT, hr=0x{hr:X8}");
            return;
        }

        rot.EnumRunning(out IEnumMoniker enumMoniker);
        var monikers = new IMoniker[1];
        IntPtr fetched = IntPtr.Zero;

        CreateBindCtx(0, out IBindCtx bindCtx);

        int idx = 0;
        int dteCount = 0;
        int vsCount = 0;

        while (enumMoniker.Next(1, monikers, fetched) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(bindCtx, null, out string displayName);
                idx++;

                string typeName = "<not accessible>";
                bool isDte = false;

                try
                {
                    int hrObj = rot.GetObject(monikers[0], out object obj);
                    if (hrObj == 0 && obj != null)
                    {
                        typeName = obj.GetType().FullName ?? "<unknown>";
                        isDte = obj is DTE;

                        if (isDte) dteCount++;
                        if (displayName.IndexOf("VisualStudio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            displayName.IndexOf("TcXae", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            displayName.IndexOf("Beckhoff", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            vsCount++;
                        }

                        Marshal.ReleaseComObject(obj);
                    }
                    else
                    {
                        typeName = $"(hr=0x{hrObj:X8})";
                    }
                }
                catch (Exception ex)
                {
                    typeName = $"({ex.GetType().Name}: {ex.Message})";
                }

                // Mark interesting entries
                string marker = "";
                if (isDte) marker = " *** DTE ***";
                else if (displayName.IndexOf("VisualStudio", StringComparison.OrdinalIgnoreCase) >= 0) marker = " *** VS ***";
                else if (displayName.IndexOf("TcXae", StringComparison.OrdinalIgnoreCase) >= 0) marker = " *** TcXae ***";
                else if (displayName.IndexOf("Beckhoff", StringComparison.OrdinalIgnoreCase) >= 0) marker = " *** Beckhoff ***";

                log.WriteLine($"  [{idx:D3}] {displayName}{marker}");
                if (isDte || !string.IsNullOrEmpty(marker))
                {
                    log.WriteLine($"        Type: {typeName}");
                }
            }
            catch (Exception ex)
            {
                idx++;
                log.WriteLine($"  [{idx:D3}] <error: {ex.Message}>");
            }
            finally
            {
                Marshal.ReleaseComObject(monikers[0]);
            }
        }

        log.WriteLine($"  --- Total: {idx} entries, {dteCount} DTE objects, {vsCount} VS/TcXae entries ---");

        Marshal.ReleaseComObject(enumMoniker);
        Marshal.ReleaseComObject(bindCtx);
        Marshal.ReleaseComObject(rot);
    }

    public static DTE? AttachByPid(int pid)
    {
        // Strategy 1: Find in ROT by PID
        var entries = EnumerateAllDteInRot();

        foreach (var entry in entries)
        {
            if (entry.Pid == pid && entry.Dte != null)
            {
                return entry.Dte;
            }
        }

        // Strategy 2: Try Marshal.GetActiveObject with various version strings
        string[] dteNames = {
            $"!VisualStudio.DTE.15.0:{pid}",
            $"VisualStudio.DTE.15.0:{pid}",
            "VisualStudio.DTE.15.0",
            $"VisualStudio.DTE.15.1:{pid}",
            "VisualStudio.DTE.15.1",
        };

        foreach (string dteName in dteNames)
        {
            try
            {
                var dte = Marshal.GetActiveObject(dteName) as DTE;
                if (dte != null)
                {
                    return dte;
                }
            }
            catch
            {
                // Not found or wrong version
            }
        }

        // Strategy 3: Check if any DTE entry matches PID regardless of moniker format
        foreach (var entry in entries)
        {
            if (entry.Dte != null)
            {
                try
                {
                    var window = entry.Dte.MainWindow;
                    if (window != null)
                    {
                        int hwndVal = window.HWnd.ToInt32();
                        if (hwndVal != 0)
                        {
                            uint processId = 0;
                            GetWindowThreadProcessId(new IntPtr(hwndVal), out processId);
                            if ((int)processId == pid)
                            {
                                return entry.Dte;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        return null;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}