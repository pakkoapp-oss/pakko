# TASKS.md — Implementation Tasks

Each task has: scope, files to create/modify, and **acceptance criteria**.
Agent marks task `[x]` only when ALL criteria pass.

---

## Phase 1 — Project Setup

### T-01 — Create Solution and Projects
- [x] **Status:** complete

**Acceptance criteria:**
- [x] `dotnet build src/Archiver.Core` runs without errors
- [x] `Archiver.Core` targets `net8.0`
- [x] `Archiver.App` references `Archiver.Core`
- [x] Solution contains both projects (`dotnet sln list` shows both)
- [x] `Archiver.Core.csproj` has `<Nullable>enable</Nullable>`

---

### T-02 — Create Folder Structure
- [x] **Status:** complete

**Acceptance criteria:**
- [x] `src/Archiver.Core/Interfaces/` exists
- [x] `src/Archiver.Core/Services/` exists
- [x] `src/Archiver.Core/Models/` exists
- [x] `src/Archiver.App/Views/` exists
- [x] `src/Archiver.App/ViewModels/` exists
- [x] `src/Archiver.App/Services/` exists

---

## Phase 2 — Core Models

### T-03 — Implement ArchiveOptions
- [x] **Status:** complete

---

### T-04 — Implement ExtractOptions
- [x] **Status:** complete

---

### T-05 — Implement ArchiveResult and ArchiveError
- [x] **Status:** complete

---

## Phase 3 — Core Interface and Service

### T-06 — Implement IArchiveService
- [x] **Status:** complete

---

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

---

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

**ZIP-compatible formats that must now work:**

| Extension | Format |
|-----------|--------|
| `.zip` | Standard ZIP |
| `.jar` | Java Archive |
| `.apk` | Android Package |
| `.docx` | Word Document |
| `.xlsx` | Excel Spreadsheet |
| `.pptx` | PowerPoint |
| `.epub` | eBook |
| `.odt` | OpenDocument Text |
| `.war` | Java Web Archive |
| `.nupkg` | NuGet Package |

**Implementation:**
```csharp
private static bool IsZipFile(string path)
{
    try
    {
        Span<byte> header = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        fs.ReadExactly(header);
        return header[0] == 0x50 && header[1] == 0x4B
            && header[2] == 0x03 && header[3] == 0x04;
    }
    catch
    {
        return false;
    }
}
```

**Decision logic:**
```
IsZipFile() == false
    → skip silently (not a ZIP, not an error)
IsZipFile() == true → try ZipFile.OpenRead()
    ├── success              → extract normally
    └── InvalidDataException → ArchiveError:
        "File has ZIP signature but appears corrupted or incomplete."
```

**Acceptance criteria:**
- [x] `IsZipFile()` private method uses magic bytes `50 4B 03 04`
- [x] Extension check removed entirely
- [x] `.jar`, `.docx`, `.xlsx`, `.apk` with valid ZIP content extracted successfully
- [x] File with `.zip` extension but wrong magic bytes → skipped silently
- [x] File with ZIP magic bytes but corrupted → `ArchiveError` with message "File has ZIP signature but appears corrupted or incomplete."
- [x] `dotnet test` passes — existing tests unchanged
- [x] New test cases:
  - [x] `.jar` with valid ZIP content → extracted successfully
  - [x] File with `.zip` extension but not ZIP magic bytes → skipped silently
  - [x] File with ZIP magic bytes but corrupted content → `ArchiveError`

---

### T-13.2 — Inform User About Skipped Non-ZIP Files
- [ ] **Status:** pending

**Files:**
- `src/Archiver.Core/Models/ArchiveResult.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`
- `src/Archiver.App/Services/IDialogService.cs`
- `src/Archiver.App/Services/DialogService.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Currently non-ZIP files are skipped silently — user has no idea why.
Add `SkippedFiles` collection to `ArchiveResult` and show skipped files
in the summary dialog (T-19) as a separate section distinct from errors.

Known non-ZIP formats to detect by magic bytes and report with friendly name:

| Magic bytes | Format | Friendly name |
|-------------|--------|---------------|
| `52 61 72 21` | RAR | RAR archive |
| `37 7A BC AF 27 1C` | 7-Zip | 7-Zip archive |
| `1F 8B` | GZIP / TAR.GZ | GZip archive |
| `42 5A 68` | BZIP2 / TAR.BZ2 | BZip2 archive |
| `FD 37 7A 58 5A 00` | XZ / TAR.XZ | XZ archive |
| `04 22 4D 18` | LZ4 | LZ4 archive |

Files with unknown magic bytes → skipped silently, no message (could be any binary).

**Change to ArchiveResult:**
```csharp
public sealed record ArchiveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> CreatedFiles { get; init; } = [];
    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
    public IReadOnlyList<SkippedFile> SkippedFiles { get; init; } = [];  // NEW
}

public sealed record SkippedFile
{
    public string Path { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
```

**Decision logic in ExtractAsync:**
```
IsZipFile() == false
    ├── IsKnownArchiveFormat() == true
    │   → add to SkippedFiles with reason:
    │     "RAR format is not supported. Only ZIP-based formats are supported."
    └── IsKnownArchiveFormat() == false
        → skip silently, no record kept
```

**Acceptance criteria:**
- [ ] `SkippedFile` sealed record added to `src/Archiver.Core/Models/`
- [ ] `ArchiveResult.SkippedFiles` is `IReadOnlyList<SkippedFile>`, defaults to `[]`
- [ ] `IsKnownArchiveFormat()` private method in `ZipArchiveService` checks magic bytes for RAR, 7z, GZip, BZip2, XZ, LZ4
- [ ] Known non-ZIP archives added to `SkippedFiles` with friendly reason message
- [ ] Unknown binary files skipped silently — not added to `SkippedFiles`
- [ ] `ArchiveResult.Success` remains `true` when only skips occurred, no errors
- [ ] `dotnet test` passes — existing tests unchanged
- [ ] New test cases:
  - [ ] RAR file → appears in `SkippedFiles` with friendly reason
  - [ ] Random binary file → not in `SkippedFiles`, not in `Errors`

---

### T-14 — Smart Extract Folder Logic
- [x] **Status:** complete

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Automatically decide whether to wrap extracted files in a subfolder.

Rules:
- Single root folder in archive → extract contents directly, no double-nesting
- Multiple items at root → create subfolder named after the archive
- Single file at root → extract directly, no subfolder

**Acceptance criteria:**
- [x] Single root folder → no double-nesting
- [x] Multiple root items → subfolder created named after archive
- [x] Single root file → extracted directly
- [x] Existing tests still pass
- [x] New test cases for each scenario

---

### T-15 — Add Files and Add Folder Buttons
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.App/Services/IDialogService.cs`
- `src/Archiver.App/Services/DialogService.cs`

**What:** Two explicit buttons below the drop zone.

```
[ Add files ]  [ Add folder ]
```

**Acceptance criteria:**
- [ ] "Add files" opens `FileOpenPicker` — multi-select, all file types
- [ ] "Add folder" opens `FolderPicker` — single folder selection
- [ ] Both add to `SelectedPaths` without duplicates
- [ ] Double-click on drop zone still triggers files picker
- [ ] Hint text: "Drop files or folders here, or double-click to browse files"

---

### T-16 — Destination Path Row
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Destination path row above Archive/Extract buttons.

```
Destination:  [C:\Users\Pa\Downloads\          ] [...]
```

**Acceptance criteria:**
- [ ] `DestinationPath` observable `string` in ViewModel
- [ ] Default = folder of first item in `SelectedPaths` when list changes
- [ ] If `SelectedPaths` empty → Desktop (`Environment.GetFolderPath(Environment.SpecialFolder.Desktop)`)
- [ ] `...` button opens `FolderPicker` and updates `DestinationPath`
- [ ] `DestinationPath` passed to `ArchiveOptions.DestinationFolder` and `ExtractOptions.DestinationFolder`
- [ ] Read-only `TextBox` — editable only via picker button

---

### T-17 — Remove Item from List (Right-click)
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Right-click on list item → context menu → "Remove".

**Acceptance criteria:**
- [x] Right-click shows `MenuFlyout` with "Remove" item
- [x] Clicking "Remove" calls `ViewModel.RemovePath(path)`
- [x] `RemovePath(string path)` added to `MainViewModel`
- [x] No business logic in code-behind

---

### T-18 — Post-Action Checkboxes — UI and Logic
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Post-action options below Archive/Extract buttons — both UI checkboxes
AND actual implementation in `ZipArchiveService`.

UI layout:
```
[ ] Open destination folder after completion
[ ] Delete source files after archiving
[ ] Delete archive after extraction
```

**Logic in ZipArchiveService:**

`OpenDestinationFolder` — after successful operation:
```csharp
if (options.OpenDestinationFolder && errors.Count == 0)
    Process.Start("explorer.exe", options.DestinationFolder);
```

`DeleteSourceFiles` — after successful archive creation, delete each source path:
```csharp
if (options.DeleteSourceFiles && result.Success)
{
    foreach (var path in options.SourcePaths)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
```

`DeleteArchiveAfterExtraction` — after successful extraction, delete each archive:
```csharp
if (options.DeleteArchiveAfterExtraction && result.Success)
{
    foreach (var path in options.ArchivePaths)
        if (File.Exists(path)) File.Delete(path);
}
```

**Important:** Delete operations run only when `result.Success == true` (no errors).
Delete failures are caught silently — do not affect `ArchiveResult`.

**Acceptance criteria:**
- [x] `OpenDestinationFolder` observable `bool` in ViewModel, default `false`
- [x] `DeleteSourceFiles` observable `bool` in ViewModel, default `false`
- [x] `DeleteArchiveAfterExtraction` observable `bool` in ViewModel, default `false`
- [x] All three values passed to `ArchiveOptions` and `ExtractOptions`
- [x] All three checkboxes visible in UI
- [x] `ZipArchiveService.ArchiveAsync` opens Explorer if `OpenDestinationFolder` and no errors
- [x] `ZipArchiveService.ArchiveAsync` deletes source paths if `DeleteSourceFiles` and `Success`
- [x] `ZipArchiveService.ExtractAsync` deletes archive files if `DeleteArchiveAfterExtraction` and `Success`
- [x] Delete failures caught silently — do not throw, do not add to `Errors`
- [x] `dotnet test` passes — add test for DeleteSourceFiles and DeleteArchiveAfterExtraction

---

### T-19 — Error Summary Dialog
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/Services/IDialogService.cs`
- `src/Archiver.App/Services/DialogService.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** After each operation show a summary dialog only if there were errors.
No dialog on success — only `StatusMessage` update.
On errors — formatted list of what failed and why.

**UI appearance:**
```
┌─────────────────────────────────────┐
│  Completed with issues              │
│─────────────────────────────────────│
│  ✗ Errors (2)                       │
│                                     │
│    document.pdf                     │
│    File is locked by another process│
│                                     │
│    archive.zip                      │
│    File has ZIP signature but       │
│    appears corrupted or incomplete  │
│─────────────────────────────────────│
│  ⊘ Skipped — unsupported format (2) │
│                                     │
│    backup.rar                       │
│    RAR format is not supported      │
│                                     │
│    archive.7z                       │
│    7-Zip format is not supported    │
│─────────────────────────────────────│
│                   [  OK  ]          │
└─────────────────────────────────────┘
```

Each section shown only if it has items. No dialog at all if both lists are empty.

**Interface addition:**
```csharp
// Add to IDialogService:
Task ShowOperationSummaryAsync(string operationName, ArchiveResult result);
```

**Implementation in DialogService:**
```csharp
public async Task ShowOperationSummaryAsync(string operationName, ArchiveResult result)
{
    // Show only if result.Errors.Count > 0 OR result.SkippedFiles.Count > 0
    // Build StackPanel:
    //   - Errors section (if any): header + per-error filename bold + message
    //   - Skipped section (if any): header + per-skip filename bold + reason
    // Show in ContentDialog with ScrollViewer
    // Title: $"{operationName} completed with issues"
}
```

**ViewModel logic:**
```csharp
// After ArchiveAsync or ExtractAsync:
if (result.Errors.Count > 0 || result.SkippedFiles.Count > 0)
    await _dialogService.ShowOperationSummaryAsync("Archive", result);
else
    StatusMessage = $"Done — {result.CreatedFiles.Count} file(s) processed.";
```

**Acceptance criteria:**
- [ ] `ShowOperationSummaryAsync(string operationName, ArchiveResult result)` added to `IDialogService`
- [ ] `DialogService` implements using `ContentDialog` with `ScrollViewer` + `StackPanel`
- [ ] Errors section shown only if `result.Errors.Count > 0` — filename bold + message
- [ ] Skipped section shown only if `result.SkippedFiles.Count > 0` — filename bold + reason
- [ ] Dialog NOT shown when both lists are empty
- [ ] On full success: only `StatusMessage` updated, no dialog
- [ ] `MainViewModel.ArchiveAsync` calls `ShowOperationSummaryAsync` when errors or skips exist
- [ ] `MainViewModel.ExtractAsync` calls `ShowOperationSummaryAsync` when errors or skips exist
- [ ] `Archiver.Core` has zero references to dialog — all results via `ArchiveResult` only

---

### T-20 — Archive Name Field
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Text field for custom archive name, visible only in archive mode.
If left empty — auto-name from first item in `SelectedPaths`.

UI layout (above Archive/Extract buttons):
```
Archive name:  [my-backup                    ]  (leave empty for auto)
```

**Acceptance criteria:**
- [ ] `ArchiveName` observable `string?` in ViewModel, default `null`
- [ ] TextBox placeholder text: "Auto (based on first file/folder name)"
- [ ] If `ArchiveName` is empty/whitespace → pass `null` to `ArchiveOptions.ArchiveName`
- [ ] `ArchiveOptions.ArchiveName` is used by `ZipArchiveService` when not null
- [ ] Field is only meaningful for `ArchiveMode.SingleArchive` — visible always but noted in hint

---

### T-21 — File List Table with Columns
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.App/Models/FileItem.cs`

**What:** Replace plain `ListView` with a table showing useful metadata per item.

Columns:
| Column | Source | Notes |
|--------|--------|-------|
| Name | `Path.GetFileName()` | |
| Type | `Directory` / file extension | "Folder", "ZIP", "JAR", "PDF"... |
| Size | `FileInfo.Length` / dir recursive | Async, shows "..." while loading |
| Modified | `FileInfo.LastWriteTime` | `yyyy-MM-dd HH:mm` |
| Full path | full path string | Shown as tooltip on Name column |

**Implementation notes:**
- Add `FileItem` model in `Archiver.App/Models/FileItem.cs` with all columns as properties
- Size calculation for folders must be async — run in background, update when done
- `SelectedPaths` in ViewModel becomes `ObservableCollection<FileItem>`
- `AddPaths()` creates `FileItem` per path, triggers async size calculation

**Acceptance criteria:**
- [ ] `FileItem` model with `Name`, `Type`, `Size`, `SizeBytes`, `Modified`, `FullPath` properties
- [ ] Table shows Name, Type, Size, Modified columns
- [ ] Full path shown as tooltip on Name cell
- [ ] Folder size calculated asynchronously — shows "..." until done
- [ ] Size formatted human-readable: "1.2 MB", "345 KB", "12 bytes"
- [ ] Type shows "Folder" for directories, uppercase extension without dot for files ("ZIP", "PDF")
- [ ] Sorting by any column supported
- [ ] Right-click Remove still works (T-17)
- [ ] Duplicate paths still prevented

---

### T-22 — Archive Mode Toggle (Single / Separate)
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Let user choose between SingleArchive and SeparateArchives mode.

UI layout (near Archive button):
```
Mode: ( ) One archive   (•) Separate archives
```

**Acceptance criteria:**
- [ ] `ArchiveMode` observable property in ViewModel, default `ArchiveMode.SingleArchive`
- [ ] Two `RadioButton` controls bound to ViewModel
- [ ] Archive name field (T-20) disabled/grayed when mode is `SeparateArchives`
- [ ] Selected mode passed to `ArchiveOptions.Mode`
- [ ] Mode selection persists while app is open (not across restarts)

---

### T-23 — Conflict Behavior Dropdown
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Let user choose what happens when output file already exists.

UI layout (below destination row):
```
If file exists: [ Overwrite ▼ ]
                  Ask
                  Overwrite
                  Skip
                  Rename (add number)
```

**Acceptance criteria:**
- [ ] `OnConflict` observable `ConflictBehavior` in ViewModel, default `ConflictBehavior.Ask`
- [ ] `ComboBox` with all four options bound to ViewModel
- [ ] Selected value passed to both `ArchiveOptions.OnConflict` and `ExtractOptions.OnConflict`
- [ ] `ZipArchiveService` handles `Skip` and `Rename` correctly (currently only Overwrite works)
- [ ] `Ask` behavior deferred to T-Future — for now treated as `Skip` with a note in code

---

### T-24 — Compression Level Selector
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`
- `src/Archiver.Core/Models/ArchiveOptions.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Let user choose compression level. Maps to `CompressionLevel` enum in `System.IO.Compression`.

UI layout (near Archive button, only relevant for archiving):
```
Compression: [ Normal ▼ ]
               Fast (Fastest)
               Normal (Optimal)
               Best (SmallestSize)
               None (NoCompression)
```

**Acceptance criteria:**
- [ ] `CompressionLevel` property added to `ArchiveOptions` with default `CompressionLevel.Optimal`
- [ ] `ComboBox` in UI with four options bound to ViewModel
- [ ] Selected level passed to `ArchiveOptions.CompressionLevel`
- [ ] `ZipArchiveService` uses `options.CompressionLevel` when creating entries
- [ ] UI shows compression selector only when operation is archiving (always visible is also acceptable for v1.0)

---

## Phase 6 — Packaging

### T-11 — MSIX Packaging Setup
- [ ] **Status:** pending

**Acceptance criteria:**
- [ ] App builds as MSIX package
- [ ] `Package.appxmanifest` has correct `Identity`, `DisplayName`, `Description`
- [ ] Package runs on Windows 10 version 1903+ (build 18362)
- [ ] No capability declarations beyond `runFullTrust`

---

## Phase 7 — Future (post v1.0)

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** future

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

### T-F03 — Dedicated Extract Window
- [ ] **Status:** future

### T-F04 — TAR Support
- [ ] **Status:** future

Via Windows built-in `tar.exe` using `System.Diagnostics.Process`.
No third-party libraries.

---

### T-F05 — Archive Contents Preview
- [ ] **Status:** future

Click on a ZIP file in the list → see contents without extracting.
Read-only tree view of entries inside the archive using `ZipFile.OpenRead`.
No extraction, no temp files..
