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

## v1.2 — Shell Extension

> **Minimum supported OS:** Windows 10 1809 (10.0.17763.0).
> Shell extension uses dual registration:
> - `desktop4:FileExplorerContextMenus` — Win10 1809+, classic context menu
> - `IExplorerCommand` via COM — Win11 22000+, modern context menu
>
> Both mechanisms invoke `Archiver.Shell.exe`. No separate code paths needed.

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

### T-F41 — Context Menu: Extract Here
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
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
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
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
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
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
| Test | ≥1 file needs extraction | done — `TestCommand` (see `TASKS_DONE.md`'s T-F62) |
| Extract (dialog, picks destination) | ≥1 file needs extraction | done — `ExtractDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
| Extract Here | ≥1 file needs extraction | done — `ExtractHereCommand` (already smart: `SeparateFolders` mode strips/wraps as needed, equivalent to NanaZip's separate "Extract Here (Smart)") |
| Extract Here (Smart) | ≥1 file needs extraction | n/a — folded into Pakko's "Extract here" above, not a separate verb |
| Extract to "\<folder\>" | ≥1 file needs extraction | done — `ExtractFolderCommand` |
| Compress (dialog, format/options) | any selection | done — `CompressDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
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

## v1.3 — tar.exe Integration

### T-F47 — ITarService Interface and TarCapabilities
- [x] **Status:** complete — scaffolding only; real detection/extraction land in T-F48/T-F49

**What:** Add `ITarService` interface and `TarCapabilities` record to `Archiver.Core`. `TarProcessService` implements `ITarService`. Register in DI.

**File:** `src/Archiver.Core/Interfaces/ITarService.cs`, `src/Archiver.Core/Models/TarCapabilities.cs`, `src/Archiver.Core/Services/TarProcessService.cs`

**Implementation:** Signatures match `ARCHITECTURE.md`'s "v1.3 — ITarService Layer" section verbatim
(including `ExtractAsync`'s `IProgress<int>?`, deliberately not `IProgress<ProgressReport>?` — a
different, not-yet-implemented interface per T-F73). `TarProcessService.DetectCapabilitiesAsync`
returns a safe all-unsupported `TarCapabilities` default (not a throw) since `App.xaml.cs`'s DI
registration resolves it eagerly as a singleton at startup (`GetAwaiter().GetResult()`);
`ExtractAsync` throws `NotImplementedException` since nothing calls it yet.

**Acceptance criteria:**
- [x] `TarCapabilities` record defined with `SupportsRar`, `Supports7z`, `SupportsZstd`, `SupportsXz`, `SupportsLzma`, `SupportsBz2`, `Version` properties
- [x] `ITarService` interface defined with `DetectCapabilitiesAsync()` and `ExtractAsync()`
- [x] `TarProcessService` class created (implementation in T-F48/T-F49)
- [x] DI registration added — `src/Archiver.App/App.xaml.cs`: `ITarService`/`TarProcessService` and
      the `TarCapabilities` singleton factory, mirroring `ARCHITECTURE.md`'s DI snippet
- [x] `dotnet build src/Archiver.Core` passes; `dotnet test --filter "Category!=Slow"` unaffected
      — 135/135 (`Archiver.App` itself requires Visual Studio to build per `CLAUDE.md`, not
      independently verified here)

---

### T-F48 — tar.exe Capability Detection
- [~] **Status:** partial (v1.3) — detection logic complete; UI criterion blocked on T-F36 (no
      tar format selector exists yet to grey out)

**What:** At app startup, run `C:\Windows\System32\tar.exe --version` to detect version and probe which formats are supported. Cache result as `TarCapabilities` singleton. UI greys out unsupported formats with tooltip "Requires Windows 11 23H2+".

**Implementation:** `TarProcessService.DetectCapabilitiesAsync` invokes `tar.exe --version`
(absolute path, stdout captured) and delegates parsing to the new `TarVersionParser.Parse`,
extracted into its own class so format detection is unit-testable without launching a process
(same rationale as `ShellArgumentParser`, T-F57). `Supports7z`/`SupportsRar`/`SupportsZstd` are
gated on libarchive >= 3.7.0 (matches `TESTING.md`'s documented "requires Win 11 23H2+ tar.exe"
note on all three formats — zstd is version-gated, not just token-gated, since a hypothetical
older libarchive build linking `libzstd` would still contradict that documented threshold).
`SupportsXz`/`SupportsLzma`/`SupportsBz2` are detected from the corresponding library tokens in
the version string, since `TESTING.md` does not flag those as 23H2+-only. Any failure to start
the process, or unrecognized output, returns the
all-unsupported `TarCapabilities` default — never throws. Found along the way: the T-F47
factory-registered `TarCapabilities` singleton only runs on first *resolution*, not at container
build — since nothing yet injects `TarCapabilities`, detection would silently never run. Fixed by
explicitly resolving it once in `App.xaml.cs`'s `ConfigureServices` right after
`BuildServiceProvider()`. Since that forced resolution runs synchronously on every app launch
(including the T-F83 cold-start path), `DetectCapabilitiesAsync` enforces a 5-second timeout via
an internal `CancellationTokenSource` and kills the process on expiry — a hung `tar.exe --version`
must not hang app launch indefinitely.

**Acceptance criteria:**
- [x] `DetectCapabilitiesAsync()` runs `C:\Windows\System32\tar.exe --version` (absolute path)
- [x] Parses version string and probes format support
- [x] Returns sensible defaults if tar.exe absent or probe fails
- [x] Result cached — detection runs once at startup (`App.xaml.cs` forces resolution explicitly;
      see note above — a bare DI registration alone does not run it)
- [ ] UI greys out formats not supported by detected tar.exe — no tar format selector exists in
      the UI yet; blocked on T-F36 (Pluggable Archive Engine Interface / format dropdown)
- [x] `dotnet test` passes — unit test with mocked process output (`TarVersionParserTests`, no
      process launch)

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

### T-F84 — Bug: Deploy.ps1's Post-Build Hook Fails on Cyrillic-Locale Machines (Mojibake)
- [x] **Status:** complete — found and fixed 2026-07-07 while verifying T-F47/T-F48 built cleanly
      in Visual Studio

**What:** Found while asking Visual Studio to build the solution (needed since `dotnet build`
cannot build `Archiver.App`, a WinUI 3 project). The Release build failed with `MSB3073`: the
post-build hook's `Deploy.ps1 -DeployOnly` invocation exited with code 1.

**Root cause:** the same mojibake bug class documented three times already in this project's C++
code (T-F64, T-F76, T-F63), now found for the first time in a PowerShell script. `Deploy.ps1` line
204 had a literal em-dash inside a `Write-Warning` string; the file is UTF-8 without a BOM, and
Windows PowerShell 5.1 decoded it via the system ANSI code page (cp1251, Cyrillic locale) instead
of UTF-8, corrupting the em-dash into `вЂ”` and breaking the string's terminator — reported by the
parser as misleading `Missing closing '}'` errors several lines away. See `DECISIONS.md`'s "T-F84"
entry for the full trace.

**Fix:** replaced the em-dash with a plain ASCII hyphen. `grep -P "[^\x00-\x7F]"` run over every
`scripts/*.ps1` (not just `Deploy.ps1`) found one more live instance in `Setup-DevCert.ps1` line
21 — fixed the same way; that script is arguably higher-risk since it explicitly relaunches
itself via `Start-Process powershell` (Windows PowerShell) when not elevated. The many
em-dash/box-drawing comment dividers in both files are unaffected (comments don't need a matching
terminator) and were left alone. `CONVENTIONS.md` gained a new "PowerShell Scripts" section for
this rule.

**Acceptance criteria:**
- [x] `scripts/Deploy.ps1`'s em-dash replaced with an ASCII-safe substitute
- [x] Every other `scripts/*.ps1` file checked (`grep -P "[^\x00-\x7F]"`) — `Setup-DevCert.ps1`'s
      matching bug found and fixed too
- [x] `[System.Management.Automation.Language.Parser]::ParseFile`, run via real `powershell.exe`
      (Windows PowerShell 5.1, the actually-vulnerable interpreter — pwsh 7 would pass either way),
      confirms zero parse errors on both files after the fix
- [x] `Deploy.ps1 -DeployOnly` run directly completes successfully (installed Pakko 1.1.0.42)
- [x] Visual Studio Release build of the full solution completes with 0 errors / 0 warnings
- [x] `CONVENTIONS.md` updated so this bug class is documented for PowerShell scripts too, not
      just C++ (`CLAUDE.md`'s hard constraint intentionally left alone — out of scope without
      explicit sign-off, per its own "Do Not modify CLAUDE.md" rule)

