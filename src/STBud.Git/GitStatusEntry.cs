namespace STBud.Git;

/// <summary>A working-tree change from <c>git status --porcelain</c>.</summary>
public sealed class GitStatusEntry
{
    /// <summary>Index (staged) status char: M/A/D/R/C/?/space.</summary>
    public char IndexStatus { get; set; } = ' ';

    /// <summary>Work-tree (unstaged) status char.</summary>
    public char WorkTreeStatus { get; set; } = ' ';

    public string Path { get; set; } = "";
    public string? OldPath { get; set; }

    public bool IsStaged => IndexStatus != ' ' && IndexStatus != '?';
    public bool IsUntracked => IndexStatus == '?' && WorkTreeStatus == '?';
    public bool HasUnstagedChanges => WorkTreeStatus != ' ' && WorkTreeStatus != '?';

    public string StateLabel =>
        IsUntracked ? "untracked"
        : $"{(IsStaged ? "staged" : "")}{(IsStaged && HasUnstagedChanges ? "+" : "")}{(HasUnstagedChanges ? "modified" : "")}";

    public override string ToString() => $"{IndexStatus}{WorkTreeStatus} {Path}";
}
