using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using STFormatter.Core.IoTree;

namespace STFormatter.UI
{
    public class IoTreeBrowserDialog : Form
    {
        private TreeView _treeView;
        private TextBox _pathBox;
        private Button _okButton;
        private Button _cancelButton;
        private Label _pathLabel;

        public string SelectedPath { get; private set; } = "";

        public IoTreeBrowserDialog(IoTreeNode ioTree)
        {
            Text = Strings.Get("IOTree.Title");
            Size = new Size(560, 500);
            MinimumSize = new Size(400, 350);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 9f);

            _pathLabel = new Label
            {
                Text = Strings.Get("IOTree.Path"),
                Location = new Point(12, 12),
                AutoSize = true
            };

            _pathBox = new TextBox
            {
                Location = new Point(12, 32),
                Size = new Size(520, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                BackColor = SystemColors.Window
            };

            _treeView = new TreeView
            {
                Location = new Point(12, 62),
                Size = new Size(520, 330),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ShowPlusMinus = true,
                ShowLines = true,
                ShowRootLines = true,
                HideSelection = false,
                FullRowSelect = true
            };

            _okButton = new Button
            {
                Text = Strings.Get("Common.OK"),
                DialogResult = DialogResult.OK,
                Size = new Size(85, 26),
                Enabled = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            _cancelButton = new Button
            {
                Text = Strings.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Size = new Size(85, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };
            _cancelButton.Location = new Point(buttonPanel.Width - _cancelButton.Width - 12, 7);
            _okButton.Location = new Point(_cancelButton.Left - _okButton.Width - 6, 7);
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Resize += (s, e) =>
            {
                _cancelButton.Left = buttonPanel.Width - _cancelButton.Width - 12;
                _okButton.Left = _cancelButton.Left - _okButton.Width - 6;
            };

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.Add(_pathLabel);
            Controls.Add(_pathBox);
            Controls.Add(_treeView);
            Controls.Add(buttonPanel);

            _treeView.AfterSelect += TreeView_AfterSelect;
            _treeView.NodeMouseDoubleClick += TreeView_NodeMouseDoubleClick;

            PopulateTree(ioTree);

            Shown += (s, e) =>
            {
                Activate();
                BringToFront();
                if (_treeView.Nodes.Count > 0)
                    _treeView.Nodes[0].Expand();
            };
        }

        private void PopulateTree(IoTreeNode ioTree)
        {
            _treeView.BeginUpdate();
            _treeView.Nodes.Clear();

            foreach (var device in ioTree.Children)
            {
                var deviceNode = new TreeNode(device.DisplayText) { Tag = device };
                AddChildNodes(deviceNode, device);
                _treeView.Nodes.Add(deviceNode);
            }

            _treeView.EndUpdate();
        }

        private void AddChildNodes(TreeNode parentNode, IoTreeNode ioNode)
        {
            foreach (var child in ioNode.Children)
            {
                var childNode = new TreeNode(child.DisplayText) { Tag = child };
                AddChildNodes(childNode, child);
                parentNode.Nodes.Add(childNode);
            }
        }

        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is IoTreeNode node && !string.IsNullOrEmpty(node.Path))
            {
                _pathBox.Text = node.Path;
                SelectedPath = node.Path;
                _okButton.Enabled = true;
            }
            else
            {
                _pathBox.Text = "";
                SelectedPath = "";
                _okButton.Enabled = false;
            }
        }

        private void TreeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is IoTreeNode node && !string.IsNullOrEmpty(node.Path))
            {
                SelectedPath = node.Path;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}