using System;

namespace STBud.Git;

/// <summary>One commit as listed by <c>git log</c>.</summary>
public sealed class CommitInfo
{
    public string Sha { get; set; } = "";
    public string ShortSha { get; set; } = "";
    public string Author { get; set; } = "";
    public string DateIso { get; set; } = "";
    public string Subject { get; set; } = "";

    /// <summary>Author date parsed from the ISO-8601 string, or null if unparseable.</summary>
    public DateTimeOffset? Date =>
        DateTimeOffset.TryParse(DateIso, out var d) ? d : (DateTimeOffset?)null;

    public override string ToString() => $"{ShortSha} {Subject}";
}
