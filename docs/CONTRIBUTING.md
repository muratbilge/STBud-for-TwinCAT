# Contribution Guidelines / Beitragsrichtlinien

Thank you for contributing to TwinCAT ST Formatter! / Vielen Dank für Ihren Beitrag zum TwinCAT ST Formatter!

---

## Prerequisites / Voraussetzungen

- .NET 8 SDK
- Visual Studio 2022 with VS SDK workload (for VSIX development)
- TwinCAT XAE Shell (for TcXaeShell extension testing)

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

## TcXaeShell Development Workflow / TcXaeShell-Entwicklungsworkflow

1. Build: `dotnet build src\STFormatter.TcXaeShell -c Release` (use `-p:TargetFramework=net462` for older TcXaeShell)
2. Stop TcXaeShell
3. Run deploy script as admin (copies DLLs to `C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter\`)
4. Clear caches (replace `15.0` with your TcXaeShell version — 15.0, 14.0, or 12.0):
   - Delete `%LOCALAPPDATA%\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache\`
   - Delete `%LOCALAPPDATA%\Beckhoff\TcXaeShell\15.0\Extensions\extensions.en-US.cache`
5. Restart TcXaeShell
6. Test: Edit → Format ST Document / Format ST Selection (Ctrl+K,D / Ctrl+K,F)
7. Check log: `%TEMP%\STFormatter_TcXaeShell.log`

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
- Test with all 3 targets (CLI, VSIX, TcXaeShell)

---

## TcXaeShell Technical Notes / TcXaeShell-Technische Hinweise

- It is a 32-bit VS Isolated Shell (VS 2017 v15, VS 2015 v14, or VS 2013 v12 depending on TwinCAT build) — must target `net462/x86` or `net48/x86`
- VS SDK 15.9.3 reference, VSSDK BuildTools 17.12.2069 for sdk-style project
- Menu resource version must stay at 1
- Beckhoff DLLs referenced with `Private=false`, loaded via TwinCAT binding path
- Deploy requires admin (registry entries, program files copy)
- Log file at `%TEMP%\STFormatter_TcXaeShell.log`

---

## Pull Request Process / Pull-Request-Prozess

1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Ensure all tests pass
5. Submit PR with description