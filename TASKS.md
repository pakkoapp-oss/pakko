# TASKS.md — Implementation Tasks

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
- Archive-only options (Name, Mode, Compression, DeleteSourceFiles) → passed only to `ArchiveOptions`, ignored when Extract is pressed
- Extract-only options (DeleteArchiveAfterExtraction) → passed only to `ExtractOptions`, ignored when Archive is pressed
- Shared options (Destination, OnConflict, OpenDestinationFolder) → passed to both

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

---

### T-13.1 — Upgrade ZIP Detection to Magic Bytes
- [x] **Status:** complete

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Replace extension-based ZIP check with magic bytes detection.
ZIP format always starts with bytes `50 4B 03 04` (`PK♥♦`).

**Acceptance criteria:**
- [x] `IsZipFile()` private method uses magic bytes `50 4B 03 04`
- [x] Extension check removed entirely
- [x] `.jar`, `.docx`, `.xlsx`, `.apk` with valid ZIP content extracted successfully
- [x] File with `.zip` extension but wrong magic bytes → skipped silently
- [x] File with ZIP magic bytes but corrupted → `ArchiveError`
- [x] `dotnet test` passes
- [x] New test cases added

---

### T-13.2 — Inform User About Skipped Non-ZIP Files
- [x] **Status:** complete

**Files:**
- `src/Archiver.Core/Models/ArchiveResult.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Add `SkippedFiles` to `ArchiveResult`. Known non-ZIP formats reported with friendly name.
Unknown binaries skipped silently.

| Magic bytes | Format |
|-------------|--------|
| `52 61 72 21` | RAR |
| `37 7A BC AF 27 1C` | 7-Zip |
| `1F 8B` | GZip |
| `42 5A 68` | BZip2 |
| `FD 37 7A 58 5A 00` | XZ |
| `04 22 4D 18` | LZ4 |

**Acceptance criteria:**
- [x] `SkippedFile` sealed record in `src/Archiver.Core/Models/`
- [x] `ArchiveResult.SkippedFiles` is `IReadOnlyList<SkippedFile>`, defaults to `[]`
- [x] `IsKnownArchiveFormat()` checks magic bytes for all formats above
- [x] Known non-ZIP archives added to `SkippedFiles` with friendly reason
- [x] Unknown binaries skipped silently
- [x] `ArchiveResult.Success` stays `true` when only skips occurred
- [x] `dotnet test` passes
- [x] New test cases added

---

### T-14 — Smart Extract Folder Logic
- [x] **Status:** complete

**Acceptance criteria:**
- [x] Single root folder → no double-nesting
- [x] Multiple root items → subfolder created named after archive
- [x] Single root file → extracted directly
- [x] ZIP slip protection on every entry
- [x] Existing tests pass, new test cases added

---

### T-15 — Add Files and Add Folder Buttons
- [x] **Status:** complete

**Acceptance criteria:**
- [x] "Add files" opens `FileOpenPicker` — multi-select
- [x] "Add folder" opens `FolderPicker`
- [x] Both add to `SelectedPaths` without duplicates
- [x] Double-click on drop zone triggers files picker
- [x] Hint text updated

---

### T-16 — Destination Path Row
- [x] **Status:** complete

**Acceptance criteria:**
- [x] `DestinationPath` observable `string` in ViewModel
- [x] Default = folder of first item in `SelectedPaths`
- [x] If empty → Desktop
- [x] `...` button opens `FolderPicker`
- [x] `DestinationPath` passed to both `ArchiveOptions` and `ExtractOptions`

---

### T-17 — Remove Item from List (Right-click)
- [x] **Status:** complete

**Acceptance criteria:**
- [x] Right-click shows `MenuFlyout` with "Remove"
- [x] Clicking "Remove" calls `ViewModel.RemovePath(path)`
- [x] No business logic in code-behind

---

### T-18 — Post-Action Options — UI and Service Logic
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Three post-action checkboxes grouped by relevance.

**UI layout:**
```
── Archive options ──────────────────────────
  [ ] Delete source files after archiving    ← archive-only

── Extract options ──────────────────────────
  [ ] Delete archive after extraction        ← extract-only

── Always ───────────────────────────────────
  [ ] Open destination folder after completion
```

**Scope (per Agent Rules):**
- `DeleteSourceFiles` → only read in `ArchiveAsync`, ignored in `ExtractAsync`
- `DeleteArchiveAfterExtraction` → only read in `ExtractAsync`, ignored in `ArchiveAsync`
- `OpenDestinationFolder` → read in both

**Service logic to implement in ZipArchiveService:**
```csharp
// OpenDestinationFolder — both Archive and Extract:
if (options.OpenDestinationFolder && result.Success)
    Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true });

// DeleteSourceFiles — ArchiveAsync only:
if (options.DeleteSourceFiles && result.Success)
    foreach (var path in options.SourcePaths)
    {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        } catch { /* silent */ }
    }

// DeleteArchiveAfterExtraction — ExtractAsync only:
if (options.DeleteArchiveAfterExtraction && result.Success)
    foreach (var path in options.ArchivePaths)
        try { if (File.Exists(path)) File.Delete(path); } catch { /* silent */ }
```

**Acceptance criteria:**
- [x] `OpenDestinationFolder` observable `bool` in ViewModel, default `false`
- [x] `DeleteSourceFiles` observable `bool` in ViewModel, default `false`
- [x] `DeleteArchiveAfterExtraction` observable `bool` in ViewModel, default `false`
- [x] All three values passed to `ArchiveOptions` and `ExtractOptions`
- [x] UI grouped as: Archive options / Extract options / Always
- [x] `ZipArchiveService.ArchiveAsync` opens Explorer if `OpenDestinationFolder` and `Success`
- [x] `ZipArchiveService.ArchiveAsync` deletes source paths if `DeleteSourceFiles` and `Success`
- [x] `ZipArchiveService.ExtractAsync` opens Explorer if `OpenDestinationFolder` and `Success`
- [x] `ZipArchiveService.ExtractAsync` deletes archive files if `DeleteArchiveAfterExtraction` and `Success`
- [x] Delete failures caught silently — do not throw, do not add to `Errors`
- [x] `dotnet test` passes — tests added for DeleteSourceFiles and DeleteArchiveAfterExtraction

---

### T-19 — Operation Summary Dialog
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/Services/IDialogService.cs`
- `src/Archiver.App/Services/DialogService.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Show summary dialog after operation only if errors or skipped files exist.
On full success — only update `StatusMessage`, no dialog.

**UI appearance:**
```
┌─────────────────────────────────────┐
│  Completed with issues              │
├─────────────────────────────────────┤
│  ✗ Errors (2)                       │
│                                     │
│    document.pdf                     │
│    File is locked by another process│
│                                     │
│    archive.zip                      │
│    File has ZIP signature but       │
│    appears corrupted or incomplete  │
├─────────────────────────────────────┤
│  ⊘ Skipped — unsupported format (2) │
│                                     │
│    backup.rar                       │
│    RAR format is not supported      │
│                                     │
│    archive.7z                       │
│    7-Zip format is not supported    │
├─────────────────────────────────────┤
│                   [  OK  ]          │
└─────────────────────────────────────┘
```

Each section shown only if it has items.

**Interface:**
```csharp
Task ShowOperationSummaryAsync(string operationName, ArchiveResult result);
```

**Acceptance criteria:**
- [x] `ShowOperationSummaryAsync` added to `IDialogService`
- [x] `DialogService` implements using `ContentDialog` + `ScrollViewer` + `StackPanel`
- [x] Errors section shown only if `result.Errors.Count > 0`
- [x] Skipped section shown only if `result.SkippedFiles.Count > 0`
- [x] No dialog when both lists empty
- [x] On success: only `StatusMessage` updated
- [x] `MainViewModel` calls `ShowOperationSummaryAsync` after both Archive and Extract
- [x] `Archiver.Core` has zero UI references

---

### T-20 — Archive Name Field
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Text field for custom archive name in Archive options section.
Empty = auto-name from first item in `SelectedPaths`.

**Scope:** Archive-only — passed to `ArchiveOptions.ArchiveName`, not used in Extract.

**UI layout (inside Archive options group):**
```
Name: [my-backup                    ]   placeholder: "Auto"
```

**Acceptance criteria:**
- [x] `ArchiveName` observable `string?` in ViewModel, default `null`
- [x] TextBox placeholder: "Auto (based on first file/folder name)"
- [x] Empty/whitespace → `null` passed to `ArchiveOptions.ArchiveName`
- [x] `ZipArchiveService` uses `options.ArchiveName` when not null for SingleArchive mode
- [x] Ignored when Extract is pressed

---

### T-21 — File List Table with Columns
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.App/Models/FileItem.cs`

**What:** Replace plain `ListView` with table showing file metadata.

Columns:
| Column | Source | Notes |
|--------|--------|-------|
| Name | `Path.GetFileName()` | Full path as tooltip |
| Type | extension or "Folder" | Uppercase, no dot |
| Size | `FileInfo.Length` / recursive | Async, shows "..." for folders |
| Modified | `FileInfo.LastWriteTime` | `yyyy-MM-dd HH:mm` |

**Acceptance criteria:**
- [x] `FileItem` model: `Name`, `Type`, `Size`, `SizeBytes`, `Modified`, `FullPath`
- [x] Table shows all four columns
- [x] Full path as tooltip on Name cell
- [x] Folder size async — shows "..." until calculated
- [x] Size human-readable: "1.2 MB", "345 KB", "12 bytes"
- [x] Sorting by any column
- [x] Right-click Remove still works
- [x] No duplicate paths

---

### T-22 — Archive Mode Toggle (Single / Separate)
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** RadioButtons inside Archive options group.

**Scope:** Archive-only — passed to `ArchiveOptions.Mode`, ignored in Extract.

**UI (inside Archive options group):**
```
Mode: (•) One archive   ( ) Separate archives
```

**Acceptance criteria:**
- [ ] `SelectedArchiveMode` observable `ArchiveMode` in ViewModel, default `SingleArchive`
- [ ] Two `RadioButton` controls bound to ViewModel
- [ ] Archive name field (T-20) disabled when mode is `SeparateArchives`
- [ ] Passed to `ArchiveOptions.Mode` only — not to `ExtractOptions`

---

### T-23 — Conflict Behavior Dropdown
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Dropdown for conflict resolution in shared options section.

**Scope:** Shared — passed to both `ArchiveOptions.OnConflict` and `ExtractOptions.OnConflict`.

**UI (in shared/always section):**
```
If file exists: [ Skip ▼ ]
                  Overwrite
                  Skip
                  Rename (add number)
```

**Note:** `Ask` removed for v1.0 — default is `Skip` which is safe.

**Acceptance criteria:**
- [ ] `OnConflict` observable `ConflictBehavior` in ViewModel, default `ConflictBehavior.Skip`
- [ ] `ComboBox` with three options: Overwrite, Skip, Rename
- [ ] Passed to both `ArchiveOptions.OnConflict` and `ExtractOptions.OnConflict`
- [ ] `ZipArchiveService` implements `Skip` — skips silently if output exists
- [ ] `ZipArchiveService` implements `Rename` — appends `(1)`, `(2)` etc to filename
- [ ] `dotnet test` passes — tests for Skip and Rename behavior

---

### T-24 — Compression Level Selector
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Models/ArchiveOptions.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Dropdown for compression level inside Archive options group.

**Scope:** Archive-only — passed to `ArchiveOptions.CompressionLevel`, not used in Extract.

**UI (inside Archive options group):**
```
Compression: [ Normal ▼ ]
               Fast
               Normal
               Best
               None
```

**Acceptance criteria:**
- [ ] `CompressionLevel` added to `ArchiveOptions`, default `CompressionLevel.Optimal`
- [ ] `ComboBox` with four options bound to ViewModel
- [ ] Passed to `ArchiveOptions.CompressionLevel` only
- [ ] `ZipArchiveService` uses `options.CompressionLevel` when creating entries
- [ ] `dotnet test` passes

---

### T-25 — Detect and Report Password-Protected ZIP Archives
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** `System.IO.Compression` does not support encrypted ZIP archives.
Instead of crashing or silently failing, detect encryption and report clearly.

ZIP encryption flag is bit 0 of the general purpose bit flag in the local file header.

**Decision logic:**
```
Open ZIP → check first entry flags
    ├── encrypted flag set → ArchiveError:
    │   "This archive is password-protected and cannot be extracted."
    └── not encrypted → extract normally
```

**Acceptance criteria:**
- [ ] Encrypted ZIP detected before extraction attempt
- [ ] Returns `ArchiveError` with message "This archive is password-protected and cannot be extracted."
- [ ] Does not throw unhandled exception
- [ ] `dotnet test` passes — test: encrypted ZIP → `ArchiveError` with correct message

---

### T-26 — Windows Compatibility Target
- [ ] **Status:** pending

**File:** `src/Archiver.App/Archiver.App.csproj`

**What:** Lower minimum Windows version from 2004 (19041) to 1809 (17763).
WinUI 3 minimum is Windows 10 1809. This also enables Windows Server 2019+.

**Supported after change:**
| OS | Version |
|----|---------|
| Windows 10 | 1809 (October 2018) and later |
| Windows Server | 2019 and later |
| Windows Server | 2022 |

**Change:**
```xml
<!-- Before -->
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>

<!-- After -->
<TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
```

**Acceptance criteria:**
- [ ] `TargetFramework` changed to `net8.0-windows10.0.17763.0`
- [ ] `Package.appxmanifest` `MinVersion` set to `10.0.17763.0`
- [ ] App builds without errors
- [ ] No API calls requiring version above 17763 — verify with build warnings

---

## Phase 6 — Packaging

### T-11 — MSIX Packaging Setup
- [ ] **Status:** pending

**Acceptance criteria:**
- [ ] App builds as MSIX package
- [ ] `Package.appxmanifest` correct `Identity`, `DisplayName`, `Description`
- [ ] Runs on Windows 10 1809+ (build 17763) — per T-26
- [ ] No capabilities beyond `runFullTrust`

---

## Phase 7 — Future (post v1.0)

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** future

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

### T-F03 — Dedicated Extract Window
- [ ] **Status:** future

### T-F04 — TAR/GZip/BZip2/XZ Support via Windows tar.exe
- [ ] **Status:** future

Uses Windows built-in `tar.exe` (available since Windows 10 1803, based on libarchive).
No third-party binaries — `tar.exe` is part of the OS.
Invoke via `System.Diagnostics.Process`.

### T-F05 — Archive Contents Preview
- [ ] **Status:** future

Click ZIP in list → read-only tree view of contents via `ZipFile.OpenRead`. No extraction.

### T-F06 — Ask on Conflict Dialog
- [ ] **Status:** future

Interactive dialog when conflict detected — Skip / Overwrite / Rename per file.

---

### T-F07 — Optional 7-Zip Extraction Support
- [ ] **Status:** future

**What:** Optional component — not bundled by default, installable separately via app settings
or as optional checkbox during MSIX install.

**Binary source:** NanaZip (MIT licensed fork of 7-Zip by M2Team, Japan).
Preferred over original 7-Zip due to reproducible builds and non-Russian maintainership.

**Security model:**
- SHA-256 hash of binary embedded as constant in source code
- Hash verified at runtime before every invocation
- Hash mismatch → operation refused, user notified with security error
- Binary stored in app's local data folder, not system-wide

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → clear security error, no execution
- [ ] Optional install — not present in base MSIX package
- [ ] `.7z` files extracted to destination using verified binary
- [ ] Falls back to "unsupported format" if binary not installed

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** future

**What:** Optional component for extracting `.rar` archives.
RAR is a closed format — only official RARLAB `unrar.exe` can be used legally.
Cannot be reimplemented — must use official binary.

**Binary source:** Official RARLAB `unrar.exe` (freeware license allows use in free software).

**Security model:** same as T-F07 — SHA-256 verification before every invocation.

**Installation:** same as T-F07 — optional, never bundled silently.

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → security error, no execution
- [ ] Optional install only
- [ ] `.rar` files extracted using verified binary

---

### T-F09 — CLI Core (Archiver.CLI)
- [ ] **Status:** future

**What:** Expose `Archiver.Core` as standalone CLI executable for scripting and automation.
New project `src/Archiver.CLI/` referencing `Archiver.Core` — no logic duplication.

Example:
```
archiver archive --src C:\files --dest C:\output --name backup
archiver extract --src C:\backup.zip --dest C:\output
```
