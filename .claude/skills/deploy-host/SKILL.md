---
name: deploy-host
description: Build, deploy, restart, and debug the STBud TcXaeShell Host (STFormatter.Host.exe). Use this skill whenever the user wants to deploy or test the Host, says the context menu / keyboard shortcuts / formatting stopped working inside TcXaeShell, asks to check or tail the Host log, mentions deploy.bat, the installer, C:\Program Files (x86)\STBud, ROT monikers, DTE connection problems, or anything about TcXaeShell integration behavior — even if they don't say "deploy" explicitly.
---

# Deploy and Debug the STBud TcXaeShell Host

The Host is an **external process** that connects to TcXaeShell via COM DTE. It is not a
VS extension and must never become one — read [AGENTS.md](../../../AGENTS.md) before
changing anything in `src/STFormatter.Host/`, `src/STFormatter.UI/`, deployment scripts,
or the installer. The non-negotiables that bite most often:

- Never deploy STBud files into Beckhoff's TcXaeShell `Extensions` tree. The Host lives in
  `C:\Program Files (x86)\STBud\` only. (Violating this once corrupted PLC project
  creation; recovery is `tools\fix-tcxeshell.ps1`.)
- Never retry VSPackage / MEF / VSIX / AddIn approaches — all are proven dead in
  TcXaeShell's isolated shell, with evidence tables in AGENTS.md.
- The Host stays `<OutputType>Exe</OutputType>` with a P/Invoke-hidden console. `WinExe`
  silently breaks COM.
- Live edit goes through DTE `Edit.SelectAll/Copy/Delete/Paste` + the Win32 clipboard
  API. `TextSelection.Text` and `System.Windows.Forms.Clipboard` do not work here.

## Deploy cycle

```powershell
# 1. Build (net48 default; net462 for older machines)
dotnet build src/STFormatter.Host/STFormatter.Host.csproj -c Debug

# 2. Stop a running Host first — deploy.bat cannot overwrite a locked exe,
#    and the Host enforces a single instance via a global mutex anyway.
Stop-Process -Name STFormatter.Host -Force -ErrorAction SilentlyContinue

# 3. Deploy (requires an elevated shell; copies ONLY the allowlisted Host files)
.\deploy.bat            # net48
.\deploy.bat net462     # older machines

# 4. Start the Host (it auto-detects running TcXaeShell instances and reconnects)
Start-Process "C:\Program Files (x86)\STBud\STFormatter.Host.exe"

# 5. Confirm it came up
Get-Content "$env:TEMP\STBud_Host.log" -Tail 20
```

`deploy.bat` ends with `pause`; when running it from a non-interactive shell, pipe input
so it doesn't hang: `cmd /c "echo. | deploy.bat"`.

## Verifying the integration works

There are no automated tests for the Host (COM/Win32-bound by design) — verification is
behavioral, against a running TcXaeShell:

1. Log shows a connection line for the TcXaeShell PID (moniker `!TcXaeShell.DTE.15.0:{PID}`
   on current installs; 14.0/12.0 on older ones).
2. Right-click in a PLC editor window → the **STBud for TwinCAT** menu appears
   (injected into `PlcCodeWinContextMenu` and `Code Window`).
3. Ctrl+Shift+F formats the active section; the edit is a single undo step (Ctrl+Z
   restores everything at once).
4. Restart TcXaeShell → the Host log shows reconnection within a few seconds.

## Debugging playbook

Always start with the log — the Host logs every connection attempt, menu injection,
format action, and failure:

```powershell
Get-Content "$env:TEMP\STBud_Host.log" -Tail 50
```

| Symptom | Likely cause / fix |
|---|---|
| "Another STFormatter.Host instance is already running" | Single-instance mutex (`Global\STFormatter.Host`). Stop the old process before starting a new one. |
| No TcXaeShell instance found | ROT scan found no moniker. Confirm TcXaeShell is actually running; both `!TcXaeShell.DTE.*` and `!VisualStudio.DTE.*` prefixes are scanned (`TcXaeShellVersionProfile`). Bitness matters: Host is x86 to match TcXaeShell. |
| Context menu missing after TcXaeShell restart | Wait for auto-reconnect (poll loop); check the log for re-injection lines. If stale duplicate buttons appear, the Host removes controls by tag prefix on reconnect. |
| Format does nothing / empty result | Clipboard pipeline issue. The editor's text is read via SelectAll+Copy+Win32 clipboard; check log for the read/format/paste step that failed. Remember: empty clipboard reads are expected on the CODESYS editor if focus was lost mid-operation. |
| Deploy fails with access denied | Shell isn't elevated, or the exe is still running (locked file). |
| TcXaeShell PLC features broken (worst case) | Someone deployed into Beckhoff's `Extensions` tree. Run `tools\fix-tcxeshell.ps1` — it restores the metadata-only state, clears MEF/extension caches, and removes stale registrations. |

`fix-tcxeshell.ps1` is emergency recovery only. It rewrites Beckhoff-owned folders and
clears caches, so run it solely after *confirming* contamination (STFormatter/STBud files
found under the `Extensions` tree) — never as a routine step in a reconnect problem,
where stopping and restarting the Host almost always suffices.

## What "Declaration vs Implementation" means here

The PLC editor has two tabs in one document and **no DTE API reveals which is active**.
The Host reads the active section via clipboard and classifies it by content
(`TwinCatXmlFormatter.LooksLikeDeclaration`), then formats with
`FormattingEngine.Format`/`FormatDeclaration` (declaration) or `FormatBody`
(implementation). If a user reports "it formatted my declaration as code" (or vice
versa), the heuristic misclassified — reproduce with the exact section text in a Core
unit test before touching the Host.

## Installer

```powershell
installer\build-installer.ps1   # Inno Setup; output lands in publish/
```

The installer packages the allowlisted Host payload from `installer/files/` and never
touches Beckhoff folders.
