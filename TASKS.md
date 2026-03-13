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
- ALWAYS run `dotnet test` after any change to `Archiver.Core`
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
- 59/59 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` (unsigned — see T-F10 for signing)
- Git tag: `v1.1.0` — GitHub-only release for early testers
- **Store release planned for v1.3** (when shell extension + MOTW + tar.exe complete)

---

## Future Tasks

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** future
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
- [ ] **Status:** future

**What:** Verify all file I/O in ZipArchiveService uses stream-based operations
with optimal buffer size. No File.ReadAllBytes or large in-memory buffers.

**Acceptance criteria:**
- [ ] All file transfers use CopyToAsync with explicit buffer size 81920 (80 KB)
- [ ] No File.ReadAllBytes or File.ReadAllText in ZipArchiveService
- [ ] Memory profiling on 1 GB file shows no spike beyond 2x buffer size
- [ ] dotnet test passes — existing tests unchanged

---

### T-F20 — Zip64 Verification
- [ ] **Status:** future

**What:** Verify Zip64 support works correctly for large archives.
.NET supports Zip64 but behavior must be explicitly tested.

**Acceptance criteria:**
- [ ] Test: archive with >65535 files completes without error
- [ ] Test: archive >4 GB completes without error
- [ ] Test: extract >4 GB archive completes without error
- [ ] dotnet test passes

---

### T-F21 — Race Condition Handling During Traversal
- [ ] **Status:** future

**What:** Files may be modified or deleted during directory traversal.
FileNotFoundException during archiving should be recoverable, not fatal.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] FileNotFoundException during traversal → ArchiveError, operation continues
- [ ] File deleted between scan and read → ArchiveError with clear message
- [ ] No unhandled exception reaches caller
- [ ] dotnet test passes — new test: file deleted mid-archive → ArchiveError

---

### T-F22 — Windows Long Path Support
- [ ] **Status:** future

**What:** Windows paths exceeding 260 characters fail silently on some APIs.
Verify Pakko handles long paths correctly.

**Acceptance criteria:**
- [ ] app.manifest contains longPathAware element set to true
- [ ] ZipArchiveService tested with paths >260 characters
- [ ] No silent truncation or failure on long paths
- [ ] dotnet test passes — new test: archive/extract with path >260 chars

---

### T-F23 — Symlink and Junction Handling
- [ ] **Status:** future

**What:** Define and implement explicit behavior for symbolic links and
NTFS junctions during archiving. Currently undefined — may cause
infinite loops or unintended file inclusion.

**Decision required before implementation:**
- Follow symlinks (include target content)
- Skip symlinks (add to SkippedFiles with reason)
- Archive link metadata only

**Recommendation:** Skip symlinks — add to SkippedFiles with reason
"Symbolic links are not supported".

**Acceptance criteria:**
- [ ] Symlinks detected during directory traversal
- [ ] Symlinks added to SkippedFiles with clear reason
- [ ] No recursive loop on circular symlinks
- [ ] NTFS junctions handled same as symlinks
- [ ] dotnet test passes — new test: directory with symlink → SkippedFile

---

### T-F24 — Property-Based Archive Integrity Testing
- [ ] **Status:** future

**What:** Generate random directory trees, archive them, extract them,
and compare file hashes to verify round-trip integrity.

**File:** `tests/Archiver.Core.Tests/`

**Acceptance criteria:**
- [ ] Test generates random directory tree (random depth, file count, sizes)
- [ ] Archive → Extract → compare SHA-256 hash of every file
- [ ] Tested with: all-small files, all-large files, mixed, deep nesting
- [ ] 10+ random seeds tested per run
- [ ] dotnet test passes

---

### T-F25 — README Security Positioning Review
- [ ] **Status:** future

**What:** Review README.md security claims for accuracy and balance.
Avoid unverifiable superiority claims. Prefer factual positioning.

**Guidance:**
- Replace "more secure than X" with "different trust model than X"
- Emphasize: transparent architecture, managed runtime, supply chain transparency
- Keep factual CVE references — these are documented and verifiable
- Add caveat: .NET runtime itself is a trust dependency

**Acceptance criteria:**
- [ ] No unverifiable security superiority claims in README
- [ ] Supply chain risk section remains — factual CVE data kept
- [ ] Trust model framing added: "different trust model, not superior security"
- [ ] SECURITY.md unchanged — it is already appropriately nuanced

---

### T-F30 — Duplicate Filename Detection Inside Archive
- [ ] **Status:** future
- **Priority:** medium

**What:** ZIP format allows multiple entries with identical paths.
Pakko should detect and handle duplicates during both archive creation
and extraction.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] During archive: duplicate source paths detected → second occurrence renamed (add number)
- [ ] During extract: duplicate entry names detected → handled per ConflictBehavior
- [ ] dotnet test passes — new test: archive with duplicate entry names

---

### T-F31 — Deterministic Archive Output
- [ ] **Status:** future
- **Priority:** medium

**What:** Sort files before archiving so identical inputs always produce
identical archives. Useful for reproducible builds and testing.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] Files sorted by full path (ordinal, case-insensitive) before archive creation
- [ ] Two archive runs with identical input produce byte-identical output
- [ ] dotnet test passes — new test: same input twice → identical ZIP

---

### T-F32 — Directory Traversal Ordering
- [ ] **Status:** future
- **Priority:** medium

**What:** Directory.EnumerateFiles returns files in non-deterministic order.
Sort paths before processing for consistent, predictable archive structure.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Note:** Partially overlaps T-F31 — implement together.

**Acceptance criteria:**
- [ ] EnumerateFiles results sorted by path before archiving
- [ ] Archive entry order is alphabetical and consistent across runs
- [ ] dotnet test passes

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

### T-F40 — Shell Extension Project Setup
- [ ] **Status:** future (v1.2)

**What:** New project `Archiver.ShellExtension` (net8.0-windows) implementing `IExplorerCommand`. Registered via MSIX AppExtension — appears in modern Windows 11 context menu without requiring "Show more options".

**Acceptance criteria:**
- [ ] `Archiver.ShellExtension` project added to solution
- [ ] `IExplorerCommand` implementation compiles
- [ ] Registered via `Package.appxmanifest` AppExtension entry
- [ ] Context menu entry visible in Windows 11 modern context menu
- [ ] Uninstall removes context menu entry cleanly

---

### T-F41 — Context Menu: Extract Here
- [ ] **Status:** future (v1.2)

**What:** "Extract here" command on ZIP files — extracts to same folder as archive, no window.

**Acceptance criteria:**
- [ ] Appears on right-click of `.zip` files
- [ ] Extracts to same directory as archive (T-14 smart folder logic)
- [ ] Calls Archiver.App via protocol activation
- [ ] Toast notification on completion

---

### T-F42 — Context Menu: Extract to Folder
- [ ] **Status:** future (v1.2)

**What:** "Extract to `<folder_name>`\" on ZIP files — creates subfolder automatically.

**Acceptance criteria:**
- [ ] Appears on right-click of `.zip` files
- [ ] Creates `<archive_name>\` subfolder next to archive
- [ ] Extracts into the created subfolder
- [ ] Toast notification on completion

---

### T-F43 — Context Menu: Archive with Pakko
- [ ] **Status:** future (v1.2)

**What:** "Archive with Pakko" on any files/folders — single archive, Fast compression, destination = source folder.

**Acceptance criteria:**
- [ ] Appears on right-click of any files/folders
- [ ] Creates single `.zip` archive next to source items
- [ ] Uses Fast compression level
- [ ] Supports multi-selection (all items in one archive)
- [ ] Toast notification on completion

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
- [ ] **Status:** future (v1.2)
- **Priority:** high (security)

**What:** On extraction, read `Zone.Identifier` ADS from the source archive and write it to every extracted file. Always on by default — cannot be disabled by user (only via GPO in v1.4).

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Implementation:** `FileStream` opened with ADS path `"<file>:Zone.Identifier"`. No P/Invoke required — NTFS ADS is accessible via standard `FileStream` on Windows.

**Acceptance criteria:**
- [ ] `Zone.Identifier` ADS read from source archive before extraction
- [ ] `Zone.Identifier` ADS written to each extracted file
- [ ] If source archive has no `Zone.Identifier`, skip silently (no error)
- [ ] Always on — no user setting to disable
- [ ] `dotnet test` passes — new test: extracted files inherit `Zone.Identifier` from archive

---

### T-F46 — File Hash Viewer
- [ ] **Status:** future (v1.2)

**What:** Select file(s) → show SHA-256 hash in UI. Useful for integrity verification before opening extracted files.

**Acceptance criteria:**
- [ ] File picker → show SHA-256 hash of selected file(s)
- [ ] UI only — no new service methods required
- [ ] Hash computed via `System.Security.Cryptography.SHA256`

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

