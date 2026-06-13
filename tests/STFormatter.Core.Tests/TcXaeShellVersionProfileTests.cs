using STFormatter.Core.Configuration;

namespace STFormatter.Core.Tests;

/// <summary>
/// Forward-compatibility contract for shell detection. These run everywhere
/// (no TcXaeShell needed) and pin the behavior the Host relies on when a newer
/// shell - e.g. TwinCAT 3 Build 4026 - registers a different DTE version or a
/// 64-bit process.
/// </summary>
public class TcXaeShellVersionProfileTests
{
    [Theory]
    [InlineData("!TcXaeShell.DTE.15.0:28196", "15.0")]
    [InlineData("!TcXaeShell.DTE.14.0:1234", "14.0")]
    [InlineData("!VisualStudio.DTE.12.0:5678", "12.0")]
    public void KnownMonikers_ResolveToProfile(string moniker, string expectedDte)
    {
        var profile = TcXaeShellVersionProfile.DetectFromRotMoniker(moniker);
        Assert.NotNull(profile);
        Assert.Equal(expectedDte, profile!.DteVersion);
    }

    // A Build 4026 shell that bumps the DTE version (or uses VS2022 integration)
    // must still be detected via the dynamic fallback, with the SAME context
    // menu names and file extensions the Host injects against.
    [Theory]
    [InlineData("!TcXaeShell.DTE.16.0:4242")]
    [InlineData("!TcXaeShell.DTE.17.0:4242")]
    [InlineData("!VisualStudio.DTE.17.0:4242")]
    public void UnknownNewerMoniker_StillResolvesWithStableMenusAndExtensions(string moniker)
    {
        var profile = TcXaeShellVersionProfile.DetectFromRotMoniker(moniker);
        Assert.NotNull(profile);
        Assert.Contains("PlcCodeWinContextMenu", profile!.TargetContextMenuNames);
        Assert.Contains("Code Window", profile.TargetContextMenuNames);
        Assert.Contains(".TcPOU", profile.TwinCatFileExtensions);
    }

    [Fact]
    public void NonTcMoniker_ReturnsNull()
    {
        Assert.Null(TcXaeShellVersionProfile.DetectFromRotMoniker("!SomeOther.App:1"));
        Assert.Null(TcXaeShellVersionProfile.DetectFromRotMoniker(""));
    }

    [Theory]
    [InlineData("TcXaeShell", true)]
    [InlineData("TcXaeShell64", true)]      // 4026 64-bit shell
    [InlineData("TcXaeShell.exe", true)]
    [InlineData("devenv", false)]
    [InlineData("notepad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsShellProcessName_CoversAllShellVariants(string? processName, bool expected)
    {
        Assert.Equal(expected, TcXaeShellVersionProfile.IsShellProcessName(processName));
    }

    [Fact]
    public void ShellProcessNames_IncludeBoth32And64Bit()
    {
        Assert.Contains("TcXaeShell", TcXaeShellVersionProfile.ShellProcessNames);
        Assert.Contains("TcXaeShell64", TcXaeShellVersionProfile.ShellProcessNames);
    }
}
