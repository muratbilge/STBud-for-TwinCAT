using System.Xml.Linq;
using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class TwinCatXmlTests
{
    [Fact]
    public void Formats_TcPOU_Declaration_And_Implementation()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""MainProgram"" Id=""{a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d}"">
    <Declaration><![CDATA[PROGRAM MainProgram
VAR
counter:INT:=0;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[PROGRAM MainProgram
IF running THEN
counter:=counter+1;
END_IF;
END_PROGRAM
]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        var doc = XDocument.Parse(xml);
        var engine = new FormattingEngine();

        // Format Declaration
        var declaration = doc.Descendants("Declaration").FirstOrDefault();
        Assert.NotNull(declaration);
        var declCdata = declaration.Nodes().OfType<XCData>().FirstOrDefault();
        Assert.NotNull(declCdata);

        var formattedDecl = engine.Format(declCdata.Value);
        declCdata.Value = formattedDecl;

        Assert.Contains("PROGRAM MainProgram", formattedDecl);
        Assert.Contains("    counter : INT := 0;", formattedDecl);

        // Format Implementation ST
        var st = doc.Descendants("ST").FirstOrDefault();
        Assert.NotNull(st);
        var stCdata = st.Nodes().OfType<XCData>().FirstOrDefault();
        Assert.NotNull(stCdata);

        var formattedSt = engine.Format(stCdata.Value);
        stCdata.Value = formattedSt;

        Assert.Contains("IF running THEN", formattedSt);
        Assert.Contains("    counter := counter + 1;", formattedSt);
    }

    [Fact]
    public void Formats_TcDUT_Declaration()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <DUT Name=""MyStruct"" Id=""{b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e}"">
    <Declaration><![CDATA[TYPE MyStruct :
STRUCT
x:INT;
y:REAL;
END_STRUCT
END_TYPE
]]></Declaration>
  </DUT>
</TcPlcObject>";

        var doc = XDocument.Parse(xml);
        var engine = new FormattingEngine();

        var declaration = doc.Descendants("Declaration").FirstOrDefault();
        Assert.NotNull(declaration);
        var cdata = declaration.Nodes().OfType<XCData>().FirstOrDefault();
        Assert.NotNull(cdata);

        var formatted = engine.Format(cdata.Value);
        cdata.Value = formatted;

        Assert.Contains("TYPE", formatted);
        Assert.Contains("MyStruct :", formatted);
        Assert.Contains("    x : INT;", formatted);
    }
}
