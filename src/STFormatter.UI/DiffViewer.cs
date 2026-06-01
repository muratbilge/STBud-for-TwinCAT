using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class DiffViewerForm : Form
    {
        private readonly RichTextBox _leftBox;
        private readonly RichTextBox _rightBox;
        private readonly CheckBox _changesOnlyCheckbox;
        private readonly Label _statsLabel;
        private List<DiffLine> _allDiffLines;
        private bool _syncingScroll;

        public DiffViewerForm(string title, string originalText, string formattedText)
        {
            Text = "ST Formatter - Diff";
            Size = new Size(1100, 700);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            Icon = MainForm.AppIcon;

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(6, 4, 6, 0)
            };

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _statsLabel = new Label
            {
                Dock = DockStyle.Right,
                Width = 380,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.Gray
            };

            topPanel.Controls.Add(titleLabel);
            topPanel.Controls.Add(_statsLabel);

            var toolbarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(6, 2, 6, 2),
                BackColor = Color.FromArgb(245, 245, 245)
            };

            _changesOnlyCheckbox = new CheckBox
            {
                Text = "Changes only",
                Checked = false,
                Dock = DockStyle.Left,
                Font = new Font("Segoe UI", 9f)
            };
            _changesOnlyCheckbox.CheckedChanged += (s, e) => RenderDiff();

            toolbarPanel.Controls.Add(_changesOnlyCheckbox);

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 500,
                SplitterWidth = 8,
                BackColor = SystemColors.ControlDark
            };
            splitContainer.SizeChanged += (s, e) =>
            {
                splitContainer.SplitterDistance = splitContainer.Width / 2;
            };

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var leftHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(255, 230, 230)
            };
            var leftHeaderText = new Label
            {
                Dock = DockStyle.Fill,
                Text = "  Original",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(255, 230, 230)
            };
            leftHeader.Controls.Add(leftHeaderText);
            _leftBox = CreateRichTextBox();
            leftPanel.Controls.Add(_leftBox);
            leftPanel.Controls.Add(leftHeader);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var rightHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(230, 255, 230)
            };
            var rightHeaderText = new Label
            {
                Dock = DockStyle.Fill,
                Text = "  Formatted",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(230, 255, 230)
            };
            rightHeader.Controls.Add(rightHeaderText);
            _rightBox = CreateRichTextBox();
            rightPanel.Controls.Add(_rightBox);
            rightPanel.Controls.Add(rightHeader);

            splitContainer.Panel1.Controls.Add(leftPanel);
            splitContainer.Panel2.Controls.Add(rightPanel);

            Controls.Add(splitContainer);
            Controls.Add(toolbarPanel);
            Controls.Add(topPanel);

            _leftBox.VScroll += OnLeftScroll;
            _rightBox.VScroll += OnRightScroll;

            LoadDiff(originalText, formattedText);
        }

        private static RichTextBox CreateRichTextBox()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f),
                ReadOnly = true,
                WordWrap = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                DetectUrls = false
            };
        }

        private void OnLeftScroll(object sender, EventArgs e)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            int pos = NativeMethods.GetScrollPos(_leftBox.Handle, NativeMethods.SB_VERT);
            NativeMethods.SetScrollPos(_rightBox.Handle, NativeMethods.SB_VERT, pos, true);
            NativeMethods.SendMessage(_rightBox.Handle, NativeMethods.WM_VSCROLL, (IntPtr)NativeMethods.SB_THUMBPOSITION, IntPtr.Zero);
            _syncingScroll = false;
        }

        private void OnRightScroll(object sender, EventArgs e)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            int pos = NativeMethods.GetScrollPos(_rightBox.Handle, NativeMethods.SB_VERT);
            NativeMethods.SetScrollPos(_leftBox.Handle, NativeMethods.SB_VERT, pos, true);
            NativeMethods.SendMessage(_leftBox.Handle, NativeMethods.WM_VSCROLL, (IntPtr)NativeMethods.SB_THUMBPOSITION, IntPtr.Zero);
            _syncingScroll = false;
        }

        private static class NativeMethods
        {
            public const int WM_VSCROLL = 0x115;
            public const int SB_VERT = 1;
            public const int SB_THUMBPOSITION = 4;

            [DllImport("user32.dll")]
            public static extern int GetScrollPos(IntPtr hWnd, int nBar);

            [DllImport("user32.dll")]
            public static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

            [DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }

        private void LoadDiff(string original, string formatted)
        {
            string[] leftLines = SplitLines(original);
            string[] rightLines = SplitLines(formatted);

            _allDiffLines = ComputeDiff(leftLines, rightLines);
            RenderDiff();
        }

        private void RenderDiff()
        {
            var diff = _allDiffLines;
            bool changesOnly = _changesOnlyCheckbox.Checked;

            var filtered = changesOnly
                ? FilterToChanges(diff, 3)
                : diff;

            RenderSide(_leftBox, filtered, Side.Left);
            RenderSide(_rightBox, filtered, Side.Right);

            int added = diff.Count(d => d.Type == DiffType.Added);
            int removed = diff.Count(d => d.Type == DiffType.Removed);
            int changed = diff.Count(d => d.Type == DiffType.Changed);
            _statsLabel.Text = $"  +{added}  -{removed}  ~{changed}  |  {diff.Count(d => d.Type == DiffType.Unchanged)} unchanged";
        }

        private static List<DiffLine> FilterToChanges(List<DiffLine> diff, int contextLines)
        {
            var result = new List<DiffLine>();
            var changeIndices = new HashSet<int>();
            for (int i = 0; i < diff.Count; i++)
            {
                if (diff[i].Type != DiffType.Unchanged)
                    changeIndices.Add(i);
            }

            var includeIndices = new HashSet<int>();
            foreach (var idx in changeIndices)
            {
                for (int c = -contextLines; c <= contextLines; c++)
                {
                    int target = idx + c;
                    if (target >= 0 && target < diff.Count)
                        includeIndices.Add(target);
                }
            }

            bool prevIncluded = false;
            for (int i = 0; i < diff.Count; i++)
            {
                if (includeIndices.Contains(i))
                {
                    if (!prevIncluded && result.Count > 0)
                    {
                        result.Add(new DiffLine { Type = DiffType.Snip });
                    }
                    result.Add(diff[i]);
                    prevIncluded = true;
                }
                else
                {
                    prevIncluded = false;
                }
            }

            return result;
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();
            return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        private void RenderSide(RichTextBox box, List<DiffLine> diff, Side side)
        {
            box.Clear();
            foreach (var line in diff)
            {
                if (line.Type == DiffType.Snip)
                {
                    box.Select(box.TextLength, 0);
                    box.SelectionBackColor = Color.FromArgb(240, 240, 240);
                    box.SelectionColor = Color.Gray;
                    box.SelectedText = "     ···  ···  ···\n";
                    continue;
                }

                bool show = line.Type switch
                {
                    DiffType.Unchanged => true,
                    DiffType.Added => side == Side.Right,
                    DiffType.Removed => side == Side.Left,
                    DiffType.Changed => true,
                    _ => true
                };

                if (!show) continue;

                string gutterMarker = line.Type switch
                {
                    DiffType.Unchanged => " ",
                    DiffType.Added => "+",
                    DiffType.Removed => "-",
                    DiffType.Changed => "~",
                    _ => " "
                };

                Color foreColor = line.Type switch
                {
                    DiffType.Unchanged => Color.FromArgb(80, 80, 80),
                    DiffType.Added => Color.FromArgb(0, 100, 0),
                    DiffType.Removed => Color.FromArgb(180, 0, 0),
                    DiffType.Changed => Color.FromArgb(0, 0, 160),
                    _ => Color.Black
                };

                Color backColor = line.Type switch
                {
                    DiffType.Unchanged => Color.White,
                    DiffType.Added => Color.FromArgb(220, 255, 220),
                    DiffType.Removed => Color.FromArgb(255, 220, 220),
                    DiffType.Changed => Color.FromArgb(255, 255, 200),
                    _ => Color.White
                };

                string text = side == Side.Left ? line.LeftText : line.RightText;
                string lineNum = side == Side.Left
                    ? (line.LeftLine > 0 ? line.LeftLine.ToString() : "")
                    : (line.RightLine > 0 ? line.RightLine.ToString() : "");

                box.Select(box.TextLength, 0);
                box.SelectionBackColor = backColor;
                box.SelectionColor = Color.Gray;
                box.SelectedText = $"{lineNum,4} ";

                box.Select(box.TextLength, 0);
                box.SelectionBackColor = backColor;
                box.SelectionFont = new Font(box.Font, FontStyle.Bold);
                box.SelectionColor = foreColor;
                box.SelectedText = gutterMarker;

                box.Select(box.TextLength, 0);
                box.SelectionBackColor = backColor;
                box.SelectionFont = box.Font;
                box.SelectionColor = foreColor;
                box.SelectedText = " " + text + "\n";
            }

            box.Select(0, 0);
        }

        private enum Side { Left, Right }

        private enum DiffType { Unchanged, Added, Removed, Changed, Snip }

        private class DiffLine
        {
            public DiffType Type;
            public string LeftText = "";
            public string RightText = "";
            public int LeftLine;
            public int RightLine;
        }

        private static List<DiffLine> ComputeDiff(string[] left, string[] right)
        {
            var result = new List<DiffLine>();
            int[,] lcs = ComputeLCS(left, right);
            int i = left.Length, j = right.Length;

            var actions = new List<(DiffType type, int li, int ri)>();
            while (i > 0 || j > 0)
            {
                if (i > 0 && j > 0 && left[i - 1] == right[j - 1])
                {
                    actions.Add((DiffType.Unchanged, i - 1, j - 1));
                    i--; j--;
                }
                else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
                {
                    actions.Add((DiffType.Added, -1, j - 1));
                    j--;
                }
                else
                {
                    actions.Add((DiffType.Removed, i - 1, -1));
                    i--;
                }
            }

            actions.Reverse();

            int leftLine = 1, rightLine = 1;
            foreach (var (type, li, ri) in actions)
            {
                switch (type)
                {
                    case DiffType.Unchanged:
                        result.Add(new DiffLine
                        {
                            Type = DiffType.Unchanged,
                            LeftText = left[li],
                            RightText = right[ri],
                            LeftLine = leftLine++,
                            RightLine = rightLine++
                        });
                        break;
                    case DiffType.Added:
                        result.Add(new DiffLine
                        {
                            Type = DiffType.Added,
                            RightText = right[ri],
                            RightLine = rightLine++
                        });
                        break;
                    case DiffType.Removed:
                        result.Add(new DiffLine
                        {
                            Type = DiffType.Removed,
                            LeftText = left[li],
                            LeftLine = leftLine++
                        });
                        break;
                }
            }

            MarkChangedLines(result);
            return result;
        }

        private static void MarkChangedLines(List<DiffLine> diff)
        {
            int i = 0;
            while (i < diff.Count)
            {
                if (diff[i].Type != DiffType.Removed)
                {
                    i++;
                    continue;
                }

                int removeRunStart = i;
                int removeRunEnd = i;
                while (removeRunEnd + 1 < diff.Count && diff[removeRunEnd + 1].Type == DiffType.Removed)
                    removeRunEnd++;

                int addRunStart = removeRunEnd + 1;
                int addRunEnd = addRunStart;
                if (addRunStart < diff.Count && diff[addRunStart].Type == DiffType.Added)
                {
                    while (addRunEnd + 1 < diff.Count && diff[addRunEnd + 1].Type == DiffType.Added)
                        addRunEnd++;

                    int removeCount = removeRunEnd - removeRunStart + 1;
                    int addCount = addRunEnd - addRunStart + 1;
                    int pairCount = Math.Min(removeCount, addCount);

                    var merged = new List<DiffLine>();
                    for (int p = 0; p < pairCount; p++)
                    {
                        merged.Add(new DiffLine
                        {
                            Type = DiffType.Changed,
                            LeftText = diff[removeRunStart + p].LeftText,
                            LeftLine = diff[removeRunStart + p].LeftLine,
                            RightText = diff[addRunStart + p].RightText,
                            RightLine = diff[addRunStart + p].RightLine
                        });
                    }

                    for (int p = pairCount; p < removeCount; p++)
                    {
                        merged.Add(new DiffLine
                        {
                            Type = DiffType.Removed,
                            LeftText = diff[removeRunStart + p].LeftText,
                            LeftLine = diff[removeRunStart + p].LeftLine
                        });
                    }

                    for (int p = pairCount; p < addCount; p++)
                    {
                        merged.Add(new DiffLine
                        {
                            Type = DiffType.Added,
                            RightText = diff[addRunStart + p].RightText,
                            RightLine = diff[addRunStart + p].RightLine
                        });
                    }

                    diff.RemoveRange(removeRunStart, addRunEnd - removeRunStart + 1);
                    diff.InsertRange(removeRunStart, merged);
                    i = removeRunStart + merged.Count;
                }
                else
                {
                    i = removeRunEnd + 1;
                }
            }
        }

        private static int[,] ComputeLCS(string[] left, string[] right)
        {
            int m = left.Length, n = right.Length;
            var dp = new int[m + 1, n + 1];

            for (int ii = 1; ii <= m; ii++)
            {
                for (int jj = 1; jj <= n; jj++)
                {
                    if (left[ii - 1] == right[jj - 1])
                        dp[ii, jj] = dp[ii - 1, jj - 1] + 1;
                    else
                        dp[ii, jj] = Math.Max(dp[ii - 1, jj], dp[ii, jj - 1]);
                }
            }

            return dp;
        }
    }
}