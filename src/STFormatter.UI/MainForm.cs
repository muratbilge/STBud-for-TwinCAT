using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    public class MainForm : Form
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly TabControl _tabControl;
        private readonly SettingsPanel _settingsPanel;
        private readonly InstancesPanel _instancesPanel;
        private readonly HistoryPanel _historyPanel;
        private readonly LogPanel _logPanel;
        private readonly ConcurrentBag<FormatRecord> _formatHistory = new();
        private readonly System.Windows.Forms.Timer _maintainTimer;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly Action? _maintainAction;

        public ConcurrentBag<FormatRecord> FormatHistory => _formatHistory;

        public MainForm(
            Func<IReadOnlyDictionary<int, InstanceInfo>> getInstances,
            Action cleanup,
            Action? maintainAction = null)
        {
            _maintainAction = maintainAction;

            Text = "ST Formatter";
            Size = new Size(1100, 750);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            _settingsPanel = new SettingsPanel();
            _settingsPanel.SettingsApplied += OnSettingsApplied;
            _instancesPanel = new InstancesPanel(getInstances, cleanup);
            _historyPanel = new HistoryPanel(_formatHistory);
            _logPanel = new LogPanel();

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f),
                Padding = new Point(12, 6)
            };

            var settingsTab = new TabPage("Settings") { Padding = new Padding(4) };
            settingsTab.Controls.Add(_settingsPanel);

            var instancesTab = new TabPage("Instances") { Padding = new Padding(4) };
            instancesTab.Controls.Add(_instancesPanel);

            var historyTab = new TabPage("History") { Padding = new Padding(4) };
            historyTab.Controls.Add(_historyPanel);

            var logTab = new TabPage("Log") { Padding = new Padding(4) };
            logTab.Controls.Add(_logPanel);

            _tabControl.TabPages.Add(settingsTab);
            _tabControl.TabPages.Add(instancesTab);
            _tabControl.TabPages.Add(historyTab);
            _tabControl.TabPages.Add(logTab);

            _tabControl.SelectedIndexChanged += OnTabChanged;

            Controls.Add(_tabControl);

            _trayMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 9f) };
            _trayMenu.Items.Add("Settings", null, (s, e) => ShowWindow(0));
            _trayMenu.Items.Add("Instances", null, (s, e) => ShowWindow(1));
            _trayMenu.Items.Add("History", null, (s, e) => ShowWindow(2));
            _trayMenu.Items.Add("Log", null, (s, e) => ShowWindow(3));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, OnExit);

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "ST Formatter",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _trayIcon.DoubleClick += (s, e) => ShowWindow(0);

            _maintainTimer = new System.Windows.Forms.Timer { Interval = 500, Enabled = true };
            _maintainTimer.Tick += (s, e) => _maintainAction?.Invoke();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
            _refreshTimer.Tick += OnRefreshTick;

            FormClosing += OnFormClosing;
            Load += (s, e) => { Hide(); };
        }

        public void ShowWindow(int tabIndex)
        {
            if (_tabControl.TabPages.Count > tabIndex)
                _tabControl.SelectedIndex = tabIndex;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            RefreshActiveTab();
        }

        public void AddFormatRecord(FormatRecord record) => _formatHistory.Add(record);

        private void OnTabChanged(object? sender, EventArgs e) => RefreshActiveTab();

        private void RefreshActiveTab()
        {
            switch (_tabControl.SelectedIndex)
            {
                case 1: _instancesPanel.RefreshInstances(); break;
                case 2: _historyPanel.CheckRefresh(); break;
                case 3: _logPanel.RefreshLog(); break;
            }
        }

        private void OnRefreshTick(object? sender, EventArgs e)
        {
            try
            {
                switch (_tabControl.SelectedIndex)
                {
                    case 1: _instancesPanel.RefreshInstances(); break;
                    case 2: _historyPanel.CheckRefresh(); break;
                    case 3: _logPanel.RefreshLog(); break;
                }
            }
            catch { }
        }

        private void OnSettingsApplied(FormattingConfiguration config)
        {
            _trayIcon.BalloonTipTitle = "ST Formatter";
            _trayIcon.BalloonTipText = "Settings applied successfully.";
            _trayIcon.ShowBalloonTip(3000);
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _maintainTimer.Stop();
            _refreshTimer.Stop();
            Application.Exit();
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _trayIcon.Visible = false;
                _maintainTimer.Stop();
                _refreshTimer.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon?.Dispose();
                _trayMenu?.Dispose();
                _maintainTimer?.Dispose();
                _refreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}