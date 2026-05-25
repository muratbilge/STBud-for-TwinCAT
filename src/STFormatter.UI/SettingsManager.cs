using System;
using System.IO;
using System.Text.Json;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    public static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STFormatter");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        private static FormattingConfiguration? _current;

        public static FormattingConfiguration Current
        {
            get
            {
                if (_current == null)
                    _current = Load();
                return _current;
            }
            set => _current = value;
        }

        public static FormattingConfiguration Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    _current = FormattingConfiguration.Default;
                    return _current;
                }

                string json = File.ReadAllText(SettingsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                _current = JsonSerializer.Deserialize<FormattingConfiguration>(json, options)
                           ?? FormattingConfiguration.Default;
                return _current;
            }
            catch
            {
                _current = FormattingConfiguration.Default;
                return _current;
            }
        }

        public static void Save(FormattingConfiguration config)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(SettingsPath, json);
                _current = config;
            }
            catch
            {
            }
        }

        public static void ResetToDefault()
        {
            _current = FormattingConfiguration.Default;
            Save(_current);
        }

        public static void ApplyPreset(string presetName)
        {
            _current = FormattingConfiguration.FromPreset(presetName);
            Save(_current);
        }
    }
}