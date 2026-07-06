using STBud.Git;
using STBud.Git.Diff;
using STFormatter.Core.Formatting;

namespace STBud.Git.Tests;

// End-to-end against a throwaway repo under %TEMP%. Everything is synthetic
// (MAIN.TcPOU, generic author). Auto-skips when git.exe is not installed so CI
// without git stays green.
public class GitClientIntegrationTests : IDisposable
{
    private readonly string _repo;
    private readonly bool _gitAvailable;

    public GitClientIntegrationTests()
    {
        _gitAvailable = GitClient.IsGitAvailable(out _);
        _repo = Path.Combine(Path.GetTempPath(), "stbud_git_it_" + Guid.NewGuid().ToString("N"));
        if (_gitAvailable)
        {
            Directory.CreateDirectory(_repo);
            GitClient.Init(_repo);
            // Local identity so commits succeed regardless of global config.
            GitProcessRunner.Run(_repo, "config", "user.email", "dev@example.com");
            GitProcessRunner.Run(_repo, "config", "user.name", "dev");
        }
    }

    private static string Pou(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<TcPlcObject Version=\"1.1.0.1\">\r\n" +
        "  <POU Name=\"MAIN\">\r\n" +
        "    <Declaration><![CDATA[PROGRAM MAIN\r\nVAR\r\n    a : INT;\r\nEND_VAR]]></Declaration>\r\n" +
        "    <Implementation>\r\n" +
        "      <ST><![CDATA[" + body + "]]></ST>\r\n" +
        "    </Implementation>\r\n" +
        "  </POU>\r\n" +
        "</TcPlcObject>";

    [Fact]
    public void History_diff_and_churn_round_trip()
    {
        if (!_gitAvailable) return; // skip without git

        string file = Path.Combine(_repo, "MAIN.TcPOU");

        File.WriteAllText(file, Pou("a := 1;"));
        Assert.True(GitClient.Stage(_repo, new[] { "MAIN.TcPOU" }).Success);
        Assert.True(GitClient.Commit(_repo, "first").Success);

        File.WriteAllText(file, Pou("a := 2;"));
        Assert.True(GitClient.Stage(_repo, new[] { "MAIN.TcPOU" }).Success);
        Assert.True(GitClient.Commit(_repo, "second").Success);

        // Log for the file shows both commits, newest first.
        var commits = GitClient.Log(_repo, "MAIN.TcPOU");
        Assert.Equal(2, commits.Count);
        Assert.Equal("second", commits[0].Subject);
        Assert.Equal("first", commits[1].Subject);

        // ShowFile at the first commit returns the old XML; ST extraction gives old body.
        string oldXml = GitClient.ShowFile(_repo, commits[1].Sha, "MAIN.TcPOU");
        string oldSt = TwinCatStExtractor.ExtractCombinedOrRaw(oldXml);
        string newSt = TwinCatStExtractor.ExtractCombinedOrRaw(File.ReadAllText(file));
        Assert.Contains("a := 1;", oldSt);
        Assert.Contains("a := 2;", newSt);

        // The ST diff is a single changed line, not XML noise.
        var (added, removed) = LineDiff.Stats(LineDiff.Compute(oldSt, newSt));
        Assert.Equal(1, added);
        Assert.Equal(1, removed);

        // Commit file list + churn.
        var files = GitClient.CommitFiles(_repo, commits[0].Sha);
        Assert.Contains(files, f => f.Path == "MAIN.TcPOU");

        var churn = GitClient.Churn(_repo);
        Assert.Contains(churn, c => c.Path == "MAIN.TcPOU" && c.Changes == 2);
    }

    [Fact]
    public void Status_reports_untracked_then_staged()
    {
        if (!_gitAvailable) return;

        File.WriteAllText(Path.Combine(_repo, "FB_Sample.TcPOU"), Pou("a := 0;"));

        var untracked = GitClient.Status(_repo);
        Assert.Contains(untracked, e => e.Path == "FB_Sample.TcPOU" && e.IsUntracked);

        GitClient.Stage(_repo, new[] { "FB_Sample.TcPOU" });
        var staged = GitClient.Status(_repo);
        Assert.Contains(staged, e => e.Path == "FB_Sample.TcPOU" && e.IsStaged);
    }

    [Fact]
    public void Branch_create_and_switch()
    {
        if (!_gitAvailable) return;

        File.WriteAllText(Path.Combine(_repo, "MAIN.TcPOU"), Pou("a := 1;"));
        GitClient.Stage(_repo, new[] { "MAIN.TcPOU" });
        GitClient.Commit(_repo, "first");

        Assert.True(GitClient.CreateBranch(_repo, "feature", checkout: true).Success);
        Assert.Equal("feature", GitClient.CurrentBranch(_repo));

        var branches = GitClient.Branches(_repo);
        Assert.Contains(branches, b => b.Name == "feature" && b.IsCurrent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }
}
