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
        private readonly ConcurrentBag<FormatRecord> _history;
        private int _lastKnownCount;

        public int HistoryCount
        {
            get
            {
                int count = 0;
                foreach (var _ in _history) count++;
                return count;
            }
        }

        public HistoryPanel(ConcurrentBag<FormatRecord> history)
        {
            _history = history;
            Dock = DockStyle.Fill;
            BuildUI();
        }

        public void CheckRefresh()
        {
            int current = HistoryCount;
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

            foreach (var r in records)
            {
                var item = new ListViewItem(r.Timestamp.ToString("HH:mm:ss.fff"));
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

        private void BuildUI()
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6)
            };

            _clearBtn = new Button
            {
                Text = "Clear History",
                Left = 8, Top = 6, Width = 130, Height = 30,
                Font = new Font("Segoe UI", 9f)
            };
            _clearBtn.Click += (s, e) =>
            {
                while (_history.TryTake(out _)) { }
                _lastKnownCount = 0;
                _listView.Items.Clear();
            };

            _exportBtn = new Button
            {
                Text = "Export Log...",
                Left = 150, Top = 6, Width = 120, Height = 30,
                Font = new Font("Segoe UI", 9f)
            };
            _exportBtn.Click += OnExport;

            toolbar.Controls.Add(_clearBtn);
            toolbar.Controls.Add(_exportBtn);

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None
            };
            _listView.Columns.Add("Time", 110);
            _listView.Columns.Add("File", 180);
            _listView.Columns.Add("Section", 110);
            _listView.Columns.Add("Lines", 120);
            _listView.Columns.Add("Method", 130);
            _listView.Columns.Add("Result", 60);
            _listView.DoubleClick += OnDoubleClick;

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = "Double-click a row to open diff viewer",
                Padding = new Padding(8, 4, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = SystemColors.ControlDarkDark,
                BackColor = SystemColors.Control
            };

            Controls.Add(_listView);
            Controls.Add(toolbar);
            Controls.Add(hint);
        }

        private void OnDoubleClick(object? sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var record = _listView.SelectedItems[0].Tag as FormatRecord;
            if (record == null) return;

            var diff = new DiffViewerForm(
                $"Diff: {record.FileName} - {record.Section} - {record.Timestamp:HH:mm:ss}",
                record.OriginalText,
                record.FormattedText);
            diff.ShowDialog(FindForm());
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
                            writer.WriteLine($"{r.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {r.FilePath} | {r.Section} | {r.Method} | {(r.Success ? "OK" : "FAIL")}");
                    }
                }
            }
        }
    }
}