using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace STFormatter.VSIX.Options;

public class STFormatterOptionPage : DialogPage
{
    [Category("Indentation")]
    [DisplayName("Indent Style")]
    [Description("Use spaces or tabs for indentation")]
    [DefaultValue("spaces")]
    [TypeConverter(typeof(IndentStyleConverter))]
    public string IndentStyle { get; set; } = "spaces";

    [Category("Indentation")]
    [DisplayName("Indent Size")]
    [Description("Number of spaces per indentation level")]
    [DefaultValue(4)]
    public int IndentSize { get; set; } = 4;

    [Category("Indentation")]
    [DisplayName("Continuation Indent Size")]
    [Description("Number of columns for continuation lines")]
    [DefaultValue(8)]
    public int ContinuationIndentSize { get; set; } = 8;

    [Category("Formatting")]
    [DisplayName("Keyword Casing")]
    [Description("Convert keywords to upper, lower, pascal, or original case")]
    [DefaultValue("upper")]
    [TypeConverter(typeof(KeywordCasingConverter))]
    public string KeywordCasing { get; set; } = "upper";

    [Category("Formatting")]
    [DisplayName("Brace Style")]
    [Description("Controls vertical spacing: allman (more spacing) or compact (less spacing)")]
    [DefaultValue("allman")]
    [TypeConverter(typeof(BraceStyleConverter))]
    public string BraceStyle { get; set; } = "allman";

    [Category("Formatting")]
    [DisplayName("Space Around Operators")]
    [Description("Insert spaces around operators like :=, +, -, etc.")]
    [DefaultValue(true)]
    public bool SpaceAroundOperators { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Space After Comma")]
    [Description("Insert a space after commas in argument lists")]
    [DefaultValue(true)]
    public bool SpaceAfterComma { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Space Before Semicolon")]
    [Description("Insert a space before semicolons")]
    [DefaultValue(false)]
    public bool SpaceBeforeSemicolon { get; set; } = false;

    [Category("Formatting")]
    [DisplayName("Space After Colon")]
    [Description("Insert a space after colons in declarations")]
    [DefaultValue(true)]
    public bool SpaceAfterColon { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Align Assignments")]
    [Description("Align := in consecutive assignment blocks")]
    [DefaultValue(true)]
    public bool AlignAssignments { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Align Variable Declarations")]
    [Description("Pad names and types in VAR sections")]
    [DefaultValue(true)]
    public bool AlignVariableDeclarations { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Max Line Length")]
    [Description("Maximum line length before wrapping (0 = unlimited)")]
    [DefaultValue(120)]
    public int MaxLineLength { get; set; } = 120;

    [Category("Formatting")]
    [DisplayName("Keep Single-Line Blocks")]
    [Description("Keep single-statement IF/FOR/WHILE blocks on one line")]
    [DefaultValue(false)]
    public bool KeepSingleLineBlocks { get; set; } = false;

    [Category("Formatting")]
    [DisplayName("Format On Save")]
    [Description("Automatically format ST code when saving files")]
    [DefaultValue(true)]
    public bool FormatOnSave { get; set; } = true;

    [Category("Line Breaks")]
    [DisplayName("Empty Lines Between POUs")]
    [Description("Number of empty lines between program organization units")]
    [DefaultValue(2)]
    public int EmptyLinesBetweenPOUs { get; set; } = 2;

    [Category("Line Breaks")]
    [DisplayName("Empty Lines Between Var Sections")]
    [Description("Number of empty lines between variable declaration sections")]
    [DefaultValue(1)]
    public int EmptyLinesBetweenVarSections { get; set; } = 1;

    [Category("Line Breaks")]
    [DisplayName("New Line Style")]
    [Description("Line ending style: crlf, lf, or cr")]
    [DefaultValue("crlf")]
    [TypeConverter(typeof(NewLineStyleConverter))]
    public string NewLineStyle { get; set; } = "crlf";

    public Core.Formatting.FormattingConfiguration ToConfiguration()
    {
        return new Core.Formatting.FormattingConfiguration
        {
            IndentStyle = IndentStyle,
            IndentSize = IndentSize,
            ContinuationIndentSize = ContinuationIndentSize,
            NewLineStyle = NewLineStyle,
            KeywordCasing = KeywordCasing,
            BraceStyle = BraceStyle,
            SpaceAroundOperators = SpaceAroundOperators,
            SpaceAfterComma = SpaceAfterComma,
            SpaceBeforeSemicolon = SpaceBeforeSemicolon,
            SpaceAfterColon = SpaceAfterColon,
            AlignAssignments = AlignAssignments,
            AlignVariableDeclarations = AlignVariableDeclarations,
            MaxLineLength = MaxLineLength,
            KeepSingleLineBlocks = KeepSingleLineBlocks,
            FormatOnSave = FormatOnSave,
            EmptyLinesBetweenPOUs = EmptyLinesBetweenPOUs,
            EmptyLinesBetweenVarSections = EmptyLinesBetweenVarSections
        };
    }
}

public class IndentStyleConverter : TypeConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(new[] { "spaces", "tabs" });
    }
}

public class KeywordCasingConverter : TypeConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(new[] { "upper", "lower", "pascal", "original" });
    }
}

public class BraceStyleConverter : TypeConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(new[] { "allman", "compact" });
    }
}

public class NewLineStyleConverter : TypeConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return new StandardValuesCollection(new[] { "crlf", "lf", "cr" });
    }
}
