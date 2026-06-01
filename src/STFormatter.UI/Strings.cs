using System.Collections.Generic;
using System.Globalization;

namespace STFormatter.UI
{
    public enum AppLanguage
    {
        De,
        En,
    }

    public static class Strings
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _bundles = new()
        {
            ["de"] = new Dictionary<string, string>
            {
                ["App.Title"] = "ST Formatter",
                ["Tray.Text"] = "ST Formatter",
                ["Tray.Settings"] = "Einstellungen",
                ["Tray.Instances"] = "Instanzen",
                ["Tray.History"] = "Verlauf",
                ["Tray.Log"] = "Protokoll",
                ["Tray.Restart"] = "Neu starten",
                ["Tray.Exit"] = "Beenden",
                ["Tray.Saved.Title"] = "ST Formatter",
                ["Tray.Saved.Text"] = "Einstellungen gespeichert.",
                ["Tab.Settings"] = "Einstellungen",
                ["Tab.Instances"] = "Instanzen",
                ["Tab.History"] = "Verlauf",
                ["Tab.Log"] = "Protokoll",
                ["Settings.Preset"] = "Vorlage:",
                ["Settings.Preset.Default"] = "Standard",
                ["Settings.Preset.Compact"] = "Kompakt",
                ["Settings.Preset.Expanded"] = "Erweitert",
                ["Settings.Button.Preview"] = "Vorschau",
                ["Settings.Button.Apply"] = "Anwenden",
                ["Settings.Button.Reset"] = "Zurücksetzen",
                ["Settings.Group.Indentation"] = "Einrückung",
                ["Settings.Group.Keywords"] = "Zeilenumbrüche & Schlüsselwörter",
                ["Settings.Group.Spacing"] = "Leerzeichen",
                ["Settings.Group.Lines"] = "Zeilen & Ausrichtung",
                ["Settings.Group.Behavior"] = "Verhalten",
                ["Settings.Group.General"] = "Allgemein",
                ["Settings.IndentStyle"] = "Einrückungsstil:",
                ["Settings.IndentSize"] = "Einrückungsbreite:",
                ["Settings.ContinuationIndent"] = "Fortsetzungseinrückung:",
                ["Settings.Newline"] = "Zeilenende:",
                ["Settings.KeywordCasing"] = "Schlüsselwort-Schreibung:",
                ["Settings.BraceStyle"] = "Klammerstil:",
                ["Settings.SpaceAroundOperators"] = "Leerzeichen um Operatoren",
                ["Settings.SpaceAfterComma"] = "Leerzeichen nach Komma",
                ["Settings.SpaceBeforeSemicolon"] = "Leerzeichen vor Semikolon",
                ["Settings.SpaceAfterColon"] = "Leerzeichen nach Doppelpunkt",
                ["Settings.AlignAssignments"] = "Zuweisungen ausrichten",
                ["Settings.AlignDeclarations"] = "Variablendeklarationen ausrichten",
                ["Settings.MaxLineLength"] = "Maximale Zeilenlänge:",
                ["Settings.EmptyLinesBetweenPOUs"] = "Leere Zeilen zwischen POUs:",
                ["Settings.EmptyLinesBetweenVarSections"] = "Leere Zeilen zwischen VAR-Blöcken:",
                ["Settings.KeepSingleLineBlocks"] = "Einzeilige Blöcke beibehalten",
                ["Settings.FormatOnSave"] = "Beim Speichern formatieren",
                ["Settings.StartWithWindows"] = "Mit Windows starten",
                ["Settings.Language"] = "Sprache:",
                ["Settings.SavedAt"] = "Zuletzt gespeichert: {0}",
                ["Settings.NeverSaved"] = "Noch nicht gespeichert.",
                ["Settings.Preview.Error"] = "Vorschaufehler: {0}",
                ["Settings.ResetConfirm.Title"] = "Einstellungen zurücksetzen",
                ["Settings.ResetConfirm.Text"] = "Alle Einstellungen auf die Standardwerte zurücksetzen?",
                ["Settings.RestartNote"] = "Sprachänderungen werden beim nächsten Start wirksam.",
                ["Instances.Refresh"] = "Aktualisieren",
                ["Instances.Scan"] = "Suchen",
                ["Instances.Cleanup"] = "Veraltete entfernen",
                ["Instances.None"] = "Keine Instanzen",
                ["Instances.Status.Connected"] = "Verbunden",
                ["Instances.Status.Disconnected"] = "Getrennt",
                ["Instances.Columns.PID"] = "PID",
                ["Instances.Columns.Title"] = "Titel",
                ["Instances.Columns.Status"] = "Status",
                ["Instances.Columns.Menus"] = "Eingefügte Menüs",
                ["Instances.Columns.LastFormat"] = "Zuletzt formatiert",
                ["Instances.Columns.Count"] = "Anzahl",
                ["History.Clear"] = "Verlauf löschen",
                ["History.Export"] = "Protokoll exportieren...",
                ["History.Hint"] = "Doppelklick auf eine Zeile öffnet den Diff-Viewer.",
                ["History.Empty"] = "Noch keine Formatierungen",
                ["History.Columns.Time"] = "Zeit",
                ["History.Columns.PID"] = "PID",
                ["History.Columns.Title"] = "Titel",
                ["History.Columns.File"] = "Datei",
                ["History.Columns.Section"] = "Bereich",
                ["History.Columns.Lines"] = "Zeilen",
                ["History.Columns.Method"] = "Methode",
                ["History.Columns.Result"] = "Ergebnis",
                ["Log.Clear"] = "Leeren",
                ["Log.CopyAll"] = "Alles kopieren",
                ["Log.Open"] = "In Notepad öffnen",
                ["Log.AutoScroll"] = "Automatisch scrollen",
                ["Common.OK"] = "OK",
                ["Common.Cancel"] = "Abbrechen",
                ["Common.Yes"] = "Ja",
                ["Common.No"] = "Nein",
                ["Common.None"] = "-",
                ["Diff.Original"] = "Original",
                ["Diff.Formatted"] = "Formatiert",
                ["Diff.ChangesOnly"] = "Nur \u00c4nderungen",
                ["Diff.NoChanges"] = "Keine \u00c4nderungen.",
                ["Diff.NoInput"] = "Kein Inhalt.",
                ["Diff.Legend"] = "+ hinzugef\u00fcgt   - entfernt   ~ ge\u00e4ndert   \u00b7\u00b7\u00b7 Ausschnitt",
                ["Diff.Unchanged"] = "unver\u00e4ndert",
            },
            ["en"] = new Dictionary<string, string>
            {
                ["App.Title"] = "ST Formatter",
                ["Tray.Text"] = "ST Formatter",
                ["Tray.Settings"] = "Settings",
                ["Tray.Instances"] = "Instances",
                ["Tray.History"] = "History",
                ["Tray.Log"] = "Log",
                ["Tray.Restart"] = "Restart",
                ["Tray.Exit"] = "Exit",
                ["Tray.Saved.Title"] = "ST Formatter",
                ["Tray.Saved.Text"] = "Settings saved.",
                ["Tab.Settings"] = "Settings",
                ["Tab.Instances"] = "Instances",
                ["Tab.History"] = "History",
                ["Tab.Log"] = "Log",
                ["Settings.Preset"] = "Preset:",
                ["Settings.Preset.Default"] = "Default",
                ["Settings.Preset.Compact"] = "Compact",
                ["Settings.Preset.Expanded"] = "Expanded",
                ["Settings.Button.Preview"] = "Preview",
                ["Settings.Button.Apply"] = "Apply",
                ["Settings.Button.Reset"] = "Reset",
                ["Settings.Group.Indentation"] = "Indentation",
                ["Settings.Group.Keywords"] = "Newlines & Keywords",
                ["Settings.Group.Spacing"] = "Spacing",
                ["Settings.Group.Lines"] = "Lines & Alignment",
                ["Settings.Group.Behavior"] = "Behavior",
                ["Settings.Group.General"] = "General",
                ["Settings.IndentStyle"] = "Indent style:",
                ["Settings.IndentSize"] = "Indent size:",
                ["Settings.ContinuationIndent"] = "Continuation indent:",
                ["Settings.Newline"] = "Newline style:",
                ["Settings.KeywordCasing"] = "Keyword casing:",
                ["Settings.BraceStyle"] = "Brace style:",
                ["Settings.SpaceAroundOperators"] = "Spaces around operators",
                ["Settings.SpaceAfterComma"] = "Space after comma",
                ["Settings.SpaceBeforeSemicolon"] = "Space before semicolon",
                ["Settings.SpaceAfterColon"] = "Space after colon",
                ["Settings.AlignAssignments"] = "Align assignments",
                ["Settings.AlignDeclarations"] = "Align variable declarations",
                ["Settings.MaxLineLength"] = "Max line length:",
                ["Settings.EmptyLinesBetweenPOUs"] = "Empty lines between POUs:",
                ["Settings.EmptyLinesBetweenVarSections"] = "Empty lines between VAR sections:",
                ["Settings.KeepSingleLineBlocks"] = "Keep single-line blocks on one line",
                ["Settings.FormatOnSave"] = "Format on save",
                ["Settings.StartWithWindows"] = "Start with Windows",
                ["Settings.Language"] = "Language:",
                ["Settings.SavedAt"] = "Last saved: {0}",
                ["Settings.NeverSaved"] = "Not saved yet.",
                ["Settings.Preview.Error"] = "Preview error: {0}",
                ["Settings.ResetConfirm.Title"] = "Reset settings",
                ["Settings.ResetConfirm.Text"] = "Reset all settings to defaults?",
                ["Settings.RestartNote"] = "Language changes take effect on next start.",
                ["Instances.Refresh"] = "Refresh",
                ["Instances.Scan"] = "Scan",
                ["Instances.Cleanup"] = "Cleanup Stale",
                ["Instances.None"] = "No instances",
                ["Instances.Status.Connected"] = "Connected",
                ["Instances.Status.Disconnected"] = "Disconnected",
                ["Instances.Columns.PID"] = "PID",
                ["Instances.Columns.Title"] = "Title",
                ["Instances.Columns.Status"] = "Status",
                ["Instances.Columns.Menus"] = "Injected Menus",
                ["Instances.Columns.LastFormat"] = "Last Format",
                ["Instances.Columns.Count"] = "Format Count",
                ["History.Clear"] = "Clear History",
                ["History.Export"] = "Export Log...",
                ["History.Hint"] = "Double-click a row to open diff viewer.",
                ["History.Empty"] = "No formats yet",
                ["History.Columns.Time"] = "Time",
                ["History.Columns.PID"] = "PID",
                ["History.Columns.Title"] = "Title",
                ["History.Columns.File"] = "File",
                ["History.Columns.Section"] = "Section",
                ["History.Columns.Lines"] = "Lines",
                ["History.Columns.Method"] = "Method",
                ["History.Columns.Result"] = "Result",
                ["Log.Clear"] = "Clear",
                ["Log.CopyAll"] = "Copy All",
                ["Log.Open"] = "Open in Notepad",
                ["Log.AutoScroll"] = "Auto-scroll",
                ["Common.OK"] = "OK",
                ["Common.Cancel"] = "Cancel",
                ["Common.Yes"] = "Yes",
                ["Common.No"] = "No",
                ["Common.None"] = "-",
                ["Diff.Original"] = "Original",
                ["Diff.Formatted"] = "Formatted",
                ["Diff.ChangesOnly"] = "Changes only",
                ["Diff.NoChanges"] = "No changes.",
                ["Diff.NoInput"] = "No input.",
                ["Diff.Legend"] = "+ added   - removed   ~ changed   \u00b7\u00b7\u00b7 snip",
                ["Diff.Unchanged"] = "unchanged",
            },
        };

        private static AppLanguage _language = AppLanguage.En;

        public static AppLanguage Language
        {
            get => _language;
            set => _language = value;
        }

        public static string Culture => _language == AppLanguage.En ? "en" : "de";

        public static string Get(string key, params object[] args)
        {
            string lang = _language == AppLanguage.En ? "en" : "de";
            string value;
            if (!_bundles[lang].TryGetValue(key, out value))
            {
                if (!_bundles["en"].TryGetValue(key, out value))
                    return key;
            }
            if (args != null && args.Length > 0)
                return string.Format(CultureInfo.CurrentCulture, value, args);
            return value;
        }

        public static void ApplyLanguage(string culture)
        {
            if (string.Equals(culture, "en", System.StringComparison.OrdinalIgnoreCase))
                _language = AppLanguage.En;
            else
                _language = AppLanguage.De;
        }
    }
}
