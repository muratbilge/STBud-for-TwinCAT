# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[0.5.0]: https://github.com/anomalyco/CodeFormatter/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/anomalyco/CodeFormatter/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/anomalyco/CodeFormatter/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/anomalyco/CodeFormatter/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/anomalyco/CodeFormatter/releases/tag/v0.1.0