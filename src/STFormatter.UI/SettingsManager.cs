using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    public sealed class AppSettings
    {
        public string VersionProfileName { get; set; } = "auto";
        public FormattingConfiguration Formatting { get; set; } = new();

        public TcXaeShellVersionProfile ResolveProfile()
        {
            if (string.Equals(VersionProfileName, "auto", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var p in TcXaeShellVersionProfile.AllProfiles)
            {
                if (string.Equals(p.Name, VersionProfileName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return TcXaeShellVersionProfile.FromDteVersion(VersionProfileName);
        }
    }

    public static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STFormatter");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings? _appSettings;

        public static AppSettings App
        {
            get
            {
                if (_appSettings == null)
                    _appSettings = LoadAppSettings();
                return _appSettings;
            }
            set => _appSettings = value;
        }

        private static FormattingConfiguration? _current;

        public static FormattingConfiguration Current
        {
            get
            {
                if (_current == null)
                    _current = App.Formatting;
                return _current;
            }
            set => _current = value;
        }

        public static AppSettings LoadAppSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                string json = File.ReadAllText(SettingsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                return JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
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
                var appSettings = JsonSerializer.Deserialize<AppSettings>(json, options);
                _appSettings = appSettings ?? new AppSettings();
                _current = appSettings?.Formatting ?? FormattingConfiguration.Default;
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
                var app = App;
                app.Formatting = config;
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                string json = JsonSerializer.Serialize(app, options);
                File.WriteAllText(SettingsPath, json);
                _current = config;
            }
            catch { }
        }

        public static void SaveAppSettings(AppSettings appSettings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                string json = JsonSerializer.Serialize(appSettings, options);
                File.WriteAllText(SettingsPath, json);
                _appSettings = appSettings;
                _current = appSettings.Formatting;
            }
            catch { }
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