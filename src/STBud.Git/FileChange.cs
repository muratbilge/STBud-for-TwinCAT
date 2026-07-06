namespace STBud.Git;

public enum FileChangeKind { Added, Modified, Deleted, Renamed, Copied, TypeChanged, Unknown }

/// <summary>A single path touched by a commit (from <c>git show --name-status</c>).</summary>
public sealed class FileChange
{
    public FileChangeKind Kind { get; set; } = FileChangeKind.Unknown;
    public string Path { get; set; } = "";

    /// <summary>The previous path for renames/copies; null otherwise.</summary>
    public string? OldPath { get; set; }

    public string KindLabel => Kind switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Modified => "M",
        FileChangeKind.Deleted => "D",
        FileChangeKind.Renamed => "R",
        FileChangeKind.Copied => "C",
        FileChangeKind.TypeChanged => "T",
        _ => "?",
    };

    public override string ToString() => $"{KindLabel} {Path}";
}
