using System.Text.Json;
using STFormatter.Core;
using STFormatter.Core.Formatting;
using STFormatter.Core.Text;

namespace STFormatter.CLI;

class Program
{
    private static readonly string[] SupportedExtensions = { ".st", ".txt", ".iecst", ".tcpou", ".tcdut", ".tcgvl", ".tcio" 
    };

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "format" => FormatCommand(args[1..]),
            "check" => CheckCommand(args[1..]),
            "batch" => BatchCommand(args[1..]),
            "init" => InitCommand(args[1..]),
            "preset" => PresetCommand(args[1..]),
            "export" => ExportCommand(args[1..]),
            "import" => ImportCommand(args[1..]),
            _ => UnknownCommand(command)
        };
    }

    static int FormatCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: No file specified for format command.");
            return 1;
        }

        var filePath = args[0];
        var outputPath = args.Length > 1 && args[1] == "-o" ? args[2] : filePath;
        var dryRun = args.Contains("--dry-run");

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        try
        {
            var source = File.ReadAllText(filePath);
            var config = FormattingEngine.LoadConfiguration(filePath);
            var engine = new FormattingEngine(config);
            var formatted = engine.Format(source);

            if (dryRun)
            {
                Console.WriteLine(formatted);
            }
            else
            {
                File.WriteAllText(outputPath, formatted);
                Console.WriteLine($"Formatted: {filePath} -> {outputPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error formatting file: {ex.Message}");
            return 1;
        }
    }

    static int CheckCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: No file or directory specified for check command.");
            return 1;
        }

        var filePath = args[0];
        var recursive = args.Contains("--recursive");

        if (Directory.Exists(filePath))
        {
            return CheckDirectory(filePath, recursive);
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        return CheckFile(filePath);
    }

    static int BatchCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: No directory specified for batch command.");
            return 1;
        }

        var directory = args[0];
        var recursive = args.Contains("--recursive") || args.Contains("-r");
        var dryRun = args.Contains("--dry-run");
        var includeTcFiles = args.Contains("--twinCAT") || args.Contains("--twincat");

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Error: Directory not found: {directory}");
            return 1;
        }

        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = new List<string>();

            // Collect ST/text files
            files.AddRange(Directory.GetFiles(directory, "*.st", searchOption));
            files.AddRange(Directory.GetFiles(directory, "*.txt", searchOption));
            files.AddRange(Directory.GetFiles(directory, "*.iecst", searchOption));

            if (includeTcFiles)
            {
                files.AddRange(Directory.GetFiles(directory, "*.TcPOU", searchOption));
                files.AddRange(Directory.GetFiles(directory, "*.TcDUT", searchOption));
                files.AddRange(Directory.GetFiles(directory, "*.TcGVL", searchOption));
            }

            if (files.Count == 0)
            {
                Console.WriteLine("No files found to format.");
                return 0;
            }

            var formattedCount = 0;
            var unchangedCount = 0;
            var errorCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var source = File.ReadAllText(file);
                    var config = FormattingEngine.LoadConfiguration(file);
                    var engine = new FormattingEngine(config);
                    var formatted = engine.Format(source);

                    if (formatted != source)
                    {
                        if (dryRun)
                        {
                            Console.WriteLine($"[DRY RUN] Would format: {file}");
                        }
                        else
                        {
                            File.WriteAllText(file, formatted);
                            Console.WriteLine($"Formatted: {file}");
                        }
                        formattedCount++;
                    }
                    else
                    {
                        unchangedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error formatting {file}: {ex.Message}");
                    errorCount++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Batch format complete: {formattedCount} formatted, {unchangedCount} unchanged, {errorCount} errors");
            return errorCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in batch format: {ex.Message}");
            return 1;
        }
    }

    static int InitCommand(string[] args)
    {
        var directory = args.Length > 0 ? args[0] : ".";
        var presetName = args.Contains("--preset") 
            ? args[Array.IndexOf(args, "--preset") + 1] 
            : "stweep";

        try
        {
            var config = FormattingConfiguration.FromPreset(presetName);
            var configPath = Path.Combine(directory, ".editorconfig");

            if (File.Exists(configPath))
            {
                Console.WriteLine($".editorconfig already exists at {configPath}");
                Console.Write("Overwrite? (y/N): ");
                if (Console.ReadLine()?.ToLowerInvariant() != "y")
                {
                    Console.WriteLine("Aborted.");
                    return 0;
                }
            }

            var editorConfig = $@"root = true

[*]
indent_style = {(config.IndentStyle == "tabs" ? "tab" : "space")}
indent_size = {config.IndentSize}
end_of_line = {config.NewLineStyle}
max_line_length = {config.MaxLineLength}

[*.st]
st_keyword_casing = {config.KeywordCasing}
st_space_around_operators = {(config.SpaceAroundOperators ? "true" : "false")}
st_align_variable_declarations = {(config.AlignVariableDeclarations ? "true" : "false")}
st_align_assignments = {(config.AlignAssignments ? "true" : "false")}
st_empty_lines_between_pous = {config.EmptyLinesBetweenPOUs}
st_empty_lines_between_var_sections = {config.EmptyLinesBetweenVarSections}
st_format_on_save = {(config.FormatOnSave ? "true" : "false")}
";

            File.WriteAllText(configPath, editorConfig);
            Console.WriteLine($"Created .editorconfig at {configPath}");
            Console.WriteLine($"Using preset: {presetName}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error creating config: {ex.Message}");
            return 1;
        }
    }

    static int PresetCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Available presets:");
            Console.WriteLine("  stweep    - Standard STweep-like formatting (default)");
            Console.WriteLine("  compact   - Compact 2-space formatting");
            Console.WriteLine("  expanded  - Expanded with 80-char line limit");
            Console.WriteLine();
            Console.WriteLine("Usage: stfmt preset <name>");
            Console.WriteLine("       stfmt preset stweep");
            return 0;
        }

        var presetName = args[0].ToLowerInvariant();
        var config = FormattingConfiguration.FromPreset(presetName);

        Console.WriteLine($"Preset: {presetName}");
        Console.WriteLine($"  Indent:         {config.IndentSize} {config.IndentStyle}");
        Console.WriteLine($"  Keyword casing: {config.KeywordCasing}");
        Console.WriteLine($"  Line length:    {config.MaxLineLength}");
        Console.WriteLine($"  Align vars:     {config.AlignVariableDeclarations}");
        Console.WriteLine($"  Align assigns:  {config.AlignAssignments}");
        Console.WriteLine($"  Empty POUs:     {config.EmptyLinesBetweenPOUs}");
        Console.WriteLine($"  Format on save: {config.FormatOnSave}");

        return 0;
    }

    static int ExportCommand(string[] args)
    {
        var outputPath = args.Length > 0 ? args[0] : "stformatter.config.json";
        var presetName = args.Contains("--preset") 
            ? args[Array.IndexOf(args, "--preset") + 1] 
            : "stweep";

        try
        {
            var config = FormattingConfiguration.FromPreset(presetName);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Exported configuration to {outputPath}");
            Console.WriteLine($"Preset: {presetName}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error exporting configuration: {ex.Message}");
            return 1;
        }
    }

    static int ImportCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: No JSON file specified.");
            return 1;
        }

        var filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<FormattingConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (config == null)
            {
                Console.Error.WriteLine("Error: Failed to parse configuration file.");
                return 1;
            }

            // Save as .editorconfig
            var editorConfig = $@"root = true

[*]
indent_style = {(config.IndentStyle == "tabs" ? "tab" : "space")}
indent_size = {config.IndentSize}
end_of_line = {config.NewLineStyle}
max_line_length = {config.MaxLineLength}

[*.st]
st_keyword_casing = {config.KeywordCasing}
st_space_around_operators = {(config.SpaceAroundOperators ? "true" : "false")}
st_align_variable_declarations = {(config.AlignVariableDeclarations ? "true" : "false")}
st_align_assignments = {(config.AlignAssignments ? "true" : "false")}
st_empty_lines_between_pous = {config.EmptyLinesBetweenPOUs}
st_empty_lines_between_var_sections = {config.EmptyLinesBetweenVarSections}
st_format_on_save = {(config.FormatOnSave ? "true" : "false")}
";

            var outputPath = Path.Combine(Path.GetDirectoryName(filePath) ?? ".", ".editorconfig");
            File.WriteAllText(outputPath, editorConfig);
            Console.WriteLine($"Imported configuration to {outputPath}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error importing configuration: {ex.Message}");
            return 1;
        }
    }

    static int CheckDirectory(string directory, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(directory, "*.st", searchOption));
        files.AddRange(Directory.GetFiles(directory, "*.txt", searchOption));
        files.AddRange(Directory.GetFiles(directory, "*.iecst", searchOption));

        var hasErrors = false;
        foreach (var file in files)
        {
            if (CheckFile(file) != 0)
            {
                hasErrors = true;
            }
        }

        return hasErrors ? 1 : 0;
    }

    static int CheckFile(string filePath)
    {
        try
        {
            var source = File.ReadAllText(filePath);
            var config = FormattingEngine.LoadConfiguration(filePath);
            var engine = new FormattingEngine(config);
            var formatted = engine.Format(source);

            if (source != formatted)
            {
                Console.Error.WriteLine($"Check failed: {filePath} is not formatted.");
                return 1;
            }

            Console.WriteLine($"Check passed: {filePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error checking file {filePath}: {ex.Message}");
            return 1;
        }
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    static void PrintUsage()
    {
        Console.WriteLine("TwinCAT ST Formatter CLI v1.0");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  stfmt format <file> [-o <output>] [--dry-run]");
        Console.WriteLine("  stfmt check <file|directory> [--recursive]");
        Console.WriteLine("  stfmt batch <directory> [--recursive] [--twincat] [--dry-run]");
        Console.WriteLine("  stfmt init [directory] [--preset <name>]");
        Console.WriteLine("  stfmt preset [name]");
        Console.WriteLine("  stfmt export [file] [--preset <name>]");
        Console.WriteLine("  stfmt import <json-file>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  format    Format a single ST file");
        Console.WriteLine("  check     Check if file(s) are formatted (CI mode)");
        Console.WriteLine("  batch     Format all ST files in a directory");
        Console.WriteLine("  init      Create .editorconfig with selected preset");
        Console.WriteLine("  preset    Show preset details or list presets");
        Console.WriteLine("  export    Export configuration to JSON");
        Console.WriteLine("  import    Import configuration from JSON to .editorconfig");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o              Output file path (default: overwrite input)");
        Console.WriteLine("  --dry-run       Preview without writing files");
        Console.WriteLine("  --recursive     Include subdirectories");
        Console.WriteLine("  --twincat       Include .TcPOU, .TcDUT, .TcGVL files");
        Console.WriteLine("  --preset        Use a preset (stweep, compact, expanded)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  stfmt format MyProgram.st");
        Console.WriteLine("  stfmt batch ./POUs --recursive --twincat");
        Console.WriteLine("  stfmt init . --preset stweep");
        Console.WriteLine("  stfmt export myconfig.json --preset compact");
        Console.WriteLine("  stfmt check ./src --recursive");
    }
}
