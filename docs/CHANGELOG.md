# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **FB/function call argument wrapping.** Calls with many named (`:=`) arguments now
  format one argument per line, aligned under the first argument:
  `fbTest(a := 233,` ⏎ `       b := 'dfd', …);`. Threshold configurable via
  `st_wrap_call_arguments_at` (default 4 named arguments; 0 disables). Calls containing
  comments between arguments stay inline so comments keep their place.
- **Content-preservation gate in `stfmt`.** `format`/`batch` now verify the formatted
  output contains exactly the input's non-whitespace content and refuse to write
  otherwise — parser-recovery truncation can no longer silently lose code.

### Fixed

- **Members named after keywords parse correctly.** `stFb.Test.Var.x` (member `Var`)
  used to break the parse and silently truncate output; keywords are now accepted as
  member names after `.` (also in dotted type names and USING directives).

- **Trailing `;` after `END_IF`/`END_WHILE`/`END_FOR`/`END_CASE` is preserved.** The
  formatter silently dropped it (the ';' parses as an empty statement after the block),
  churning nearly every file in an open-source corpus scan (TcUnit, TcOpen, struckig,
  TcBlack — 208 files). It now stays glued to the END keyword; formatting remains
  idempotent.
- **UTF-8 BOM preserved by `stfmt`.** TwinCAT writes `.TcPOU/.TcDUT/.TcGVL` with a BOM;
  `stfmt format`/`batch` re-wrote them BOM-less, differing from TcXaeShell's own output.
  Files are now written back with their original preamble (UTF-8 BOM / UTF-16 LE).

- **DUT name stays on the `TYPE` line.** The formatter moved the name to the next line
  (`TYPE` ⏎ `U_Sample : UNION`), which read as the UNION/STRUCT name being deleted. The
  header is now TwinCAT-conventional (`TYPE U_Sample :`), composite bodies start on the
  next line, alias types stay inline.
- **Format Document / Git work on method tabs.** TcXaeShell reports method/action editor
  tabs as `<file>.TcPOU;POU.Member`; the pseudo-path made Format Document fail with
  "File not found" and broke git repo resolution. The suffix is now stripped everywhere.
- Host log stamps include the date, and the startup line logs the product version —
  multi-day log forensics no longer have to guess which build produced a session.

## [1.1.0] - 2026-07-07

### Added

- **Compare-tool accept workflow in the Git diff viewer.** Left pane = HEAD (source),
  right pane = working file, with labelled column headers. Accepting is **line-granular
  and staged**: the ▶ gutter arrow / context menu / toolbar stage a **blue preview** of
  HEAD's line (nothing is written yet); **Save to working file** then writes all staged
  accepts **per change-block straight to the `.TcPOU/.TcGVL` on disk** (deterministic —
  no editor/tab guessing) and brings TcXaeShell to the front so its native reload prompt
  appears. A one-level **Undo** reverts the last save from a file snapshot. Clear-staged
  drops previews. Accepted-added lines are struck through in the preview and removed on
  save.
- **Diff viewer UI overhaul**: compact glyph toolbar with tooltips (nothing clips),
  semantic icon colors (green accept / red clear / blue save / amber undo), light/dark
  **theme toggle**, bottom color-key legend with live swatches, and a distinct blue
  "staged" color that can't be confused with added-green.
- **Deploy verification.** `deploy.ps1` (with `deploy.bat` as wrapper) stops the Host,
  copies, **verifies every file** (timestamp + length, exits 1 on any stale file), and
  restarts the Host — no more silent stale deploys when the Host held file locks.
- Repo-wide `.editorconfig` + `.gitattributes` pin UTF-8 for sources (several contain
  Unicode glyph literals, now written as `\uXXXX` escapes).
- **Git tools (TwinCAT-aware), kept separate from the formatter.** A new `STBud.Git`
  engine (shells out to `git.exe`, so it works on all target frameworks) plus a standalone
  **`stgit`** CLI (now multi-targeted net48/net8.0 and shipped in the install) — `stfmt`
  and the formatter engine are untouched.
  - **Git tab** in the tray UI: initialize/open a local repo, view status, stage/unstage,
    commit, and create/switch local branches; browse commits and their changed files;
    see **change hotspots** (most-churned POUs); and a **Current File** view with the
    active POU's history.
  - **Context menu** in TcXaeShell (under STBud → Git): **File History…**, **Compare with
    HEAD…**, **Commit…** for the active POU.
  - **ST-level diffs**: `.TcPOU/.TcDUT/.TcGVL` are compared at the Structured-Text level
    (inside CDATA), not as raw XML, via a read-only `TwinCatStExtractor` in the core.
    Declaration and Implementation are diffed **separately** as two tagged blocks in the
    same window, so restore knows which editor tab a line belongs to.
  - **Section-aware restore**: select committed lines in the diff and push them back into
    the open editor through the clipboard live-edit pipeline. The active editor tab is
    detected (`LooksLikeDeclaration`) and the restore is **refused with a warning** when
    the active tab doesn't match the line's section — preventing a declaration line being
    pasted into the implementation tab (and vice versa). The originating TcXaeShell
    instance (PID) is threaded through so restore lands in the right editor when more
    than one is open.
  - `stgit` commands: `init`, `status`, `log`, `history`, `blame [--raw]`, `diff <rev> <file>`,
    `churn`, `stage <file>...`, `unstage <file>...`, `commit -m <msg>`, `branch [name]`,
    `checkout <branch>`, `restore <rev> <file>`. `stgit blame` on a `.TcPOU` now extracts
    the ST from CDATA and attributes the ST lines (use `--raw` for the old XML-level output).

### Fixed

- **Unified diff engine.** The tray DiffViewer and `stgit` now share a single `LineDiff`
  implementation in `STBud.Git/Diff/LineDiff.cs`. Previously the viewer had its own private
  (divergent) LCS; `stgit diff` and the tray viewer could disagree on the same commit.
- **Word-level intra-line highlight.** The diff viewer now tokenizes ST lines on natural
  boundaries (identifiers, numbers, strings, operator runs) and runs a token-level LCS to
  highlight the changed sub-spans per token — the old prefix/suffix approach lit up one big
  middle block and couldn't tell that `a` and `b` swapped in `IF a AND b THEN`.
- **Custom-drawn diff surface.** Replaced the RichTextBox-based rendering with a custom
  `DiffCanvas`/`DiffPane`: full-width row bands (the RichTextBox `SelectionBackColor` only
  painted character cells, leaving ragged bands), a real gutter overlay separate from the
  text (so Copy yields clean ST without line-number noise), horizontal + vertical scroll
  sync, dark-mode color scheme with system-theme detection, and HiDPI-aware layout.
- **No more silent truncation.** The diff is now computed over the full input — the old
  `MaxDiffLines=4000` cap could silently report "No changes" when real changes were beyond
  line 4000. A visible `— large diff (N rows)` banner appears for huge diffs; the canvas
  renders only visible rows so the UI stays responsive regardless of diff size.
- **`ScanCData` misclassification.** The malformed-XML fallback in `TwinCatStExtractor`
  now finds the real enclosing element via a backward depth-tracking scan (the old
  `LastIndexOf('<', pos)` found the `<` of `<![CDATA[` itself, classifying every CDATA
  block as Implementation). Element-name classification is now exact (`== "Declaration"`),
  not substring. A previously-passing-but-weak test that masked the bug now asserts
  `Declaration != null` for the malformed fixture.
- **Non-blocking Compare-with-HEAD.** `HandleGitCompareHead` now uses `BeginInvoke` and
  passes the Host main form as the dialog owner — the TcXaeShell editor is no longer frozen
  for the lifetime of the diff dialog, and the dialog no longer renders ownerless.
- **GitPanel UX.** The Status tab now reflects staged state in the row checkboxes after
  refresh (so you can see at a glance what's staged), adds **Stage All** / **Unstage All**
  buttons, and **Open Folder** no longer discards the currently-open file or runs a dead
  double `FindRepoRoot`.
- Git tools now resolve to the **main project (solution) repo** instead of stopping at a
  stray nested `.git` under the project tree (and warn once when one shadows the repo).
- **Cleanup Stale** no longer throws — the instance sweep iterates a snapshot rather than the
  live dictionary it mutates, and the toolbar actions are guarded.
- Deployed Host now ships `STFormatter.Host.exe.config` (binding redirects), fixing the
  `System.Text.Json` initialization failure that prevented settings from saving. The
  installer payload also now includes `STBud.Git.dll`, `stgit.exe`, and the config.

## [1.0.0] - 2026-06-23

### Added

- **TwinCAT 3 Build 4026 support**, verified on a live install: the Host connects to and
  injects its menu into the 4024 TcXaeShell (DTE 15.0), the 4026 TcXaeShell (DTE 17.0, via
  the dynamic moniker fallback), and **TwinCAT loaded inside Visual Studio 2022** (`devenv`,
  detected by the `PlcCodeWinContextMenu` command bar rather than the DTE name). The 64-bit
  `TcXaeShell64` process is recognized.
- `stfmt doctor` — environment diagnostics: TwinCAT install + build, install model (TcPkg
  vs classic), running shells with their live ROT monikers (classified
  SUPPORTED/fallback/unknown), the deployed Host, and a local ADS check. `--save` writes
  the report for diffing across upgrades.
- `stfmt ping <host>` and a tray-UI **Toolbox** tab — TwinCAT machine pinger (ICMP + ADS
  ports 48898/8016) with persisted recent targets, plus the diagnostics report.
- `WrapLongLines` formatting option (`st_wrap_long_lines`) — master switch for long-line
  wrapping; a "Wrap long lines" checkbox in Settings.
- I/O Linking browser rebuilt: live filter, direction-aware coloring, attribute preview,
  and **TIID / TIIB** link-style selection (terminal-relative, rename-safe links).
- Pragma showcase fixtures and exact-preservation tests covering every documented pragma
  family in every structural position.

### Changed

- Context menu reorganized to surface common actions: **Format Document**, **Format
  Selection**, and **I/O Linking…** are now top-level items.

### Fixed

- Formatter correctness, found by token-preservation gates over ~1,400 real TwinCAT files
  (TcUnit/TcOpen/struckig) plus the regression corpus: time/date literals in expressions
  and argument lists, bracket array initializers, paren-form enums, `STRING(n)`, `REF_TO`,
  namespace-qualified names, pointer dereference (`^`), post-keyword modifiers, soft
  keywords used as identifiers, and pragmas containing `}` inside quoted values — all
  previously dropped or truncated.
- `.editorconfig`: honor top-level `root = true`, and apply later sections over earlier ones.
- Host live-edit robustness: write via `TextSelection.Insert` instead of spraying SendKeys
  at whatever window has focus; own all dialogs to the editor window (no focus theft);
  wait for modifier-key release; detect clipboard-set failure.

### Removed

- Removed legacy Visual Studio VSIX and in-process TcXaeShell VSPackage projects.
- Removed stale VSIX build and TcXaeShell registration scripts.
- Removed VSIX packaging documentation; TcXaeShell integration is Host-only.

## [0.5.0] - 2026-05-21

> **Note**: The Automation API live-edit approach described below was later replaced
> by the external Host process with clipboard-based DTE commands. The in-process
> Automation API does not work from an external COM process (`DISP_E_UNKNOWNNAME`).

### Added

- TcXaeShell extension: live editor update via TwinCAT Automation API (in-process only)
- `IPLCData` -> `PlcFileNode` -> `TcPouItemAdapter` pipeline for in-memory formatting
- Direct write-back to `DeclarationText` and `ImplementationText` properties
- Seamless format-on-save experience inside TcXaeShell without file round-tripping

## [0.4.0] - 2026-05-14

> **Note**: The VSIX/CDATA-replacement approach was later replaced by the external
> Host process with clipboard-based DTE live edit. The VSIX project has been deleted.

### Added

- TcXaeShell extension (VSIX) with file-only CDATA replacement approach
- Formatting of `.TcPOU`, `.TcDUT`, and `.TcGVL` files via CDATA section replacement
- No dependency on Automation API; safe for environments where the API is unavailable

## [0.3.0] - 2026-05-07

> **Note**: The VS 2022 VSIX extension was later removed. TcXaeShell does not load
> custom VSPackages or MEF components. Production integration is the external Host.

### Added

- Visual Studio 2022 extension (VSIX)
- Format Document command
- Format Selection command
- Format on Save auto-formatting
- Options page for configuring formatting rules

## [0.2.0] - 2026-04-30

### Added

- CLI tool with the following commands:
  - `format` — format one or more files in place
  - `check` — verify formatting without making changes
  - `batch` — format an entire directory tree
  - `init` — scaffold a configuration file
  - `preset` — apply a built-in style preset
  - `export` — export current settings
  - `import` — import settings from a file

## [0.1.0] - 2026-04-23

### Added

- Initial project setup
- Core formatting engine
- Lexer for structured text tokenisation
- Parser producing an abstract syntax tree
- Syntax tree model for round-trip-aware formatting
- Multi-targeting: `net8.0`, `net48`, `net462`
- 57 unit tests passing

[Unreleased]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v0.5.0...v1.0.0
[0.5.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/muratbilge/STBud-for-TwinCAT/releases/tag/v0.1.0