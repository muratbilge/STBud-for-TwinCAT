using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    public class MainForm : Form
    {
        private readonly NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private readonly TabControl _tabControl;
        private readonly SettingsPanel _settingsPanel;
        private readonly InstancesPanel _instancesPanel;
        private readonly HistoryPanel _historyPanel;
        private readonly LogPanel _logPanel;
        private readonly ToolboxPanel _toolboxPanel;
        private readonly GitPanel _gitPanel;
        private readonly ConcurrentBag<FormatRecord> _formatHistory = new();
        private readonly System.Windows.Forms.Timer _maintainTimer;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _startupScanTimer;
        private readonly Action? _maintainAction;
        private readonly int[] _startupScanDelays = { 500, 1000, 2000, 5000, 10000 };
        private int _startupScanIndex;
        private bool _allowVisible;
        private bool _localizeSuspend;
        internal static readonly Icon AppIcon = LoadAppIcon();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public ConcurrentBag<FormatRecord> FormatHistory => _formatHistory;

        private static Icon LoadAppIcon()
        {
            var assembly = typeof(MainForm).Assembly;
            var resourceName = assembly.GetName().Name + ".Resources.icon.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                try { return new Icon(stream); }
                catch { }
            }
            try { return Icon.ExtractAssociatedIcon(assembly.Location); }
            catch { }
            return SystemIcons.Application;
        }

        public MainForm(
            Func<IReadOnlyDictionary<int, InstanceInfo>> getInstances,
            Action cleanup,
            Action? maintainAction = null,
            Func<string>? getStatus = null)
        {
            _maintainAction = maintainAction;

            Text = Strings.Get("App.Title");
            Size = new Size(1100, 750);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon;
            AutoScaleMode = AutoScaleMode.Font;
            KeyPreview = true;

            _settingsPanel = new SettingsPanel();
            _settingsPanel.SettingsApplied += OnSettingsApplied;
            _instancesPanel = new InstancesPanel(getInstances, cleanup, maintainAction, getStatus);
            _historyPanel = new HistoryPanel(_formatHistory);
            _logPanel = new LogPanel();
            _toolboxPanel = new ToolboxPanel();
            _gitPanel = new GitPanel();

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f),
                Padding = new Point(12, 6)
            };

            var settingsTab = new TabPage(Strings.Get("Tab.Settings")) { Padding = new Padding(4) };
            settingsTab.Controls.Add(_settingsPanel);

            var instancesTab = new TabPage(Strings.Get("Tab.Instances")) { Padding = new Padding(4) };
            instancesTab.Controls.Add(_instancesPanel);

            var historyTab = new TabPage(Strings.Get("Tab.History")) { Padding = new Padding(4) };
            historyTab.Controls.Add(_historyPanel);

            var logTab = new TabPage(Strings.Get("Tab.Log")) { Padding = new Padding(4) };
            logTab.Controls.Add(_logPanel);

            var toolboxTab = new TabPage(Strings.Get("Tab.Toolbox")) { Padding = new Padding(4) };
            toolboxTab.Controls.Add(_toolboxPanel);

            var gitTab = new TabPage(Strings.Get("Tab.Git")) { Padding = new Padding(4) };
            gitTab.Controls.Add(_gitPanel);

            _tabControl.TabPages.Add(settingsTab);
            _tabControl.TabPages.Add(instancesTab);
            _tabControl.TabPages.Add(historyTab);
            _tabControl.TabPages.Add(logTab);
            _tabControl.TabPages.Add(toolboxTab);
            _tabControl.TabPages.Add(gitTab);

            _tabControl.SelectedIndexChanged += OnTabChanged;

            Controls.Add(_tabControl);

            BuildTrayMenu();

            _trayIcon = new NotifyIcon
            {
                Icon = AppIcon,
                Text = Strings.Get("Tray.Text"),
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _trayIcon.DoubleClick += (s, e) => ShowWindow(0);

            _maintainTimer = new System.Windows.Forms.Timer { Interval = 500, Enabled = true };
            _maintainTimer.Tick += (s, e) => _maintainAction?.Invoke();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
            _refreshTimer.Tick += OnRefreshTick;

            _startupScanTimer = new System.Windows.Forms.Timer { Interval = _startupScanDelays[0], Enabled = true };
            _startupScanTimer.Tick += OnStartupScanTick;

            FormClosing += OnFormClosing;
            Load += (s, e) =>
            {
                _maintainAction?.Invoke();
            };
            ShowInTaskbar = false;
        }

        private void BuildTrayMenu()
        {
            var old = _trayMenu;
            _trayMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 9f) };
            _trayMenu.Items.Add(Strings.Get("Tray.Settings"), null, (s, e) => ShowWindow(0));
            _trayMenu.Items.Add(Strings.Get("Tray.Instances"), null, (s, e) => ShowWindow(1));
            _trayMenu.Items.Add(Strings.Get("Tray.History"), null, (s, e) => ShowWindow(2));
            _trayMenu.Items.Add(Strings.Get("Tray.Log"), null, (s, e) => ShowWindow(3));
            _trayMenu.Items.Add(Strings.Get("Tray.Toolbox"), null, (s, e) => ShowWindow(4));
            _trayMenu.Items.Add(Strings.Get("Tray.Git"), null, (s, e) => ShowWindow(5));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Strings.Get("Tray.Restart"), null, OnRestart);
            _trayMenu.Items.Add(Strings.Get("Tray.Exit"), null, OnExit);
            old?.Dispose();
        }

        public void Relocalize()
        {
            if (_localizeSuspend) return;
            _localizeSuspend = true;
            try
            {
                Text = Strings.Get("App.Title");
                if (_tabControl.TabPages.Count >= 6)
                {
                    _tabControl.TabPages[0].Text = Strings.Get("Tab.Settings");
                    _tabControl.TabPages[1].Text = Strings.Get("Tab.Instances");
                    _tabControl.TabPages[2].Text = Strings.Get("Tab.History");
                    _tabControl.TabPages[3].Text = Strings.Get("Tab.Log");
                    _tabControl.TabPages[4].Text = Strings.Get("Tab.Toolbox");
                    _tabControl.TabPages[5].Text = Strings.Get("Tab.Git");
                }
                _trayIcon.Text = Strings.Get("Tray.Text");
                BuildTrayMenu();
                _trayIcon.ContextMenuStrip = _trayMenu;
                _settingsPanel.RebuildUi();
                _instancesPanel.RebuildUi();
                _historyPanel.RebuildUi();
                _logPanel.RebuildUi();
                _toolboxPanel.RebuildUi();
                _gitPanel.RebuildUi();
            }
            finally
            {
                _localizeSuspend = false;
            }
        }

        public void ShowWindow(int tabIndex)
        {
            if (_tabControl.TabPages.Count > tabIndex)
                _tabControl.SelectedIndex = tabIndex;
            _allowVisible = true;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            SetForegroundWindow(Handle);
            RefreshActiveTab();
        }

        public void AddFormatRecord(FormatRecord record) => _formatHistory.Add(record);

        /// <summary>Open the Git tab focused on a specific file (from the Host context menu).</summary>
        public void ShowGitForFile(string filePath, int subTab = 0, string? repoRootHint = null)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)(() => ShowGitForFile(filePath, subTab, repoRootHint)));
                    return;
                }
                _gitPanel.LoadForFile(filePath, subTab, repoRootHint);
                ShowWindow(5);
            }
            catch (Exception ex)
            {
                HostLog.Append("MainForm", $"ShowGitForFile failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a non-blocking tray balloon. Used for transient format feedback
        /// (e.g. "could not format") instead of a modal dialog, which — when
        /// owned by the wrong editor window across multiple instances — could
        /// render off-screen and block the IDE.
        /// </summary>
        public void ShowNotification(string title, string text)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)(() => ShowNotification(title, text)));
                    return;
                }
                _trayIcon.BalloonTipTitle = title;
                _trayIcon.BalloonTipText = text;
                _trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _trayIcon.ShowBalloonTip(5000);
            }
            catch (Exception ex)
            {
                HostLog.Append("MainForm", $"ShowNotification failed: {ex.Message}");
            }
        }

        private void OnTabChanged(object? sender, EventArgs e) => RefreshActiveTab();

        private void RefreshActiveTab()
        {
            switch (_tabControl.SelectedIndex)
            {
                case 1: _instancesPanel.RefreshInstances(); break;
                case 2: _historyPanel.CheckRefresh(); break;
                case 3: _logPanel.RefreshLog(); break;
                case 5: _gitPanel.EnsureLoaded(); break;
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

        private void OnStartupScanTick(object? sender, EventArgs e)
        {
            try
            {
                _maintainAction?.Invoke();
                _instancesPanel.RefreshInstances();

                _startupScanIndex++;
                if (_startupScanIndex >= _startupScanDelays.Length)
                {
                    _startupScanTimer.Stop();
                    return;
                }

                _startupScanTimer.Interval = _startupScanDelays[_startupScanIndex];
            }
            catch { }
        }

        private void OnSettingsApplied(FormattingConfiguration config)
        {
            try
            {
                Relocalize();
                _trayIcon.BalloonTipTitle = Strings.Get("Tray.Saved.Title");
                _trayIcon.BalloonTipText = Strings.Get("Tray.Saved.Text");
                _trayIcon.ShowBalloonTip(3000);
            }
            catch (Exception ex)
            {
                HostLog.Append("MainForm", $"OnSettingsApplied failed: {ex.Message}");
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _maintainTimer.Stop();
            _refreshTimer.Stop();
            _startupScanTimer.Stop();
            Application.Exit();
        }

        private void OnRestart(object? sender, EventArgs e)
        {
            var exePath = Application.ExecutablePath;
            _trayIcon.Visible = false;
            _maintainTimer.Stop();
            _refreshTimer.Stop();
            _startupScanTimer.Stop();
            StartReplacementHost(exePath);
            Application.Exit();
        }

        private static void StartReplacementHost(string exePath)
        {
            if (IsElevated())
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + exePath + "\"");
                return;
            }

            System.Diagnostics.Process.Start(exePath);
        }

        private static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _allowVisible = false;
                ShowInTaskbar = false;
                Hide();
            }
            else
            {
                _trayIcon.Visible = false;
                _maintainTimer.Stop();
                _refreshTimer.Stop();
                _startupScanTimer.Stop();
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
                _startupScanTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowVisible && value)
            {
                base.SetVisibleCore(false);
                return;
            }

            base.SetVisibleCore(value);
        }
    }
}
