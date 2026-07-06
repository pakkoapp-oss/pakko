# TESTING.md — Test Plan

Covers `Archiver.Core` only. UI layer (`Archiver.App`) is not unit-tested in v1.0.

---

## Running Tests

```bash
# Run all tests (skips T-F20's Zip64 Slow tests — see below)
dotnet test tests/Archiver.Core.Tests --filter "Category!=Slow"

# With verbose output
dotnet test tests/Archiver.Core.Tests --filter "Category!=Slow" --logger "console;verbosity=normal"

# Zip64 tests only — real multi-second/multi-GB cost, not run by default
dotnet test tests/Archiver.Core.Tests --filter "Category=Slow"
```

**Slow tests (`[Trait("Category", "Slow")]`):** `ZipArchiveServiceZip64Tests.cs` (T-F20) creates
65,600 real files and a >4 GiB sparse file to exercise Zip64's entry-count and large-size
boundaries — genuinely expensive (~30s per >65535-file test; multi-GB disk I/O for the large-file
test), not something worth paying on every `dotnet test` run. Run explicitly before a release or
when a change touches Zip64-adjacent code.

**Note:** Do not run from Visual Studio Test Explorer when WinUI project is in the same solution — VS Test Explorer has a known issue with WinUI + mixed solution. Use CLI.

---

## Test Project Setup

```xml
<!-- tests/Archiver.Core.Tests/Archiver.Core.Tests.csproj -->
<TargetFramework>net8.0</TargetFramework>  <!-- NOT net8.0-windows — pure .NET -->
<PackageReference Include="xunit" Version="2.5.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

---

## Current Test Count

**48 tests total** — all pass as of v1.1.

| File | Tests | Coverage |
|------|-------|---------|
| `ZipArchiveServiceArchiveTests.cs` | ~17 | Archive modes, conflicts, progress, cancellation, delete source, temp file pattern (T-F26), UTF-8 filenames (T-F29) |
| `ZipArchiveServiceExtractTests.cs` | ~14 | Extract modes, smart foldering, password detection, conflict, delete archive, temp dir pattern (T-F27), bomb protection (T-F28) |
| `ZipArchiveServiceFixtureTests.cs` | 18 | Fixture-based: valid archives, corrupted, encrypted, ZIP slip |
| `ZipArchiveServiceTestAsyncTests.cs` | 4 | T-F62: `TestAsync` CRC-32 verification — valid archive passes, corrupted-CRC fixture fails, encrypted archive errors, mixed valid+corrupted selection reports only the corrupted one |
| `ZipArchiveServicePropertyTests.cs` | 16 | T-F24: property-based archive/extract round-trip — random directory trees (12 seeds) + named all-small/all-large/mixed/deep-nesting scenarios, SHA-256 hash comparison per file |
| `ZipArchiveServiceZip64Tests.cs` | 3 | T-F20: Zip64 boundaries — `[Trait("Category","Slow")]`, excluded from default `dotnet test` (see "Running Tests" above) |
| `ArchiveOptionsTests.cs` | ~2 | Model defaults |

This table (and the "48 tests total" figure below) predates several rounds of additions
(T-F37/38/39/45/58/59/60, etc.) and is known stale beyond the `TestAsync` row just added —
tracked as its own cleanup, not fixed wholesale here. Current true count: run `dotnet test`.

Tests added in v1.1:
- `ArchiveAsync_Cancelled_LeavesNoTempFile` (T-F26)
- `ExtractAsync_Cancelled_LeavesNoTempDirectory` (T-F27)
- `ExtractAsync_SuspiciousCompressionRatio_SkipsEntry` (T-F28)
- `CyrillicFilename_PreservedAfterRoundTrip` (T-F29)
- `EmojiFilename_PreservedAfterRoundTrip` (T-F29)

---

## Test Helpers

### TempDirectory

```csharp
// Helpers/TempDirectory.cs
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }
    public TempDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()); Directory.CreateDirectory(Path); }
    public string CreateFile(string name, string content = "test content") { var p = System.IO.Path.Combine(Path, name); File.WriteAllText(p, content); return p; }
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
}
```

### FixtureHelper

```csharp
// Helpers/FixtureHelper.cs
public static class FixtureHelper
{
    public static string Archive(string name)      // throws Assert.Inconclusive if missing
    public static string ArchiveOptional(string name)  // returns null if missing
    public static string PlainFile(string name)    // throws Assert.Inconclusive if missing
}
```

Missing fixture → `Assert.Inconclusive` with message:
```
Fixture missing: created_by_macos.zip
Run: dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```

---

## Test Fixtures

Located at `tests/Archiver.Core.Tests/Fixtures/`.

**Generated automatically** (run `GenerateFixtures` project):
- `files/compressible.txt`, `incompressible.bin`, `unicode_filename_привіт.txt`, `readme.txt`
- `archives/valid_*.zip` — 5 valid archives
- `archives/extract_*.zip` — 3 smart extract scenarios
- `archives/corrupted_*.zip` — 3 corrupted archives (T-F62 adds `corrupted_crc_stored.zip`:
  a Stored/uncompressed entry with a data byte flipped after write — reads back cleanly, only
  its CRC-32 is wrong, unlike the other two which break the Deflate stream or the EOCD signature)
- `archives/encrypted_zipcrypto.zip`
- `archives/zipslip_traversal.zip`

**Manual fixtures** (instructions in `*_MANUAL.txt` files):
- `encrypted_aes256.zip` — requires 7-Zip
- `created_by_7zip.zip`, `created_by_winrar.zip`, `created_by_macos.zip`
- `pakko_integrity_valid.zip`, `pakko_integrity_tampered.zip` — after T-34

**Tests with missing manual fixtures are skipped (yellow), not failed.**
`dotnet test` returns success even with skipped tests.

---

## Key Patterns

```csharp
// Standard test structure
public sealed class ZipArchiveServiceArchiveTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ArchiveAsync_SingleFile_CreatesZip()
    {
        var file = _temp.CreateFile("document.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
    }
}
```

---

## Integration Tests (v1.3+)

Project: `tests/Archiver.Core.IntegrationTests/` (created in T-F49).

```bash
# Run integration tests (requires Windows with tar.exe)
dotnet test tests/Archiver.Core.IntegrationTests
```

`TarProcessServiceExtractTests.cs` (7 tests) exercises `TarProcessService.ExtractAsync` against
the real system `tar.exe`: a round-trip extraction, a rename-conflict case, MOTW propagation,
and three whole-archive-reject cases (path-traversal entry, ADS/reserved-name entry, and a
symlink-entry escape — the last is a regression test for the exploit documented in
`DECISIONS.md`'s T-F49 entry). Fixtures are self-generated per-test via `TarBuilder.cs` (raw
USTAR bytes, no third-party tooling — needed since a `..`-entry or a symlink escape target isn't
a representable real source path) rather than a prebuilt corpus; T-F50 still owns the full
multi-format fixture set below.

### Tags

- `[Integration]` — custom `FactAttribute` (`IntegrationAttribute.cs`), skipped automatically if
  `C:\Windows\System32\tar.exe` is not present
- `[SkipIfFormatUnsupported("rar5")]` etc. — custom `FactAttribute`
  (`SkipIfFormatUnsupportedAttribute.cs`), skipped if `DetectCapabilitiesAsync` reports the named
  format unsupported. Not yet exercised by any test — T-F50's format-specific fixtures will use it.

### When to Run

- Requires Windows with `tar.exe` present (Windows 10 1803+)
- RAR5 and 7z tests require Windows 11 23H2+ (tar.exe with libarchive 3.7+)
- CI: run separately from unit tests; tag as `[Integration]` so `dotnet test tests/Archiver.Core.Tests` remains fast

---

## Tar Fixtures (v1.3+)

Located at `tests/Archiver.Core.Tests/Fixtures/tar/`.

Generated by `GenerateFixtures` project (where tar.exe can create them). Manual fixtures needed for formats only tar.exe can read (RAR, certain 7z variants).

| Fixture | Notes |
|---------|-------|
| `valid_tar.tar` | Plain tar, no compression |
| `valid_tar_gz.tar.gz` | gzip compressed |
| `valid_tar_bz2.tar.bz2` | bzip2 compressed |
| `valid_tar_xz.tar.xz` | xz compressed |
| `valid_tar_zst.tar.zst` | zstd compressed — requires Win 11 23H2+ tar.exe |
| `valid_tar_lzma.tar.lzma` | lzma compressed |
| `valid_7z.7z` | 7z archive — requires Win 11 23H2+ tar.exe |
| `valid_rar4.rar` | RAR4 — requires Win 11 23H2+ tar.exe |
| `valid_rar5.rar` | RAR5 — requires Win 11 23H2+ tar.exe |
| `corrupted_tar.tar` | Intentionally corrupted header |
| `zipslip_tar.tar` | Path traversal entry (`../../evil.txt`) |
| `bomb_tar.tar.gz` | Highly compressed, triggers bomb detection |
| `unicode_cyrillic.tar` | Cyrillic filename entries |
| `unicode_emoji.tar` | Emoji filename entries |

To regenerate:
```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```

---

## Manual Smoke Test Cycle (Full Stack)

Ordered simplest → most complex. Confirms Core, Shell, ShellExtension (COM), and the WinUI app
all work end-to-end after a change — not just `dotnet test`. Run before a release or after
touching shell-triggered/UI behavior (see `CLAUDE.md`'s Workflow Tips). Last run in full:
2026-07-06.

1. **Build core (fast fail)**
   ```
   dotnet build src/Archiver.Core
   ```
2. **.NET test suite**
   ```
   dotnet test --filter "Category!=Slow"
   ```
3. **C++ Google Test suite** (rebuild only if the exe is missing or C++ source changed)
   ```
   tests\Archiver.ShellExtension.Tests\bin\x64\Debug\Archiver.ShellExtension.Tests.exe
   ```
4. **Shell context menu (Explorer, manual)** — requires the installed MSIX to match the current
   commit (check `Get-AppxPackage *Pakko*` version against the `Package.appxmanifest` version at
   HEAD; re-run `Deploy.ps1` only if they've diverged). Use a scratch folder, verify actual disk
   output (not just that a dialog appeared), clean up after:
   - Folder → right-click → Pakko → `Add to "<name>.zip"` → verify entries keep their path
     prefix (T-F75)
   - Single non-zip file → same → verify archive created
   - `.zip` → `Extract here` → verify smart-folder logic (wraps in a subfolder when the archive
     has multiple root items)
   - `.zip` → `Extract to folder...` → verify `<name>\` subfolder created
   - `.zip` (valid) → `Test archive` → "No errors detected in the archive(s)."
   - `.zip` (use the `corrupted_crc_stored.zip` fixture) → `Test archive` → CRC-32 mismatch
     message naming the entry and both hash values
   - Mixed selection (zip + non-zip) → confirms `Add to "..."` and `Test archive` both appear,
     Test archive after the primary action (context-menu ordering rule, `CLAUDE.md`)
5. **WinUI app (manual)** — launch via
   `shell:AppsFolder\PavloRybchenko.Pakko_9hkd8feqeqbr4!App` (not `dotnet run` — WinUI dev builds
   are VS-only, see `CLAUDE.md`). Add files → Archive → Clear → add the resulting archive →
   Extract → diff extracted content against the originals. Known automation quirk: the
   Destination text box does not reliably accept direct keyboard input — use the "..."
   folder-picker button instead.
6. **Slow tests** (optional — before a release or a Zip64-adjacent change)
   ```
   dotnet test --filter "Category=Slow"
   ```

**Known non-bug finding:** `.zip`'s `UserChoice` file association may still point at Windows'
built-in `CompressedFolder` handler even after Pakko is installed. T-F44 registers the
association, but Windows requires explicit user opt-in via Settings → Default apps before
double-click routes to a non-built-in handler — this is a Windows security mechanism (UserChoice
hash), not a Pakko defect.

---

## Rules

- No `Thread.Sleep` — use `await Task.Delay` if needed
- Each test cleans up via `TempDirectory.Dispose()`
- No test depends on another test's state
- `dotnet test` never writes files outside `%TEMP%`
