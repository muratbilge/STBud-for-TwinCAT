using System;

namespace STFormatter.Core.Configuration;

public sealed class TcXaeShellVersionProfile
{
    public string Name { get; }
    public string DteVersion { get; }
    public string VsShellGeneration { get; }
    public string PrimaryRotMonikerPrefix { get; }
    public string FallbackRotMonikerPrefix { get; }
    public string RegistryRoot { get; }
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
        string registryRoot,
        string requiredFramework)
    {
        Name = name;
        DteVersion = dteVersion;
        VsShellGeneration = vsShellGeneration;
        PrimaryRotMonikerPrefix = $"!TcXaeShell.DTE.{dteVersion}.";
        FallbackRotMonikerPrefix = $"!VisualStudio.DTE.{dteVersion}.";
        RegistryRoot = registryRoot;
        RequiredFramework = requiredFramework;
        TargetContextMenuNames = new[] { "PlcCodeWinContextMenu", "Code Window" };
        TwinCatFileExtensions = new[] { ".TcPOU", ".TcDUT", ".TcGVL", ".TcIO", ".TcTO" };
        ProcessName = "TcXaeShell";
        DteNameMatch = "TcXaeShell";
        InstallPathPattern = @"Beckhoff\TcXaeShell\Common7\IDE\";
    }

    public static TcXaeShellVersionProfile VS2017 { get; } = new(
        "TC3-VS2017",
        "15.0",
        "2017",
        @"Software\Beckhoff\TcXaeShell\15.0",
        "4.6");

    public static TcXaeShellVersionProfile VS2015 { get; } = new(
        "TC3-VS2015",
        "14.0",
        "2015",
        @"Software\Beckhoff\TcXaeShell\14.0",
        "4.6");

    public static TcXaeShellVersionProfile VS2013 { get; } = new(
        "TC3-VS2013",
        "12.0",
        "2013",
        @"Software\Beckhoff\TcXaeShell\12.0",
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
            int firstDot = displayName.IndexOf('.', 1);
            int secondDot = displayName.IndexOf('.', firstDot + 1);
            if (firstDot > 0 && secondDot > firstDot)
            {
                string version = displayName.Substring(firstDot + 1, secondDot - firstDot - 1);
                return FromDteVersion(version);
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
            $@"Software\Beckhoff\TcXaeShell\{dteVersion}",
            "4.5.1");
    }

    public override string ToString() => $"{Name} (DTE {DteVersion}, VS {VsShellGeneration})";
}