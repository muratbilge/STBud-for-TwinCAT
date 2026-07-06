namespace STBud.Git;

/// <summary>A local branch.</summary>
public sealed class BranchInfo
{
    public string Name { get; set; } = "";
    public bool IsCurrent { get; set; }

    public override string ToString() => IsCurrent ? $"* {Name}" : $"  {Name}";
}
