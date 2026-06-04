using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;

namespace STFormatter.Core.Tests;

public class EditorConfigPatternTests
{
    [Fact]
    public void MatchesPattern_Star_MatchesEverything()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*", "bar.txt"));
    }

    [Fact]
    public void MatchesPattern_Extension_MatchesFileName()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.st", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.st", "MyProgram.st"));
        Assert.False(EditorConfigParser.MatchesPattern("*.st", "foo.txt"));
    }

    [Fact]
    public void MatchesPattern_CommaSeparated()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.st, *.iecst", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.st, *.iecst", "bar.iecst"));
        Assert.False(EditorConfigParser.MatchesPattern("*.st, *.iecst", "foo.txt"));
    }

    [Fact]
    public void MatchesPattern_QuestionMark()
    {
        Assert.True(EditorConfigParser.MatchesPattern("?.st", "a.st"));
        Assert.False(EditorConfigParser.MatchesPattern("?.st", "ab.st"));
    }

    [Fact]
    public void MatchesPattern_BracketSet()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.[st]", "foo.s"));
        Assert.True(EditorConfigParser.MatchesPattern("*.[st]", "foo.t"));
        Assert.False(EditorConfigParser.MatchesPattern("*.[st]", "foo.x"));
    }

    [Fact]
    public void MatchesPattern_BracesAlternation()
    {
        Assert.True(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.st"));
        Assert.True(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.iecst"));
        Assert.False(EditorConfigParser.MatchesPattern("*.{st,iecst}", "foo.txt"));
    }
}

public class ConfigurationPropertyTests
{
    [Fact]
    public void KeepSingleLineBlocks_KeepsSingleLineIf()
    {
        var config = FormattingConfiguration.CompactPreset;
        Assert.True(config.KeepSingleLineBlocks);

        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nIF x > 0 THEN y := 1; END_IF\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("y := 1;", result);
    }

    [Fact]
    public void KeepSingleLineBlocks_Disabled_MultiLineByDefault()
    {
        var config = FormattingConfiguration.Default;
        Assert.False(config.KeepSingleLineBlocks);

        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nIF x > 0 THEN y := 1; END_IF\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.NotNull(result);
    }

    [Fact]
    public void SpaceAfterComma_ControlsArgumentSpacing()
    {
        var configWithSpace = new FormattingConfiguration { SpaceAfterComma = true };
        var engineWithSpace = new FormattingEngine(configWithSpace);
        var source = "PROGRAM Test\nVAR\nx:INT;\nEND_VAR\nFmt(a, b, c);\nEND_PROGRAM";
        var resultWithSpace = engineWithSpace.Format(source);
        Assert.Contains("Fmt(a, b, c)", resultWithSpace);

        var configNoSpace = new FormattingConfiguration { SpaceAfterComma = false };
        var engineNoSpace = new FormattingEngine(configNoSpace);
        var resultNoSpace = engineNoSpace.Format(source);
        Assert.Contains("Fmt(a,b,c)", resultNoSpace);
    }

    [Fact]
    public void SpaceBeforeSemicolon_AddsSpace()
    {
        var config = new FormattingConfiguration { SpaceBeforeSemicolon = true };
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nVAR\nx:INT;\nEND_VAR\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains(" ;", result);
    }

    [Fact]
    public void FormatBody_ExtractsBodyFromTree()
    {
        var engine = new FormattingEngine();
        var body = "IF x > 0 THEN\ny := 1;\nEND_IF";
        var result = engine.FormatBody(body);
        Assert.Contains("IF x > 0 THEN", result);
        Assert.Contains("y := 1;", result);
    }

    [Fact]
    public void FormatBody_PreservesCrlf()
    {
        var engine = new FormattingEngine();
        var body = "IF x > 0 THEN\r\ny := 1;\r\nEND_IF";
        var result = engine.FormatBody(body);
        Assert.Contains("\r\n", result);
    }

    [Fact]
    public void ContinuationIndentSize_UsedInLineWrapping()
    {
        var config = new FormattingConfiguration
        {
            MaxLineLength = 30,
            ContinuationIndentSize = 8,
            IndentSize = 4
        };
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nx := verylongexpression + anotherverylongvalue;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.NotNull(result);
    }

    [Fact]
    public void BraceStyle_Allman_IsDefault()
    {
        var config = FormattingConfiguration.Default;
        Assert.Equal("allman", config.BraceStyle);
        Assert.True(config.IsAllmanStyle());
        Assert.False(config.IsCompactStyle());
    }

    [Fact]
    public void BraceStyle_CompactPreset_IsCompact()
    {
        var config = FormattingConfiguration.CompactPreset;
        Assert.Equal("compact", config.BraceStyle);
        Assert.False(config.IsAllmanStyle());
        Assert.True(config.IsCompactStyle());
    }

    [Fact]
    public void BraceStyle_Allman_BlankLinesBetweenVarSections()
    {
        var config = new FormattingConfiguration { EmptyLinesBetweenVarSections = 2 };
        var engine = new FormattingEngine(config);
        var source = "FUNCTION_BLOCK Test\nVAR_INPUT\nx:INT;\nEND_VAR\nVAR_OUTPUT\ny:INT;\nEND_VAR\nEND_FUNCTION_BLOCK";
        var result = engine.Format(source);
        Assert.Contains("END_VAR", result);
        Assert.Contains("VAR_INPUT", result);
        Assert.Contains("VAR_OUTPUT", result);
    }

    [Fact]
    public void BraceStyle_Compact_NoExtraBlankLinesBetweenVarSections()
    {
        var config = FormattingConfiguration.CompactPreset;
        var engine = new FormattingEngine(config);
        var source = "FUNCTION_BLOCK Test\nVAR_INPUT\nx:INT;\nEND_VAR\nVAR_OUTPUT\ny:INT;\nEND_VAR\nEND_FUNCTION_BLOCK";
        var result = engine.Format(source);
        Assert.Contains("end_var", result.ToLowerInvariant());
        Assert.DoesNotContain("\r\n\r\n\r\n", result);
    }

    [Fact]
    public void BraceStyle_Allman_HasSeparateLinesForStructure()
    {
        var config = FormattingConfiguration.Default;
        var engine = new FormattingEngine(config);
        var source = "PROGRAM Test\nVAR\nx:INT;\ny:DINT;\nEND_VAR\nx := 1;\ny := 2;\nEND_PROGRAM";
        var allmanResult = engine.Format(source);
        Assert.Contains("END_VAR", allmanResult);
    }

    [Fact]
    public void BraceStyle_Compact_IsMoreCompactThanAllman()
    {
        var allmanConfig = FormattingConfiguration.Default;
        var compactConfig = FormattingConfiguration.CompactPreset;
        var source = "PROGRAM Test\nVAR\nx:INT;\ny:DINT;\nEND_VAR\nx := 1;\ny := 2;\nEND_PROGRAM";
        var allman = new FormattingEngine(allmanConfig).Format(source);
        var compact = new FormattingEngine(compactConfig).Format(source);
        Assert.True(compact.Length <= allman.Length, "Compact should produce equal or shorter output");
    }

    [Fact]
    public void Formatter_Handles_UnionType()
    {
        var engine = new FormattingEngine();
        var source = "TYPE MyUnion :\nUNION\nasInt:DINT;\nasReal:REAL;\nEND_UNION\nEND_TYPE";
        var result = engine.Format(source);
        Assert.Contains("UNION", result);
        Assert.Contains("END_UNION", result);
        Assert.Contains("END_TYPE", result);
    }

    [Fact]
    public void Formatter_Handles_UsingDirective()
    {
        var engine = new FormattingEngine();
        var source = "USING MyLibrary;\nPROGRAM Test\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("USING", result.ToUpperInvariant());
    }

    [Fact]
    public void Formatter_Handles_LabelStatement()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nRetry:\nx := x + 1;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("Retry", result);
        Assert.Contains("x := x + 1", result);
    }

    [Fact]
    public void Formatter_Handles_GotoStatement()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nIF x > 10 THEN\nGOTO Skip;\nEND_IF\ny := 2;\nSkip:\ny := 3;\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("GOTO", result.ToUpperInvariant());
        Assert.Contains("Skip", result);
    }

    [Fact]
    public void FormatDeclaration_WithPouHeader_NoEndKeyword()
    {
        var engine = new FormattingEngine();
        var decl = "FUNCTION_BLOCK POU_1\nVAR_INPUT\nEND_VAR\nVAR_OUTPUT\nEND_VAR\nVAR\naaa: BOOL;\nbbb: BOOL;\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        Assert.Contains("FUNCTION_BLOCK POU_1", result);
        Assert.Contains("VAR_INPUT", result);
        Assert.DoesNotContain("END_FUNCTION_BLOCK", result);

        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal("FUNCTION_BLOCK POU_1", lines[0]);
        Assert.Equal("VAR_INPUT", lines[1]);
        Assert.Equal("END_VAR", lines[2]);
    }

    [Fact]
    public void FormatDeclaration_VarSectionsAtColumnZero()
    {
        var engine = new FormattingEngine();
        var decl = "FUNCTION_BLOCK POU_1\nVAR_INPUT\nEND_VAR\nVAR_OUTPUT\nEND_VAR\nVAR\naaa: BOOL;\nbbb: BOOL;\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.StartsWith("VAR") || line.StartsWith("END_VAR"))
            {
                Assert.False(line.StartsWith(" ") || line.StartsWith("\t"),
                    $"VAR/END_VAR line should not be indented: '{line}'");
            }
        }
    }

    [Fact]
    public void FormatDeclaration_EmptyVarSections_NoExtraSpacing()
    {
        var engine = new FormattingEngine();
        var decl = "FUNCTION_BLOCK POU_1\nVAR_INPUT\nEND_VAR\nVAR_OUTPUT\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        Assert.Contains("VAR_INPUT", result);
        Assert.Contains("END_VAR", result);
        Assert.DoesNotContain("END_FUNCTION_BLOCK", result);
    }

    [Fact]
    public void FormatDeclaration_BlankLinesBetweenVarSections()
    {
        var config = FormattingConfiguration.Default;
        var engine = new FormattingEngine(config);
        var decl = "FUNCTION_BLOCK POU_1\nVAR_INPUT\nEND_VAR\nVAR_OUTPUT\nEND_VAR\nVAR\naaa: BOOL;\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        Assert.Contains("FUNCTION_BLOCK POU_1", result);
        Assert.Contains("VAR_INPUT", result);
        Assert.Contains("VAR_OUTPUT", result);
        Assert.DoesNotContain("END_FUNCTION_BLOCK", result);
    }

    [Fact]
    public void FormatDeclaration_WithoutPouHeader_JustVarSections()
    {
        var engine = new FormattingEngine();
        var decl = "VAR_INPUT\nx: INT;\nEND_VAR\nVAR\ny: BOOL;\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        Assert.Contains("VAR_INPUT", result);
        Assert.Contains("END_VAR", result);
        Assert.DoesNotContain("FUNCTION_BLOCK", result);
    }

    [Fact]
    public void FormatDeclaration_PreservesCrlf()
    {
        var engine = new FormattingEngine();
        var decl = "FUNCTION_BLOCK POU_1\r\nVAR_INPUT\r\nx: INT;\r\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        Assert.Contains("\r\n", result);
    }

    [Fact]
    public void FormatDeclaration_ExactOutput_MixedVarSections()
    {
        var engine = new FormattingEngine();
        var decl = "FUNCTION_BLOCK POU_1\nVAR_INPUT\nEND_VAR\nVAR_OUTPUT\nEND_VAR\nVAR\n\taaa: BOOL;\n\tbbb: BOOL;\nEND_VAR";
        var result = engine.FormatDeclaration(decl);
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        Assert.Equal("FUNCTION_BLOCK POU_1", lines[0]);
        Assert.Equal("VAR_INPUT", lines[1]);
        Assert.Equal("END_VAR", lines[2]);
        Assert.Equal("", lines[3]);
        Assert.Equal("VAR_OUTPUT", lines[4]);
        Assert.Equal("END_VAR", lines[5]);
        Assert.Equal("", lines[6]);
        Assert.Equal("VAR", lines[7]);
        Assert.Equal("    aaa : BOOL;", lines[8]);
        Assert.Equal("    bbb : BOOL;", lines[9]);
        Assert.Equal("END_VAR", lines[10]);
    }

    [Fact]
    public void Formatter_OutputArgument_NoValue()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nfbtest(in := 112.1, out =>);\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("=>", result);
        Assert.DoesNotContain("= >", result);
    }

    [Fact]
    public void Formatter_OutputArgument_WithValue()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nfbtest(in := 112.1, out => myVar);\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("=>", result);
        Assert.DoesNotContain("= >", result);
        Assert.Contains("myVar", result);
    }

    [Fact]
    public void Formatter_MethodWithReturnType()
    {
        var engine = new FormattingEngine();
        var source = "METHOD FB_init : BOOL\nVAR_INPUT\nx:INT;\nEND_VAR\nEND_METHOD";
        var result = engine.Format(source);
        Assert.Contains("METHOD", result);
        Assert.Contains("FB_init", result);
        Assert.Contains(":", result);
        Assert.Contains("BOOL", result);
    }

    [Fact]
    public void Formatter_SingleLineComment_NotGluedToNextLine()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nVAR\nx:INT; // comment\ny:INT;\nEND_VAR\nEND_PROGRAM";
        var result = engine.Format(source);
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool foundCommentLine = false;
        bool nextLineIsSeparate = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("// comment"))
            {
                foundCommentLine = true;
                if (i + 1 < lines.Length)
                {
                    nextLineIsSeparate = !lines[i + 1].StartsWith("//");
                }
                break;
            }
        }
        Assert.True(foundCommentLine, "Comment should be in output");
        Assert.True(nextLineIsSeparate, "Next declaration should be on a separate line");
    }

    [Fact]
    public void Formatter_MultiLineComment_NotGluedToNextLine()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM Test\nVAR\n(* block comment *)\ny:INT;\nEND_VAR\nEND_PROGRAM";
        var result = engine.Format(source);
        Assert.Contains("(* block comment *)", result);
        Assert.DoesNotContain("*)y", result);
    }

    [Fact]
    public void LooksLikeDeclaration_BareVariableLines_Detected()
    {
        var text = "    Start         : BOOL;\r\n    Stop          : BOOL;\r\n    Fault         : BOOL;\r\n    InternalLatch : BOOL;";
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(text));
    }

    [Fact]
    public void LooksLikeDeclaration_BareVarLinesWithTwoPerLine_Detected()
    {
        var text = "    Fault         : BOOL;    InternalLatch : BOOL;\r\n    Running       : BOOL;";
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(text));
    }

    [Fact]
    public void LooksLikeDeclaration_ImplementationBody_NotDetected()
    {
        var text = "IF Start AND NOT Stop THEN\r\n    InternalLatch := TRUE;\r\nEND_IF";
        Assert.False(TwinCatXmlFormatter.LooksLikeDeclaration(text));
    }

    [Fact]
    public void LooksLikeDeclaration_EmptyText_IsDeclaration()
    {
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(""));
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(null));
    }

    [Fact]
    public void FormatDeclaration_BareVarLines_FormatsCorrectly()
    {
        var engine = new FormattingEngine();
        var source = "    Start         : BOOL;\r\n    Stop          : BOOL;\r\n    Fault         : BOOL;\r\n    InternalLatch : BOOL;";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.DoesNotContain("VAR_INPUT", result);
        Assert.DoesNotContain("END_VAR", result);
        Assert.Contains("Start", result);
        Assert.Contains("BOOL", result);
    }

    [Fact]
    public void FormatDeclaration_BareVarLinesWithMixedTypes_FormatsCorrectly()
    {
        var engine = new FormattingEngine();
        var source = "    TON_Fill : TON;\r\n    LevelHigh : BOOL;\r\n    Mixer     : BOOL;\r\n    TON_Mix : TON;";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.DoesNotContain("VAR_INPUT", result);
        Assert.DoesNotContain("END_VAR", result);
        Assert.Contains("TON_Fill", result);
        Assert.Contains("BOOL", result);
    }

    [Fact]
    public void LooksLikeDeclaration_VarGlobalWithAttributePragmas_Detected()
    {
        var text = "VAR_GLOBAL\r\n\t{attribute 'task_name' := 'PlcTask'}\r\n{attribute 'TcLinkTo' := 'TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL2522)^PTO Target Channel 1^Target counter value'}\r\n\ttestrr AT %I* :UDINT;\r\nEND_VAR";
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(text));
    }

    [Fact]
    public void LooksLikeDeclaration_VarGlobalWithAttributeQualifiedOnly_Detected()
    {
        var text = "{attribute 'qualified_only'}\r\nVAR_GLOBAL\r\n\tafff\t\t\t\t\tAT \t%I* :BOOL;\r\nEND_VAR";
        Assert.True(TwinCatXmlFormatter.LooksLikeDeclaration(text));
    }

    [Fact]
    public void FormatDeclaration_VarGlobalWithAttribute_AtClause()
    {
        var engine = new FormattingEngine();
        var source = "{attribute 'qualified_only'}\r\nVAR_GLOBAL\r\n\tafff\t\t\t\t\tAT \t%I* :BOOL;\r\nEND_VAR";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("afff", result);
        Assert.Contains("AT", result);
        Assert.Contains("%I*", result);
        Assert.Contains("BOOL", result);
        Assert.Contains("VAR_GLOBAL", result);
        Assert.Contains("END_VAR", result);
    }

    [Fact]
    public void FormatDeclaration_VarGlobalWithNestedAttributes()
    {
        var engine = new FormattingEngine();
        var source = "VAR_GLOBAL\r\n\t{attribute 'task_name' := 'PlcTask'}\r\n{attribute 'TcLinkTo' := 'TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL2522)^PTO Target Channel 1^Target counter value'}\r\n\ttestrr AT %I* :UDINT;\r\nEND_VAR";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("testrr", result);
        Assert.Contains("AT", result);
        Assert.Contains("UDINT", result);
        Assert.Contains("VAR_GLOBAL", result);
        Assert.Contains("END_VAR", result);
    }

    [Fact]
    public void FormatDeclaration_ProgramMainEmptyVar_()
    {
        var engine = new FormattingEngine();
        var source = "PROGRAM MAIN\r\nVAR\r\nEND_VAR\r\n";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("PROGRAM MAIN", result);
        Assert.Contains("VAR", result);
        Assert.Contains("END_VAR", result);
    }

    [Fact]
    public void FormatBody_VarSectionOnly_FallsBackToOriginal()
    {
        var engine = new FormattingEngine();
        var source = "VAR_OUTPUT\r\nEND_VAR\r\nVAR\r\nEND_VAR\r\n";
        var result = engine.FormatBody(source);
        Assert.NotNull(result);
        Assert.True(result.Length > 0, $"FormatBody returned empty for VAR-section-only text. Expected fallback to original: '{source}'");
    }

    [Fact]
    public void Format_FunctionBlockWithVarSections_FormatsDeclaration()
    {
        var engine = new FormattingEngine();
        var source = "FUNCTION_BLOCK POU_1\r\nVAR_INPUT\r\nEND_VAR\r\nVAR_OUTPUT\r\nEND_VAR\r\nVAR\r\nEND_VAR\r\n";
        var result = engine.Format(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("FUNCTION_BLOCK POU_1", result);
    }

    [Fact]
    public void Format_VarGlobalWithAttributeAndAt_Formats()
    {
        var engine = new FormattingEngine();
        var source = "{attribute 'qualified_only'}\r\nVAR_GLOBAL\r\n\tafff\t\t\t\t\tAT \t%I* :BOOL;\r\nEND_VAR";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("{attribute 'qualified_only'}", result);
        Assert.Contains("afff", result);
        Assert.Contains("AT", result);
        Assert.Contains("%I*", result);
        Assert.Contains("BOOL", result);
    }

    [Fact]
    public void FormatDeclaration_PragmaAfterVarDecl_OnOwnLine()
    {
        var engine = new FormattingEngine();
        var source = "VAR_GLOBAL\r\n    afff AT %I*  : BOOL;\r\n\t\t\t\t\t{attribute 'TcLinkTo' := 'trert'}\r\nEND_VAR";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("{attribute 'TcLinkTo' := 'trert'}", result);
        Assert.Contains("VAR_GLOBAL", result);
        Assert.Contains("END_VAR", result);
        Assert.Contains("afff", result);

        var lines = result.Replace("\r\n", "\n").Split('\n');
        var pragmaLine = Array.FindIndex(lines, l => l.Contains("{attribute 'TcLinkTo'"));
        var endVarLine = Array.FindIndex(lines, l => l.Trim().StartsWith("END_VAR"));
        Assert.True(pragmaLine >= 0, "Pragma should be on its own line");
        Assert.True(endVarLine >= 0, "END_VAR should be on its own line");
        Assert.True(pragmaLine < endVarLine, "Pragma should appear before END_VAR");
        Assert.DoesNotContain("}END_VAR", result.Replace("\r\n", "\n"));
        Assert.DoesNotContain("}END_VAR", result);
    }

    [Fact]
    public void FormatDeclaration_PragmaBeforeVarDecl_OnOwnLine()
    {
        var engine = new FormattingEngine();
        var source = "VAR_GLOBAL\r\n{attribute 'qualified_only'}\r\naaaa : BOOL;\r\nEND_VAR";
        var result = engine.FormatDeclaration(source);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("{attribute 'qualified_only'}", result);
        Assert.Contains("aaaa", result);
        Assert.Contains("VAR_GLOBAL", result);
        Assert.Contains("END_VAR", result);

        Assert.DoesNotContain("}aaaa", result.Replace("\r\n", "\n"));
        Assert.DoesNotContain("}aaaa", result);
    }
}