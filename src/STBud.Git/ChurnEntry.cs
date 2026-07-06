namespace STBud.Git;

/// <summary>How often a path changed across history — the hotspot metric.</summary>
public sealed class ChurnEntry
{
    public string Path { get; set; } = "";
    public int Changes { get; set; }

    public override string ToString() => $"{Changes,5}  {Path}";
}
