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

    [Category("Formatting")]
    [DisplayName("Keyword Casing")]
    [Description("Convert keywords to upper, lower, or pascal case")]
    [DefaultValue("upper")]
    [TypeConverter(typeof(KeywordCasingConverter))]
    public string KeywordCasing { get; set; } = "upper";

    [Category("Formatting")]
    [DisplayName("Space Around Operators")]
    [Description("Insert spaces around operators like :=, +, -, etc.")]
    [DefaultValue(true)]
    public bool SpaceAroundOperators { get; set; } = true;

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

    public Core.Formatting.FormattingConfiguration ToConfiguration()
    {
        return new Core.Formatting.FormattingConfiguration
        {
            IndentStyle = IndentStyle,
            IndentSize = IndentSize,
            KeywordCasing = KeywordCasing,
            SpaceAroundOperators = SpaceAroundOperators,
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
