using System;
using System.IO;
using System.Linq;
using System.Reflection;
using STBud.Git;
using STBud.Git.Diff;
using STFormatter.Core.Formatting;

namespace STBud.Git.Cli;

/// <summary>
/// stgit — STBud's TwinCAT-aware git helper. Standalone and independent of the
/// stfmt formatter CLI; it shells out to the same git.exe the rest of STBud uses
/// and presents diffs at the ST level (inside CDATA) rather than as raw XML.
/// </summary>
internal static class Program
{
    private static readonly string[] TwinCatXmlExtensions =
        { ".tcpou", ".tcdut", ".tcgvl", ".tcio", ".tcto" };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        string[] rest = args.Skip(1).ToArray();

        return command switch
        {
            "--help" or "-h" or "help" => PrintUsageAndSucceed(),
            "--version" or "-v" or "version" => PrintVersion(),
            "init" => InitCommand(rest),
            "status" => StatusCommand(rest),
            "log" => LogCommand(rest),
            "history" => LogCommand(rest, requirePath: true),
            "blame" => BlameCommand(rest),
            "diff" => DiffCommand(rest),
            "churn" => ChurnCommand(rest),
            "stage" => StageCommand(rest, stage: true),
            "unstage" => StageCommand(rest, stage: false),
            "commit" => CommitCommand(rest),
            "branch" => BranchCommand(rest),
            "checkout" => CheckoutCommand(rest),
            "restore" => RestoreCommand(rest),
            _ => UnknownCommand(command),
        };
    }

    // ---- commands -----------------------------------------------------------------

    private static int InitCommand(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        if (!RequireGit()) return 3;

        var r = GitClient.Init(dir);
        if (!r.Success)
        {
            Console.Error.WriteLine($"init failed: {r.ErrorMessage}");
            return 1;
        }
        Console.WriteLine($"Initialized empty Git repository in {Path.GetFullPath(dir)}");
        return 0;
    }

    private static int StatusCommand(string[] args)
    {
        if (!RequireGit()) return 3;
        if (!TryResolveRepo(args, out string repo, out _)) return 2;

        var entries = GitClient.Status(repo);
        if (entries.Count == 0)
        {
            Console.WriteLine("clean working tree");
            return 0;
        }
        foreach (var e in entries)
            Console.WriteLine($"{e.IndexStatus}{e.WorkTreeStatus}  {e.Path}  ({e.StateLabel})");
        return 0;
    }

    private static int LogCommand(string[] args, bool requirePath = false)
    {
        if (!RequireGit()) return 3;

        string? path = args.Length > 0 ? args[0] : null;
        if (requirePath && string.IsNullOrEmpty(path))
        {
            Console.Error.WriteLine("Usage: stgit history <file>");
            return 2;
        }

        string anchor = path ?? Directory.GetCurrentDirectory();
        string? repo = GitClient.FindRepoRoot(anchor);
        if (repo == null)
        {
            Console.Error.WriteLine("Not inside a git repository.");
            return 2;
        }

        string? rel = path != null ? GitClient.RelativePath(repo, Path.GetFullPath(path)) : null;
        var commits = GitClient.Log(repo, rel);
        foreach (var c in commits)
            Console.WriteLine($"{c.ShortSha}  {c.DateIso}  {c.Author,-16}  {c.Subject}");
        return 0;
    }

    private static int BlameCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stgit blame <file> [--raw]");
            return 2;
        }
        if (!RequireGit()) return 3;

        string full = Path.GetFullPath(args[0]);
        bool raw = args.Length > 1 && args[1] == "--raw";
        string? repo = GitClient.FindRepoRoot(full);
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        string rel = GitClient.RelativePath(repo, full);
        var blame = GitClient.Blame(repo, rel);

        // For TwinCAT XML files, git blame reports the raw XML/CDATA lines, which is
        // meaningless for ST work. When --raw is NOT given, extract the ST from the
        //blamed-file content and print only the ST lines with their attribution.
        // The line-number mapping is approximate (XML line → ST line within CDATA) but
        // useful for "who last touched this code". --raw preserves the old XML-level output.
        if (!raw && IsTwinCatXml(full))
        {
            // Map each XML blame line to its position within the CDATA blocks, then
            // emit only the ST lines with the corresponding author/sha.
            int stLineNo = 0;
            bool inCData = false;
            foreach (var line in blame)
            {
                string content = line.Content;
                if (content.Contains("<![CDATA["))
                {
                    inCData = true;
                    // ST may start on the same line after the opener.
                    int after = content.IndexOf("<![CDATA[") + 9;
                    if (after < content.Length)
                    {
                        stLineNo++;
                        Console.WriteLine($"{line.ShortSha}  {line.Author,-16}  {stLineNo,5}  {content.Substring(after)}");
                    }
                    continue;
                }
                if (content.Contains("]]>"))
                {
                    inCData = false;
                    continue;
                }
                if (inCData)
                {
                    stLineNo++;
                    Console.WriteLine($"{line.ShortSha}  {line.Author,-16}  {stLineNo,5}  {content}");
                }
            }
            return 0;
        }

        foreach (var line in blame)
            Console.WriteLine($"{line.ShortSha}  {line.Author,-16}  {line.LineNumber,5}  {line.Content}");
        return 0;
    }

    private static bool IsTwinCatXml(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".TcPOU", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".TcDUT", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".TcGVL", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".TcIO", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".TcTO", StringComparison.OrdinalIgnoreCase);
    }

    private static int StageCommand(string[] args, bool stage)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine($"Usage: stgit {(stage ? "stage" : "unstage")} <file>...");
            return 2;
        }
        if (!RequireGit()) return 3;

        string first = Path.GetFullPath(args[0]);
        string? repo = GitClient.FindRepoRoot(first);
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        var relPaths = args.Select(a => GitClient.RelativePath(repo, Path.GetFullPath(a))).ToList();
        var r = stage ? GitClient.Stage(repo, relPaths) : GitClient.Unstage(repo, relPaths);
        if (!r.Success) { Console.Error.WriteLine(r.ErrorMessage); return 1; }
        Console.WriteLine($"{(stage ? "Staged" : "Unstaged")} {relPaths.Count} file(s).");
        return 0;
    }

    private static int CommitCommand(string[] args)
    {
        // stgit commit -m <message>
        int mIdx = Array.IndexOf(args, "-m");
        if (mIdx < 0 || mIdx + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: stgit commit -m <message>");
            return 2;
        }
        if (!RequireGit()) return 3;

        string message = args[mIdx + 1];
        string? repo = GitClient.FindRepoRoot(Directory.GetCurrentDirectory());
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        var r = GitClient.Commit(repo, message);
        if (!r.Success) { Console.Error.WriteLine(r.ErrorMessage); return 1; }
        Console.WriteLine("Committed.");
        return 0;
    }

    private static int BranchCommand(string[] args)
    {
        if (!RequireGit()) return 3;
        string? repo = GitClient.FindRepoRoot(Directory.GetCurrentDirectory());
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        if (args.Length == 0)
        {
            // List branches.
            foreach (var b in GitClient.Branches(repo))
                Console.WriteLine($"{(b.IsCurrent ? "*" : " ")} {b.Name}");
            return 0;
        }

        var r = GitClient.CreateBranch(repo, args[0], checkout: false);
        if (!r.Success) { Console.Error.WriteLine(r.ErrorMessage); return 1; }
        Console.WriteLine($"Created branch {args[0]}.");
        return 0;
    }

    private static int CheckoutCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stgit checkout <branch>");
            return 2;
        }
        if (!RequireGit()) return 3;

        string? repo = GitClient.FindRepoRoot(Directory.GetCurrentDirectory());
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        var r = GitClient.Checkout(repo, args[0]);
        if (!r.Success) { Console.Error.WriteLine(r.ErrorMessage); return 1; }
        Console.WriteLine($"Checked out {args[0]}.");
        return 0;
    }

    private static int RestoreCommand(string[] args)
    {
        // stgit restore <rev> <file>  — write the ST from <rev> back into the file.
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: stgit restore <rev> <file>");
            return 2;
        }
        if (!RequireGit()) return 3;

        string rev = args[0];
        string full = Path.GetFullPath(args[1]);
        string? repo = GitClient.FindRepoRoot(full);
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        string rel = GitClient.RelativePath(repo, full);
        string committed = GitClient.ShowFile(repo, rev, rel);
        if (string.IsNullOrEmpty(committed))
        {
            Console.Error.WriteLine($"No such revision: {rev}");
            return 1;
        }

        // Backup, then write the ST from <rev> into the working file. For TwinCAT XML
        // we restore the whole XML (the CDATA ST is part of the file); for plain .st
        // we restore the raw text.
        string backup = full + ".bak";
        if (File.Exists(full)) File.Copy(full, backup, overwrite: true);
        File.WriteAllText(full, committed);
        Console.WriteLine($"Restored {rel} from {rev} (backup: {backup}).");
        return 0;
    }

    private static int DiffCommand(string[] args)
    {
        // stgit diff <rev> <file>   — ST-level diff of <rev> vs the working file.
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: stgit diff <rev> <file>");
            return 2;
        }
        if (!RequireGit()) return 3;

        string rev = args[0];
        string full = Path.GetFullPath(args[1]);
        string? repo = GitClient.FindRepoRoot(full);
        if (repo == null) { Console.Error.WriteLine("Not inside a git repository."); return 2; }

        string rel = GitClient.RelativePath(repo, full);
        string oldSt = TwinCatStExtractor.ExtractCombinedOrRaw(GitClient.ShowFile(repo, rev, rel));
        string newSt = File.Exists(full)
            ? TwinCatStExtractor.ExtractCombinedOrRaw(File.ReadAllText(full))
            : "";

        var rows = LineDiff.PairChangeRuns(LineDiff.Compute(oldSt, newSt));
        var (added, removed, changed, unchanged) = LineDiff.DetailedStats(rows);
        if (added == 0 && removed == 0 && changed == 0)
        {
            Console.WriteLine($"No ST changes between {rev} and working tree for {rel}.");
            return 0;
        }
        Console.Write(LineDiff.ToText(rows));
        Console.WriteLine($"  (+{added}  -{removed}  ~{changed})");
        return 0;
    }

    private static int ChurnCommand(string[] args)
    {
        if (!RequireGit()) return 3;
        if (!TryResolveRepo(args, out string repo, out _)) return 2;

        var churn = GitClient.Churn(repo);
        Console.WriteLine("changes  path");
        foreach (var c in churn)
            Console.WriteLine($"{c.Changes,7}  {c.Path}");
        return 0;
    }

    // ---- helpers ------------------------------------------------------------------

    private static bool TryResolveRepo(string[] args, out string repo, out string anchor)
    {
        anchor = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        string? found = GitClient.FindRepoRoot(anchor);
        if (found == null)
        {
            Console.Error.WriteLine("Not inside a git repository.");
            repo = "";
            return false;
        }
        repo = found;
        return true;
    }

    private static bool RequireGit()
    {
        if (GitClient.IsGitAvailable(out _)) return true;
        Console.Error.WriteLine("git not found — install Git for Windows and ensure git is on PATH.");
        return false;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static int PrintUsageAndSucceed()
    {
        PrintUsage();
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"STBud stgit {GetVersion()}");
        return 0;
    }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    private static void PrintUsage()
    {
        Console.WriteLine(
@"stgit — STBud TwinCAT-aware git helper

Usage:
  stgit init [dir]            Initialize a repository
  stgit status [path]         Working-tree status
  stgit log [file]            Commit log (optionally for one file)
  stgit history <file>        Commit history of a file
  stgit blame <file> [--raw]  Line-by-line last-change attribution
                              (--raw = raw XML for .TcPOU; default = ST-aware)
  stgit diff <rev> <file>     ST-level diff of <rev> vs the working file
  stgit churn [path]          Most-changed files (hotspots)
  stgit stage <file>...       Stage files
  stgit unstage <file>...    Unstage files
  stgit commit -m <msg>       Commit staged changes
  stgit branch [name]         List branches, or create a new one
  stgit checkout <branch>     Switch branch
  stgit restore <rev> <file>  Restore <file> from <rev> (backup .bak)

TwinCAT .TcPOU/.TcDUT/.TcGVL files are diffed/blamed at the Structured-Text level
(inside CDATA), not as raw XML. This tool is independent of the stfmt formatter.");
    }
}
