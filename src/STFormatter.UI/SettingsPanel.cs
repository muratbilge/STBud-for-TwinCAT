using System;
using System.Drawing;
using System.Windows.Forms;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    public class SettingsPanel : UserControl
    {
        private ComboBox _presetCombo;
        private ComboBox _indentStyleCombo;
        private NumericUpDown _indentSize;
        private NumericUpDown _continuationIndentSize;
        private ComboBox _newLineStyleCombo;
        private ComboBox _keywordCasingCombo;
        private ComboBox _braceStyleCombo;
        private CheckBox _spaceAroundOperators;
        private CheckBox _spaceAfterComma;
        private CheckBox _spaceBeforeSemicolon;
        private CheckBox _spaceAfterColon;
        private CheckBox _alignAssignments;
        private CheckBox _alignVariableDeclarations;
        private NumericUpDown _maxLineLength;
        private NumericUpDown _emptyLinesBetweenPOUs;
        private NumericUpDown _emptyLinesBetweenVarSections;
        private CheckBox _keepSingleLineBlocks;
        private CheckBox _formatOnSave;
        private CheckBox _startWithWindows;
        private RichTextBox _previewBox;

        public event Action<FormattingConfiguration>? SettingsApplied;

        private static readonly string SampleCode =
@"FUNCTION_BLOCK MotorControl
VAR_INPUT
xStart:BOOL;nStop:BOOL;nSpeed:INT;
END_VAR
VAR_OUTPUT
xRunning:BOOL;nActualSpeed:INT;
END_VAR
VAR
xInternal:BOOL;nCounter:INT;
tonDelay:TON;
END_VAR
IF xStart AND NOT xStop THEN
xInternal:=TRUE;nCounter:=nCounter+1;
IF nCounter>nSpeed THEN nCounter:=nSpeed;END_IF;
tonDelay(IN:=xInternal,PT:=T#2S);
xRunning:=tonDelay.Q;
ELSE
xInternal:=FALSE;nCounter:=0;xRunning:=FALSE;
END_IF";

        public SettingsPanel()
        {
            Dock = DockStyle.Fill;
            BuildUI();
            LoadFromConfig(SettingsManager.Current);
            _startWithWindows.Checked = AutoStart.IsEnabled();
        }

        private void BuildUI()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 340,
                SplitterWidth = 6,
                Panel1MinSize = 200,
                Panel2MinSize = 150,
                BackColor = SystemColors.Control
            };

            // --- Top panel: settings controls ---
            var topFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(8, 4, 8, 4)
            };

            // Preset bar
            var presetRow = new Panel { Width = 800, Height = 38, Margin = new Padding(0, 0, 0, 4) };
            var presetLabel = new Label { Text = "Preset:", Left = 0, Top = 8, Width = 50, Font = new Font("Segoe UI", 9.5f), TextAlign = ContentAlignment.MiddleRight };
            _presetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 58, Top = 5, Width = 180, Height = 28, Font = new Font("Segoe UI", 9.5f) };
            _presetCombo.Items.AddRange(new object[] { "Default", "Compact", "Expanded" });
            _presetCombo.SelectedIndex = 0;
            _presetCombo.SelectedIndexChanged += OnPresetChanged;
            var previewBtn = new Button { Text = "Preview", Left = 260, Top = 4, Width = 80, Height = 30, Font = new Font("Segoe UI", 9.5f) };
            previewBtn.Click += OnPreview;
            var applyBtn = new Button { Text = "Apply", Left = 350, Top = 4, Width = 80, Height = 30, Font = new Font("Segoe UI", 9.5f) };
            applyBtn.Click += OnApply;
            var resetBtn = new Button { Text = "Reset", Left = 440, Top = 4, Width = 80, Height = 30, Font = new Font("Segoe UI", 9.5f) };
            resetBtn.Click += OnReset;
            presetRow.Controls.AddRange(new Control[] { presetLabel, _presetCombo, previewBtn, applyBtn, resetBtn });
            topFlow.Controls.Add(presetRow);

            // Settings in two columns side by side
            var colsPanel = new Panel { Width = 800, Height = 260, Margin = new Padding(0) };

            var leftCol = new FlowLayoutPanel
            {
                Left = 0, Top = 0, Width = 390, Height = 260,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            leftCol.Controls.Add(MakeGroup("Indentation", BuildIndentControls()));
            leftCol.Controls.Add(MakeGroup("Newlines & Keywords", BuildKeywordControls()));
            leftCol.Controls.Add(MakeGroup("Spacing", BuildSpacingControls()));

            var rightCol = new FlowLayoutPanel
            {
                Left = 400, Top = 0, Width = 390, Height = 260,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            rightCol.Controls.Add(MakeGroup("Alignment & Limits", BuildAlignmentControls()));
            rightCol.Controls.Add(MakeGroup("Behavior", BuildBehaviorControls()));

            colsPanel.Controls.Add(leftCol);
            colsPanel.Controls.Add(rightCol);
            topFlow.Controls.Add(colsPanel);

            split.Panel1.Controls.Add(topFlow);

            // --- Bottom panel: preview ---
            var previewOuter = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
            var previewLabel = new Label { Dock = DockStyle.Top, Height = 24, Text = "Preview", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Padding = new Padding(0, 2, 0, 0) };
            _previewBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f),
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false
            };
            previewOuter.Controls.Add(_previewBox);
            previewOuter.Controls.Add(previewLabel);
            split.Panel2.Controls.Add(previewOuter);

            Controls.Add(split);
        }

        private static GroupBox MakeGroup(string title, Control inner)
        {
            var gb = new GroupBox
            {
                Text = title,
                Width = 380,
                Height = inner.Height + 26,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(8, 18, 8, 4),
                Margin = new Padding(0, 2, 0, 2)
            };
            inner.Dock = DockStyle.Fill;
            gb.Controls.Add(inner);
            return gb;
        }

        private Control BuildIndentControls()
        {
            var p = new Panel { Width = 360, Height = 98 };
            p.Controls.Add(MakeRow("Indent style:", _indentStyleCombo = MakeCombo(new[] { "spaces", "tabs" }, 0), 0));
            p.Controls.Add(MakeRow("Indent size:", _indentSize = MakeNum(1, 16, 4), 1));
            p.Controls.Add(MakeRow("Continuation indent:", _continuationIndentSize = MakeNum(1, 16, 8), 2));
            return p;
        }

        private Control BuildKeywordControls()
        {
            var p = new Panel { Width = 360, Height = 98 };
            p.Controls.Add(MakeRow("Newline style:", _newLineStyleCombo = MakeCombo(new[] { "crlf", "lf", "cr" }, 0), 0));
            p.Controls.Add(MakeRow("Keyword casing:", _keywordCasingCombo = MakeCombo(new[] { "upper", "lower", "pascal", "original" }, 0), 1));
            p.Controls.Add(MakeRow("Brace style:", _braceStyleCombo = MakeCombo(new[] { "allman", "kr" }, 0), 2));
            return p;
        }

        private Control BuildSpacingControls()
        {
            var p = new Panel { Width = 360, Height = 124 };
            _spaceAroundOperators = MakeCheck("Spaces around operators", true);
            _spaceAfterComma = MakeCheck("Space after comma", true);
            _spaceBeforeSemicolon = MakeCheck("Space before semicolon", false);
            _spaceAfterColon = MakeCheck("Space after colon", true);
            p.Controls.AddRange(new Control[] { _spaceAroundOperators, _spaceAfterComma, _spaceBeforeSemicolon, _spaceAfterColon });
            PositionChecks(p);
            return p;
        }

        private Control BuildAlignmentControls()
        {
            var p = new Panel { Width = 360, Height = 158 };
            _alignAssignments = MakeCheck("Align assignments", true);
            _alignVariableDeclarations = MakeCheck("Align variable declarations", true);
            p.Controls.Add(_alignAssignments);
            p.Controls.Add(_alignVariableDeclarations);
            p.Controls.Add(MakeRow("Max line length:", _maxLineLength = MakeNum(40, 200, 120), 2));
            p.Controls.Add(MakeRow("Empty lines between POUs:", _emptyLinesBetweenPOUs = MakeNum(0, 10, 2), 3));
            p.Controls.Add(MakeRow("Empty lines between VARs:", _emptyLinesBetweenVarSections = MakeNum(0, 10, 1), 4));
            _alignAssignments.Top = 0; _alignAssignments.Left = 4;
            _alignVariableDeclarations.Top = 26; _alignVariableDeclarations.Left = 4;
            return p;
        }

        private Control BuildBehaviorControls()
        {
            var p = new Panel { Width = 360, Height = 95 };
            _keepSingleLineBlocks = MakeCheck("Keep single-line blocks on one line", false);
            _formatOnSave = MakeCheck("Format on save", true);
            _startWithWindows = MakeCheck("Start with Windows", false);
            p.Controls.AddRange(new Control[] { _keepSingleLineBlocks, _formatOnSave, _startWithWindows });
            PositionChecks(p);
            return p;
        }

        private static void PositionChecks(Panel p)
        {
            int y = 0;
            foreach (Control c in p.Controls)
            {
                if (c is CheckBox) { c.Top = y; c.Left = 4; y += 28; }
            }
        }

        private static Control MakeRow(string labelText, Control inner, int row)
        {
            var rowPanel = new Panel { Top = row * 30, Left = 0, Width = 360, Height = 28 };
            var lbl = new Label
            {
                Text = labelText, Left = 0, Top = 4, Width = 170,
                AutoSize = false, Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleRight
            };
            inner.Left = 178; inner.Top = 2; inner.Font = new Font("Segoe UI", 9f);
            rowPanel.Controls.Add(lbl);
            rowPanel.Controls.Add(inner);
            return rowPanel;
        }

        private static ComboBox MakeCombo(string[] items, int idx)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Height = 26 };
            cb.Items.AddRange(items); cb.SelectedIndex = idx;
            return cb;
        }

        private static NumericUpDown MakeNum(int min, int max, int val)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 80, Height = 26 };
        }

        private static CheckBox MakeCheck(string text, bool isChecked)
        {
            return new CheckBox { Text = text, Checked = isChecked, Width = 350, Height = 26, Font = new Font("Segoe UI", 9f) };
        }

        private void OnPresetChanged(object? sender, EventArgs e)
        {
            LoadFromConfig(FormattingConfiguration.FromPreset(_presetCombo.SelectedItem?.ToString() ?? "Default"));
        }

        private void OnPreview(object? sender, EventArgs e)
        {
            try { _previewBox.Text = new FormattingEngine(BuildConfig()).Format(SampleCode); }
            catch (Exception ex) { _previewBox.Text = $"Preview error: {ex.Message}"; }
        }

        private void OnApply(object? sender, EventArgs e)
        {
            var config = BuildConfig();
            SettingsManager.Save(config);
            SettingsManager.Current = config;
            if (_startWithWindows.Checked) AutoStart.Enable(); else AutoStart.Disable();
            SettingsApplied?.Invoke(config);
        }

        private void OnReset(object? sender, EventArgs e)
        {
            LoadFromConfig(FormattingConfiguration.Default);
            SettingsManager.ResetToDefault();
            _presetCombo.SelectedIndex = 0;
        }

        private FormattingConfiguration BuildConfig() => new FormattingConfiguration
        {
            IndentStyle = _indentStyleCombo.SelectedItem?.ToString() ?? "spaces",
            IndentSize = (int)_indentSize.Value,
            ContinuationIndentSize = (int)_continuationIndentSize.Value,
            NewLineStyle = _newLineStyleCombo.SelectedItem?.ToString() ?? "crlf",
            KeywordCasing = _keywordCasingCombo.SelectedItem?.ToString() ?? "upper",
            BraceStyle = _braceStyleCombo.SelectedItem?.ToString() ?? "allman",
            SpaceAroundOperators = _spaceAroundOperators.Checked,
            SpaceAfterComma = _spaceAfterComma.Checked,
            SpaceBeforeSemicolon = _spaceBeforeSemicolon.Checked,
            SpaceAfterColon = _spaceAfterColon.Checked,
            AlignAssignments = _alignAssignments.Checked,
            AlignVariableDeclarations = _alignVariableDeclarations.Checked,
            MaxLineLength = (int)_maxLineLength.Value,
            EmptyLinesBetweenPOUs = (int)_emptyLinesBetweenPOUs.Value,
            EmptyLinesBetweenVarSections = (int)_emptyLinesBetweenVarSections.Value,
            KeepSingleLineBlocks = _keepSingleLineBlocks.Checked,
            FormatOnSave = _formatOnSave.Checked
        };

        private void LoadFromConfig(FormattingConfiguration c)
        {
            _indentStyleCombo.SelectedIndex = c.IndentStyle == "tabs" ? 1 : 0;
            _indentSize.Value = c.IndentSize;
            _continuationIndentSize.Value = c.ContinuationIndentSize;
            _newLineStyleCombo.SelectedIndex = c.NewLineStyle switch { "lf" => 1, "cr" => 2, _ => 0 };
            _keywordCasingCombo.SelectedIndex = c.KeywordCasing switch { "lower" => 1, "pascal" => 2, "original" => 3, _ => 0 };
            _braceStyleCombo.SelectedIndex = c.BraceStyle == "kr" ? 1 : 0;
            _spaceAroundOperators.Checked = c.SpaceAroundOperators;
            _spaceAfterComma.Checked = c.SpaceAfterComma;
            _spaceBeforeSemicolon.Checked = c.SpaceBeforeSemicolon;
            _spaceAfterColon.Checked = c.SpaceAfterColon;
            _alignAssignments.Checked = c.AlignAssignments;
            _alignVariableDeclarations.Checked = c.AlignVariableDeclarations;
            _maxLineLength.Value = c.MaxLineLength;
            _emptyLinesBetweenPOUs.Value = c.EmptyLinesBetweenPOUs;
            _emptyLinesBetweenVarSections.Value = c.EmptyLinesBetweenVarSections;
            _keepSingleLineBlocks.Checked = c.KeepSingleLineBlocks;
            _formatOnSave.Checked = c.FormatOnSave;
        }
    }
}