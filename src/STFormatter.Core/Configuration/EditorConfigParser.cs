using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using STFormatter.Core.Formatting;

namespace STFormatter.Core.Configuration;

public sealed class EditorConfigParser
{
    public static FormattingConfiguration? LoadForFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return null;

        return LoadFromDirectory(directory);
    }

    public static FormattingConfiguration? LoadFromDirectory(string startDirectory)
    {
        var configs = new List<EditorConfigSection>();
        var currentDir = startDirectory;

        // Walk up directory tree collecting .editorconfig files
        while (!string.IsNullOrEmpty(currentDir))
        {
            var editorConfigPath = Path.Combine(currentDir, ".editorconfig");
            if (File.Exists(editorConfigPath))
            {
                var sections = Parse(editorConfigPath);
                configs.AddRange(sections);

                // Check if this is root
                var rootSection = sections.FirstOrDefault(s => s.Pattern == "*");
                if (rootSection?.Properties.ContainsKey("root") == true &&
                    rootSection.Properties["root"].Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            var parentDir = Directory.GetParent(currentDir);
            currentDir = parentDir?.FullName;
        }

        if (configs.Count == 0)
            return null;

        // Build configuration from collected settings
        // Later configs (closer to file) override earlier ones
        var config = new FormattingConfiguration();
        var allProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Apply from outermost to innermost
        for (var i = configs.Count - 1; i >= 0; i--)
        {
            foreach (var prop in configs[i].Properties)
            {
                allProperties[prop.Key] = prop.Value;
            }
        }

        ApplyEditorConfigProperties(config, allProperties);
        return config;
    }

    private static void ApplyEditorConfigProperties(FormattingConfiguration config, Dictionary<string, string> properties)
    {
        if (properties.TryGetValue("indent_style", out var indentStyle))
        {
            config.IndentStyle = indentStyle.ToLowerInvariant() switch
            {
                "tab" => "tabs",
                _ => "spaces"
            };
        }

        if (properties.TryGetValue("indent_size", out var indentSize) &&
            int.TryParse(indentSize, out var indentSizeValue))
        {
            config.IndentSize = indentSizeValue;
        }

        if (properties.TryGetValue("tab_width", out var tabWidth) &&
            int.TryParse(tabWidth, out var tabWidthValue))
        {
            // Only apply if indent_size is 'tab'
            if (config.IndentStyle == "tabs")
            {
                config.IndentSize = tabWidthValue;
            }
        }

        if (properties.TryGetValue("end_of_line", out var endOfLine))
        {
            config.NewLineStyle = endOfLine.ToLowerInvariant() switch
            {
                "lf" => "lf",
                "cr" => "cr",
                _ => "crlf"
            };
        }

        if (properties.TryGetValue("max_line_length", out var maxLineLength))
        {
            if (int.TryParse(maxLineLength, out var maxLineLengthValue))
            {
                config.MaxLineLength = maxLineLengthValue;
            }
            else if (maxLineLength.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                config.MaxLineLength = 0;
            }
        }

        // ST-specific properties (custom extension)
        if (properties.TryGetValue("st_keyword_casing", out var keywordCasing))
        {
            config.KeywordCasing = keywordCasing.ToLowerInvariant();
        }

        if (properties.TryGetValue("st_space_around_operators", out var spaceAroundOperators))
        {
            config.SpaceAroundOperators = IsTruthy(spaceAroundOperators);
        }

        if (properties.TryGetValue("st_align_variable_declarations", out var alignVars))
        {
            config.AlignVariableDeclarations = IsTruthy(alignVars);
        }

        if (properties.TryGetValue("st_align_assignments", out var alignAssignments))
        {
            config.AlignAssignments = IsTruthy(alignAssignments);
        }

        if (properties.TryGetValue("st_empty_lines_between_pous", out var emptyLinesPOUs) &&
            int.TryParse(emptyLinesPOUs, out var emptyLinesPOUsValue))
        {
            config.EmptyLinesBetweenPOUs = emptyLinesPOUsValue;
        }

        if (properties.TryGetValue("st_format_on_save", out var formatOnSave))
        {
            config.FormatOnSave = IsTruthy(formatOnSave);
        }
    }

    private static bool IsTruthy(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" => true,
            _ => false
        };
    }

    private static List<EditorConfigSection> Parse(string filePath)
    {
        var sections = new List<EditorConfigSection>();
        EditorConfigSection? currentSection = null;

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            // Section header
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var pattern = line.Substring(1, line.Length - 2).Trim();
                currentSection = new EditorConfigSection(pattern);
                sections.Add(currentSection);
                continue;
            }

            // Key = Value pair
            var equalsIndex = line.IndexOf('=');
            if (equalsIndex > 0 && currentSection != null)
            {
                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                currentSection.Properties[key] = value;
            }
        }

        return sections;
    }
}

internal sealed class EditorConfigSection
{
    public string Pattern { get; }
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public EditorConfigSection(string pattern)
    {
        Pattern = pattern;
    }
}
