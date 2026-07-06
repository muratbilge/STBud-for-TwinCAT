using STFormatter.Core.Formatting;

namespace STBud.Git.Tests;

// Synthetic TwinCAT XML only — generic POU names, no real project data.
public class StExtractorTests
{
    private const string SamplePou =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<TcPlcObject Version=\"1.1.0.1\">\r\n" +
        "  <POU Name=\"MAIN\">\r\n" +
        "    <Declaration><![CDATA[PROGRAM MAIN\r\nVAR\r\n    a : INT;\r\nEND_VAR]]></Declaration>\r\n" +
        "    <Implementation>\r\n" +
        "      <ST><![CDATA[a := a + 1;]]></ST>\r\n" +
        "    </Implementation>\r\n" +
        "  </POU>\r\n" +
        "</TcPlcObject>";

    [Fact]
    public void Extract_pulls_declaration_and_implementation_st()
    {
        var st = TwinCatStExtractor.Extract(SamplePou);

        Assert.False(st.IsEmpty);
        Assert.NotNull(st.Declaration);
        Assert.Contains("PROGRAM MAIN", st.Declaration!);
        Assert.Contains("a : INT;", st.Declaration!);
        Assert.NotNull(st.Implementation);
        Assert.Equal("a := a + 1;", st.Implementation!.Trim());

        string combined = st.Combined();
        Assert.Contains("PROGRAM MAIN", combined);
        Assert.Contains("a := a + 1;", combined);
    }

    [Fact]
    public void Extract_on_plain_text_is_empty()
    {
        var st = TwinCatStExtractor.Extract("a := 1;");
        Assert.True(st.IsEmpty);
    }

    [Fact]
    public void ExtractCombinedOrRaw_returns_raw_for_non_xml()
    {
        Assert.Equal("a := 1;", TwinCatStExtractor.ExtractCombinedOrRaw("a := 1;"));
    }

    [Fact]
    public void ExtractCombinedOrRaw_returns_st_for_pou()
    {
        string combined = TwinCatStExtractor.ExtractCombinedOrRaw(SamplePou);
        Assert.Contains("PROGRAM MAIN", combined);
        Assert.Contains("a := a + 1;", combined);
        Assert.DoesNotContain("CDATA", combined);
        Assert.DoesNotContain("TcPlcObject", combined);
    }

    [Fact]
    public void Extract_recovers_st_from_malformed_xml()
    {
        // Unclosed tag forces the XDocument path to fail; the raw scan should still work.
        // The Declaration block must land in Declaration (not Implementation, which was
        // the ScanCData bug). This test now guards the regression.
        string broken = "<POU><Declaration><![CDATA[PROGRAM P\r\nEND_PROGRAM]]></Declaration";
        var st = TwinCatStExtractor.Extract(broken);
        Assert.False(st.IsEmpty);
        Assert.NotNull(st.Declaration);
        Assert.Contains("PROGRAM P", st.Declaration!);
        Assert.Null(st.Implementation);
    }

    [Fact]
    public void Extract_malformed_xml_classifies_declaration_not_implementation()
    {
        // Two CDATA blocks in malformed XML: one inside <Declaration>, one inside
        // <Implementation>. The raw scan must put each in the right section — the
        // pre-fix ScanCData put both in Implementation.
        string broken =
            "<POU>" +
            "<Declaration><![CDATA[PROGRAM P\r\nVAR\r\n    a : INT;\r\nEND_VAR]]></Declaration>" +
            "<Implementation><![CDATA[a := a + 1;]]></Implementation>";
        var st = TwinCatStExtractor.Extract(broken);

        Assert.NotNull(st.Declaration);
        Assert.Contains("PROGRAM P", st.Declaration!);
        Assert.NotNull(st.Implementation);
        Assert.Contains("a := a + 1;", st.Implementation!);
    }

    [Fact]
    public void Extract_concatenates_method_declarations_and_implementations_in_document_order()
    {
        // A multi-method POU: main decl/impl plus one method's decl/impl. All
        // declaration-side blocks concatenate in document order; all impl-side too.
        string multi =
            "<?xml version=\"1.0\"?>\r\n" +
            "<TcPlcObject Version=\"1.1.0.1\">\r\n" +
            "  <POU Name=\"FB\">\r\n" +
            "    <Declaration><![CDATA[FUNCTION_BLOCK FB\r\nEND_FUNCTION_BLOCK]]></Declaration>\r\n" +
            "    <Implementation><![CDATA[]]></Implementation>\r\n" +
            "    <Method Name=\"Init\">\r\n" +
            "      <Declaration><![CDATA[METHOD Init : BOOL\r\nEND_METHOD]]></Declaration>\r\n" +
            "      <Implementation><![CDATA[Init := TRUE;]]></Implementation>\r\n" +
            "    </Method>\r\n" +
            "  </POU>\r\n" +
            "</TcPlcObject>";

        var st = TwinCatStExtractor.Extract(multi);

        Assert.NotNull(st.Declaration);
        Assert.Contains("FUNCTION_BLOCK FB", st.Declaration!);
        Assert.Contains("METHOD Init : BOOL", st.Declaration!);
        // Main implementation is empty CDATA (skipped); method impl is the only impl text.
        Assert.NotNull(st.Implementation);
        Assert.Equal("Init := TRUE;", st.Implementation!.Trim());
    }
}
