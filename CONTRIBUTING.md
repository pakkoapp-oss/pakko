# Contributing to Pakko

## Prerequisites

- **Visual Studio 2022** with the *Windows App SDK / WinUI 3* workload
- **.NET 8 SDK**
- **Windows 10 1809+** or Windows 11

---

## Building and testing

```bash
# Build the core library (works from any terminal)
dotnet build src/Archiver.Core

# Run all tests (excludes a handful of real multi-second Zip64/performance tests and a couple of
# genuinely large, on-demand-only tests)
dotnet test --filter "Category!=Slow&Category!=VeryLarge"

# Run the Zip64 tests too, before a release or when touching Zip64-adjacent code
dotnet test --filter "Category=Slow"
```

Always run `dotnet test` with no path argument — all projects must stay green after every change.

The WinUI 3 application (`Archiver.App`) must be built and run from **Visual Studio 2022**.
`dotnet build src/Archiver.Core` and `dotnet test` work freely from the terminal — as does
`dotnet build src/Archiver.App`, which compiles the WinUI project (useful as a quick
compile-check on ViewModel/XAML changes without opening Visual Studio), though full MSIX
packaging/signing/running still needs `Deploy.ps1` or Visual Studio.

---

## Test fixtures

`tests/Archiver.Core.Tests.GenerateFixtures` is a standalone console project that creates
the binary ZIP fixtures consumed by `Archiver.Core.Tests`. Fixtures are pre-generated and
committed to the repository — you do not need to regenerate them on a normal clone.

**Regenerate fixtures when:**
- Adding new tests that require new fixture files
- Existing fixtures become corrupted

```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```

Output goes to `tests/Archiver.Core.Tests/Fixtures/`.

---

## Local MSIX deployment

For local development and testing you need a signed MSIX. See [`scripts/README.md`](scripts/README.md)
for full details. The short version:

1. **Once per machine** — create and install a self-signed developer certificate:
   ```powershell
   .\scripts\Setup-DevCert.ps1
   ```
   Copy the thumbprint it prints.

2. **After each build** — package and sideload:
   ```powershell
   .\scripts\Deploy.ps1
   # or: .\scripts\Deploy.ps1 -Thumbprint "<thumbprint>"
   ```

   > **Visual Studio shortcut** — Release builds in VS trigger
   > `Deploy.ps1 -DeployOnly` automatically via a post-build event.
   > No manual script needed after first setup; just build in Release and
   > the new MSIX is installed automatically.
   > Use `.\scripts\Deploy.ps1` from the terminal for a full build + deploy
   > outside of Visual Studio.

---

## Testing protocol activation (pakko://)

After installing the MSIX, verify the `pakko://` URI scheme works:

```powershell
$files = '["C:\\path\\to\\file.zip"]'
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($files))
Start-Process "pakko://extract?files=$b64"
```

Pakko should launch and begin extracting the specified archive.

---

## Project structure

| Project | Role |
|---------|------|
| `Archiver.Core` | Compression/extraction logic and the tar.exe AppContainer sandbox — no UI dependencies, no NuGet packages |
| `Archiver.App.Core` | WinUI-free helpers for `Archiver.App` (Archive Browser tree/breadcrumb building, real-filesystem browsing, file-activation routing) — kept separate so they're unit-testable without a WinUI test host |
| `Archiver.App` | WinUI 3 main application |
| `Archiver.Shell` | Shell-triggered operation entry point (silent CLI, launched by the shell extension); shows progress via the Windows Shell's built-in `IProgressDialog`, in-process |
| `Archiver.ShellExtension` | C++ COM DLL implementing `IExplorerCommand` (the actual right-click context menu) — built via MSBuild, not `dotnet build`; see `CLAUDE.md` Build Commands |
| `Archiver.CLI` | Standalone console frontend (T-F09), built as `pakko.exe` — no WinUI, no MSIX, ships as its own self-contained per-architecture download via `scripts/Publish-Cli.ps1`; see `CLI.md` |
| `Archiver.Core.Tests` | Unit tests for core compression/extraction logic |
| `Archiver.Core.IntegrationTests` | Tests that shell out to the real `C:\Windows\System32\tar.exe` (tagged `[Integration]`) |
| `Archiver.Core.PerformanceTests` | ZIP compression/extraction performance-regression tests vs. a vendored, sandboxed `7za.exe` reference (T-F114); see `TESTING.md` |
| `Archiver.App.Core.Tests` | Unit tests for `Archiver.App.Core`'s WinUI-free helpers |
| `Archiver.Shell.Tests` | Argument parser tests for `Archiver.Shell` |
| `Archiver.ShellExtension.Tests` | C++ Google Test suite for `Archiver.ShellExtension`'s COM-free logic — run separately, not covered by `dotnet test`; see `CLAUDE.md` Build Commands |
| `Archiver.CLI.Tests` | Parser/mapper/help-text unit tests for `Archiver.CLI` plus a `Subprocess/` layer that launches the real built `pakko.exe`; see `CLI.md` |
| `Archiver.Core.Tests.GenerateFixtures` | Fixture generator (see above) |

> There is no `Archiver.ProgressWindow` project — an earlier design (a second WinUI 3 satellite
> `.exe` talking to `Archiver.Shell` over a named pipe) was removed; see `DECISIONS.md` (T-F65).
