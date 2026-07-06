using System;
using System.Drawing;
using STBud.Git.Diff;

namespace STFormatter.UI.DiffRendering
{
    /// <summary>
    /// Color palette for the diff viewer. Light and Dark presets. Default is picked
    /// from the system theme (the luminance of <see cref="SystemColors.Control"/>)
    /// but the user can override via the diff viewer's theme toggle.
    ///
    /// Semantics are kept distinct on purpose so the diff reads at a glance:
    /// added = green, removed = red, changed = amber, and the *staged accept preview*
    /// = blue (a pending action) so it never gets confused with an added (green) line.
    /// Once written, a row is shown "restored" in green (pending blue → saved green).
    /// </summary>
    public sealed class DiffColorScheme
    {
        // Surfaces
        public Color PaneBackground { get; set; }
        public Color GutterBackground { get; set; }
        public Color DividerColor { get; set; }
        public Color SectionHeaderBack { get; set; }
        public Color SectionHeaderFore { get; set; }

        // Window chrome (toolbar / header / legend / status) — kept in the scheme so the
        // whole window themes from one source instead of hardcoded light colors.
        public Color ToolbarBack { get; set; }
        public Color ChromeFore { get; set; }
        public Color ToolbarButtonHover { get; set; }
        public Color ToolbarButtonChecked { get; set; }

        // Row backgrounds (full-width bands)
        public Color EqualBack { get; set; }
        public Color InsertBack { get; set; }
        public Color DeleteBack { get; set; }
        public Color ChangedBack { get; set; }
        public Color SnipBack { get; set; }

        // Text colors per row kind
        public Color EqualFore { get; set; }
        public Color InsertFore { get; set; }
        public Color DeleteFore { get; set; }
        public Color ChangedFore { get; set; }
        public Color SnipFore { get; set; }

        // Gutter text (line numbers + markers)
        public Color GutterFore { get; set; }
        public Color MarkerFore { get; set; }

        // Intra-line highlight (the strong band on changed sub-spans)
        public Color IntraHighlight { get; set; }
        public Color IntraHighlightDark { get; set; }

        // Selection
        public Color SelectionBack { get; set; }
        public Color SelectionFore { get; set; }

        // "Restored" marker (rows already written back into the working file — done, green)
        public Color RestoredStripe { get; set; }
        public Color RestoredMarker { get; set; }

        // One-click "accept from HEAD" gutter arrow (blue — a pending action)
        public Color AcceptArrow { get; set; }

        // Staged accept preview (rows queued to take HEAD's line, not yet saved — pending blue)
        public Color StagedBack { get; set; }
        public Color StagedStripe { get; set; }
        public Color StagedMarker { get; set; }

        /// <summary>Background swatch for a row kind — drives the legend color key.</summary>
        public Color SwatchFor(DiffOp op) => op switch
        {
            DiffOp.Insert => InsertBack,
            DiffOp.Delete => DeleteBack,
            DiffOp.Changed => ChangedBack,
            _ => EqualBack,
        };

        public static DiffColorScheme Light { get; } = new DiffColorScheme
        {
            PaneBackground = Color.White,
            GutterBackground = Color.FromArgb(245, 245, 245),
            DividerColor = Color.FromArgb(218, 218, 218),
            SectionHeaderBack = Color.FromArgb(235, 235, 235),
            SectionHeaderFore = Color.FromArgb(90, 90, 90),

            ToolbarBack = Color.FromArgb(244, 245, 247),
            ChromeFore = Color.FromArgb(60, 60, 60),
            ToolbarButtonHover = Color.FromArgb(225, 232, 243),
            ToolbarButtonChecked = Color.FromArgb(200, 218, 245),

            EqualBack = Color.White,
            InsertBack = Color.FromArgb(218, 251, 225),   // green
            DeleteBack = Color.FromArgb(255, 224, 224),   // red
            ChangedBack = Color.FromArgb(255, 237, 200),  // amber (off the highlight yellow)
            SnipBack = Color.FromArgb(240, 240, 240),

            EqualFore = Color.FromArgb(80, 80, 80),
            InsertFore = Color.FromArgb(0, 110, 40),
            DeleteFore = Color.FromArgb(185, 0, 0),
            ChangedFore = Color.FromArgb(140, 90, 0),
            SnipFore = Color.Gray,

            GutterFore = Color.FromArgb(150, 150, 150),
            MarkerFore = Color.FromArgb(60, 60, 60),

            // Stronger, contrasting highlight band so the changed tokens pop on amber.
            IntraHighlight = Color.FromArgb(255, 214, 110),
            IntraHighlightDark = Color.FromArgb(245, 190, 70),

            SelectionBack = Color.FromArgb(180, 210, 255),
            SelectionFore = Color.Black,

            RestoredStripe = Color.FromArgb(0, 150, 60),
            RestoredMarker = Color.FromArgb(0, 140, 55),

            AcceptArrow = Color.FromArgb(0, 102, 204),
            StagedBack = Color.FromArgb(214, 233, 255),   // pending blue (clearly != green)
            StagedStripe = Color.FromArgb(0, 102, 204),
            StagedMarker = Color.FromArgb(0, 92, 184),
        };

        public static DiffColorScheme Dark { get; } = new DiffColorScheme
        {
            PaneBackground = Color.FromArgb(30, 30, 30),
            GutterBackground = Color.FromArgb(40, 40, 40),
            DividerColor = Color.FromArgb(62, 62, 62),
            SectionHeaderBack = Color.FromArgb(45, 45, 45),
            SectionHeaderFore = Color.FromArgb(170, 170, 170),

            ToolbarBack = Color.FromArgb(37, 37, 38),
            ChromeFore = Color.FromArgb(205, 205, 205),
            ToolbarButtonHover = Color.FromArgb(58, 62, 70),
            ToolbarButtonChecked = Color.FromArgb(48, 68, 98),

            EqualBack = Color.FromArgb(30, 30, 30),
            InsertBack = Color.FromArgb(24, 56, 34),      // green
            DeleteBack = Color.FromArgb(64, 26, 26),      // red
            ChangedBack = Color.FromArgb(64, 52, 24),     // amber
            SnipBack = Color.FromArgb(45, 45, 45),

            EqualFore = Color.FromArgb(200, 200, 200),
            InsertFore = Color.FromArgb(120, 220, 120),
            DeleteFore = Color.FromArgb(255, 120, 120),
            ChangedFore = Color.FromArgb(230, 190, 110),
            SnipFore = Color.FromArgb(140, 140, 140),

            GutterFore = Color.FromArgb(120, 120, 120),
            MarkerFore = Color.FromArgb(180, 180, 180),

            IntraHighlight = Color.FromArgb(150, 120, 40),
            IntraHighlightDark = Color.FromArgb(170, 135, 45),

            SelectionBack = Color.FromArgb(60, 90, 140),
            SelectionFore = Color.White,

            RestoredStripe = Color.FromArgb(70, 200, 110),
            RestoredMarker = Color.FromArgb(90, 210, 130),

            AcceptArrow = Color.FromArgb(90, 165, 255),
            StagedBack = Color.FromArgb(28, 48, 74),      // pending blue
            StagedStripe = Color.FromArgb(90, 165, 255),
            StagedMarker = Color.FromArgb(110, 180, 255),
        };

        /// <summary>Pick a default scheme from the current system theme.</summary>
        public static DiffColorScheme Detect() =>
            IsDarkSystem() ? Dark : Light;

        /// <summary>True when this instance is the dark preset.</summary>
        public bool IsDark => ReferenceEquals(this, Dark);

        // Simple luminance test on SystemColors.Control — good enough for Win10/11.
        private static bool IsDarkSystem()
        {
            try
            {
                Color c = SystemColors.Control;
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return lum < 0.5;
            }
            catch { return false; }
        }
    }
}
