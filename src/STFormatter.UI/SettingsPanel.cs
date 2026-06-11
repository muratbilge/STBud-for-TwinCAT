using System;
using System.Drawing;
using System.Globalization;
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
        private CheckBox _wrapLongLines;
        private NumericUpDown _maxLineLength;
        private NumericUpDown _emptyLinesBetweenPOUs;
        private NumericUpDown _emptyLinesBetweenVarSections;
        private CheckBox _keepSingleLineBlocks;
        private CheckBox _formatOnSave;
        private CheckBox _startWithWindows;
        private RichTextBox _previewBox;
        private Label _savedAtLabel;
        private SplitContainer _split;
        private Panel _scroll;
        private TableLayoutPanel _grid;

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
            AutoScaleMode = AutoScaleMode.Font;
            BuildUI();
            LoadFromConfig(SettingsManager.Current);
            LoadFromAppSettings();
            UpdateSavedAtLabel();
        }

        public void RebuildUi()
        {
            var config = BuildConfig();

            _split.SuspendLayout();
            try
            {
                _grid.Controls.Clear();
                _grid.RowStyles.Clear();
                for (int i = 0; i < 4; i++)
                    _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var presetRow = BuildPresetRow();
                _grid.SetColumnSpan(presetRow, 2);
                _grid.Controls.Add(presetRow, 0, 0);

                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Indentation"), BuildIndentControls()), 0, 1);
                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Spacing"), BuildSpacingControls()), 1, 1);
                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Keywords"), BuildKeywordControls()), 0, 2);
                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Lines"), BuildLineControls()), 1, 2);
                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Behavior"), BuildBehaviorControls()), 0, 3);
                _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.General"), BuildGeneralControls()), 1, 3);

                LoadFromConfig(config);
            }
            finally
            {
                _split.ResumeLayout(true);
            }

            _savedAtLabel.Text = Strings.Get("Settings.NeverSaved");
            UpdateSavedAtLabel();
        }

        private void BuildUI()
        {
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                Panel1MinSize = 200,
                BackColor = SystemColors.Control,
            };
            _split.HandleCreated += (s, e) =>
            {
                _split.BeginInvoke(new Action(() =>
                {
                    var h = _split.Height;
                    if (h > 0)
                    {
                        _split.Panel2MinSize = Math.Min(100, h / 2);
                        _split.SplitterDistance = Math.Max(_split.Panel1MinSize, h - _split.Panel2MinSize - _split.SplitterWidth);
                    }
                }));
            };

            _scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4),
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < 4; i++)
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var presetRow = BuildPresetRow();
            _grid.SetColumnSpan(presetRow, 2);
            _grid.Controls.Add(presetRow, 0, 0);

            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Indentation"), BuildIndentControls()), 0, 1);
            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Spacing"), BuildSpacingControls()), 1, 1);
            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Keywords"), BuildKeywordControls()), 0, 2);
            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Lines"), BuildLineControls()), 1, 2);
            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.Behavior"), BuildBehaviorControls()), 0, 3);
            _grid.Controls.Add(MakeGroup(Strings.Get("Settings.Group.General"), BuildGeneralControls()), 1, 3);

            _scroll.Controls.Add(_grid);
            _split.Panel1.Controls.Add(_scroll);
            _split.Panel2.Controls.Add(BuildPreviewPanel());

            Controls.Add(_split);
        }

        private Control BuildPresetRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 6,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var presetLabel = new Label
            {
                Text = Strings.Get("Settings.Preset"),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 6, 0),
            };
            _presetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 8, 3),
            };
            _presetCombo.Items.Add(Strings.Get("Settings.Preset.Default"));
            _presetCombo.Items.Add(Strings.Get("Settings.Preset.Compact"));
            _presetCombo.Items.Add(Strings.Get("Settings.Preset.Expanded"));
            _presetCombo.SelectedIndex = 0;
            _presetCombo.SelectedIndexChanged += OnPresetChanged;

            var previewBtn = new Button
            {
                Text = Strings.Get("Settings.Button.Preview"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 3, 4, 3),
            };
            previewBtn.Click += OnPreview;

            var applyBtn = new Button
            {
                Text = Strings.Get("Settings.Button.Apply"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 3, 4, 3),
            };
            applyBtn.Click += OnApply;

            var resetBtn = new Button
            {
                Text = Strings.Get("Settings.Button.Reset"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 3, 4, 3),
            };
            resetBtn.Click += OnReset;

            row.Controls.Add(presetLabel, 0, 0);
            row.Controls.Add(_presetCombo, 1, 0);
            row.Controls.Add(previewBtn, 2, 0);
            row.Controls.Add(applyBtn, 3, 0);
            row.Controls.Add(resetBtn, 4, 0);

            return row;
        }

        private Control BuildIndentControls()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            _indentStyleCombo = MakeCombo(new[] { "spaces", "tabs" }, 0);
            _indentSize = MakeNum(1, 16, 4);
            _continuationIndentSize = MakeNum(1, 16, 8);

            AddRow(p, Strings.Get("Settings.IndentStyle"), _indentStyleCombo);
            AddRow(p, Strings.Get("Settings.IndentSize"), _indentSize);
            AddRow(p, Strings.Get("Settings.ContinuationIndent"), _continuationIndentSize);
            return p;
        }

        private Control BuildSpacingControls()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            _spaceAroundOperators = MakeCheck(Strings.Get("Settings.SpaceAroundOperators"), true);
            _spaceAfterComma = MakeCheck(Strings.Get("Settings.SpaceAfterComma"), true);
            _spaceBeforeSemicolon = MakeCheck(Strings.Get("Settings.SpaceBeforeSemicolon"), false);
            _spaceAfterColon = MakeCheck(Strings.Get("Settings.SpaceAfterColon"), true);
            p.Controls.AddRange(new Control[] { _spaceAroundOperators, _spaceAfterComma, _spaceBeforeSemicolon, _spaceAfterColon });
            return p;
        }

        private Control BuildKeywordControls()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            _newLineStyleCombo = MakeCombo(new[] { "crlf", "lf", "cr" }, 0);
            _keywordCasingCombo = MakeCombo(new[] { "upper", "lower", "pascal", "original" }, 0);
            _braceStyleCombo = MakeCombo(new[] { "allman", "compact" }, 0);

            AddRow(p, Strings.Get("Settings.Newline"), _newLineStyleCombo);
            AddRow(p, Strings.Get("Settings.KeywordCasing"), _keywordCasingCombo);
            AddRow(p, Strings.Get("Settings.BraceStyle"), _braceStyleCombo);
            return p;
        }

        private Control BuildLineControls()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            _alignAssignments = MakeCheck(Strings.Get("Settings.AlignAssignments"), true);
            _alignVariableDeclarations = MakeCheck(Strings.Get("Settings.AlignDeclarations"), true);
            _wrapLongLines = MakeCheck(Strings.Get("Settings.WrapLongLines"), true);
            _wrapLongLines.CheckedChanged += (s, e) => _maxLineLength.Enabled = _wrapLongLines.Checked;
            _maxLineLength = MakeNum(40, 200, 120);
            _emptyLinesBetweenPOUs = MakeNum(0, 10, 2);
            _emptyLinesBetweenVarSections = MakeNum(0, 10, 1);

            AddRow(p, _alignAssignments);
            AddRow(p, _alignVariableDeclarations);
            AddRow(p, _wrapLongLines);
            AddRow(p, Strings.Get("Settings.MaxLineLength"), _maxLineLength);
            AddRow(p, Strings.Get("Settings.EmptyLinesBetweenPOUs"), _emptyLinesBetweenPOUs);
            AddRow(p, Strings.Get("Settings.EmptyLinesBetweenVarSections"), _emptyLinesBetweenVarSections);
            return p;
        }

        private Control BuildBehaviorControls()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            _keepSingleLineBlocks = MakeCheck(Strings.Get("Settings.KeepSingleLineBlocks"), false);
            _formatOnSave = MakeCheck(Strings.Get("Settings.FormatOnSave"), true);
            p.Controls.AddRange(new Control[] { _keepSingleLineBlocks, _formatOnSave });
            return p;
        }

        private Control BuildGeneralControls()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            _startWithWindows = MakeCheck(Strings.Get("Settings.StartWithWindows"), false);
            _startWithWindows.CheckedChanged += (s, e) =>
            {
                if (_startWithWindows.Checked) AutoStart.Enable();
                else AutoStart.Disable();
            };

            AddRow(p, _startWithWindows);

            return p;
        }

        private Control BuildPreviewPanel()
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(4),
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var previewLabel = new Label
            {
                Text = Strings.Get("Settings.Button.Preview"),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            outer.Controls.Add(previewLabel, 0, 0);

            _savedAtLabel = new Label
            {
                Text = Strings.Get("Settings.NeverSaved"),
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark,
            };
            outer.Controls.Add(_savedAtLabel, 0, 1);

            _previewBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f),
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false,
                MaxLength = 0,
            };
            outer.Controls.Add(_previewBox, 0, 2);

            return outer;
        }

        private static GroupBox MakeGroup(string title, Control inner)
        {
            var gb = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
                Margin = new Padding(4, 2, 4, 2),
            };
            inner.Dock = DockStyle.Fill;
            gb.Controls.Add(inner);
            return gb;
        }

        private static void AddRow(TableLayoutPanel p, string labelText, Control inner)
        {
            int rowIndex = p.RowCount++;
            p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 6, 3),
            };
            p.Controls.Add(lbl, 0, rowIndex);
            if (inner != null)
            {
                inner.Dock = DockStyle.Fill;
                inner.Margin = new Padding(0, 3, 0, 3);
                p.Controls.Add(inner, 1, rowIndex);
            }
        }

        private static void AddRow(TableLayoutPanel p, CheckBox check)
        {
            int rowIndex = p.RowCount++;
            p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            p.SetColumnSpan(check, 2);
            check.Margin = new Padding(0, 3, 0, 3);
            p.Controls.Add(check, 0, rowIndex);
        }

        private static ComboBox MakeCombo(string[] items, int idx)
        {
            var cb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3),
            };
            cb.Items.AddRange(items);
            cb.SelectedIndex = idx;
            return cb;
        }

        private static NumericUpDown MakeNum(int min, int max, int val)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3),
            };
        }

        private static CheckBox MakeCheck(string text, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };
        }

        private void OnPresetChanged(object? sender, EventArgs e)
        {
            string name = _presetCombo.SelectedIndex switch
            {
                1 => "Compact",
                2 => "Expanded",
                _ => "Default",
            };
            LoadFromConfig(FormattingConfiguration.FromPreset(name));
        }

        private void OnPreview(object? sender, EventArgs e)
        {
            try
            {
                _previewBox.Text = new FormattingEngine(BuildConfig()).Format(SampleCode);
            }
            catch (Exception ex)
            {
                _previewBox.Text = Strings.Get("Settings.Preview.Error", ex.Message);
            }
        }

        private void OnApply(object? sender, EventArgs e)
        {
            var config = BuildConfig();
            var app = SettingsManager.App;
            app.Formatting = config;
            app.Language = Strings.Culture;
            SettingsManager.SaveAppSettings(app);

            if (_startWithWindows.Checked) AutoStart.Enable();
            else AutoStart.Disable();

            UpdateSavedAtLabel();
            SafeInvokeSettingsApplied(config);
        }

        private void OnReset(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(
                FindForm(),
                Strings.Get("Settings.ResetConfirm.Text"),
                Strings.Get("Settings.ResetConfirm.Title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            LoadFromConfig(FormattingConfiguration.Default);
            _presetCombo.SelectedIndex = 0;

            var app = SettingsManager.App;
            app.Formatting = FormattingConfiguration.Default;
            SettingsManager.SaveAppSettings(app);
            UpdateSavedAtLabel();
        }

        private void SafeInvokeSettingsApplied(FormattingConfiguration config)
        {
            try { SettingsApplied?.Invoke(config); }
            catch (Exception ex)
            {
                STFormatter.Core.Configuration.HostLog.Append("SettingsPanel", $"SettingsApplied handler failed: {ex.Message}");
            }
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
            WrapLongLines = _wrapLongLines.Checked,
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
            // Anything non-allman formats compact ("kr"/"k&r"/"stroustrup" are aliases)
            _braceStyleCombo.SelectedIndex = c.IsAllmanStyle() ? 0 : 1;
            _spaceAroundOperators.Checked = c.SpaceAroundOperators;
            _spaceAfterComma.Checked = c.SpaceAfterComma;
            _spaceBeforeSemicolon.Checked = c.SpaceBeforeSemicolon;
            _spaceAfterColon.Checked = c.SpaceAfterColon;
            _alignAssignments.Checked = c.AlignAssignments;
            _alignVariableDeclarations.Checked = c.AlignVariableDeclarations;
            _wrapLongLines.Checked = c.WrapLongLines;
            _maxLineLength.Value = Math.Max(_maxLineLength.Minimum, Math.Min(_maxLineLength.Maximum, c.MaxLineLength));
            _maxLineLength.Enabled = c.WrapLongLines;
            _emptyLinesBetweenPOUs.Value = c.EmptyLinesBetweenPOUs;
            _emptyLinesBetweenVarSections.Value = c.EmptyLinesBetweenVarSections;
            _keepSingleLineBlocks.Checked = c.KeepSingleLineBlocks;
            _formatOnSave.Checked = c.FormatOnSave;
        }

        private void LoadFromAppSettings()
        {
            _startWithWindows.Checked = AutoStart.IsEnabled();
        }

        private void UpdateSavedAtLabel()
        {
            var app = SettingsManager.App;
            if (app.LastSavedUtc is DateTime utc)
            {
                var local = utc.ToLocalTime();
                _savedAtLabel.Text = Strings.Get("Settings.SavedAt", local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
            }
            else
            {
                _savedAtLabel.Text = Strings.Get("Settings.NeverSaved");
            }
        }
    }
}