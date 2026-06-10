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
        private FlowLayoutPanel _toolbar;
        private readonly Func<IReadOnlyDictionary<int, InstanceInfo>> _getInstances;
        private readonly Action _cleanup;
        private readonly Action? _scan;
        private readonly Func<string>? _getStatus;

        public InstancesPanel(
            Func<IReadOnlyDictionary<int, InstanceInfo>> getInstances,
            Action cleanup,
            Action? scan = null,
            Func<string>? getStatus = null)
        {
            _getInstances = getInstances;
            _cleanup = cleanup;
            _scan = scan;
            _getStatus = getStatus;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
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
                item.SubItems.Add(kvp.Value.Title);
                item.SubItems.Add(kvp.Value.Connected
                    ? Strings.Get("Instances.Status.Connected")
                    : Strings.Get("Instances.Status.Disconnected"));
                item.SubItems.Add(kvp.Value.InjectedMenus);
                item.SubItems.Add(kvp.Value.LastFormatTime?.ToString("HH:mm:ss") ?? Strings.Get("Common.None"));
                item.SubItems.Add(kvp.Value.FormatCount.ToString());
                if (!kvp.Value.Connected)
                    item.ForeColor = Color.FromArgb(180, 0, 0);
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();
            int connectedCount = 0;
            foreach (var kvp in instances)
                if (kvp.Value.Connected) connectedCount++;
            _statusLabel.Text = instances.Count > 0
                ? $"{connectedCount}/{instances.Count} {Strings.Get("Instances.Status.Connected").ToLowerInvariant()}"
                : _getStatus?.Invoke() ?? Strings.Get("Instances.None");
        }

        public void RebuildUi()
        {
            BuildUI();
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

            _refreshBtn = new Button { Text = Strings.Get("Instances.Refresh"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _refreshBtn.Click += (s, e) => RefreshInstances();

            _scanBtn = new Button { Text = Strings.Get("Instances.Scan"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _scanBtn.Click += (s, e) =>
            {
                _scan?.Invoke();
                RefreshInstances();
            };

            _cleanupBtn = new Button { Text = Strings.Get("Instances.Cleanup"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _cleanupBtn.Click += (s, e) =>
            {
                _cleanup();
                RefreshInstances();
            };

            _toolbar.Controls.Add(_refreshBtn);
            _toolbar.Controls.Add(_scanBtn);
            _toolbar.Controls.Add(_cleanupBtn);

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.None,
            };
            _listView.Columns.Add(Strings.Get("Instances.Columns.PID"), 60);
            _listView.Columns.Add(Strings.Get("Instances.Columns.Title"), 180);
            _listView.Columns.Add(Strings.Get("Instances.Columns.Status"), 100);
            _listView.Columns.Add(Strings.Get("Instances.Columns.Menus"), 240);
            _listView.Columns.Add(Strings.Get("Instances.Columns.LastFormat"), 100);
            _listView.Columns.Add(Strings.Get("Instances.Columns.Count"), -2);

            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = Strings.Get("Instances.None"),
                Padding = new Padding(8, 4, 0, 0),
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlDarkDark,
            };

            Controls.Add(_listView);
            Controls.Add(_toolbar);
            Controls.Add(_statusLabel);
        }
    }

    public class InstanceInfo
    {
        public bool Connected { get; set; }
        public string Title { get; set; } = "";
        public string InjectedMenus { get; set; } = "";
        public DateTime? LastFormatTime { get; set; }
        public int FormatCount { get; set; }
    }
}