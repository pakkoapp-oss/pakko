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

# Run all tests
dotnet test
```

Always run `dotnet test` with no path argument — all projects must stay green after every change.

The WinUI 3 application (`Archiver.App`) must be built and run from **Visual Studio 2022**.
`dotnet build src/Archiver.Core` and `dotnet test` work freely from the terminal.

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
| `Archiver.Core` | Compression logic — no UI dependencies, no NuGet packages |
| `Archiver.App` | WinUI 3 main application |
| `Archiver.Shell` | Shell extension entry point (`IExplorerCommand`) |
| `Archiver.ProgressWindow` | Progress UI for silent shell-invoked operations |
| `Archiver.Core.Tests` | Unit tests for core compression logic |
| `Archiver.Shell.Tests` | Argument parser tests for the shell extension |
| `Archiver.Core.Tests.GenerateFixtures` | Fixture generator (see above) |
