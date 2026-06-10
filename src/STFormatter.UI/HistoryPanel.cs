using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class HistoryPanel : UserControl
    {
        private ListView _listView;
        private Button _clearBtn;
        private Button _exportBtn;
        private FlowLayoutPanel _toolbar;
        private Label _hint;
        private readonly ConcurrentBag<FormatRecord> _history;
        private int _lastKnownCount;
        private int _sortColumn = -1;
        private SortOrder _sortOrder = SortOrder.Descending;

        public HistoryPanel(ConcurrentBag<FormatRecord> history)
        {
            _history = history;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUI();
        }

        public void CheckRefresh()
        {
            int current = _history.Count;
            if (current != _lastKnownCount)
            {
                _lastKnownCount = current;
                RefreshHistory();
            }
        }

        public void RefreshHistory()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();

            var records = new List<FormatRecord>();
            foreach (var r in _history) records.Add(r);
            records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

            if (records.Count == 0)
            {
                var empty = new ListViewItem(Strings.Get("History.Empty"));
                empty.ForeColor = SystemColors.ControlDarkDark;
                _listView.Items.Add(empty);
                _listView.EndUpdate();
                return;
            }

            foreach (var r in records)
            {
                var item = new ListViewItem(r.Timestamp.ToString("HH:mm:ss.fff"));
                item.SubItems.Add(r.Pid.ToString());
                item.SubItems.Add(r.Title);
                item.SubItems.Add(r.FileName);
                item.SubItems.Add(r.Section);
                item.SubItems.Add(r.OriginalLineCount + " > " + r.FormattedLineCount);
                item.SubItems.Add(r.Method);
                item.SubItems.Add(r.Success ? "OK" : "FAIL");
                item.Tag = r;
                if (!r.Success)
                    item.ForeColor = Color.FromArgb(180, 0, 0);
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();
        }

        public void RebuildUi()
        {
            BuildUI();
            CheckRefresh();
        }

        private void BuildUI()
        {
            Controls.Clear();

            _toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(8, 6, 8, 6),
            };

            _clearBtn = new Button { Text = Strings.Get("History.Clear"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _clearBtn.Click += (s, e) =>
            {
                while (_history.TryTake(out _)) { }
                _lastKnownCount = 0;
                _listView.Items.Clear();
            };

            _exportBtn = new Button { Text = Strings.Get("History.Export"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _exportBtn.Click += OnExport;

            _toolbar.Controls.Add(_clearBtn);
            _toolbar.Controls.Add(_exportBtn);

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.None,
            };
            _listView.Columns.Add(Strings.Get("History.Columns.Time"), 90);
            _listView.Columns.Add(Strings.Get("History.Columns.PID"), 60);
            _listView.Columns.Add(Strings.Get("History.Columns.Title"), 160);
            _listView.Columns.Add(Strings.Get("History.Columns.File"), 160);
            _listView.Columns.Add(Strings.Get("History.Columns.Section"), 90);
            _listView.Columns.Add(Strings.Get("History.Columns.Lines"), 100);
            _listView.Columns.Add(Strings.Get("History.Columns.Method"), 110);
            _listView.Columns.Add(Strings.Get("History.Columns.Result"), -2);
            _listView.DoubleClick += OnDoubleClick;
            _listView.ColumnClick += OnColumnClick;

            _hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = Strings.Get("History.Hint"),
                Padding = new Padding(8, 4, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = SystemColors.ControlDarkDark,
                BackColor = SystemColors.Control,
            };

            Controls.Add(_listView);
            Controls.Add(_toolbar);
            Controls.Add(_hint);
        }

        private void OnDoubleClick(object? sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var record = _listView.SelectedItems[0].Tag as FormatRecord;
            if (record == null) return;
            if (string.IsNullOrEmpty(record.OriginalText) && string.IsNullOrEmpty(record.FormattedText))
            {
                MessageBox.Show(FindForm(),
                    Strings.Get("Common.None"),
                    Strings.Get("App.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var diff = new DiffViewerForm(
                $"Diff: {record.FileName} - {record.Section} - {record.Timestamp:HH:mm:ss}",
                record.OriginalText,
                record.FormattedText);
            diff.ShowDialog(FindForm());
        }

        private void OnColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn)
            {
                _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortOrder = SortOrder.Ascending;
            }

            _listView.ListViewItemSorter = new ListViewItemComparer(e.Column, _sortOrder);
            _listView.Sort();
        }

        private void OnExport(object? sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"STFormatter_History_{DateTime.Now:yyyyMMdd}.txt"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    using (var writer = new System.IO.StreamWriter(dlg.FileName))
                    {
                        var records = new List<FormatRecord>();
                        foreach (var r in _history) records.Add(r);
                        records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                        foreach (var r in records)
                            writer.WriteLine($"{r.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | PID {r.Pid} | {r.Title} | {r.FilePath} | {r.Section} | {r.Method} | {(r.Success ? "OK" : "FAIL")}");
                    }
                }
            }
        }

        private sealed class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _column;
            private readonly SortOrder _order;

            public ListViewItemComparer(int column, SortOrder order)
            {
                _column = column;
                _order = order;
            }

            public int Compare(object? x, object? y)
            {
                if (x is not ListViewItem a || y is not ListViewItem b) return 0;
                string sa = a.SubItems.Count > _column ? a.SubItems[_column].Text : "";
                string sb = b.SubItems.Count > _column ? b.SubItems[_column].Text : "";
                int result;
                if (_column == 0)
                {
                    if (DateTime.TryParseExact(sa, "HH:mm:ss.fff", null, System.Globalization.DateTimeStyles.None, out var da) &&
                        DateTime.TryParseExact(sb, "HH:mm:ss.fff", null, System.Globalization.DateTimeStyles.None, out var db))
                    {
                        result = da.TimeOfDay.CompareTo(db.TimeOfDay);
                    }
                    else
                    {
                        result = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                    }
                }
                else if (_column == 1)
                {
                    if (int.TryParse(sa, out var na) && int.TryParse(sb, out var nb))
                        result = na.CompareTo(nb);
                    else
                        result = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                }
                else if (_column == 5)
                {
                    // Lines column is "<original> > <formatted>"; sort by original count
                    result = LeadingNumber(sa).CompareTo(LeadingNumber(sb));
                }
                else
                {
                    result = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                }
                return _order == SortOrder.Descending ? -result : result;
            }

            private static int LeadingNumber(string s)
            {
                int end = 0;
                while (end < s.Length && char.IsDigit(s[end])) end++;
                return end > 0 && int.TryParse(s.Substring(0, end), out var n) ? n : -1;
            }
        }
    }
}