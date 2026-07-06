using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STBud.Git;

/// <summary>
/// A thin, defensive service over <c>git.exe</c> for STBud's repo tools. Read
/// operations return parsed POCOs (empty on failure); write operations return the
/// raw <see cref="GitResult"/> so callers can surface git's own error text.
///
/// The output parsers are pure functions over git's stable porcelain/format output
/// and are public so they can be unit-tested with canned strings (no real repo).
/// </summary>
public static class GitClient
{
    // Unit Separator between log fields; subjects never contain it.
    private const char FieldSep = '\x1f';
    private const string LogFormat = "--pretty=format:%H\x1f%h\x1f%an\x1f%aI\x1f%s";

    // ---- availability & discovery -------------------------------------------------

    public static bool IsGitAvailable(out string version)
    {
        version = "";
        var r = GitProcessRunner.Run(null, 5000, "--version");
        if (!r.Success) return false;
        version = r.StdOut.Trim();
        return version.IndexOf("git version", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Walk up from <paramref name="startPath"/> (file or directory) to the repo
    /// root — the directory containing <c>.git</c>. Pure filesystem, so it works
    /// even when git is not installed. Returns null when not inside a repo.
    /// </summary>
    public static string? FindRepoRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        DirectoryInfo? dir;
        try
        {
            dir = File.Exists(startPath)
                ? new FileInfo(startPath).Directory
                : new DirectoryInfo(startPath);
        }
        catch
        {
            return null;
        }

        while (dir != null)
        {
            string git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Make a repo-relative, forward-slash path git can address.</summary>
    public static string RelativePath(string repoRoot, string fullPath)
    {
        try
        {
            string root = Path.GetFullPath(repoRoot).TrimEnd('\\', '/');
            string full = Path.GetFullPath(fullPath);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                full = full.Substring(root.Length + 1);
            return full.Replace('\\', '/');
        }
        catch
        {
            return fullPath.Replace('\\', '/');
        }
    }

    // ---- read operations ----------------------------------------------------------

    public static List<CommitInfo> Log(string repoRoot, string? relPath = null, int max = 200)
    {
        var args = new List<string> { "-c", "core.quotepath=false", "log", LogFormat, "-n", max.ToString() };
        if (!string.IsNullOrEmpty(relPath))
        {
            args.Add("--");
            args.Add(relPath!);
        }
        var r = GitProcessRunner.Run(repoRoot, args.ToArray());
        return r.Success ? ParseLog(r.StdOut) : new List<CommitInfo>();
    }

    public static List<FileChange> CommitFiles(string repoRoot, string sha)
    {
        var r = GitProcessRunner.Run(repoRoot, "-c", "core.quotepath=false",
            "show", "--name-status", "--pretty=format:", sha);
        return r.Success ? ParseNameStatus(r.StdOut) : new List<FileChange>();
    }

    public static List<GitStatusEntry> Status(string repoRoot)
    {
        var r = GitProcessRunner.Run(repoRoot, "-c", "core.quotepath=false",
            "status", "--porcelain=v1");
        return r.Success ? ParseStatus(r.StdOut) : new List<GitStatusEntry>();
    }

    public static List<BlameLine> Blame(string repoRoot, string relPath)
    {
        var r = GitProcessRunner.Run(repoRoot, "-c", "core.quotepath=false",
            "blame", "--porcelain", "--", relPath);
        return r.Success ? ParseBlame(r.StdOut) : new List<BlameLine>();
    }

    public static List<BranchInfo> Branches(string repoRoot)
    {
        var r = GitProcessRunner.Run(repoRoot,
            "for-each-ref", "--format=%(refname:short)\x1f%(HEAD)", "refs/heads");
        return r.Success ? ParseBranches(r.StdOut) : new List<BranchInfo>();
    }

    public static string CurrentBranch(string repoRoot)
    {
        var r = GitProcessRunner.Run(repoRoot, "rev-parse", "--abbrev-ref", "HEAD");
        return r.Success ? r.StdOut.Trim() : "";
    }

    /// <summary>Raw bytes of <paramref name="relPath"/> at <paramref name="rev"/> (e.g. "HEAD").</summary>
    public static string ShowFile(string repoRoot, string rev, string relPath)
    {
        var r = GitProcessRunner.Run(repoRoot, "show", $"{rev}:{relPath}");
        return r.Success ? r.StdOut : "";
    }

    public static List<ChurnEntry> Churn(string repoRoot, string? since = null, int max = 50)
    {
        var args = new List<string> { "-c", "core.quotepath=false", "log", "--pretty=format:", "--name-only" };
        if (!string.IsNullOrEmpty(since))
        {
            args.Add("--since");
            args.Add(since!);
        }
        var r = GitProcessRunner.Run(repoRoot, args.ToArray());
        return r.Success ? AggregateChurn(r.StdOut, max) : new List<ChurnEntry>();
    }

    // ---- write operations (return GitResult so the UI can show git's error) -------

    public static GitResult Init(string directory) => GitProcessRunner.Run(directory, "init");

    public static GitResult Stage(string repoRoot, IEnumerable<string> relPaths)
    {
        var args = new List<string> { "add", "--" };
        args.AddRange(relPaths);
        return GitProcessRunner.Run(repoRoot, args.ToArray());
    }

    public static GitResult Unstage(string repoRoot, IEnumerable<string> relPaths)
    {
        var args = new List<string> { "reset", "-q", "HEAD", "--" };
        args.AddRange(relPaths);
        return GitProcessRunner.Run(repoRoot, args.ToArray());
    }

    public static GitResult Commit(string repoRoot, string message)
        => GitProcessRunner.Run(repoRoot, "commit", "-m", message);

    public static GitResult CreateBranch(string repoRoot, string name, bool checkout)
        => checkout
            ? GitProcessRunner.Run(repoRoot, "checkout", "-b", name)
            : GitProcessRunner.Run(repoRoot, "branch", name);

    public static GitResult Checkout(string repoRoot, string name)
        => GitProcessRunner.Run(repoRoot, "checkout", name);

    // ---- parsers (public for unit testing) ----------------------------------------

    public static List<CommitInfo> ParseLog(string stdout)
    {
        var list = new List<CommitInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (line.Length == 0) continue;
            var p = line.Split(new[] { FieldSep }, 5);
            if (p.Length < 5) continue;
            list.Add(new CommitInfo
            {
                Sha = p[0],
                ShortSha = p[1],
                Author = p[2],
                DateIso = p[3],
                Subject = p[4],
            });
        }
        return list;
    }

    public static List<FileChange> ParseNameStatus(string stdout)
    {
        var list = new List<FileChange>();
        foreach (var line in SplitLines(stdout))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            char code = parts[0].Length > 0 ? parts[0][0] : '?';
            var change = new FileChange { Kind = KindFromCode(code) };
            if ((code == 'R' || code == 'C') && parts.Length >= 3)
            {
                change.OldPath = parts[1];
                change.Path = parts[2];
            }
            else
            {
                change.Path = parts[parts.Length - 1];
            }
            list.Add(change);
        }
        return list;
    }

    public static List<GitStatusEntry> ParseStatus(string stdout)
    {
        var list = new List<GitStatusEntry>();
        foreach (var line in SplitLines(stdout))
        {
            if (line.Length < 3) continue;
            var entry = new GitStatusEntry
            {
                IndexStatus = line[0],
                WorkTreeStatus = line[1],
            };
            string path = line.Substring(3);
            int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                entry.OldPath = Unquote(path.Substring(0, arrow));
                entry.Path = Unquote(path.Substring(arrow + 4));
            }
            else
            {
                entry.Path = Unquote(path);
            }
            list.Add(entry);
        }
        return list;
    }

    public static List<BlameLine> ParseBlame(string stdout)
    {
        var list = new List<BlameLine>();
        var authorBySha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string curSha = "";
        string curAuthor = "";
        int curFinalLine = 0;

        foreach (var line in SplitLines(stdout))
        {
            if (line.Length == 0) continue;

            if (line[0] == '\t')
            {
                // The actual source line for the most recent header.
                if (!string.IsNullOrEmpty(curSha) && string.IsNullOrEmpty(curAuthor)
                    && authorBySha.TryGetValue(curSha, out var cached))
                {
                    curAuthor = cached;
                }
                list.Add(new BlameLine
                {
                    Sha = curSha,
                    Author = curAuthor,
                    LineNumber = curFinalLine,
                    Content = line.Substring(1),
                });
                continue;
            }

            if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                curAuthor = line.Substring(7);
                if (!string.IsNullOrEmpty(curSha))
                    authorBySha[curSha] = curAuthor;
                continue;
            }

            // Header: "<40-hex-sha> <origLine> <finalLine> [<numLines>]"
            var header = TryParseBlameHeader(line);
            if (header != null)
            {
                curSha = header.Value.sha;
                curFinalLine = header.Value.finalLine;
                curAuthor = authorBySha.TryGetValue(curSha, out var a) ? a : "";
            }
        }
        return list;
    }

    public static List<BranchInfo> ParseBranches(string stdout)
    {
        var list = new List<BranchInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (line.Length == 0) continue;
            var p = line.Split(FieldSep);
            list.Add(new BranchInfo
            {
                Name = p[0],
                IsCurrent = p.Length > 1 && p[1].Trim() == "*",
            });
        }
        return list;
    }

    public static List<ChurnEntry> AggregateChurn(string stdout, int max = 50)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in SplitLines(stdout))
        {
            var path = raw.Trim();
            if (path.Length == 0) continue;
            counts.TryGetValue(path, out var n);
            counts[path] = n + 1;
        }

        var list = new List<ChurnEntry>();
        foreach (var kv in counts)
            list.Add(new ChurnEntry { Path = kv.Key, Changes = kv.Value });

        list.Sort((a, b) =>
        {
            int c = b.Changes.CompareTo(a.Changes);
            return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path);
        });

        if (max > 0 && list.Count > max)
            list = list.GetRange(0, max);
        return list;
    }

    // ---- helpers ------------------------------------------------------------------

    private static (string sha, int finalLine)? TryParseBlameHeader(string line)
    {
        int sp = line.IndexOf(' ');
        if (sp != 40) return null;
        string sha = line.Substring(0, 40);
        for (int k = 0; k < 40; k++)
            if (!Uri.IsHexDigit(sha[k])) return null;

        var parts = line.Split(' ');
        // parts: [sha, origLine, finalLine, (numLines)]
        if (parts.Length >= 3 && int.TryParse(parts[2], out var finalLine))
            return (sha, finalLine);
        return null;
    }

    private static FileChangeKind KindFromCode(char code) => code switch
    {
        'A' => FileChangeKind.Added,
        'M' => FileChangeKind.Modified,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'C' => FileChangeKind.Copied,
        'T' => FileChangeKind.TypeChanged,
        _ => FileChangeKind.Unknown,
    };

    private static string Unquote(string path)
    {
        // git quotes paths with special chars in double quotes; strip the wrapper.
        if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
            return path.Substring(1, path.Length - 2);
        return path;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int end = i;
                if (end > start && text[end - 1] == '\r') end--;
                yield return text.Substring(start, end - start);
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            int end = text.Length;
            if (end > start && text[end - 1] == '\r') end--;
            yield return text.Substring(start, end - start);
        }
    }
}
