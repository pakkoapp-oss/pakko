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
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Right-click on list item → context menu → "Remove".

**Acceptance criteria:**
- [ ] Right-click shows `MenuFlyout` with "Remove" item
- [ ] Clicking "Remove" calls `ViewModel.RemovePath(path)`
- [ ] `RemovePath(string path)` added to `MainViewModel`
- [ ] No business logic in code-behind

---

### T-18 — Post-Action Checkboxes in UI
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Post-action options below Archive/Extract buttons.

```
[ ] Open destination folder after completion
[ ] Delete source files after archiving
[ ] Delete archive after extraction
```

**Acceptance criteria:**
- [ ] `OpenDestinationFolder` observable `bool`, default `false`
- [ ] `DeleteSourceFiles` observable `bool`, default `false`
- [ ] `DeleteArchiveAfterExtraction` observable `bool`, default `false`
- [ ] All values passed to `ArchiveOptions` and `ExtractOptions`
- [ ] All three checkboxes always visible

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
│  Completed with 2 error(s)          │
│─────────────────────────────────────│
│  ✗ document.pdf                     │
│    File is locked by another process│
│                                     │
│  ✗ archive.zip                      │
│    File has ZIP signature but       │
│    appears corrupted or incomplete  │
│─────────────────────────────────────│
│                   [  OK  ]          │
└─────────────────────────────────────┘
```

**Interface addition:**
```csharp
// Add to IDialogService:
Task ShowErrorSummaryAsync(string operationName, IReadOnlyList<ArchiveError> errors);
```

**Implementation in DialogService:**
```csharp
public async Task ShowErrorSummaryAsync(string operationName, IReadOnlyList<ArchiveError> errors)
{
    // Build formatted error list as StackPanel with TextBlocks
    // Show in ContentDialog with scrollable content
    // Title: $"{operationName} completed with {errors.Count} error(s)"
}
```

**ViewModel logic:**
```csharp
// After ArchiveAsync or ExtractAsync:
if (result.Errors.Count > 0)
    await _dialogService.ShowErrorSummaryAsync("Archive", result.Errors);
else
    StatusMessage = $"Done — {result.CreatedFiles.Count} file(s) processed.";
```

**Acceptance criteria:**
- [ ] `ShowErrorSummaryAsync(string operationName, IReadOnlyList<ArchiveError> errors)` added to `IDialogService`
- [ ] `DialogService` implements it using `ContentDialog` with scrollable `StackPanel`
- [ ] Each error shows: filename (bold) + error message below it
- [ ] Dialog title shows operation name and error count
- [ ] Dialog NOT shown when `result.Errors` is empty
- [ ] On success: only `StatusMessage` updated, no dialog
- [ ] `MainViewModel.ArchiveAsync` calls `ShowErrorSummaryAsync` when errors exist
- [ ] `MainViewModel.ExtractAsync` calls `ShowErrorSummaryAsync` when errors exist
- [ ] `Archiver.Core` has zero references to dialog — errors returned via `ArchiveResult` only

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
