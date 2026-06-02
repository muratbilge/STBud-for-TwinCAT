using System;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class InputDialog : Form
    {
        private readonly TextBox _textBox;
        public string InputText { get; private set; }

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Text = title;
            Size = new System.Drawing.Size(400, 160);
            MinimumSize = new System.Drawing.Size(300, 140);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            Font = new System.Drawing.Font("Segoe UI", 9f);

            var label = new Label
            {
                Text = prompt,
                Location = new System.Drawing.Point(12, 12),
                AutoSize = true
            };

            _textBox = new TextBox
            {
                Text = defaultValue,
                Location = new System.Drawing.Point(12, 36),
                Size = new System.Drawing.Size(360, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _textBox.SelectAll();

            var okBtn = new Button
            {
                Text = Strings.Get("Common.OK"),
                DialogResult = DialogResult.OK,
                Size = new System.Drawing.Size(85, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            var cancelBtn = new Button
            {
                Text = Strings.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Size = new System.Drawing.Size(85, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };
            cancelBtn.Location = new System.Drawing.Point(buttonPanel.Width - cancelBtn.Width - 12, 7);
            okBtn.Location = new System.Drawing.Point(cancelBtn.Left - okBtn.Width - 6, 7);
            buttonPanel.Controls.Add(okBtn);
            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Resize += (s, e) =>
            {
                cancelBtn.Left = buttonPanel.Width - cancelBtn.Width - 12;
                okBtn.Left = cancelBtn.Left - okBtn.Width - 6;
            };

            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            Controls.Add(label);
            Controls.Add(_textBox);
            Controls.Add(buttonPanel);

            ActiveControl = _textBox;

            Shown += (s, e) =>
            {
                Activate();
                BringToFront();
            };

            FormClosing += (s, e) =>
            {
                InputText = DialogResult == DialogResult.OK ? _textBox.Text.Trim() : null;
            };
        }
    }
}