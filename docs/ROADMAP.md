# Roadmap

STBud for TwinCAT is a toolbox for TwinCAT Structured Text work. Formatting is
the first major tool, but the long-term direction is a practical pool of editor
helpers, project utilities, runtime diagnostics, connectivity checks, and safe
automation around TcXaeShell.

This roadmap is intentionally pragmatic. Items may move between phases based on
what is most useful in daily TwinCAT work.

---

## Current Focus

- Keep the external Host stable and independent from Beckhoff's TcXaeShell folders.
- Improve Structured Text formatting quality for `.st`, `.TcPOU`, `.TcDUT`, and `.TcGVL` files.
- Expand context-menu helpers for TwinCAT attributes, pragmas, regions, tasks, and I/O linking.
- Keep installer/deployment clean under `C:\Program Files (x86)\STBud`.
- Maintain compatibility with TcXaeShell through external COM DTE only.

---

## Near Term

### Editor Helper Improvements

- Improve the **Add** submenu structure and labels.
- Expand TwinCAT pragma and attribute templates.
- Add safer insertion flows for warnings, regions, task attributes, and call-order attributes.
- Improve I/O linking path insertion and validation.
- Add better feedback when the active editor section cannot be detected.

### Formatter Improvements

- Continue improving declaration and implementation formatting edge cases.
- Add more sample TwinCAT XML files and regression tests.
- Improve formatting diagnostics in Host logs.
- Improve selection formatting behavior for partial ST snippets.

### Installer And Runtime Polish

- Keep Host payload packaging allowlisted.
- Improve upgrade behavior from older `STFormatter` installs.
- Keep generated artifacts out of the repository.
- Improve first-run and auto-start behavior.

---

## Runtime And Connectivity Tools

### ADS Tester

Build a small ADS test utility for validating TwinCAT runtime connectivity.

Planned capabilities:

- Test ADS route availability.
- Connect by AMS Net ID and ADS port.
- Read simple symbols.
- Write simple symbols with explicit confirmation.
- Browse symbols if the target exposes symbol information.
- Show ADS errors with readable explanations.
- Export connection diagnostics for troubleshooting.

Implementation notes:

- Start as a standalone STBud tool, not as a TcXaeShell extension.
- Prefer read-only tests by default.
- Make write operations opt-in and visible.
- Keep connection history local to the user profile.

### Pinger

Add a lightweight network/runtime pinger for TwinCAT machines.

Planned capabilities:

- Ping target IP address or hostname.
- Check common TwinCAT-related ports.
- Detect whether ADS/router appears reachable.
- Show latency and connection status.
- Save recent targets.
- Provide a simple diagnostic summary for support/debugging.

Implementation notes:

- Keep it independent from TcXaeShell.
- Support quick checks from the tray UI and CLI.
- Avoid requiring administrator privileges for normal checks.

### TwinCAT 4026 Compatibility Test

Add explicit validation for TwinCAT 3 Build 4026 environments.

Planned phases:

- **Manual checklist**: document expected TcXaeShell behavior on Build 4026.
- **Host compatibility checks**: verify ROT moniker detection, context-menu injection, live edit, undo, and reconnect.
- **Project compatibility checks**: verify I/O tree parsing and TwinCAT XML formatting against Build 4026 project structures.
- **Runtime compatibility checks**: detect installed TwinCAT version where possible and report known support status.

Planned capabilities:

- Verify TcXaeShell ROT moniker detection.
- Verify `PlcCodeWinContextMenu` injection still works.
- Verify live edit flow: copy, format, paste, undo.
- Verify I/O tree parsing against Build 4026 `.tsproj` structures.
- Document known 4026-specific behavior.
- Add a compatibility report that can be copied into issues/support notes.

---

## Mid Term

### Project Analysis Tools

- Detect common ST style issues beyond formatting.
- Identify suspicious empty declarations, duplicated regions, and inconsistent attributes.
- Add naming/style inspections configurable by `.editorconfig`.
- Provide project-level summaries for POUs, DUTs, GVLs, and methods.

### TwinCAT Project Utilities

- Improve `.tsproj` parsing helpers.
- Add tools for locating PLC objects and related files.
- Add safe backup/restore helpers for TwinCAT XML files.
- Add diagnostics for missing or malformed project metadata.

### Host UI Improvements

- Improve settings UX.
- Add a dedicated toolbox tab for non-formatting utilities.
- Add better history records for helper actions, not just format actions.
- Add a diagnostics panel for Host, TcXaeShell, ADS, and network checks.

---

## Long Term

### Toolbox Architecture

- Organize STBud tools as separate modules inside the external Host.
- Keep each tool independently testable.
- Support shared diagnostics, logging, settings, and history across tools.
- Avoid in-process TcXaeShell extension mechanisms.

### Code Quality And Review Tools

- Add rule-based code quality checks for Structured Text.
- Add project health dashboards.
- Add optional reports for CI usage.
- Consider ST explanation/review helpers if they can be implemented safely and locally.

### Broader TwinCAT Automation

- Add more editor automation commands where COM DTE is reliable.
- Add more runtime diagnostics around ADS and routes.
- Add exportable troubleshooting bundles.

---

## Non-Goals

- No VSPackage integration for TcXaeShell.
- No VSIX deployment for TcXaeShell.
- No MEF component deployment for TcXaeShell.
- No VS AddIn deployment for TcXaeShell.
- No STBud files inside Beckhoff's TcXaeShell `Extensions` tree.
- No copying Beckhoff PLC DLLs into STBud folders.
- No modifying Beckhoff-owned folders during normal install/deploy.

Emergency historical cleanup remains separate in `tools\fix-tcxeshell.ps1` and
must not be treated as normal deployment.

---

## Design Principles

- Use an external process plus COM DTE for TcXaeShell integration.
- Prefer live editor automation over disk writes for active documents.
- Keep operations reversible where possible.
- Make write/destructive operations explicit.
- Keep deployment independent from Beckhoff's installation folders.
- Prefer small practical tools over large fragile integrations.
- Log enough detail to troubleshoot field issues quickly.
