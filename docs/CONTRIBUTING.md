# Contribution Guidelines / Beitragsrichtlinien

Thank you for contributing to TwinCAT ST Formatter! / Vielen Dank für Ihren Beitrag zum TwinCAT ST Formatter!

---

## Prerequisites / Voraussetzungen

- .NET 8 SDK
- TwinCAT XAE Shell (for Host integration testing)

---

## Build / Build

```
dotnet build TwinCAT.STFormatter.sln -c Release
```

---

## Run Tests / Tests Ausführen

```
dotnet test tests/STFormatter.Core.Tests
```

---

## TcXaeShell Host Development Workflow / TcXaeShell-Host-Entwicklungsworkflow

1. Build: `dotnet build src\STFormatter.Host\STFormatter.Host.csproj -c Release -p:TargetFramework=net48`
2. Deploy as admin with `deploy.bat` or build the installer with `installer\build-installer.ps1`.
3. Start `STFormatter.Host.exe` non-elevated from `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\`.
4. Open TcXaeShell and a PLC editor window.
5. Test the context menu commands: **Format ST Document** and **Format ST Selection**.
6. Check log: `%TEMP%\STFormatter_Host.log`.

---

## Code Style / Code-Stil

- Follow existing patterns in the codebase
- No unnecessary comments
- Null reference warnings should be addressed
- Use C# nullable reference types

---

## Adding Formatting Rules / Formatierungsregeln Hinzufügen

- Modify `FormattingEngine` or `FormattingVisitor` in Core
- Add corresponding unit tests in `STFormatter.Core.Tests`
- Test with the CLI and TcXaeShell Host targets.

---

## TcXaeShell Technical Notes / TcXaeShell-Technische Hinweise

- TcXaeShell is a 32-bit VS Isolated Shell, so the Host targets x86 `net462`/`net48`.
- Production integration is an external process that connects through COM DTE ROT monikers.
- Do not add VSPackage, MEF, VSIX, or AddIn integration for TcXaeShell; those paths do not load reliably.
- Deploy requires admin for Program Files copy, but run the Host non-elevated so it can see a non-elevated TcXaeShell ROT entry.
- Log file at `%TEMP%\STFormatter_Host.log`.

---

## Pull Request Process / Pull-Request-Prozess

1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Ensure all tests pass
5. Submit PR with description
