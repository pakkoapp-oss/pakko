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

## Current State — v1.0 Complete

- All T-01 through T-35 + T-11 complete and committed
- 45/45 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` (unsigned — see T-F10 for signing)
- Git tag: `v1.0.0`

---

## Future Tasks (post v1.0)

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
- [ ] **Status:** future

**What:** Optional component — not bundled by default, installable separately.

**Binary source:** NanaZip (MIT licensed fork of 7-Zip by M2Team, Japan).
Preferred over original 7-Zip due to reproducible builds and non-Russian maintainership.

**Security model:**
- SHA-256 hash of binary embedded as constant in source code
- Hash verified at runtime before every invocation
- Hash mismatch → operation refused, user notified with security error
- Binary stored in app's local data folder, not system-wide

**Process isolation:** see T-F13.

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → clear security error, no execution
- [ ] Optional install — not present in base MSIX package
- [ ] `.7z` files extracted to destination using verified binary
- [ ] Falls back to "unsupported format" if binary not installed
- [ ] `Process` always disposed after use — wrap in `using` or `finally`
- [ ] No orphaned process handles after extraction completes or fails

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** future

**What:** Optional component for `.rar` archives.
Official RARLAB `unrar.exe` (freeware license allows use in free software).

**Security model:** same as T-F07 — SHA-256 verification before every invocation.

**Process isolation:** see T-F13.

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → security error, no execution
- [ ] Optional install only
- [ ] `.rar` files extracted using verified binary
- [ ] `Process` always disposed after use
- [ ] No orphaned process handles

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
- [x] Smoke test on ARM64: archive and extract work correctly

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

**Threat model:** binary passes SHA-256 but has undiscovered vulnerability, or is compromised between verification and execution, or attempts network exfiltration.

**Layer 1 — Windows Job Object (P/Invoke):**
- `ActiveProcessLimit = 1` — cannot spawn child processes
- Memory limit 512 MB — prevent resource exhaustion
- UI restrictions — no clipboard, no desktop manipulation

**Layer 2 — WFP firewall rule:**
Added at optional component install time (requires elevation once):
```powershell
New-NetFirewallRule -DisplayName "Pakko — block 7z.exe outbound" `
    -Direction Outbound -Program "$env:LOCALAPPDATA\Pakko\tools\7z.exe" -Action Block
```
Rule removed on uninstall.

**What this does NOT cover:** full filesystem isolation, AppContainer-level isolation.

**Acceptance criteria (when implemented):**
- [ ] External binary process assigned to Job Object before execution
- [ ] `ActiveProcessLimit = 1`
- [ ] Memory limit enforced
- [ ] UI restrictions applied
- [ ] Firewall rule added at install, removed at uninstall
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
- About dialog with version and links (T-F14)
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

### T-F16 — Byte-accurate Progress Reporting
- [ ] **Status:** future

**What:** Replace the indeterminate progress bar with real percentage by wrapping `ZipArchiveService` streams in a `ProgressStream` that counts bytes written/read. `System.IO.Compression` does not expose byte-level progress natively, so stream wrapping is required.

**Key challenge:** Total byte count must be computed up-front (sum of source file sizes for archive, sum of compressed entry sizes for extract) before streaming begins.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs` and a new `src/Archiver.Core/IO/ProgressStream.cs`.

**Acceptance criteria:**
- [ ] `ProgressStream` wrapper class in `Archiver.Core` counts bytes read/written and reports to `IProgress<int>`
- [ ] `ArchiveAsync` reports real byte-based percentage for `SingleArchive` mode
- [ ] `ExtractAsync` reports real byte-based percentage
- [ ] `IsIndeterminate` removed from `MainViewModel` — replaced with real percentage
- [ ] `dotnet test` passes — existing tests unchanged

---

### T-F17 — Tray Left-Click Toggle
- [ ] **Status:** future

**What:** Left-click on the system tray icon toggles window visibility (show if hidden, hide if visible). Currently only right-click shows the context menu.

**File:** `src/Archiver.App/MainWindow.xaml.cs` and `MainWindow.xaml`.

**Acceptance criteria:**
- [ ] `TrayLeftClickCommand` added to `MainWindow.xaml.cs`
- [ ] Left-click on tray icon hides window if visible, shows if hidden
- [ ] `LeftClickCommand="{x:Bind TrayLeftClickCommand}"` wired in XAML
- [ ] Works in both Release and Debug configurations

---

### T-F18 — Operation Spinner on Action Buttons
- [ ] **Status:** future

**What:** Show an indeterminate `ProgressRing` inline next to the button text on Archive and Extract buttons while an operation is running. Provides visual feedback without relying solely on the progress bar.

**File:** `src/Archiver.App/MainWindow.xaml`.

**Acceptance criteria:**
- [ ] `ProgressRing` visible on Archive button while `IsOperationRunning = true`
- [ ] `ProgressRing` visible on Extract button while `IsOperationRunning = true`
- [ ] Buttons remain disabled during operation (existing behavior unchanged)
- [ ] No layout shift when spinner appears or disappears

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

### T-F26 — Temporary File Pattern for Safe Archive Creation
- [ ] **Status:** future
- **Priority:** high

**What:** Write archive to a .tmp file first. On success rename to final name.
On failure or cancellation delete the .tmp file. Prevents corrupted archives
from reaching the user.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] Archive written to destPath + ".tmp" during creation
- [ ] On success: .tmp renamed to final destination path
- [ ] On failure or exception: .tmp deleted, no partial archive left
- [ ] On cancellation: .tmp deleted cleanly
- [ ] dotnet test passes — new test: cancelled archive leaves no .tmp file

---

### T-F27 — Temporary Directory Pattern for Safe Extraction
- [ ] **Status:** future
- **Priority:** high

**What:** Extract to a temporary directory first. On success move to final
destination. On failure delete the temporary directory. Prevents partial
extraction on interruption.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] Extraction target is destPath + "_tmp" during operation
- [ ] On success: _tmp directory moved to final destination
- [ ] On failure or cancellation: _tmp directory deleted cleanly
- [ ] dotnet test passes — new test: cancelled extraction leaves no _tmp directory

---

### T-F28 — Archive Bomb Protection
- [ ] **Status:** future
- **Priority:** high

**What:** Limit extraction size to prevent ZIP bomb attacks.
ZIP bombs decompress to enormous sizes from small archives.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Limits:**
- Max total extraction size: 4 GB
- Max single entry size: 2 GB
- Max entry count: 100 000

**Acceptance criteria:**
- [ ] Total extraction size tracked during extract
- [ ] Exceeds 4 GB limit → ArchiveError "Archive extraction size limit exceeded"
- [ ] Single entry > 2 GB → ArchiveError "Entry size limit exceeded"
- [ ] Entry count > 100 000 → ArchiveError "Archive entry count limit exceeded"
- [ ] Limits configurable via ExtractOptions constants
- [ ] dotnet test passes — new tests for each limit

---

### T-F29 — UTF-8 Filename Encoding Verification
- [ ] **Status:** future
- **Priority:** high

**What:** Verify Cyrillic, Asian, emoji filenames are preserved correctly
in ZIP archives. ZIP historically used CP437 — ensure UTF-8 flag is set.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] Archive and extract file with Cyrillic name — name preserved exactly
- [ ] Archive and extract file with emoji name — name preserved exactly
- [ ] ZIP entries have UTF-8 encoding flag set
- [ ] dotnet test passes — new tests: Cyrillic filename round-trip, emoji filename round-trip

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
- [ ] **Status:** future
- **Priority:** low
- **Depends on:** T-F09 (CLI Core)

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
- [ ] **Status:** future
- **Priority:** low

**What:** Store Pakko version and creation timestamp in ZIP comment
alongside existing PAKKO-INTEGRITY-V1 manifest.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] PAKKO-VERSION written to ZIP comment on archive creation
- [ ] PAKKO-CREATED (UTC ISO 8601) written to ZIP comment
- [ ] Existing PAKKO-INTEGRITY-V1 format unchanged — new fields appended
- [ ] dotnet test passes — existing integrity tests unchanged

