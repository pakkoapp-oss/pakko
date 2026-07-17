# TESTING.md — Test Plan

Covers `Archiver.Core` only. UI layer (`Archiver.App`) is not unit-tested in v1.0.

---

## Running Tests

```bash
# Run all tests (skips T-F20's Zip64 Slow tests AND the VeryLarge tier — see below)
dotnet test tests/Archiver.Core.Tests --filter "Category!=Slow&Category!=VeryLarge"

# With verbose output
dotnet test tests/Archiver.Core.Tests --filter "Category!=Slow&Category!=VeryLarge" --logger "console;verbosity=normal"

# Zip64 tests only — real multi-second cost, not run by default
dotnet test tests/Archiver.Core.Tests --filter "Category=Slow"

# The one genuinely oversized (>4 GiB) test — on demand only, never part of Category=Slow
dotnet test tests/Archiver.Core.Tests --filter "Category=VeryLarge"
```

**Plain `Category!=Slow` alone is NOT the correct "default" filter — it does not exclude
`VeryLarge`-tagged tests, since they aren't tagged `Slow` (confirmed empirically 2026-07-17: a
bare `Category!=Slow` run picked up T-F114's two one-large-file tests, which are exactly the ones
meant to be on-demand-only). Always combine both: `Category!=Slow&Category!=VeryLarge`.**

**Three tiers, not two — `Category` alone isn't enough to describe cost here:**
- **(no trait)** — default fast unit tests, always run.
- **`[Trait("Category", "Slow")]`** — genuinely expensive but bounded (seconds, not minutes); run
  before a release or when touching Zip64/compression-path-adjacent code.
- **`[Trait("Category", "VeryLarge")]`** — the one >4 GiB Zip64 test
  (`ArchiveAndExtract_FileOver4Gb_RoundTripsWithoutError`, T-F20) and T-F114's two one-large-file
  (~300 MB) performance scenarios. Deliberately **not** included in `Category=Slow` — run only on
  explicit demand via `Category=VeryLarge`, per user request: the "short" perf scenarios below
  (many-small-files, hybrid) should always run under a normal `Category=Slow` pass; only the
  genuinely large ones need a separate, deliberate opt-in.

`ZipArchiveServiceZip64Tests.cs` (T-F20) creates 65,600 real files (the `Slow`-tagged tests) and a
>4 GiB sparse file (the `VeryLarge`-tagged test) to exercise Zip64's entry-count and large-size
boundaries — the sparse-file test itself is fast wall-clock-wise (no real disk I/O for the all-zero
content), but is still gated behind `VeryLarge` since a multi-GiB round trip is the kind of thing
that shouldn't run just because someone ran the "Slow" tier.

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

`TarSandboxedServiceExtractTests.cs` (14 tests; renamed from `TarProcessServiceExtractTests.cs`
when T-F52 replaced `TarProcessService` with the sandboxed service — same test bodies, only the
`_sut` type changed) exercises `TarSandboxedService.ExtractAsync` against the real system
`tar.exe`: round-trip extraction, rename-conflict cases, MOTW propagation, selective extraction
(files-only and folder-with-descendants), compression-bomb handling, and whole-archive-reject
cases (path-traversal entry, ADS/reserved-name entry, truncated tar, and a symlink-entry escape —
the last is a regression test for the exploit documented in `DECISIONS.md`'s T-F49 entry).
Fixtures are self-generated per-test via `TarBuilder.cs` (raw USTAR bytes, no third-party tooling
— needed since a `..`-entry or a symlink escape target isn't a representable real source path)
rather than a prebuilt corpus; T-F50 still owns the full multi-format fixture set below.
`TarSandboxedServiceCompressedFormatsTests.cs` and `TarSandboxedServiceExternalFormatsTests.cs`
were renamed the same way.

### Sandbox subsystem tests (T-F52, v1.4)

Exercise the real Win32 AppContainer/ACL/Job-Object/Authenticode APIs directly — no mocks (this
repo's convention), every assertion is against real OS behavior:

- `tests/Archiver.Core.Tests/Services/Sandbox/` — pure/fast unit tests: `SandboxedProcessLauncherTests.cs`
  (raw `CreateProcessW` launcher, no AppContainer), `SecurityCapabilitiesAttributeListTests.cs`
  (`tar.exe --version` inside a real AppContainer), `AppContainerProfileTests.cs` (profile
  create/reuse/delete, using its own throwaway test profile name — never the shared production
  `Pakko.TarSandbox` profile — plus a real forced-failure case: a >64-char profile name makes
  `CreateAppContainerProfile` throw `InvalidOperationException`, the exact failure shape
  `TarSandboxScope` now rewraps as `SandboxSetupException`), `QuarantineStagingTests.cs`
  (hardlink/copy staging), `TarSignatureVerifierTests.cs` (real tar.exe passes, an unsigned decoy
  and a catalog-signed system binary both correctly fail).
- `tests/Archiver.Core.IntegrationTests/` — `QuarantineAclTests.cs` (3 tests: a granted quarantine
  lets a real sandboxed extraction succeed; an un-granted sibling folder is denied — the actual
  security proof; and a nonexistent path makes `GetNamedSecurityInfoW` throw
  `InvalidOperationException`, the same forced-failure shape as above), `TarSandboxScopeTests.cs`
  (4 tests: pre-scan + extraction in one scope,
  listing-only scope creates no `out\`, Dispose cleans up but never touches the shared profile),
  `SandboxJobObjectTarExtractionTests.cs` (2 tests: `.tar.xz`/`.tar.zst` extraction survives
  `ActiveProcessLimit = 1`), and `TarSandboxedServiceSandboxBehaviorTests.cs` (3 tests — the
  acceptance-criteria proofs: a write outside the quarantine is denied, a spawned child process
  under the Job Object never completes, and a socket-connect attempt fails inside the
  AppContainer while succeeding unsandboxed against the same listener).

No `[Trait("Category", "Sandbox")]` was added — per-test wall time measured at 44–172ms (profile
reuse means no registry-provisioning cost per test), so there was nothing to gain from a
filterable-but-not-excluded category; add one later only if a real cost is measured.

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

## Performance/Regression Tests vs. a 7-Zip Reference (T-F114, v1.4+)

Project: `tests/Archiver.Core.PerformanceTests/` — `CompressionPerformanceTests.cs`, 6 tests
(archive + extract × one-large-file / many-small-files / hybrid). The many-small-files and hybrid
scenarios (4 tests) are tagged `[Trait("Category", "Slow")]`; the one-large-file scenarios (2
tests, ~300 MB fixture) are tagged `[Trait("Category", "VeryLarge")]` instead — deliberately
**not** part of the default Slow run, on demand only (see "Running Tests" above for why).

```bash
# Runs alongside Zip64's Slow tests — same filter, no new mechanism (4 of the 6 perf tests)
dotnet test --filter "Category=Slow"

# The two one-large-file scenarios only — alongside Zip64's >4 GiB test
dotnet test --filter "Category=VeryLarge"
```

**Why this exists:** catches a code change that silently makes Pakko's ZIP compression/extraction
meaningfully slower, without a flaky absolute-time threshold that breaks the moment the test runs
on a different machine. **This is distinct from `GenerateFixtures`' small, committed correctness
fixtures** — this suite's fixtures (a 300 MB file, 5,000 small files, a hybrid mix) are generated
fresh into a `TempDirectory` at test-run time and never committed to git, following
`ZipArchiveServiceZip64Tests`' precedent, not `GenerateFixtures`'.

**Mechanism:** each test runs one discarded warmup pass, then one timed pass, for both Pakko
(`ZipArchiveService`) and a vendored `7za.exe` reference — back-to-back, on the same machine, in
the same test method — then asserts on the *ratio* between their elapsed times against a
per-scenario calibrated constant with a 3x tolerance multiplier. This is the only pattern of the
three researched precedents (BenchmarkDotNet, criterion.rs, benchstat) that generalizes to an
arbitrary, never-before-seen machine — see `DECISIONS.md`'s T-F114 entry for the full research and
the observed baseline ratios. Extraction scenarios extract from one shared reference ZIP (built
once via 7za, untimed) so both engines process byte-identical input.

**7za.exe is a test-only, dev-time dependency** (`tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`,
pinned + hash-verified + LGPL-attributed, see that folder's `NOTICE.md`) — never shipped in the
MSIX, distinct from `CLAUDE.md`'s "No 7-Zip"/"zero third-party dependencies" hard constraint, which
governs the shipped product only. Every `7za.exe` launch runs under a basic sandbox — a Job Object
(`SandboxJobObject`, reused from tar.exe's own sandbox subsystem: no child-process creation, RAM/CPU
caps) via `SandboxedProcessLauncher`, but deliberately **without** the AppContainer/quarantine
layer tar.exe gets, since that layer exists to contain untrusted *input* (not applicable — the
fixture is Pakko's own generated content) and would add ACL/staging overhead that could bias the
very timing being measured. See `SevenZipRunner.cs`, `SECURITY.md`, and `DECISIONS.md`'s T-F114
entry for the full rationale.

**Failure-handling — different from Zip64's Slow tests, read this before treating a failure as a
real regression:** a Zip64 test failure is always a real bug (deterministic, no timing involved).
A perf-test failure carries a nonzero chance of being a one-off machine hiccup (background scan,
thermal throttling, a stray process) — **rerun once before treating a failure as a real
regression.** A *repeatable* failure across reruns is the real signal. Scope is ZIP only (no
tar-family) — `TarSandboxedService`'s AppContainer/sandbox overhead would make a shared tolerance
band meaningless for that path; see `DECISIONS.md` if that's ever revisited.

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
   dotnet test --filter "Category!=Slow&Category!=VeryLarge"
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
6. **Slow tests** (optional — before a release or a Zip64/compression-path-adjacent change; now
   also runs T-F114's 7-Zip-reference performance suite alongside Zip64's — see above for its
   rerun-once-before-treating-as-regression rule)
   ```
   dotnet test --filter "Category=Slow"
   ```
7. **VeryLarge tests** (optional, on demand only — not part of a normal release cycle; run when
   deliberately verifying Zip64's >4 GiB path or T-F114's one-large-file perf scenarios)
   ```
   dotnet test --filter "Category=VeryLarge"
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
