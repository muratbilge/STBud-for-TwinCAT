using STFormatter.Core.Toolbox;

namespace STFormatter.Core.Tests;

public class PragmaTemplatesTests
{
    [Fact]
    public void Attribute_NameOnly()
    {
        Assert.Equal("{attribute 'hide'}", PragmaTemplates.Attribute("hide"));
    }

    [Fact]
    public void Attribute_NameAndValue()
    {
        Assert.Equal("{attribute 'TcLinkTo' := 'TIID^Device 1^Term 2^Channel 1'}",
            PragmaTemplates.Attribute("TcLinkTo", "TIID^Device 1^Term 2^Channel 1"));
        Assert.Equal("{attribute 'call_after' := 'MAIN'}",
            PragmaTemplates.Attribute("call_after", "MAIN"));
        Assert.Equal("{attribute 'priority' := '1'}",
            PragmaTemplates.Attribute("priority", "1"));
    }

    [Fact]
    public void Warning_WrapsMessage()
    {
        Assert.Equal("{warning 'do not use'}", PragmaTemplates.Warning("do not use"));
    }

    [Fact]
    public void RegionStart_WrapsName()
    {
        Assert.Equal("{region 'Init'}", PragmaTemplates.RegionStart("Init"));
    }

    [Fact]
    public void RegionBlock_HasStartBlankLinesAndEnd()
    {
        Assert.Equal("{region 'Init'}\r\n\r\n\r\n{endregion}", PragmaTemplates.RegionBlock("Init"));
    }

    [Fact]
    public void EndRegion_Literal()
    {
        Assert.Equal("{endregion}", PragmaTemplates.EndRegion);
    }

    [Fact]
    public void WrapMenuPragma_BareNameIsWrappedAsAttribute()
    {
        Assert.Equal("{attribute 'qualified_only'}", PragmaTemplates.WrapMenuPragma("qualified_only"));
        Assert.Equal("{attribute 'linkalways'}", PragmaTemplates.WrapMenuPragma("linkalways"));
    }

    [Fact]
    public void WrapMenuPragma_CompletePragmaPassesThrough()
    {
        Assert.Equal("{endregion}", PragmaTemplates.WrapMenuPragma("{endregion}"));
        Assert.Equal("{attribute 'hide'}", PragmaTemplates.WrapMenuPragma("{attribute 'hide'}"));
    }
}
