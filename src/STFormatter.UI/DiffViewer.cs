using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using STBud.Git.Diff;
using STFormatter.Core.Formatting;
using STFormatter.UI.DiffRendering;

namespace STFormatter.UI
{
    public class DiffViewerForm : Form
    {
        // Restore callback: (committed, working, sectionTag, pid) => applied?
        // The Host finds `working` (current text of the block) in the editor and replaces
        // it with `committed`. sectionTag is "decl"/"impl" or null; pid identifies the
        // originating TcXaeShell instance (0 when unknown). Returns true only on a unique
        // content match; otherwise the Host copies `committed` to the clipboard.
        private readonly Func<string, string, string?, int, bool>? _restoreCallback;
        private readonly int _pid;

        // Path of the working file on disk (set for Git diffs so a successful restore
        // can re-read the file and refresh the diff). Null for the format-history viewer,
        // which has no on-disk file to re-read.
        private readonly string? _workingFilePath;
        // Whether the diff was loaded as section-aware (decl/impl). Drives
        // RefreshWorkingFromDisk to re-extract sections vs. re-read raw text.
        private bool _sectionAware;
        // The original (committed / left) side, preserved at load time so a refresh
        // after restore only re-reads the working file, not the (unchanged) original.
        private string _originalCombined = "";
        private TwinCatStExtractor.StSections? _originalSections;

        private DiffCanvas _canvas = null!;
        private Panel _contentPanel = null!;
        private Panel _noChangesPanel = null!;
        private Label _statsLabel = null!;
        private Label _counterLabel = null!;
        private Label _applyStatus = null!;
        private bool _changesOnly;
        private bool _unified;
        private Button _changesOnlyBtn = null!;
        private Button _unifiedBtn = null!;
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 4000 };

        private Font _normalFont = null!;
        private float _fontSize = 10f;

        private List<DiffRow> _allDiffLines = new List<DiffRow>();
        private List<DiffRow> _rendered = new List<DiffRow>();
        private List<int> _changeBlockRows = new List<int>(); // first visual row of each change block
        private int _currentBlock = -1;

        // Compare-tool interaction: right-click menu, gutter-arrow target row, edit mode.
        private ContextMenuStrip _contextMenu = null!;
        private int _contextRow = -1;
        private bool _editMode;
        private RichTextBox _editBox = null!;
        private string _editOriginal = "";
        private Button? _saveBtn;
        private Button? _cancelBtn;
        private Button? _saveAcceptsBtn; // writes staged accepts to the editor
        private Button? _clearStagedBtn; // clears all staged (previewed) accepts
        private Button? _undoBtn;        // reverts the last save (file snapshot)
        private bool _undoAvailable;     // a save snapshot exists to undo

        // Theming + compact icon toolbar.
        private DiffColorScheme _scheme = DiffColorScheme.Detect();
        private Panel _toolbar = null!;
        private FlowLayoutPanel _bar = null!;
        private Panel _legendStrip = null!;
        private Button _themeBtn = null!;
        private readonly List<Button> _toolButtons = new List<Button>();
        private Font _glyphFont = null!;  // Segoe UI Symbol — the toolbar glyph font
        private readonly List<(Panel box, string kind)> _legendBoxes = new List<(Panel, string)>();
        private readonly List<Label> _legendLabels = new List<Label>();

        public DiffViewerForm(string title, string originalText, string formattedText)
            : this(title, originalText, formattedText, null, null, null, 0, null)
        {
        }

        /// <summary>
        /// Diff viewer with an optional "restore" action. When <paramref name="restoreCallback"/>
        /// is supplied (the STBud.Git copy-back use), buttons let the user push committed
        /// lines back into the live editor; the callback returns true when applied. With no
        /// callback this is the plain read-only viewer used for format history.
        /// <paramref name="pid"/> identifies the originating TcXaeShell instance so restore
        /// lands in the right editor when more than one is open (0 = unknown).
        /// <paramref name="workingFilePath"/> is the on-disk path of the working file; when
        /// set, a successful restore re-reads it and refreshes the diff so the restored lines
        /// show as unchanged.
        /// </summary>
        public DiffViewerForm(string title, string originalText, string formattedText,
            Func<string, string, string?, int, bool>? restoreCallback, string? leftLabel, string? rightLabel, int pid,
            string? workingFilePath = null)
        {
            _restoreCallback = restoreCallback;
            _pid = pid;
            _workingFilePath = workingFilePath;

            Text = title;
            Size = new Size(1120, 740);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            Font = new Font("Segoe UI", 9f);
            Icon = MainForm.AppIcon;
            AutoScaleMode = AutoScaleMode.Font;
            KeyPreview = true;

            RebuildFonts();
            BuildLayout(
                string.IsNullOrEmpty(leftLabel) ? Strings.Get("Diff.Original") : leftLabel!,
                string.IsNullOrEmpty(rightLabel) ? Strings.Get("Diff.Formatted") : rightLabel!);
            LoadDiff(originalText, formattedText);

            KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Section-aware diff viewer: diffs Declaration and Implementation as two
        /// separate blocks in the same window, tagging each row with its section so
        /// "Restore selected lines" can target the right editor tab. Used for TwinCAT
        /// .TcPOU/.TcDUT/.TcGVL Git diffs. Falls back to a combined view when a side
        /// is empty. <paramref name="pid"/> identifies the originating TcXaeShell.
        /// <paramref name="workingFilePath"/> enables post-restore diff refresh.
        /// </summary>
        public DiffViewerForm(string title, TwinCatStExtractor.StSections original,
            TwinCatStExtractor.StSections formatted,
            Func<string, string, string?, int, bool>? restoreCallback, string? leftLabel, string? rightLabel, int pid,
            string? workingFilePath = null)
            : this(title, original.Combined(), formatted.Combined(), restoreCallback, leftLabel, rightLabel, pid, workingFilePath)
        {
            _sectionAware = true;
            // Rebuild the diff with per-row section tags. The combined view already
            // rendered in the base constructor is replaced by two tagged sub-diffs.
            LoadSectionDiff(original, formatted);
        }

        private void RebuildFonts()
        {
            _normalFont?.Dispose();
            _normalFont = new Font("Consolas", _fontSize);
        }

        private void BuildLayout(string leftLabel, string rightLabel)
        {
            // Segoe UI Symbol covers the toolbar glyphs (▲▼≠≡✓✗⧉⤓✎☀☾) and falls back
            // gracefully if absent. Point size keeps it DPI-independent.
            _glyphFont = new Font("Segoe UI Symbol", 10.5f);

            // The window title bar already shows the comparison; the stats live in the bottom
            // legend strip (no duplicated in-window title).
            _statsLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 340,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 10, 0),
            };

            // --- toolbar (compact icon buttons + tooltips; nothing clips) ---
            _toolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
            _bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Padding = new Padding(6, 4, 6, 0) };

            // Navigation: ‹ counter ›
            _bar.Controls.Add(MakeIcon("\u25B2" /* up triangle */, "Diff.Tip.Prev", (s, e) => Navigate(-1)));
            _counterLabel = new Label
            {
                AutoSize = false,
                Width = 58,
                Height = 26,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = Strings.Get("Diff.NoChangesShort"),
                Margin = new Padding(2, 4, 2, 0),
            };
            _bar.Controls.Add(_counterLabel);
            _bar.Controls.Add(MakeIcon("\u25BC" /* down triangle */, "Diff.Tip.Next", (s, e) => Navigate(+1)));

            _bar.Controls.Add(MakeSeparator());

            // View toggles
            _changesOnlyBtn = MakeIcon("\u2260" /* not equal */, "Diff.Tip.ChangesOnly",
                (s, e) => { _changesOnly = !_changesOnly; UpdateToggleVisual(_changesOnlyBtn, _changesOnly); RenderDiff(); });
            _bar.Controls.Add(_changesOnlyBtn);
            _unifiedBtn = MakeIcon("\u2261" /* triple bar */, "Diff.Tip.Unified",
                (s, e) => { _unified = !_unified; UpdateToggleVisual(_unifiedBtn, _unified); RenderDiff(); });
            _bar.Controls.Add(_unifiedBtn);

            // Font size + copy
            _bar.Controls.Add(MakeIcon("A-", "Diff.Tip.FontDec", (s, e) => ChangeFont(-1)));
            _bar.Controls.Add(MakeIcon("A+", "Diff.Tip.FontInc", (s, e) => ChangeFont(+1)));
            _bar.Controls.Add(MakeIcon("\u29C9" /* two squares */, "Diff.Tip.Copy", (s, e) => CopyFocusedSelection()));

            if (_restoreCallback != null)
            {
                _bar.Controls.Add(MakeSeparator());
                _bar.Controls.Add(MakeIcon("\u2713" /* check */, "Diff.Tip.AcceptSel", (s, e) => SafeAction(RestoreSelected), "accept"));
                _bar.Controls.Add(MakeIcon("\u2713\u2713" /* double check */, "Diff.Tip.AcceptAll", (s, e) => SafeAction(AcceptAllFromHead), "accept"));

                _clearStagedBtn = MakeIcon("\u2717" /* cross */, "Diff.Tip.ClearStaged", (s, e) => SafeAction(ClearAllStaged), "clear");
                _clearStagedBtn.Enabled = false;
                _bar.Controls.Add(_clearStagedBtn);

                _saveAcceptsBtn = MakeIcon("\u2913" /* down arrow to bar */, "Diff.Tip.Save", (s, e) => SafeAction(SaveAccepts), "save");
                _saveAcceptsBtn.Enabled = false;
                _bar.Controls.Add(_saveAcceptsBtn);

                _undoBtn = MakeIcon("\u21B6" /* undo arrow */, "Diff.Tip.Undo", (s, e) => SafeAction(UndoLastSave), "undo");
                _undoBtn.Enabled = false;
                _bar.Controls.Add(_undoBtn);

                _bar.Controls.Add(MakeSeparator());
                _bar.Controls.Add(MakeIcon("\u270E" /* pencil */, "Diff.Tip.Edit", (s, e) => EnterEditMode()));
                _saveBtn = MakeIcon("\u2913" /* down arrow to bar */, "Diff.Tip.EditSave", (s, e) => SaveEdit(), "save");
                _cancelBtn = MakeIcon("\u2717" /* cross */, "Diff.Tip.EditCancel", (s, e) => ExitEditMode(saved: false), "clear");
                _saveBtn.Visible = false;
                _cancelBtn.Visible = false;
                _bar.Controls.Add(_saveBtn);
                _bar.Controls.Add(_cancelBtn);
            }

            _bar.Controls.Add(MakeSeparator());
            _themeBtn = MakeIcon(_scheme.IsDark ? "\u2600" : "\u263E" /* sun : moon */, "Diff.Tip.Theme", (s, e) => ToggleTheme());
            _bar.Controls.Add(_themeBtn);

            _applyStatus = new Label
            {
                AutoSize = false,
                Width = 160,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 4, 0, 0),
            };
            _bar.Controls.Add(_applyStatus);
            _statusTimer.Tick += (s, e) => { _statusTimer.Stop(); _applyStatus.Text = ""; };

            _toolbar.Controls.Add(_bar);

            // --- color-key legend strip (bottom) ---
            _legendStrip = BuildLegendStrip();

            // --- content (custom-drawn canvas + no-changes overlay) ---
            _contentPanel = new Panel { Dock = DockStyle.Fill };

            _canvas = new DiffCanvas { Dock = DockStyle.Fill, Visible = false };
            _canvas.SetPaneHeaders(leftLabel, rightLabel);
            _canvas.ContextRequested += OnCanvasContextRequested;
            _canvas.AcceptBlockRequested += (pane, row) => AcceptBlockAtRow(row);

            BuildContextMenu();

            // Editable working pane (feature: free-typing edit of the working section).
            _editBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = _normalFont,
                WordWrap = false,
                AcceptsTab = true,
                HideSelection = false,
                Visible = false,
                BorderStyle = BorderStyle.None,
            };

            _noChangesPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            _noChangesPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12f, FontStyle.Italic),
                Text = Strings.Get("Diff.NoChanges"),
            });

            _contentPanel.Controls.Add(_editBox);
            _contentPanel.Controls.Add(_canvas);
            _contentPanel.Controls.Add(_noChangesPanel);

            // Fill control first, then docked edges (WinForms docks in reverse z-order).
            Controls.Add(_contentPanel);
            Controls.Add(_legendStrip);
            Controls.Add(_toolbar);

            _canvas.Scheme = _scheme;
            ApplyTheme();
        }

        // Square, borderless glyph button with a tooltip. AutoScaleMode.Font scales the size;
        // the point-size font keeps the glyph crisp at any DPI. The color role (stored in Tag)
        // is resolved from the active scheme in ApplyTheme so it tracks light/dark.
        private Button MakeIcon(string glyph, string tooltipKey, EventHandler onClick, string role = "neutral")
        {
            var b = new Button
            {
                AutoSize = false,
                Size = new Size(38, 32),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 3, 2, 0),
                TabStop = false,
                UseVisualStyleBackColor = false,
                Font = _glyphFont,
                Text = glyph,
                Tag = role,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            _toolTip.SetToolTip(b, Strings.Get(tooltipKey));
            _toolButtons.Add(b);
            return b;
        }

        // Semantic icon color from the active scheme: accept=green, clear=red, save=blue,
        // undo=amber, everything else neutral chrome.
        private Color IconColor(string? role) => role switch
        {
            "accept" => _scheme.InsertFore,
            "clear" => _scheme.DeleteFore,
            "save" => _scheme.StagedMarker,
            "undo" => _scheme.ChangedFore,
            _ => _scheme.ChromeFore,
        };

        private Panel MakeSeparator() =>
            new Panel { Width = 1, Height = 22, Margin = new Padding(5, 5, 5, 0), Tag = "sep" };

        private void UpdateToggleVisual(Button b, bool on)
        {
            b.BackColor = on ? _scheme.ToolbarButtonChecked : _scheme.ToolbarBack;
            b.FlatAppearance.BorderColor = on ? _scheme.StagedStripe : _scheme.ToolbarBack;
            b.FlatAppearance.BorderSize = on ? 1 : 0;
        }

        private Panel BuildLegendStrip()
        {
            // Bottom bar: color key on the left, change stats on the right.
            var strip = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(8, 4, 0, 0) };
            flow.Controls.Add(MakeLegendItem("ins", "Diff.Legend.Added"));
            flow.Controls.Add(MakeLegendItem("del", "Diff.Legend.Removed"));
            flow.Controls.Add(MakeLegendItem("chg", "Diff.Legend.Changed"));
            if (_restoreCallback != null)
                flow.Controls.Add(MakeLegendItem("stg", "Diff.Legend.Staged"));
            strip.Controls.Add(_statsLabel);
            strip.Controls.Add(flow);
            return strip;
        }

        private Control MakeLegendItem(string kind, string textKey)
        {
            var item = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 16, 0) };
            var box = new Panel { Width = 13, Height = 13, Margin = new Padding(0, 4, 5, 0), BorderStyle = BorderStyle.FixedSingle };
            var lbl = new Label { AutoSize = true, Text = Strings.Get(textKey), Margin = new Padding(0, 2, 0, 0) };
            item.Controls.Add(box);
            item.Controls.Add(lbl);
            _legendBoxes.Add((box, kind));
            _legendLabels.Add(lbl);
            return item;
        }

        // Re-color every non-canvas surface from the active scheme (called on load + theme toggle).
        private void ApplyTheme()
        {
            BackColor = _scheme.PaneBackground;
            _statsLabel.ForeColor = _scheme.SnipFore;
            _statsLabel.BackColor = _scheme.ToolbarBack;
            _toolbar.BackColor = _scheme.ToolbarBack;
            _bar.BackColor = _scheme.ToolbarBack;
            _counterLabel.ForeColor = _scheme.ChromeFore;
            _counterLabel.BackColor = _scheme.ToolbarBack;
            _applyStatus.ForeColor = _scheme.InsertFore;
            _applyStatus.BackColor = _scheme.ToolbarBack;

            foreach (var b in _toolButtons)
            {
                b.BackColor = _scheme.ToolbarBack;
                b.ForeColor = IconColor(b.Tag as string);
                b.FlatAppearance.MouseOverBackColor = _scheme.ToolbarButtonHover;
                b.FlatAppearance.MouseDownBackColor = _scheme.ToolbarButtonChecked;
            }
            UpdateToggleVisual(_changesOnlyBtn, _changesOnly);
            UpdateToggleVisual(_unifiedBtn, _unified);

            foreach (Control c in _bar.Controls)
                if (c is Panel p && (p.Tag as string) == "sep") p.BackColor = _scheme.DividerColor;

            _editBox.BackColor = _scheme.PaneBackground;
            _editBox.ForeColor = _scheme.EqualFore;
            _noChangesPanel.BackColor = _scheme.PaneBackground;
            if (_noChangesPanel.Controls.Count > 0) _noChangesPanel.Controls[0].ForeColor = _scheme.SnipFore;

            ThemeLegend();
        }

        private void ThemeLegend()
        {
            _legendStrip.BackColor = _scheme.ToolbarBack;
            foreach (Control c in _legendStrip.Controls)
            {
                c.BackColor = _scheme.ToolbarBack;
                foreach (Control item in c.Controls) item.BackColor = _scheme.ToolbarBack;
            }
            foreach (var (box, kind) in _legendBoxes)
                box.BackColor = kind switch
                {
                    "ins" => _scheme.InsertBack,
                    "del" => _scheme.DeleteBack,
                    "chg" => _scheme.ChangedBack,
                    "stg" => _scheme.StagedBack,
                    _ => _scheme.EqualBack,
                };
            foreach (var lbl in _legendLabels) { lbl.ForeColor = _scheme.ChromeFore; lbl.BackColor = _scheme.ToolbarBack; }
        }

        private void ToggleTheme()
        {
            _scheme = _scheme.IsDark ? DiffColorScheme.Light : DiffColorScheme.Dark;
            _canvas.Scheme = _scheme;
            _themeBtn.Text = _scheme.IsDark ? "\u2600" : "\u263E"; // sun : moon
            ApplyTheme();
            RenderDiff();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _canvas.InitSplitter();
            _canvas.RebuildFonts(_normalFont);
        }

        protected override void OnResize(EventArgs e) => base.OnResize(e);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _normalFont?.Dispose();
                _glyphFont?.Dispose();
                _statusTimer?.Dispose();
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }

        // ----- loading & rendering -----

        private void LoadDiff(string original, string formatted)
        {
            if (string.IsNullOrEmpty(original) && string.IsNullOrEmpty(formatted))
            {
                _allDiffLines = new List<DiffRow>();
                ShowNoChanges(Strings.Get("Diff.NoInput"));
                return;
            }
            if (string.Equals(original, formatted, StringComparison.Ordinal))
            {
                _allDiffLines = new List<DiffRow>();
                ShowNoChanges(Strings.Get("Diff.NoChanges"));
                return;
            }

            // Compute the diff over the FULL input — never silently truncate, because
            // truncating before diffing can produce a false "No changes" when the real
            // changes are beyond the cap. Large inputs are handled by the Myers path in
            // LineDiff (bounds memory by edit distance). A render cap is enforced in the
            // canvas pane (it only paints visible rows), so memory is bounded regardless.
            _originalCombined = original;
            _allDiffLines = LineDiff.PairChangeRuns(LineDiff.Compute(original, formatted));

            // If the diff is huge, surface a visible banner in the title so the user
            // knows the viewer is showing a large result — but the diff itself is complete.
            if (_allDiffLines.Count > 10000)
                Text += $" — large diff ({_allDiffLines.Count} rows)";

            RenderDiff();
        }

        // Section-aware diff: compute Declaration and Implementation separately, tag each
        // row with "decl"/"impl", and concatenate them with a Snip divider so the viewer
        // shows two logical blocks. Restore uses the tag to target the right editor tab.
        private void LoadSectionDiff(TwinCatStExtractor.StSections original,
            TwinCatStExtractor.StSections formatted)
        {
            _originalSections = original;
            _allDiffLines = new List<DiffRow>();

            bool anyChange = false;
            void AppendSection(string tag, string? oldText, string? newText)
            {
                if (string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText)) return;
                var rows = LineDiff.PairChangeRuns(LineDiff.Compute(oldText ?? "", newText ?? ""));
                if (rows.Count == 0) return;
                if (rows.Any(r => r.Op != DiffOp.Equal))
                    anyChange = true;

                // Section header: a Snip row re-purposed as a visible divider. The
                // renderer paints Snip rows gray; the SectionTag carries the section.
                if (_allDiffLines.Count > 0)
                    _allDiffLines.Add(new DiffRow { Op = DiffOp.Snip, SectionTag = tag });

                foreach (var r in rows)
                {
                    r.SectionTag = tag;
                    _allDiffLines.Add(r);
                }
            }

            AppendSection("decl", original.Declaration, formatted.Declaration);
            AppendSection("impl", original.Implementation, formatted.Implementation);

            if (_allDiffLines.Count == 0)
            {
                ShowNoChanges(Strings.Get("Diff.NoChanges"));
                return;
            }
            if (!anyChange)
            {
                ShowNoChanges(Strings.Get("Diff.NoChanges"));
                return;
            }
            RenderDiff();
        }

        private void ShowNoChanges(string message)
        {
            _canvas.Visible = false;
            _noChangesPanel.Visible = true;
            _statsLabel.Text = "  " + message;
            _counterLabel.Text = Strings.Get("Diff.NoChangesShort");
        }

        private void RenderDiff()
        {
            if (_allDiffLines.Count == 0) return;

            bool hasChanges = _allDiffLines.Any(d => d.Op != DiffOp.Equal && d.Op != DiffOp.Snip);
            if (!hasChanges)
            {
                ShowNoChanges(Strings.Get("Diff.NoChanges"));
                return;
            }

            _noChangesPanel.Visible = false;
            _canvas.Visible = true;

            _rendered = _changesOnly ? LineDiff.FilterToChanges(_allDiffLines, 3) : _allDiffLines;

            if (_unified)
                _canvas.SetRows(ExpandUnified(_rendered));
            else
                _canvas.SetRows(_rendered);

            // Gutter accept arrows only make sense in side-by-side with a restore target.
            _canvas.SetAcceptArrows(_restoreCallback != null && !_unified);

            ComputeChangeBlocks();
            _currentBlock = _changeBlockRows.Count > 0 ? 0 : -1;
            UpdateCounter();

            var (added, removed, changed, unchanged) = LineDiff.DetailedStats(_allDiffLines);
            _statsLabel.Text = $"  +{added}  -{removed}  ~{changed}  |  {unchanged} {Strings.Get("Diff.Unchanged")}";

            if (_currentBlock >= 0) GoToBlock(0);
        }

        // Unified mode: expand Changed rows into a Delete row followed by an Insert row
        // so the single-pane view shows the conventional -/+ form. Section-tagged rows
        // keep their tags so restore still targets the right tab.
        private static List<DiffRow> ExpandUnified(List<DiffRow> rows)
        {
            var expanded = new List<DiffRow>(rows.Count + 8);
            foreach (var r in rows)
            {
                if (r.Op == DiffOp.Changed)
                {
                    expanded.Add(new DiffRow { Op = DiffOp.Delete, Left = r.Left, LeftLine = r.LeftLine, SectionTag = r.SectionTag });
                    expanded.Add(new DiffRow { Op = DiffOp.Insert, Right = r.Right, RightLine = r.RightLine, SectionTag = r.SectionTag });
                }
                else expanded.Add(r);
            }
            return expanded;
        }

        // ----- navigation -----

        private void ComputeChangeBlocks()
        {
            _changeBlockRows = new List<int>();
            bool prevChange = false;
            for (int r = 0; r < _rendered.Count; r++)
            {
                bool isChange = _rendered[r].Op == DiffOp.Insert
                    || _rendered[r].Op == DiffOp.Delete
                    || _rendered[r].Op == DiffOp.Changed;
                if (isChange && !prevChange) _changeBlockRows.Add(r);
                prevChange = isChange;
            }
        }

        private void Navigate(int delta)
        {
            if (_changeBlockRows.Count == 0) return;
            int next = _currentBlock < 0 ? (delta > 0 ? 0 : _changeBlockRows.Count - 1) : _currentBlock + delta;
            next = Math.Max(0, Math.Min(_changeBlockRows.Count - 1, next));
            GoToBlock(next);
        }

        private void GoToBlock(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= _changeBlockRows.Count) return;
            _currentBlock = blockIndex;
            int row = _changeBlockRows[blockIndex];

            // In unified mode the row index is in the expanded list; map back to the
            // _rendered index, then forward to the expanded index for scrolling.
            int visualRow = row;
            if (_unified)
            {
                // Count expanded rows up to `row` in _rendered (each Changed adds 1 extra).
                int extra = 0;
                for (int i = 0; i < row && i < _rendered.Count; i++)
                    if (_rendered[i].Op == DiffOp.Changed) extra++;
                visualRow = row + extra;
            }

            _canvas.ScrollToRow(visualRow);
            UpdateCounter();
        }

        private void UpdateCounter()
        {
            _counterLabel.Text = _changeBlockRows.Count == 0
                ? Strings.Get("Diff.NoChangesShort")
                : Strings.Get("Diff.Counter", _currentBlock + 1, _changeBlockRows.Count);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F8) { Navigate(e.Shift ? -1 : +1); e.Handled = true; }
        }

        // ----- font -----

        private void ChangeFont(int delta)
        {
            _fontSize = Math.Max(7f, Math.Min(22f, _fontSize + delta));
            RebuildFonts();
            _canvas.RebuildFonts(_normalFont);
            RenderDiff();
        }

        // ----- copy / restore -----

        private void CopyFocusedSelection()
        {
            // Copy from whichever pane was last clicked; default to the left (committed).
            var pane = _canvas.LeftPane;
            string text = pane.GetSelectedText();
            if (string.IsNullOrEmpty(text)) text = _canvas.RightPane.GetSelectedText();
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch (Exception ex) { DiagLog($"Copy: clipboard set failed: {ex.Message}"); }
        }

        private static bool IsChangeOp(DiffOp op) =>
            op == DiffOp.Insert || op == DiffOp.Delete || op == DiffOp.Changed;

        // Toolbar "Accept selected": stage exactly the selected line(s) from HEAD (one-way).
        private void RestoreSelected()
        {
            if (_restoreCallback == null) return;

            // In unified view the canvas selection is in expanded-row space, which doesn't
            // map to _rendered; fall back to the current change block's first line.
            if (_unified) { RestoreCurrentChange(); return; }

            var (s, en) = _canvas.GetSelectedRowRange();
            if (s < 0) { RestoreCurrentChange(); return; }
            StageRows(s, en, true);
        }

        private void RestoreCurrentChange()
        {
            if (_restoreCallback == null) return;
            if (_currentBlock < 0 || _currentBlock >= _changeBlockRows.Count) return;
            StageRows(_changeBlockRows[_currentBlock], _changeBlockRows[_currentBlock], true);
        }

        // ----- compare-tool: context menu, gutter arrows, accept-all, edit mode -----

        private ToolStripItem? _miAcceptLine, _miUnaccept, _miAcceptAll, _miClearAll;

        private void BuildContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            if (_restoreCallback != null)
            {
                _miAcceptLine = _contextMenu.Items.Add(Strings.Get("Diff.Ctx.AcceptLine"), null, (s, e) => SafeAction(AcceptContextRows));
                _miUnaccept = _contextMenu.Items.Add(Strings.Get("Diff.Ctx.Unaccept"), null, (s, e) => SafeAction(UnacceptContextRows));
                _contextMenu.Items.Add(new ToolStripSeparator());
            }
            _contextMenu.Items.Add(Strings.Get("Diff.Ctx.Copy"), null, (s, e) => SafeAction(CopyFocusedSelection));
            if (_restoreCallback != null)
            {
                _contextMenu.Items.Add(new ToolStripSeparator());
                _miAcceptAll = _contextMenu.Items.Add(Strings.Get("Diff.Ctx.AcceptAll"), null, (s, e) => SafeAction(AcceptAllFromHead));
                _miClearAll = _contextMenu.Items.Add(Strings.Get("Diff.ClearStaged"), null, (s, e) => SafeAction(ClearAllStaged));
            }
        }

        // Never let a single failed action (COM hiccup, etc.) take down the dialog.
        private void SafeAction(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                DiagLog($"context action failed: {ex.Message}");
                try { MessageBox.Show(this, ex.Message, Strings.Get("App.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void OnCanvasContextRequested(DiffPane pane, Point screen, int row)
        {
            if (_editMode) return;

            // Accept actions need side-by-side row alignment; in unified view the canvas
            // rows are expanded (Changed → 2 rows) and don't match _rendered, so disable them.
            bool canAccept = _restoreCallback != null && !_unified;
            bool anyStaged = _allDiffLines.Any(r => r.Restored && IsChangeOp(r.Op));
            if (_miAcceptLine != null) _miAcceptLine.Enabled = canAccept;
            if (_miUnaccept != null) _miUnaccept.Enabled = canAccept && anyStaged;
            if (_miAcceptAll != null) _miAcceptAll.Enabled = canAccept;
            if (_miClearAll != null) _miClearAll.Enabled = canAccept && anyStaged;

            _contextRow = row;
            _contextMenu.Show(screen);
        }

        // The rows a context action targets: the selection if the click landed inside it,
        // otherwise just the right-clicked line (keeps accept line-granular).
        private (int s, int en) ContextRange()
        {
            var (ss, se) = _canvas.GetSelectedRowRange();
            if (ss >= 0 && _contextRow >= ss && _contextRow <= se) return (ss, se);
            return (_contextRow, _contextRow);
        }

        private void AcceptContextRows()
        {
            if (_restoreCallback == null || _unified || _contextRow < 0) return;
            var (s, en) = ContextRange();
            StageRows(s, en, true);
        }

        private void UnacceptContextRows()
        {
            if (_restoreCallback == null || _unified || _contextRow < 0) return;
            var (s, en) = ContextRange();
            StageRows(s, en, false);
        }

        // Gutter ▶ arrow: stage the single clicked line from HEAD (one-way — never un-stages).
        private void AcceptBlockAtRow(int row)
        {
            if (_restoreCallback == null || _unified) return;
            StageRows(row, row, true);
        }

        // Stage every change as a preview (the user reviews, then Save writes to the editor).
        private void AcceptAllFromHead()
        {
            if (_restoreCallback == null) return;
            if (!_allDiffLines.Any(r => IsChangeOp(r.Op))) return;
            foreach (var r in _allDiffLines)
                if (IsChangeOp(r.Op)) r.Restored = true;
            _canvas.Repaint();
            UpdateStagedUi();
        }

        private void EnterEditMode()
        {
            if (_editMode || _restoreCallback == null) return;
            string? current = GitEditorBridge.ReadEditorSection?.Invoke(_pid);
            if (string.IsNullOrEmpty(current))
            {
                MessageBox.Show(this, Strings.Get("Diff.Edit.NoEditor"), Strings.Get("App.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _editOriginal = current!;
            _editBox.Font = _normalFont;
            _editBox.Text = current!;
            _editMode = true;

            _canvas.Visible = false;
            _noChangesPanel.Visible = false;
            _editBox.Visible = true;
            _editBox.BringToFront();
            if (_saveBtn != null) _saveBtn.Visible = true;
            if (_cancelBtn != null) _cancelBtn.Visible = true;
            _statsLabel.Text = "  " + Strings.Get("Diff.Edit.Active");
        }

        private void SaveEdit()
        {
            if (!_editMode) return;
            // Replace the section we read (working) with the edited text (committed-arg).
            if (InvokeRestore(_editBox.Text, _editOriginal, null))
                ExitEditMode(saved: true);
            // on false the Host already showed why (not found / clipboard) — stay in edit mode.
        }

        private void ExitEditMode(bool saved)
        {
            _editMode = false;
            _editBox.Visible = false;
            if (_saveBtn != null) _saveBtn.Visible = false;
            if (_cancelBtn != null) _cancelBtn.Visible = false;

            bool hasChanges = _allDiffLines.Any(d => d.Op != DiffOp.Equal && d.Op != DiffOp.Snip);
            _canvas.Visible = hasChanges;
            _noChangesPanel.Visible = !hasChanges;
            if (hasChanges) RenderDiff();

            if (saved)
            {
                _applyStatus.Text = Strings.Get("Diff.Edit.Saved");
                _statusTimer.Stop();
                _statusTimer.Start();
                // Same post-save behavior as SaveAccepts: re-diff against the file on disk
                // so the view reflects reality instead of the pre-edit rows.
                RefreshWorkingFromDisk();
            }
        }

        // Set the staged-accept flag on the change rows in [s,en] (preview only — no editor
        // write yet). on=true stages (one-way: accepting never clears), on=false un-stages.
        // The working pane then previews HEAD's line (changed/deleted) or strikes the line
        // (added → will be removed); the actual write happens when the user clicks Save.
        private void StageRows(int s, int en, bool on)
        {
            if (s < 0 || s >= _rendered.Count) return;
            if (en >= _rendered.Count) en = _rendered.Count - 1;

            bool any = false;
            for (int r = s; r <= en; r++)
                if (IsChangeOp(_rendered[r].Op) && _rendered[r].Restored != on) { _rendered[r].Restored = on; any = true; }

            if (!any) return;
            DiagLog($"StageRows: rows={s}..{en} staged={on}");
            _canvas.Repaint();
            UpdateStagedUi();
        }

        // Drop every staged accept (the "undo" for one-way accepts).
        private void ClearAllStaged()
        {
            bool any = false;
            foreach (var r in _allDiffLines)
                if (r.Restored && IsChangeOp(r.Op)) { r.Restored = false; any = true; }
            if (!any) return;
            DiagLog("ClearAllStaged");
            _canvas.Repaint();
            UpdateStagedUi();
        }

        // Reflect the number of staged accepts on the Save icon (enabled + count in its tooltip).
        private void UpdateStagedUi()
        {
            int staged = _allDiffLines.Count(r => r.Restored && IsChangeOp(r.Op));
            if (_saveAcceptsBtn != null)
            {
                _saveAcceptsBtn.Enabled = staged > 0;
                _toolTip.SetToolTip(_saveAcceptsBtn, staged > 0
                    ? Strings.Get("Diff.Tip.SaveN", staged)
                    : Strings.Get("Diff.Tip.Save"));
            }
            if (_clearStagedBtn != null) _clearStagedBtn.Enabled = staged > 0;
        }

        // Write every staged accept straight to the working file on disk, one block per
        // contiguous run of staged change rows. The diff's working side was read from this
        // file, so each block locates deterministically (no editor/section guesswork). After
        // writing we re-read the file and re-diff, so applied accepts drop out and any block
        // that couldn't be placed stays visible.
        private void SaveAccepts()
        {
            if (!_allDiffLines.Any(r => r.Restored && IsChangeOp(r.Op))) return;

            var blocks = new List<(string committed, string working, string? section)>();
            int skipped = 0;
            int i = 0;
            while (i < _allDiffLines.Count)
            {
                var row = _allDiffLines[i];
                if (!(row.Restored && IsChangeOp(row.Op))) { i++; continue; }

                int s = i, e = i;
                while (e + 1 < _allDiffLines.Count)
                {
                    var nxt = _allDiffLines[e + 1];
                    if (nxt.Restored && IsChangeOp(nxt.Op) && nxt.SectionTag == row.SectionTag) e++;
                    else break;
                }

                var blk = LineDiff.ExtractAcceptBlock(_allDiffLines, s, e);
                if (blk.Working.Length == 0)
                {
                    // Re-adding HEAD-only (deleted) lines: there's nothing of theirs in the
                    // working file to locate, so anchor the block on an adjacent UNCHANGED
                    // line instead — the disk write then replaces `anchor` with
                    // `anchor + restored lines` (or `restored + anchor` when anchoring after).
                    int anchor = -1;
                    if (s > 0 && _allDiffLines[s - 1].Op == DiffOp.Equal
                              && _allDiffLines[s - 1].SectionTag == row.SectionTag)
                        anchor = s - 1;
                    else if (e + 1 < _allDiffLines.Count && _allDiffLines[e + 1].Op == DiffOp.Equal
                              && _allDiffLines[e + 1].SectionTag == row.SectionTag)
                        anchor = e + 1;

                    if (anchor >= 0)
                    {
                        blk = LineDiff.ExtractAcceptBlock(_allDiffLines, Math.Min(anchor, s), Math.Max(anchor, e));
                        DiagLog($"SaveAccepts: delete-only run rows={s}..{e} anchored on row {anchor}");
                    }
                }

                if (blk.Working.Length == 0)
                {
                    // No usable anchor either (rare): clipboard fallback so the user can paste.
                    DiagLog($"SaveAccepts: rows={s}..{e} has no anchor - clipboard fallback");
                    if (!string.IsNullOrEmpty(blk.Committed))
                        try { Clipboard.SetText(blk.Committed); } catch (Exception ex) { DiagLog($"SaveAccepts: clipboard fallback failed: {ex.Message}"); }
                    skipped++;
                }
                else
                {
                    blocks.Add((blk.Committed, blk.Working, blk.Section));
                }
                i = e + 1;
            }

            int applied = 0, failed = 0;
            if (blocks.Count > 0)
            {
                if (GitEditorBridge.WriteAcceptsToDisk != null && !string.IsNullOrEmpty(_workingFilePath))
                {
                    DiagLog($"SaveAccepts: writing {blocks.Count} block(s) to {_workingFilePath}");
                    var res = GitEditorBridge.WriteAcceptsToDisk!(_workingFilePath!, blocks, _pid);
                    applied = res.applied; failed = res.failed;
                    DiagLog($"SaveAccepts: applied={applied} failed={failed}");
                }
                else if (_restoreCallback != null)
                {
                    // No working-file path (non-Git viewer): fall back to per-block live edit.
                    foreach (var b in blocks)
                        if (InvokeRestore(b.committed, b.working, b.section)) applied++; else failed++;
                }
            }

            if (applied > 0)
            {
                _undoAvailable = true;
                _applyStatus.Text = Strings.Get("Diff.Saved.Status", applied);
                _statusTimer.Stop(); _statusTimer.Start();
                RefreshWorkingFromDisk();
                UpdateStagedUi();
                UpdateUndoUi();
            }

            int notPlaced = failed + skipped;
            if (notPlaced > 0)
            {
                MessageBox.Show(this,
                    Strings.Get(applied > 0 ? "Diff.Saved.Partial" : "Diff.Saved.None", notPlaced),
                    Strings.Get("App.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Revert the last save by writing the Host's file snapshot back, then re-diff from disk.
        private void UndoLastSave()
        {
            if (!_undoAvailable || GitEditorBridge.UndoLastSave == null) return;
            if (!GitEditorBridge.UndoLastSave!(_pid)) return;

            _undoAvailable = false;
            _applyStatus.Text = Strings.Get("Diff.Undo.Status");
            _statusTimer.Stop(); _statusTimer.Start();
            RefreshWorkingFromDisk();
            UpdateStagedUi();
            UpdateUndoUi();
        }

        private void UpdateUndoUi()
        {
            if (_undoBtn != null) _undoBtn.Enabled = _undoAvailable;
        }

        // Re-read the working file from disk and re-diff against the stored HEAD snapshot.
        // Used after a disk-write save and after undo so the view reflects the file exactly.
        private void RefreshWorkingFromDisk()
        {
            try
            {
                if (string.IsNullOrEmpty(_workingFilePath) || !System.IO.File.Exists(_workingFilePath)) return;
                string raw = System.IO.File.ReadAllText(_workingFilePath!);
                if (_sectionAware && _originalSections != null)
                    LoadSectionDiff(_originalSections, TwinCatStExtractor.Extract(raw));
                else
                    LoadDiff(_originalCombined, TwinCatStExtractor.ExtractCombinedOrRaw(raw));
            }
            catch (Exception ex) { DiagLog($"RefreshWorkingFromDisk: {ex.Message}"); }
        }

        // One log for everything: the diff viewer's diagnostics land in STBud_Host.log
        // under the DiffViewer tag (the separate STBud_DiffViewer.log is retired).
        private static void DiagLog(string message) =>
            STFormatter.Core.Configuration.HostLog.Append("DiffViewer", message);

        private bool InvokeRestore(string committed, string working, string? sectionTag)
        {
            try
            {
                DiagLog($"InvokeRestore: committed.len={committed.Length} working.len={working.Length} section={sectionTag ?? "null"} pid={_pid}");
                bool ok = _restoreCallback!(committed, working, sectionTag, _pid);
                DiagLog($"InvokeRestore: callback returned {ok}");
                if (ok)
                {
                    int lines = committed.Length == 0 ? 0 : committed.Split('\n').Length;
                    _applyStatus.Text = Strings.Get("Diff.Apply.Status", lines);
                    _statusTimer.Stop();
                    _statusTimer.Start();
                }
                // On false the Host has already given the user feedback (a balloon: not
                // found / appears multiple times / clipboard fallback) — no modal needed.
                return ok;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Strings.Get("App.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // ----- helpers -----
    }
}
