# TASKS.md — Active and Future Tasks

> Completed tasks (T-01 through T-35, T-11) are archived in [`TASKS_DONE.md`](TASKS_DONE.md).
> **v1.0 is complete.** All items below are post-v1.0 future work.

---

## ⚠ Agent Rules — Read Before Every Task

These rules apply to ALL tasks. Violating them = task is NOT complete.

**Completion rules:**
- NEVER mark `[x]` unless every single acceptance criterion is checked `[x]`
- `[~]` means partially complete — UI done but logic missing, or logic done but untested
- A task with ANY `[ ]` criterion must stay `[ ]` or `[~]` — never `[x]`

**Testing rules:**
- Test-run commands and when to run the Slow filter are `CLAUDE.md`'s Hard Constraints — the
  canonical copy; don't restate them here.
- If tests fail → fix before marking anything complete
- Every new behavior in `ZipArchiveService` needs at least one test

**UI vs Logic rules:**
- UI-only implementation = `[~]` not `[x]`
- If a task touches both XAML and a service, BOTH must be done before `[x]`
- Options passed from ViewModel to service must actually be READ and ACTED ON in the service

**Scope rules — which options apply to which action:**
- Archive-only options (Name, Mode, Compression, DeleteSourceFiles) → `ArchiveOptions` only
- Extract-only options (DeleteArchiveAfterExtraction) → `ExtractOptions` only
- Shared options (Destination, OnConflict, OpenDestinationFolder) → both

---

## Current State — v1.1 Complete

- All T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39 complete and committed
- 95/95 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` via `Deploy.ps1` (signed with dev cert)
- Satellite EXEs (Archiver.Shell.exe, Archiver.ProgressWindow.exe) included via Content Include in Archiver.App.csproj
- Git tag: `v1.1.0` — GitHub-only release for early testers
- **Store release planned for v1.3** (when shell extension + MOTW + tar.exe complete)

---

## Future Tasks

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** SUPERSEDED by T-F53–T-F57 — kept for historical reference
- **Depends on:** T-F09 (CLI Core)

**What:** Right-click context menu in Windows Explorer for archiving and extracting without opening the main UI window.

**User experience:**

Right-click on any files/folders (non-ZIP or mixed):
```
Pakko ►
  ├── Add to "first_item.zip"    ← immediate, no window, single archive
  ├── Add to separate ZIPs       ← immediate, no window, one ZIP per item
  └── Archive with Pakko...      ← opens main window with items pre-loaded
```

Right-click on one or more ZIP files:
```
Pakko ►
  ├── Extract here               ← immediate, no window, extract next to archive
  ├── Extract here (new folder)  ← immediate, subfolder per archive
  └── Extract with Pakko...      ← opens main window with archives pre-loaded
```

Right-click on mixed selection (ZIP + non-ZIP):
```
Pakko ►
  ├── Add to "first_item.zip"
  ├── Extract ZIPs here
  └── Open with Pakko...
```

**Technical approach — two components:**

**1. `Archiver.Shell` project** (new, `src/Archiver.Shell/`)
Lightweight console exe invoked by the context menu with arguments:
```
Archiver.Shell.exe --archive --dest same "file1" "file2" "file3"
Archiver.Shell.exe --archive --separate --dest same "file1" "file2"
Archiver.Shell.exe --extract --dest same "archive1.zip" "archive2.zip"
Archiver.Shell.exe --open-ui --archive "file1" "file2"
```
Uses `Archiver.Core` directly — no WinUI dependency. Runs silently (`<OutputType>WinExe</OutputType>`, no console window).

**2. Shell extension registration**
Windows 11 (build 22621+): sparse package manifest — no COM DLL needed.
Windows 10 fallback: classic COM `IContextMenu` shell extension DLL.

Declared in `Package.appxmanifest` for MSIX distribution.

**Silent operation — no window flicker:**
- `Archiver.Shell.exe` runs with `CreateNoWindow = true`
- Progress shown via Windows Toast notification on completion:
  ```
  Pakko
  Archived 3 files → backup.zip
  ```
- Errors shown via Toast, not dialog

**Acceptance criteria (when implemented):**
- [ ] `Archiver.Shell` project added to solution, references `Archiver.Core`
- [ ] `--archive` flag: archives all passed paths into single ZIP next to first item
- [ ] `--archive --separate` flag: one ZIP per item
- [ ] `--extract` flag: extracts all passed ZIPs next to each archive (T-14 smart folder logic)
- [ ] `--open-ui` flag: launches `Archiver.App` with items pre-loaded
- [ ] No console window shown during silent operations
- [ ] Toast notification on completion — success and error
- [ ] Context menu appears for ZIP files with Extract options
- [ ] Context menu appears for non-ZIP files/folders with Archive options
- [ ] Multi-selection works — all selected items passed in single invocation
- [ ] Works on Windows 10 1809+ and Windows 11
- [ ] Registered via MSIX manifest — no manual registry editing
- [ ] Uninstall removes all context menu entries cleanly
- [ ] `dotnet test` passes — basic invocation tests for Archiver.Shell

---

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

Separate window for archive configuration instead of inline controls.

---

### T-F03 — Dedicated Extract Window
- [ ] **Status:** future

Separate window for extract configuration.

---

### T-F04 — TAR/GZip/BZip2/XZ Support via Windows tar.exe
- [ ] **Status:** future

Uses Windows built-in `tar.exe` (available since Windows 10 1803, based on libarchive).
No third-party binaries — `tar.exe` is part of the OS.
Invoke via `System.Diagnostics.Process`.

---

### T-F05 — Archive Contents Preview
- [ ] **Status:** future

Click ZIP in list → read-only tree view of contents via `ZipFile.OpenRead`. No extraction.

---

### T-F06 — Ask on Conflict Dialog
- [ ] **Status:** future

Interactive dialog when conflict detected — Skip / Overwrite / Rename per file.

---

### T-F07 — Optional 7-Zip Extraction Support
- [ ] **Status:** CANCELLED — replaced by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` (Microsoft-signed) supports 7z extraction on Windows 11 23H2+ without requiring a third-party binary.

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** CANCELLED — covered by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` supports RAR extraction on Windows 11 23H2+, eliminating the need for `unrar.exe`.

---

### T-F09 — CLI Core (Archiver.CLI)
- [ ] **Status:** future

Expose `Archiver.Core` as standalone CLI executable for scripting.
New project `src/Archiver.CLI/` — no logic duplication.

```
archiver archive --src C:\files --dest C:\output --name backup
archiver extract --src C:\backup.zip --dest C:\output
```

---

### T-F10 — Code Signing
- [ ] **Status:** future

**Why critical for target audience:** government/defense environments often block unsigned executables via AppLocker/WDAC. Unsigned MSIX triggers SmartScreen.

**Two levels:**
- MSIX package signature — required for sideload installs
- Authenticode on binaries — visible in file Properties → Digital Signatures

**Certificate options:**

| Option | Cost | Trust |
|--------|------|-------|
| Commercial EV (DigiCert, Sectigo) | ~$300–500/yr | Immediate SmartScreen trust |
| Standard OV | ~$100–200/yr | Trust builds over time |
| Microsoft Store | Free | Full trust, Store review required |
| Self-signed | Free | Manual install only |

For Ukrainian government deployment: self-signed with distributed root cert via Group Policy is viable for internal use.

**Acceptance criteria (when implemented):**
- [ ] All `.exe` and `.dll` binaries signed
- [ ] MSIX package signed — installs without SmartScreen warning
- [ ] Timestamp applied
- [ ] Signing in release build process
- [ ] Certificate not in repository
- [ ] `Get-AuthenticodeSignature` returns `Valid` on all binaries

---

### T-F11 — ARM64 Support
- [x] **Status:** complete

One-line change. Windows on ARM increasingly common in government/enterprise.

```xml
<!-- Before -->
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>

<!-- After -->
<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
```

No code changes required — .NET 8 JIT handles ARM64 natively.

**Acceptance criteria:**
- [x] `win-arm64` added to `RuntimeIdentifiers`
- [x] App builds for ARM64 without errors
- [x] MSIX bundle includes both architectures
- [ ] Smoke test on ARM64: archive and extract work correctly

---

### T-F12 — Parallel Compression (SeparateArchives Mode)
- [ ] **Status:** future

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

`SeparateArchives` archives are fully independent — can run in parallel.

```csharp
await Parallel.ForEachAsync(
    options.SourcePaths,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
    async (sourcePath, token) => await CreateSingleArchiveAsync(sourcePath, options, progress, token));
```

Note: `SingleArchive` mode stays sequential. Progress reporting needs `Interlocked.Increment`.

**Acceptance criteria (when implemented):**
- [ ] `SeparateArchives` uses `Parallel.ForEachAsync`
- [ ] `MaxDegreeOfParallelism` capped at `Environment.ProcessorCount`
- [ ] Progress reporting thread-safe
- [ ] `CancellationToken` respected
- [ ] `SingleArchive` unchanged
- [ ] `dotnet test` passes, no file corruption

---

### T-F13 — Process Sandbox Isolation for External Binaries
- [ ] **Status:** future
- **Depends on:** T-F07 or T-F08

**Threat model:** binary passes SHA-256 but has undiscovered vulnerability, or is compromised between verification and execution, or attempts network exfiltration or filesystem traversal.

**Layer 1 — Restricted token:**
- Create process with restricted token: no debug privileges, no driver privileges
- Drops all unnecessary privilege groups before `Process.Start`

**Layer 2 — Windows Job Object (P/Invoke):**
- `ActiveProcessLimit = 1` — cannot spawn child processes
- RAM limit 512 MB — prevent resource exhaustion
- CPU time limit — maximum runtime enforced
- UI restrictions — no clipboard, no desktop manipulation

**Layer 3 — Filesystem restriction:**
- Filesystem access limited to two directories: sandbox/input (read-only) and sandbox/output (write-only)
- All other filesystem paths denied via DACL or AppContainer policy

**Layer 4 — Network isolation:**
- Network access completely disabled for worker process
- No outbound or inbound connections permitted

**Layer 5 — WFP firewall rule:**
Added at optional component install time (requires elevation once):
```powershell
New-NetFirewallRule -DisplayName "Pakko — block 7z.exe outbound" `
    -Direction Outbound -Program "$env:LOCALAPPDATA\Pakko\tools\7z.exe" -Action Block
```
Rule removed on uninstall.

**Layer 6 — Staging directory validation:**
- Files extracted to staging directory first
- Staging output validated (path traversal check, no reparse points) before move to final destination
- TOCTOU mitigation: resolve real paths immediately before file creation
- Staging directory cleaned up on both success and failure

**Acceptance criteria (when implemented):**
- [ ] External binary process assigned to Job Object before execution
- [ ] Worker process runs with restricted token (no debug, no driver privileges)
- [ ] `ActiveProcessLimit = 1`
- [ ] RAM limit enforced (512 MB)
- [ ] CPU time limit enforced — maximum runtime applied
- [ ] UI restrictions applied
- [ ] Filesystem access limited to sandbox/input and sandbox/output only
- [ ] Network access completely disabled for worker process
- [ ] Firewall rule added at install, removed at uninstall
- [ ] Files extracted to staging directory first, validated, then moved to final destination
- [ ] TOCTOU mitigation: real paths resolved immediately before file creation
- [ ] Staging directory cleaned up on success and failure
- [ ] Job Object handle closed after process exits — no leak
- [ ] `dotnet test` passes
- [ ] Verified: spawning child process from sandboxed binary fails

---

### T-F15 — Microsoft Store Publication
- [ ] **Status:** future

**What:** Publish Pakko to Microsoft Store via Partner Center. Store handles MSIX signing, hosting, distribution, and automatic updates.

**Cost:** $0 for individual developers (as of September 2025).

**Prerequisites before submission:**
- Proper app icon in all required sizes
- About dialog with version and links (T-F14) ✓ done
- Store listing assets: screenshots, description, privacy policy URL

**Required icon sizes for Store:**
| File | Size |
|------|------|
| `StoreLogo.png` | 50×50 |
| `Square44x44Logo.png` | 44×44 |
| `Square150x150Logo.png` | 150×150 |
| `Wide310x150Logo.png` | 310×150 |
| `Square71x71Logo.png` | 71×71 |
| `Square310x310Logo.png` | 310×310 |

**Submission process:**
1. Register at storedeveloper.microsoft.com (individual, free, ID verification)
2. Create app reservation — reserve "Pakko" name
3. Build MSIX bundle (x64, optionally + arm64 per T-F11)
4. Upload to Partner Center
5. Fill Store listing: description, screenshots, category (Utilities), privacy policy
6. Submit for certification (1–3 business days)
7. Store signs the package — no separate code signing certificate needed

**Privacy policy note:**
Store requires a privacy policy URL even for apps that collect no data.
Acceptable: simple GitHub Pages page stating "Pakko collects no data."

**Automatic updates:**
Once published, Store delivers updates automatically when new version is submitted.
Version bump: increment `Package.appxmanifest` `Version` attribute before each submission.

**Acceptance criteria (when implemented):**
- [ ] Partner Center account registered (individual, free)
- [ ] App name "Pakko" reserved in Store
- [ ] All required icon sizes present in `Assets/`
- [ ] Privacy policy page published (GitHub Pages or similar)
- [ ] MSIX bundle built and uploaded
- [ ] Store listing complete: description (EN), screenshots, category
- [ ] App passes Store certification
- [ ] Published app installs and runs correctly from Store
- [ ] Version update flow tested — submit new version, confirm auto-update delivers

---

### T-F19 — Streaming Safety Audit
- [x] **Status:** complete

**What:** Verify all file I/O in ZipArchiveService uses stream-based operations
with optimal buffer size. No File.ReadAllBytes or large in-memory buffers.

**Acceptance criteria:**
- [x] All file transfers use CopyToAsync with explicit buffer size 81920 (80 KB)
- [x] No File.ReadAllBytes or File.ReadAllText in ZipArchiveService
- [x] Memory profiling on 1 GB file shows no spike beyond 2x buffer size
- [x] dotnet test passes — existing tests unchanged

---

### T-F20 — Zip64 Verification
- [x] **Status:** complete

**What:** Verify Zip64 support works correctly for large archives.
.NET supports Zip64 but behavior must be explicitly tested.

**File:** `tests/Archiver.Core.Tests/Services/ZipArchiveServiceZip64Tests.cs`

**Cost tradeoff, resolved with user:** the >65535-file tests measured ~30s each (real
`ZipArchiveService` per-entry processing cost, not fixable by optimizing test setup — tried
parallelizing file creation, saved only ~6s). Rather than pay that on every `dotnet test`, these
three tests are tagged `[Trait("Category", "Slow")]` and excluded from the routine run via
`--filter "Category!=Slow"` — the first use of this convention in the repo. See `DECISIONS.md`'s
"T-F20 — Slow Test Convention" entry for the full rationale, and `CLAUDE.md`/`TESTING.md` for the
updated standard test commands.

**>4 GiB technique:** an NTFS sparse file (`FSCTL_SET_SPARSE` via P/Invoke, test-only) lets
`SetLength` reach >4 GiB without writing real data — archived with `CompressionLevel
.NoCompression` (Stored) so an all-zero source doesn't trip our own ZIP-bomb ratio check on
extract, and so archiving/extracting doesn't spend CPU compressing 4 GiB of zeros. Falls back to
skipping gracefully if the volume doesn't support sparse files.

**Acceptance criteria:**
- [x] Test: archive with >65535 files completes without error
- [x] Test: archive >4 GB completes without error
- [x] Test: extract >4 GB archive completes without error
- [x] `dotnet test --filter "Category=Slow"` passes — 3/3, ~1m25s total (measured)
- [x] `dotnet test --filter "Category!=Slow"` (routine run) unaffected — 99/99 Core + 28/28 Shell

---

### T-F21 — Race Condition Handling During Traversal
- [x] **Status:** complete

**What:** Files may be modified or deleted during directory traversal.
FileNotFoundException during archiving should be recoverable, not fatal.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] FileNotFoundException during traversal → ArchiveError, operation continues
- [x] File deleted between scan and read → ArchiveError with clear message
- [x] No unhandled exception reaches caller
- [x] dotnet test passes — new test: file deleted mid-archive → ArchiveError

---

### T-F22 — Windows Long Path Support
- [x] **Status:** complete

**What:** Windows paths exceeding 260 characters fail silently on some APIs.
Verify Pakko handles long paths correctly.

**Acceptance criteria:**
- [x] app.manifest contains longPathAware element set to true
- [x] ZipArchiveService tested with paths >260 characters
- [x] No silent truncation or failure on long paths
- [x] dotnet test passes — new test: archive/extract with path >260 chars

---

### T-F23 — Symlink and Junction Handling
- [x] **Status:** complete

**What:** Define and implement explicit behavior for symbolic links and
NTFS junctions during archiving. Currently undefined — may cause
infinite loops or unintended file inclusion.

**Decision required before implementation:**
- Follow symlinks (include target content)
- Skip symlinks (add to SkippedFiles with reason)
- Archive link metadata only

**Decision:** Skip symlinks — added to SkippedFiles with clear reason.

**Acceptance criteria:**
- [x] Symlinks detected during directory traversal
- [x] Symlinks added to SkippedFiles with clear reason
- [x] No recursive loop on circular symlinks
- [x] NTFS junctions handled same as symlinks
- [x] dotnet test passes — new test: directory with symlink → SkippedFile

---

### T-F24 — Property-Based Archive Integrity Testing
- [x] **Status:** complete

**What:** Generate random directory trees, archive them, extract them,
and compare file hashes to verify round-trip integrity.

**File:** `tests/Archiver.Core.Tests/Services/ZipArchiveServicePropertyTests.cs`

**Found T-F75 by design:** implementing this deliberately AFTER T-F75's recursion fix landed
(rather than before) confirmed it now catches exactly the class of bug T-F75 fixed by hand — a
random deep-nesting tree round-tripped through the pre-fix code would have failed a hash
comparison at any file below the first nesting level.

**Acceptance criteria:**
- [x] Test generates random directory tree (random depth, file count, sizes) —
      `GenerateLevel` with a configurable `RandomTreeShape`
- [x] Archive → Extract → compare SHA-256 hash of every file
- [x] Tested with: all-small files, all-large files, mixed, deep nesting — four dedicated
      `[Fact]`s with distinct `RandomTreeShape` parameters, plus `ForceMaxDepth` for the
      deep-nesting case (no random early stop, so every level actually nests)
- [x] 10+ random seeds tested per run — `[Theory]`/`MemberData` over seeds 1–12
- [x] `dotnet test` passes — 99/99 (was 83/83), all 16 new tests complete in ~1s total

---

### T-F25 — README Security Positioning Review
- [x] **Status:** done

**What:** Review README.md security claims for accuracy and balance.
Avoid unverifiable superiority claims. Prefer factual positioning.

**Guidance:**
- Replace "more secure than X" with "different trust model than X"
- Emphasize: transparent architecture, managed runtime, supply chain transparency
- Keep factual CVE references — these are documented and verifiable
- Add caveat: .NET runtime itself is a trust dependency

**Acceptance criteria:**
- [x] No unverifiable security superiority claims in README
- [x] Supply chain risk section remains — factual CVE data kept
- [x] Trust model framing added: "different trust model, not superior security"
- [x] SECURITY.md unchanged — it is already appropriately nuanced

---

### T-F30 — Duplicate Filename Detection Inside Archive
- [x] **Status:** complete
- **Priority:** medium

**What:** ZIP format allows multiple entries with identical paths.
Pakko should detect and handle duplicates during both archive creation
and extraction.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Fix:** Archive side — `GetUniqueEntryName` tracks top-level entry names already claimed
(`usedEntryNames`) and renames a colliding second SourcePath ("name (1).ext"), same convention
as `GetUniqueFilePath`. Extract side — `claimedFinalPaths` tracks every final path already
claimed within the current run, closing a gap where the existing conflict check only looked at
the real destination's pre-existing state (nothing is committed there until the whole extraction
loop finishes), so a genuine duplicate entry inside one ZIP silently overwrote itself in the temp
extraction directory. `GetUniqueFilePath` gained an optional `claimedPaths` exclusion set so a
Rename target can't collide with an earlier duplicate's already-chosen rename target either.

**Acceptance criteria:**
- [x] During archive: duplicate source paths detected → second occurrence renamed (add number)
- [x] During extract: duplicate entry names detected → handled per ConflictBehavior
- [x] `dotnet test` passes — new tests: two source files/two source directories sharing a
      basename (archive side); a ZIP with a genuine duplicate entry name under Rename and under
      Skip (extract side) — 83/83 (was 79/79)

---

### T-F31 — Deterministic Archive Output
- [x] **Status:** complete
- **Priority:** medium

**What:** Sort files before archiving so identical inputs always produce
identical archives. Useful for reproducible builds and testing.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] Files sorted by full path (ordinal, case-insensitive) before archive creation
- [x] Two archive runs with identical input produce byte-identical output
- [x] dotnet test passes — new test: same input twice → identical ZIP

---

### T-F32 — Directory Traversal Ordering
- [x] **Status:** complete
- **Priority:** medium

**What:** Directory.EnumerateFiles returns files in non-deterministic order.
Sort paths before processing for consistent, predictable archive structure.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Note:** Partially overlaps T-F31 — implemented together.

**Acceptance criteria:**
- [x] EnumerateFiles results sorted by path before archiving
- [x] Archive entry order is alphabetical and consistent across runs
- [x] dotnet test passes

---

### T-F33 — Archive Verify Command
- [ ] **Status:** cancelled — integrity manifest removed; ZIP CRC-32 is sufficient

**What:** CLI command to verify archive integrity without extraction.
Checks ZIP structure and PAKKO-INTEGRITY-V1 manifest if present.

**Acceptance criteria:**
- [ ] verify command reads ZIP structure — reports corrupted entries
- [ ] If PAKKO-INTEGRITY-V1 manifest present — verifies SHA-256 per entry
- [ ] Exit code 0 = valid, 1 = invalid
- [ ] Human-readable output: per-entry status
- [ ] dotnet test passes

---

### T-F34 — Archive Metadata in ZIP Comment
- [ ] **Status:** cancelled — integrity manifest removed; ZIP CRC-32 is sufficient

**What:** Store Pakko version and creation timestamp in ZIP comment
alongside existing PAKKO-INTEGRITY-V1 manifest.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] PAKKO-VERSION written to ZIP comment on archive creation
- [ ] PAKKO-CREATED (UTC ISO 8601) written to ZIP comment
- [ ] Existing PAKKO-INTEGRITY-V1 format unchanged — new fields appended
- [ ] dotnet test passes — existing integrity tests unchanged

---

### T-F35 — Streaming Pipeline Architecture
- [ ] **Status:** future
- **Priority:** low
- **Depends on:** T-F12 (Parallel Compression)

**What:** Replace sequential file-by-file compression with a pipeline architecture that separates reading, compression, and writing into parallel stages.

**Architecture:**
```
filesystem reader → Channel<FileWorkItem> → compression workers → archive writer
```

**Implementation primitives:**
- System.Threading.Channels for work queues
- Parallel compression tasks (bounded by ProcessorCount)
- Single-threaded archive writer (ZIP format constraint)

**Expected benefit:** 2x–4x faster compression on large archives with many files.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] FileWorkItem record defined: path, entryName, bytes/stream
- [ ] Reader stage enqueues files into Channel<FileWorkItem>
- [ ] Compression workers consume channel in parallel
- [ ] Writer stage is single-threaded — ZIP format requires sequential entry writes
- [ ] CancellationToken respected in all stages
- [ ] Progress reporting thread-safe — Interlocked.Increment
- [ ] SingleArchive mode only — SeparateArchives already parallelized in T-F12
- [ ] dotnet test passes — existing archive tests unchanged
- [ ] Verified: no file corruption in parallel pipeline

---

### T-F36 — Pluggable Archive Engine Interface
- [ ] **Status:** future
- **Priority:** low
- **Depends on:** T-F04 (TAR support)

**What:** Introduce IArchiveEngine abstraction to decouple core logic from ZIP-specific implementation. Enables TAR, tar.gz, and future formats without UI changes.

**Architecture:**
```
Archiver.Core
  IArchiveEngine
    ZipEngine       ← current ZipArchiveService refactored
    TarEngine       ← T-F04
    FutureEngines
```

**UI impact:** Archive Format dropdown added to UI:
```
Format: [ ZIP ▾]   ZIP / TAR / TAR.GZ
```

**File:** `src/Archiver.Core/Interfaces/IArchiveEngine.cs` (new)

**Acceptance criteria:**
- [ ] IArchiveEngine interface defined with ArchiveAsync and ExtractAsync
- [ ] ZipArchiveService refactored to implement IArchiveEngine
- [ ] IArchiveService updated or replaced — no breaking changes to existing callers
- [ ] TarEngine stub created — ready for T-F04 implementation
- [ ] Format selector in UI — ZIP default, extensible
- [ ] DI registration updated — engine selected based on format choice
- [ ] dotnet test passes — existing 45 tests unchanged
- [ ] Adding new engine requires: new class + DI registration — no other changes

---

### T-F37 — Reparse Point Protection During Extraction
- [x] **Status:** complete

**What:** ZIP archives may contain symlinks, junctions, or mount points
that redirect file writes outside the destination directory.
System.IO.Compression does not protect against this automatically.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] Before extracting each entry, check file attributes for
      `FILE_ATTRIBUTE_REPARSE_POINT` using P/Invoke or
      `FileInfo.Attributes` after creation
- [x] Reject entries that would create symlinks or junctions
- [x] Rejected entries added to `SkippedFiles` with reason
- [x] `dotnet test` passes — no automated unit test (System.IO.Compression cannot create reparse points in fixtures); manual test required

---

### T-F38 — Alternate Data Streams Protection
- [x] **Status:** complete

**What:** NTFS Alternate Data Streams allow hiding executable payloads
in filenames containing `:` (e.g. `file.txt:payload.exe`).
ZIP archives may contain such entries.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] Reject any ZIP entry whose name contains `:`
- [x] Rejected entries added to `SkippedFiles` with reason
      `"Alternate Data Stream entry rejected for security"`
- [x] `dotnet test` passes — new test: entry with `:` in name is skipped

---

### T-F39 — Reserved Windows Filename and Control Character Filtering
- [x] **Status:** complete

**What:** Windows reserved device names (`CON`, `PRN`, `NUL`, `COM1`–`COM9`,
`LPT1`–`LPT9`) and filenames containing control characters (`0x00`–`0x1F`)
can cause unpredictable behavior or security issues during extraction.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] Reject entries whose filename (without path) matches reserved
      Windows names (case-insensitive, with or without extension)
- [x] Reject entries whose filename contains control characters `0x00`–`0x1F`
- [x] Rejected entries added to `SkippedFiles` with descriptive reason
- [x] `dotnet test` passes — new tests for reserved names and control chars

---

## v1.2 — Shell Extension

> **Minimum supported OS:** Windows 10 1809 (10.0.17763.0).
> Shell extension uses dual registration:
> - `desktop4:FileExplorerContextMenus` — Win10 1809+, classic context menu
> - `IExplorerCommand` via COM — Win11 22000+, modern context menu
>
> Both mechanisms invoke `Archiver.Shell.exe`. No separate code paths needed.

---

### T-F53 — Archiver.Shell Project
- [x] **Status:** complete (v1.2)

**What:** New lightweight WinExe project (`src/Archiver.Shell/`, net8.0-windows). Entry point for all shell-triggered operations. References `Archiver.Core` only — no WinUI dependency. No console window (`<OutputType>WinExe</OutputType>`).

**CLI arguments:**
```
Archiver.Shell.exe --extract-here "file.zip" ["file2.zip"]
Archiver.Shell.exe --extract-folder "file.zip"
Archiver.Shell.exe --archive "file1" "file2" ["file3"]
Archiver.Shell.exe --open-ui --extract "file.zip"
Archiver.Shell.exe --open-ui --archive "file1" "file2"
```

**Silent operation flow** (`--extract-here`, `--extract-folder`, `--archive`):
1. Parse arguments
2. Launch `Archiver.ProgressWindow` with operation parameters
3. Run `Archiver.Core` operation, pipe `ProgressReport` to ProgressWindow via named pipe
4. Wait for completion; ProgressWindow shows live progress and Cancel button

**Open-UI flow** (`--open-ui`):
1. Parse arguments
2. Launch `Archiver.App` via `pakko://` URI with base64-encoded args (T-F56)
3. Exit immediately

**Acceptance criteria:**
- [x] `Archiver.Shell` project added to solution, `net8.0-windows`, references `Archiver.Core`
- [x] All argument combinations parsed and dispatched correctly
- [x] `--extract-here`: extracts each ZIP to its own directory (T-14 smart folder logic)
- [x] `--extract-folder`: creates `<archive_name>\` subfolder and extracts into it
- [x] `--archive`: archives all passed paths into a single ZIP next to the first item
- [x] `--open-ui --extract`: launches `Archiver.App` via `pakko://` and exits
- [x] `--open-ui --archive`: launches `Archiver.App` via `pakko://` and exits
- [x] No console window shown (`WinExe` output type)
- [x] `dotnet build src/Archiver.Shell` passes

---

### T-F54 — Archiver.ProgressWindow Project
- [x] **Status:** complete
- **Depends on:** T-F53

**What:** Minimal WinUI 3 project (`src/Archiver.ProgressWindow/`, net8.0-windows). Single small window showing live progress for silent shell operations. Receives operation parameters from `Archiver.Shell` via named pipe.

**Window contents (only):**
- Title bar: operation name (e.g. "Extracting archive.zip")
- Progress bar
- Speed / ETA text line
- Cancel button

No file list, no options, no tray icon. Window is non-resizable, fixed size (~400×120 px).

**Lifecycle:**
- Auto-closes 1.5 seconds after successful completion
- On failure: shows simple error dialog, stays open until dismissed

**Acceptance criteria:**
- [x] `Archiver.ProgressWindow` project added to solution, `net8.0-windows`
- [x] Window appears with operation name, progress bar, speed/ETA, Cancel button
- [x] Receives progress updates via named pipe from `Archiver.Shell`
- [x] Cancel button signals cancellation back to `Archiver.Shell`
- [x] Auto-closes 1.5 sec after success
- [x] Error dialog shown on failure; window stays open until dismissed
- [x] Window is non-resizable, fixed ~400×120 px
- [x] `dotnet build src/Archiver.ProgressWindow` passes

---

### T-F55 — Dual Shell Registration
- [~] **Status:** partial (v1.2) — manifest declarations written then temporarily reverted

> **Note:** COM registration (`com:Extension`) and context menu binding (`desktop4:Extension`)
> were written and then removed from `Package.appxmanifest` because Explorer hangs on
> right-click when `Archiver.Shell.exe` does not implement `IExplorerCommand`. Restore both
> blocks after T-F61 is complete.
- **Depends on:** T-F53

**What:** Register Pakko's context menu via two mechanisms declared in `Package.appxmanifest`, both targeting `Archiver.Shell.exe`. Windows automatically uses the appropriate mechanism per OS version — no separate code paths needed.

**Mechanism 1 — `desktop4:FileExplorerContextMenus`** (Win10 1809+):
- Appears in classic context menu
- Works on Windows 10 and Windows 11 ("Show more options")

**Mechanism 2 — `com:Extension` + `IExplorerCommand`** (Win11 22000+):
- Appears directly in modern context menu
- No "Show more options" click required on Windows 11

**Context menu structure:**

Right-click on `.zip` file(s):
```
Pakko ►
  Extract here
  Extract to "<folder_name>"
  ─────────────────
  Extract with Pakko...
```

Right-click on non-ZIP files/folders:
```
Pakko ►
  Add to "<name>.zip"
  ─────────────────
  Archive with Pakko...
```

Right-click on mixed selection:
```
Pakko ►
  Add to "<name>.zip"
  Extract ZIPs here
  ─────────────────
  Open with Pakko...
```

**Acceptance criteria:**
- [x] `desktop4:FileExplorerContextMenus` entry declared in `Package.appxmanifest` (Win10+)
- [x] `com:Extension` + `IExplorerCommand` entry declared in `Package.appxmanifest` (Win11)
- [x] Both entries invoke `Archiver.Shell.exe` with correct arguments
- [ ] Context menu appears on Win10 (classic menu) after MSIX install
- [ ] Context menu appears on Win11 (modern menu) after MSIX install
- [ ] `.zip` files show Extract submenu; non-ZIP files show Archive submenu
- [ ] Mixed selection shows combined submenu
- [ ] Uninstall removes both context menu registrations cleanly

---

### T-F56 — Protocol Activation (pakko://)
- [x] **Status:** complete
- **Depends on:** T-F53

**What:** Register `pakko://` URI scheme in `Package.appxmanifest`. `Archiver.App` handles activation by parsing the URI and pre-loading the UI. Used by `Archiver.Shell` for `--open-ui` operations.

**URI format:**
```
pakko://extract?files=<base64-encoded-json-array>
pakko://archive?files=<base64-encoded-json-array>
```

**Acceptance criteria:**
- [x] `pakko://` URI scheme registered in `Package.appxmanifest`
- [x] `Archiver.App` handles `pakko://extract?files=...` — opens with ZIPs pre-loaded
- [x] `Archiver.App` handles `pakko://archive?files=...` — opens with source files pre-loaded
- [x] Both cold-start and warm activation (already running) handled via `AppInstance.Activated`
- [x] `Archiver.Shell --open-ui --extract "file.zip"` correctly constructs and launches the URI
- [x] `Archiver.Shell --open-ui --archive "file1" "file2"` correctly constructs and launches the URI
- [x] `dotnet build src/Archiver.App` passes

---

### T-F57 — Shell Integration Tests
- [x] **Status:** complete
- **Depends on:** T-F53

**What:** Basic smoke tests for `Archiver.Shell` argument parsing logic. No UI tests — `Archiver.ProgressWindow` is tested manually.

**Acceptance criteria:**
- [x] `Archiver.Shell` argument parsing extracted into a testable class
- [x] Tests cover: `--extract-here`, `--extract-folder`, `--archive`, `--open-ui --extract`, `--open-ui --archive`
- [x] Tests cover: missing arguments → graceful error (no crash)
- [x] Tests cover: multiple file arguments parsed correctly
- [x] `dotnet test` passes

---

### T-F40 — Shell Extension Registration (Dual Mechanism)
- [~] **Status:** partial (v1.2) — MSIX installs with all three EXEs present
- **Depends on:** T-F53, T-F55

> **Note:** `Archiver.Shell.exe` and `Archiver.ProgressWindow.exe` confirmed present in the
> installed package alongside `Archiver.App.exe`. Context menu functionality is blocked on
> `IExplorerCommand` implementation (T-F61). COM and context menu manifest entries restored
> once T-F61 is complete.

**What:** Complete dual-mechanism shell registration wired to `Archiver.Shell.exe`. Validates that both `desktop4:FileExplorerContextMenus` (Win10) and `IExplorerCommand` via COM (Win11) registrations work end-to-end after MSIX install.

**Note:** Registration declarations are written in T-F55. This task covers end-to-end validation — install, verify menu appearance on both OS versions, verify uninstall cleanup.

**Acceptance criteria:**
- [x] MSIX installs without errors on Windows 10 1809+
- [x] MSIX installs without errors on Windows 11 22000+
- [x] `Archiver.Shell.exe` and `Archiver.ProgressWindow.exe` present in installed package alongside `Archiver.App.exe`
- [ ] Context menu entry visible in classic menu on Win10 (right-click → menu appears) — requires IExplorerCommand implementation
- [ ] Context menu entry visible in modern menu on Win11 (no "Show more options" needed) — requires IExplorerCommand implementation
- [ ] Invoking any menu item launches `Archiver.Shell.exe` with correct arguments — requires IExplorerCommand implementation
- [ ] Uninstall removes both registration entries cleanly — no orphan registry keys

---

### T-F61 — IExplorerCommand Implementation
- [x] **Status:** complete (v1.2)
- **Depends on:** T-F53, T-F55

**What:** In-process COM DLL (`Archiver.ShellExtension.dll`) implementing `IExplorerCommand` via
WRL. Registered as `com:SurrogateServer` in `Package.appxmanifest` (runs inside an isolated
`dllhost.exe`; see "Correction — SurrogateServer" in `DECISIONS.md`). Launches `Archiver.Shell.exe`
via `CreateProcess` from `Invoke`.

**Projects:**
- `src/Archiver.ShellExtension/` — C++ DLL (x64 + ARM64, static CRT /MT)
- `tests/Archiver.ShellExtension.Tests/` — C++ test EXE (Google Test via NuGet)

**Acceptance criteria:**
- [x] Architecture documented in `DECISIONS.md` before code written
- [x] `Archiver.ShellExtension` C++ DLL project added to solution
- [x] `Archiver.ShellExtension.Tests` C++ test project added to solution
- [x] `IExplorerCommand` implemented for `PakkoRootCommand`, `ExtractHereCommand`,
      `ExtractFolderCommand`, `ArchiveCommand` via WRL
- [x] Dynamic submenu: ZIP files → Extract here + Extract to folder; non-ZIP/mixed → Add to archive
- [x] Selected file paths extracted from `IShellItemArray` via `GetPathsFromShellItemArray`
- [x] `com:Extension` + `desktop4:Extension` restored in `Package.appxmanifest` (unblocks T-F55)
- [x] DLL included in MSIX via `Content Include` in `Archiver.App.csproj`
- [x] `Deploy.ps1` builds DLL via MSBuild before `dotnet publish`
- [x] `GetIcon`/`GetToolTip` return `E_NOTIMPL` (not `S_FALSE`) when no value is provided —
      fixed a real `explorer.exe` crash; see `DECISIONS.md`
- [x] `Archiver.Shell.exe`/`Archiver.ProgressWindow.exe` declared as their own `<Application>`
      entries (`AppListEntry="none"`) — Windows blocks `CreateProcess` of undeclared EXEs
      inside the package; see `DECISIONS.md`
- [x] `Archiver.Shell.exe`/`Archiver.ProgressWindow.exe` built self-contained (not
      framework-dependent) and ship their `.dll`/`.deps.json`/`.runtimeconfig.json` via
      `Content Include` — see `DECISIONS.md`
- [x] **Manual smoke test:** Explorer does not hang/crash on right-click after `Deploy.ps1`
- [x] **Manual smoke test:** "Pakko ▶" submenu appears in context menu, with icon
- [x] **Manual smoke test:** Extract here invokes `Archiver.Shell.exe` and files are actually
      extracted (verified 2026-07-04 via direct `Start-Process` + event log + disk check)
- [x] **Manual smoke test:** Extract to folder / Add to archive commands verified end-to-end
      (verified 2026-07-05 — both commands create/extract archives correctly, no progress
      bar shown, see known gap below)
- [x] **Known gap — resolved via T-F65:** `Archiver.ProgressWindow.exe` (separate WinUI 3 process)
      is deleted entirely; progress UI now runs in-process via `IProgressDialog`, confirmed
      working end-to-end (2026-07-05). No longer a gap.
- [x] **Manual smoke test:** `Type="Directory"` shows menu on folder right-click (verified
      2026-07-05)
- [x] `Archiver.ShellExtension.Tests.exe` passes all Google Test cases (23/23, verified
      2026-07-05 — built via `MSBuild tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj`
      with explicit `/p:SolutionDir=<repo root>\`, since `$(SolutionDir)` is only set
      automatically when building through the `.sln`)

**Manual end-to-end smoke test procedure (step 8):**
1. Run `.\scripts\Deploy.ps1` — confirm no build errors; MSIX installs successfully.
2. Open File Explorer; right-click a `.zip` file.
   - Confirm "Pakko ▶" submenu appears with "Extract here" and "Extract to folder…".
   - Click "Extract here" → `Archiver.ProgressWindow` appears; extraction completes.
3. Right-click a non-ZIP file (e.g., `.txt`).
   - Confirm "Pakko ▶" submenu appears with "Add to archive…".
   - Click "Add to archive…" → `Archiver.ProgressWindow` appears; archive is created.
4. Right-click a folder.
   - Confirm "Pakko ▶" submenu appears. If `Type="Directory"` is not supported by the OS
     version, remove that entry from `Package.appxmanifest` and re-run `Deploy.ps1`.
5. Select a mixed set (one ZIP + one non-ZIP); right-click.
   - Confirm only "Add to archive…" appears (mixed → archive only).
6. Uninstall Pakko via `Get-AppxPackage *Pakko* | Remove-AppxPackage`.
   - Confirm no orphan context menu entries remain.

**Test project setup (NuGet restore required before first build):**
```
nuget restore tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj
  -SolutionDirectory .
```
Then build and run `tests\Archiver.ShellExtension.Tests\bin\x64\Debug\Archiver.ShellExtension.Tests.exe`.

---

### T-F41 — Context Menu: Extract Here
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see note above T-F62**; already
      implemented as `ExtractHereCommand` and smoke-tested. Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Extract here" command on ZIP files — extracts to same folder as archive. Runs silently via `Archiver.Shell.exe --extract-here`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of `.zip` files
- [ ] Invokes `Archiver.Shell.exe --extract-here "<path>"` for each selected ZIP
- [ ] Extraction runs silently — `Archiver.ProgressWindow` shows progress (T-F54)
- [ ] Extracts to same directory as archive (T-14 smart folder logic)
- [ ] Multi-selection: all selected ZIPs extracted in a single `Archiver.Shell` invocation
- [ ] `Archiver.ProgressWindow` auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

### T-F42 — Context Menu: Extract to Folder
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see note above T-F62**; already
      implemented as `ExtractFolderCommand` and smoke-tested. Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Extract to `<folder_name>`" on ZIP files — creates a named subfolder automatically. Runs silently via `Archiver.Shell.exe --extract-folder`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of `.zip` files
- [ ] Invokes `Archiver.Shell.exe --extract-folder "<path>"` for each selected ZIP
- [ ] Creates `<archive_name>\` subfolder next to archive; extracts into it
- [ ] Multi-selection: each ZIP gets its own named subfolder
- [ ] `Archiver.ProgressWindow` shows progress, auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

### T-F43 — Context Menu: Archive with Pakko
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see note above T-F62**; already
      implemented as `ArchiveCommand` and smoke-tested (label/naming gap tracked separately
      as T-F64). Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Add to `<name>.zip`" on any files/folders — single archive, Fast compression, destination = source folder. Runs silently via `Archiver.Shell.exe --archive`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of any files/folders
- [ ] Invokes `Archiver.Shell.exe --archive "file1" "file2" ...`
- [ ] Creates single `.zip` archive next to the first selected item
- [ ] Uses Fast compression level
- [ ] Supports multi-selection (all selected items passed in one invocation)
- [ ] `Archiver.ProgressWindow` shows progress, auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

### T-F44 — File Type Association
- [x] **Status:** complete

**What:** Register `.zip` file association in `Package.appxmanifest`. Double-click opens archive in Pakko.

**Acceptance criteria:**
- [x] `.zip` association declared in appxmanifest
- [x] Double-click on `.zip` opens Pakko with archive loaded
- [x] Association registered/unregistered with MSIX install/uninstall

---

### T-F45 — Mark of the Web (MOTW) Propagation
- [x] **Status:** complete

**What:** On extraction, read `Zone.Identifier` ADS from the source archive and write it to every extracted file. Always on by default — cannot be disabled by user (only via GPO in v1.4).

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Implementation:** `FileStream` opened with ADS path `"<file>:Zone.Identifier"`. No P/Invoke required — NTFS ADS is accessible via standard `FileStream` on Windows.

**Acceptance criteria:**
- [x] `Zone.Identifier` ADS read from source archive before extraction
- [x] `Zone.Identifier` ADS written to each extracted file
- [x] If source archive has no `Zone.Identifier`, skip silently (no error)
- [x] Always on — no user setting to disable
- [x] `dotnet test` passes — new test: extracted files inherit `Zone.Identifier` from archive

---

### T-F46 — File Hash Viewer
- [ ] **Status:** future (v1.2)

**What:** Select file(s) → show SHA-256 hash in UI. Useful for integrity verification before opening extracted files.

**Acceptance criteria:**
- [ ] File picker → show SHA-256 hash of selected file(s)
- [ ] UI only — no new service methods required
- [ ] Hash computed via `System.Security.Cryptography.SHA256`

---

## Context Menu — NanaZip Parity Review (2026-07-04)

Per project direction, NanaZip is the reference implementation for what the Pakko context
menu should offer. Reviewed NanaZip's actual modern (`IExplorerCommand`-based) shell
extension source —
[`NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`](https://github.com/M2Team/NanaZip/blob/main/NanaZip.UI.Modern/NanaZip.ShellExtension.cpp)
— the direct architectural equivalent of `Archiver.ShellExtension`, not the legacy classic
`IContextMenu` implementation (`NanaZip.UI.Classic/.../ContextMenu.cpp`), which is
irrelevant here per this project's `IExplorerCommand`-only constraint.

**NanaZip's full modern-menu command set** (flat list, no separate folder/file/mixed
submenus — conditions are evaluated per-command against the selection, not via distinct
menu trees):

| Command | Condition | Pakko status |
|---|---|---|
| Open | single file, needs extraction | done differently — double-click file association (T-F44); no explicit context-menu verb |
| Test | ≥1 file needs extraction | **missing** — see T-F62 |
| Extract (dialog, picks destination) | ≥1 file needs extraction | **missing** — see T-F63 |
| Extract Here | ≥1 file needs extraction | done — `ExtractHereCommand` (already smart: `SeparateFolders` mode strips/wraps as needed, equivalent to NanaZip's separate "Extract Here (Smart)") |
| Extract Here (Smart) | ≥1 file needs extraction | n/a — folded into Pakko's "Extract here" above, not a separate verb |
| Extract to "\<folder\>" | ≥1 file needs extraction | done — `ExtractFolderCommand` |
| Compress (dialog, format/options) | any selection | **missing** — see T-F63 |
| Compress to "\<name\>.zip" (one click) | any selection | done, but see T-F64 (label says "Add to archive…" though behavior is already the one-click no-dialog path) |
| Compress to "\<name\>.7z" | any selection | out of scope — 7z creation forbidden (`CLAUDE.md`: ZIP only, no third-party compression code) |
| Compress + Email variants (×4) | any selection | **out of scope, deliberately** — mail client integration adds attack surface and a dependency the gov/defense trust model doesn't need; not tracked as a task |
| CRC/Checksum submenu (CRC-32/64, SHA-1/256/384/512, BLAKE2/3, etc.) | any selection | covered by existing T-F46 (File Hash Viewer), which already targets SHA-256; T-F46 is in-app UI only today, not a context-menu verb — cross-referenced, no new task |

**Note on T-F41/T-F42/T-F43:** these three older task entries (below, still `future`/unchecked)
describe "Extract Here", "Extract to Folder", and "Archive with Pakko" as if unimplemented.
They predate T-F61 and are now superseded by it — all three behaviors are implemented and
smoke-tested there. Left in place with a note rather than deleted, per the "never silently
deprecate" rule; do not re-implement them as new work.

---

### T-F62 — Context Menu: Test Archive (Integrity Check)
- [x] **Status:** complete (v1.2) — manual Explorer smoke test passed 2026-07-06
- **Depends on:** T-F61

**What:** "Test archive" command — verifies every entry's CRC-32 without writing any files to
disk. Modeled on NanaZip's `Test` verb (`IDS_CONTEXT_TEST`), which appears whenever the
selection contains at least one archive.

**Why this one and not the CRC/checksum submenu:** NanaZip's checksum submenu hashes
arbitrary files for user-facing comparison; "Test" instead validates that an archive's
*own* declared checksums match its contents — a distinct, extraction-adjacent operation
that fits `IArchiveService` naturally (`IArchiveService` currently only has `ArchiveAsync`/
`ExtractAsync` — no verify method exists yet).

**Acceptance criteria:**
- [x] New `TestAsync` (or similarly named) method on `IArchiveService` — reads every entry,
      computes CRC-32, compares against the entry's declared value, never writes to disk
- [x] Appears in Pakko submenu whenever selection contains ≥1 `.zip` — `TestCommand::GetState`
      uses `AnyPathIsZip` (not `AllPathsAreZip`), so it shows even on a mixed selection
- [x] Runs silently via `Archiver.Shell.exe --test "<path>"`; result shown via the same
      `IProgressDialog`/`ShowErrorSummary` path Extract/Archive use (`Archiver.ProgressWindow`
      no longer exists, see T-F65) plus a new "No errors detected" confirmation on success,
      since Test has no visible on-disk result to imply success
- [x] Multi-selection: all selected archives tested in one invocation
- [x] `dotnet test` passes — new tests: corrupted-CRC fixture fails (`corrupted_crc_stored.zip`,
      a Stored entry with a post-write byte flip), valid fixture passes, encrypted archive
      errors, mixed valid+corrupted selection reports only the corrupted one
      (Archiver.Core.Tests 77/77, was 73/73; Archiver.Shell.Tests 28/28, was 25/25 — new `--test`
      parser cases)
- [x] C++ side: `TestCommand` (`ExplorerCommands.h/.cpp`) + `BuildTestArgs`
      (`ShellExtUtils.h/.cpp`), wired first in `PakkoRootCommand::EnumSubCommands`
      (Google Test 33/33, was 30/30)
- [x] **Manual smoke test:** after `Deploy.ps1`, "Test archive" appears on a `.zip` and on a
      mixed selection; running it on a valid archive shows "No errors detected"; on a corrupted
      one shows the CRC-mismatch error. Verified 2026-07-06 via Explorer UI automation (Windows
      MCP): single `.zip` submenu order is Extract here / Extract to folder / Test archive; mixed
      `.zip`+`.txt` selection shows Add to "\<name\>.zip" / Test archive — both confirm the
      diagnostic-after-primary-action ordering rule. `valid_archive.zip` → "No errors detected in
      the archive(s)." `corrupted_crc_stored.zip` → "Entry 'file.txt' failed CRC-32 check
      (expected BA4A5016, got 7884F662)."

---

### T-F63 — Context Menu: "Extract…" and "Compress…" with Dialog
- [x] **Status:** complete (v1.2) — verified end-to-end on-device 2026-07-06, after fixing T-F83
      (a pre-existing cold-start activation bug this task's manual test surfaced)
- **Depends on:** T-F61

**What:** Two additional leaf commands that open the full Pakko UI instead of running
silently — matching NanaZip's dialog-based `Extract` (`IDS_CONTEXT_EXTRACT`) and `Compress`
(`IDS_CONTEXT_COMPRESS`) verbs, which let the user pick a destination folder / compression
options instead of accepting the auto-derived defaults `ExtractHereCommand`/
`ExtractFolderCommand`/`ArchiveCommand` use today. This is also exactly what the original
T-F01 design (see its historical entry above) called "Archive with Pakko…" / "Extract with
Pakko…" — never implemented under the old sparse-package/`IContextMenu` plan T-F01 was
superseded by; this task re-introduces the same idea under the current
`IExplorerCommand`/T-F61 architecture.

**Already-existing plumbing to reuse — do not duplicate:** `Archiver.Shell`'s
`ShellArgumentParser` already parses `--extract` → `CommandType.OpenUiExtract` and
`--archive` (dialog form) → `CommandType.OpenUiArchive`; `Program.cs`'s `LaunchOpenUi`
already launches `Archiver.App` via the `pakko://` protocol with the selected files
pre-loaded (T-F56). The only missing piece is wiring two new `IExplorerCommand` leaf
classes in `Archiver.ShellExtension` to invoke `Archiver.Shell.exe --extract`/`--archive`
(dialog form) — no new backend work.

**Acceptance criteria:**
- [x] New leaf command "Extract…" — shown whenever selection contains ≥1 `.zip`; invokes
      `Archiver.Shell.exe --open-ui --extract "<path>..."` — `ExtractDialogCommand`, gated on
      `AnyPathIsZip` (same as `TestCommand`, not the stricter `AllPathsAreZip` used by
      `ExtractHereCommand`/`ExtractFolderCommand`)
- [x] New leaf command "Compress…" — shown for any selection; invokes
      `Archiver.Shell.exe --open-ui --archive "<path>..."` (dialog form, not the silent one) —
      `CompressDialogCommand`, unconditionally `ECS_ENABLED`
- [x] Both open `Archiver.App` with the files pre-loaded via the existing `pakko://` flow —
      required fixing T-F83 first (cold-start activation silently dropped the payload); verified
      working after that fix
- [x] `GetState` filtering matches the sibling silent commands (same `AllPathsAreZip`/
      `AnyPathIsZip` helpers)
- [x] Menu ordering verified against NanaZip's actual shipped source
      (`NanaZip.UI.Modern/SevenZip/CPP/7zip/UI/Explorer/ContextMenu.cpp`, fetched via the GitHub
      trees API per `CLAUDE.md`'s pre-implementation-research rule): the dialog-based command
      precedes its one-click siblings in both groups (`kExtract` before `kExtractHere`/
      `kExtractTo`; `kCompress` before `kCompressToZip`). Pakko's `EnumSubCommands` order is now
      `ExtractDialog, ExtractHere, ExtractFolder, CompressDialog, Archive, Test` — Test still last
      per Pakko's own deliberate deviation (primary actions before diagnostic, `CLAUDE.md` hard
      constraint), unlike NanaZip's own Test-before-Compress grouping
- [x] `Archiver.ShellExtension.Tests` gains `BuildOpenUiExtractArgs`/`BuildOpenUiArchiveArgs` cases
      — 43/43 (was 39/39)
- [x] `dotnet test --filter "Category!=Slow"` passes — 135/135 unaffected (no C# changes besides
      the T-F83 fix in `Archiver.App`, which has no automated test coverage — WinUI activation
      can't be unit-tested; see T-F83)
- [x] **Manual smoke test:** verified 2026-07-06 via Explorer UI automation (Windows MCP) after
      `Deploy.ps1` (v1.1.0.40+, includes the T-F83 fix) — right-clicking a `.zip` shows "Extract…",
      "Extract here", "Extract to \"\<name\>\\\"", "Compress…", "Test archive" in that order, no
      mojibake; clicking "Extract…" launches Pakko with the archive pre-loaded in the file list

---

### T-F64 — Context Menu: Fix "Add to archive…" Label vs One-Click Behavior
- [x] **Status:** complete (v1.2) — all acceptance criteria verified 2026-07-05, including manual
      smoke test (see below); status line was stale, not the task itself
- **Depends on:** T-F61

**What:** `ArchiveCommand`'s current title is "Add to archive…" (an ellipsis conventionally
signals a dialog will follow), but its actual behavior is NanaZip's one-click,
no-dialog "Compress to \"\<name\>.zip\"" path — it archives immediately with an
auto-derived name and no user prompt. This is a label/expectation mismatch, not a
functionality gap.

**Fix:** rename the leaf command's title to match its real behavior, e.g. dynamically
build `"Add to \"<name>.zip\""` (mirroring NanaZip's exact pattern — the name is computed
from the first selected item, same logic `RunArchiveAsync` already uses), removing the
ellipsis. Once T-F63 adds a real dialog-based "Compress…", the two commands will be
correctly distinguished the same way NanaZip distinguishes its one-click and dialog
variants.

**Acceptance criteria:**
- [x] `ArchiveCommand::GetTitle` returns `Add to "<name>.zip"` using the same name-derivation
      logic as `Program.cs`'s `RunArchiveAsync` (first selected item's base name) — implemented
      as `BuildAddToArchiveTitle` in `ShellExtUtils.cpp`, using `psia` (previously ignored)
- [x] Long names truncated in the middle (`head…tail`) before display — a 255-char folder name
      would otherwise make the "Pakko" submenu absurdly wide. Cap: 40 chars total (22 head + 15
      tail), `.zip` always fully visible. See `TruncateMiddle` in `ShellExtUtils.cpp`.
- [x] No behavior change — still archives immediately, no dialog (`Invoke`/`GetState` untouched)
- [x] **Manual smoke test:** title updates correctly for single vs multi-selection, and for a
      long folder/file name (verified 2026-07-05 via `Deploy.ps1` + Explorer right-click,
      after fixing the mojibake bug below)

**Verified 2026-07-05:** `Archiver.ShellExtension.Tests.exe` 30/30 (7 new `BuildAddToArchiveTitle`
cases: empty vector fallback, single file, multi-selection uses first path only, folder with no
extension, leading-dot filename like `.gitignore` not treated as an extension, name exactly at
the 40-char limit passes through unchanged, name over the limit truncated head/tail). `dotnet
test` 95/95 unaffected (no C# changes). MSIX built + installed via `Deploy.ps1` per new
CLAUDE.md workflow rule.

**Bug found and fixed during on-device smoke test:** the first build (v1.1.0.20) showed
`Add to "raspberry-pi-5-case-мовЂ¦1111111111111111.zip"` instead of the expected
`...case-mo…11111111.zip` — the ellipsis character was mojibake'd into three garbage bytes.
Root cause: `ShellExtUtils.cpp` was saved as UTF-8 **without a BOM** and contained a literal
`…` glyph (not an escape sequence). Without a BOM, MSVC falls back to the system's active code
page (Windows-1251 on this Cyrillic/Ukrainian-locale machine) to decode the source file, so the
3-byte UTF-8 sequence for `…` (`E2 80 A6`) got decoded as three separate cp1251 characters
(`в`, `Ђ`, `¦`) instead of one Unicode code point. The pre-existing `ExtractFolderCommand` title
never hit this because it already used the `…` escape (verified: pure-ASCII source, immune
to source-encoding assumptions). Fixed by replacing the literal glyph with `…` in both
`ShellExtUtils.cpp` and `ShellExtUtilsTests.cpp` (the test file had the same latent bug, but it
went undetected because both sides of `EXPECT_EQ` were mis-decoded identically). Verified fix
on-device: v1.1.0.21 shows the title correctly. See `CONVENTIONS.md`'s new "non-ASCII string
literals" rule.

---

### T-F65 — Fix Archiver.ProgressWindow.exe App.xbf Resource Collision
- [x] **Status:** complete (v1.2) — **not fixed as titled; the underlying design was removed.**
      The "App.xbf collision" theory was disproven (identical crash with zero XAML — see
      `DECISIONS.md` "Progress UI: IProgressDialog replaces Archiver.ProgressWindow" for the
      full disproof and NanaZip-verified reasoning). `Archiver.ProgressWindow` (separate WinUI 3
      `.exe` + named-pipe IPC) is deleted entirely; `Archiver.Shell` now shows progress via the
      Windows Shell's built-in `IProgressDialog` COM object, in-process, no second EXE, no IPC.
- **Depends on:** T-F61

**What:** `Archiver.ProgressWindow.exe` never actually launched after a shell-triggered
operation — it crashed at WinUI/WindowsAppRuntime init (`Application Error`,
`Microsoft.UI.Xaml.dll`, `0xc000027b`) regardless of its XAML content. `RunWithProgressWindowAsync`
in `Archiver.Shell`'s `Program.cs` degraded gracefully (falls back to running the operation
silently within 5 seconds), so extraction/archiving always succeeded — just with no visual
feedback. This is now moot: `Archiver.Shell` no longer spawns a second process for progress UI.

**File:** `src/Archiver.Shell/NativeProgressDialog.cs` (new), `src/Archiver.Shell/Program.cs`,
`src/Archiver.App/Package.appxmanifest`, `src/Archiver.App/Archiver.App.csproj`,
`scripts/Deploy.ps1`, `windows-archiver-wrapper.sln` — `src/Archiver.ProgressWindow/` deleted.

**Acceptance criteria:**
- [x] Root cause investigated — the `App.xbf` collision theory disproven via a no-XAML rewrite
      that crashed identically; real HRESULT never obtained (would need WinDbg/`dotnet-dump`,
      unavailable in the diagnosing session) but made moot by removing the second-process design
- [x] `NanaZip` source verified (`M2Team/NanaZip`, `NanaZip.Modern.cpp`/`ProgressPage.xaml`) per
      `CLAUDE.md`'s pre-implementation-research rule — confirmed it never spawns a second WinUI
      process either; progress UI is a `Page` inside its single `App.xaml`, invoked in-process
- [x] `Archiver.Shell` shows progress via `IProgressDialog` (`CLSID_ProgressDialog`), in-process,
      no separate `.exe`
- [x] Cancel wired via `IProgressDialog.HasUserCancelled()` → existing `CancellationToken` plumbing.
      **Bug found during manual verification:** Cancel appeared to do nothing (operation ran to
      completion regardless). Root cause: `HasUserCancelled` is the only method on
      `IProgressDialog` that returns a plain `BOOL`, not `HRESULT` (per `shobjidl.h`) — without
      `[PreserveSig]` on the interop declaration, the marshaller assumes the HRESULT convention
      and misreads the return value, so the call always came back `false`. Fixed by adding
      `[PreserveSig]` in `NativeProgressDialog.cs`.
- [x] Progress dialog shows the current file name (line 1) plus percent/bytes (line 2) — added
      `ProgressReport.CurrentFile`, threaded through `ProgressStream` and both `ZipArchiveService`
      call sites (archive entry write, extract entry write), per user feedback on the first
      on-device screenshot ("яка назва поточного файлу")
- [x] `dotnet test` passes (95/95) — no functional regression in `Archiver.Shell`/`Archiver.Core`
- [x] Manual smoke test: Extract here / Extract to folder / Add to archive all show the shell
      progress dialog with file name + percent/bytes and a working Cancel button, after
      `Deploy.ps1` install (confirmed 2026-07-05)

---

## v1.3 — tar.exe Integration

### T-F47 — ITarService Interface and TarCapabilities
- [ ] **Status:** future (v1.3)

**What:** Add `ITarService` interface and `TarCapabilities` record to `Archiver.Core`. `TarProcessService` implements `ITarService`. Register in DI.

**File:** `src/Archiver.Core/Interfaces/ITarService.cs`, `src/Archiver.Core/Models/TarCapabilities.cs`, `src/Archiver.Core/Services/TarProcessService.cs`

**Acceptance criteria:**
- [ ] `TarCapabilities` record defined with `SupportsRar`, `Supports7z`, `SupportsZstd`, `SupportsXz`, `SupportsLzma`, `SupportsBz2`, `Version` properties
- [ ] `ITarService` interface defined with `DetectCapabilitiesAsync()` and `ExtractAsync()`
- [ ] `TarProcessService` class created (implementation in T-F48/T-F49)
- [ ] DI registration added
- [ ] `dotnet build src/Archiver.Core` passes

---

### T-F48 — tar.exe Capability Detection
- [ ] **Status:** future (v1.3)

**What:** At app startup, run `C:\Windows\System32\tar.exe --version` to detect version and probe which formats are supported. Cache result as `TarCapabilities` singleton. UI greys out unsupported formats with tooltip "Requires Windows 11 23H2+".

**Acceptance criteria:**
- [ ] `DetectCapabilitiesAsync()` runs `C:\Windows\System32\tar.exe --version` (absolute path)
- [ ] Parses version string and probes format support
- [ ] Returns sensible defaults if tar.exe absent or probe fails
- [ ] Result cached — detection runs once at startup
- [ ] UI greys out formats not supported by detected tar.exe
- [ ] `dotnet test` passes — unit test with mocked process output

---

### T-F49 — tar.exe Extraction Pipeline
- [ ] **Status:** future (v1.3)

**What:** Implement `TarProcessService.ExtractAsync()`. Always uses absolute path. Argument whitelist enforced. Quarantine staging directory on same disk as destination. Full validation after extraction. MOTW propagation. Timeout via `CancellationToken` + `Process.Kill()`.

**File:** `src/Archiver.Core/Services/TarProcessService.cs`

**Acceptance criteria:**
- [ ] Always invokes `C:\Windows\System32\tar.exe` (absolute path — never PATH)
- [ ] Only `-xf` and `-C` arguments allowed — no arbitrary flag injection
- [ ] Extraction goes to quarantine directory on same disk as destination
- [ ] All extracted files validated: no ADS, no reserved names, no reparse points
- [ ] MOTW propagation: copies `Zone.Identifier` from archive to each extracted file
- [ ] `CancellationToken` triggers `Process.Kill()` — no orphaned processes
- [ ] Quarantine directory cleaned up on success and failure
- [ ] New test project `Archiver.Core.IntegrationTests` created
- [ ] Integration tests tagged `[Integration]` — skipped if tar.exe not present
- [ ] Format-specific tests tagged `[SkipIfFormatUnsupported(format)]`
- [ ] `dotnet test` passes (unit tests); integration tests pass on Win 11 23H2+

---

### T-F50 — tar.exe Test Fixtures
- [ ] **Status:** future (v1.3)

**What:** Create fixture set for tar format integration tests. Update `GenerateFixtures` project to generate tar fixtures.

**Fixtures required** (`tests/Archiver.Core.Tests/Fixtures/tar/`):
- `valid_tar.tar`, `valid_tar_gz.tar.gz`, `valid_tar_bz2.tar.bz2`
- `valid_tar_xz.tar.xz`, `valid_tar_zst.tar.zst`, `valid_tar_lzma.tar.lzma`
- `valid_7z.7z`, `valid_rar4.rar`, `valid_rar5.rar`
- `corrupted_tar.tar`, `zipslip_tar.tar`, `bomb_tar.tar.gz`
- `unicode_cyrillic.tar`, `unicode_emoji.tar`

**Acceptance criteria:**
- [ ] All fixtures present and generated by `GenerateFixtures` project (where tar.exe can create them)
- [ ] Integration tests for each valid fixture format
- [ ] Security tests: zipslip rejected, bomb skipped, ADS blocked
- [ ] Tests tagged `[SkipIfFormatUnsupported]` for RAR5/7z (require Win 11 23H2+)
- [ ] `dotnet test` passes (skips gracefully where format unsupported)

---

## v1.4 — GPO + Low IL Sandbox

### T-F51 — Group Policy Support
- [ ] **Status:** future (v1.4)

**What:** Registry-based Group Policy support for enterprise deployment. `PolicyService` reads at startup, overrides user settings. ADMX/ADML template provided.

**Registry path:** `HKLM\Software\Policies\Pakko\`

**Keys:**
| Key | Type | Effect |
|-----|------|--------|
| `EnforceMOTW` | DWORD | Force MOTW propagation (cannot be disabled by user) |
| `AllowedFormats` | multi-string | Whitelist of allowed archive formats |
| `StrictZipBombMode` | DWORD | Lower compression ratio threshold |
| `DisableTarExtraction` | DWORD | Block all tar.exe extraction |

**Acceptance criteria:**
- [ ] `PolicyService` reads all four keys at startup
- [ ] Policies override corresponding user settings
- [ ] `EnforceMOTW=1` forces MOTW on even if user would disable
- [ ] `DisableTarExtraction=1` hides tar format options in UI
- [ ] ADMX/ADML template file added to repo (`deploy/Pakko.admx`, `deploy/Pakko.adml`)
- [ ] `dotnet test` passes — unit tests with mocked registry

---

### T-F52 — Low IL Sandbox for tar.exe
- [ ] **Status:** future (v1.4)

**What:** `TarSandboxedService` implements `ITarService` using a P/Invoke-based Low Integrity Level sandbox for `tar.exe`. Replaces `TarProcessService` via single DI line change.

**File:** `src/Archiver.Core/Services/TarSandboxedService.cs`

**P/Invoke surface:**
- `CreateRestrictedToken` — strip privileges from Pakko's token
- `DuplicateTokenEx` — duplicate for `CreateProcessAsUser`
- `SetTokenInformation` — set integrity level to Low IL
- `CreateProcessAsUser` — launch tar.exe with restricted token
- `SetNamedSecurityInfo` — label quarantine directory with Low IL

**Flow:**
1. Create quarantine directory on same disk as destination
2. Label quarantine directory Low IL via `SetNamedSecurityInfo`
3. Launch `tar.exe` into quarantine with restricted token (Low IL)
4. After process exits, validate all files at Medium IL (C# code)
5. Atomic move to final destination
6. Clean up quarantine directory

**Acceptance criteria:**
- [ ] `TarSandboxedService` implements `ITarService` — same interface as `TarProcessService`
- [ ] DI swap is one line: `AddSingleton<ITarService, TarSandboxedService>()`
- [ ] Quarantine directory receives Low IL label before tar.exe launch
- [ ] tar.exe process runs with restricted Low IL token
- [ ] Validation and move run at Medium IL in C# after process exits
- [ ] Quarantine directory cleaned up on success and failure
- [ ] All P/Invoke handles properly closed — no leaks
- [ ] `dotnet test` passes — integration test: file write outside quarantine fails

---

### T-F58 — Archive Finalization Phase UX
- [x] **Status:** complete

**What:** After all file bytes are copied into the ZIP, `ZipArchive` still performs real work (writing the central directory, flushing, closing, renaming the temp file). During this phase `ProgressStream` reports no new bytes, so the progress bar freezes and speed/ETA disappear, making the app appear hung.

**Fix:** When the reported byte count reaches the operation total (percent = 100), switch the UI to a "finalizing" state: progress bar becomes indeterminate, status line shows "Finalizing...", and speed/ETA are hidden. Normal completion state takes over once the operation returns.

**Files:** `src/Archiver.App/ViewModels/MainViewModel.cs`, `src/Archiver.App/MainWindow.xaml`, `src/Archiver.App/Strings/en-US/Resources.resw`

**Acceptance criteria:**
- [x] `IsProgressIndeterminate` observable property added to `MainViewModel`
- [x] When archive progress callback receives `Percent >= 100`, set `IsProgressIndeterminate = true` and `StatusMessage = "Finalizing..."`
- [x] `IsProgressIndeterminate` reset to `false` in the `finally` block
- [x] ProgressBar `IsIndeterminate` bound to `ViewModel.IsProgressIndeterminate` in XAML
- [x] `StatusFinalizing` resource string added to `Resources.resw`
- [x] State driven from ViewModel — no code-behind logic
- [x] `dotnet test` passes — 67/67

---

### T-F59 — Extraction Progress Overshoot Fix
- [x] **Status:** complete

**What:** Extraction progress total was computed from `entry.CompressedLength` (bytes inside the ZIP), but `ProgressStream` counts uncompressed bytes as they flow out of `entry.Open()`. When compression ratio is significant the byte count exceeds the total, causing the progress bar to overshoot 100% and reset.

**Fix:** Compute the extraction total from `entry.Length` (uncompressed size) for all entries where `Length > 0`. Use the same uncompressed unit for all `bytesRead` accumulation so the ProgressStream offset and total are consistent.

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] `totalUncompressedBytes` computed as `fileEntries.Where(e => e.Length > 0).Sum(e => e.Length)`
- [x] `ProgressStream` initialized with `totalUncompressedBytes` instead of `totalCompressedBytes`
- [x] All `bytesRead += entry.CompressedLength` changed to `bytesRead += entry.Length`
- [x] Entries with `Length == 0` excluded from the total (directories, streaming entries)
- [x] ZIP bomb check still uses `entry.CompressedLength` (ratio check unchanged)
- [x] `dotnet test` passes — 67/67

---

### T-F60 — Cleanup Bug: Leftover .tmp on All-Failures Archive
- [x] **Status:** complete

**What:** When archiving fails for every source file (all paths missing, locked, etc.), `ZipArchive` still creates and closes the temp file before the commit point is reached. The unconditional `File.Move(tempPath, destPath)` then produces an empty `.zip` on disk. In the SeparateArchives path, the same bug affects directories whose every contained file fails.

**Fix:** Add a `HasTempEntries` helper that opens the temp ZIP and checks `Entries.Count > 0`. Before committing (moving temp → dest), check it:
- Entries found → commit as before (partial archive with errors is still useful)
- No entries → delete the temp, leave `createdFiles` empty

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`, `tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`

**Acceptance criteria:**
- [x] `HasTempEntries(path)` private helper added — opens ZIP read-only, returns `Entries.Count > 0`, swallows exceptions
- [x] SingleArchive path: `File.Move` gated on `HasTempEntries` — empty temp deleted, not moved
- [x] SeparateArchives path: same gate applied to the per-item `File.Move`
- [x] Partial success (some files ok, some missing): archive committed, `Success = false`, errors reported
- [x] Test: all sources missing → `result.Errors.Count == 2`, `CreatedFiles` empty, no `.zip` or `.tmp` on disk
- [x] Test: one valid + one missing → `CreatedFiles.Count == 1`, `Errors.Count == 1`, `Success = false`, no `.tmp` on disk
- [x] All existing tests still pass
- [x] `dotnet test` passes — 69/69

---

### T-F75 — Correctness Bug: Nested Subdirectory Entries Lost Their Path Prefix
- [x] **Status:** complete — **confirmed shipped in tagged v1.1.0**, found 2026-07-06 while
      investigating T-F30

**What:** `AddDirectoryToArchiveAsync` computed each entry's relative path against the current
recursion level's own immediate parent, recomputed fresh every level, instead of against the
true archived root held constant. Archiving a directory nested two or more levels deep produced
ZIP entries missing their accumulated prefix — e.g. `notes/sub/file.txt` was written as just
`sub/file.txt`. Worse: two files at different depths whose path relative to their own immediate
parent happened to match could collide into the *same* entry name, and since
`ZipArchive.CreateEntry` doesn't reject duplicates, the second write silently clobbered the
first on extraction. See `DECISIONS.md`'s "T-F75" entry for the full trace and root-cause detail.

**Fix:** `AddDirectoryToArchiveAsync` gained a `rootDir` parameter, fixed across all recursion,
used (with `entryPrefix`) to compute every entry name against the true root regardless of depth.
The T-F66 empty-subdirectory special case had the identical bug and is fixed the same way.

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`,
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`

**Acceptance criteria:**
- [x] `AddDirectoryToArchiveAsync` computes entry names against a fixed `rootDir`, not each
      recursion level's own parent
- [x] Empty-subdirectory entries (T-F66) also computed against the fixed root
- [x] `ArchiveAsync_FolderWithEmptySubfolder_PreservesEmptySubfolderEntry` updated — it asserted
      the bug's own output (`EmptyChild/`) as correct; now expects `Parent/EmptyChild/`
- [x] New test: 3-level nesting — entry names include the full path from root at every depth
- [x] New test: sibling subdirectories with matching relative structure no longer collide into
      one entry name; archive → extract round trip preserves both files' distinct content
- [x] `dotnet test` passes — 79/79 (was 77/77)
- [ ] Decide whether this warrants a v1.1 patch/release note for early testers (flagged to user,
      not yet decided)

---

## Diagram Review Findings (2026-07-05)

Surfaced while drafting `DIAGRAMS.md`'s required diagram set (see that file's "Ground Truth
Rule" — every branch below was traced to a specific file:line before being written up, not
inferred). Cross-reference: `DIAGRAMS.md` → "Findings summary".

### T-F68 — Shell Extract Silently Ignores SkippedFiles (possible dead-end)
- [x] **Status:** complete — verified 2026-07-06

**What:** `ArchiveResult.Success` is computed as `errors.Count == 0`
(`ZipArchiveService.cs:449`) — `SkippedFiles` never affects it. The GUI path surfaces
`SkippedFiles` correctly via `ShowOperationSummaryAsync`. The shell path does not: `Program.cs:235`
only calls `ShowErrorSummary` when `!result.Success || result.Errors.Count > 0`. Every gate in the
extract validation chain (ADS, reserved device name, control chars, reparse point, ZIP bomb ratio,
`OnConflict=Skip`) routes to `SkippedFiles`, never `Errors`. So a shell-triggered "Extract here" on
an archive where *every* entry hits one of these gates completes with `Success=true`, creates an
empty (or near-empty) folder, and shows **no dialog at all** — indistinguishable from a normal
successful extraction with nothing visibly wrong.

**File:** `src/Archiver.Shell/Program.cs`, possibly `src/Archiver.Core/Models/ArchiveResult.cs`

**Decision needed first:** should `ArchiveResult.Success` itself account for `SkippedFiles`
(changes the GUI contract too), or should only the shell path's error/skip-summary trigger be
widened to also check `SkippedFiles.Count > 0` (minimal, GUI unaffected since it already shows
skips)? Record the choice in `DECISIONS.md` before implementing.

**Acceptance criteria:**
- [x] Decision recorded in `DECISIONS.md` (see "T-F68 — Shell Extract Silently Ignoring
      SkippedFiles") — widen only the shell trigger; `ArchiveResult.Success` unchanged
- [x] Shell path (`RunWithProgressWindowAsync`) shows a dialog when `result.SkippedFiles.Count > 0`,
      even if `Errors.Count == 0` — via new `ShellResultPresenter.Classify`
- [x] Message distinguishes "N entries skipped" (`MB_ICONWARNING`) from "operation failed"
      (`MB_ICONERROR`, unchanged) — `ShellResultPresenter.BuildSkippedMessage`
- [x] `dotnet test` passes — new `ShellResultPresenterTests` (8 cases: classify success/failed/
      skipped-only/errors-win-over-skips, message singular/plural/truncation) — Archiver.Shell.Tests
      36/36 (was 28/28), Archiver.Core.Tests 99/99 unaffected
- [x] **Manual smoke test:** verified 2026-07-06 via Explorer UI automation (Windows MCP) after
      `Deploy.ps1` (v1.1.0.38) — a ZIP with a single ADS entry (`file.txt:payload.exe`, the only
      entry, so extraction has nothing else to write) and "Extract here" now shows
      `MB_ICONWARNING`: "1 entry skipped: file.txt:payload.exe: Alternate Data Stream entry
      rejected for security." Previously this completed silently with no dialog at all.

---

### T-F69 — Fix ARCHITECTURE.md Doc Drift: com:InProcessServer → com:SurrogateServer
- [x] **Status:** complete (trivial) — fixed 2026-07-06, alongside T-F63's sub-command list update

**What:** `ARCHITECTURE.md:259` still says *"Registered via `com:InProcessServer` in
`Package.appxmanifest`"*. The actual manifest (`Package.appxmanifest:70-78`) and `DECISIONS.md`'s
own "Correction — SurrogateServer" entry both say `com:SurrogateServer`. `ARCHITECTURE.md` was
never updated when that correction landed during T-F61.

**Acceptance criteria:**
- [x] `ARCHITECTURE.md:259` updated to `com:SurrogateServer`, matching the manifest and `DECISIONS.md`
      — also updated the stale sub-command list/selection-logic description alongside it (it had
      drifted again, missing `TestCommand`/T-F63's new dialog commands)
- [x] Grep confirms no remaining doc references the old `com:InProcessServer` wording as current
      truth — the 3 remaining hits (`CLAUDE.md` ×2, `DIAGRAMS.md` ×1) are historical descriptions
      of the bug itself, not stale claims

---

### T-F70 — Confirm Intended UX: IsBusy vs. Status-Text Timing After Cancel vs. Success/Error
- [x] **Status:** complete — decided and fixed 2026-07-06 (aligned, not documented-as-intended)

**What:** In `MainViewModel.ArchiveAsync`/`ExtractAsync` (lines 228–437), the
success/issues/error exit paths await their modal dialog (`ShowOperationSummaryAsync` /
`ShowErrorAsync`) *before* the `finally` block runs — so `IsBusy` stays `true` (controls stay
disabled) for as long as that dialog is on screen. The cancelled exit path shows no dialog:
`IsBusy` flips to `false` immediately in `finally`, and only *afterward* does a fixed
`Task.Delay(2000)` run before `StatusMessage` reverts to "Ready". Net effect: for ~2 seconds after
a cancel, the UI is already not-busy (a new operation is invokable) while the status text still
reads "Cancelled" — the reverse of the other three outcomes, where the UI stays busy exactly as
long as something requiring dismissal is still on screen. Not confirmed as a bug — nothing gets
stuck — but it's a real, verified asymmetry that should be a deliberate choice, not an accident.

**Acceptance criteria:**
- [x] Decide: is it acceptable that a new Archive/Extract can start during the 2-second post-cancel
      "Cancelled" status display, while the other three outcomes fully block until their dialog is
      dismissed? — **No.** Decision recorded in `DECISIONS.md`'s "T-F70" entry.
- [x] If not intended: align behavior — `IsBusy = false` moved from `finally` to immediately before
      the final `StatusMessage = "Ready"` line (after the cancel-only delay), in both `ArchiveAsync`
      and `ExtractAsync`
- [x] `DIAGRAMS.md` diagram 2 updated — it had documented the old behavior in explicit detail
      (`CancelledNoDialog --> Idle: finally{IsBusy=false} runs FIRST...`), so it was now stale;
      re-derived all three exit transitions from the current source per the Ground Truth Rule
- [x] **Verification:** code-level (the line move is unambiguous); live-tested that Cancel still
      stops an in-progress Archive cleanly and the UI reaches "Ready" afterward with no crash. The
      exact 2-second window itself could not be visually caught via remote UI automation — each
      tool round-trip in this session exceeded 2 seconds, so any post-cancel check landed after the
      window had already elapsed regardless of correctness; see `DECISIONS.md` for detail.

---

## Documentation Map Findings (2026-07-05)

Surfaced while auditing every `.md` file in the repo for duplication/drift and building
`CLAUDE.md`'s "Documentation Map" section. The map itself, the `AGENT.md` redirect, and the
broken build instructions in `scripts/README.md`/`CONTRIBUTING.md` (`Archiver.Package.wapproj` +
`Archiver.ProgressWindow.exe` + manual `SignTool.exe` — all three rejected/deleted/broken per
`DECISIONS.md`) were fixed directly, not tasked, since anyone following the old instructions
would fail. Remaining duplication/drift is lower-severity and tracked below.

### T-F71 — Consolidate Security/Supply-Chain Rationale (SECURITY.md as sole owner)
- [x] **Status:** complete — fixed 2026-07-06

**What:** The 7-Zip/WinRAR supply-chain risk tables, CVE lists, and MOTW rationale are written out
in full independently in three places: `SECURITY.md` (the canonical threat model), `SPEC.md`
("Security Rationale" section), and `README.md` ("Why Not 7-Zip or WinRAR?" section). All three
are hand-maintained prose/tables covering the same CVEs and the same reasoning, with no link
between them — a future CVE addition or rationale change has to be remembered and applied in up
to three places, and nothing catches it if one is missed.

**Fix:** Trim `SPEC.md` and `README.md` to a 2–3 line teaser plus a link to `SECURITY.md` for the
full table/rationale. `CLAUDE.md`'s Documentation Map already names `SECURITY.md` as canonical.

**Acceptance criteria:**
- [x] `SPEC.md`'s "Security Rationale" section trimmed to a teaser + link to `SECURITY.md`
- [x] `README.md`'s "Why Not 7-Zip or WinRAR?" section trimmed to a teaser + link to `SECURITY.md`
      (its separate "What Pakko Uses Instead"/"Security Properties" sections are distinct content,
      not CVE/rationale duplication, and were left as-is — out of this task's specific scope)
- [x] No CVE table or supply-chain rationale text duplicated outside `SECURITY.md`
- [x] `SECURITY.md` itself unchanged (already the richest, most current version)

---

### T-F72 — Consolidate Version Roadmap Table (SPEC.md as sole owner)
- [x] **Status:** complete — fixed 2026-07-06

**What:** A version-to-focus roadmap table (v1.1 through v1.5) is independently maintained in
`CLAUDE.md`, `SPEC.md`, and `README.md`, with slightly different wording and status per copy
(e.g. `CLAUDE.md`'s v1.2 row has per-feature status detail the other two don't). This is exactly
the kind of drift this repo already caught once (`ARCHITECTURE.md`'s `com:InProcessServer` vs.
`com:SurrogateServer`, see `T-F69`) — three tables that can silently disagree.

**Fix:** `SPEC.md`'s "Future Roadmap" table becomes the sole source. `CLAUDE.md`'s "Roadmap
Summary" and `README.md`'s "Roadmap" sections are replaced with a link to it; version-specific
status detail that doesn't fit a one-line roadmap entry stays in `CLAUDE.md`'s "Current State"
prose (which already carries it) rather than in a second table.

**Acceptance criteria:**
- [x] `SPEC.md`'s roadmap table reviewed and confirmed current (cross-check against `TASKS.md`
      status of each version's tasks) — e.g. v1.2's "hash viewer" mention matches `T-F46`
      (status: future), still accurate; the table describes scope only, not per-task completion,
      so no content edit was needed, only the other two files' copies
- [x] `CLAUDE.md`'s "Roadmap Summary" table replaced with a link to `SPEC.md`
- [x] `README.md`'s "Roadmap" table replaced with a link to `SPEC.md`
- [x] No independent roadmap table remains outside `SPEC.md` — grep confirmed

---

### T-F73 — Fix Stale `IProgress<int>` Signature in ARCHITECTURE.md and CONVENTIONS.md
- [x] **Status:** complete (trivial) — fixed while touching this exact snippet for T-F62's `TestAsync`

**What:** `ARCHITECTURE.md`'s `IArchiveService` interface snippet and `CONVENTIONS.md`'s XML-doc
example both show `IProgress<int>? progress` with "(0–100)"/"Optional progress reporter (0–100)"
wording. The actual interface (`src/Archiver.Core/Interfaces/IArchiveService.cs:11-23`) is
`IProgress<ProgressReport>? progress` — `ProgressReport` carries `Percent`, `BytesTransferred`,
`TotalBytes`, `CurrentFile` (added for T-F16's byte-accurate progress). `ARCHITECTURE.md` is the
file `CLAUDE.md`'s Documentation Map names canonical for signatures — it must not itself be stale.

**Acceptance criteria:**
- [x] `ARCHITECTURE.md`'s `IArchiveService` snippet updated to `IProgress<ProgressReport>?`
- [x] `CONVENTIONS.md`'s XML-doc example updated to match, including a real `ProgressReport`
      description instead of "(0–100)"
- [x] Grep confirms no other doc still shows the old `IProgress<int>` signature for this interface
      (the one remaining hit, `ARCHITECTURE.md`'s `ITarService` v1.3 stub, is a different,
      not-yet-implemented interface — out of scope for this task)

---

### T-F74 — Consolidate Duplicate Testing/Build Rules Between CLAUDE.md and TASKS.md
- [x] **Status:** complete (low priority) — fixed 2026-07-06

**What:** `TASKS.md`'s "⚠ Agent Rules" section (top of file) restates rules already in
`CLAUDE.md`'s "Hard Constraints" — e.g. "ALWAYS run `dotnet test` (no path — all projects)"
appears near-verbatim in both files. `CLAUDE.md`'s Documentation Map now names `CLAUDE.md` as the
canonical owner of hard constraints; `TASKS.md`'s copy should become a link, keeping only the
task-specific rules that don't exist elsewhere (completion-marking rules `[x]`/`[~]`, UI-vs-logic
rules, scope rules for which options apply to which action).

**Acceptance criteria:**
- [x] `TASKS.md`'s testing-rule bullets that duplicate `CLAUDE.md` hard constraints replaced with
      a link to `CLAUDE.md`
- [x] Task-specific rules with no equivalent in `CLAUDE.md` (completion marking, UI-vs-logic,
      scope rules) kept in `TASKS.md` unchanged

---

## UI/UX Design Review Findings (2026-07-06)

Surfaced during a design pass over `Archiver.App`'s main window and the Explorer context menu
(screenshots from this session's smoke-test cycle — see `TESTING.md`'s manual smoke test entry).
Design direction below was checked against Pakko's own niche (Ukrainian government/defense —
trust, auditability, minimal attack surface, `CLAUDE.md`'s Project section) before being written
up: this audience reads native OS fidelity as a trust signal and a bespoke brand skin as the
opposite, so recommendations deliberately favor WinUI-native idioms and restraint over a custom
visual identity. T-F76 is a concrete, reproducible bug; T-F77–T-F81 are design debt, not bugs —
each needs a decision before implementation, not just code.

---

### T-F76 — Bug: "Extract to folder…" Ellipsis Implies a Dialog That Never Appears
- [x] **Status:** complete — fixed 2026-07-06, same class as T-F64

**What:** `ExtractFolderCommand::GetTitle` (`ExplorerCommands.cpp:145-149`) hardcodes
`"Extract to folder…"`. The trailing ellipsis is a UI convention meaning "a dialog/prompt
follows" — but `ExtractFolderCommand::Invoke` (`ExplorerCommands.cpp:179-188`) calls
`LaunchShellExe` directly with no dialog; it silently creates `<archive_name>\` and extracts into
it. This is the identical label/behavior mismatch already found and fixed for `ArchiveCommand`'s
"Add to archive…" in T-F64 — that fix removed the ellipsis and showed the real computed name
instead (`Add to "<name>.zip"`). `ExtractFolderCommand` never got the equivalent treatment.

**Fix (mirrors T-F64):** `GetTitle` already receives `IShellItemArray* psia` (currently unused —
the signature takes it but the body ignores it, same starting point `ArchiveCommand` had before
T-F64). Compute the real destination folder name from the first selected archive's base name and
render e.g. `Extract to "<name>\"`, reusing `TruncateMiddle` from `ShellExtUtils.cpp` for long
names, no ellipsis.

**Files:** `src/Archiver.ShellExtension/ExplorerCommands.cpp`,
`src/Archiver.ShellExtension/ShellExtUtils.cpp` (helper reuse), corresponding
`Archiver.ShellExtension.Tests` cases.

**Acceptance criteria:**
- [x] `ExtractFolderCommand::GetTitle` computes `Extract to "<name>\"` from the first selected
      item, no trailing ellipsis — implemented as `BuildExtractFolderTitle` in `ShellExtUtils.cpp`
      (multi-selection extracts each archive to its own separately-named folder per T-F42, so no
      single name would be truthful there — returns `"Extract each to its own folder"` instead)
- [x] Long names truncated middle via existing `TruncateMiddle`, consistent with T-F64
- [x] No behavior change to `Invoke`/`GetState` — still extracts silently, no dialog
- [x] `Archiver.ShellExtension.Tests` gains cases mirroring `BuildAddToArchiveTitle`'s coverage
      (empty vector, single item, multi-selection, leading-dot edge case, at-limit and
      over-limit truncation) — 39/39 (was 33/33)
- [x] **Manual smoke test:** verified 2026-07-06 via Explorer UI automation (Windows MCP) after
      `Deploy.ps1` — single archive shows `Extract to "quarterly_report\"`, two-archive selection
      shows `Extract each to its own folder`; invoking the multi-selection command still extracts
      each archive to its own separate folder exactly as before (no behavior regression)

---

### T-F77 — Archive/Extract Options Don't Adapt to the Active Action
- [x] **Status:** complete — decision recorded and fixed 2026-07-06

**What:** `MainWindow`'s options panel always shows `Mode`, `Name`, `Compression` (Archive-only
per `CLAUDE.md`'s own documented scope rules) even when the current selection is extract-only
(e.g. a single `.zip`), and always shows both Archive and Extract buttons regardless of which one
the current selection actually supports. The form doesn't tell the user, at a glance, what will
happen when they press the primary button — it reads as one long flat list of settings, some of
which are inert for the current selection.

**Design direction:** don't hide fields with elaborate animation — this niche rewards
predictability over motion. Collapse Archive-only fields (`Mode`, `Name`, `Compression`) via a
plain `Visibility` binding to a ViewModel property that already knows the active action (mirrors
the existing `IsArchiveNameAndNotBusy`-style computed properties per `ARCHITECTURE.md`). Showing
only the options that apply *is* the signature move here: which fields are visible becomes
information about what's about to happen, which is more honest than a permanent settings dump.

**Acceptance criteria:**
- [x] Decision recorded (`DECISIONS.md`, "T-F77 / T-F81 — Contextual Option Visibility and the
      Outcome Subtitle"): collapse via `ArchiveOptionsVisibility`; extract-only requires a 100%
      `.zip` selection (any non-`.zip` item keeps archive-mode)
- [x] Archive-only fields hidden when selection is extract-only and vice versa; shared fields
      (`Destination`, `If file exists`, `Open destination folder`) always visible
- [x] No new dependencies, no custom animation — plain XAML `Visibility` binding
      (`ArchiveOptionsVisibility` on `MainViewModel`)
- [x] `dotnet test` passes — 127/127 (visibility logic is a plain computed property over
      `FileItems`, not independently unit-testable without a WinUI host; covered by manual
      verification below)
- [x] **Manual smoke test:** verified 2026-07-06 via Pakko UI automation (Windows MCP) after
      `Deploy.ps1` — three scenarios confirmed: single `.zip` collapses Mode/Name/Compression;
      single non-`.zip` file keeps them visible; mixed `.zip` + non-`.zip` selection also keeps
      them visible (per the strict all-`.zip` rule)

---

### T-F78 — No Typographic Hierarchy in the Options Panel
- [x] **Status:** complete — fixed 2026-07-06

**What:** Every label (`Destination:`, `Mode:`, `Name:`, `Compression:`, `If file exists:`) and
every value/input renders at the same weight and size — nothing distinguishes a section label
from body text, so the panel reads as an undifferentiated list rather than a structured form.

**Design direction:** don't introduce a custom type system — WinUI 3 already ships
`CaptionTextBlockStyle`/`BodyStrongTextBlockStyle`/`TitleTextBlockStyle` resources built for
exactly this. Apply `CaptionTextBlockStyle` (smaller, secondary-color) to the field labels and
leave inputs at body weight. Purely additive, zero new dependencies, consistent with the
project's "no custom framework" posture.

**Files:** `src/Archiver.App/MainWindow.xaml`

**Acceptance criteria:**
- [x] Field labels (`Destination:`, `Mode:`, `Name:`, `Compression:`, `If file exists:`) use
      `CaptionTextBlockStyle` or equivalent existing WinUI resource — no new custom style
- [x] Inputs/values remain at current (body) weight — contrast comes from the label, not the value
- [x] No layout shift/regression — spacing unchanged, only text style
- [x] **Manual smoke test:** verified 2026-07-06 via Explorer/Pakko UI automation (Windows MCP)
      after `Deploy.ps1` — labels render visibly secondary/dimmer than input values in both dark
      theme (default) and light theme (temporarily toggled via `AppsUseLightTheme` registry value,
      reverted after); no layout shift observed in either theme

---

### T-F79 — Record the Decision Not to Add a Custom Brand Palette
- [x] **Status:** complete — recorded 2026-07-06, documentation-only

**What:** Pakko currently has no app-specific color identity — background/text come from the
WinUI dark theme, and the only accent is the user's system accent color on the primary button.
This was flagged during design review as a potential gap, but for this audience (government/
defense — trust, auditability) a bespoke brand palette that visually diverges from native Windows
chrome would read as "skinned," which undercuts trust rather than building it. The considered,
deliberate choice is to **not** add a custom palette and instead invest restraint and hierarchy
(T-F78) plus contextual clarity (T-F77) as the actual signature.

**Why this is a task and not just a conversation:** per `CLAUDE.md`'s "never silently deprecate"
and `DECISIONS.md`'s role as the record of chosen/rejected approaches — if this reasoning isn't
written down, a future session may "fix" the lack of branding as an oversight.

**Acceptance criteria:**
- [x] Decision written to `DECISIONS.md`: no custom brand palette; native WinUI theme + system
      accent only; rationale tied to the gov/defense trust model
- [x] `SECURITY.md`/`SPEC.md` teaser-linked if either references visual trust signals (checked —
      neither makes visual-trust-signal claims beyond the general framing; no change needed)

---

### T-F80 — Empty State Doesn't Teach the Two Modes
- [x] **Status:** complete — fixed 2026-07-06

**What:** The empty-state drop zone ("Drop files or folders here, or double-click to browse
files") is functionally correct but purely instructional — it tells the user how to add items,
not what happens next. A first-time user doesn't learn from this screen that dropping a `.zip`
leads to extraction while dropping anything else leads to archiving.

**Design direction:** treat the empty state as an invitation to act, not decoration (per the
project's own copy-is-material principle) — add one calm second line stating the fork, e.g.
"Archives offer to extract; anything else gets archived." No icons, no illustration, no
animation — a single added sentence, consistent with the rest of the panel's plain-text register.

**Files:** `src/Archiver.App/MainWindow.xaml`, `src/Archiver.App/Strings/en-US/Resources.resw`

**Acceptance criteria:**
- [x] Second line added under the existing drop-zone prompt, resource string in `Resources.resw`
      (not hardcoded, per existing localization convention) — `DropZoneModeHint.Text`: "Archives
      offer to extract; anything else gets archived"
- [x] Copy reviewed for plain, active-voice register matching the rest of the app's strings
- [x] No layout/visual redesign of the drop zone itself — text addition only
- [x] **Manual smoke test:** verified 2026-07-06 via Pakko UI automation (Windows MCP) after
      `Deploy.ps1` — second line renders under the existing hint in both dark and light theme

---

### T-F81 — Archive/Extract Button Pair Reads as a Toggle, Not Two Distinct Actions
- [x] **Status:** complete — fixed 2026-07-06, alongside the T-F77 decision

**What:** `Archive` and `Extract` render as adjacent, equal-width buttons that visually resemble a
segmented toggle control, but they're two independent actions whose availability depends on the
current selection (only one is normally actionable at a time). When one is disabled it still
shows at reduced opacity next to the enabled one, with no text explaining why — a user extracting
a single `.zip` sees a live "Archive" button and a dead "Extract" button with no stated reason.

**Design direction:** once T-F77's contextual-visibility decision lands, pair it with a one-line
status/subtitle under the button row stating the resolved action in plain language (e.g. "Will
extract 1 archive to the folder above") — this reuses the same "structure communicates outcome"
principle as T-F77 rather than introducing a separate mechanism.

**Acceptance criteria:**
- [x] Decision on whether the inactive button should grey out (current behavior, kept) or hide
      entirely — recorded alongside T-F77's decision, not separately: grey out, kept
- [x] One-line outcome subtitle added under the button row, driven by existing ViewModel state
      (`OperationOutcomeText`/`OperationOutcomeVisibility`)
- [x] No change to the underlying Archive/Extract command logic — presentation only
- [x] `dotnet test` passes — 127/127, no ViewModel behavior change, only new bindable display text
- [x] **Manual smoke test:** verified 2026-07-06 via Pakko UI automation (Windows MCP) — subtitle
      reads "Will extract 1 archive(s)..." / "Will archive 1 item(s)..." / "Will archive 2
      item(s)..." matching each of the three T-F77 scenarios

---

### T-F82 — Archive Button Keeps Accent Color Even When the Outcome Subtitle Says "Extract"
- [x] **Status:** complete — fixed 2026-07-06, found during a follow-up design review of T-F77/T-F81

**What:** Found during a second design-review pass (after T-F76–T-F81 shipped) comparing the
actual rendered screens rather than mockups. `Archive`'s `AccentButtonStyle` is unconditional —
it stays visually primary (blue) regardless of selection type. For an extract-only selection
(single `.zip`), T-F81's new outcome subtitle reads "Will extract 1 archive(s) to the folder
above," but `Archive` is still live (archiving a `.zip` into a new `.zip` is a valid, reachable
edge case) and still the visually-highlighted button — so the interface's text and its visual
weight point at two different actions. A user following the accent color gets an outcome the
subtitle never described. This is isolated to the extract-only case: for archive-mode selections
(non-`.zip` or mixed), `Archive`-blue and "Will archive…" already agree.

**Design direction:** no new mechanism — drive the accent style off the same
`IsExtractOnlySelection` property that already governs T-F77's field collapse, so visual weight
always matches whichever action the outcome subtitle is describing.

**Files:** `src/Archiver.App/MainWindow.xaml`, `src/Archiver.App/ViewModels/MainViewModel.cs`

**Acceptance criteria:**
- [x] `Archive` has `AccentButtonStyle` when the selection is not extract-only; `Extract` has it
      when the selection is extract-only — driven by `IsExtractOnlySelection`
- [x] No change to `CanArchive`/`CanExtract`/command logic — presentation only
- [x] `dotnet test` passes
- [x] **Manual smoke test:** verified 2026-07-06 via Pakko UI automation (Windows MCP) — accent
      color follows the resolved action across all three T-F77 scenarios (zip-only, non-zip-only,
      mixed)

---

### T-F83 — Bug: Cold-Start Protocol/File Activation Never Reached OnActivated
- [~] **Status:** partial — fixed and verified end-to-end for protocol activation 2026-07-06;
      T-F44's file-activation cold-start claim remains unverified (flagged, not actioned, see
      last criterion). Found while manually testing T-F63.
- **Depends on:** none (bug in already-shipped T-F44/T-F56 code)

**What:** `App.xaml.cs` subscribed to `AppInstance.GetCurrent().Activated` in its constructor, but
per the Windows App SDK, that event only fires for activations *redirected* to an already-running
instance — never for the process's own initial (cold-start) activation. `OnLaunched` (the plain
WinUI override) fired instead on cold start, which unconditionally built a blank `MainWindow` and
never inspected the File/Protocol activation payload. Net effect: any cold-start `pakko://` launch
(T-F63's new dialog commands, and potentially T-F44/T-F56's already-shipped paths) opened Pakko
with an **empty file list**, silently dropping the files it was invoked with — no error, no dialog,
just looked like a normal empty launch. See `DECISIONS.md`'s "T-F83" entry for the full repro
(reproduced independently of the shell extension via direct `Archiver.Shell.exe --open-ui --extract`
invocation) and root-cause trace via `pakko.log`.

**Why T-F56/T-F44 didn't catch this:** neither task's acceptance criteria list an explicit
"file list is visibly populated after a cold launch" smoke-test line — both were verified at the
build/URI-construction level, or (for warm re-activation, where `Activated` *does* fire) while a
Pakko window was already open. T-F44's cold-start claim ("`AppInstance.Activated` handles both
cold-start and warm file activation") could not be independently reverified in this session — this
dev machine's default `.zip` handler is NanaZip, not Pakko — so treat it as **unverified**, not
confirmed-working, until checked on a machine where Pakko owns the `.zip` association.

**Fix:** `HandleActivation(AppActivationArguments, string defaultLogMessage)` extracted from the old
`OnActivated`; both `OnActivated` (warm) and `OnLaunched` (cold, via
`AppInstance.GetCurrent().GetActivatedEventArgs()`) now route through it.

**File:** `src/Archiver.App/App.xaml.cs`

**Acceptance criteria:**
- [x] `OnLaunched` pulls the real activation kind via `GetActivatedEventArgs()` instead of ignoring
      `LaunchActivatedEventArgs` and always building a blank window
- [x] File/Protocol handling shared between `OnLaunched` and `OnActivated` via one
      `HandleActivation` helper — no logic duplication, can't drift
- [x] `dotnet build src/Archiver.App` passes (no automated test coverage possible — WinUI
      activation requires a real process/window, not unit-testable)
- [x] **Manual smoke test:** verified 2026-07-06 after `Deploy.ps1` (v1.1.0.41) — cold-start
      `Archiver.Shell.exe --open-ui --extract "<zip>"` (no Pakko instance already running, verified
      via `Get-Process`) opened Pakko with `tf63test.zip` visible in the file list, "Extract"
      accented, subtitle "Will extract 1 archive(s)..."; `pakko.log` showed `"Pakko started via
      protocol activation"` for that launch (previously just `"Pakko started"`, blank window).
      Also reverified end-to-end through the real Explorer context menu (both "Extract…" on a
      `.zip` and "Compress…" on a `.pdf`) — both correctly pre-load Pakko now.
- [ ] T-F44 cold-start file activation reverified on a machine where Pakko is the default `.zip`
      handler (flagged, not actioned, in this session)

