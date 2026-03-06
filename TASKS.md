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

## 📋 File Maintenance Note

This file will be split into two files when Phase 5b is fully complete (after T-33):

- `TASKS.md` — active tasks only (pending/future) + Agent Rules
- `TASKS_DONE.md` — archive of all completed tasks for reference

Each file must contain the Agent Rules section at the top.
`TASKS_DONE.md` must include a warning: "Do NOT re-implement anything in this file."
`TASKS.md` must include a reference: "See TASKS_DONE.md for completed tasks (T-01 through T-XX)."

**Trigger:** when all T-13.x through T-33 are marked `[x]` and before starting Phase 6 (T-11).

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

**UX note:** `FolderPicker` is limited to single folder selection by Windows Shell API —
`PickMultipleFoldersAsync` does not exist in WinRT. Workaround via COM `IFileOpenDialog`
with `FOS_ALLOWMULTISELECT` is possible but requires P/Invoke boilerplate — deferred.
Current mitigation: hint text below buttons reads
"For multiple folders — drag & drop from Explorer"
Drag & drop already supports multiple folders simultaneously.

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
{
    using var p = Process.Start(
        new ProcessStartInfo("explorer.exe", options.DestinationFolder)
        { UseShellExecute = true });
    // using ensures Process handle is released immediately after Start
}

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
- [x] **Status:** complete

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
- [x] `SelectedArchiveMode` observable `ArchiveMode` in ViewModel, default `SingleArchive`
- [x] Two `RadioButton` controls bound to ViewModel
- [x] Archive name field (T-20) disabled when mode is `SeparateArchives`
- [x] Passed to `ArchiveOptions.Mode` only — not to `ExtractOptions`

---

### T-23 — Conflict Behavior Dropdown
- [x] **Status:** complete

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
- [x] `OnConflict` observable `ConflictBehavior` in ViewModel, default `ConflictBehavior.Skip`
- [x] `ComboBox` with three options: Overwrite, Skip, Rename
- [x] Passed to both `ArchiveOptions.OnConflict` and `ExtractOptions.OnConflict`
- [x] `ZipArchiveService` implements `Skip` — skips silently if output exists
- [x] `ZipArchiveService` implements `Rename` — appends `(1)`, `(2)` etc to filename
- [x] `dotnet test` passes — tests for Skip and Rename behavior

---

### T-24 — Compression Level Selector
- [x] **Status:** complete

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
- [x] `CompressionLevel` added to `ArchiveOptions`, default `CompressionLevel.Optimal`
- [x] `ComboBox` with four options bound to ViewModel
- [x] Passed to `ArchiveOptions.CompressionLevel` only
- [x] `ZipArchiveService` uses `options.CompressionLevel` when creating entries
- [x] `dotnet test` passes

---

### T-25 — Detect and Report Password-Protected ZIP Archives
- [x] **Status:** complete

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
- [x] Encrypted ZIP detected before extraction attempt
- [x] Returns `ArchiveError` with message "This archive is password-protected and cannot be extracted."
- [x] Does not throw unhandled exception
- [x] `dotnet test` passes — test: encrypted ZIP → `ArchiveError` with correct message

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

### T-27 — Replace ZipFile.CreateFromDirectory with Lazy Enumeration
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** In `SeparateArchives` mode, folders are currently archived via
`ZipFile.CreateFromDirectory` which eagerly enumerates all files into memory before
writing. For folders with many files (100k+) this creates a large in-memory collection.

Replace with `AddDirectoryToArchive` which already exists in the service and uses
`Directory.EnumerateFiles` — lazy, one file at a time, no upfront collection.

**Current code (SeparateArchives, directory branch):**
```csharp
await Task.Run(() =>
    ZipFile.CreateFromDirectory(sourcePath, destPath, CompressionLevel.Optimal, includeBaseDirectory: true),
    cancellationToken).ConfigureAwait(false);
```

**Replacement:**
```csharp
await Task.Run(() =>
{
    using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);
    AddDirectoryToArchive(archive, sourcePath, Path.GetFileName(sourcePath));
}, cancellationToken).ConfigureAwait(false);
```

**Note:** `includeBaseDirectory: true` behaviour is preserved — `AddDirectoryToArchive`
already prefixes entries with the folder name via `entryPrefix`.

**Acceptance criteria:**
- [ ] `ZipFile.CreateFromDirectory` removed from `ArchiveAsync`
- [ ] `SeparateArchives` directory branch uses `AddDirectoryToArchive` instead
- [ ] Resulting ZIP structure identical — folder name preserved as root entry prefix
- [ ] `dotnet test` passes — existing archive tests unchanged

---

### T-28 — Internationalization Foundation (ResW)
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/Strings/en-US/Resources.resw` ← create
- `src/Archiver.App/MainWindow.xaml` ← replace hardcoded strings with `x:Uid`
- `src/Archiver.App/ViewModels/MainViewModel.cs` ← use `ResourceLoader` for dynamic strings
- `src/Archiver.App/Services/DialogService.cs` ← use `ResourceLoader` for dialog strings

**What:** Extract all UI strings into `.resw` resource files using the standard
Windows/WinUI 3 localization system. No third-party libraries.
English (`en-US`) is the only language for v1.0 — Ukrainian (`uk-UA`) added later.

**ResW file location:**
```
src/Archiver.App/
  Strings/
    en-US/
      Resources.resw    ← all UI strings here
```

**XAML usage — via x:Uid (zero code-behind):**
```xml
<!-- Resources.resw key: ArchiveButton.Content = "Archive" -->
<Button x:Uid="ArchiveButton" />

<!-- Resources.resw key: DropZoneHint.Text = "Drop files or folders here..." -->
<TextBlock x:Uid="DropZoneHint" />
```

**C# usage — via ResourceLoader:**
```csharp
private static readonly ResourceLoader _res = new();

// In ViewModel or DialogService:
StatusMessage = _res.GetString("StatusDone").Replace("{0}", count.ToString());
```

**Strings to extract — minimum set:**

| Key | Value |
|-----|-------|
| `ArchiveButton.Content` | Archive |
| `ExtractButton.Content` | Extract |
| `ClearButton.Content` | Clear |
| `AddFilesButton.Content` | Add files |
| `AddFolderButton.Content` | Add folder |
| `DropZoneHint.Text` | Drop files or folders here, or double-click to browse files |
| `MultipleFoldersHint.Text` | For multiple folders — drag & drop from Explorer |
| `DestinationLabel.Text` | Destination: |
| `ArchiveNameLabel.Text` | Name: |
| `CompressionLabel.Text` | Compression: |
| `ConflictLabel.Text` | If file exists: |
| `OpenDestinationCheck.Content` | Open destination folder after completion |
| `DeleteSourceCheck.Content` | Delete source files after archiving |
| `DeleteArchiveCheck.Content` | Delete archive after extraction |
| `StatusDone` | Done — {0} file(s) processed. |
| `ErrorDialogTitle` | Completed with issues |
| `ErrorSectionHeader` | Errors |
| `SkippedSectionHeader` | Skipped — unsupported format |

**Architecture boundary — Archiver.Core stays clean:**
- `Archiver.Core` must NOT reference `ResourceLoader` or any UI assembly
- Error messages in `ZipArchiveService` stay as English constants
- Translation of Core error messages happens in `MainViewModel` or `DialogService`
- Use an `ErrorMessageTranslator` helper class in `Archiver.App` if needed

**Acceptance criteria:**
- [ ] `Strings/en-US/Resources.resw` created with all strings from the table above
- [ ] All hardcoded strings in `MainWindow.xaml` replaced with `x:Uid`
- [ ] Dynamic strings in `MainViewModel` use `ResourceLoader.GetString()`
- [ ] Dynamic strings in `DialogService` use `ResourceLoader.GetString()`
- [ ] App builds and runs — all UI text appears correctly
- [ ] `Archiver.Core` has zero references to `ResourceLoader` or `Windows.ApplicationModel.Resources`
- [ ] Adding `uk-UA/Resources.resw` in future requires no code changes — only new file

---

### T-29 — Drag & Drop on File List Area
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`

**What:** Expand drag & drop target from the small drop zone hint area to the entire
file list/table area. When list is empty — drop zone hint is visible. When files are
added — user can still drag & drop directly onto the table.

This matches Bandizip and NanaZip behavior — the list area is always the drop target,
not the whole window (which would cause accidental drops on buttons and inputs).

**Behavior:**
```
List area empty  → shows large hint "Drop files or folders here..."
                   drop anywhere in list area works
List area filled → table visible, hint hidden
                   drop onto table still works — appends to existing list
```

**Implementation notes:**
- Move `DragOver` and `Drop` event handlers from drop zone hint to the list/table control
- The hint `TextBlock`/`Border` becomes purely visual — hidden when `SelectedPaths` not empty
- Use `AllowDrop="True"` on the `ListView`/`DataGrid` control itself
- `DragOver` → set `AcceptedOperation = DataPackageOperation.Copy`
- `Drop` → extract `StorageItems`, add paths via `ViewModel.AddPaths()`

**Acceptance criteria:**
- [ ] `AllowDrop="True"` set on file list control, not on drop zone hint
- [ ] Drop zone hint visible only when list is empty
- [ ] Drop zone hint hidden when list has items
- [ ] Drag & drop onto list area works when list is empty
- [ ] Drag & drop onto list area works when list already has items — appends
- [ ] Files and folders both accepted
- [ ] Duplicates still prevented
- [ ] Dropping on buttons, inputs, or other controls outside list area does NOT trigger add

---

### T-30 — App Title and Simple File Log
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/Services/ILogService.cs` ← create
- `src/Archiver.App/Services/LogService.cs` ← create
- `src/Archiver.App/App.xaml.cs`

**What:** Two separate concerns combined in one task:

**A) App title in taskbar and title bar:**
Currently shows raw executable name. Set a proper display name.

```csharp
// In MainWindow constructor or Activated handler:
AppWindow.Title = "Pakko";
```

Also set in `Package.appxmanifest` (for MSIX, T-11):
```xml
<uap:VisualElements DisplayName="Pakko" ... />
```

**B) Simple file log:**
Log significant events to `%LocalAppData%\Pakko\logs\pakko.log`.
No third-party libraries — plain `File.AppendAllText` with rotation.

Log format:
```
2025-01-15 14:32:01 [INFO]  Archive completed — 3 files → C:\Users\Pa\Desktop\backup.zip
2025-01-15 14:32:05 [INFO]  Extract completed — archive.zip → C:\Users\Pa\Downloads\
2025-01-15 14:32:10 [WARN]  Skipped backup.rar — RAR format not supported
2025-01-15 14:32:15 [ERROR] document.pdf — File is locked by another process
```

**Log rotation:** if `archiver.log` exceeds 1 MB — rename to `archiver.log.1` and start fresh.
Keep maximum 3 rotated files. No external libraries needed — check size before append.

**ILogService interface:**
```csharp
public interface ILogService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
```

**What to log:**
- Archive started / completed (file count, destination)
- Extract started / completed (file count, destination)
- Each `ArchiveError` → `Error()`
- Each `SkippedFile` → `Warn()`
- App startup

**Acceptance criteria:**
- [ ] `AppWindow.Title` set to "Pakko" in `MainWindow`
- [ ] Title visible correctly in taskbar and title bar
- [ ] `ILogService` and `LogService` created in `Archiver.App/Services/`
- [ ] Log written to `%LocalAppData%\Pakko\logs\pakko.log`
- [ ] Log directory created automatically if not exists
- [ ] Log rotation at 1 MB — old file renamed to `.log.1`, max 3 rotated files
- [ ] Archive and Extract results logged after every operation
- [ ] `ILogService` registered in DI container
- [ ] `Archiver.Core` has zero references to `ILogService` — logging done in ViewModel

---

### T-31 — App Icon and System Tray (Pakko Branding)
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/Assets/` ← icon files
- `src/Archiver.App/Package.appxmanifest`
- `src/Archiver.App/App.xaml.cs`
- `src/Archiver.App/Archiver.App.csproj`

**What:** Three levels of icon presence for Pakko:

**A) App icon** — taskbar, Alt+Tab, title bar, Start menu.
Required sizes for MSIX (PNG, transparent background):
| File | Size |
|------|------|
| `Square44x44Logo.png` | 44×44 |
| `Square44x44Logo.targetsize-16.png` | 16×16 |
| `Square44x44Logo.targetsize-32.png` | 32×32 |
| `Square150x150Logo.png` | 150×150 |
| `Wide310x150Logo.png` | 310×150 |
| `StoreLogo.png` | 50×50 |

All referenced in `Package.appxmanifest` under `<uap:VisualElements>`.

**B) Window title bar icon** — small icon in the custom title bar area.
```csharp
AppWindow.SetIcon("Assets/Square44x44Logo.ico");
```
Requires `.ico` file (can be converted from PNG).

**C) System tray icon** — icon in notification area, lives when window is minimized.
Use `H.NotifyIcon.WinUI` NuGet package (MIT license, actively maintained).

```xml
<!-- In MainWindow.xaml -->
<tb:TaskbarIcon
    IconSource="Assets/Square44x44Logo.ico"
    ToolTipText="Pakko">
    <tb:TaskbarIcon.ContextMenuMode>
        SecondWindow
    </tb:TaskbarIcon.ContextMenuMode>
</tb:TaskbarIcon>
```

Tray context menu minimum:
```
Open Pakko
─────────
Exit
```

**Note on icon design:** icon not designed in this task — placeholder from Assets/
is acceptable for v1.0. Proper icon design is separate creative work.

**Acceptance criteria:**
- [ ] All required PNG sizes present in `Assets/`
- [ ] `Package.appxmanifest` references all icon sizes correctly
- [ ] App icon appears correctly in taskbar and Alt+Tab
- [ ] Window title bar shows icon
- [ ] `H.NotifyIcon.WinUI` added to `Archiver.App.csproj`
- [ ] Tray icon visible in notification area when app is running
- [ ] Tray context menu: "Open Pakko" brings window to foreground, "Exit" closes app
- [ ] App name shown as "Pakko" everywhere — taskbar, tray tooltip, title bar

---


### T-32 — File List Minimum Height and Layout Fix
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`

**What:** On startup the file list table is too small — other UI elements push it out
and it becomes invisible or very thin. The list area should have a guaranteed minimum
height and should stretch to fill available space.

**Expected layout:**
```
┌─ Window ──────────────────────────────┐
│  Drop zone hint / file table  ↕ grows │  ← Height="*"
│  Destination row                      │  ← Height="Auto"
│  [ Archive ] [ Extract ] [ Clear ]    │  ← Height="Auto"
│  ── Archive options ──────────────────│  ← Height="Auto"
│  ── Extract options ──────────────────│  ← Height="Auto"
│  ── Always ───────────────────────────│  ← Height="Auto"
│  Status bar                           │  ← Height="Auto"
└───────────────────────────────────────┘
```

**Implementation notes:**
- File list row: `Height="*"` in Grid row definitions
- All other rows: `Height="Auto"`
- `MinHeight="120"` on the file list control
- Root layout must be `Grid`, not `StackPanel`

**Acceptance criteria:**
- [ ] File list visible on startup without resizing the window
- [ ] File list has `MinHeight="120"` — never collapses to zero
- [ ] File list grows when window is resized taller
- [ ] Options panel stays at bottom, compact, fixed height
- [ ] Layout correct at minimum window size (600×500)

---

### T-33 — Real-Time Progress During Archive and Extract
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Currently progress bar jumps from 0% to 100% after operation completes.
User sees no feedback during archiving OR extracting — the UI appears frozen.
Both `ArchiveAsync` and `ExtractAsync` are affected identically.

**Issue 1 — UI thread blocking:**
Both operations in ViewModel must run on background thread:
```csharp
var result = await Task.Run(() => _archiveService.ArchiveAsync(options, progress, ct));
var result = await Task.Run(() => _archiveService.ExtractAsync(options, progress, ct));
```

**Issue 2 — Progress not granular enough:**
Report after each file completes in both `ArchiveAsync` and `ExtractAsync`:
```csharp
progress?.Report((i + 1) * 100 / total);
```
For single large file/archive (>10 MB) → `IsIndeterminate = true`.

**ViewModel pattern — identical for both operations:**
```csharp
IsOperationRunning = true;
StatusMessage = "Archiving..."; // or "Extracting..."
Progress = 0;
var progress = new Progress<int>(p => Progress = p);
// await operation...
IsOperationRunning = false;
```

**Acceptance criteria:**
- [ ] Progress bar updates visibly during **archiving** — not just at end
- [ ] Progress bar updates visibly during **extraction** — not just at end
- [ ] Buttons (`Archive`, `Extract`, `Clear`, `Add files`, `Add folder`) disabled during both operations
- [ ] `IsOperationRunning` observable `bool` in ViewModel, default `false`
- [ ] Progress bar shows indeterminate animation for single large file/archive (>10 MB)
- [ ] Progress bar shows percentage for multiple files/archives
- [ ] UI remains responsive during both operations — window can be moved/resized
- [ ] `StatusMessage` shows "Archiving..." during archive operation
- [ ] `StatusMessage` shows "Extracting..." during extract operation
- [ ] `StatusMessage` updated to final result after completion of either operation
- [ ] `CancellationTokenSource` properly created per operation and disposed after
- [ ] `dotnet test` passes

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
- [ ] `Process` always disposed after use — wrap in `using` or explicit `Dispose()` in `finally`
- [ ] No orphaned process handles after extraction completes or fails

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
- [ ] `Process` always disposed after use — wrap in `using` or explicit `Dispose()` in `finally`
- [ ] No orphaned process handles after extraction completes or fails

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
