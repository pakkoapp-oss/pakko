# TASKS.md — Implementation Tasks

Each task has: scope, files to create/modify, and **acceptance criteria**.  
Agent marks task `[x]` only when ALL criteria pass.

---

## Phase 1 — Project Setup

### T-01 — Create Solution and Projects
- [~] **Status:** partially complete (Archiver.App to be created manually in Visual Studio)

**Files to create:**
- `windows-archiver-wrapper.sln`
- `src/Archiver.Core/Archiver.Core.csproj`
- `src/Archiver.App/Archiver.App.csproj`

**Acceptance criteria:**
- [x] `dotnet build src/Archiver.Core` runs without errors
- [x] `Archiver.Core` targets `net8.0`
- [ ] `Archiver.App` references `Archiver.Core` ← to be done after VS creates the project
- [ ] Solution contains both projects (`dotnet sln list` shows both) ← pending Archiver.App
- [x] `Archiver.Core.csproj` has `<Nullable>enable</Nullable>`

---

### T-02 — Create Folder Structure
- [x] **Status:** complete

**Files to create:** empty `.gitkeep` or placeholder files in each folder.

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
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Models/ArchiveOptions.cs`

**Acceptance criteria:**
- [ ] `record` type (not class)
- [ ] All properties use `init;` setters
- [ ] `SourcePaths` is `IReadOnlyList<string>`, defaults to `[]`
- [ ] `DestinationFolder` defaults to `string.Empty`
- [ ] `ArchiveName` is `string?` (nullable)
- [ ] `Mode` is `ArchiveMode` enum, defaults to `ArchiveMode.SingleArchive`
- [ ] `OnConflict` is `ConflictBehavior` enum, defaults to `ConflictBehavior.Ask`
- [ ] `OpenDestinationFolder` defaults to `false`
- [ ] `DeleteSourceFiles` defaults to `false`
- [ ] Enums `ArchiveMode` and `ConflictBehavior` are defined in same namespace

---

### T-04 — Implement ExtractOptions
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Models/ExtractOptions.cs`

**Acceptance criteria:**
- [ ] `record` type with `init;` setters
- [ ] `ArchivePaths` is `IReadOnlyList<string>`, defaults to `[]`
- [ ] `DestinationFolder` defaults to `string.Empty`
- [ ] `Mode` is `ExtractMode` enum, defaults to `ExtractMode.SeparateFolders`
- [ ] `OnConflict` is `ConflictBehavior` (reuse from T-03)
- [ ] `OpenDestinationFolder` defaults to `false`
- [ ] `DeleteArchiveAfterExtraction` defaults to `false`
- [ ] Enum `ExtractMode` defined in same namespace

---

### T-05 — Implement ArchiveResult and ArchiveError
- [ ] **Status:** pending

**Files:**
- `src/Archiver.Core/Models/ArchiveResult.cs`
- `src/Archiver.Core/Models/ArchiveError.cs`

**Acceptance criteria:**
- [ ] Both are `sealed record` types
- [ ] `ArchiveResult.Success` is `bool`
- [ ] `ArchiveResult.CreatedFiles` is `IReadOnlyList<string>`, defaults to `[]`
- [ ] `ArchiveResult.Errors` is `IReadOnlyList<ArchiveError>`, defaults to `[]`
- [ ] `ArchiveError.SourcePath` is `string`, defaults to `string.Empty`
- [ ] `ArchiveError.Message` is `string`, defaults to `string.Empty`
- [ ] `ArchiveError.Exception` is `Exception?` (nullable)

---

## Phase 3 — Core Interface and Service

### T-06 — Implement IArchiveService
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Interfaces/IArchiveService.cs`

**Acceptance criteria:**
- [ ] Interface is `public`
- [ ] `ArchiveAsync(ArchiveOptions, IProgress<int>?, CancellationToken)` method exists
- [ ] `ExtractAsync(ExtractOptions, IProgress<int>?, CancellationToken)` method exists
- [ ] Both return `Task<ArchiveResult>`
- [ ] `IProgress<int>?` and `CancellationToken` have default values (`null` and `default`)
- [ ] No implementation logic in the interface file
- [ ] XML doc comments on both methods

---

### T-07 — Implement ZipArchiveService
- [ ] **Status:** pending

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] Class implements `IArchiveService`
- [ ] Class is `sealed`
- [ ] Uses only `System.IO.Compression` — no third-party packages
- [ ] `ArchiveAsync` handles both `ArchiveMode.SingleArchive` and `ArchiveMode.SeparateArchives`
- [ ] `ExtractAsync` handles both `ExtractMode.SeparateFolders` and `ExtractMode.SingleFolder`
- [ ] All `IOException` and `UnauthorizedAccessException` are caught per-item, appended to errors list, processing continues
- [ ] `CancellationToken` is checked between processing items
- [ ] `IProgress<int>` reports 0–100 as percentage of items processed
- [ ] Method never throws — all exceptions result in `ArchiveError` entries
- [ ] `ArchiveResult.Success` is `true` only when `Errors` list is empty

---

## Phase 4 — UI Layer

### T-08 — Implement MainViewModel
- [ ] **Status:** pending

**File:** `src/Archiver.App/ViewModels/MainViewModel.cs`

**Dependencies:** `CommunityToolkit.Mvvm`

**Acceptance criteria:**
- [ ] Inherits `ObservableObject` from CommunityToolkit
- [ ] `SelectedPaths` is `ObservableCollection<string>`
- [ ] `IsBusy` is observable `bool` property
- [ ] `Progress` is observable `int` property (0–100)
- [ ] `StatusMessage` is observable `string` property
- [ ] `ArchiveCommand` is `AsyncRelayCommand`, disabled when `IsBusy` or no paths selected
- [ ] `ExtractCommand` is `AsyncRelayCommand`, disabled when `IsBusy` or no paths selected
- [ ] Commands call `IArchiveService` (injected via constructor)
- [ ] `IsBusy` set to `true` during operation, `false` in `finally` block
- [ ] `StatusMessage` updated on completion and on error

---

### T-09 — Implement MainWindow
- [ ] **Status:** pending

**Files:**
- `src/Archiver.App/Views/MainWindow.xaml`
- `src/Archiver.App/Views/MainWindow.xaml.cs`

**Acceptance criteria:**
- [ ] Window has a drag-and-drop area (`AllowDrop="True"`)
- [ ] `ListView` or `ItemsControl` bound to `ViewModel.SelectedPaths`
- [ ] "Archive" button bound to `ViewModel.ArchiveCommand`
- [ ] "Extract" button bound to `ViewModel.ExtractCommand`
- [ ] Buttons disabled when `IsBusy` is `true`
- [ ] `ProgressBar` bound to `ViewModel.Progress`
- [ ] Status label bound to `ViewModel.StatusMessage`
- [ ] No business logic in code-behind — only UI wiring
- [ ] Drag-and-drop handler calls `ViewModel` method, not service directly

---

## Phase 5 — Error Handling UI

### T-10 — DialogService for Error Display
- [ ] **Status:** pending

**File:** `src/Archiver.App/Services/DialogService.cs`

**Acceptance criteria:**
- [ ] `ShowErrorAsync(string title, string message)` method
- [ ] Uses WinUI `ContentDialog`
- [ ] Called from ViewModel (not from code-behind)
- [ ] Interface `IDialogService` defined in `Archiver.App.Services` namespace
- [ ] `MainViewModel` depends on `IDialogService`, not concrete class

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
