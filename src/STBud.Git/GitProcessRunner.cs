using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace STBud.Git;

/// <summary>Outcome of one git invocation.</summary>
public sealed class GitResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public bool TimedOut { get; set; }
    public bool Launched { get; set; } = true;

    /// <summary>True when git ran and returned exit code 0.</summary>
    public bool Success => Launched && !TimedOut && ExitCode == 0;

    /// <summary>A short, user-facing error (git's stderr, or a launch/timeout note).</summary>
    public string ErrorMessage =>
        !Launched ? "git could not be started (is Git for Windows installed and on PATH?)"
        : TimedOut ? "git timed out"
        : string.IsNullOrWhiteSpace(StdErr) ? $"git exited with code {ExitCode}" : StdErr.Trim();
}

/// <summary>
/// The single choke point for running <c>git.exe</c>. Captures stdout verbatim
/// (so file content from <c>git show</c> is not line-ending-mangled) and never
/// pops a console window. Pure <see cref="System.Diagnostics.Process"/> — works on
/// net8.0/net48/net462 alike, which is why STBud uses git.exe rather than
/// LibGit2Sharp (the latter cannot target net462).
/// </summary>
public static class GitProcessRunner
{
    /// <summary>Override to point at a specific git.exe; default resolves via PATH.</summary>
    public static string GitExecutable { get; set; } = "git";

    public const int DefaultTimeoutMs = 30000;

    public static GitResult Run(string? workingDirectory, params string[] args)
        => Run(workingDirectory, DefaultTimeoutMs, args);

    public static GitResult Run(string? workingDirectory, int timeoutMs, params string[] args)
    {
        var result = new GitResult();
        var psi = new ProcessStartInfo
        {
            FileName = GitExecutable,
            Arguments = BuildArguments(args),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
            {
                result.Launched = false;
                return result;
            }

            // Read both streams off-thread so a full pipe can never deadlock us.
            var outTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var errTask = Task.Run(() => proc.StandardError.ReadToEnd());

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(); } catch { /* already gone */ }
                result.TimedOut = true;
            }

            Task.WaitAll(new Task[] { outTask, errTask }, 2000);
            result.StdOut = outTask.Status == TaskStatus.RanToCompletion ? outTask.Result : "";
            result.StdErr = errTask.Status == TaskStatus.RanToCompletion ? errTask.Result : "";
            result.ExitCode = result.TimedOut ? -1 : SafeExitCode(proc);
        }
        catch (Exception ex)
        {
            // Most commonly: git.exe not found on PATH.
            result.Launched = false;
            result.StdErr = ex.GetBaseException().Message;
        }

        return result;
    }

    private static int SafeExitCode(Process proc)
    {
        try { return proc.ExitCode; } catch { return -1; }
    }

    /// <summary>
    /// Build a Windows command line from individual arguments using the MSVCRT
    /// quoting rules (ProcessStartInfo.ArgumentList is unavailable on net48/net462).
    /// </summary>
    internal static string BuildArguments(string[] args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            AppendQuoted(sb, a ?? string.Empty);
        }
        return sb.ToString();
    }

    private static void AppendQuoted(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '\\' }) < 0)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
    }
}
