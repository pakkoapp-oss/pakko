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

**`HashPerformanceTests.cs` (T-F128 follow-up, 2026-07-20)** extends this same pattern to
`FileHashService`, using `7za h -scrcCRC32` (real 7-Zip's own hash command, same `HashCalc.cpp`
algorithm this project's own DataSum/NamesSum reproduce) as the reference instead of ZIP
archive/extract. Two scenarios: `HashAsync_OneLargeFile` (300 MB, `Category=VeryLarge`, reuses
`PerformanceFixtures.CreateOneLargeFileFolder`) and `HashAsync_ManyFilesAndFolders`
(`Category=Slow`, new `PerformanceFixtures.CreateManyFilesAndFoldersFolder` — 300 subfolders × 10
files, the first fixture in this project with real nested subfolders). This suite is what found a
real ~9x CRC-32 slowdown against 7-Zip, root-caused to `Crc32.cs`'s hashing algorithm itself (not
I/O), improved to ~6.4x via a slice-by-8 rewrite, then to ~1.35x typical (worst observed ~2.9x,
still comfortably inside the 3x tolerance) via genuine intra-file parallelism — `Crc32.Combine`
(a zlib `crc32_combine` reimplementation) lets `FileHashService` hash one large file's chunks on
separate threads and fold the results back together in order. See `DECISIONS.md`'s T-F128 entry
for the full investigation, including a real `ThreadPool` ramp-up stability bug found and fixed
along the way, and why the remaining gap (likely 7-Zip's own hardware-accelerated CRC-32) wasn't
chased further this round. New `SevenZipRunner.Hash(path, algorithm, recursive)` method alongside
the existing `Archive`/`Extract`/`Test` wrappers.

---

## T-F35 Parallel SingleArchive Pipeline Tests (v1.4+)

Three test classes cover the gated `Archiver.Core/Services/Zip/` subsystem (see `ARCHITECTURE.md`'s
own section for it, `DECISIONS.md`'s T-F35 entry for the design rationale and two real bugs these
tests caught before shipping):

- `DosDateTimeTests` (`Archiver.Core.Tests/Services/Zip/`) — round-trip encode/decode, 1980/2107
  clamping, and a byte-for-byte comparison against a real `ZipArchiveEntry`'s own DOS date/time
  encoding.
- `ParallelSingleArchiveWriterTests` (`Archiver.Core.Tests/Services/Zip/`) — two layers:
  - Unit-level, against `RunPipelineAsync` directly with injectable compress delegates (not real
    file I/O): enqueue-order-not-completion-order write proof, a whitebox concurrency-ceiling test
    (a controllable blocking gate proves at most `windowCapacity` compress tasks ever run
    concurrently — this caught a real bug: the bounded channel alone did not bound concurrency),
    already-cancelled-token graceful no-op, mid-flight cancellation with no orphaned background
    tasks left running afterward, and per-file error isolation.
  - Real-file-I/O level, against `WriteAsync` with actual files/temp directories (added when the
    original "large files stream sequentially" design was replaced by per-worker temp-file
    compression — no more file-size ceiling): successful cleanup after a normal run (no leftover
    `*.chunk-*.tmp` files), cleanup after a locked-file compression error with the rest of the
    batch still archived, and cleanup after mid-flight cancellation — this last one caught a real
    concurrency bug (temp-file cleanup racing against a still-running straggler task) that failed
    intermittently only under full-suite parallel load, never in isolation; fixed by awaiting
    every dispatched compress task before sweeping leftover temp files.
- `ZipEntryWriterCompatibilityTests` (`Archiver.Core.PerformanceTests/` — lives there specifically
  to reuse the vendored, hash-verified `7za.exe` binary rather than duplicating it into a second
  test project; a correctness suite, not a performance one, despite the location) — proves the
  hand-rolled ZIP bytes are independently readable, not just self-consistent: a
  `System.IO.Compression.ZipFile.OpenRead` round trip, an independent `7za.exe t` integrity check
  (this caught a real Zip64 local-header field-offset swap bug — `ZipFile.OpenRead` accepted the
  corrupted bytes silently, `7za.exe` rejected them outright), a raw structural byte parser written
  independently of `ZipEntryWriter`'s own code, and a forced-Zip64 test (deliberately-synthetic
  declared sizes on a tiny real stream, exercising `WriteCompressedEntryFromStreamAsync`'s Zip64
  path without needing gigabytes of test data — verified via the raw parser only, since a real
  reader would reasonably reject content whose actual length doesn't match its declared length).
  The shared `BuildMixedArchiveAsync` helper (reused by the three tests above) also includes a
  zero-byte real file compressed at `CompressionLevel.Optimal`, added after a real on-device
  NanaZip cross-check found `ZipEntryCompressor` tagged empty files as `Deflate` even though
  `DeflateStream` writes 0 output bytes for zero input — invalid to a real deflate reader though
  invisible to .NET's own lenient one; see `DECISIONS.md`'s T-F35 follow-up entry. A separate test,
  `ArchiveAsync_RealFolderWithEmptyFilesAndFoldersAboveParallelThreshold_PassesSevenZipIntegrityCheck`,
  reproduces that exact bug report end-to-end through the real public `ZipArchiveService.ArchiveAsync`
  API (not just `ZipEntryCompressor`/`ZipEntryWriter` called directly) — a real folder with 70+
  files, several genuinely empty ones, and an empty subdirectory, above the parallel threshold,
  verified with `7za.exe t`. Confirmed to actually catch the regression (temporarily reverted the
  fix and re-ran — both this test and the shared-helper one failed with the exact same "Data Error"
  NanaZip reported).
- `ZipArchiveServiceParallelPipelineTests` (`Archiver.Core.Tests/Services/`) — the same properties
  re-verified through the real public `ArchiveAsync` API at gate-triggering scale (120 files, above
  `ParallelPipelineFileCountThreshold`'s 64): byte-identical/entry-order-identical determinism,
  already-cancelled and mid-flight cancellation, one locked file mid-batch, and a mixed
  small+large-file archive in one run.

All four run as part of the default `dotnet test --filter "Category!=Slow&Category!=VeryLarge"`
pass (untagged, fast) — no new Slow/VeryLarge tier was needed for T-F35 itself.

---

## Archiver.CLI.Tests (v1.5, T-F09)

Project: `tests/Archiver.CLI.Tests/`. Two layers, both run as part of the default
`dotnet test --filter "Category!=Slow&Category!=VeryLarge"` pass — neither carries multi-second
cost, so no `Slow`/`VeryLarge` tag was needed.

**Unit tests** (no process spawn) — `CliArgumentParserTests.cs` (every command's happy path, every
`-mx`/`-ao`/`-t` value, and at least one real case of each of `CLI.md`'s three unknown-input
categories), `CliCompressionLevelMapperTests.cs` (boundary values at every `-mx` bucket edge),
`CliHelpTextTests.cs` (every command/switch mentioned, and the printed compression-level table
checked against the real bucket boundaries so it can't silently drift from the mapper),
`CliEntryFormatterTests.cs` (the `l` command's TSV row formatting, including the `-`/`f`/`d`
sentinels for nullable/tar-family fields).

**Subprocess layer** (`Subprocess/CliSubprocessTests.cs`) — genuinely new to this repo, per
`TASKS.md`'s T-F09 acceptance criteria: unlike `Archiver.Shell` (whose arguments are only ever
generated programmatically by the COM shell extension, so unit-testing its parser class alone is
sufficient), a human or script types `Archiver.CLI`'s arguments directly — its real exit code and
stdout/stderr text *are* the public contract. `CliProcessRunner.cs` launches the actual built
`pakko.exe` (the project's built `AssemblyName`) via plain `System.Diagnostics.Process` (deliberately not `Archiver.Core`'s
internal `SandboxedProcessLauncher` — that machinery sandboxes *untrusted* external binaries via
Job Objects/AppContainer; `pakko.exe` is a trusted, first-party sibling build artifact, not
something that needs containing) and resolves the exe path by walking up from
`AppContext.BaseDirectory` to `windows-archiver-wrapper.sln`, then mirroring the same trailing
`<Configuration>\<TFM>` segments onto `src/Archiver.CLI/bin/...` — robust to Debug/Release without
hardcoding either. `CliFixtureFiles.cs` builds its own fixtures once per test run (a ZIP via
`ZipFile.CreateFromDirectory`, and — when `C:\Windows\System32\tar.exe` is present — a `.tar.gz`
by shelling directly to it) rather than depending on `Archiver.Core.IntegrationTests` as a project
reference, keeping this test layer decoupled; tar-family scenarios are gated behind a local
`RequiresTarExeAttribute` (duplicated from `Archiver.Core.IntegrationTests`' `IntegrationAttribute`
on purpose, same reasoning). Covers each command's happy path with real output verified on disk/in
stdout, plus one real instance of each of the three three-way-rule categories with the real exit
code and real stderr substring asserted — `pakko.exe` must be built (`dotnet build
src/Archiver.CLI` or a prior `dotnet test`/`dotnet build` at the repo root) before running this
layer, or `CliProcessRunner` throws with a clear message naming the expected path.

**T-F116 additions (`-si`/`-so` streaming):** `CliStreamStagingTests.cs` (new unit-test file, no
process spawn) covers `CliStreamStaging.StreamSingleFileAsync`'s three outcomes — exactly one file
copied byte-for-byte, zero/multiple files named in the error message, and a destination `Stream`
that throws `IOException` on write (simulating a broken downstream pipe) returning a clean error
instead of propagating the exception. `CliProcessRunner.RunWithBinaryStdio` extends the subprocess
layer with raw byte stdin-in/stdout-out capture (a text `string` capture would corrupt or mask the
exact byte comparisons T-F116's round-trip tests need). `CliSubprocessTests.cs` adds a full
`a -so` → `x -si` byte round trip, `-so` against the `valid.7z`/`valid.rar` fixtures, a named-count
error for `-so` against a multi-file archive, `-si`/`-so` graceful handling of empty/garbage input,
and — the test that actually proves the documented shell recipe works, not just .NET's own
`Process` plumbing — `CmdPipe_ArchiveSoToExtractSi_RoundTripsBytesExactly`, which launches
`cmd.exe /c "pakko a -so ... | pakko x -si ... > log"` as the subprocess under test. See
`DECISIONS.md`'s T-F116 entry for the empirical PowerShell-pipe findings that shaped this test
list, and for why a real-subprocess broken-pipe simulation was tried first and abandoned as racy.

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
