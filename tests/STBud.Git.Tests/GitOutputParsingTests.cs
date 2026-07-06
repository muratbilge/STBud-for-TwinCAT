using STBud.Git;

namespace STBud.Git.Tests;

// All fixtures here are SYNTHETIC: generic names (MAIN, FB_Sample, dev/Alice/Bob)
// and fake SHAs. No real repository data per the project privacy rule.
public class GitOutputParsingTests
{
    private const char US = ''; // field separator used in our --pretty/for-each-ref formats

    [Fact]
    public void ParseLog_reads_all_fields()
    {
        string sha1 = new string('a', 40);
        string sha2 = new string('b', 40);
        string output =
            $"{sha1}{US}aaaaaaa{US}dev{US}2026-06-20T10:00:00+00:00{US}Add MAIN\n" +
            $"{sha2}{US}bbbbbbb{US}dev{US}2026-06-19T09:30:00+00:00{US}Initial commit";

        var commits = GitClient.ParseLog(output);

        Assert.Equal(2, commits.Count);
        Assert.Equal(sha1, commits[0].Sha);
        Assert.Equal("aaaaaaa", commits[0].ShortSha);
        Assert.Equal("dev", commits[0].Author);
        Assert.Equal("Add MAIN", commits[0].Subject);
        Assert.NotNull(commits[0].Date);
        Assert.Equal("Initial commit", commits[1].Subject);
    }

    [Fact]
    public void ParseLog_keeps_separators_inside_subject_out_of_fields()
    {
        // Subject is the 5th field (split limited to 5) so any stray content stays intact.
        string sha = new string('c', 40);
        string output = $"{sha}{US}ccccccc{US}dev{US}2026-06-20T10:00:00+00:00{US}Fix a := b := c chaining";

        var commits = GitClient.ParseLog(output);

        Assert.Single(commits);
        Assert.Equal("Fix a := b := c chaining", commits[0].Subject);
    }

    [Fact]
    public void ParseNameStatus_handles_modify_add_and_rename()
    {
        string output =
            "\n" +                       // leading blank from --pretty=format:
            "M\tMAIN.TcPOU\n" +
            "A\tFB_Sample.TcPOU\n" +
            "R096\tOld_Name.TcPOU\tNew_Name.TcPOU";

        var changes = GitClient.ParseNameStatus(output);

        Assert.Equal(3, changes.Count);
        Assert.Equal(FileChangeKind.Modified, changes[0].Kind);
        Assert.Equal("MAIN.TcPOU", changes[0].Path);
        Assert.Equal(FileChangeKind.Added, changes[1].Kind);
        Assert.Equal(FileChangeKind.Renamed, changes[2].Kind);
        Assert.Equal("Old_Name.TcPOU", changes[2].OldPath);
        Assert.Equal("New_Name.TcPOU", changes[2].Path);
    }

    [Fact]
    public void ParseStatus_classifies_staged_unstaged_untracked_and_rename()
    {
        string output =
            "M  MAIN.TcPOU\n" +          // staged modification
            " M FB_Sample.TcPOU\n" +     // unstaged modification
            "?? Notes.txt\n" +
            "R  Old.TcPOU -> New.TcPOU";

        var entries = GitClient.ParseStatus(output);

        Assert.Equal(4, entries.Count);
        Assert.True(entries[0].IsStaged);
        Assert.False(entries[0].HasUnstagedChanges);
        Assert.True(entries[1].HasUnstagedChanges);
        Assert.False(entries[1].IsStaged);
        Assert.True(entries[2].IsUntracked);
        Assert.Equal("Old.TcPOU", entries[3].OldPath);
        Assert.Equal("New.TcPOU", entries[3].Path);
    }

    [Fact]
    public void ParseBlame_attributes_lines_and_caches_author_per_commit()
    {
        string sha1 = new string('1', 40);
        string sha2 = new string('2', 40);
        string output =
            $"{sha1} 1 1 1\n" +
            "author Alice\n" +
            "author-mail <alice@example.com>\n" +
            "author-time 1700000000\n" +
            "author-tz +0000\n" +
            "committer Alice\n" +
            "summary add header\n" +
            "filename FB_Sample.st\n" +
            "\tFUNCTION_BLOCK FB_Sample\n" +
            $"{sha2} 2 2 1\n" +
            "author Bob\n" +
            "author-mail <bob@example.com>\n" +
            "author-time 1700000100\n" +
            "author-tz +0000\n" +
            "committer Bob\n" +
            "summary add body\n" +
            "filename FB_Sample.st\n" +
            "\tEND_FUNCTION_BLOCK";

        var lines = GitClient.ParseBlame(output);

        Assert.Equal(2, lines.Count);
        Assert.Equal(sha1, lines[0].Sha);
        Assert.Equal("Alice", lines[0].Author);
        Assert.Equal(1, lines[0].LineNumber);
        Assert.Equal("FUNCTION_BLOCK FB_Sample", lines[0].Content);
        Assert.Equal("Bob", lines[1].Author);
        Assert.Equal(2, lines[1].LineNumber);
        Assert.Equal("END_FUNCTION_BLOCK", lines[1].Content);
    }

    [Fact]
    public void ParseBranches_marks_current()
    {
        string output =
            $"master{US}\n" +
            $"feature{US}*";

        var branches = GitClient.ParseBranches(output);

        Assert.Equal(2, branches.Count);
        Assert.False(branches[0].IsCurrent);
        Assert.Equal("master", branches[0].Name);
        Assert.True(branches[1].IsCurrent);
        Assert.Equal("feature", branches[1].Name);
    }

    [Fact]
    public void AggregateChurn_counts_and_orders_by_frequency()
    {
        string output =
            "MAIN.TcPOU\n" +
            "FB_Sample.TcPOU\n" +
            "\n" +
            "MAIN.TcPOU\n" +
            "\n" +
            "MAIN.TcPOU\n" +
            "FB_Sample.TcPOU";

        var churn = GitClient.AggregateChurn(output);

        Assert.Equal(2, churn.Count);
        Assert.Equal("MAIN.TcPOU", churn[0].Path);
        Assert.Equal(3, churn[0].Changes);
        Assert.Equal("FB_Sample.TcPOU", churn[1].Path);
        Assert.Equal(2, churn[1].Changes);
    }

    [Fact]
    public void RelativePath_makes_repo_relative_forward_slash_path()
    {
        string rel = GitClient.RelativePath(@"C:\repo", @"C:\repo\sub\MAIN.TcPOU");
        Assert.Equal("sub/MAIN.TcPOU", rel);
    }

    [Fact]
    public void FindRepoRoot_walks_up_to_the_dot_git_directory()
    {
        string root = Path.Combine(Path.GetTempPath(), "stbud_git_test_" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "a", "b");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(nested);

            string? found = GitClient.FindRepoRoot(nested);

            Assert.NotNull(found);
            Assert.Equal(
                Path.GetFullPath(root).TrimEnd('\\'),
                Path.GetFullPath(found!).TrimEnd('\\'));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FindRepoRoot_returns_null_outside_a_repo()
    {
        string lone = Path.Combine(Path.GetTempPath(), "stbud_no_repo_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(lone);
            // A temp dir with no .git anywhere up to the drive root.
            Assert.Null(GitClient.FindRepoRoot(lone));
        }
        finally
        {
            try { Directory.Delete(lone, recursive: true); } catch { }
        }
    }
}
