using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using STBud.Git.Diff;

namespace STFormatter.UI.DiffRendering
{
    /// <summary>
    /// One custom-drawn side of a side-by-side diff. Paints full-width row bands
    /// (the classic RichTextBox limitation that didn't reach line width is gone), a
    /// real gutter overlay (line numbers + markers) separate from the text so Copy
    /// yields clean ST, word-level intra-line highlight on Changed rows, row-based
    /// selection, and HiDPI-aware layout. Dark-mode colors come from
    /// <see cref="DiffColorScheme"/>.
    ///
    /// Selection is row-based (click/drag selects whole rows); <see cref="GetSelectedText"/>
    /// returns clean ST text without the gutter, so copy yields clean code.
    /// </summary>
    public sealed class DiffPane : Control
    {
        public enum SideKind { Left, Right }

        private readonly SideKind _side;
        private List<DiffRow> _rows = new List<DiffRow>();
        private DiffColorScheme _scheme = DiffColorScheme.Detect();

        private Font _font = null!;
        private Font _boldFont = null!;
        private Font _strikeFont = null!; // staged-removal preview (added lines that Save will drop)
        private int _lineHeight;
        private int _gutterWidth;
        private int _charWidth;

        // Visible scroll range — driven by the parent's scroll bar (the parent
        // owns a single VScrollBar + HScrollBar shared between the two panes).
        public int FirstVisibleRow { get; set; }
        public int HorizontalOffset { get; set; }
        public int MaxHorizontalExtent { get; private set; }

        // Row-based selection (inclusive). -1 = no selection.
        public int SelStartRow { get; private set; } = -1;
        public int SelEndRow { get; private set; } = -1;

        // For each visual row we render, the restorable committed text (null for
        // filler/snip/inserted rows on the left side) and the section tag, so the
        // parent diff form can drive "Restore selected lines" + tab targeting.
        public IReadOnlyList<string?> CommittedRowText => _committedRowText;
        public IReadOnlyList<string?> CommittedRowSection => _committedRowSection;
        private readonly List<string?> _committedRowText = new List<string?>();
        private readonly List<string?> _committedRowSection = new List<string?>();

        public event Action<DiffPane>? Scrolled;
        public event Action<DiffPane>? SelectionChanged;
        // Right-click on a row: (pane, screen point, row).
        public event Action<DiffPane, Point, int>? ContextRequested;
        // One-click gutter accept arrow on a change block: (pane, block-start row).
        public event Action<DiffPane, int>? AcceptBlockRequested;

        // Show a clickable ▶ "accept from HEAD" arrow at the start of each change block
        // (enabled on the working/right pane only).
        public bool ShowAcceptArrows { get; set; }
        private readonly HashSet<int> _blockStartRows = new HashSet<int>();
        private int _arrowZoneWidth;

        private static bool IsChangeOp(DiffOp op) =>
            op == DiffOp.Insert || op == DiffOp.Delete || op == DiffOp.Changed;

        public DiffPane(SideKind side)
        {
            _side = side;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = _scheme.PaneBackground;
            DoubleBuffered = true;
            RebuildFonts(new Font("Consolas", 10f));
        }

        public DiffColorScheme Scheme
        {
            get => _scheme;
            set { _scheme = value; BackColor = value.PaneBackground; Invalidate(); }
        }

        public void RebuildFonts(Font font)
        {
            _font?.Dispose();
            _boldFont?.Dispose();
            _strikeFont?.Dispose();
            _font = (Font)font.Clone();
            _boldFont = new Font(font, FontStyle.Bold);
            _strikeFont = new Font(font, FontStyle.Strikeout);

            using (var g = CreateGraphics())
            {
                _lineHeight = (int)Math.Ceiling(g.MeasureString("Ag", _font, 9999, StringFormat.GenericTypographic).Height) + 2;
                _charWidth = (int)Math.Ceiling(g.MeasureString("M", _font, 9999, StringFormat.GenericTypographic).Width) + 1;
            }
            _gutterWidth = _charWidth * 7; // room for "9999 + "
            _arrowZoneWidth = _charWidth * 2; // left of the line number — holds the ▶ arrow
        }

        public void SetRows(List<DiffRow> rows)
        {
            _rows = rows;
            _committedRowText.Clear();
            _committedRowSection.Clear();
            MaxHorizontalExtent = 0;
            using (var g = CreateGraphics())
            {
                foreach (var r in rows)
                {
                    string? text = RowText(r);
                    if (text != null)
                    {
                        int w = (int)Math.Ceiling(g.MeasureString(text, _font, 9999, StringFormat.GenericTypographic).Width);
                        if (w > MaxHorizontalExtent) MaxHorizontalExtent = w;
                    }
                }
            }
            // Build the committed-row parallel arrays (left side only).
            if (_side == SideKind.Left)
            {
                foreach (var r in rows)
                {
                    bool present = r.Op != DiffOp.Insert && r.Op != DiffOp.Snip;
                    _committedRowText.Add(present ? r.Left : null);
                    _committedRowSection.Add(r.SectionTag);
                }
            }

            // Mark the first row of each change block (consecutive Insert/Delete/Changed).
            _blockStartRows.Clear();
            bool prevChange = false;
            for (int i = 0; i < rows.Count; i++)
            {
                bool isChange = IsChangeOp(rows[i].Op);
                if (isChange && !prevChange) _blockStartRows.Add(i);
                prevChange = isChange;
            }

            SelStartRow = SelEndRow = -1;
            Invalidate();
        }

        public int RowCount => _rows.Count;
        public int VisibleRowCount => Math.Max(0, Height / _lineHeight);

        private string? RowText(DiffRow r) => _side == SideKind.Left ? r.Left : r.Right;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(_scheme.PaneBackground);

            int visibleRows = VisibleRowCount;
            int maxRow = Math.Min(_rows.Count, FirstVisibleRow + visibleRows);

            StringFormat sf = StringFormat.GenericTypographic;
            sf.FormatFlags |= StringFormatFlags.NoWrap;

            for (int vi = 0, row = FirstVisibleRow; row < maxRow; row++, vi++)
            {
                var r = _rows[row];
                int y = vi * _lineHeight;
                Color back, fore, markerFore;
                string marker;

                if (r.Op == DiffOp.Snip)
                {
                    back = _scheme.SnipBack;
                    fore = _scheme.SnipFore;
                    marker = " ";
                    markerFore = _scheme.SnipFore;
                }
                else
                {
                    (back, fore) = r.Op switch
                    {
                        DiffOp.Equal => (_scheme.EqualBack, _scheme.EqualFore),
                        DiffOp.Insert => (_scheme.InsertBack, _scheme.InsertFore),
                        DiffOp.Delete => (_scheme.DeleteBack, _scheme.DeleteFore),
                        DiffOp.Changed => (_scheme.ChangedBack, _scheme.ChangedFore),
                        _ => (_scheme.EqualBack, _scheme.EqualFore),
                    };
                    marker = r.Op == DiffOp.Insert ? "+"
                           : r.Op == DiffOp.Delete ? "-"
                           : r.Op == DiffOp.Changed ? "~"
                           : " ";
                    markerFore = r.Op == DiffOp.Insert ? _scheme.InsertFore
                               : r.Op == DiffOp.Delete ? _scheme.DeleteFore
                               : r.Op == DiffOp.Changed ? _scheme.ChangedFore
                               : _scheme.GutterFore;
                }

                // Working-pane preview of a staged accept (what the line becomes after Save):
                // changed/deleted rows take HEAD's line; added rows are struck (Save removes them).
                Font contentFont = _font;
                bool stagedPreview = _side == SideKind.Right && r.Restored && IsChangeOp(r.Op);
                if (stagedPreview)
                {
                    back = _scheme.StagedBack;
                    if (r.Op == DiffOp.Insert) { fore = _scheme.SnipFore; contentFont = _strikeFont; }
                    else fore = _scheme.EqualFore;
                }

                // Selection highlight overrides the row band.
                bool selected = row >= SelStartRow && row <= SelEndRow;
                if (selected) back = _scheme.SelectionBack;

                // Full-width row band.
                g.FillRectangle(new SolidBrush(back), 0, y, Width, _lineHeight);

                // Gutter background.
                g.FillRectangle(new SolidBrush(_scheme.GutterBackground), 0, y, _gutterWidth, _lineHeight);

                // Staged/restored rows get a left stripe + ✓ marker. A still-changed row is
                // *staged* (pending Save → blue); a collapsed Equal row is *saved* (green).
                if (r.Op != DiffOp.Snip && r.Restored)
                {
                    bool pending = IsChangeOp(r.Op);
                    g.FillRectangle(new SolidBrush(pending ? _scheme.StagedStripe : _scheme.RestoredStripe), 0, y, 3, _lineHeight);
                    marker = "\u2713"; // check
                    markerFore = pending ? _scheme.StagedMarker : _scheme.RestoredMarker;
                }

                // Section header text for snip rows that carry a section tag.
                string? text = RowText(r);
                if (r.Op == DiffOp.Snip && !string.IsNullOrEmpty(r.SectionTag))
                {
                    string header = r.SectionTag == "decl" ? "-- DECLARATION --"
                                   : r.SectionTag == "impl" ? "-- IMPLEMENTATION --"
                                   : "...";
                    g.DrawString(header, _boldFont, new SolidBrush(_scheme.SectionHeaderFore),
                        _gutterWidth + 4 - HorizontalOffset, y, sf);
                    continue;
                }

                // Line number.
                int lineNum = _side == SideKind.Left ? r.LeftLine : r.RightLine;
                if (lineNum > 0)
                    g.DrawString(lineNum.ToString(), _font, new SolidBrush(_scheme.GutterFore),
                        _gutterWidth - _charWidth * 5, y, sf);

                // One-click "accept this line from HEAD" arrow on every un-staged change line.
                if (ShowAcceptArrows && IsChangeOp(r.Op) && !r.Restored)
                    g.DrawString("\u25B6" /* accept arrow */, _boldFont, new SolidBrush(_scheme.AcceptArrow), 1, y, sf);

                // Marker (+ / - / ~).
                g.DrawString(marker, _boldFont, new SolidBrush(markerFore), _gutterWidth - _charWidth, y, sf);

                // Content. A staged changed/deleted accept previews HEAD's line (r.Left)
                // on the working pane instead of the current working text.
                int contentX = _gutterWidth + 2 - HorizontalOffset;
                string? content = (stagedPreview && r.Op != DiffOp.Insert) ? r.Left : text;
                if (!string.IsNullOrEmpty(content))
                {
                    if (r.Op == DiffOp.Changed && !stagedPreview)
                        DrawIntraLineHighlight(g, r, contentX, y, fore, sf);
                    else
                        g.DrawString(content ?? "", contentFont, new SolidBrush(fore), contentX, y, sf);
                }
            }

            // Divider line at the gutter edge.
            using (var pen = new Pen(_scheme.DividerColor, 1))
                g.DrawLine(pen, _gutterWidth, 0, _gutterWidth, Height);
        }

        // Word-level intra-line highlight: the differing tokens get a stronger band.
        // The diff is always computed Left-vs-Right in canonical order, and each pane draws
        // ONLY its own side's tokens, in order. Concatenating one side's tokens reproduces that
        // line verbatim (whitespace tokens included), so spaces are preserved and the other
        // side's tokens never bleed in (the old `seg.Left ?? seg.Right` fallback did both).
        private void DrawIntraLineHighlight(Graphics g, DiffRow row, int x, int y,
            Color fore, StringFormat sf)
        {
            var segments = IntraLineHighlight.Compute(row.Left, row.Right);
            var highlightBrush = new SolidBrush(_scheme.IntraHighlight);

            float px = x;
            foreach (var seg in segments)
            {
                IntraLineHighlight.Token? sideTok = _side == SideKind.Left ? seg.Left : seg.Right;
                if (sideTok == null) continue;
                var tok = sideTok.Value;
                if (tok.Length == 0) continue;

                float w = g.MeasureString(tok.Text, _font, 9999, sf).Width;
                if (!seg.Equal)
                {
                    // Strong band under the changed token.
                    g.FillRectangle(highlightBrush, px, y, w, _lineHeight);
                    g.DrawString(tok.Text, _boldFont, new SolidBrush(fore), px, y, sf);
                }
                else
                {
                    g.DrawString(tok.Text, _font, new SolidBrush(fore), px, y, sf);
                }
                px += w;
            }
        }

        // ----- mouse selection (row-based) -----

        private int RowAtY(int y) => FirstVisibleRow + (y / _lineHeight);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int row = RowAtY(e.Y);

            if (e.Button == MouseButtons.Left)
            {
                // Gutter accept arrow: one-click accept of this single line (one-way — only
                // fires on an un-staged change line, so clicking a staged row does nothing).
                if (ShowAcceptArrows && e.X >= 0 && e.X < _arrowZoneWidth
                    && row >= 0 && row < _rows.Count && IsChangeOp(_rows[row].Op) && !_rows[row].Restored)
                {
                    AcceptBlockRequested?.Invoke(this, row);
                    base.OnMouseDown(e);
                    return;
                }

                if (row >= 0 && row < _rows.Count)
                {
                    // Shift-click extends the existing selection; plain click resets it.
                    if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && SelStartRow >= 0)
                        SelEndRow = row;
                    else
                        SelStartRow = SelEndRow = row;
                    Focus();
                    Invalidate();
                    SelectionChanged?.Invoke(this);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (row >= 0 && row < _rows.Count)
                {
                    // Select the right-clicked row unless it's already inside the selection.
                    var (s, en) = GetSelection();
                    if (row < s || row > en)
                    {
                        SelStartRow = SelEndRow = row;
                        Invalidate();
                        SelectionChanged?.Invoke(this);
                    }
                    ContextRequested?.Invoke(this, PointToScreen(new Point(e.X, e.Y)), row);
                }
            }

            base.OnMouseDown(e);
        }

        /// <summary>The change-block range (consecutive Insert/Delete/Changed) containing
        /// <paramref name="row"/>, or (-1,-1) when the row isn't part of a change.
        /// Delegates to the tested <see cref="LineDiff.BlockRangeAt"/> — one algorithm, one owner.</summary>
        public (int start, int end) BlockRangeAt(int row) => LineDiff.BlockRangeAt(_rows, row);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SelStartRow >= 0)
            {
                int row = Math.Max(0, Math.Min(_rows.Count - 1, RowAtY(e.Y)));
                SelEndRow = row;
                Invalidate();
                SelectionChanged?.Invoke(this);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? -3 : 3;
            // Clamp so the last row stays visible — never scroll past the end.
            int maxFirst = Math.Max(0, _rows.Count - VisibleRowCount);
            FirstVisibleRow = Math.Max(0, Math.Min(maxFirst, FirstVisibleRow + delta));
            Scrolled?.Invoke(this);
            Invalidate();
            base.OnMouseWheel(e);
        }

        public (int startRow, int endRow) GetSelection()
        {
            int s = Math.Min(SelStartRow, SelEndRow);
            int en = Math.Max(SelStartRow, SelEndRow);
            return (s, en);
        }

        /// <summary>Clean ST text for the selected rows (gutter/markers stripped). Empty when nothing is selected.</summary>
        public string GetSelectedText()
        {
            var (s, en) = GetSelection();
            if (s < 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int row = s; row <= en && row < _rows.Count; row++)
            {
                var r = _rows[row];
                if (r.Op == DiffOp.Snip) continue;
                string? text = RowText(r);
                if (string.IsNullOrEmpty(text)) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// <summary>The committed (left) text for the selected rows + the majority section tag.</summary>
        public (string text, string? section) GetSelectedCommitted()
        {
            var (s, en) = GetSelection();
            if (s < 0 || _committedRowText.Count == 0) return ("", null);

            var sb = new System.Text.StringBuilder();
            var sectionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int row = s; row <= en && row < _committedRowText.Count; row++)
            {
                string? line = _committedRowText[row];
                if (line == null) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(line);

                string? tag = row < _committedRowSection.Count ? _committedRowSection[row] : null;
                if (!string.IsNullOrEmpty(tag))
                {
                    sectionCounts.TryGetValue(tag!, out var n);
                    sectionCounts[tag!] = n + 1;
                }
            }
            string? section = sectionCounts.Count > 0
                ? System.Linq.Enumerable.OrderByDescending(sectionCounts, kv => kv.Value).First().Key
                : null;
            return (sb.ToString(), section);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _font?.Dispose(); _boldFont?.Dispose(); _strikeFont?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}