# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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