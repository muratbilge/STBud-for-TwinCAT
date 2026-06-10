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

        return LoadFromDirectory(directory, filePath);
    }

    public static FormattingConfiguration? LoadFromDirectory(string startDirectory, string? filePath = null)
    {
        // Walk up the directory tree collecting .editorconfig files, innermost
        // first; stop at the first file declaring "root = true" (a top-level
        // preamble property, parsed into the synthetic "" section).
        var files = new List<List<EditorConfigSection>>();
        var currentDir = startDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            var editorConfigPath = Path.Combine(currentDir, ".editorconfig");
            if (File.Exists(editorConfigPath))
            {
                var sections = Parse(editorConfigPath);
                files.Add(sections);

                bool isRoot = sections.Any(s =>
                    (s.Pattern == "" || s.Pattern == "*") &&
                    s.Properties.TryGetValue("root", out var rootValue) &&
                    rootValue.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (isRoot)
                    break;
            }

            var parentDir = Directory.GetParent(currentDir);
            currentDir = parentDir?.FullName;
        }

        if (files.Count == 0)
            return null;

        var config = new FormattingConfiguration();
        var allProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var fileName = filePath != null ? Path.GetFileName(filePath) : null;

        // EditorConfig precedence: outermost file first, then inner files
        // override; within a file, later sections override earlier ones.
        for (var f = files.Count - 1; f >= 0; f--)
        {
            foreach (var section in files[f])
            {
                if (section.Pattern == "") continue; // preamble (only "root" lives there)
                if (fileName != null && !MatchesPattern(section.Pattern, fileName))
                    continue;

                foreach (var prop in section.Properties)
                {
                    allProperties[prop.Key] = prop.Value;
                }
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

        if (properties.TryGetValue("st_brace_style", out var braceStyle))
        {
            config.BraceStyle = braceStyle.ToLowerInvariant() switch
            {
                "compact" or "kr" or "k&r" or "stroustrup" => "compact",
                _ => "allman"
            };
        }

        if (properties.TryGetValue("st_continuation_indent_size", out var contIndent) &&
            int.TryParse(contIndent, out var contIndentValue))
        {
            config.ContinuationIndentSize = contIndentValue;
        }

        if (properties.TryGetValue("st_empty_lines_between_var_sections", out var emptyLinesVar) &&
            int.TryParse(emptyLinesVar, out var emptyLinesVarValue))
        {
            config.EmptyLinesBetweenVarSections = emptyLinesVarValue;
        }

        if (properties.TryGetValue("st_keep_single_line_blocks", out var keepSingleLine))
        {
            config.KeepSingleLineBlocks = IsTruthy(keepSingleLine);
        }

        if (properties.TryGetValue("st_space_after_comma", out var spaceAfterComma))
        {
            config.SpaceAfterComma = IsTruthy(spaceAfterComma);
        }

        if (properties.TryGetValue("st_space_before_semicolon", out var spaceBeforeSemicolon))
        {
            config.SpaceBeforeSemicolon = IsTruthy(spaceBeforeSemicolon);
        }

        if (properties.TryGetValue("st_space_after_colon", out var spaceAfterColon))
        {
            config.SpaceAfterColon = IsTruthy(spaceAfterColon);
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
        // Properties before the first [section] header form the preamble -
        // that is where "root = true" lives per the EditorConfig spec.
        var currentSection = new EditorConfigSection("");
        sections.Add(currentSection);

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var pattern = line.Substring(1, line.Length - 2).Trim();
                currentSection = new EditorConfigSection(pattern);
                sections.Add(currentSection);
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex > 0)
            {
                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                currentSection.Properties[key] = value;
            }
        }

        return sections;
    }

    public static bool MatchesPattern(string pattern, string fileName)
    {
        if (pattern == "*")
            return true;

        foreach (var singlePattern in SplitTopLevelPatterns(pattern))
        {
            if (MatchesSinglePattern(singlePattern.Trim(), fileName))
                return true;
        }

        return false;
    }

    private static List<string> SplitTopLevelPatterns(string pattern)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(pattern.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < pattern.Length)
            result.Add(pattern.Substring(start));

        return result;
    }

    private static bool MatchesSinglePattern(string pattern, string fileName)
    {
        if (pattern == "*")
            return true;

        // Convert editorconfig glob to regex
        // Supported: *, **, ?, [chars], [!chars], {a,b}
        var regexPattern = "^";
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    regexPattern += ".*";
                    i += 2;
                    // Skip trailing /
                    if (i < pattern.Length && pattern[i] == '/')
                        i++;
                }
                else
                {
                    regexPattern += "[^/]*";
                    i++;
                }
            }
            else if (c == '?')
            {
                regexPattern += "[^/]";
                i++;
            }
            else if (c == '[')
            {
                var end = pattern.IndexOf(']', i + 1);
                if (end < 0)
                {
                    regexPattern += Regex.Escape(c.ToString());
                    i++;
                }
                else
                {
                    var bracketContent = pattern.Substring(i + 1, end - i - 1);
                    if (bracketContent.StartsWith("!"))
                        bracketContent = "^" + bracketContent.Substring(1);
                    regexPattern += "[" + bracketContent + "]";
                    i = end + 1;
                }
            }
            else if (c == '{')
            {
                var end = pattern.IndexOf('}', i + 1);
                if (end < 0)
                {
                    regexPattern += Regex.Escape(c.ToString());
                    i++;
                }
                else
                {
                    var options = pattern.Substring(i + 1, end - i - 1);
                    var alternation = string.Join("|", options.Split(',').Select(o => o.Trim()));
                    regexPattern += "(" + alternation + ")";
                    i = end + 1;
                }
            }
            else
            {
                regexPattern += Regex.Escape(c.ToString());
                i++;
            }
        }
        regexPattern += "$";

        try
        {
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
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
