using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using STFormatter.Core.Configuration;
using STFormatter.Core.Toolbox;

namespace STFormatter.UI
{
    /// <summary>
    /// Non-formatting utilities: the TwinCAT machine pinger and a copyable
    /// diagnostics report for support/issue tickets.
    /// </summary>
    public class ToolboxPanel : UserControl
    {
        private ComboBox _targetCombo = null!;
        private NumericUpDown _timeoutInput = null!;
        private Button _pingBtn = null!;
        private Button _diagBtn = null!;
        private Button _copyBtn = null!;
        private Button _clearBtn = null!;
        private RichTextBox _output = null!;
        private bool _busy;

        public ToolboxPanel()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUI();
        }

        public void RebuildUi() => BuildUI();

        private void BuildUI()
        {
            Controls.Clear();

            // -- Pinger row --
            var pingerGroup = new GroupBox
            {
                Text = Strings.Get("Toolbox.Group.Pinger"),
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
            };

            var pingerRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(4),
            };

            pingerRow.Controls.Add(new Label
            {
                Text = Strings.Get("Toolbox.Target"),
                AutoSize = true,
                Margin = new Padding(0, 7, 4, 0),
            });

            _targetCombo = new ComboBox
            {
                Width = 260,
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = new Padding(0, 3, 12, 3),
            };
            foreach (var t in SettingsManager.App.RecentPingTargets)
                _targetCombo.Items.Add(t);
            if (_targetCombo.Items.Count > 0)
                _targetCombo.SelectedIndex = 0;
            _targetCombo.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunPing(); }
            };
            pingerRow.Controls.Add(_targetCombo);

            pingerRow.Controls.Add(new Label
            {
                Text = Strings.Get("Toolbox.Timeout"),
                AutoSize = true,
                Margin = new Padding(0, 7, 4, 0),
            });

            _timeoutInput = new NumericUpDown
            {
                Minimum = 200,
                Maximum = 30000,
                Increment = 500,
                Value = 2000,
                Width = 80,
                Margin = new Padding(0, 3, 12, 3),
            };
            pingerRow.Controls.Add(_timeoutInput);

            _pingBtn = new Button
            {
                Text = Strings.Get("Toolbox.Check"),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            _pingBtn.Click += (s, e) => RunPing();
            pingerRow.Controls.Add(_pingBtn);

            pingerGroup.Controls.Add(pingerRow);

            // -- Diagnostics / output toolbar --
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(8, 5, 8, 5),
            };

            _diagBtn = new Button
            {
                Text = Strings.Get("Toolbox.RunDiagnostics"),
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0),
            };
            _diagBtn.Click += (s, e) => RunDiagnostics();
            toolbar.Controls.Add(_diagBtn);

            _copyBtn = new Button
            {
                Text = Strings.Get("Toolbox.CopyOutput"),
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0),
            };
            _copyBtn.Click += (s, e) =>
            {
                if (_output.TextLength > 0)
                    Clipboard.SetText(_output.Text);
            };
            toolbar.Controls.Add(_copyBtn);

            _clearBtn = new Button
            {
                Text = Strings.Get("Toolbox.Clear"),
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0),
            };
            _clearBtn.Click += (s, e) => _output.Clear();
            toolbar.Controls.Add(_clearBtn);

            _output = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                DetectUrls = false,
            };

            Controls.Add(_output);
            Controls.Add(toolbar);
            Controls.Add(pingerGroup);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _pingBtn.Enabled = !busy;
            _diagBtn.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void Append(string text)
        {
            _output.AppendText(text);
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }

        private void RunPing()
        {
            if (_busy) return;
            var target = _targetCombo.Text.Trim();
            if (string.IsNullOrEmpty(target)) return;

            int timeoutMs = (int)_timeoutInput.Value;
            RememberTarget(target);
            SetBusy(true);
            Append($"=== {DateTime.Now:HH:mm:ss} ping {target} (timeout {timeoutMs} ms) ==={Environment.NewLine}");

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = TwinCatPinger.RunDiagnostics(target, timeoutMs).BuildSummary();
                }
                catch (Exception ex)
                {
                    summary = $"Error: {ex.GetBaseException().Message}{Environment.NewLine}";
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Append(summary + Environment.NewLine);
                        SetBusy(false);
                    }));
                }
                catch { }
            });
        }

        private void RememberTarget(string target)
        {
            try
            {
                var app = SettingsManager.App;
                app.RecentPingTargets.RemoveAll(t =>
                    string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
                app.RecentPingTargets.Insert(0, target);
                if (app.RecentPingTargets.Count > 10)
                    app.RecentPingTargets.RemoveRange(10, app.RecentPingTargets.Count - 10);
                SettingsManager.SaveAppSettings(app);

                if (!_targetCombo.Items.Contains(target))
                    _targetCombo.Items.Insert(0, target);
            }
            catch (Exception ex)
            {
                HostLog.Append("ToolboxPanel", $"RememberTarget failed: {ex.Message}");
            }
        }

        private void RunDiagnostics()
        {
            if (_busy) return;
            SetBusy(true);
            Append($"=== {DateTime.Now:HH:mm:ss} diagnostics ==={Environment.NewLine}");

            Task.Run(() =>
            {
                string report;
                try
                {
                    report = BuildDiagnosticsReport();
                }
                catch (Exception ex)
                {
                    report = $"Error: {ex.GetBaseException().Message}{Environment.NewLine}";
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Append(report + Environment.NewLine);
                        SetBusy(false);
                    }));
                }
                catch { }
            });
        }

        /// <summary>
        /// Builds the copyable support report: versions, environment, deployed
        /// Host files, running TcXaeShell instances, and a local ADS port check.
        /// </summary>
        public static string BuildDiagnosticsReport()
        {
            var sb = new StringBuilder();
            var asm = typeof(ToolboxPanel).Assembly;
            sb.AppendLine($"STBud version : {asm.GetName().Version}");
            sb.AppendLine($"OS            : {Environment.OSVersion}");
            sb.AppendLine($"Process       : {(Environment.Is64BitProcess ? "x64" : "x86")} on {(Environment.Is64BitOperatingSystem ? "x64" : "x86")} OS, .NET {Environment.Version}");
            sb.AppendLine($"Settings file : {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STBud", "settings.json")}");

            var logPath = HostLog.Path;
            sb.AppendLine(File.Exists(logPath)
                ? $"Host log      : {logPath} ({new FileInfo(logPath).Length / 1024} KB)"
                : $"Host log      : {logPath} (missing)");

            sb.AppendLine();
            sb.AppendLine("Deployed Host files (C:\\Program Files (x86)\\STBud):");
            var installDir = @"C:\Program Files (x86)\STBud";
            if (Directory.Exists(installDir))
            {
                foreach (var file in Directory.GetFiles(installDir))
                {
                    string version = "";
                    try { version = FileVersionInfo.GetVersionInfo(file).FileVersion ?? ""; }
                    catch { }
                    sb.AppendLine($"  {Path.GetFileName(file),-40} {version}");
                }
            }
            else
            {
                sb.AppendLine("  (not deployed)");
            }

            sb.AppendLine();
            sb.AppendLine("Running TcXaeShell / Visual Studio instances:");
            bool any = false;
            foreach (var name in new[] { "TcXaeShell", "TcXaeShell64", "devenv" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        sb.AppendLine($"  {name} PID {p.Id}");
                        any = true;
                    }
                }
            }
            if (!any) sb.AppendLine("  (none)");

            sb.AppendLine();
            sb.AppendLine("Local TwinCAT/ADS check (127.0.0.1):");
            sb.Append(TwinCatPinger.RunDiagnostics("127.0.0.1", 1000).BuildSummary());

            return sb.ToString();
        }
    }
}
