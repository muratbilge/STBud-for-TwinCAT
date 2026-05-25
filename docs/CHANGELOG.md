# Changelog / Aenderungsprotokoll

All notable changes to this project will be documented in this file.
Alle wichtigen Aenderungen an diesem Projekt werden in dieser Datei dokumentiert.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.5.0] - 2026-05-21

### Added / Hinzugefuegt

- TcXaeShell extension: live editor update via TwinCAT Automation API
- `IPLCData` → `PlcFileNode` → `TcPouItemAdapter` pipeline for in-memory formatting
- Direct write-back to `DeclarationText` and `ImplementationText` properties
- Seamless format-on-save experience inside TcXaeShell without file round-tripping

## [0.4.0] - 2026-05-14

### Added / Hinzugefuegt

- TcXaeShell extension (VSIX) with file-only CDATA replacement approach
- Formatting of `.TcPOU`, `.TcDUT`, and `.TcGVL` files via CDATA section replacement
- No dependency on Automation API; safe for environments where the API is unavailable

## [0.3.0] - 2026-05-07

### Added / Hinzugefuegt

- Visual Studio 2022 extension (VSIX)
- Format Document command
- Format Selection command
- Format on Save auto-formatting
- Options page for configuring formatting rules

## [0.2.0] - 2026-04-30

### Added / Hinzugefuegt

- CLI tool with the following commands:
  - `format` — format one or more files in place
  - `check` — verify formatting without making changes
  - `batch` — format an entire directory tree
  - `init` — scaffold a configuration file
  - `preset` — apply a built-in style preset
  - `export` — export current settings
  - `import` — import settings from a file

## [0.1.0] - 2026-04-23

### Added / Hinzugefuegt

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