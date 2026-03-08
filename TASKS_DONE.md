# TASKS_DONE.md — Completed Tasks Archive

> ⚠ **DO NOT RE-IMPLEMENT anything in this file.**
> All tasks here are complete and committed. Re-implementing will break existing behavior.
>
> For active and future tasks see [`TASKS.md`](TASKS.md).

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

## Phase 1 — Project Setup

### T-01 — Create Solution and Projects
- [x] **Status:** complete

### T-02 — Create Folder Structure
- [x] **Status:** complete

---

## Phase 2 — Core Models

### T-03 — Implement ArchiveOptions
- [x] **Status:** complete

### T-04 — Implement ExtractOptions
- [x] **Status:** complete

### T-05 — Implement ArchiveResult and ArchiveError
- [x] **Status:** complete

---

## Phase 3 — Core Interface and Service

### T-06 — Implement IArchiveService
- [x] **Status:** complete

### T-07 — Implement ZipArchiveService
- [x] **Status:** complete

---

## Phase 3b — Tests

### T-12 — Implement Test Project
- [x] **Status:** complete

---

## Phase 4 — UI Layer

### T-08 — Implement MainViewModel
- [x] **Status:** complete

### T-09 — Implement MainWindow
- [x] **Status:** complete

---

## Phase 5 — Error Handling UI

### T-10 — DialogService for Error Display
- [x] **Status:** complete

---

## Phase 5b — UX Improvements

### T-13 — ZIP Detection (extension-based)
- [x] **Status:** complete — superseded by T-13.1

### T-13.1 — Upgrade ZIP Detection to Magic Bytes
- [x] **Status:** complete

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

`IsZipFile()` uses magic bytes `50 4B 03 04`. Extension check removed entirely.

### T-13.2 — Inform User About Skipped Non-ZIP Files
- [x] **Status:** complete

`ArchiveResult.SkippedFiles` added. Known formats detected by magic bytes:
RAR `52 61 72 21`, 7-Zip `37 7A BC AF 27 1C`, GZip `1F 8B`, BZip2 `42 5A 68`, XZ `FD 37 7A 58 5A 00`, LZ4 `04 22 4D 18`.

### T-14 — Smart Extract Folder Logic
- [x] **Status:** complete

Single root folder → no double-nesting. Multiple root items → subfolder. Single root file → direct. ZIP slip protection on every entry.

### T-15 — Add Files and Add Folder Buttons
- [x] **Status:** complete

### T-16 — Destination Path Row
- [x] **Status:** complete

Default = folder of first item. Falls back to Desktop.

### T-17 — Remove Item from List (Right-click)
- [x] **Status:** complete

### T-18 — Post-Action Options — UI and Service Logic
- [x] **Status:** complete

`OpenDestinationFolder`, `DeleteSourceFiles`, `DeleteArchiveAfterExtraction` — all wired to service.

### T-19 — Operation Summary Dialog
- [x] **Status:** complete

`ShowOperationSummaryAsync` in `IDialogService`. Shows Errors / Skipped / Warnings sections conditionally.

### T-20 — Archive Name Field
- [x] **Status:** complete

### T-21 — File List Table with Columns
- [x] **Status:** complete

`FileItem` model with Name, Type, Size, SizeBytes, Modified, FullPath. Sorting by any column.

### T-22 — Archive Mode Toggle (Single / Separate)
- [x] **Status:** complete

### T-23 — Conflict Behavior Dropdown
- [x] **Status:** complete

`ConflictBehavior`: Overwrite, Skip, Rename. `Ask` removed — default is `Skip`.

### T-24 — Compression Level Selector
- [x] **Status:** complete

`CompressionLevel` in `ArchiveOptions`. Fast / Normal / Best / None.

### T-25 — Detect and Report Password-Protected ZIP Archives
- [x] **Status:** complete

Reads bit 0 of general purpose bit flag. Returns `ArchiveError` with message "This archive is password-protected and cannot be extracted."

### T-26 — Windows Compatibility Target
- [x] **Status:** complete

`TargetFramework` → `net8.0-windows10.0.17763.0`. Supports Windows 10 1809+ and Windows Server 2019+.

### T-27 — Replace ZipFile.CreateFromDirectory with Lazy Enumeration
- [x] **Status:** complete

`Directory.EnumerateFiles` via `AddDirectoryToArchive` — lazy, no upfront collection.

### T-28 — Internationalization Foundation (ResW)
- [x] **Status:** complete

`Strings/en-US/Resources.resw`. All UI strings via `x:Uid`. Dynamic strings via `ResourceLoader`. `Archiver.Core` has zero UI references.

### T-29 — Drag & Drop on File List Area
- [x] **Status:** complete

`AllowDrop="True"` on `ListView`. Empty-state hint overlay with `IsHitTestVisible="False"`. Drop works anywhere in list area.

### T-30 — App Title and Simple File Log
- [x] **Status:** complete

`AppWindow.Title = "Pakko"`. Log at `%LocalAppData%\Pakko\logs\pakko.log`. Rotation at 1 MB, max 3 rotated files. `ILogService` / `LogService` in DI.

### T-31 — App Icon and System Tray
- [x] **Status:** complete

`H.NotifyIcon.WinUI 2.1.0`. `TaskbarIcon` with `ContextFlyout`. "Open Pakko" / "Exit". `AppWindow.SetIcon()` with generated ICO. `TrayIcon.Dispose()` on close.

### T-32 — File List Minimum Height and Layout Fix
- [x] **Status:** complete

File list row `Height="*"`. Window size `800×700`.

### T-33 — Real-Time Progress During Archive and Extract
- [x] **Status:** complete

Both `ArchiveAsync` (SingleArchive) and `ExtractAsync` wrapped in `Task.Run`. Per-file `progress?.Report()`. `IsIndeterminate` for single large file/archive >10 MB. `IsOperationRunning` disables all buttons.

### T-34 — SHA-256 Integrity Manifest in ZIP Comment
- [x] **Status:** complete → Removed in post-v1.0 — redundant with ZIP built-in CRC-32

`PAKKO-INTEGRITY-V1` header in ZIP comment. SHA-256 per entry after archive creation. Verification on extract — mismatch → `ArchiveResult.Warnings`, not errors. `ArchiveResult.Warnings` field added.

### T-35 — Test Fixtures — Real Archive Files
- [x] **Status:** complete

`FixtureHelper` with `Archive()`, `ArchiveOptional()`, `PlainFile()` — `Assert.Inconclusive` for missing fixtures. 18 new fixture-based tests. 45/45 tests pass total.

---

## Phase 6 — Packaging

### T-11 — MSIX Packaging Setup
- [x] **Status:** complete

`win-x64-msix.pubxml` publish profile. `GenerateAppxPackageOnBuild=false` by default. `AppxPackageSigningEnabled=false`. Output: `AppPackages/Archiver.App_1.0.0.0_x64_Test/`. Requires self-signed cert or Developer Mode for local install.

### T-36 — Pakko Branding Icons
- [x] **Status:** complete

Steel blue `#1D5FA8`, slab bold П lettermark. All `Assets/` placeholders replaced: `Square44x44Logo.scale-200.png`, `Square44x44Logo.targetsize-24_altform-unplated.png`, `Square150x150Logo.scale-200.png`, `Wide310x150Logo.scale-200.png`, `StoreLogo.png`, `Square44x44Logo.ico`, `SplashScreen.scale-200.png`. Source `pakko_*.png` / `pakko.ico` files removed after copy. Build: 0 warnings, 0 errors.
