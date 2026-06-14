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

## Verified baseline (this dev machine)

Checked 2026-06: this machine runs **TwinCAT 3.1 Build 4024-series** — classic
`C:\TwinCAT\3.1` layout, single `TcXaeShell` on the VS 2017 isolated shell
(registry root `Beckhoff\TcXaeShell\15.0`, DTE 15.0, moniker
`!TcXaeShell.DTE.15.0:{PID}`). No TwinCAT Package Manager (`tcpkg`), no 64-bit
shell, no VS 2022 integration. **A live 4026 runtime test is therefore not
possible here** — Phases 0 and 2 still require access to a real 4026 install.

## Already handled (no 4026 machine needed)

Safe forward-compat that can only *add* detection, shipped ahead of Phase 0:

- **Shell process detection covers the 64-bit shell.** `TcXaeShellVersionProfile.
  ShellProcessNames` = `{TcXaeShell, TcXaeShell64}` and `IsShellProcessName()`
  prefix-matches `TcXaeShell*`. The keyboard-hook PID check and the Host scan
  diagnostics use it, so a 4026 `TcXaeShell64` is recognized for hotkeys and
  diagnostics without code changes.
- **Dynamic moniker fallback** already resolves any `!TcXaeShell.DTE.X.Y` /
  `!VisualStudio.DTE.X.Y` to a working profile with the stable
  `PlcCodeWinContextMenu` / `Code Window` menu names and `.TcPOU/.TcDUT/...`
  extensions. `TcXaeShellVersionProfileTests` pins this for DTE 16.0/17.0 so a
  version bump in 4026 still connects.

The remaining unknown for connection is whether 4026 routes through VS 2022's
`devenv` with a "Microsoft Visual Studio" DTE name (which `IsTcXaeShell` would
reject) rather than the standalone TcXaeShell — that is a **[verify]** for Phase 0.

## Upgrade procedure (use this when installing 4026)

`stfmt doctor` is the recon tool. The 4024 baseline is already captured in
[baseline-4024.txt](baseline-4024.txt). When you install 4026:

```
stfmt doctor --save docs\after-4026.txt
```

then diff `after-4026.txt` against `baseline-4024.txt`. The report shows the exact
ROT moniker each running shell registers, with a per-moniker verdict (SUPPORTED /
forward-compat fallback / not recognized), the install model (TcPkg vs classic),
detected shells with versions, and the deployed Host. That diff answers Phase 0's
three unknowns in one shot: new moniker?, standalone-shell vs VS 2022 `devenv`?,
and 64-bit shell present?.

## Phase 0 — Reconnaissance (no code changes)

On a Build 4026 installation, capture ground truth:

1. **Run `stfmt doctor --save docs\after-4026.txt`** — it enumerates the ROT and
   classifies every DTE moniker against `TcXaeShellVersionProfile`. (The Host also
   logs monikers in `%TEMP%\STBud_Host.log`, but doctor needs no Host deploy.)
   **[verify]** moniker prefix and DTE version for: the classic TcXaeShell, the
   64-bit shell, and VS 2022 with the TwinCAT integration.
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
3. ~~If the 64-bit shell exists as a separate process name, the Host's process scan
   must include it.~~ **Done** — `ShellProcessNames` includes `TcXaeShell64`; the
   keyboard hook and scan diagnostics prefix-match `TcXaeShell*`.

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

## Phase 4 — Compatibility report tool ✅ Done

`stfmt doctor` (`TwinCatDoctor` in Core, used by the CLI) prints:

- detected TwinCAT install + build (ProductVersion of the runtime service) and
  whether TcPkg / classic install model is in use,
- detected XAE shells with versions,
- running shells and their **live ROT monikers**, each classified against the
  version matrix (SUPPORTED / forward-compat fallback / not recognized),
- Host deploy state (files + versions in `C:\Program Files (x86)\STBud`),
- the Pinger's local ADS port check.

`--save <file>` writes the report for diffing. Pure filesystem + COM ROT +
process inspection (no registry dependency). A tray-UI button can call the same
`TwinCatDoctor.BuildReport()` later.

## Ordering and effort

| Phase | Needs a 4026 machine | Effort | Blocked by |
|---|---|---|---|
| 0 Recon | yes | hours | access to 4026 |
| 1 Version profile | no (uses Phase 0 data) | small | Phase 0 |
| 2 Host verification | yes | half a day | Phase 1 |
| 3 Project fixtures | no (files from Phase 0) | small | Phase 0 |
| 4 `stfmt doctor` | no | medium | ✅ done |

Phase 4 is done and is the recon tool for Phase 0. Remaining order: **0 → 1 → 3 → 2**,
all gated on access to a real 4026 install.

## Non-goals (unchanged from ROADMAP)

No VSPackage/MEF/VSIX/AddIn integration on any version, including 4026's shells and
Visual Studio 2019/2022 integration — external COM DTE only. No files in
Beckhoff-owned folders.
