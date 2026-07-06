using STFormatter.Core.Formatting;
using Xunit;

namespace STFormatter.Core.Tests;

// Function-block instance initializers in VAR declarations: `inst : FB_Type(a := 1);`.
// All fixtures synthetic (generic names) per the project privacy rule.
public class FbInstanceInitializerTests
{
    private static FormattingEngine Engine() => new FormattingEngine();

    [Fact]
    public void Inline_fb_initializer_parses_and_round_trips()
    {
        var engine = Engine();
        string decl = "VAR_GLOBAL\r\n    fb : FB_Sample(a_Init := 1, b_Init := 2);\r\nEND_VAR";

        Assert.True(engine.DeclarationParsesCleanly(decl));

        string formatted = engine.FormatDeclaration(decl);
        Assert.Contains("fb : FB_Sample(a_Init := 1, b_Init := 2);", formatted);
    }

    [Fact]
    public void Multiline_fb_initializer_with_nested_call_parses()
    {
        var engine = Engine();
        string decl =
            "VAR_GLOBAL\r\n" +
            "    fb : FB_Sample(a_Init := 1\r\n" +
            "                  ,b_Init := 2\r\n" +
            "                  ,p_Init := ADR(x));\r\n" +
            "END_VAR";

        Assert.True(engine.DeclarationParsesCleanly(decl));

        string formatted = engine.FormatDeclaration(decl);
        // Args preserved (names + nested ADR call), no data dropped.
        Assert.Contains("a_Init := 1", formatted);
        Assert.Contains("b_Init := 2", formatted);
        Assert.Contains("p_Init := ADR(x)", formatted);
        Assert.Contains("FB_Sample(", formatted);
    }

    [Fact]
    public void Fb_initializer_mixed_with_plain_and_conditional_pragma_parses()
    {
        var engine = Engine();
        // The shape that previously made the formatter refuse: FB initializers plus
        // conditional-compilation pragmas plus AT% addresses, all in one GVL.
        string decl =
            "VAR_GLOBAL\r\n" +
            "    sName : STRING(32);\r\n" +
            "    {attribute 'TcLinkTo':='TIIB[Dev (EL6695)]^In^DataIn'}\r\n" +
            "    a AT%I* : ST_Sample;\r\n" +
            "    fbCom : FB_Sample(x_Init := f1.fb, n_Init := 2, p_Init := ADR(a));\r\n" +
            "    {IF defined (OptionA)}\r\n" +
            "        fbScriber : FB_A(p_Init := ADR(g.x));\r\n" +
            "    {ELSIF defined (OptionB)}\r\n" +
            "        fbScriber : FB_B(p_Init := ADR(g.y));\r\n" +
            "    {END_IF}\r\n" +
            "END_VAR";

        Assert.True(engine.DeclarationParsesCleanly(decl));

        string formatted = engine.FormatDeclaration(decl);
        Assert.Contains("FB_Sample(x_Init := f1.fb, n_Init := 2, p_Init := ADR(a))", formatted);
        Assert.Contains("fbScriber : FB_A(p_Init := ADR(g.x));", formatted);
    }

    [Fact]
    public void Gvl_with_fb_initializers_is_classified_as_declaration()
    {
        // The ':=' in FB initializers must NOT make a GVL look like an implementation
        // body — otherwise it gets routed to FormatBody and left unformatted.
        string decl =
            "VAR_GLOBAL\r\n" +
            "    {attribute 'TcLinkTo':='TIIB[Dev]^In^DataIn'}\r\n" +
            "    a AT%I* : ST_Sample;\r\n" +
            "    fb : FB_Sample(a_Init := 1, p_Init := ADR(a));\r\n" +
            "END_VAR";
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(decl));
    }

    [Fact]
    public void Implementation_body_is_not_classified_as_declaration()
    {
        string body = "a := 1;\r\nIF b THEN\r\n    c := fb.Run(x := 2);\r\nEND_IF";
        Assert.False(TwinCatXmlFormatter.LooksLikeDeclaration(body));
    }

    [Fact]
    public void Fb_initializer_is_idempotent()
    {
        var engine = Engine();
        string decl = "VAR_GLOBAL\r\n    fb : FB_Sample(a_Init := 1, b_Init := 2);\r\nEND_VAR";

        string once = engine.FormatDeclaration(decl);
        string twice = engine.FormatDeclaration(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Fb_initializer_in_full_program_round_trips()
    {
        var engine = Engine();
        string src =
            "FUNCTION_BLOCK FB_Outer\r\n" +
            "VAR\r\n" +
            "    inner : FB_Inner(p_Init := ADR(buf), n_Init := 16);\r\n" +
            "END_VAR\r\n" +
            "inner();\r\n" +
            "END_FUNCTION_BLOCK";

        Assert.True(TwinCatXmlFormatter.ParsesWithoutErrors(src));
        string formatted = engine.Format(src);
        Assert.Contains("inner : FB_Inner(p_Init := ADR(buf), n_Init := 16);", formatted);
    }
}
