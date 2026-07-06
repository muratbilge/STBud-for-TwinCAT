using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using STBud.Git.Diff;

namespace STFormatter.UI.DiffRendering
{
    /// <summary>
    /// Side-by-side (or unified) diff surface that hosts two <see cref="DiffPane"/>s
    /// and shared vertical + horizontal scrollbars. Owns row-list setup, scroll sync,
    /// and exposes selection/restore helpers for the parent form. Replaces the old
    /// RichTextBox-based rendering — the custom-drawn panes produce full-width row
    /// bands, a real gutter overlay (so Copy yields clean ST), word-level intra-line
    /// highlight, dark-mode colors, and HiDPI-aware layout.
    /// </summary>
    public sealed class DiffCanvas : UserControl
    {
        private readonly SplitContainer _split;
        private readonly DiffPane _left;
        private readonly DiffPane _right;
        private readonly Label _leftHeader;
        private readonly Label _rightHeader;
        private readonly VScrollBar _vscroll;
        private readonly HScrollBar _hscroll;
        private DiffColorScheme _scheme = DiffColorScheme.Detect();

        public DiffColorScheme Scheme
        {
            get => _scheme;
            set
            {
                _scheme = value;
                _left.Scheme = value;
                _right.Scheme = value;
                BackColor = value.PaneBackground;
                ApplyHeaderTheme();
                Invalidate();
            }
        }

        /// <summary>Set the column header captions over each pane (e.g. "HEAD (source)").</summary>
        public void SetPaneHeaders(string left, string right)
        {
            _leftHeader.Text = "  " + left;
            _rightHeader.Text = "  " + right;
        }

        private void ApplyHeaderTheme()
        {
            foreach (var h in new[] { _leftHeader, _rightHeader })
            {
                h.BackColor = _scheme.SectionHeaderBack;
                h.ForeColor = _scheme.SectionHeaderFore;
            }
        }

        public bool IsDark => _scheme == DiffColorScheme.Dark;

        public DiffCanvas()
        {
            BackColor = _scheme.PaneBackground;

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = _scheme.DividerColor,
                Panel1MinSize = 120,
                Panel2MinSize = 120,
            };
            _left = new DiffPane(DiffPane.SideKind.Left) { Dock = DockStyle.Fill, Scheme = _scheme };
            _right = new DiffPane(DiffPane.SideKind.Right) { Dock = DockStyle.Fill, Scheme = _scheme };
            // Accept arrows live on the working (right) side — "pull HEAD's line into here".
            _right.ShowAcceptArrows = true;

            var headerFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            _leftHeader = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Font = headerFont };
            _rightHeader = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Font = headerFont };

            // Fill pane first, then header docked Top above it (WinForms docks in reverse z-order).
            _split.Panel1.Controls.Add(_left);
            _split.Panel1.Controls.Add(_leftHeader);
            _split.Panel2.Controls.Add(_right);
            _split.Panel2.Controls.Add(_rightHeader);
            ApplyHeaderTheme();

            _vscroll = new VScrollBar { Dock = DockStyle.Right, Width = 18 };
            _hscroll = new HScrollBar { Dock = DockStyle.Bottom, Height = 18 };

            _left.Scrolled += OnPaneScrolled;
            _right.Scrolled += OnPaneScrolled;
            _left.SelectionChanged += OnPaneSelectionChanged;
            _right.SelectionChanged += OnPaneSelectionChanged;
            _left.ContextRequested += (p, pt, row) => ContextRequested?.Invoke(p, pt, row);
            _right.ContextRequested += (p, pt, row) => ContextRequested?.Invoke(p, pt, row);
            _left.AcceptBlockRequested += (p, row) => AcceptBlockRequested?.Invoke(p, row);
            _right.AcceptBlockRequested += (p, row) => AcceptBlockRequested?.Invoke(p, row);

            _vscroll.Scroll += OnVScroll;
            _hscroll.Scroll += OnHScroll;

            Controls.Add(_split);
            Controls.Add(_vscroll);
            Controls.Add(_hscroll);
        }

        public void InitSplitter()
        {
            try
            {
                if (_split.Width > _split.Panel1MinSize + _split.Panel2MinSize)
                    _split.SplitterDistance = _split.Width / 2;
            }
            catch { }
        }

        public void RebuildFonts(Font font)
        {
            _left.RebuildFonts(font);
            _right.RebuildFonts(font);
            UpdateScrollRanges();
        }

        public void SetRows(List<DiffRow> rows)
        {
            _left.SetRows(rows);
            _right.SetRows(rows);
            UpdateScrollRanges();
        }

        private void UpdateScrollRanges()
        {
            int rowCount = Math.Max(_left.RowCount, _right.RowCount);
            _vscroll.Maximum = Math.Max(0, rowCount - 1);
            _vscroll.LargeChange = Math.Max(1, _left.VisibleRowCount);
            _vscroll.SmallChange = 1;

            int maxExtent = Math.Max(_left.MaxHorizontalExtent, _right.MaxHorizontalExtent);
            int visibleW = Math.Max(0, _left.Width - _left.Margin.Horizontal);
            _hscroll.Maximum = Math.Max(0, maxExtent + 20);
            _hscroll.LargeChange = Math.Max(1, visibleW);
            _hscroll.SmallChange = 10;
        }

        private void OnVScroll(object? sender, ScrollEventArgs e)
        {
            _left.FirstVisibleRow = e.NewValue;
            _right.FirstVisibleRow = e.NewValue;
            _left.Invalidate();
            _right.Invalidate();
        }

        private void OnHScroll(object? sender, ScrollEventArgs e)
        {
            _left.HorizontalOffset = e.NewValue;
            _right.HorizontalOffset = e.NewValue;
            _left.Invalidate();
            _right.Invalidate();
        }

        private void OnPaneScrolled(DiffPane pane)
        {
            // Wheel scroll: keep both panes in vertical lock and reflect on the scrollbar.
            int f = pane.FirstVisibleRow;
            _left.FirstVisibleRow = f;
            _right.FirstVisibleRow = f;

            // WinForms ScrollBar enforces Value <= Maximum - LargeChange + 1; setting a
            // Value past that throws ArgumentOutOfRangeException (the user's crash). Clamp.
            int maxVal = Math.Max(0, _vscroll.Maximum - Math.Max(1, _vscroll.LargeChange) + 1);
            int clamped = Math.Min(f, maxVal);
            if (_vscroll.Value != clamped) _vscroll.Value = clamped;

            _left.Invalidate();
            _right.Invalidate();
        }

        private void OnPaneSelectionChanged(DiffPane pane)
        {
            // Selection is per-pane; no cross-pane sync needed. The parent form reads
            // the focused pane's selection when handling the Copy/Restore toolbar buttons.
        }

        /// <summary>Left pane (committed / "Original" side).</summary>
        public DiffPane LeftPane => _left;

        /// <summary>Right pane (working / "Formatted" side).</summary>
        public DiffPane RightPane => _right;

        /// <summary>Right-click on a diff row: (pane, screen point, row).</summary>
        public event Action<DiffPane, Point, int>? ContextRequested;

        /// <summary>Gutter accept-arrow click: (pane, block-start row).</summary>
        public event Action<DiffPane, int>? AcceptBlockRequested;

        /// <summary>The change-block range containing the row (rows are aligned across panes).</summary>
        public (int start, int end) BlockRangeAt(int row) => _left.BlockRangeAt(row);

        /// <summary>Show/hide the one-click accept ▶ arrows on the working pane.</summary>
        public void SetAcceptArrows(bool on)
        {
            if (_right.ShowAcceptArrows == on) return;
            _right.ShowAcceptArrows = on;
            _right.Invalidate();
        }

        /// <summary>
        /// The selected row range from whichever pane has a selection (the panes are
        /// row-aligned, so the indices are interchangeable). (-1,-1) when nothing is selected.
        /// </summary>
        public (int start, int end) GetSelectedRowRange()
        {
            var (ls, le) = _left.GetSelection();
            if (ls >= 0) return (ls, le);
            var (rs, re) = _right.GetSelection();
            if (rs >= 0) return (rs, re);
            return (-1, -1);
        }

        /// <summary>Repaint both panes (e.g. after marking rows as restored).</summary>
        public void Repaint()
        {
            _left.Invalidate();
            _right.Invalidate();
        }

        /// <summary>Scroll both panes so the given row is in view.</summary>
        public void ScrollToRow(int row)
        {
            if (row < 0) return;
            // Clamp the target row to the pane's valid first-visible range so we never
            // scroll past the last page (which would also overflow the scrollbar).
            int maxFirst = Math.Max(0, Math.Max(_left.RowCount, _right.RowCount) - Math.Max(1, _left.VisibleRowCount));
            int target = Math.Min(row, maxFirst);

            _left.FirstVisibleRow = target;
            _right.FirstVisibleRow = target;

            int maxVal = Math.Max(0, _vscroll.Maximum - Math.Max(1, _vscroll.LargeChange) + 1);
            int clamped = Math.Min(target, maxVal);
            if (_vscroll.Value != clamped) _vscroll.Value = clamped;

            _left.Invalidate();
            _right.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollRanges();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _left.Scrolled -= OnPaneScrolled;
                _right.Scrolled -= OnPaneScrolled;
            }
            base.Dispose(disposing);
        }
    }
}