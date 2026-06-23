---
name: release
description: Cut a versioned release of STBud for TwinCAT. Use when the user wants to release a new version, bump the version, tag a release, publish, or ship — e.g. "cut a release", "release 1.1.0", "bump the minor version", "tag a release", "make a release build". Handles the SemVer dev/release flow: bump VersionPrefix, finalize the CHANGELOG, build a clean release, commit, tag vX.Y.Z, then start the next -dev line. Does NOT push (the operator pushes tags).
---

# Cut a release of STBud for TwinCAT

Versioning rules live in [CLAUDE.md](../../../CLAUDE.md): `Directory.Build.props`
`<VersionPrefix>` is the single source of truth; dev builds are `X.Y.Z-dev+<sha>`, releases
are clean `X.Y.Z` (built with `-p:PublicRelease=true`) and git-tagged `vX.Y.Z`.

## Before doing anything

1. **Clean tree & right branch.** `git status --short` must be empty; on `master`.
2. **Tests green.** `dotnet test tests/STFormatter.Core.Tests` passes.
3. **Decide the version.** From the requested bump (feat→minor, fix→patch, breaking→major)
   or an explicit `X.Y.Z`. Between releases `VersionPrefix` already holds the next `-dev`
   target, so a "release what's in dev" usually means releasing the current `VersionPrefix`.

## Steps

1. **Set the release version** in `Directory.Build.props` — set `<VersionPrefix>` to the
   release number `X.Y.Z` (it likely already is). The release build drops the `-dev` suffix
   via `-p:PublicRelease=true`; do not add a `<Version>` element.
2. **Finalize the CHANGELOG** (`docs/CHANGELOG.md`): move everything under `[Unreleased]`
   to a new `## [X.Y.Z] - <yyyy-mm-dd>` section (use today's date), leave an empty
   `[Unreleased]` at the top, and add/refresh the compare links at the bottom
   (`[X.Y.Z]: https://github.com/muratbilge/STBud-for-TwinCAT/compare/v<prev>...vX.Y.Z`).
3. **Verify the release build** prints a clean version:
   ```powershell
   dotnet build TwinCAT.STFormatter.sln -p:PublicRelease=true -c Release --nologo
   dotnet run --project src/STFormatter.CLI --no-build -p:PublicRelease=true -- --version
   # -> "STBud for TwinCAT CLI X.Y.Z"  (no -dev, no +sha)
   ```
4. **(Optional) Build the installer** — it reads the version from
   `Directory.Build.props` automatically:
   ```powershell
   installer\build-installer.ps1 -Configuration Release
   # -> publish\STBud-for-TwinCAT-Setup-X.Y.Z.exe
   ```
5. **Commit & tag**:
   ```powershell
   git add Directory.Build.props docs/CHANGELOG.md
   git commit -m "release: vX.Y.Z"
   git tag -a vX.Y.Z -m "STBud for TwinCAT vX.Y.Z"
   ```
6. **Open the next dev line**: bump `<VersionPrefix>` to the next planned version (e.g. a
   release of `1.1.0` → `1.2.0`), commit:
   ```powershell
   git add Directory.Build.props
   git commit -m "chore: begin <next>-dev"
   ```
7. **Hand off the push.** Do NOT push automatically. Tell the operator:
   `git push --follow-tags origin master` (or the relevant remote). Note that pushing a
   rewritten/force history is a separate, explicit decision.

## Notes

- Dev builds need no action here — a normal `dotnet build` already yields
  `X.Y.Z-dev+<gitShortSha>`.
- Keep `AssemblyVersion`/`FileVersion` numeric (handled by the props); only the
  informational version carries the `-dev+<sha>` / clean form.
- If `git status` is dirty or tests fail, stop and report — never tag an unclean tree.
