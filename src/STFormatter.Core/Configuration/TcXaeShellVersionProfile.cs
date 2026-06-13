using System;

namespace STFormatter.Core.Configuration;

public sealed class TcXaeShellVersionProfile
{
    public string Name { get; }
    public string DteVersion { get; }
    public string VsShellGeneration { get; }
    public string PrimaryRotMonikerPrefix { get; }
    public string FallbackRotMonikerPrefix { get; }
    public string[] TargetContextMenuNames { get; }
    public string[] TwinCatFileExtensions { get; }
    public string ProcessName { get; }
    public string DteNameMatch { get; }
    public string RequiredFramework { get; }
    public string InstallPathPattern { get; }

    private TcXaeShellVersionProfile(
        string name,
        string dteVersion,
        string vsShellGeneration,
        string requiredFramework)
    {
        Name = name;
        DteVersion = dteVersion;
        VsShellGeneration = vsShellGeneration;
        PrimaryRotMonikerPrefix = $"!TcXaeShell.DTE.{dteVersion}:";
        FallbackRotMonikerPrefix = $"!VisualStudio.DTE.{dteVersion}:";
        RequiredFramework = requiredFramework;
        TargetContextMenuNames = new[] { "PlcCodeWinContextMenu", "Code Window" };
        TwinCatFileExtensions = new[] { ".TcPOU", ".TcDUT", ".TcGVL", ".TcIO", ".TcTO" };
        ProcessName = "TcXaeShell";
        DteNameMatch = "TcXaeShell";
        InstallPathPattern = @"Beckhoff\TcXaeShell\Common7\IDE\";
    }

    /// <summary>
    /// Process names of every TwinCAT XAE shell variant to look for. Build 4026
    /// adds a 64-bit shell (TcXaeShell64) alongside the classic 32-bit one;
    /// both register a DTE automation object, so both must be scanned. Match by
    /// prefix ("TcXaeShell"*) at call sites rather than exact-equals so a future
    /// variant suffix is still recognized.
    /// </summary>
    public static readonly string[] ShellProcessNames = { "TcXaeShell", "TcXaeShell64" };

    /// <summary>True if <paramref name="processName"/> is any TwinCAT XAE shell.</summary>
    public static bool IsShellProcessName(string? processName) =>
        !string.IsNullOrEmpty(processName) &&
        processName!.StartsWith("TcXaeShell", StringComparison.OrdinalIgnoreCase);

    public static TcXaeShellVersionProfile VS2017 { get; } = new(
        "TC3-VS2017",
        "15.0",
        "2017",
        "4.6");

    public static TcXaeShellVersionProfile VS2015 { get; } = new(
        "TC3-VS2015",
        "14.0",
        "2015",
        "4.6");

    public static TcXaeShellVersionProfile VS2013 { get; } = new(
        "TC3-VS2013",
        "12.0",
        "2013",
        "4.5.1");

    public static TcXaeShellVersionProfile[] AllProfiles { get; } =
        { VS2017, VS2015, VS2013 };

    public static TcXaeShellVersionProfile? DetectFromRotMoniker(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return null;

        foreach (var profile in AllProfiles)
        {
            if (displayName.StartsWith(profile.PrimaryRotMonikerPrefix, StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith(profile.FallbackRotMonikerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        if (displayName.StartsWith("!TcXaeShell.DTE.", StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase))
        {
            // Format: !TcXaeShell.DTE.15.0:28196 or !VisualStudio.DTE.15.0:28196
            // Extract version between "!<prefix>.DTE." and ":PID"
            int dteEnd = displayName.IndexOf(".DTE.", StringComparison.OrdinalIgnoreCase);
            if (dteEnd > 0)
            {
                int versionStart = dteEnd + 5; // skip ".DTE."
                int colonIdx = displayName.IndexOf(':', versionStart);
                string versionPart = colonIdx > versionStart
                    ? displayName.Substring(versionStart, colonIdx - versionStart)
                    : displayName.Substring(versionStart);
                return FromDteVersion(versionPart);
            }
        }

        return null;
    }

    public static TcXaeShellVersionProfile? FromDteVersion(string dteVersion)
    {
        foreach (var profile in AllProfiles)
        {
            if (string.Equals(profile.DteVersion, dteVersion, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return new TcXaeShellVersionProfile(
            $"TC3-unknown-{dteVersion}",
            dteVersion,
            "unknown",
            "4.5.1");
    }

    public override string ToString() => $"{Name} (DTE {DteVersion}, VS {VsShellGeneration})";
}
