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

### Pinger — ✅ Shipped

A lightweight network/runtime pinger for TwinCAT machines, available as the CLI
`stfmt ping <host>` command and the tray UI **Toolbox** tab. Pings the target,
checks the ADS/AMS (48898) and Secure ADS (8016) ports, shows latency/status,
persists recent targets, and prints a copyable diagnostic summary. Independent of
TcXaeShell; no admin needed.

### TwinCAT 4026 Compatibility — ✅ Verified on a live 4026 install

Detailed, phased plan: [COMPATIBILITY-4026-PLAN.md](COMPATIBILITY-4026-PLAN.md).

Confirmed working on Build 4026: the Host connects to and injects its menu into all
three engineering environments — the 4024 TcXaeShell (DTE 15.0), the 4026 TcXaeShell
(DTE 17.0, dynamic moniker fallback), **and TwinCAT-in-Visual-Studio-2022** (`devenv`,
detected by the `PlcCodeWinContextMenu` command bar since its DTE name is "Microsoft
Visual Studio"). The 64-bit `TcXaeShell64` process name is recognized.

The **`stfmt doctor`** command delivers the "compatibility report for issues/support
notes" item: it reports the install + build, install model (TcPkg vs classic), running
shells with their live ROT monikers (each classified SUPPORTED / fallback / unknown),
the deployed Host, and a local ADS check. `--save` writes it for diffing across upgrades.

Still worth a manual pass on 4026: live-edit format and the I/O-linking insert inside
each environment (the connection + injection paths are confirmed).

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
