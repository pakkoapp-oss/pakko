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

**Three cost tiers, not two, plus Fuzz — `Category` alone isn't enough to describe cost here:**
- **(no trait)** — default fast unit tests, always run.
- **`[Trait("Category", "Slow")]`** — genuinely expensive but bounded (seconds, not minutes); run
  before a release or when touching Zip64/compression-path-adjacent code.
- **`[Trait("Category", "VeryLarge")]`** — the one >4 GiB Zip64 test
  (`ArchiveAndExtract_FileOver4Gb_RoundTripsWithoutError`, T-F20) and T-F114's two one-large-file
  (~300 MB) performance scenarios. Deliberately **not** included in `Category=Slow` — run only on
  explicit demand via `Category=VeryLarge`, per user request: the "short" perf scenarios below
  (many-small-files, hybrid) should always run under a normal `Category=Slow` pass; only the
  genuinely large ones need a separate, deliberate opt-in.
- **`[Trait("Category", "Fuzz")]`** (T-F240) — `tests/Archiver.Core.Tests/Fuzz/`. Seeded mutational
  fuzzing, no fuzzing library: `ByteMutator` mutates every fixture ZIP (except EICAR), half the
  offsets just after a ZIP signature. Targets: `RawZipEntryLocator.LocateAll` may throw only
  `IOException`/`InvalidDataException`; `ListEntriesAsync`/`TestAsync`/`ExtractAsync` never throw,
  and extraction writes nothing outside the destination; `LaunchArguments.TryParse` never throws.
  Runs in the default filter at 10 iterations per seed input with a fixed seed (a few seconds);
  the nightly `canary-fuzz` job runs 500 with a fresh seed. Knobs: `PAKKO_FUZZ_ITERATIONS`,
  `PAKKO_FUZZ_SEED`, `PAKKO_FUZZ_ONLY_ITERATION` (replays one case from a failure message),
  `PAKKO_FUZZ_OUTPUT` (default `%TEMP%\pakko-fuzz`; each input is written as `current-*.bin` before
  it runs, a failure is kept as `failure-*.bin`). A set but unparsable knob fails the run.
  ```powershell
  $env:PAKKO_FUZZ_SEED='777'; $env:PAKKO_FUZZ_ITERATIONS='300'
  dotnet test tests/Archiver.Core.Tests --filter "Category=Fuzz"
  ```
  First finding (seed 777): a local-header offset past the end of the archive escaped
  `LocateAll` as `ArgumentOutOfRangeException`; regression test in `RawZipEntryLocatorTests`.

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
<TargetFramework>net10.0</TargetFramework>  <!-- NOT net10.0-windows — pure .NET -->
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

**T-F188 (ZIP password reading) real crypto fixtures** — password `"testpassword"` for all of
them; regeneration commands are in `GenerateFixtures/Program.cs`'s header comment, not run
automatically (same manual-tool pattern as `encrypted_aes256.zip` above):
- `encrypted_aes128.zip`, `encrypted_zipcrypto_real.zip` — real WinZip AES-128 / real PKWARE
  ZipCrypto, generated via the vendored `7za.exe` (T-F114). `encrypted_zipcrypto_real.zip` is
  distinct from the older `encrypted_zipcrypto.zip` above, which only sets the encryption flag
  over fake bytes for T-25's *detection* tests — it is not real cipher output and cannot be
  decrypted.
- `mixed_encrypted_and_plain.zip` — one plain entry (`readme.txt`) + one AES-256-encrypted entry
  (`compressible.txt`) in the same archive, for the no-second-extraction-path invariant.
- `encrypted_aes256_ae1.zip` — **SYNTHETIC**, not a `7za.exe` output. `7za.exe` (26.02) only ever
  emits WinZip AE-2; this fixture is `encrypted_aes256.zip` byte-patched (extra-field version
  2→1, plus the real CRC-32 of `compressible.txt` injected into both the local and central
  headers) to exercise the AE-1 code path, where the header CRC-32 is real rather than zeroed.
  Cross-checked against the vendored `7za.exe` itself (still decrypts correctly after patching).
- `encrypted_aes256_tampered.zip` — **SYNTHETIC**, one ciphertext byte flipped in
  `encrypted_aes256.zip` (inside the AES-CTR data, not the salt/password-verification prefix) —
  the real password still verifies, but the HMAC-SHA1 authentication tag must reject the result.
  Cross-checked: the vendored `7za.exe` itself also reports `"Data Error"` against this fixture.
- T-F194 (all password `testpassword`): `encrypted_zipcrypto_store.zip` (ZipCrypto, **Store** —
  `wrong103` collides with its one-byte password check, driving the "never report garbage as Clean"
  test), `encrypted_aes256_bzip2.zip` (AES-256 over BZip2 — the unsupported-method-under-encryption
  path), and `encrypted_aes256_eicar.zip` (**SYNTHETIC** — EICAR piped into `7za.exe` from stdin so
  no plaintext EICAR ever hit disk, then its local header patched off 7za's stdin-only Zip64
  sentinels). Regeneration commands in the GenerateFixtures header; rationale in
  `docs/DECISIONS.md`'s T-F194 entry.
- T-F193 phase 0: `encrypted_aes256_stdin_zip64local.zip` (real `7za.exe` output from stdin, so
  its local header carries 0xFFFFFFFF Zip64 size sentinels). `Helpers/Zip64DirectoryRewriter.cs`
  rewrites a small fixture into the full Zip64 directory layout (Zip64 EOCD + locator, Zip64 extra
  in central records) — validated with the vendored `7za.exe` — for `RawZipEntryLocatorTests`.

**T-F193 (encrypted ZIP creation) tests.** `EncryptedZipStreamingReaderTests` (a 64 MiB 7za-made
AES entry reads back within an 8 MB allocation bound; a > `int.MaxValue` entry is `VeryLarge`);
`ZipArchiveServiceEncryptTests` (round trip through Pakko's reader, AE-2 header fields, fresh salt
per entry, wrong password, a cancelled prompt creates nothing and returns `Success = false` —
the App deletes sources only on success, and that App-side step has no automated test — plus an
existing destination left untouched, empty/non-ASCII/control-character/99-vs-100-character
passwords); `EncryptionPasswordRuleTests` (boundaries 0x1F/0x20/
0x7F/0x80, 99/100); `ZipEncryptionCompatibilityTests` in `Archiver.Core.PerformanceTests` (the
independent reader: `7za.exe t`/`x` on Pakko archives byte-exact, and Pakko on 7za-written ones);
a TAR-plus-password rejection in `TarSandboxedServiceCompressTests`. Mutation-checked: fixed salt,
HMAC over plaintext, the 99 clamp, the TAR guard, both rule boundaries.

**T-F337 (byte-level check of encrypted ZIPs).** `ZipEncryptionByteLevelTests` reads the archives
`ArchiveAsync` writes with a ZIP and AE-2 reader written in the test (BCL `Rfc2898DeriveBytes`,
`HMACSHA1`, AES-ECB over its own counter blocks; no `Archiver.Core.Services.Zip` type), so a bug
shared by Pakko's writer and reader cannot pass as a round trip. Per entry: both headers, the
`0x9901` field, salt, verification value, authentication code, decrypted content against the
source; sizes 0, 1, 15, 16, 17, 65536, 65537, a file above the in-memory limit, 70 small files,
at Optimal and NoCompression; no salt shared inside an archive or between two runs; no source
marker readable in the archive. Mutation-checked: counter starting at 0 (in the keystream class
the reader shares, so a round trip keeps passing), HMAC over plaintext, fixed salt, strength
code, real method.

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
Fixtures are self-generated per-test via `TarBuilder.cs` (raw USTAR bytes, or GNU magic via
`gnuMagic` (T-F305), no third-party tooling
— needed since a `..`-entry or a symlink escape target isn't a representable real source path)
rather than a prebuilt corpus; T-F50 still owns the full multi-format fixture set below.
`TarSandboxedServiceCompressedFormatsTests.cs` and `TarSandboxedServiceExternalFormatsTests.cs`
were renamed the same way.

### Sandbox subsystem tests (T-F52, v1.4)

Exercise the real Win32 AppContainer/ACL/Job-Object/Authenticode APIs directly — no mocks (this
repo's convention), every assertion is against real OS behavior:

- `tests/Archiver.Core.Tests/Services/Sandbox/` — pure/fast unit tests: `SandboxedProcessLauncherTests.cs`
  (raw `CreateProcessW` launcher, no AppContainer; fix phase 4: an unrelated inheritable pipe is
  not inherited by the child — `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`; a cancelled child has exited
  when the call returns), `SandboxedProcessLauncherHandleLeakTests.cs` (a failing launch leaks no
  handles — its own `DisableParallelization` collection, since it counts the whole process's
  handles), `AppContainerLaunchTests.cs` (`tar.exe --version` inside a real AppContainer),
  `TarCommandLineEncodingTests.cs` (T-F266: best-fit/unrepresentable strings per explicit code
  page, the launcher refuses before creating a process), `TarSandboxScopeLimitMessageTests.cs`
  (T-F239 messages, the memory/CPU limit rules), `TarHeaderNamesTests.cs` (T-F305: the header-byte
  UTF-8 check, incl. GNU `L`/pax records and malformed headers giving "unknown"; T-F310: the same through gzip, with the size limit and a damaged stream), `AppContainerProfileTests.cs` (profile
  create/reuse/delete, using its own throwaway test profile name — never the shared production
  `Pakko.TarSandbox` profile — plus a real forced-failure case: a >64-char profile name makes
  `CreateAppContainerProfile` throw `InvalidOperationException`, the exact failure shape
  `TarSandboxScope` now rewraps as `SandboxSetupException`), `TarSignatureVerifierTests.cs` (real
  tar.exe passes, an unsigned decoy and a catalog-signed system binary both correctly fail).
- `tests/Archiver.Core.IntegrationTests/` — `QuarantineAclTests.cs` (3 tests: a granted quarantine
  lets a real sandboxed extraction succeed; an un-granted sibling folder is denied — the actual
  security proof; and a nonexistent path makes `GetNamedSecurityInfoW` throw
  `InvalidOperationException`, the same forced-failure shape as above), `TarSandboxScopeTests.cs`
  (pre-scan + extraction in one scope, listing-only scope creates no `out\`, Dispose cleans up but
  never touches the shared profile; T-F233/T-F248: the original's SDDL is unchanged, an
  `OWNER RIGHTS` read-only archive opens, a read-only archive keeps its attribute and link count,
  the archive cannot be written during the scope, a file open for writing is refused),
  `SandboxStdinTests.cs` (the archive as stdin works inside the AppContainer with no grant on its
  folder, for `.tar.gz` and 7z; by path it fails), `TarSandboxConcurrencyTests.cs` (concurrent
  `EnsureExists` never fails; a `Slow` test runs 8 workers x 15 scopes),
  `SandboxJobObjectTarExtractionTests.cs` (`.tar.xz`/`.tar.zst` extraction survives
  `ActiveProcessLimit = 1`; T-F239: the job reports memory, CPU-time or no limit hit),
  `TarSandboxedServiceNameEncodingTests.cs` (T-F204/T-F215/T-F305: UTF-8, OEM and pax names under
  POSIX and GNU magic, a name outside the code page, a backslash traversal, a creation error without `-v` lines — expectations
  built from this machine's code pages), and `TarSandboxedServiceSandboxBehaviorTests.cs` (3 tests
  — the acceptance-criteria proofs: a write outside the quarantine is denied, a spawned child
  process under the Job Object never completes, and a socket-connect attempt fails inside the
  AppContainer while succeeding unsandboxed against the same listener).
  `TarSandboxedServiceDuplicateNamesTests.cs` (T-F171: two same-named entries under
  Rename/Skip/Overwrite/Ask, three copies, case-only names, a selected duplicate, a name that is also
  a pattern; creation from two same-named folders and a file plus a folder, a source named `@x.txt`).
  `TarSandboxedServiceMemberPatternTests.cs` (T-F284: a selected bracket-named entry next to the name
  its pattern would match; `*`/`?` only in `TarMemberPatternTests.cs`, since Windows cannot hold such
  a file). `TarSandboxedServiceEmptyDestinationTests.cs` (T-F309: a refused or cancelled run removes
  the destination it created, never one that existed). `TarCollisionStagingTests.cs` (T-F286: the
  stale-staging sweep over real junctions, with a fake process-alive check).
  `TarSandboxedServiceNameEncodingTests.cs` also runs its GNU-magic layouts under gzip (T-F310).
  `DriveRootSourceTests.cs` (T-F285, T-F344: a `subst` drive over a temp folder, made and removed
  by the test. TAR: its root and a file directly in the root are archived and read back; a
  Hidden+System entry, a junction and the run's own archive in the root are left out. ZIP: both
  writers name a root's entries with no leading "/", and leave out Hidden+System entries of the
  root's own level only (T-F345); an empty root says `NothingToArchive` in ZIP and TAR).
  Pure parts (`FindDuplicateGroups`, `DirectoryJunction`) are in
  `Archiver.Core.Tests/Services/TarDuplicateNamesTests.cs`.

No `[Trait("Category", "Sandbox")]` was added — per-test wall time measured at 44–172ms (profile
reuse means no registry-provisioning cost per test), so there was nothing to gain from a
filterable-but-not-excluded category; add one later only if a real cost is measured.

**T-F195 correction (2026-09-24):** the remaining cross-*process* flakiness was in fact a real
product race on the shared `%TEMP%\PakkoTarSandbox` parent's ACL, now fixed and pinned by
`QuarantineAclParentRaceTests` (in-process reproduction, 300 scopes x 4 threads). T-F196 added
`ExternalTarFixtureBuilder.CreateCompressedTarOfDotRoot` (the everyday `tar -C dir .` shape) and
three `ExtractAsync_DotRoot*_MatchesPlainArchiveTree` parity tests.

**All 10 test classes in this project are grouped into one xUnit `[Collection("TarSandbox")]`**
(`TarSandboxCollection.cs`, `DisableParallelization = true`, added T-F130) — they now run
sequentially relative to each other while still running in parallel with unrelated test
projects/collections. Likely root cause of the CI flakiness documented in `CLAUDE.md`'s "Known
test gaps": concurrent AppContainer profile/Job Object/quarantine ACL calls across different test
classes racing under xUnit's default parallel-by-class execution, not a real product bug and not
a per-test cost problem. **Confirmed fixed via a real CI run on the actual fix** (60/60, 0
failures — not just a local `dotnet test` pass); if flakiness ever recurs here, it's a real
regression worth investigating, not the old expected noise.

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

# The two one-large-file scenarios only — alongside Zip64's >4 GiB test. Release only (T-F272):
# a Debug Core fails their ratio, so the tests fail fast with that message in Debug.
dotnet test -c Release --filter "Category=VeryLarge"
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

## PAR2 Oracles (T-F275, v1.8)

The recovery-data tests check Pakko's PAR2 files against three independent tools, downloaded by
`scripts/Get-Par2Oracles.ps1` into `artifacts/par2-oracles/` (git-ignored; each release asset pinned
to the SHA-256 GitHub publishes). They are GPL-2.0 programs, so they are never committed, and
par2cmdline's own test archives are not copied either — fixtures are generated from Pakko-owned data.

| Tool | Folder | Use |
|---|---|---|
| par2cmdline 1.4.0 | `par2cmdline\par2.exe` | reference packets (same `-s`/`-c` gives the same bytes); `verify --full-hash` (without it 1.4.0 skips the whole-file MD5); repair only for contiguous exponent sets — it takes the first k recovery blocks and fails on a singular choice |
| par2cmdline-turbo 1.5.0 | `par2cmdline-turbo\par2.exe` | repair of any set (retries another block on a singular matrix) |
| MultiPar 1.3.3.6 | `multipar\par2j64.exe` | a second, Windows-native implementation; x64 only (emulated on ARM64); exit code is a bit mask (16 = repaired). From Git Bash set `MSYS_NO_PATHCONV=1`, or `/ss4096` becomes a path |

The tests (T-F275 step 1):

- `Archiver.Core.Tests/Recovery/`: `Gf16Tests` (the specification's constants; multiplication against
  a carry-less multiply), `Gf16RegionTests` (the vector and scalar kernels called directly, against
  the per-word definition, every length modulo 32), `Par2CreatorTests` (packets equal to the golden
  par2cmdline sets in `Fixtures/par2/`, parameters, volume names, every writer shape inside the
  reader's limits), `Par2PacketReaderTests` (hostile packets from `Par2PacketForge`, which recomputes
  the packet MD5 so the field checks are reached), `Par2SetLocatorTests`, `RecoveryMatrixTests` (the
  [2, 48, 237] x {1, 2, 4, 5} case, seen red against first-k), `Par2SliceCombinerTests` (range and
  batch seams forced small), `Par2RepairTests` (flipped, zeroed, cut, appended and deleted data;
  damaged PAR2 files; forged recovery data caught by the final check; cancellation at each phase; an
  unwritable output folder; one `Slow` test with slices larger than one range).
- `Archiver.Core.Tests/Services/ArchiveCreationRouterRecoveryTests` (step 2): a hand-rolled engine
  writes real archive files, so the router's PAR2 step runs for real — a set per created archive
  (also with errors, also SeparateArchives), none for a skipped one, percent and policy refused before
  the engine, a failed set downgrading the sources, cancellation leaving no partial file, one rising
  percent with 100 last, the PAR2 work off the caller's thread, stale volumes removed and a stuck one
  a warning; an archive rewritten without a percent losing its earlier set (also one named `a.par2`),
  two earlier sets, one under each name, and with a percent the earlier set gone even when the new
  one is cancelled or not created,
  while a content match, another file's set, unreadable or mixed files and the policy keep theirs. `GroupPolicyServiceTests` reads `DisableRecoveryData`; `RecoveryDataOptionTests` and
  `CreateModeTextTests` (App.Core) the card's option; `CliArgumentParserTests` and two
  `CliSubprocessTests` the `-rr[N]` switch.
- `Archiver.Core.Tests/Services/ExtractionRouterRecoveryTests` (step 3): a ZIP engine that passes
  or fails named paths, real archives and Pakko's own sets — intact (ZIP, tar.gz, a `.par2` or
  volume path, a set named `a.par2`), flag off and policy unchanged, a `.par2` refused under the
  policy, archive plus its `.par2` checked once, renamed archive and set, a set for another file, a
  ZIP rewritten without a set (warning, not damage), repairable and beyond-repair damage, a missing
  archive, damaged set files, a set left incomplete (no index, no volume, a volume cut short at
  every length, only the writer's temporary files), a locked archive and unlistable folders (errors, never a throw), one
  rising percent ending at 100, cancellation. `CliSubprocessTests` runs `pakko a -rr10` then `t`
  over intact, repairable, beyond-repair and damaged sets for ZIP and tar.gz, a `.par2` path, and
  a ZIP and a tar.gz rewritten without `-rr` (the old set is gone, no recovery verdict), and
  `pakko a -rr100` killed while the set is being written (no false verdict from `t`, the next run
  sweeps the temporary files and writes a set that checks).
- Step 3b, Explorer's "Verify with PAR2". `Archiver.Shell.Tests/ShellCommandsRecoveryTests` runs
  `ShellCommands.VerifyRecoveryAsync` on real sets with the fake UI: a tar-family archive that
  matches, a `.par2` or a volume alone, archive plus index plus volume checked once, the Ukrainian
  text, "Test archive" unchanged, damage a set can and cannot repair (nothing written next to the
  archive), one message for a mixed selection, another file's set, an unreadable set (warning next
  to an archive, error when selected itself), no index, no volume, a volume cut short, only the
  writer's temporary files, a `.par2` whose archive is gone, `DisableRecoveryData`, a set check that still runs under
  `BlockedFormats`, cancel; and on a real ZIP: test passed and set matches, a rewritten ZIP beside its
  old set (a warning, never "damaged"), a damaged ZIP its set can repair.
  `ShellArgumentParserTests` has the switch. C++ `ShellExtUtilsTests` (`AnyPathHasRecoveryData`,
  `IsRecoverySetFileName`, `ListFolderNames`) decides the item's visibility with a fake folder
  listing and once on a real folder: a `.par2` alone without touching the disk, a set under either
  name, volumes without an index, temporary files, names that only start alike, non-archives, the
  16-archive cap, the policy value, a blocked format. `ComLoadTests` pins the item's place after
  Test; `LocalizationTests` and `Archiver.Messages.Tests` hold the 37 translations.
- `Archiver.Core.Tests/Fuzz/Par2FuzzTests` (`Category=Fuzz`): raw mutations of the golden sets, and
  mutations with the packet MD5 recomputed; reader, verifier and repairer must not throw or write
  anything but the output they are given.
- `Archiver.Core.IntegrationTests/Recovery/Par2OracleTests`: `[Par2OracleFact]` skips a test when a
  tool is missing, unless `PAKKO_PAR2_ORACLES_REQUIRED=1`, where the test runs and fails. CI's `test`
  job downloads the tools first (two attempts) and sets the variable; the nightly canary does not
  download them, so these tests skip there. `ArchiveWithRecoveryData_Par2cmdlineVerifiesTheSet`
  runs the whole router path (real ZIP engine) and has par2cmdline verify the result.
- `Archiver.Core.PerformanceTests/Par2PerformanceTests` (`Category=VeryLarge`, Release only):
  512 MiB at 5 % against par2cmdline (Pakko must not be slower; 4.3 s against 9.3 s on 2026-10-09),
  a repair of 100 slices (6.9 s), and a round trip over a file above 4 GiB.

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
  - Allocation guard (T-F271): `CompressSmallFile` on a 4 KiB file allocates under 32 KiB on the
    calling thread (`GC.GetAllocatedBytesForCurrentThread`, isolated from parallel tests) — pins
    the unbuffered read; a 64 KiB `FileStream` buffer per file made it 70 KiB.
- `SmallFileAllocationTests` (`Archiver.Core.Tests/Services/`, T-F271 follow-up) — its own
  `DisableParallelization` collection, since it measures `GC.GetTotalAllocatedBytes` for the whole
  process: hashing (CRC-32, SHA-256) and sequential archiving of 40 x 4 KiB files stay under 64 KiB
  allocated per file. Hashing failed at ~264 KiB per file before `ReadAndDigestAsync` pooled its
  buffer; the archive case is a guard (it passed before the change).
- `ZipArchiveServiceWriterChoiceTests` (`Archiver.Core.Tests/Services/`, T-F352) - the rule that
  picks the writer, on its boundaries (`UsesParallelWriter`), and 9 MiB random files through
  `ArchiveAsync`: two of them take the parallel writer (entries stored, content round-trips),
  one beside a small file stays sequential (as files and inside a folder); same-name sources, a
  locked file and a link give what the sequential writer gives; Cancel leaves nothing; progress
  never goes back; a source or destination disk with a seek penalty keeps the sequential writer.
  The service's disk answer is pinned in these tests (`HasNoSeekPenalty`), since a runner's
  virtual disk may not answer. `DiskSeekPenaltyTests` (`IO/`): a network path, a missing drive and
  a non-path are false and never throw; what a real disk answers is machine-dependent and was
  checked by hand (NVMe: true, `\\localhost\C$`: false). T-F359, same classes:
  `AnyDiskHasSeekPenalty` asks once per folder and about the destination; with a pinned "has a
  penalty" (`HasSeekPenalty`) the first large file to report holds its worker for a second and no
  other large file may report meanwhile, in both archive modes, and the plain archive is
  byte-identical to the one made without it; `OneAtATime_*` (writer) - never two at once, a
  failure or a cancel while waiting does not block the next.
  `ArchiveAsync_FewLargeFiles_WithinToleranceOfSevenZipReference` (`Category=Slow`) adds `7za t`
  and the timing ratio. `ParallelSingleArchiveWriterTests.RunPipelineAsync_CancelledWhileChunks
  AreCopiedIntoTheArchive_...` pins Cancel in the copy phase (the bar at 99%), which a few large
  files make seconds long.
- `LazyTarProbeTests` (`Archiver.Core.Tests/Services/`, T-F350) - through `PakkoServices` with a
  probe-counting `ITarService`: extract/test/list of a ZIP or of a format policy refuses never
  probe, a tar-family archive probes once across operations, Cancel ends the wait for a probe
  still running; `Classify` and `ClassifyAsync` against 72 pinned verdicts with their texts
  (9 formats by magic bytes x 4 policies x 2 capability sets, captured before the change).
  `HelperOperationUiTests.*BeforeTheHelperIsReady*` (T-F351): a clean end before `HelperReady`
  ends the helper without waiting; a result still waits for the window.
  T-F356: `DeferredOperationSessionTests` (the window is `FakeOperationUi`'s) - a fast clean
  operation starts no window, a longer one starts it once with the current archive and the latest
  progress, a prompt or a result starts it at once, Cancel in the window cancels the operation,
  nothing starts after the end, eight prompts racing the timer share one window;
  `HelperOperationUiTests` - the helper is not launched for a fast operation, `Begin` carries the
  elapsed time, `endsWithResult` launches at once, a launch that fails when needed goes to the
  fallback (the 37 older tests pin `StartDelay = 0`); `ShellCommandsTests.Begin_ExtractAndArchiveCanWait_...` (Scan's flag has no test);
  `OperationWindowModelTests.ShowDelayFor_*`; `HandleListProcessTests` - a real child (`ping.exe`)
  on real pipes does not hold an inheritable handle it was not given (fails when the handle list
  is dropped), holds the one it was given, and the command line reads back through
  `CommandLineToArgvW`. Twelve mutants, all caught.
- T-F357 (`ParallelSingleArchiveWriterTests`): a Stored entry at NoCompression, Optimal and Fastest
  (random data) keeps its source open (write and delete refused) and writes no chunk; a
  compressible file and an encrypted incompressible one keep the chunk and free the source; a
  `SourceStored` whose CRC, length or end of file disagrees throws in the drain; Cancel while
  sources wait for the drain releases every one. `ZipArchiveServiceIncompressibleTests.
  ArchiveAsync_LargeStoredAndDeflatedFiles_...` (single and separate): methods, round trip,
  sources deletable when `ArchiveAsync` returns. `WriteAsync_StoredFileOver4GiB_...`
  (`Category=VeryLarge`, on demand) takes this path with Zip64 and reads back with its CRC
  checked - `ZipArchiveServiceZip64Tests`' >4 GiB test takes `ZipArchive` instead. Nine of ten mutants caught; rewriting the chunk
  anyway changes only the bytes written, which the measurement in TASKS.md shows, not a test.
- `ArchiveEntrySecurityMotwTests` (`Archiver.Core.Tests/Services/`, T-F358) - `ReadMotw` returns
  the archive's mark, null for `Disabled`, an unmarked archive or a path that cannot be read;
  `TryWriteMotw` marks any file under `AllFiles` and only the listed extensions under
  `UnsafeExtensionsOnly`, replaces a mark already there, works while the file is still open for
  writing, never throws; `TryPropagateMotw` (tar's path) still keeps the file's time.
  `ZipArchiveServiceExtractTimesTests.ExtractAsync_WithOrWithoutProgressAndMark_...` covers both
  copy branches with and without the mark. Ten of eleven mutants caught; the one that survives
  drops the flush before the time is set - NTFS keeps a time set through a handle when that handle
  writes later, so no test on this machine can see it (kept for a share).
- T-F360: `DownloadMarkChoiceTests` (`Archiver.Core.Tests/Services/`) - through
  `PakkoServices.Create(policy)`'s router (the App and CLI path, so a copy of `ExtractOptions`
  that drops the choice shows): mark on/off on a marked ZIP, off keeps the entry time, a set
  `AllFiles` policy marks despite off, a set `Disabled` does not mark despite on; the 8-case
  `EffectiveMotwMode` matrix; `ArchiveDownloadMark.IsPresent` on marked, unmarked, empty, missing,
  invalid and folder paths. `GroupPolicyServiceTests`: `MotwModeSetByPolicy` only for 0/1/2.
  `TarSandboxedServiceExtractTests.ExtractThroughRouter_DownloadMarkChoice_...` (Integration):
  the same through tar.exe. App.Core `DownloadMarkOptionTests`: hidden, checked with the cost
  note, unchecked with the risk note, locked with the policy note. CLI `CliDownloadMarkTests`:
  `-snz`/`-snz1`/`-snz0`, last wins, `-snz2` and unknown values exit 7, `-snz0` on `t`/`l`/`a`/`h`
  exits 7, the policy warning; subprocess `Extract_MarkedZip_SnzDecidesTheMark`. Seven mutants:
  five caught; the two survivors were redundant checks and were removed.
- `PolicyOwnershipTests` (`Archiver.Core.Tests/Services/`, T-F261/T-F250) — Group Policy has one
  owner: every engine/router constructor requires a non-null `GroupPolicyOptions` (reflection);
  `TarSandboxedService` refuses Extract/List/Compress under `DisableTarExtraction` and never runs
  its version probe (injected probe seam — no tar.exe started); `PakkoServices` probes once and
  hands the policy to every service; extract/create/list/test under `BlockedFormats=tar` and
  `DisableTarExtraction` never reach a tar fake that fails when called; scan reports the policy
  (checked with no AV provider, so a gate regression shows as a different reason, not a tar run).
- `ZipArchiveServicePolicyTests` (`Archiver.Core.Tests/Services/`, T-F250) — the ZIP engine and
  scan refuse a blocked `zip` themselves, including an entry-less ZIP the magic-byte detector
  calls `Unknown` (the case no router check sees).
- `ZipArchiveServiceUserSkipTests` (`Archiver.Core.Tests/Services/`, T-F216) — the user's own
  conflict Skip (one by one or "apply to all") gives no "every entry was skipped" warning, while
  the no-prompt default Skip or an added safety skip still does, and a partly skipped archive
  stays `Partial`. The tar equivalents are two `[Integration]` tests in
  `TarSandboxedServiceExtractTests.cs` (`ExtractAsync_UserChoseSkipAll_...`,
  `ExtractAsync_UserSkippedOneEntry_SourcePartial`).
- `FormatListConsistencyTests` (`Archiver.Core.Tests/Services/`, T-F264) — parses
  `ShellExtUtils.cpp` (ZIP-container and non-ZIP extension arrays, compound extensions, the T-F262
  extension-to-policy-name table) and `Package.appxmanifest`'s file types and compares them with
  the C# lists; seen red by removing `.bdoc` from the manifest and `.txz` from the C++ list.
- `StdinPathListTests` (`Archiver.Shell.Tests/`, T-F235) — the Explorer selection read from stdin
  (`--paths-stdin`): the exact bytes the C++ builder writes, 10,000 paths, quotes/Unicode/an
  unpaired surrogate, the size cap; no stdin, end marker only, empty entry, UTF-8 text; a stream
  that ends after a complete entry without the end marker (mutation-checked), inside a path, on an
  odd byte count, or with a throwing read.
- C++ suites added to `Archiver.ShellExtension.Tests` (T-F235/T-F262): `PathListTransport` and
  `LaunchWithPathList` (payload format, 300 x 95-character repro, only the read end inheritable,
  10,000 paths through a real pipe, a real child process receiving the payload, a write with no
  reader failing), `GetSelectionPaths` (an array with a non-filesystem item is incomplete),
  `MenuPolicy` (each `DisableTarExtraction`/`BlockedFormats` value hides exactly the documented
  items; missing or unreadable values hide nothing) and `Win32PolicyRegistryReader` (missing key,
  wrong type, a real DWORD). Build from a short path: a worktree path can exceed MAX_PATH for the
  gtest lib (`LNK1104`).
- `FolderHashParityTests` (`Archiver.Core.PerformanceTests/`, T-F225; untagged, so it runs in the
  default filter) — folder DataSum/NamesSum against the vendored `7za.exe h` run live on the same
  folder, CRC-32 and SHA-256: one file, flat, nested with an empty subfolder and Cyrillic names,
  empty folder, a folder typed in another case (7-Zip uses the on-disk name), and `folder\.`
  (contents only, no folder item).
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

**T-F299:** `ZipArchiveServiceIncompressibleTests` (`Archiver.Core.Tests/Services/`) — random data
through `ArchiveAsync` is stored, not grown: one file at Fastest (in-memory and temp-file sizes),
SeparateArchives at Fastest, a password at Optimal (Stored inside AES), 80 files at Optimal; a text
file at Fastest stays Deflate. Method and sizes are read with `RawZipEntryLocator`; each case
round-trips through Pakko's reader.

**T-F298:** `ZipArchiveServiceExtractTimesTests` (`Archiver.Core.Tests/Services/`) — archives from
`LegacyZipBuilder` (now with DOS time/date and `NtfsTimeExtra`/`UnixTimeExtra` helpers): DOS, NTFS
and Unix times, NTFS winning over a disagreeing Unix field, six hostile time fields (extraction
succeeds with the DOS time), folder entries in a new and in a merged destination (an existing folder
keeps its time), the SeparateFolders root folder, MOTW plus time, an AES entry.
`TarDirectoriesToPreCreateTests` — which quarantine directories are pre-created, by archive order.
`TarSandboxedServiceExtractTimesTests` (`Archiver.Core.IntegrationTests`, real tar.exe; `TarBuilder`
now takes `ModifiedUnixSeconds`) — MOTW plus file time, folder times new/merged/root, and a file ahead
of its folder entry plus an implicit subfolder still extracting (the T-F52 guard).

**T-F280:** `ZipArchiveServiceTestHeadersTests` (`Archiver.Core.Tests/Services/`) — Test reports a
local CRC plus a local name flip as one error (count 2, first entry named), a changed local size, and
a local header whose signature is gone; and reports nothing for every repo fixture, a data-descriptor
archive and Pakko's own writers (Optimal, Fastest, Stored, AES). `ZipHeaderCheckCorpusTests`
(`Archiver.Core.PerformanceTests`, vendored 7za) — 7-Zip's plain, AES-256 and `-si` archives report
nothing.
Extraction half (warning channel): `ZipArchiveServiceExtractHeaderWarningTests` — the same tampered
archive extracts by the central directory with one `ArchiveWarning` (`Success`, outcome
`CompletedWithWarnings`, the archive still deletable), a selection warns too, nothing extracted
means no warning, and the same false-positive corpus as Test extracts with no warning.
`OperationOutcomeTests` (warnings against errors and skips), `ExtractionRouterTests` (merge keeps
both engines' warnings), `OperationMessagesWarningTests` in `Archiver.Shell.Tests` (alone, under an
error, under the skipped list, capped at ten, `Combine`, and a real extraction rendered in
Ukrainian), two `Subprocess/` tests of `pakko x` (warning line and exit 1; with a failed archive,
both lines and exit 2), `OutcomeAndEncryptionSummaryTests` (the App's footer line counts warnings).
Ten mutants killed. The App's summary dialog section has no automated test (WinUI).

**Wave 1 after v1.7.0 (2026-10-05).** `EntryRegionStreamTests` and two cases in
`VerifyingReadStreamTests`: the size and truncated-entry errors carry their codes (T-F333), also
checked through `TestAsync`/`ExtractAsync` in `ZipArchiveServicePasswordTests`.
`ZipArchiveServiceDestinationInsideSourceTests` (four writers x two modes), three tests in
`TarSandboxedServiceCompressTests` and two in `TempOwnerTests` (`IsOwnBelow`): an archive created
inside its own source folder holds only the user's files (T-F316).

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

**T-F193 additions (`a -p`, bare `-p`, `-mem`):** `CliArgumentParserTests` covers `-p`/bare `-p`
on `a`/`x`/`t`, `-p` + `-t tar*` in either order, bare `-p` + `-si`, and every `-mem` value.
`CliPasswordPromptTests` drives `ReadNewPassword` with a fake key source (match, mismatch, Esc,
Ctrl+C, non-ASCII refused before the re-enter prompt, empty, 100 characters). `CliSubprocessTests`
adds an `a -p` → `x -p` round trip (a wrong password must fail, proving real encryption), and exit 7
for a non-ASCII `-p`, `-p` with `-ttar`, `-mem=ZipCrypto`, and a bare `-p` with redirected stdin on
`a` and `x`. Not covered by any automated test: the real-console double prompt itself.

**Fix phase 8 additions (2026-09-27):** `CliArgumentParserTests` covers `-scc` on every command,
last-wins and unsupported names (T-F238); `CliConsoleCharsetTests`, `CliVersionTextTests` (T-F222),
`CliCancellationTests` and new `CliStreamStagingTests` cases (staging removed on a failed or
cancelled copy, dead-process sweep, PID-reuse ownership, T-F244). `CliSubprocessTests` adds `x`
without `-o` into the working directory (`CliProcessRunner.RunIn`, T-F206), `l -sccUTF-8` checked
as raw BOM-free UTF-8 bytes, the dev-version `-v` pattern, and a sweep of a dead run's folder.
Not automated: real-console Ctrl+C and the cp866 loss itself.

**Post-v1.7.0 wave 2 additions (2026-10-05):** `SourcePathNormalizerTests` (Core: a fully qualified
path unchanged, `.`/`..`/relative resolved, a drive root keeps its separator, an unresolvable path
left as typed; T-F338) with `ArchiveAsync_SourceNamedByDots_*` and two subprocess tests that run
`pakko a <out> .` in a chosen directory (ZIP and tar). `TarListingDateTests` and one real-tar.exe
listing test say whether the time is known (T-F335). `ArchiveCreationRouterTests`/`PolicyOwnershipTests`
check the item a refusal names (T-F326). `ZipArchiveServiceExtractTests` and `ExtractionRouterTests`
cover `KeptExistingFiles` (automatic skip listed, the user's own answer not, merge), and the
subprocess layer pins `pakko x` over existing files: default (line, hint, exit 1), `-aos`, `-aoa`
(T-F313). `CliCreatedNameTests` covers the taken-name line (T-F325); its wiring in `Program.cs` is
checked only by a real two-run race on a device, since a test cannot stage that race reliably.

**v1.7.0 wave 3 additions (2026-10-03):** `CliArgumentParserTests` covers PowerShell-split pairs
(`-ttar` `.gz`, `-pSecret` `.1`, `-oout` `.d` — the password never in the message), dot-leading paths that are no
split piece (`h -scrcSHA256 .gitignore`), the dot-free `-t` aliases and `-t` against the name's own archive type,
`.tgz`-style names included (T-F294/T-F296).
`CliHintsTests` checks the cause table names every `MessageCode` and the `-aoa`/`-p` hint rules
(T-F293). `CliMessagesSubprocessTests` adds a CRC failure with no `-aoa` hint, a tar conflict with
it, `a out.tar.gz` without `-t` (exit 7, nothing written) and the name `.gz` written as typed.
`CliConflictPromptTests` checks both files' size and time in the prompt and now deletes its folder
through `TestTempFolder` (retrying, T-F300; `TestTempFolderTests`); `CliPasswordPromptTests` the
"input is masked" wording (T-F295). Not automated: real-console Ctrl+C (checked on device by
driving a conhost console, see T-F295).

---

## AMSI Antivirus Scan Tests (T-F146, v1.4+)

`tests/Archiver.Core.Tests/Services/Antivirus/`:
- `AmsiScannerTests.cs` — real `amsi.dll` P/Invoke against a runtime-generated EICAR buffer (never
  committed to disk — Defender would quarantine a committed EICAR fixture on clone/build) and a
  clean buffer, mirroring the probe run during design (`docs/DECISIONS.md`'s T-F146 entry).
  Environment-dependent by nature — exercises whatever AV is actually registered on the machine
  running the suite, same as any real AMSI consumer.
- `AmsiProviderCheckTests.cs` — smoke test only (`IsAnyProviderRegistered()` never throws); the
  actual registered-provider state isn't asserted, since that would make the suite depend on the
  test machine's own AV configuration.
- `AntivirusScanServiceTests.cs` — orchestration logic via a hand-rolled `FakeAmsiScanner` (no
  mocking library, matching repo convention): clean/`ThreatDetected` ZIP archives,
  `SelectedEntryPaths` subset scanning, the 64 MiB oversized-entry skip
  (`AntivirusScanService.MaxScannableEntryBytes`), the no-provider-registered gate, Group Policy
  blocked-format handling, and an unrecognized-file-doesn't-throw case. T-F194 adds
  password-protected ZIP coverage: no resolver / cancel / exhausted retries stay `Inconclusive`
  with zero AMSI calls; a correct password hands AMSI the byte-exact decrypted plaintext (the fake
  records scanned bytes, not just name+length); mixed archives prompt once; "apply to remaining"
  spans a multi-archive call; a ZipCrypto check-byte collision is never `Clean`; BZip2-under-AES
  and a local-header size past end-of-file end `Inconclusive` without throwing. The same file's
  `AntivirusScanServiceEncryptedEicarTests` (gated by `[SkipIfAmsiScanUnavailable]`) runs real EICAR
  from `encrypted_aes256_eicar.zip` through the real AMSI provider.

`tests/Archiver.Core.IntegrationTests/AntivirusScanServiceTarTests.cs` (`[Collection("TarSandbox")]`,
same T-F130 serialization as every other real-sandbox test class) — the tar-family quarantine-scan
path against real `tar.exe`: clean/threat-detected archives, subset scanning, and a symlink-entry
archive confirming T-F49's pre-scan rejection becomes an `Inconclusive` finding rather than an
unhandled throw.

`tests/Archiver.Shell.Tests/ShellArgumentParserTests.cs` gained a `--scan` block (`Scan_SingleFile_
ReturnsScan`/`Scan_MultipleFiles_ReturnsAllFiles`/`Scan_NoFiles_ReturnsInvalid`), mirroring the
existing `--test` block exactly.

`tests/Archiver.ShellExtension.Tests/ShellExtUtilsTests.cpp` gained `BuildScanArgs` cases
(`SingleFile`/`MultipleFiles`), mirroring `BuildTestArgs`.

**Not covered by `dotnet test`, needs on-device verification** (per this project's standing rule —
shell-triggered/UI behavior never graduates on automated tests alone):
- A real EICAR-in-archive detection through both *shell-triggered UI* entry points (Explorer
  `Scan for threats`, Archive Browser's scan button) — for the tar-family case specifically, this
  needs a temporary Defender exclusion folder added by the user first (`docs/DECISIONS.md`'s
  Phase 0 finding: real-time protection intercepts a plain on-disk EICAR file before tar.exe can
  even read it, independent of Pakko's own AMSI call). **Partially superseded by T-F177
  (2026-08-31):** the underlying `Core.AntivirusScanService.ScanAsync` orchestration itself (both
  Zip in-memory and Tar quarantine paths) now has an automated regression test with real AMSI —
  `AntivirusScanServiceEicarTests.cs` — so what's left uncovered here is specifically the two WinUI/
  Shell UI entry points' own glue code, not the underlying scan logic.
- The `Inconclusive` path with no AMSI provider registered (e.g. Defender real-time protection
  temporarily disabled) — confirms the three-state dialog never renders it as `Clean`.
- A clean real-world archive through both entry points, confirming "No threats found in this
  archive" copy and that nothing is left on disk afterward (tar-family quarantine cleanup).

## Test-Coverage Audit Follow-Ups (T-F174–T-F186, 2026-08-31)

Sourced from a full three-stage QA/AppSec coverage audit — see `docs/TASKS.md`'s own
"Test-Coverage Audit Follow-Ups" section for the complete per-task detail. This section only
records the two accepted-scope-limitation decisions and one real finding that don't fit neatly
into an existing section above.

- **`tests/Archiver.Core.IntegrationTests/AntivirusScanServiceEicarTests.cs` (T-F177)** closes a
  gap the AMSI section above didn't call out explicitly: every `ScanAsync_...DetectedEntry` test,
  Zip and Tar alike, used `FakeAmsiScanner` — real EICAR bytes had only ever been driven through
  `AmsiScanner.ScanBuffer` directly (`AmsiScannerTests`), never through `AntivirusScanService.
  ScanAsync`'s real orchestration with the real scanner. Two new tests close that specifically;
  the Tar variant accepts either `ThreatDetected` or "the fixture was intercepted by real-time AV
  before AMSI ran" as passing, per T-F146's own already-documented Phase 0 finding that Defender's
  on-access scanner can win that race. Confirmed working for real on a dev machine (both passed).
- **`FileHashService`'s `Int64` byte-total summation near `Int64.MaxValue` is untested by design
  (T-F181)** — real multi-exabyte fixtures are infeasible, and no injectable file-size abstraction
  exists anywhere else in this codebase to test the arithmetic in isolation without adding one
  solely for this purpose. User-directed decision: document, don't build a new seam.
- **The `IsBusy`/rapid-repeated-invocation guard in `MainViewModel.cs` was verified, not
  implemented, against (T-F183).** `ArchiveAsync` (line ~457) and `RunExtractAsync` (line ~596)
  both set `IsBusy = true` as the literal first statement before any `await`; `MainWindow.xaml.cs`'s
  `ArchiveBrowserList_DoubleTapped` (line ~197) checks `ViewModel.IsBusy` the same way, before any
  `await`. On WinUI's single dispatcher thread this closes the TOCTOU window by construction — a
  second click's check can only run after the first click's handler already set `IsBusy = true`.
  T-F123 (the precedent this task was based on) was later root-caused as a stale `dotnet build`
  masking an already-correct fix, not an actual race. **If a future refactor introduces an `await`
  before either guard, re-verify this invariant before assuming it still holds** — nothing
  currently enforces it structurally beyond the ordering itself.
- **T-F178 finding:** `dotnet test`'s `testhost.exe` has no `app.manifest` (unlike `Archiver.App`/
  `Archiver.Shell`), yet a real on-disk path over 260 characters archived/extracted successfully at
  the `ZipArchiveService` layer with no special handling. This is .NET Core's own built-in
  long-path File I/O support (present since .NET Core 2.1, independent of any Win32 manifest
  declaration) — see `docs/DECISIONS.md`'s T-F178 entry for the full account.

## ZIP Extraction Safety and Integrity (fix phase 2, 2026-09-25)

One test class per fixed defect, each written red first (see `docs/DECISIONS.md`'s fix-phase-2
entry): `ZipArchiveServiceExtractStagingTests` (T-F227 — a user's `<dest>_tmp` survives, no
`.pakko-x-*` leftover, concurrent runs, extracted folder not Hidden),
`ZipArchiveServiceExtractUnsafePathTests` (T-F228), `ZipArchiveServiceExtractPerEntryFailureTests`
(T-F230, incl. the disk-full classifier), `ZipArchiveServiceExtractIntegrityTests` (T-F246/T-F231 —
fixtures are built in the test by patching CRC/size fields of a fresh archive, not stored),
`IO/VerifyingReadStreamTests`, `ZipArchiveServiceExtractRootFolderTests` +
`TarSandboxedServiceRootFolderTests` (T-F205), `ZipArchiveServiceExtractEmptyFolderTests` +
`TarSandboxedServiceEmptyFolderTests` (T-F197). Staging-leftover assertions look for
`.pakko-x-*`; an assertion on the old `*_tmp` name would now pass vacuously.

## ZIP Names and Reader Hardening (fix phase 3, 2026-09-25)

Written red first (see `docs/DECISIONS.md`'s fix-phase-3 entry). `Zip/ZipEntryNameDecoderTests`
(explicit code pages only — 866/437/1252/1251/932, never the machine's) and
`ZipArchiveServiceLegacyNameEncodingTests` (service pinned to 866/1251 through the internal
`NameCodePages` seam, because the en-US CI runner has 437/1252). Fixtures: `legacy_oem866_7za.zip`
(committed, vendored `7za -mcp=866`: cp866 names, flag clear, plus 0x7075) and
`Helpers/LegacyZipBuilder` (raw name bytes, flag, host OS, central extra, local-name override —
for shapes 7za cannot write). `ZipArchiveServiceZipCryptoCompatTests` with
`Helpers/ZipCryptoFixture` (ZipCrypto written from APPNOTE, independent of `ZipCryptoStream`;
data descriptor and raw password bytes; cross-checked with 7za) covers T-F243 items 2-3 and the
ANSI/UTF-8 password candidates. `ArchiveEntrySecurityReservedNameTests` (T-F243 item 4),
`ZipArchiveServiceLongEntryNameTests` (T-F243 item 6, end to end; returns early without
`LongPathsEnabled`), `CliSubprocessTests.Extract_LegacyOemNamedZip_WritesRealNames` (asserts files
on disk, not console output, so it is code-page independent).

## Explorer to App Hand-Off (fix phase 4a, T-F232, 2026-09-26)

`LaunchArgumentsTests` (Core: format/parse round trip incl. Cyrillic and UNC paths, malformed or
unknown input, blank entries, the 32000-character limit, 80 long Cyrillic paths fitting — red on
the default `\uXXXX` JSON escaping), `LaunchActivationRouterTests` (App.Core: browse vs. pending
list, plain Start-menu launch, a leftover `pakko://` string is not recognized), `AppLauncherTests`
(Shell: the length guard at the base64 boundary — no string formats to exactly 32000, so the test
finds the longest fitting path and the next one), `FileItemTests` (App.Core: `TryCreate` returns
null for a missing/invalid path — red before the fix; waits for the background CRC so the temp
folder can be deleted), and `ResultMessagesLocalizerTests.Get_OpenUiKey_IsTranslatedInEveryLocale`.
The `ActivateApplication` call itself is device-only (it needs package identity): see
`docs/DECISIONS.md`'s T-F232 entry for the checks.

## Explorer Operation Window (T-F268 step 3, 2026-09-26)

`ShellCommandsTests` (Shell): the three extract commands open ONE session for a multi-archive
selection, name each archive through `BeginItem`, and show one combined result; cancelling the
first archive never starts the second (T-F269) — 8 tests red on the old per-archive code.

`tests/Archiver.OperationUi.Tests` (new project, references only `Archiver.OperationUi.Protocol`):
`FrameCodecTests` round-trip every Shell<->helper message, the frame format over a real
`AnonymousPipeServerStream`/`ClientStream` pair, the size boundary (a 32,767-char Cyrillic path in
a conflict, a result listing ten of them, a message over `MaxFrameBytes`, invalid length fields),
malformed payloads (unknown type, missing field, `null` for a non-null field, invalid JSON),
truncated header/payload vs. a clean end of stream, 200 concurrent `MessageWriter` writes, and
that neither `PasswordAnswer.ToString()` nor a `ProtocolException` quotes a password. All seven
guards were mutation-checked (each removed in turn, its test turned red).

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
   dotnet test -c Release --filter "Category=VeryLarge"
   ```

**Known non-bug finding:** `.zip`'s `UserChoice` file association may still point at Windows'
built-in `CompressedFolder` handler even after Pakko is installed. T-F44 registers the
association, but Windows requires explicit user opt-in via Settings → Default apps before
double-click routes to a non-built-in handler — this is a Windows security mechanism (UserChoice
hash), not a Pakko defect.

## Explorer Operation Window Strings (T-F268 step 6, 2026-09-27)

`ShellResourceParityTests` (Shell): for all six Shell `.resx` families and all 36 non-English
locales, every English key exists in the locale's own resource set (read with `tryParents:
false`, so a silent fallback to English fails) with the same `{n}` placeholders; and under uk-UA
`OperationWindowText.CreateHello()` carries every `WindowStrings` key, none left English. Both
mutation-checked (a renamed key in uk-UA, a `{1}` for `{0}` in ja-JP). `ProgressTextTests`: size
units per language (`2 КБ`, `3,0 Go`, `2,0 МБ/с`). `ShellCommandsTests.Test_UnderUkrainian_
TitlesAreTranslated`, `OperationMessagesTests` (hash size without "bytes", several clean scanned
archives). Tests that assert English pin `CurrentUICulture` to en-US: this dev machine runs a
Ukrainian UI, CI an English one.

---

## Core Messages, Outcome and CLI Messages (fix phase 7, 2026-09-28)

- v1.7.0 wave 4 (2026-10-03): `CoreMessageSourceGuardTests` also fails on a raw `ex.Message` inside
  `CoreMessages.Text(` and covers `CoreMessages.Detail` (each mapped Windows code, inner exception,
  unmapped code, non-Windows HRESULT, rewrite); `MessageTextTests` checks the Ukrainian-only codes
  (uk has them and keeps the English text, the other 35 do not, de-DE renders English + code);
  `VerifyingReadStreamTests` pins the CRC code; a real `What?.txt` extraction ends in
  `(0x8007007B)`. A cancelled conflict prompt throws `OperationCanceledException` on ZIP extract
  (`SourceOutcomeTests`), ZIP create in both modes (`ZipArchiveServiceArchiveTests`) and tar
  extract/create (`TarSourceOutcomeTests`). App.Core: `ProgressTextTests`, `ConflictTextTests`,
  `SortIndicatorTests`, `NestedDisplayPathTests`, and `SourceRecyclerTests`' `Deleted` list.
- `CoreMessageSourceGuardTests` (Core) reads `src/Archiver.Core` and fails on any `Message =`/
  `Reason =`/`ErrorMessage =` outside `CoreMessages` (red on the unconverted code: 113 sites);
  `CoreMessageCodeTests` pins the codes users meet (corrupted ZIP, GZip, not an archive, missing
  source, all skipped, bomb declined, blocked listing) with byte-identical English.
- `Archiver.Messages.Tests` (new project): the neutral `CoreMessages.resx` equals Core's templates;
  every one of 36 locales translates every code with the same placeholders; nested rendering;
  `UiCulture` table (zh-CN/SG -> zh-Hans, zh-TW -> English, de-AT -> de-DE, English listed first
  wins).
- T-F329, same project: `LocalizedSources` reads all nine string sources from the files;
  `GlossaryTests` (one word per concept per locale, from `Glossary.tsv`), `SharedStringTests`
  (one English string, one translation), `CountTemplateTests` (no count before a noun in
  English UI text; no inflected word for "bytes" after a number in the seven locales where it
  would disagree).
- T-F331, same project: `StoreListingTests` reads `docs/store-listing/<locale>.txt`: every field
  within Partner Center's limits, the menu items named as `Localization.cpp` has them, no word
  that `Glossary.tsv` lists as replaced.
- T-F330: `ShellUiLanguageTests` (Shell) and four `PickLanguageTag` tests (C++): the first
  language of the user's list that Pakko ships wins over the display language.
- `OperationOutcomeTests` (Core): each `OperationOutcome` from real ZIP/router runs, including a Test
  that read nothing (T-F274). Shell `OperationMessagesTests.ForTestResult_*` and
  `ShellCommandsTests.Test_NothingTested_DoesNotClaimNoErrors` (red before the fix).
- T-F217: `OperationWindowModelConfirmTests`, `HelperOperationUiTests.AQuestion_*`,
  `ShellCommandsTests.Extract_SuspectedBomb_*` (50 MB of zeros, all three extract commands),
  `ShellConflictDialogTests.ConfirmMapResult_*`. T-F255: `PasswordDialogReadTextTests` with a real
  EDIT control (0-4096 characters; a 256-cap mutant fails three). T-F253: `BuildContent_*`.
- T-F254 (C++): `LocalizationTests` — zh-CN/zh-SG/zh-Hans-CN, Traditional stays English, regional
  variants, tag case (three red before the fix).
- T-F237 item 2 (App.Core, tests first): `ArchiveTreeIndexTests` — a 20,000-segment entry name
  builds and opens its deepest folder under a 32 MiB allocation budget
  (`GC.GetAllocatedBytesForCurrentThread`; 1.6 GB before the fix), plus pinned odd shapes (explicit
  folder after its implied one, a file and a folder with one name, `a//b`, a leading `/`). Core:
  `CoreTextTests` (a long argument keeps head and tail, surrogate pairs whole) and
  `ExtractAsync_TwentyThousandSegmentName_OthersExtractAndTheErrorIsShort` (80 KB error before);
  `MaxEntryDepth`: `HasUnsafePath_Depth*` (limit and limit+1, both separators, `a//b`),
  `ExtractAsync_NameDeeperThanTheLimit_*` (ZIP, that entry only) and
  `NameDeeperThanTheLimit_RejectedByThePreScan` (tar, pax path).
- T-F198: `AppResourceKeysTests` (App.Core) — the twelve new App keys in all 37 `.resw` files with
  English's placeholders.
- T-F221: `CliFacingMessagesTests` (Core) and `CliMessagesSubprocessTests` (real `pakko.exe`):
  missing input, -p hint and one wrong-password line, -aoa hint, empty and garbage stdin shown as
  "(stdin)", explicit names written the 7-Zip way, `h <folder>` relative names; `CliProgressTests`
  for the console-only percentage.
- Wave 4 / T-F199 (App.Core, tests first, mutation-checked): `PrimaryActionPolicyTests`,
  `CreateModeTextTests`, `InlinePasswordStateTests`, `OutcomeAndEncryptionSummaryTests`
  (`OutcomeLine`, `FooterLine.Pick`, `EncryptionSummary`), `BrowseModeTests`
  (`BrowseLocationState`, `BrowseWork`, badge and notes, the tree carrying encryption to the row), row UIA names
  via `ToString()` in `FileItemTests`/`ArchiveEntryViewModelTests`, `SessionPasswordMemoryTests`
  (T-F200), `WindowCascadeTests` (T-F201), `ProcessTempRootTests` (T-F252), `BuildStampTests`
  (T-F218), `BrowserEntryRoutingTests` (T-F242). `AppResourceKeysTests` now requires every en-US
  key in all 37 `.resw` files with English's placeholders (only the About URLs are exempt), every
  key to be used and every `x:Uid` to have a key. Core/CLI: the per-entry `Encryption` marker and
  `pakko l`'s Encrypted column. The window layout itself (cards, scroll fit, 900x520 floor) is
  checked on device, not by tests.

## Diagram 6 as a Checked Contract (v1.7.0 wave 7, T-F112)

- Wave 3 after v1.7.0 (2026-10-06): `BrowseNavigationTests` also covers `DecideListFailure` (T-F319: back to the real folder only when one was browsed before the open; five other prior states go to the pending list); `FileSystemBrowserTests` covers T-F324 (a Hidden+System folder and file are left out; Hidden-only and System-only ones are listed).
- `BrowseNavigationTests` (App.Core, tests first): every `BrowseUpStep` of `DecideUp`, a drive
  root, no archive path.
- `DiagramSixTests` reads `docs/DIAGRAMS.md`'s diagram 6 and checks its three `check:` tables
  (`browse-location`, `row-open`, `browse-up`) against `BrowseLocationState.For`,
  `BrowserEntryRouting` and `BrowseNavigation.DecideUp`. It requires the full 2x2x2 domain of
  `For` exactly once, every `RowOpenAction` and `BrowseUpStep`, that the probe runs exactly when the
  table gives it a value, and an `Up` arrow in the state diagram for each Up row. Mutation-checked
  both ways (2026-10-03): 2 code mutants and 5 document edits, all red. Entry, exit, breadcrumb and
  drill-in transitions stay in the WinUI view model and are checked on device.

## `pakko` in the Package (v1.7.0 wave 8, T-F317)

- `PackagingManifestTests` (Core.Tests, tests first): the source `Package.appxmanifest` has one
  hidden FullTrust `<Application>` for `pakko.exe` with the only `ExecutionAlias` (`pakko.exe`), and
  `Archiver.App.csproj` packages `pakko.exe` alone from `Archiver.CLI`'s output (T-F355: Native
  AOT; before it, the apphost's `.dll`/`.deps.json`/`.runtimeconfig.json` too). Mutation-checked
  (2026-10-03): alias removed — red. `CI-Build-Msix.ps1` checks the same in the built package.
- **Native AOT (T-F355)**, `PackagingManifestTests` and `WinUiAotSourceGuardTests` (Core.Tests,
  tests first, read the repo's files): each satellite packaged as its exe alone; `PublishAot` in
  the four exe projects and no `PublishReadyToRun`/`PublishTrimmed`; `AllowUnsafeBlocks` and
  `CsWinRTAotWarningLevel` 2 in App and OperationUi; `IsAotCompatible` in the five libraries; no
  `(T)` or `as T` on a resource-dictionary read in App or OperationUi (only `WinRT.CastExtensions.As<T>`).
  Seven mutants killed (2026-10-08): a `pakko.dll` item back, `(Style)` and `as Brush` back,
  `PublishAot` dropped from Shell, `IsAotCompatible` dropped from Messages, the warning level dropped
  from OperationUi, `PublishReadyToRun` added to the CLI. `dotnet test` runs under JIT, so the
  Subprocess layer also runs against the published AOT `pakko.exe` (`build.yml`'s `build-cli`, and
  nightly on x64 and arm64 in `canary.yml`).
- **C# 14 (T-F363)**, `PackagingManifestTests.Repo_PinsCSharp14Once` and `LockSourceGuardTests`
  (Core.Tests, read the repo's files): `LangVersion` 14 only in `Directory.Build.props`, no
  `.csproj` overriding it; no bare `object x = new()` (a lock object) in `src/`. Mutants killed
  (2026-10-09): `LangVersion` added to the CLI project, `FileHashService`'s lock back to `object`.
- **Lock files (T-F364)**, `BuildReproducibilityTests` (Core.Tests, read the repo's files): no `*`
  in any `PackageReference` version, a `packages.lock.json` next to every `.csproj` under `src/`,
  `tests/`, `tools/`, lock restore on and locked mode conditioned on `CI` in
  `Directory.Build.props`, `global.json` exact with `rollForward: disable`, `build.yml` taking the
  SDK from `global.json`. All five red before the change (2026-10-09).
- **SBOM (T-F365)**, `SbomWorkflowTests` (Core.Tests, read the repo's files): the CycloneDX tool
  pinned exactly; `build.yml`'s `sbom` job read-only with no secrets; no job holding a secret or
  `id-token` restores tools or runs the generator; `build-msix`/`build-cli` need `sbom` and attest
  with `actions/attest` and `sbom-path`; `release` publishes the `.cdx.json` files; the script
  restores locked and drops the build-only and Windows ML packages. Six of seven red before the
  change (2026-10-09; the seventh, no privileged job runs the generator, held already).
- **Agent hook (T-F370)**, `AgentBashHookTests` (Core.Tests): runs `scripts/hooks/Test-AgentBashCommand.ps1`
  through pwsh (found on PATH) with the payload Claude Code sends — bare `python`/`python3` and
  `dotnet ... /p:` at command position (after `;`, `&&`, `|`, `$(`, a newline, an env assignment, a
  backslash continuation) exit 2; quoted text, a heredoc body, `py -3` and `echo dotnet /p:` exit 0; a
  non-JSON payload exits 1; `.claude/settings.json` wires the hook to `Bash`. Mutation-checked.
- **Tracked manifest revision (T-F368)**, `PackagingManifestTests.Manifest_KeepsTheRevisionAtZero`:
  `Package.appxmanifest`'s Identity revision is 0. Red on 1.7.1.24 before the change.
- **Job limits (T-F366)**, `WorkflowLimitsTests` (Core.Tests; both share `WorkflowJobs`, the job
  splitter): every job in `build.yml` and `canary.yml` has a job-level `timeout-minutes` of 1-120;
  `build.yml`'s concurrency group falls back to `run_id` outside branch pushes and PRs;
  `canary.yml` has none; canary-status reads check-run annotations for a timeout. All four red
  before the change; mutants killed: one timeout removed, the `run_id` fallback, the annotation text.
- `CliVersionTextTests.WithPackage_*`: `pakko -v` appends `(package <full name>)` only when packaged.
- `PAKKO_CLI_EXE` points `CliProcessRunner` at another `pakko.exe`; set it to
  `%LOCALAPPDATA%\Microsoft\WindowsApps\pakko.exe` to run the whole `Subprocess/` layer against the
  installed package (68/68 on 2026-10-03). The `-v` pattern accepts the package suffix.

## Temporary Names and Commits (v1.7.0 wave 6, T-F263/T-F312)

`IO/TempOwnerTests` (tag and name shape, `IsRunningHere` incl. a reused pid and v1.6.0's
`<pid>-<ticks>`, the sweep by place: this machine's dead/live/reused owners, another machine's and
v1.6.0's pid-only names by age next to a destination but by process under `%TEMP%`, owner-less
names by age, a held entry, a junction deep inside, a read-only file), `ArchiveTempFileTests`
(the name, a dead run's file swept, the commit with either file held briefly, held throughout,
missing, cancelled). Through the engines: `ZipArchiveServiceArchiveTests` (an old fixed `.tmp`
held, the archive name taken by a folder, a ~240-character archive name, Overwrite onto an archive
held briefly and throughout), `TarSandboxedServiceCompressTests` (the same old `.tmp` and held
Overwrite cases through real tar.exe), `ZipArchiveServiceExtractStagingTests`,
`ParallelSingleArchiveWriterTests` and `TarSandboxScopeTests` (a dead run's staging, chunk folder
and quarantine swept). Overwrite of an archive inside the folder being archived, ZIP (both
modes) and tar: the old archive is not packed. `ProcessTempRootTests.CurrentOwnerName_IsTheV160Shape`.
The dead owner is the current pid with another start time (`Helpers/DeadOwner`). Mutants killed: liveness always false, the machine check removed, link
removal removed, each of the four sweeps removed, the fixed `.tmp` name, the narrow retry predicate,
the delete-first Overwrite in tar.

T-F321 (wave 9): `ArchiveTempFileTests` (an archive that appeared during the run is kept and the
commit takes `a (1).zip`; a free name commits there; Overwrite replaces the archive that existed at
the start) and two runs creating one name at once, every reported archive holding its own entries:
`ZipArchiveServiceArchiveTests` (SingleArchive and SeparateArchives) and
`TarSandboxedServiceCompressTests` (real tar.exe). Mutants killed: `replacesExisting = true` at each
of the three commit sites.

T-F236: `IO/FolderTotalsTests` (bytes and count, a link not followed, an unreadable subfolder, a
cancelled walk) and `FileItemTests` in `Archiver.App.Core.Tests` (a folder's totals with a link, a
file's, disposing the item stops the walk and its totals still complete). Mutants killed: the token
ignored, `Dispose` not cancelling. The queued CRC-32 read's cancel is enforced by CA2016, not a test.

T-F288: `Archiver.Shell.Tests/ShellComTests` creates the progress dialog and the activation
manager through `ShellCom` (title, line, `HasUserCancelled` before start; an unknown AUMID returns a
failure HRESULT; an unregistered class throws `COMException`). It cannot see a wrong vtable order;
progress and Cancel are checked on the device.

---

## Rules

- No `Thread.Sleep` — use `await Task.Delay` if needed
- Each test cleans up via `TempDirectory.Dispose()`
- No test depends on another test's state
- `dotnet test` never writes files outside `%TEMP%`
