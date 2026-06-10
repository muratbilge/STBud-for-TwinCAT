# TwinCAT 3 Build 4026 Compatibility Plan

Build 4026 is the newest TwinCAT 3 generation. It changes how TwinCAT is installed
(TwinCAT Package Manager instead of the monolithic setup) and what shells are
available. This plan turns the ROADMAP's "TwinCAT 4026 Compatibility Test" section
into concrete, ordered work. Everything STBud does against TcXaeShell flows through
a handful of choke points, so the plan is organized around verifying each one.

## What we rely on today (Build 4024 baseline)

| Dependency | Where it lives | 4026 risk |
|---|---|---|
| ROT moniker `!TcXaeShell.DTE.15.0:{PID}` | `TcXaeShellVersionProfile` (auto-detect) | New shell may register a different DTE version (16.0/17.0) or a different prefix |
| VS 2017 isolated shell, 32-bit | Host is x86 to match | 4026 adds a 64-bit shell; bitness mismatch changes COM marshaling assumptions |
| `PlcCodeWinContextMenu` / `Code Window` CommandBars | `HostManager` menu injection | Menu names/control counts may change with the shell version |
| Clipboard live-edit via `Edit.SelectAll/Copy/Delete/Paste` | `LiveEditor` | Standard DTE commands - most stable part, still needs a smoke pass |
| Install path `Beckhoff\TcXaeShell\Common7\IDE\` | Version profile, docs | Package Manager installs to different roots |
| Registry root `Beckhoff\TcXaeShell\15.0` | Version profile | New hive/paths under 4026 |
| `.tsproj` schema (I/O tree, mappings) | `IoTreeParser` | 4026 project format may add/move elements |
| TwinCAT XML (`.TcPOU/.TcDUT/.TcGVL`) | `TwinCatXmlFormatter` | ProductVersion bump; CDATA structure expected stable |

Unknowns are flagged below as **[verify]** — resolve them on a real 4026 machine
before writing any code. Do not hard-code guesses; everything version-specific goes
through `TcXaeShellVersionProfile`.

## Phase 0 — Reconnaissance (no code changes)

On a Build 4026 installation, capture ground truth:

1. **Enumerate the ROT** while the 4026 shell runs. The Host log already prints every
   DTE-like moniker it sees (`Scan:` lines in `%TEMP%\STBud_Host.log`) — run the
   current Host unmodified and record what appears. **[verify]** moniker prefix and
   DTE version for: the classic TcXaeShell, the 64-bit shell, and VS 2022 with the
   TwinCAT integration.
2. Record install paths, registry roots, process names, and shell bitness
   (`Get-Process <name> | Select Path, ProcessName`; check WOW64).
3. Save a 4026-created PLC project (`.tsproj` + POUs) into `samples/` as fixtures —
   **[verify]** schema differences against our `IoTreeParser` expectations and the
   `TcPlcObject` ProductVersion/format.
4. Document everything in AGENTS.md (new "Build 4026" section of the version matrix).

Deliverable: filled-in version matrix row; no surprises left in the table above.

## Phase 1 — Version profile support

All changes land in `TcXaeShellVersionProfile` (Core) only:

1. Add the 4026 profile(s): DTE version, moniker prefixes, install path, registry
   root, process name, bitness — from Phase 0 data.
2. The dynamic-moniker fallback already parses unrecognized
   `!TcXaeShell.DTE.*`/`!VisualStudio.DTE.*` versions; add unit tests asserting the
   4026 monikers resolve to the right profile (these tests run everywhere, no
   TcXaeShell needed).
3. If the 64-bit shell exists as a separate process name **[verify]**, the Host's
   process scan must include it.

## Phase 2 — Host behavioral verification on 4026

Manual checklist against a live 4026 shell (no automated coverage possible — COM):

1. ROT discovery and DTE connection (Host log shows the connect line).
2. Context-menu injection: does `PlcCodeWinContextMenu` still exist with that name?
   **[verify]** If renamed, add the new name to the version profile, keyed by version.
3. Live edit: copy → detect section → format → paste → single undo step.
4. Keyboard shortcuts with shell focused; auto-reconnect across shell restart.
5. **Bitness check**: if the 64-bit shell is the target, confirm the x86 Host can
   bind the DTE moniker cross-bitness (COM marshaling normally allows this, but
   `CommandBars` event sinks are the risky part). If it fails, the fix is a second
   Host build (AnyCPU/x64) selected at runtime — keep the decision in the profile,
   not scattered in code.

## Phase 3 — Project-format compatibility (automated)

1. Add the 4026 sample files from Phase 0 to the corpus — the existing
   `SampleCorpusTests` (idempotency + token preservation) then gate them on every
   test run automatically.
2. Add `IoTreeParser` fixture tests for the 4026 `.tsproj` structure.
3. `stfmt batch <4026 project> --twincat --dry-run` must report 0 errors.

## Phase 4 — Compatibility report tool

Roadmap asks for "a compatibility report that can be copied into issues/support
notes". Cheapest useful version: a `stfmt doctor` CLI command (and a tray-UI button
later) that prints:

- detected TwinCAT installs (paths + versions from registry),
- running shells and their ROT monikers,
- Host deploy state (files + versions in `C:\Program Files (x86)\STBud`),
- the Pinger's local ADS port check,
- known-support status per the version matrix.

This reuses `TcXaeShellVersionProfile`, `TwinCatPinger`, and the ROT scan the Host
already has — mostly wiring, not new machinery.

## Ordering and effort

| Phase | Needs a 4026 machine | Effort | Blocked by |
|---|---|---|---|
| 0 Recon | yes | hours | access to 4026 |
| 1 Version profile | no (uses Phase 0 data) | small | Phase 0 |
| 2 Host verification | yes | half a day | Phase 1 |
| 3 Project fixtures | no (files from Phase 0) | small | Phase 0 |
| 4 `stfmt doctor` | no | medium | none — can start anytime |

Phase 4 can start immediately and actually makes Phase 0 easier (the doctor command
is the recon tool). Recommended order: **4 → 0 → 1 → 3 → 2**.

## Non-goals (unchanged from ROADMAP)

No VSPackage/MEF/VSIX/AddIn integration on any version, including 4026's shells and
Visual Studio 2019/2022 integration — external COM DTE only. No files in
Beckhoff-owned folders.
