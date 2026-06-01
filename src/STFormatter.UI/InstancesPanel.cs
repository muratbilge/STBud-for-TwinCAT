using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class InstancesPanel : UserControl
    {
        private ListView _listView;
        private Button _refreshBtn;
        private Button _scanBtn;
        private Button _cleanupBtn;
        private Label _statusLabel;
        private readonly Func<IReadOnlyDictionary<int, InstanceInfo>> _getInstances;
        private readonly Action _cleanup;
        private readonly Action? _scan;

        public InstancesPanel(
            Func<IReadOnlyDictionary<int, InstanceInfo>> getInstances,
            Action cleanup,
            Action? scan = null)
        {
            _getInstances = getInstances;
            _cleanup = cleanup;
            _scan = scan;
            Dock = DockStyle.Fill;
            BuildUI();
        }

        public void RefreshInstances()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();
            var instances = _getInstances();
            foreach (var kvp in instances)
            {
                var item = new ListViewItem(kvp.Key.ToString());
                item.SubItems.Add(kvp.Value.Connected ? "Connected" : "Disconnected");
                item.SubItems.Add(kvp.Value.InjectedMenus);
                item.SubItems.Add(kvp.Value.LastFormatTime?.ToString("HH:mm:ss") ?? "-");
                item.SubItems.Add(kvp.Value.FormatCount.ToString());
                if (!kvp.Value.Connected)
                    item.ForeColor = Color.FromArgb(180, 0, 0);
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();
            _statusLabel.Text = $"{instances.Count} instance(s) connected";
        }

        private void BuildUI()
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6)
            };

            _refreshBtn = new Button
            {
                Text = "Refresh",
                Left = 8, Top = 6, Width = 85, Height = 30,
                Font = new Font("Segoe UI", 9f)
            };
            _refreshBtn.Click += (s, e) => RefreshInstances();

            _scanBtn = new Button
            {
                Text = "Scan",
                Left = 100, Top = 6, Width = 85, Height = 30,
                Font = new Font("Segoe UI", 9f)
            };
            _scanBtn.Click += (s, e) =>
            {
                _scan?.Invoke();
                RefreshInstances();
            };

            _cleanupBtn = new Button
            {
                Text = "Cleanup Stale",
                Left = 192, Top = 6, Width = 120, Height = 30,
                Font = new Font("Segoe UI", 9f)
            };
            _cleanupBtn.Click += (s, e) =>
            {
                _cleanup();
                RefreshInstances();
            };

            toolbar.Controls.Add(_refreshBtn);
            toolbar.Controls.Add(_scanBtn);
            toolbar.Controls.Add(_cleanupBtn);

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None
            };
            _listView.Columns.Add("PID", 80);
            _listView.Columns.Add("Status", 110);
            _listView.Columns.Add("Injected Menus", 280);
            _listView.Columns.Add("Last Format", 120);
            _listView.Columns.Add("Format Count", 100);

            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = "No instances",
                Padding = new Padding(8, 4, 0, 0),
                Font = new Font("Segoe UI", 9f),
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlDarkDark
            };

            Controls.Add(_listView);
            Controls.Add(toolbar);
            Controls.Add(_statusLabel);
        }
    }

    public class InstanceInfo
    {
        public bool Connected { get; set; }
        public string InjectedMenus { get; set; } = "";
        public DateTime? LastFormatTime { get; set; }
        public int FormatCount { get; set; }
    }
}