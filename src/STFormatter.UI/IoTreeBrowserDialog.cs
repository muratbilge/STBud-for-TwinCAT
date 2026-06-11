using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using STFormatter.Core.IoTree;
using STFormatter.Core.Toolbox;

namespace STFormatter.UI
{
    /// <summary>
    /// I/O tree browser for picking a TcLinkTo path. Built for real machines
    /// with hundreds of terminals: live filter with ancestor-preserving
    /// matches, direction-aware coloring, attribute preview, and a fully
    /// responsive docked layout.
    /// </summary>
    public class IoTreeBrowserDialog : Form
    {
        private readonly IoTreeNode _root;
        private readonly TextBox _searchBox;
        private readonly TreeView _treeView;
        private readonly TextBox _pathBox;
        private readonly TextBox _previewBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Button _copyButton;
        private readonly Label _statsLabel;
        private readonly RadioButton _tiidRadio;
        private readonly RadioButton _tiibRadio;
        private readonly System.Windows.Forms.Timer _filterDebounce;
        private readonly Dictionary<IoTreeNode, IoTreeNode?> _parents = new Dictionary<IoTreeNode, IoTreeNode?>();
        private IoTreeNode? _selectedNode;

        private static Size _rememberedSize = new Size(680, 620);
        private static bool _preferTiib;

        private static readonly Color DeviceColor = Color.FromArgb(40, 40, 40);
        private static readonly Color BoxColor = Color.FromArgb(60, 60, 120);
        private static readonly Color InputColor = Color.FromArgb(0, 110, 0);
        private static readonly Color OutputColor = Color.FromArgb(170, 90, 0);
        private static readonly Color EntryColor = Color.FromArgb(30, 30, 30);

        public string SelectedPath { get; private set; } = "";

        public IoTreeBrowserDialog(IoTreeNode ioTree)
        {
            _root = ioTree;

            Text = Strings.Get("IOTree.Title");
            Size = _rememberedSize;
            MinimumSize = new Size(460, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9f);
            Icon = MainForm.AppIcon;

            // --- Top: search + expand/collapse ---
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 4,
                Padding = new Padding(10, 10, 10, 4),
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var searchLabel = new Label
            {
                Text = Strings.Get("IOTree.Filter"),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0),
            };

            _searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 8, 3),
            };
            _searchBox.TextChanged += (s, e) => _filterDebounce!.Stop();
            _searchBox.TextChanged += (s, e) => _filterDebounce!.Start();
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    // Enter in the filter jumps into the tree instead of OK-ing
                    e.SuppressKeyPress = true;
                    ApplyFilter();
                    if (_treeView!.Nodes.Count > 0)
                    {
                        _treeView.SelectedNode = _treeView.Nodes[0];
                        _treeView.Focus();
                    }
                }
                else if (e.KeyCode == Keys.Escape && _searchBox!.TextLength > 0)
                {
                    e.SuppressKeyPress = true;
                    _searchBox.Clear();
                }
            };

            var expandBtn = new Button
            {
                Text = Strings.Get("IOTree.ExpandAll"),
                AutoSize = true,
                Margin = new Padding(0, 2, 4, 2),
            };
            expandBtn.Click += (s, e) => { _treeView!.BeginUpdate(); _treeView.ExpandAll(); _treeView.EndUpdate(); };

            var collapseBtn = new Button
            {
                Text = Strings.Get("IOTree.CollapseAll"),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            collapseBtn.Click += (s, e) => { _treeView!.BeginUpdate(); _treeView.CollapseAll(); _treeView.EndUpdate(); };

            topPanel.Controls.Add(searchLabel, 0, 0);
            topPanel.Controls.Add(_searchBox, 1, 0);
            topPanel.Controls.Add(expandBtn, 2, 0);
            topPanel.Controls.Add(collapseBtn, 3, 0);

            // --- Middle: tree ---
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                ShowPlusMinus = true,
                ShowLines = true,
                ShowRootLines = true,
                HideSelection = false,
                FullRowSelect = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f),
                ItemHeight = 22,
            };
            _treeView.AfterSelect += TreeView_AfterSelect;
            _treeView.NodeMouseDoubleClick += TreeView_NodeMouseDoubleClick;

            var treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
            treeHost.Controls.Add(_treeView);

            // --- Bottom: link style, path, preview, stats, buttons ---
            var bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(10, 0, 10, 10),
            };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var styleRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
            };
            var styleLabel = new Label
            {
                Text = Strings.Get("IOTree.LinkStyle"),
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 0),
            };
            _tiidRadio = new RadioButton
            {
                Text = Strings.Get("IOTree.StyleTiid"),
                AutoSize = true,
                Checked = !_preferTiib,
                Margin = new Padding(0, 1, 12, 1),
            };
            _tiibRadio = new RadioButton
            {
                Text = Strings.Get("IOTree.StyleTiib"),
                AutoSize = true,
                Checked = _preferTiib,
                Margin = new Padding(0, 1, 0, 1),
            };
            _tiidRadio.CheckedChanged += (s, e) => { _preferTiib = _tiibRadio.Checked; UpdateSelection(); };
            _tiibRadio.CheckedChanged += (s, e) => { _preferTiib = _tiibRadio.Checked; UpdateSelection(); };
            styleRow.Controls.Add(styleLabel);
            styleRow.Controls.Add(_tiidRadio);
            styleRow.Controls.Add(_tiibRadio);

            var pathLabel = new Label
            {
                Text = Strings.Get("IOTree.Path"),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            };
            _pathBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = SystemColors.Window,
                Margin = new Padding(0, 3, 8, 3),
            };

            _copyButton = new Button
            {
                Text = Strings.Get("IOTree.CopyPath"),
                AutoSize = true,
                Enabled = false,
                Margin = new Padding(0, 2, 0, 2),
            };
            _copyButton.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(SelectedPath))
                {
                    try { Clipboard.SetText(SelectedPath); } catch { }
                }
            };

            var previewLabel = new Label
            {
                Text = Strings.Get("IOTree.Preview"),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            };
            _previewBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(245, 245, 245),
                ForeColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Consolas", 9f),
                Margin = new Padding(0, 3, 8, 3),
            };

            _statsLabel = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark,
                Margin = new Padding(0, 6, 0, 0),
            };

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            _cancelButton = new Button
            {
                Text = Strings.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 28),
                Margin = new Padding(6, 2, 0, 2),
            };
            _okButton = new Button
            {
                Text = Strings.Get("Common.OK"),
                DialogResult = DialogResult.OK,
                Size = new Size(90, 28),
                Enabled = false,
                Margin = new Padding(6, 2, 0, 2),
            };
            buttonRow.Controls.Add(_cancelButton);
            buttonRow.Controls.Add(_okButton);

            bottomPanel.Controls.Add(styleRow, 0, 0);
            bottomPanel.SetColumnSpan(styleRow, 2);
            bottomPanel.Controls.Add(pathLabel, 0, 1);
            bottomPanel.Controls.Add(_pathBox, 0, 2);
            bottomPanel.Controls.Add(_copyButton, 1, 2);
            bottomPanel.Controls.Add(previewLabel, 0, 3);
            bottomPanel.Controls.Add(_previewBox, 0, 4);
            bottomPanel.Controls.Add(_statsLabel, 0, 5);
            bottomPanel.Controls.Add(buttonRow, 1, 5);

            Controls.Add(treeHost);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            _filterDebounce = new System.Windows.Forms.Timer { Interval = 250 };
            _filterDebounce.Tick += (s, e) => { _filterDebounce.Stop(); ApplyFilter(); };

            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.F)
                {
                    e.SuppressKeyPress = true;
                    _searchBox.Focus();
                    _searchBox.SelectAll();
                }
            };

            ResizeEnd += (s, e) => _rememberedSize = Size;

            BuildParentMap(_root, null);
            PopulateTree(filter: "");
            UpdateStats();

            Shown += (s, e) =>
            {
                Activate();
                BringToFront();
                _searchBox.Focus();
                if (_treeView.Nodes.Count > 0)
                    _treeView.Nodes[0].Expand();
            };
        }

        private void ApplyFilter()
        {
            PopulateTree(_searchBox.Text.Trim());
            UpdateStats();
        }

        private void PopulateTree(string filter)
        {
            _treeView.BeginUpdate();
            _treeView.Nodes.Clear();

            foreach (var device in _root.Children)
            {
                var node = BuildNode(device, filter);
                if (node != null)
                    _treeView.Nodes.Add(node);
            }

            if (!string.IsNullOrEmpty(filter))
                _treeView.ExpandAll();

            _treeView.EndUpdate();
        }

        // Builds the visual node; when filtering, a node survives if it or any
        // descendant matches, so the hierarchy above a match stays visible.
        private TreeNode? BuildNode(IoTreeNode ioNode, string filter)
        {
            var children = new List<TreeNode>();
            foreach (var child in ioNode.Children)
            {
                var built = BuildNode(child, filter);
                if (built != null)
                    children.Add(built);
            }

            bool selfMatch = string.IsNullOrEmpty(filter) || Matches(ioNode, filter);
            if (!selfMatch && children.Count == 0)
                return null;

            var node = new TreeNode(ioNode.DisplayText)
            {
                Tag = ioNode,
                ForeColor = ColorFor(ioNode),
            };
            if (ioNode.NodeType == "Entry")
                node.NodeFont = new Font(_treeView.Font, FontStyle.Bold);

            node.Nodes.AddRange(children.ToArray());
            return node;
        }

        private static bool Matches(IoTreeNode node, string filter)
        {
            return node.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   node.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   node.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Color ColorFor(IoTreeNode node)
        {
            if (node.Direction == "Input") return InputColor;
            if (node.Direction == "Output") return OutputColor;
            return node.NodeType switch
            {
                "Device" => DeviceColor,
                "Box" => BoxColor,
                "Entry" => EntryColor,
                _ => SystemColors.WindowText,
            };
        }

        private void UpdateStats()
        {
            int devices = 0, boxes = 0, channels = 0;
            void Count(IoTreeNode n)
            {
                switch (n.NodeType)
                {
                    case "Device": devices++; break;
                    case "Box": boxes++; break;
                    case "Pdo":
                    case "Entry": channels++; break;
                }
                foreach (var c in n.Children) Count(c);
            }
            Count(_root);
            _statsLabel.Text = Strings.Get("IOTree.Stats", devices, boxes, channels);
        }

        private void BuildParentMap(IoTreeNode node, IoTreeNode? parent)
        {
            _parents[node] = parent;
            foreach (var child in node.Children)
                BuildParentMap(child, node);
        }

        /// <summary>
        /// TIIB[Box Name]^Channel^Entry - addresses the innermost terminal by
        /// name instead of the full device hierarchy, so the link survives
        /// device renames. Returns null when the node has no Box ancestor
        /// (devices, or the box itself), where only TIID applies.
        /// </summary>
        private string? BuildTiibPath(IoTreeNode node)
        {
            var below = new List<string>();
            var current = node;
            while (current != null && _parents.TryGetValue(current, out var parent))
            {
                if (current.NodeType == "Box" && below.Count > 0)
                    return $"TIIB[{current.Name}]^{string.Join("^", below)}";
                below.Insert(0, current.Name);
                current = parent;
            }
            return null;
        }

        private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            _selectedNode = e.Node?.Tag as IoTreeNode;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            var node = _selectedNode;
            if (node == null || string.IsNullOrEmpty(node.Path))
            {
                SelectedPath = "";
                _pathBox.Text = "";
                _previewBox.Text = "";
                _okButton.Enabled = false;
                _copyButton.Enabled = false;
                return;
            }

            string? tiib = BuildTiibPath(node);
            _tiibRadio.Enabled = tiib != null;
            if (tiib == null && _tiibRadio.Checked)
                _tiidRadio.Checked = true; // device-level nodes only support TIID

            SelectedPath = _tiibRadio.Checked && tiib != null ? tiib : node.Path;
            _pathBox.Text = SelectedPath;
            _previewBox.Text = PragmaTemplates.Attribute("TcLinkTo", SelectedPath);
            _okButton.Enabled = true;
            _copyButton.Enabled = true;
        }

        private void TreeView_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            // AfterSelect has already computed SelectedPath in the chosen style
            if (e.Node?.Tag is IoTreeNode node && !string.IsNullOrEmpty(node.Path) && !node.HasChildren &&
                !string.IsNullOrEmpty(SelectedPath))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _filterDebounce?.Dispose();
            base.Dispose(disposing);
        }
    }
}
