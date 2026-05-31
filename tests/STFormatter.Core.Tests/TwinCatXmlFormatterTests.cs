using System.Xml.Linq;
using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class TwinCatXmlFormatterTests
{
    [Fact]
    public void FormatXmlContent_Formats_Declaration_And_Implementation()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""MainProgram"">
    <Declaration><![CDATA[PROGRAM MainProgram
VAR
counter:INT:=0;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[counter:=counter+1;
]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        var formatter = new TwinCatXmlFormatter();
        bool changed = formatter.FormatXmlContent(xml, out var result, out var decl, out var impl);

        Assert.True(changed);
        Assert.NotNull(decl);
        Assert.NotNull(impl);
        Assert.Contains("counter : INT := 0;", decl);
    }

    [Fact]
    public void FormatXmlContent_ReturnsUnchanged_WhenAlreadyFormatted()
    {
        var engine = new FormattingEngine();
        var preFormatted = engine.Format("PROGRAM X\nVAR\ny : INT;\nEND_VAR\nEND_PROGRAM");
        var xml = $"<Root><Declaration><![CDATA[{preFormatted}]]></Declaration></Root>";
        var formatter = new TwinCatXmlFormatter();
        bool changed = formatter.FormatXmlContent(xml, out var result, out _, out _);

        Assert.False(changed);
    }

    [Fact]
    public void FormatXDocument_Formats_Declaration_CData()
    {
        var xml = @"<TcPlcObject><POU Name=""Test"">
<Declaration><![CDATA[PROGRAM Test
VAR
x:INT;
END_VAR
END_PROGRAM
]]></Declaration>
</POU></TcPlcObject>";

        var doc = XDocument.Parse(xml);
        var formatter = new TwinCatXmlFormatter();
        bool modified = formatter.FormatXDocument(doc);

        Assert.True(modified);
        var cdata = doc.Descendants("Declaration").First().Nodes().OfType<XCData>().First();
        Assert.Contains("x : INT;", cdata.Value);
    }

    [Fact]
    public void FormatXDocument_Skips_Empty_CData()
    {
        var xml = @"<TcPlcObject><POU Name=""Test"">
<Declaration><![CDATA[  ]]></Declaration>
</POU></TcPlcObject>";

        var doc = XDocument.Parse(xml);
        var formatter = new TwinCatXmlFormatter();
        bool modified = formatter.FormatXDocument(doc);

        Assert.False(modified);
    }

    [Fact]
    public void LooksLikeDeclaration_Detects_VarSection()
    {
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration("VAR\nx : INT;\nEND_VAR"));
        Assert.False(TwinCatXmlFormatter.LooksLikeDeclaration("IF x > 0 THEN\ny := 1;\nEND_IF"));
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration("PROGRAM Test\nVAR\nx : INT;\nEND_VAR\nEND_PROGRAM"));
    }

    [Fact]
    public void LooksLikeDeclaration_HandlesEmpty()
    {
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(""));
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(null!));
    }

    [Fact]
    public void FindParentElement_FindsParentTag()
    {
        var xml = "<Declaration><![CDATA[stuff]]></Declaration>";
        int cdataPos = xml.IndexOf("<![CDATA[", StringComparison.Ordinal);
        var parent = TwinCatXmlFormatter.FindParentElement(xml, cdataPos);
        Assert.Contains("Declaration", parent);
    }
}