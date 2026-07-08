using STFormatter.Core.Configuration;
using Xunit;

namespace STFormatter.Core.Tests;

// TcXaeShell's DTE reports method/action tabs as "<file>.TcPOU;POU.Member" - a pseudo-path
// that broke Format Document ("File not found") and git repo resolution. Synthetic names.
public class DocumentPathTests
{
    [Theory]
    [InlineData(@"C:\p\FB_Sample.TcPOU;FB_Sample.FB_init", @"C:\p\FB_Sample.TcPOU")]
    [InlineData(@"C:\p\FB_Sample.TcPOU;FB_Sample.MyMethod", @"C:\p\FB_Sample.TcPOU")]
    [InlineData(@"C:\p\MAIN.TcPOU", @"C:\p\MAIN.TcPOU")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_strips_member_suffix(string? input, string expected)
    {
        Assert.Equal(expected, DocumentPath.Normalize(input));
    }
}
