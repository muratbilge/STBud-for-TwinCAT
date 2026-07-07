using System;
using System.Drawing;

namespace STFormatter.UI.DiffRendering
{
    /// <summary>
    /// Toolbar icon glyphs for the diff viewer. Prefers <b>Segoe MDL2 Assets</b> - the Windows
    /// UI icon font (solid, uniform-weight icons, the same set Explorer/Settings use) - and falls
    /// back to Segoe UI Symbol dingbats when MDL2 is not installed (older OS / net462).
    ///
    /// Glyphs are built from integer codepoints (char.ConvertFromUtf32) so the source stays pure
    /// ASCII - a non-UTF-8 tool can never mojibake an icon into different behavior. MDL2 icons live
    /// in the private-use area (>= U+E000); <see cref="IsIconGlyph"/> lets the caller route them to
    /// the MDL2 font while plain text ("A-"/"A+") uses the normal UI font.
    /// </summary>
    internal static class DiffGlyphs
    {
        public static readonly bool MdlAvailable = FontInstalled("Segoe MDL2 Assets");

        private static string Cp(int codepoint) => char.ConvertFromUtf32(codepoint);

        /// <summary>MDL2 codepoint when the font is present, else the Segoe UI Symbol fallback.</summary>
        private static string M(int mdl, int fallback) => Cp(MdlAvailable ? mdl : fallback);

        public static string Prev        => M(0xE70E, 0x25B2); // ChevronUp   / up triangle
        public static string Next        => M(0xE70D, 0x25BC); // ChevronDown / down triangle
        public static string ChangesOnly => M(0xE71C, 0x2260); // Filter      / not-equal
        public static string Unified     => M(0xE8FD, 0x2261); // List        / triple bar
        public static string Copy        => M(0xE8C8, 0x29C9); // Copy        / two squares
        public static string AcceptSel   => M(0xE73E, 0x2713); // CheckMark   / check
        public static string AcceptAll   => MdlAvailable ? Cp(0xE8FB)          // Accept (filled)
                                                         : Cp(0x2713) + Cp(0x2713); // double check
        public static string ClearStaged => M(0xE711, 0x2717); // Cancel      / cross
        public static string Save        => M(0xE74E, 0x2913); // Save        / down-arrow-to-bar
        public static string Undo        => M(0xE7A7, 0x21B6); // Undo        / undo arrow
        public static string Edit        => M(0xE70F, 0x270E); // Edit        / pencil
        public static string ThemeSun    => M(0xE706, 0x2600); // Brightness  / sun
        public static string ThemeMoon   => M(0xE708, 0x263E); // QuietHours  / moon

        // Font size stays clear text in both modes (no icon needed, font-agnostic).
        public static string FontDec => "A-";
        public static string FontInc => "A+";

        /// <summary>True when the glyph is an MDL2 private-use icon (needs the MDL2 font).</summary>
        public static bool IsIconGlyph(string glyph) =>
            !string.IsNullOrEmpty(glyph) && glyph[0] >= 0xE000 && glyph[0] <= 0xF8FF;

        private static bool FontInstalled(string name)
        {
            try
            {
                using (var f = new Font(name, 9f))
                    return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
