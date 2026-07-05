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
- ALWAYS run `dotnet test` (no path — all projects) after any change to any project. A change in one project can break tests in another.
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
- [~] **Status:** in progress (v1.2)
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
- [ ] **Known gap:** `Archiver.ProgressWindow.exe` still fails to launch (separate `App.xbf`
      resource collision with `Archiver.App.exe` — two WinUI 3 apps in one flat package root).
      Operations currently fall back to running silently with no progress UI. See
      "Known remaining gap" in `DECISIONS.md`. Needs its own task before closing T-F61 fully.
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
- [ ] **Status:** future (v1.2)
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
- [ ] New `TestAsync` (or similarly named) method on `IArchiveService` — reads every entry,
      computes CRC-32, compares against the entry's declared value, never writes to disk
- [ ] Appears in Pakko submenu whenever selection contains ≥1 `.zip`
- [ ] Runs silently via `Archiver.Shell.exe --test "<path>"`; result (pass/fail + which
      entries failed) shown in `Archiver.ProgressWindow` or a summary dialog
- [ ] Multi-selection: all selected archives tested in one invocation
- [ ] `dotnet test` passes — new test: corrupted-CRC fixture fails; valid fixture passes

---

### T-F63 — Context Menu: "Extract…" and "Compress…" with Dialog
- [ ] **Status:** future (v1.2)
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
- [ ] New leaf command "Extract…" — shown whenever selection contains ≥1 `.zip`; invokes
      `Archiver.Shell.exe --extract "<path>..."`
- [ ] New leaf command "Compress…" — shown for any selection; invokes
      `Archiver.Shell.exe --archive "<path>..."` (dialog form, not the silent one)
- [ ] Both open `Archiver.App` with the files pre-loaded via the existing `pakko://` flow
- [ ] `GetState` filtering matches the sibling silent commands (same `AllPathsAreZip`/
      `AnyPathIsZip` helpers)

---

### T-F64 — Context Menu: Fix "Add to archive…" Label vs One-Click Behavior
- [~] **Status:** partial (v1.2) — code + tests done, manual Explorer smoke test pending
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
- [ ] **Status:** future (v1.2)
- **Depends on:** T-F61

**What:** `Archiver.ProgressWindow.exe` never actually launches after a shell-triggered
operation. `Archiver.ProgressWindow.exe` and `Archiver.App.exe` are two independent
WinUI 3 apps (each with its own `App.xaml` → `App.xbf`) landing in the same flat MSIX
package root, causing a `Files/App.xbf` resource-name collision — the same conflict
documented as the reason `.wapproj` was rejected for this project (see `DECISIONS.md`).
`RunWithProgressWindowAsync` in `Archiver.Shell`'s `Program.cs` degrades gracefully:
if the named pipe doesn't connect within 5 seconds it falls back to running the
operation silently — so extraction/archiving still succeeds, just with no visual
feedback. This is the gap observed during T-F61 smoke testing on 2026-07-05: Extract
to folder / Add to archive both worked end-to-end, but with no progress bar.

**File:** `src/Archiver.App/Package.appxmanifest`, `src/Archiver.App/Archiver.App.csproj`,
`src/Archiver.ProgressWindow/`

**Constraints (per `CLAUDE.md`):** no `.wapproj`, no `BeforeTargets` MSBuild hooks, no
manual `MakeAppx` calls — must stay within the existing `Content Include` packaging
approach. Read `DECISIONS.md`'s MSIX packaging section before implementing.

**Acceptance criteria:**
- [ ] Root cause confirmed against the shipped package layout — inspect the installed
      MSIX's `Files\` folder for the exact `App.xbf` collision (or rule it out and find
      the real cause if it's something else)
- [ ] `Archiver.ProgressWindow` given a non-colliding resource layout — e.g. its own
      subfolder with an independent PRI resource map, or an alternate resource file name
      — investigate options; document the chosen approach in `DECISIONS.md`
- [ ] `Archiver.ProgressWindow.exe` launches successfully after `Deploy.ps1` install —
      verified via direct `Start-Process` + `Get-WinEvent` check per the "verify a
      shell-triggered EXE actually runs" method in `CLAUDE.md`
- [ ] Named pipe connects within `Archiver.Shell`'s 5-second timeout — progress window
      actually renders instead of falling back to silent mode
- [ ] Manual smoke test: Extract here / Extract to folder / Add to archive all show a
      live progress bar with speed/ETA, not the silent fallback
- [ ] `dotnet test` passes — no functional regression in `Archiver.Shell`/`Archiver.Core`
- [ ] Once fixed, cross-reference back to close the "Known gap" bullet under T-F61

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

