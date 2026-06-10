namespace STFormatter.Core.Toolbox;

/// <summary>
/// Builds the TwinCAT pragma/attribute text snippets inserted by the editor helpers.
/// Centralized here (instead of inline in the Host handlers) so the exact insertion
/// text is unit-testable without COM.
/// </summary>
public static class PragmaTemplates
{
    public const string EndRegion = "{endregion}";

    /// <summary>{attribute 'name'}</summary>
    public static string Attribute(string name) => $"{{attribute '{name}'}}";

    /// <summary>{attribute 'name' := 'value'}</summary>
    public static string Attribute(string name, string value) => $"{{attribute '{name}' := '{value}'}}";

    /// <summary>{warning 'message'}</summary>
    public static string Warning(string message) => $"{{warning '{message}'}}";

    /// <summary>{region 'name'}</summary>
    public static string RegionStart(string name) => $"{{region '{name}'}}";

    /// <summary>{region 'name'} + three blank lines + {endregion}, ready for cursor placement inside.</summary>
    public static string RegionBlock(string name) => $"{{region '{name}'}}\r\n\r\n\r\n{{endregion}}";

    /// <summary>
    /// Menu items pass either a bare attribute name ("hide") or a complete pragma
    /// ("{endregion}", "monitoring := 'call'" is also bare). Bare names are wrapped
    /// as {attribute '...'}; text already containing '{' is inserted verbatim.
    /// </summary>
    public static string WrapMenuPragma(string pragmaText) =>
        pragmaText.Contains("{") ? pragmaText : Attribute(pragmaText);
}
