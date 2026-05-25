using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace STFormatter.UI
{
    public class DiffViewerForm : Form
    {
        private readonly RichTextBox _leftBox;
        private readonly RichTextBox _rightBox;
        private readonly Label _titleLabel;

        public DiffViewerForm(string title, string originalText, string formattedText)
        {
            Text = "ST Formatter - Diff";
            Size = new Size(1000, 600);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            _titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 0, 0)
            };

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 480,
                SplitterWidth = 6,
                BackColor = SystemColors.ControlDark
            };

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var leftLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "Original",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 0, 0),
                BackColor = Color.FromArgb(255, 230, 230)
            };
            _leftBox = CreateRichTextBox();
            leftPanel.Controls.Add(_leftBox);
            leftPanel.Controls.Add(leftLabel);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var rightLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "Formatted",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 0, 0),
                BackColor = Color.FromArgb(230, 255, 230)
            };
            _rightBox = CreateRichTextBox();
            rightPanel.Controls.Add(_rightBox);
            rightPanel.Controls.Add(rightLabel);

            splitContainer.Panel1.Controls.Add(leftPanel);
            splitContainer.Panel2.Controls.Add(rightPanel);

            Controls.Add(splitContainer);
            Controls.Add(_titleLabel);

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

        private void LoadDiff(string original, string formatted)
        {
            string[] leftLines = SplitLines(original);
            string[] rightLines = SplitLines(formatted);

            var diff = ComputeDiff(leftLines, rightLines);

            RenderSide(_leftBox, diff, Side.Left);
            RenderSide(_rightBox, diff, Side.Right);
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
                bool show = line.Type switch
                {
                    DiffType.Unchanged => true,
                    DiffType.Added => side == Side.Right,
                    DiffType.Removed => side == Side.Left,
                    DiffType.Changed => true,
                    _ => true
                };

                if (!show) continue;

                Color foreColor = line.Type switch
                {
                    DiffType.Unchanged => Color.Black,
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
                box.SelectionColor = Color.DarkGray;
                box.SelectedText = $"{lineNum,4}  ";

                box.Select(box.TextLength, 0);
                box.SelectionBackColor = backColor;
                box.SelectionColor = foreColor;
                box.SelectedText = text + "\n";
            }

            box.Select(0, 0);
        }

        private enum Side { Left, Right }

        private enum DiffType { Unchanged, Added, Removed, Changed }

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
            for (int i = 0; i < diff.Count; i++)
            {
                if (diff[i].Type != DiffType.Removed) continue;

                int nextNonRemoved = -1;
                for (int k = i + 1; k < diff.Count; k++)
                {
                    if (diff[k].Type != DiffType.Added)
                    {
                        nextNonRemoved = k;
                        break;
                    }
                }

                int removeRunEnd = i;
                for (int k = i + 1; k < diff.Count && diff[k].Type == DiffType.Removed; k++)
                    removeRunEnd = k;

                int addStart = removeRunEnd + 1;
                int addEnd = addStart;
                for (int k = addStart; k < diff.Count && diff[k].Type == DiffType.Added; k++)
                    addEnd = k;

                if (addStart < diff.Count && diff[addStart].Type == DiffType.Added)
                {
                    int removeCount = removeRunEnd - i + 1;
                    int addCount = addEnd - addStart + 1;
                    int pairCount = Math.Min(removeCount, addCount);

                    for (int p = 0; p < pairCount; p++)
                    {
                        diff[i + p].Type = DiffType.Changed;
                        diff[i + p].RightText = diff[addStart + p].RightText;
                        diff[i + p].RightLine = diff[addStart + p].RightLine;
                        diff[addStart + p].Type = DiffType.Changed;
                        diff[addStart + p].LeftText = diff[i + p].LeftText;
                        diff[addStart + p].LeftLine = diff[i + p].LeftLine;
                    }

                    for (int p = pairCount; p < removeCount; p++)
                    {
                        diff[i + p].Type = DiffType.Removed;
                    }

                    for (int p = pairCount; p < addCount; p++)
                    {
                        diff[addStart + p].Type = DiffType.Added;
                    }

                    i = addEnd;
                }
                else
                {
                    i = removeRunEnd;
                }
            }
        }

        private static int[,] ComputeLCS(string[] left, string[] right)
        {
            int m = left.Length, n = right.Length;
            var dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (left[i - 1] == right[j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            return dp;
        }
    }
}