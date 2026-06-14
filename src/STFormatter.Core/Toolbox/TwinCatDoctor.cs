using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using STFormatter.Core.Configuration;

namespace STFormatter.Core.Toolbox;

/// <summary>
/// Environment diagnostics for TcXaeShell / TwinCAT, designed primarily as the
/// recon tool for new-version upgrades (e.g. Build 4026): run it on the current
/// install to capture a baseline, run it again after upgrading and diff. It
/// reports detected installs, running shells with their exact ROT monikers,
/// the deployed Host, and a local ADS check - everything the Host's connection
/// path depends on. Pure filesystem + COM ROT + process inspection; no registry
/// dependency, no extra packages, Windows-guarded.
/// </summary>
public static class TwinCatDoctor
{
    public static string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== STBud TwinCAT Doctor ===");
        sb.AppendLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"STBud Core: {typeof(TwinCatDoctor).Assembly.GetName().Version}");
        sb.AppendLine($"OS        : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine();

        AppendTwinCatInstall(sb);
        sb.AppendLine();
        AppendShellInstalls(sb);
        sb.AppendLine();
        AppendRunningShells(sb);
        sb.AppendLine();
        AppendDeployedHost(sb);
        sb.AppendLine();
        AppendAdsCheck(sb);

        return sb.ToString();
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static void AppendTwinCatInstall(StringBuilder sb)
    {
        sb.AppendLine("-- TwinCAT runtime --");
        var tcRoot = @"C:\TwinCAT\3.1";
        if (Directory.Exists(tcRoot))
        {
            sb.AppendLine($"  Install   : {tcRoot}");
            // The build (e.g. 3.1.4024.75 or 3.1.4026.x) is in the ProductVersion
            // of the runtime binaries - the System Service is the stable probe.
            var sys = Path.Combine(tcRoot, "System");
            var build = ProductVersion(Path.Combine(sys, "TCATSysSrv.exe"))
                     ?? ProductVersion(Path.Combine(sys, "TcAmsRemoteMgr.exe"))
                     ?? ProductVersion(Path.Combine(sys, "TcRteInstall.exe"));
            sb.AppendLine($"  Build     : {build ?? "unknown"}");
        }
        else
        {
            sb.AppendLine("  Install   : C:\\TwinCAT\\3.1 not found");
        }

        // TwinCAT Package Manager (tcpkg) is the Build 4026 installation model;
        // its presence is the clearest "this is 4026+" signal.
        var tcpkg = FindOnPathOrKnown("tcpkg.exe", new[]
        {
            @"C:\Program Files\Beckhoff\TcPkg\tcpkg.exe",
            @"C:\Program Files (x86)\Beckhoff\TcPkg\tcpkg.exe",
        });
        sb.AppendLine(tcpkg != null
            ? $"  TcPkg     : present ({tcpkg}) -> Build 4026+ installation model"
            : "  TcPkg     : not present -> classic (<=4024) installation model");
    }

    private static void AppendShellInstalls(StringBuilder sb)
    {
        sb.AppendLine("-- XAE shell installs --");
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe",
            @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe",
            @"C:\Program Files\Beckhoff\TcXaeShell64\Common7\IDE\TcXaeShell64.exe",
            @"C:\Program Files (x86)\Beckhoff\TwinCAT XAE Shell\Common7\IDE\TcXaeShell.exe",
        };
        bool any = false;
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                sb.AppendLine($"  {Path.GetFileName(path),-16} {FileVersion(path) ?? "",-18} {path}");
                any = true;
            }
        }
        if (!any) sb.AppendLine("  (no TcXaeShell install found at known paths)");
    }

    private static void AppendRunningShells(StringBuilder sb)
    {
        sb.AppendLine("-- Running shells & ROT monikers --");

        var shellPids = new HashSet<int>();
        foreach (var name in TcXaeShellVersionProfile.ShellProcessNames)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        shellPids.Add(p.Id);
                        sb.AppendLine($"  process : {name} PID {p.Id}");
                    }
                }
            }
            catch { }
        }
        if (shellPids.Count == 0)
            sb.AppendLine("  process : (no TcXaeShell process running)");

        if (!IsWindows)
        {
            sb.AppendLine("  ROT     : (not Windows - skipped)");
            return;
        }

        var monikers = EnumerateDteMonikers();
        if (monikers.Count == 0)
        {
            sb.AppendLine("  ROT     : (no DTE monikers in the Running Object Table)");
            return;
        }

        foreach (var moniker in monikers)
        {
            var profile = TcXaeShellVersionProfile.DetectFromRotMoniker(moniker);
            string verdict = profile == null
                ? "not a TcXaeShell DTE moniker"
                : (Array.IndexOf(TcXaeShellVersionProfile.AllProfiles, profile) >= 0
                    ? $"SUPPORTED ({profile})"
                    : $"forward-compat fallback ({profile}) - menus/extensions assumed stable");
            sb.AppendLine($"  ROT     : {moniker}  ->  {verdict}");
        }
    }

    private static void AppendDeployedHost(StringBuilder sb)
    {
        sb.AppendLine("-- Deployed Host (C:\\Program Files (x86)\\STBud) --");
        var dir = @"C:\Program Files (x86)\STBud";
        if (!Directory.Exists(dir))
        {
            sb.AppendLine("  (not deployed)");
            return;
        }
        foreach (var file in Directory.GetFiles(dir, "STFormatter.*"))
            sb.AppendLine($"  {Path.GetFileName(file),-28} {FileVersion(file) ?? ""}");
    }

    private static void AppendAdsCheck(StringBuilder sb)
    {
        sb.AppendLine("-- Local TwinCAT/ADS check (127.0.0.1) --");
        try
        {
            sb.Append(TwinCatPinger.RunDiagnostics("127.0.0.1", 1000).BuildSummary());
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  ADS check failed: {ex.GetBaseException().Message}");
        }
    }

    // --- helpers ---

    private static string? FileVersion(string path)
    {
        try { return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null; }
        catch { return null; }
    }

    private static string? ProductVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var pv = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            return string.IsNullOrWhiteSpace(pv) ? null : pv.Trim();
        }
        catch { return null; }
    }

    private static string? FindOnPathOrKnown(string exeName, string[] knownPaths)
    {
        foreach (var p in knownPaths)
            if (File.Exists(p)) return p;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    // Enumerate the Running Object Table and return display names that look like
    // a Visual Studio / TcXaeShell DTE moniker (!*.DTE.*).
    private static List<string> EnumerateDteMonikers()
    {
        var result = new List<string>();
        IRunningObjectTable? rot = null;
        IEnumMoniker? enumMoniker = null;
        IBindCtx? bindCtx = null;
        try
        {
            if (GetRunningObjectTable(0, out rot) != 0 || rot == null) return result;
            rot.EnumRunning(out enumMoniker);
            if (CreateBindCtx(0, out bindCtx) != 0 || bindCtx == null) return result;

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                try
                {
                    monikers[0].GetDisplayName(bindCtx, null, out string displayName);
                    if (displayName.IndexOf(".DTE.", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.Add(displayName);
                }
                catch { }
                finally
                {
                    if (monikers[0] != null) Marshal.ReleaseComObject(monikers[0]);
                }
            }
        }
        catch { }
        finally
        {
            if (enumMoniker != null) Marshal.ReleaseComObject(enumMoniker);
            if (bindCtx != null) Marshal.ReleaseComObject(bindCtx);
            if (rot != null) Marshal.ReleaseComObject(rot);
        }
        return result;
    }

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
}
