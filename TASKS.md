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

**File:** `src/Archiver.Core/Models/ArchiveOptions.cs`

**Acceptance criteria:**
- [x] `record` type (not class)
- [x] All properties use `init;` setters
- [x] `SourcePaths` is `IReadOnlyList<string>`, defaults to `[]`
- [x] `DestinationFolder` defaults to `string.Empty`
- [x] `ArchiveName` is `string?` (nullable)
- [x] `Mode` is `ArchiveMode` enum, defaults to `ArchiveMode.SingleArchive`
- [x] `OnConflict` is `ConflictBehavior` enum, defaults to `ConflictBehavior.Ask`
- [x] `OpenDestinationFolder` defaults to `false`
- [x] `DeleteSourceFiles` defaults to `false`
- [x] Enums `ArchiveMode` and `ConflictBehavior` are defined in same namespace

---

### T-04 — Implement ExtractOptions
- [x] **Status:** complete

**File:** `src/Archiver.Core/Models/ExtractOptions.cs`

**Acceptance criteria:**
- [x] `record` type with `init;` setters
- [x] `ArchivePaths` is `IReadOnlyList<string>`, defaults to `[]`
- [x] `DestinationFolder` defaults to `string.Empty`
- [x] `Mode` is `ExtractMode` enum, defaults to `ExtractMode.SeparateFolders`
- [x] `OnConflict` is `ConflictBehavior` (reuse from T-03)
- [x] `OpenDestinationFolder` defaults to `false`
- [x] `DeleteArchiveAfterExtraction` defaults to `false`
- [x] Enum `ExtractMode` defined in same namespace

---

### T-05 — Implement ArchiveResult and ArchiveError
- [x] **Status:** complete

**Files:**
- `src/Archiver.Core/Models/ArchiveResult.cs`
- `src/Archiver.Core/Models/ArchiveError.cs`

**Acceptance criteria:**
- [x] Both are `sealed record` types
- [x] `ArchiveResult.Success` is `bool`
- [x] `ArchiveResult.CreatedFiles` is `IReadOnlyList<string>`, defaults to `[]`
- [x] `ArchiveResult.Errors` is `IReadOnlyList<ArchiveError>`, defaults to `[]`
- [x] `ArchiveError.SourcePath` is `string`, defaults to `string.Empty`
- [x] `ArchiveError.Message` is `string`, defaults to `string.Empty`
- [x] `ArchiveError.Exception` is `Exception?` (nullable)

---

## Phase 3 — Core Interface and Service

### T-06 — Implement IArchiveService
- [x] **Status:** complete

**File:** `src/Archiver.Core/Interfaces/IArchiveService.cs`

**Acceptance criteria:**
- [x] Interface is `public`
- [x] `ArchiveAsync(ArchiveOptions, IProgress<int>?, CancellationToken)` method exists
- [x] `ExtractAsync(ExtractOptions, IProgress<int>?, CancellationToken)` method exists
- [x] Both return `Task<ArchiveResult>`
- [x] `IProgress<int>?` and `CancellationToken` have default values (`null` and `default`)
- [x] No implementation logic in the interface file
- [x] XML doc comments on both methods

---

### T-07 — Implement ZipArchiveService
- [x] **Status:** complete

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [x] Class implements `IArchiveService`
- [x] Class is `sealed`
- [x] Uses only `System.IO.Compression` — no third-party packages
- [x] `ArchiveAsync` handles both `ArchiveMode.SingleArchive` and `ArchiveMode.SeparateArchives`
- [x] `ExtractAsync` handles both `ExtractMode.SeparateFolders` and `ExtractMode.SingleFolder`
- [x] All `IOException` and `UnauthorizedAccessException` are caught per-item, appended to errors list, processing continues
- [x] `CancellationToken` is checked between processing items
- [x] `IProgress<int>` reports 0–100 as percentage of items processed
- [x] Method never throws — all exceptions result in `ArchiveError` entries
- [x] `ArchiveResult.Success` is `true` only when `Errors` list is empty

---

## Phase 3b — Tests

### T-12 — Implement Test Project
- [x] **Status:** complete

**Files:**
- `tests/Archiver.Core.Tests/Helpers/TempDirectory.cs`
- `tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`
- `tests/Archiver.Core.Tests/Services/ZipArchiveServiceExtractTests.cs`
- `tests/Archiver.Core.Tests/Models/ArchiveOptionsTests.cs`

**Acceptance criteria:**
- [x] `dotnet test` passes with zero failures
- [x] All 10 test cases implemented
- [x] No tests use `Thread.Sleep` — use `await Task.Delay` if needed
- [x] Each test cleans up temp files via `TempDirectory.Dispose()`
- [x] No test depends on another test's state

---

## Phase 4 — UI Layer

### T-08 — Implement MainViewModel
- [x] **Status:** complete

**File:** `src/Archiver.App/ViewModels/MainViewModel.cs`

**Acceptance criteria:**
- [x] Inherits `ObservableObject` from CommunityToolkit
- [x] `SelectedPaths` is `ObservableCollection<string>`
- [x] `IsBusy` is observable `bool` property
- [x] `Progress` is observable `int` property (0–100)
- [x] `StatusMessage` is observable `string` property
- [x] `ArchiveCommand` is `AsyncRelayCommand`, disabled when `IsBusy` or no paths selected
- [x] `ExtractCommand` is `AsyncRelayCommand`, disabled when `IsBusy` or no paths selected
- [x] Commands call `IArchiveService` (injected via constructor)
- [x] `IsBusy` set to `true` during operation, `false` in `finally` block
- [x] `StatusMessage` updated on completion and on error

---

### T-09 — Implement MainWindow
- [x] **Status:** complete

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`

**Acceptance criteria:**
- [x] Window has a drag-and-drop area (`AllowDrop="True"`)
- [x] `ListView` bound to `ViewModel.SelectedPaths`
- [x] "Archive" button bound to `ViewModel.ArchiveCommand`
- [x] "Extract" button bound to `ViewModel.ExtractCommand`
- [x] `ProgressBar` bound to `ViewModel.Progress`
- [x] Status label bound to `ViewModel.StatusMessage`
- [x] No business logic in code-behind — only UI wiring
- [x] Drag-and-drop handler calls `ViewModel` method, not service directly

---

## Phase 5 — Error Handling UI

### T-10 — DialogService for Error Display
- [x] **Status:** complete

**File:** `src/Archiver.App/Services/DialogService.cs`

**Acceptance criteria:**
- [x] `ShowErrorAsync(string title, string message)` method
- [x] Uses WinUI `ContentDialog`
- [x] Called from ViewModel (not from code-behind)
- [x] Interface `IDialogService` defined in `Archiver.App.Services` namespace
- [x] `MainViewModel` depends on `IDialogService`, not concrete class

---

## Phase 5b — UX Improvements

### T-13 — Smart Extraction (skip non-archives)
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Before extracting each file check if it is a valid ZIP.
Non-ZIP files are silently skipped — no error, no crash.

**Acceptance criteria:**
- [ ] Files without `.zip` extension are skipped silently
- [ ] Files with `.zip` extension but invalid content return `ArchiveError` with friendly message
- [ ] Skipped files are NOT added to `ArchiveResult.Errors`
- [ ] Skipped files are NOT added to `ArchiveResult.CreatedFiles`
- [ ] `dotnet test` still passes after change

---

### T-14 — Smart Extract Folder Logic
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**What:** Automatically decide whether to wrap extracted files in a subfolder.

Rules:
- Archive contains a **single root folder** → extract its contents directly, no double-nesting
- Archive contains **multiple items at root** → create subfolder named after the archive
- Archive contains a **single file at root** → extract directly, no subfolder

**Acceptance criteria:**
- [ ] Single root folder → no double-nesting (`archive/folder/files` becomes `folder/files`)
- [ ] Multiple root items → subfolder created (`archive.zip` → `archive/` containing all items)
- [ ] Single root file → extracted directly to destination, no subfolder
- [ ] Existing tests still pass
- [ ] New test cases added for each smart extraction scenario

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

UI layout below drop zone:
```
[ Add files ]  [ Add folder ]
```

**Acceptance criteria:**
- [ ] "Add files" opens `FileOpenPicker` — multi-select, all file types
- [ ] "Add folder" opens `FolderPicker` — single folder selection
- [ ] Both add to `SelectedPaths` without duplicates
- [ ] Double-click on drop zone still triggers files picker
- [ ] Hint text in drop zone: "Drop files or folders here, or double-click to browse files"

---

### T-16 — Destination Path Row
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Destination path row above Archive/Extract buttons.
Default = folder of first item in `SelectedPaths`.

UI layout:
```
Destination:  [C:\Users\Pa\Downloads\          ] [...]
```

**Acceptance criteria:**
- [ ] `DestinationPath` is observable `string` property in `MainViewModel`
- [ ] Default = folder of first item in `SelectedPaths` when list changes
- [ ] If `SelectedPaths` is empty → default to user Desktop (`Environment.GetFolderPath(Environment.SpecialFolder.Desktop)`)
- [ ] `...` button opens `FolderPicker` and updates `DestinationPath`
- [ ] `DestinationPath` passed to `ArchiveOptions.DestinationFolder` and `ExtractOptions.DestinationFolder`
- [ ] Path displayed in read-only `TextBox` — editable only via picker button

---

### T-17 — Remove Item from List (Right-click)
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/MainWindow.xaml.cs`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Right-click on list item → context menu → "Remove".

**Acceptance criteria:**
- [ ] Right-click shows `MenuFlyout` with single item "Remove"
- [ ] Clicking "Remove" removes that path from `SelectedPaths`
- [ ] `RemovePath(string path)` method added to `MainViewModel`
- [ ] No business logic in code-behind — handler passes path to ViewModel only

---

### T-18 — Post-Action Checkboxes in UI
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/MainWindow.xaml`
- `src/Archiver.App/ViewModels/MainViewModel.cs`

**What:** Post-action options below Archive/Extract buttons.

UI layout:
```
[ ] Open destination folder after completion
[ ] Delete source files after archiving
[ ] Delete archive after extraction
```

**Acceptance criteria:**
- [ ] `OpenDestinationFolder` observable `bool` in ViewModel, default `false`
- [ ] `DeleteSourceFiles` observable `bool` in ViewModel, default `false`
- [ ] `DeleteArchiveAfterExtraction` observable `bool` in ViewModel, default `false`
- [ ] All three values passed correctly to `ArchiveOptions` and `ExtractOptions`
- [ ] All three checkboxes always visible (not conditional)

---

## Phase 6 — Packaging

### T-11 — MSIX Packaging Setup
- [ ] **Status:** pending

**Files:**
- `src/Archiver.Packaging/` project or `Package.appxmanifest` configuration

**Acceptance criteria:**
- [ ] App builds as MSIX package
- [ ] `Package.appxmanifest` has correct `Identity`, `DisplayName`, `Description`
- [ ] Package runs on Windows 10 version 1903+ (build 18362)
- [ ] No capability declarations beyond `runFullTrust`

---

## Phase 7 — Future (post v1.0)

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** future

Right-click in Explorer → "Archive with Archiver" / "Extract here"
Requires shell extension via sparse MSIX package.

---

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

Separate window for archive mode — opened when files passed via Explorer context menu.

---

### T-F03 — Dedicated Extract Window
- [ ] **Status:** future

Separate window for extract mode — opened when archives passed via Explorer context menu.

---

### T-F04 — TAR Support
- [ ] **Status:** future

Via Windows built-in `tar.exe` using `System.Diagnostics.Process`.
No third-party libraries.
