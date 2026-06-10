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
        public string Language { get; set; } = "en";
        public DateTime? LastSavedUtc { get; set; }
        public FormattingConfiguration Formatting { get; set; } = new();
        public System.Collections.Generic.List<string> RecentPingTargets { get; set; } = new();
    }

    public static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STBud");

        private static readonly string LegacySettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STFormatter");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
        private static readonly string LegacySettingsPath = Path.Combine(LegacySettingsDir, "settings.json");

        private static AppSettings? _appSettings;
        private static FormattingConfiguration? _current;
        private static readonly object _gate = new();

        public static AppSettings App
        {
            get
            {
                EnsureLoaded();
                return _appSettings!;
            }
        }

        public static FormattingConfiguration Current
        {
            get
            {
                EnsureLoaded();
                return _current!;
            }
            set
            {
                lock (_gate)
                {
                    _current = value;
                    if (_appSettings != null)
                        _appSettings.Formatting = value;
                }
            }
        }

        public static void EnsureLoaded()
        {
            lock (_gate)
            {
                if (_appSettings != null) return;
                try
                {
                    string path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
                    if (!File.Exists(path))
                    {
                        _appSettings = new AppSettings();
                        _current = _appSettings.Formatting;
                        Strings.ApplyLanguage(_appSettings.Language);
                        return;
                    }

                    string json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true
                    };
                    _appSettings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
                    _current = _appSettings.Formatting;

                    if (_appSettings.Language == "de" && _appSettings.LastSavedUtc is null)
                    {
                        _appSettings.Language = "en";
                    }
                    Strings.ApplyLanguage(_appSettings.Language);
                }
                catch (Exception ex)
                {
                    HostLog.Append("SettingsManager", $"Load failed: {ex.Message}");
                    _appSettings = new AppSettings();
                    _current = _appSettings.Formatting;
                    Strings.ApplyLanguage(_appSettings.Language);
                }
            }
        }

        public static FormattingConfiguration Load() => Current;

        public static void Save(FormattingConfiguration config)
        {
            try
            {
                EnsureLoaded();
                Directory.CreateDirectory(SettingsDir);
                lock (_gate)
                {
                    _appSettings!.Formatting = config;
                    _appSettings.LastSavedUtc = DateTime.UtcNow;
                    _current = config;
                }
                WriteAppSettings(_appSettings!);
            }
            catch (Exception ex)
            {
                HostLog.Append("SettingsManager", $"Save failed: {ex.Message}");
            }
        }

        public static void SaveAppSettings(AppSettings appSettings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                Strings.ApplyLanguage(appSettings.Language);
                WriteAppSettings(appSettings);
                lock (_gate)
                {
                    _appSettings = appSettings;
                    _current = appSettings.Formatting;
                }
            }
            catch (Exception ex)
            {
                HostLog.Append("SettingsManager", $"SaveAppSettings failed: {ex.Message}");
            }
        }

        private static void WriteAppSettings(AppSettings appSettings)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            string json = JsonSerializer.Serialize(appSettings, options);
            File.WriteAllText(SettingsPath, json);
        }

        public static void ResetToDefault()
        {
            _current = FormattingConfiguration.Default;
            Save(_current);
        }
    }
}
