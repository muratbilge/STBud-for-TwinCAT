using System.Text;
using STFormatter.Core.Configuration;
using Xunit;

namespace STFormatter.Core.Tests;

// TwinCAT files carry a UTF-8 BOM; the returned encoding must reproduce the original
// preamble on write so a format round-trip is byte-faithful outside the ST changes.
public class FileTextTests
{
    [Fact]
    public void Utf8_bom_is_reported_and_stripped_from_text()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'b' };
        var text = FileText.DecodePreservingEncoding(bytes, out var enc);

        Assert.Equal("ab", text);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, enc.GetPreamble());
    }

    [Fact]
    public void Utf16_le_bom_is_reported()
    {
        var bytes = new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 };
        var text = FileText.DecodePreservingEncoding(bytes, out var enc);

        Assert.Equal("a", text);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, enc.GetPreamble());
    }

    [Fact]
    public void Bomless_utf8_stays_bomless()
    {
        var text = FileText.DecodePreservingEncoding(Encoding.UTF8.GetBytes("abc"), out var enc);

        Assert.Equal("abc", text);
        Assert.Empty(enc.GetPreamble());
    }
}
