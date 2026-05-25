using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using Process = System.Diagnostics.Process;

namespace STFormatter.Discover;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var log = new DualLogger();
        int exitCode;

        try
        {
            exitCode = Run(args, log);
        }
        catch (Exception ex)
        {
            log.WriteError("Unhandled exception", ex);
            exitCode = 99;
        }
        finally
        {
            log.Dispose();
        }

        return exitCode;
    }

    private static int Run(string[] args, DualLogger log)
    {
        log.WriteSection($"STFormatter.Discover run @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        log.WriteLine($"Args: [{string.Join(", ", args)}]");
        log.WriteLine($"Log file: {log.LogPath}");

        // No args or --help: show usage
        if (args.Length > 0 && (
            string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "/?", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage(log);
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "--list", StringComparison.OrdinalIgnoreCase))
        {
            return ListDteInstances(log);
        }

        // Determine PID: from arg or auto-detect TcXaeShell
        int pid;
        if (args.Length > 0 && int.TryParse(args[0], out pid))
        {
            log.WriteLine($"Using PID from argument: {pid}");
        }
        else
        {
            log.WriteLine("No PID specified, auto-detecting TcXaeShell process...");
            pid = FindTcXaeShellPid(log);
            if (pid <= 0)
            {
                return 2;
            }
        }

        return AttachAndDump(pid, log);
    }

    private static void PrintUsage(DualLogger log)
    {
        log.WriteLine("STFormatter.Discover - TcXaeShell Context Menu Discovery Tool");
        log.WriteLine("");
        log.WriteLine("Usage:");
        log.WriteLine("  STFormatter.Discover.exe              Auto-detect TcXaeShell and attach");
        log.WriteLine("  STFormatter.Discover.exe <PID>        Attach to specific process by PID");
        log.WriteLine("  STFormatter.Discover.exe --list       List all DTE instances in the ROT");
        log.WriteLine("  STFormatter.Discover.exe --help      Show this help");
        log.WriteLine("");
        log.WriteLine("Steps:");
        log.WriteLine("  1. Start TcXaeShell and open a PLC project");
        log.WriteLine("  2. Click into the ST code editor (make it the active window)");
        log.WriteLine("  3. Run:       STFormatter.Discover.exe");
        log.WriteLine("  4. Check:     %TEMP%\\STFormatter_Discover.log");
    }

    private static int FindTcXaeShellPid(DualLogger log)
    {
        var processes = Process.GetProcessesByName("TcXaeShell");

        if (processes.Length == 0)
        {
            log.WriteError("No TcXaeShell process found. Is TcXaeShell running?");
            return -1;
        }

        if (processes.Length == 1)
        {
            int pid = processes[0].Id;
            log.WriteLine($"Found TcXaeShell process (PID {pid})");
            processes[0].Dispose();
            return pid;
        }

        log.WriteLine($"Found {processes.Length} TcXaeShell processes:");
        for (int i = 0; i < processes.Length; i++)
        {
            log.WriteLine($"  [{i}] PID={processes[i].Id}");
        }

        // Try to find the one with a DTE in the ROT
        var entries = DteAttacher.EnumerateAllDteInRot();
        foreach (var proc in processes)
        {
            foreach (var entry in entries)
            {
                if (entry.Pid == proc.Id && entry.Dte != null)
                {
                    log.WriteLine($"Auto-selected PID {proc.Id} (has DTE registered in ROT)");
                    proc.Dispose();
                    foreach (var p in processes) { if (p != proc) p.Dispose(); }
                    return proc.Id;
                }
            }
        }

        // Fallback: pick the first
        log.WriteLine($"Multiple TcXaeShell processes found, none in ROT. Using PID {processes[0].Id}.");
        int firstPid = processes[0].Id;
        foreach (var p in processes) p.Dispose();
        return firstPid;
    }

    private static int ListDteInstances(DualLogger log)
    {
        // Always dump ALL ROT entries first
        DteAttacher.DumpAllRotEntries(log);

        log.WriteSection("DTE-specific entries in ROT");

        var entries = DteAttacher.EnumerateAllDteInRot();

        if (entries.Count == 0)
        {
            log.WriteLine("  No DTE instances found in the Running Object Table.");
            log.WriteLine("  Is TcXaeShell running with a solution loaded?");
            return 2;
        }

        log.WriteLine($"  Found {entries.Count} DTE instance(s):");
        log.WriteLine("");

        foreach (var entry in entries)
        {
            string processName = "";
            bool processExists = false;
            try
            {
                var proc = Process.GetProcessById(entry.Pid);
                processName = proc.ProcessName;
                processExists = true;
                proc.Dispose();
            }
            catch { }

            string dteVersion = "";
            try
            {
                if (entry.Dte != null)
                {
                    dteVersion = entry.Dte.Version ?? "?";
                }
            }
            catch { }

            log.WriteLine($"  PID {entry.Pid}: {entry.DisplayName}");
            log.WriteLine($"    Process: '{processName}' {(processExists ? "(running)" : "(not found)")}");
            log.WriteLine($"    DTE Version: {dteVersion}");
            log.WriteLine($"    DTE attached: {(entry.Dte != null ? "yes" : "no")}");
            log.WriteLine("");
        }

        return 0;
    }

    private static int AttachAndDump(int pid, DualLogger log)
    {
        // Validate process
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            log.WriteError($"Process with PID {pid} not found.");
            return 2;
        }

        string processName = process.ProcessName;
        log.WriteLine($"Target PID: {pid}");
        log.WriteLine($"Target Process: {processName}.exe");

        try
        {
            log.WriteLine($"Target Path: {process.MainModule?.FileName ?? "(unable to determine)"}");
        }
        catch { }

        if (!string.Equals(processName, "TcXaeShell", StringComparison.OrdinalIgnoreCase))
        {
            log.WriteLine($"WARNING: Process '{processName}' is not TcXaeShell. Attempting anyway...");
        }
        process.Dispose();

        // Register COM message filter to handle retries
        OleMessageFilter.Register();

        try
        {
            // Always dump all ROT entries first (even if DTE attach fails)
            DteAttacher.DumpAllRotEntries(log);

            log.WriteLine($"Attaching to DTE via ROT (PID={pid})...");
            DTE? dte = DteAttacher.AttachByPid(pid);

            if (dte == null)
            {
                log.WriteError($"Failed to attach to DTE for PID {pid}.");
                log.WriteLine("");
                log.WriteLine("Possible causes:");
                log.WriteLine("  - TcXaeShell hasn't registered a DTE object in the ROT");
                log.WriteLine("  - TcXaeShell needs a solution loaded first");
                log.WriteLine("  - The PID doesn't belong to TcXaeShell");
                log.WriteLine("  - Bitness mismatch (this tool is x86, TcXaeShell is x86 - should match)");
                log.WriteLine("");
                log.WriteLine("Check the 'All ROT Entries' section above for any VisualStudio/TcXae entries.");
                log.WriteLine("If no DTE entries found, try opening a solution in TcXaeShell first.");
                return 3;
            }

            log.WriteLine("DTE attached successfully.");

            // DTE info
            log.WriteSection("DTE Info");
            try { log.WriteLine($"  Version:  {dte.Version}"); } catch (Exception ex) { log.WriteError("Version", ex); }
            try { log.WriteLine($"  Edition:  {dte.Edition}"); } catch (Exception ex) { log.WriteError("Edition", ex); }
            try { log.WriteLine($"  Name:     {dte.Name}"); } catch (Exception ex) { log.WriteError("Name", ex); }
            try
            {
                string sln = dte.Solution?.FullName ?? "(no solution)";
                log.WriteLine($"  Solution: {sln}");
            }
            catch (Exception ex) { log.WriteError("Solution", ex); }

            // Active window probe
            ActiveWindowProbe.Probe(dte, log);

            // VS Shell probes
            VsShellProbe.Probe(dte, log);

            // Live edit probes (DTE commands, TextSelection, UndoContext, VS services)
            LiveEditProbe.Probe(dte, log);

            // CommandBars dump
            CommandBarDump.DumpAll(dte, log);

            log.WriteSection("Discovery Complete");
            log.WriteLine($"Log saved to: {log.LogPath}");
            log.WriteLine("Review the 'Context Menu Candidates' section above for the PLC editor context menu name.");
            log.WriteLine("Update PriorityNames[] in ContextMenuInjector.cs with the correct name.");

            Marshal.ReleaseComObject(dte);
        }
        finally
        {
            OleMessageFilter.Revoke();
        }

        return 0;
    }
}

/// <summary>
/// COM message filter to handle RPC_E_SERVERCALL_RETRYLATER and RPC_E_CALL_REJECTED
/// errors when communicating with the VS DTE object.
/// </summary>
internal sealed class OleMessageFilter : IOleMessageFilter
{
    private static OleMessageFilter? _instance;

    public static void Register()
    {
        _instance = new OleMessageFilter();
        CoRegisterMessageFilter(_instance, out _);
    }

    public static void Revoke()
    {
        if (_instance != null)
        {
            CoRegisterMessageFilter(null, out _);
            _instance = null;
        }
    }

    public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
    {
        return 0; // SERVERCALL_ISHANDLED
    }

    public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        if (dwRejectType == 2) // SERVERCALL_RETRYLATER
        {
            if (dwTickCount < 5000) return 99;
        }
        return -1;
    }

    public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
    {
        return 1; // PENDINGMSG_WAITDEFPROCESS
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? lpMessageFilter, out IOleMessageFilter? lplpOldFilter);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000016-0000-0000-C000-000000000046")]
internal interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}