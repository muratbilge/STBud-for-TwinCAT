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
        private long _lastReadPosition;
        private readonly string _logPath;
        private readonly System.Windows.Forms.Timer _tailTimer;

        public LogPanel()
        {
            _logPath = Path.Combine(Path.GetTempPath(), "STFormatter_Host.log");
            _lastReadPosition = 0;
            Dock = DockStyle.Fill;
            BuildUI();

            _tailTimer = new System.Windows.Forms.Timer { Interval = 500, Enabled = true };
            _tailTimer.Tick += (s, e) => TailLog();
        }

        public void RefreshLog()
        {
            TailLog();
        }

        private void TailLog()
        {
            try
            {
                if (!File.Exists(_logPath)) return;

                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
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
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 5, 8, 5)
            };

            _clearBtn = new Button { Text = "Clear", Left = 8, Top = 5, Width = 70, Height = 28, Font = new Font("Segoe UI", 9f) };
            _clearBtn.Click += (s, e) => { _logBox.Clear(); };

            _copyBtn = new Button { Text = "Copy All", Left = 88, Top = 5, Width = 85, Height = 28, Font = new Font("Segoe UI", 9f) };
            _copyBtn.Click += (s, e) =>
            {
                if (_logBox.TextLength > 0)
                    Clipboard.SetText(_logBox.Text);
            };

            _openBtn = new Button { Text = "Open in Notepad", Left = 183, Top = 5, Width = 130, Height = 28, Font = new Font("Segoe UI", 9f) };
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
                Text = "Auto-scroll",
                Checked = true,
                Left = 330, Top = 8,
                Width = 110, Height = 24,
                Font = new Font("Segoe UI", 9f)
            };

            toolbar.Controls.AddRange(new Control[] { _clearBtn, _copyBtn, _openBtn, _autoScrollCheck });

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                DetectUrls = false
            };

            Controls.Add(_logBox);
            Controls.Add(toolbar);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tailTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}