using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// Disk-write restore fallback: replace a working block with the committed block inside the
// CDATA of a specific section. Synthetic XML (generic names) per the project privacy rule.
public class CdataSectionReplaceTests
{
    private const string Pou =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<TcPlcObject Version=\"1.1.0.1\">\r\n" +
        "  <POU Name=\"MAIN\">\r\n" +
        "    <Declaration><![CDATA[PROGRAM MAIN\r\nVAR\r\n    a : INT;\r\nEND_VAR]]></Declaration>\r\n" +
        "    <Implementation>\r\n" +
        "      <ST><![CDATA[a := 1;]]></ST>\r\n" +
        "    </Implementation>\r\n" +
        "  </POU>\r\n" +
        "</TcPlcObject>";

    [Fact]
    public void Replaces_in_declaration_only()
    {
        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            Pou, declaration: true, working: "    a : INT;", committed: "    a : DINT;", out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r);
        Assert.Contains("a : DINT;", newXml);
        Assert.DoesNotContain("a : INT;", newXml);
        Assert.Contains("a := 1;", newXml); // implementation untouched
    }

    [Fact]
    public void Replaces_in_implementation_only()
    {
        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            Pou, declaration: false, working: "a := 1;", committed: "a := 2;", out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r);
        Assert.Contains("a := 2;", newXml);
        Assert.Contains("a : INT;", newXml); // declaration untouched
    }

    [Fact]
    public void Section_isolation_block_only_in_other_section_is_not_found()
    {
        // The implementation line, requested in the declaration section, must NOT match.
        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            Pou, declaration: true, working: "a := 1;", committed: "a := 2;", out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.NotFound, r);
        Assert.Equal(Pou, newXml);
    }

    [Fact]
    public void Missing_block_returns_not_found()
    {
        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            Pou, declaration: false, working: "z := 9;", committed: "z := 0;", out _);
        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.NotFound, r);
    }

    [Fact]
    public void Duplicate_block_across_two_cdata_is_ambiguous()
    {
        // Same implementation line in the main ST and a method's ST → refuse (ambiguous).
        string multi =
            "<TcPlcObject>\r\n" +
            "  <POU Name=\"FB\">\r\n" +
            "    <Implementation><ST><![CDATA[x := 1;]]></ST></Implementation>\r\n" +
            "    <Method Name=\"M\"><Implementation><ST><![CDATA[x := 1;]]></ST></Implementation></Method>\r\n" +
            "  </POU>\r\n" +
            "</TcPlcObject>";

        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            multi, declaration: false, working: "x := 1;", committed: "x := 2;", out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Ambiguous, r);
        Assert.Equal(multi, newXml); // nothing written
    }

    [Fact]
    public void Empty_committed_deletes_the_located_line()
    {
        // Accepting HEAD on a line that's new in the working file → committed is empty →
        // the line must be removed entirely (no blank-line residue).
        const string impl =
            "<POU>\r\n" +
            "  <Implementation><ST><![CDATA[a := 1;\r\nNEW := 9;\r\nb := 2;]]></ST></Implementation>\r\n" +
            "</POU>";

        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            impl, declaration: false, working: "NEW := 9;", committed: "", out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r);
        Assert.DoesNotContain("NEW := 9;", newXml);
        Assert.Contains("a := 1;", newXml);
        Assert.Contains("b := 2;", newXml);
        // The two surviving lines are now adjacent — no empty line left behind.
        Assert.Contains("a := 1;\r\nb := 2;", newXml);
    }

    [Fact]
    public void Sequential_block_replaces_across_pou_and_method_both_land()
    {
        // The staged-accept "Save" applies one block at a time to the same in-memory XML. A POU
        // with a method has two Implementation CDATAs; replacing a block in each in sequence must
        // both succeed (this is the multi-CDATA case the whole-section save used to miss).
        const string xml =
            "<TcPlcObject>\r\n" +
            "  <POU Name=\"MAIN\">\r\n" +
            "    <Implementation><ST><![CDATA[a := 1;\r\nb := 2;]]></ST></Implementation>\r\n" +
            "    <Method Name=\"M\"><Implementation><ST><![CDATA[c := 3;]]></ST></Implementation></Method>\r\n" +
            "  </POU>\r\n" +
            "</TcPlcObject>";

        var r1 = TwinCatXmlFormatter.ReplaceStBlockInSection(
            xml, declaration: false, working: "a := 1;", committed: "a := 9;", out string xml1);
        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r1);

        var r2 = TwinCatXmlFormatter.ReplaceStBlockInSection(
            xml1, declaration: false, working: "c := 3;", committed: "c := 7;", out string xml2);
        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r2);

        Assert.Contains("a := 9;", xml2);
        Assert.Contains("b := 2;", xml2);
        Assert.Contains("c := 7;", xml2);
        Assert.DoesNotContain("a := 1;", xml2);
        Assert.DoesNotContain("c := 3;", xml2);
    }

    [Fact]
    public void Multiline_block_replacement_preserves_surroundings()
    {
        var r = TwinCatXmlFormatter.ReplaceStBlockInSection(
            Pou, declaration: true,
            working: "VAR\r\n    a : INT;\r\nEND_VAR",
            committed: "VAR\r\n    a : DINT;\r\n    b : BOOL;\r\nEND_VAR",
            out string newXml);

        Assert.Equal(TwinCatXmlFormatter.StReplaceResult.Replaced, r);
        Assert.Contains("b : BOOL;", newXml);
        Assert.Contains("PROGRAM MAIN", newXml);   // line before the block kept
        Assert.Contains("a := 1;", newXml);        // impl kept
    }
}
