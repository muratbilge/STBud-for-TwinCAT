using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class LogPanel : UserControl
    {
        private RichTextBox _logBox;
        private Button _clearBtn;
        private Button _copyBtn;
        private Button _openBtn;
        private CheckBox _autoScrollCheck;
        private FlowLayoutPanel _toolbar;
        private long _lastReadPosition;
        private bool _firstRead = true;
        private readonly string _logPath;
        private readonly System.Windows.Forms.Timer _tailTimer;

        public LogPanel()
        {
            _logPath = STFormatter.Core.Configuration.HostLog.Path;
            _lastReadPosition = 0;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUI();

            _tailTimer = new System.Windows.Forms.Timer { Interval = 500, Enabled = true };
            _tailTimer.Tick += (s, e) => TailLog();
        }

        public void RefreshLog()
        {
            TailLog();
        }

        public void RebuildUi()
        {
            BuildUI();
        }

        private void TailLog()
        {
            try
            {
                if (!File.Exists(_logPath)) return;

                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (_firstRead)
                    {
                        _lastReadPosition = fs.Length;
                        _firstRead = false;
                        return;
                    }

                    if (fs.Length < _lastReadPosition)
                        _lastReadPosition = 0;

                    if (fs.Length == _lastReadPosition) return;

                    fs.Seek(_lastReadPosition, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs))
                    {
                        string newText = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(newText))
                        {
                            _logBox.AppendText(newText);
                            _lastReadPosition = fs.Position;
                        }
                    }
                }

                if (_autoScrollCheck.Checked && _logBox.TextLength > 0)
                {
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.ScrollToCaret();
                }
            }
            catch { }
        }

        private void BuildUI()
        {
            Controls.Clear();

            _toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(8, 5, 8, 5),
            };

            _clearBtn = new Button { Text = Strings.Get("Log.Clear"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _clearBtn.Click += (s, e) => { _logBox.Clear(); };

            _copyBtn = new Button { Text = Strings.Get("Log.CopyAll"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _copyBtn.Click += (s, e) =>
            {
                if (_logBox.TextLength > 0)
                    Clipboard.SetText(_logBox.Text);
            };

            _openBtn = new Button { Text = Strings.Get("Log.Open"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _openBtn.Click += (s, e) =>
            {
                try
                {
                    if (File.Exists(_logPath))
                        System.Diagnostics.Process.Start("notepad.exe", _logPath);
                }
                catch { }
            };

            _autoScrollCheck = new CheckBox
            {
                Text = Strings.Get("Log.AutoScroll"),
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };

            _toolbar.Controls.Add(_clearBtn);
            _toolbar.Controls.Add(_copyBtn);
            _toolbar.Controls.Add(_openBtn);
            _toolbar.Controls.Add(_autoScrollCheck);

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                DetectUrls = false,
                MaxLength = 0,
            };

            Controls.Add(_logBox);
            Controls.Add(_toolbar);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tailTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}