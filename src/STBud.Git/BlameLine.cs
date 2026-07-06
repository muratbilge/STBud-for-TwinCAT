namespace STBud.Git;

/// <summary>One source line attributed to the commit that last changed it.</summary>
public sealed class BlameLine
{
    public string Sha { get; set; } = "";
    public string Author { get; set; } = "";
    public int LineNumber { get; set; }
    public string Content { get; set; } = "";

    public string ShortSha => Sha.Length >= 8 ? Sha.Substring(0, 8) : Sha;

    public override string ToString() => $"{ShortSha} {LineNumber,5}  {Content}";
}
