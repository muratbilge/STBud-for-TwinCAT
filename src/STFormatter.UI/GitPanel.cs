using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using STBud.Git;
using STBud.Git.Diff;
using STFormatter.Core.Formatting;

namespace STFormatter.UI
{
    /// <summary>
    /// The Git tab: init/manage a local repo, browse history for the active POU,
    /// view commits and their files, see change hotspots, and (for the active file)
    /// diff a committed version against the working tree with line-level restore.
    /// Diffs of TwinCAT XML files are taken at the Structured-Text level.
    /// </summary>
    public sealed class GitPanel : UserControl
    {
        private string? _repoRoot;
        private string? _repoRootHint;
        private string? _currentFilePath;
        private string? _currentRelPath;
        private bool _loaded;

        private Label _repoLabel = null!;
        private ComboBox _branchCombo = null!;
        private TabControl _subTabs = null!;
        private ListView _fileHistoryList = null!;
        private ListView _commitsList = null!;
        private ListView _commitFilesList = null!;
        private ListView _statusList = null!;
        private ListView _churnList = null!;
        private TextBox _commitMessage = null!;
        private Label _noRepoLabel = null!;

        public GitPanel()
        {
            BuildUI();
        }

        public void RebuildUi() => BuildUI();

        private void BuildUI()
        {
            SuspendLayout();
            Controls.Clear();
            Dock = DockStyle.Fill;

            _subTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f) };
            _subTabs.TabPages.Add(BuildCurrentFileTab());
            _subTabs.TabPages.Add(BuildCommitsTab());
            _subTabs.TabPages.Add(BuildStatusTab());
            _subTabs.TabPages.Add(BuildHotspotsTab());

            _noRepoLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11f, FontStyle.Italic),
                ForeColor = SystemColors.ControlDarkDark,
                Text = Strings.Get("Git.NoRepo"),
                Visible = false,
            };

            Controls.Add(_subTabs);
            Controls.Add(_noRepoLabel);
            Controls.Add(BuildRepoHeader());

            ResumeLayout();
            UpdateRepoVisibility();
        }

        private Control BuildRepoHeader()
        {
            var group = new GroupBox
            {
                Text = Strings.Get("Git.Group.Repo"),
                Dock = DockStyle.Top,
                Height = 78,
                Padding = new Padding(8, 4, 8, 4),
            };

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
            };

            _repoLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(2, 8, 12, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Text = "",
            };

            var branchLabel = new Label { AutoSize = true, Text = Strings.Get("Git.Branch"), Margin = new Padding(2, 8, 2, 0) };
            _branchCombo = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 4, 8, 0) };

            var switchBtn = NewButton("Git.Switch", OnSwitchBranch);
            var newBranchBtn = NewButton("Git.NewBranch", OnNewBranch);
            var refreshBtn = NewButton("Git.Refresh", (_, __) => RefreshAll());
            var openBtn = NewButton("Git.OpenFolder", OnOpenFolder);
            var initBtn = NewButton("Git.Init", OnInitRepo);

            row.Controls.Add(_repoLabel);
            row.Controls.Add(branchLabel);
            row.Controls.Add(_branchCombo);
            row.Controls.Add(switchBtn);
            row.Controls.Add(newBranchBtn);
            row.Controls.Add(refreshBtn);
            row.Controls.Add(openBtn);
            row.Controls.Add(initBtn);

            group.Controls.Add(row);
            return group;
        }

        private TabPage BuildCurrentFileTab()
        {
            var tab = new TabPage(Strings.Get("Git.SubTab.CurrentFile")) { Padding = new Padding(4) };
            _fileHistoryList = NewCommitListView();
            _fileHistoryList.DoubleClick += OnFileHistoryDoubleClick;

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = SystemColors.ControlDarkDark,
                Text = Strings.Get("Git.Hint.CurrentFile"),
            };

            tab.Controls.Add(_fileHistoryList);
            tab.Controls.Add(hint);
            return tab;
        }

        private TabPage BuildCommitsTab()
        {
            var tab = new TabPage(Strings.Get("Git.SubTab.Commits")) { Padding = new Padding(4) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

            _commitsList = NewCommitListView();
            _commitsList.SelectedIndexChanged += OnCommitSelected;

            _commitFilesList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
            };
            _commitFilesList.Columns.Add(Strings.Get("Git.Col.Change"), 80);
            _commitFilesList.Columns.Add(Strings.Get("Git.Col.Path"), 360);
            _commitFilesList.DoubleClick += OnCommitFileDoubleClick;

            split.Panel1.Controls.Add(_commitsList);
            split.Panel2.Controls.Add(_commitFilesList);

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = SystemColors.ControlDarkDark,
                Text = Strings.Get("Git.Hint.Commits"),
            };

            tab.Controls.Add(split);
            tab.Controls.Add(hint);
            split.HandleCreated += (s, e) =>
            {
                try { split.SplitterDistance = Math.Max(120, split.Width / 2); } catch { }
            };
            return tab;
        }

        private TabPage BuildStatusTab()
        {
            var tab = new TabPage(Strings.Get("Git.SubTab.Status")) { Padding = new Padding(4) };

            _statusList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
            };
            _statusList.Columns.Add(Strings.Get("Git.Col.State"), 140);
            _statusList.Columns.Add(Strings.Get("Git.Col.Path"), 380);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96 };

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
            btnRow.Controls.Add(NewButton("Git.Stage", (_, __) => StageChecked(stage: true)));
            btnRow.Controls.Add(NewButton("Git.Unstage", (_, __) => StageChecked(stage: false)));
            btnRow.Controls.Add(NewButton("Git.StageAll", (_, __) => StageAll(stage: true)));
            btnRow.Controls.Add(NewButton("Git.UnstageAll", (_, __) => StageAll(stage: false)));

            var msgLabel = new Label { Dock = DockStyle.Top, Height = 18, Text = Strings.Get("Git.CommitMessage") };
            _commitMessage = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 44 };
            var commitBtn = NewButton("Git.Commit", OnCommit);
            commitBtn.Dock = DockStyle.Right;

            var msgRow = new Panel { Dock = DockStyle.Fill };
            msgRow.Controls.Add(_commitMessage);
            msgRow.Controls.Add(commitBtn);

            bottom.Controls.Add(msgRow);
            bottom.Controls.Add(msgLabel);
            bottom.Controls.Add(btnRow);

            tab.Controls.Add(_statusList);
            tab.Controls.Add(bottom);
            return tab;
        }

        private TabPage BuildHotspotsTab()
        {
            var tab = new TabPage(Strings.Get("Git.SubTab.Hotspots")) { Padding = new Padding(4) };
            _churnList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
            };
            _churnList.Columns.Add(Strings.Get("Git.Col.Changes"), 90);
            _churnList.Columns.Add(Strings.Get("Git.Col.Path"), 380);
            tab.Controls.Add(_churnList);
            return tab;
        }

        private ListView NewCommitListView()
        {
            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
            };
            lv.Columns.Add(Strings.Get("Git.Col.Sha"), 80);
            lv.Columns.Add(Strings.Get("Git.Col.Date"), 150);
            lv.Columns.Add(Strings.Get("Git.Col.Author"), 120);
            lv.Columns.Add(Strings.Get("Git.Col.Subject"), 360);
            return lv;
        }

        private Button NewButton(string key, EventHandler onClick)
        {
            var b = new Button
            {
                Text = Strings.Get(key),
                AutoSize = true,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(2, 4, 2, 0),
            };
            b.Click += onClick;
            return b;
        }

        // ---- loading ------------------------------------------------------------------

        /// <summary>Load the panel against a specific file (called from the Host context menu).</summary>
        public void LoadForFile(string? filePath, int subTab = 0, string? repoRootHint = null)
        {
            _currentFilePath = string.IsNullOrEmpty(filePath) ? null : filePath;
            _repoRootHint = string.IsNullOrEmpty(repoRootHint) ? null : repoRootHint;
            _loaded = true;
            RefreshAll();
            if (_subTabs.TabPages.Count > 0 && subTab >= 0 && subTab < _subTabs.TabPages.Count)
                _subTabs.SelectedIndex = subTab;
        }

        /// <summary>Load lazily the first time the tab is shown, using the active editor file.</summary>
        public void EnsureLoaded()
        {
            if (_loaded) return;
            string? active = GitEditorBridge.GetActiveFilePath?.Invoke();
            LoadForFile(active);
        }

        /// <summary>
        /// Resolve the repo root from the best available anchor: an explicit hint from
        /// the Host, then the active file's folder, then the solution folder (where
        /// `git init` is typically run). Walking up from a POU file alone can miss the
        /// repo when the POU lives in a subtree below the solution.
        /// </summary>
        private string? ResolveRepoRoot()
        {
            if (!string.IsNullOrEmpty(_repoRootHint)) return _repoRootHint;

            // Solution before file: the solution's repo is the canonical project repo;
            // walking up from a POU can stop at a stray nested .git below the project.
            string? solDir = GitEditorBridge.GetActiveSolutionDir?.Invoke();
            if (!string.IsNullOrEmpty(solDir))
            {
                string? fromSln = GitClient.FindRepoRoot(solDir);
                if (fromSln != null) return fromSln;
            }

            if (_currentFilePath != null)
            {
                string? fromFile = GitClient.FindRepoRoot(_currentFilePath);
                if (fromFile != null) return fromFile;
            }
            return null;
        }

        private void RefreshAll()
        {
            if (!GitClient.IsGitAvailable(out _))
            {
                _repoRoot = null;
                _repoLabel.Text = Strings.Get("Git.GitMissing");
                UpdateRepoVisibility();
                return;
            }

            // Resolve the repo from the best anchor (file → solution dir → hint).
            _repoRoot = ResolveRepoRoot();
            _currentRelPath = (_repoRoot != null && _currentFilePath != null)
                ? GitClient.RelativePath(_repoRoot, _currentFilePath)
                : null;

            UpdateRepoVisibility();
            if (_repoRoot == null) return;

            Cursor.Current = Cursors.WaitCursor;
            try
            {
                _repoLabel.Text = _repoRoot;
                LoadBranches();
                LoadCurrentFileHistory();
                LoadCommits();
                LoadStatus();
                LoadChurn();
            }
            catch (Exception ex)
            {
                _repoLabel.Text = ex.Message;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void UpdateRepoVisibility()
        {
            bool hasRepo = _repoRoot != null;
            if (_subTabs != null) _subTabs.Visible = hasRepo;
            if (_noRepoLabel != null) _noRepoLabel.Visible = !hasRepo;
            if (!hasRepo && _repoLabel != null && string.IsNullOrEmpty(_repoLabel.Text))
                _repoLabel.Text = Strings.Get("Git.NoRepo");
        }

        private void LoadBranches()
        {
            _branchCombo.Items.Clear();
            string current = GitClient.CurrentBranch(_repoRoot!);
            foreach (var b in GitClient.Branches(_repoRoot!))
                _branchCombo.Items.Add(b.Name);
            if (!string.IsNullOrEmpty(current))
            {
                int idx = _branchCombo.Items.IndexOf(current);
                if (idx >= 0) _branchCombo.SelectedIndex = idx;
            }
        }

        private void LoadCurrentFileHistory()
        {
            _fileHistoryList.Items.Clear();
            if (_currentRelPath == null)
            {
                _fileHistoryList.Items.Add(new ListViewItem(new[] { "", "", "", "(open a file in the editor)" }));
                return;
            }
            foreach (var c in GitClient.Log(_repoRoot!, _currentRelPath))
                _fileHistoryList.Items.Add(CommitRow(c));
        }

        private void LoadCommits()
        {
            _commitsList.Items.Clear();
            _commitFilesList.Items.Clear();
            foreach (var c in GitClient.Log(_repoRoot!, null))
                _commitsList.Items.Add(CommitRow(c));
        }

        private void LoadStatus()
        {
            _statusList.Items.Clear();
            foreach (var e in GitClient.Status(_repoRoot!))
            {
                // Check the row when the file is already staged so the checkbox column
                // reflects current state — the user can see at a glance what's staged.
                var item = new ListViewItem(new[] { e.StateLabel, e.Path }) { Tag = e };
                item.Checked = e.IsStaged;
                _statusList.Items.Add(item);
            }
        }

        private void LoadChurn()
        {
            _churnList.Items.Clear();
            foreach (var c in GitClient.Churn(_repoRoot!))
                _churnList.Items.Add(new ListViewItem(new[] { c.Changes.ToString(), c.Path }));
        }

        private static ListViewItem CommitRow(CommitInfo c)
        {
            string date = c.Date?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? c.DateIso;
            return new ListViewItem(new[] { c.ShortSha, date, c.Author, c.Subject }) { Tag = c };
        }

        // ---- actions ------------------------------------------------------------------

        private void OnCommitSelected(object? sender, EventArgs e)
        {
            _commitFilesList.Items.Clear();
            if (_repoRoot == null || _commitsList.SelectedItems.Count == 0) return;
            if (_commitsList.SelectedItems[0].Tag is not CommitInfo c) return;

            foreach (var f in GitClient.CommitFiles(_repoRoot, c.Sha))
                _commitFilesList.Items.Add(new ListViewItem(new[] { f.KindLabel, f.Path }) { Tag = f });
        }

        private void OnFileHistoryDoubleClick(object? sender, EventArgs e)
        {
            if (_repoRoot == null || _currentRelPath == null || _currentFilePath == null) return;
            if (_fileHistoryList.SelectedItems.Count == 0) return;
            if (_fileHistoryList.SelectedItems[0].Tag is not CommitInfo c) return;

            var committedSections = TwinCatStExtractor.Extract(
                GitClient.ShowFile(_repoRoot, c.Sha, _currentRelPath));
            var workingSections = File.Exists(_currentFilePath)
                ? TwinCatStExtractor.Extract(File.ReadAllText(_currentFilePath))
                : new TwinCatStExtractor.StSections();

            string title = $"{Path.GetFileName(_currentRelPath)} @ {c.ShortSha} ↔ {Strings.Get("Git.Diff.Working")}";
            // Section-aware diff when the file is TwinCAT XML; pid=0 lets the Host fall
            // back to FindActiveInstance (the tray panel doesn't track the originating pid).
            if (!committedSections.IsEmpty || !workingSections.IsEmpty)
            {
                using var diff = new DiffViewerForm(
                    title, committedSections, workingSections,
                    GitEditorBridge.RestoreToEditor,
                    Strings.Get("Git.Diff.Committed"),
                    Strings.Get("Git.Diff.Working"),
                    pid: 0,
                    workingFilePath: _currentFilePath);
                diff.ShowDialog(FindForm());
            }
            else
            {
                using var diff = new DiffViewerForm(
                    title,
                    TwinCatStExtractor.ExtractCombinedOrRaw(GitClient.ShowFile(_repoRoot, c.Sha, _currentRelPath)),
                    File.Exists(_currentFilePath) ? File.ReadAllText(_currentFilePath) : "",
                    GitEditorBridge.RestoreToEditor,
                    Strings.Get("Git.Diff.Committed"),
                    Strings.Get("Git.Diff.Working"),
                    pid: 0,
                    workingFilePath: _currentFilePath);
                diff.ShowDialog(FindForm());
            }
        }

        private void OnCommitFileDoubleClick(object? sender, EventArgs e)
        {
            if (_repoRoot == null || _commitsList.SelectedItems.Count == 0) return;
            if (_commitsList.SelectedItems[0].Tag is not CommitInfo c) return;
            if (_commitFilesList.SelectedItems.Count == 0) return;
            if (_commitFilesList.SelectedItems[0].Tag is not FileChange f) return;

            // Commit-vs-commit diff is read-only (no restore), so the plain combined
            // view is fine — no section tag needed.
            string newSt = TwinCatStExtractor.ExtractCombinedOrRaw(GitClient.ShowFile(_repoRoot, c.Sha, f.Path));
            string oldSt = TwinCatStExtractor.ExtractCombinedOrRaw(GitClient.ShowFile(_repoRoot, c.Sha + "^", f.Path));

            string title = $"{Path.GetFileName(f.Path)} @ {c.ShortSha}";
            using var diff = new DiffViewerForm(title, oldSt, newSt);
            diff.ShowDialog(FindForm());
        }

        private void StageChecked(bool stage)
        {
            if (_repoRoot == null) return;
            var paths = _statusList.CheckedItems.Cast<ListViewItem>()
                .Select(i => (i.Tag as GitStatusEntry)?.Path)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) return;

            var r = stage ? GitClient.Stage(_repoRoot, paths) : GitClient.Unstage(_repoRoot, paths);
            if (!r.Success) ShowError(r.ErrorMessage);
            LoadStatus();
        }

        // Stage/unstage every file in the status list in one git call — convenient
        // for "stage all" before a commit, mirroring what `git add -A` does.
        private void StageAll(bool stage)
        {
            if (_repoRoot == null) return;
            var paths = _statusList.Items.Cast<ListViewItem>()
                .Select(i => (i.Tag as GitStatusEntry)?.Path)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) return;

            var r = stage ? GitClient.Stage(_repoRoot, paths) : GitClient.Unstage(_repoRoot, paths);
            if (!r.Success) ShowError(r.ErrorMessage);
            LoadStatus();
        }

        private void OnCommit(object? sender, EventArgs e)
        {
            if (_repoRoot == null) return;
            string msg = _commitMessage.Text.Trim();
            bool anyStaged = _statusList.Items.Cast<ListViewItem>()
                .Any(i => (i.Tag as GitStatusEntry)?.IsStaged == true);
            if (string.IsNullOrEmpty(msg) || !anyStaged)
            {
                ShowInfo(Strings.Get("Git.CommitEmpty"));
                return;
            }

            var r = GitClient.Commit(_repoRoot, msg);
            if (!r.Success) { ShowError(r.ErrorMessage); return; }
            _commitMessage.Clear();
            RefreshAll();
        }

        private void OnSwitchBranch(object? sender, EventArgs e)
        {
            if (_repoRoot == null || _branchCombo.SelectedItem == null) return;
            string name = _branchCombo.SelectedItem.ToString()!;
            var r = GitClient.Checkout(_repoRoot, name);
            if (!r.Success) { ShowError(r.ErrorMessage); return; }
            RefreshAll();
        }

        private void OnNewBranch(object? sender, EventArgs e)
        {
            if (_repoRoot == null) return;
            using var dlg = new InputDialog(Strings.Get("Git.NewBranch.Title"), Strings.Get("Git.NewBranch.Prompt"));
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            string name = dlg.InputText.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var r = GitClient.CreateBranch(_repoRoot, name, checkout: true);
            if (!r.Success) { ShowError(r.ErrorMessage); return; }
            RefreshAll();
        }

        private void OnOpenFolder(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

            // Re-anchor the repo to the chosen folder without throwing away the
            // currently-open file — the Current File tab stays useful. If the folder
            // isn't itself a repo (and isn't inside one), the tabs stay hidden until
            // the user runs Init on it.
            string? found = GitClient.FindRepoRoot(dlg.SelectedPath);
            _repoRoot = found;
            _loaded = true;
            RefreshAll();
        }

        private void OnInitRepo(object? sender, EventArgs e)
        {
            string? dir = _currentFilePath != null ? Path.GetDirectoryName(_currentFilePath) : null;
            if (dir == null)
            {
                using var dlg = new FolderBrowserDialog();
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                dir = dlg.SelectedPath;
            }

            var r = GitClient.Init(dir);
            if (!r.Success) { ShowError(r.ErrorMessage); return; }
            _repoRoot = GitClient.FindRepoRoot(dir);
            _loaded = true;
            RefreshAll();
        }

        private void ShowError(string message) =>
            MessageBox.Show(FindForm(), message, Strings.Get("App.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private void ShowInfo(string message) =>
            MessageBox.Show(FindForm(), message, Strings.Get("App.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
