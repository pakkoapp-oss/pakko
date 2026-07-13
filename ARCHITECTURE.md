# ARCHITECTURE.md — Architecture and Layer Contracts

> **Current as of v1.0.** All signatures reflect actual implemented code.

---

## Layer Diagram

```
┌─────────────────────────────────────┐  ┌──────────────────────────────────────┐
│           Archiver.App              │  │         Archiver.Shell               │
│           (WinUI 3, net8.0-win)     │  │   (net8.0-windows, WinExe, no WinUI) │
│                                     │  │                                      │
│  MainWindow.xaml / .cs              │  │  Program.cs (entry point)            │
│  ViewModels/MainViewModel.cs        │  │  ShellArgumentParser.cs              │
│  Services/ (Dialog, Log)            │  │  NativeProgressDialog.cs (COM)       │
│  Strings/en-US/Resources.resw       │  │  Launches App via pakko:// URI       │
└──────────────┬──────────────────────┘  └───────────────┬──────────────────────┘
               │  project reference                       │  project reference
               └──────────────┬──────────────────────────┘
                               ▼
┌─────────────────────────────────────┐
│           Archiver.Core             │
│         (net8.0, no UI deps)        │
│                                     │
│  Interfaces/IArchiveService.cs      │
│  Services/ZipArchiveService.cs      │
│  Models/                            │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│       Windows / .NET APIs           │
│  System.IO.Compression              │
│  System.Diagnostics.Process         │
└─────────────────────────────────────┘
```

**Progress UI** — `Archiver.Shell` shows progress via the Windows Shell's built-in
`IProgressDialog` COM object (`NativeProgressDialog.cs`), in-process — no satellite process,
no IPC. An earlier design (`Archiver.ProgressWindow`, a second WinUI 3 `.exe` talking to
`Archiver.Shell` over a named pipe) was removed in T-F65 after its WinUI/WindowsAppRuntime
activation proved unreliable when spawned via `Process.Start`; see `DECISIONS.md`.

**Archiver.Package** — created and deleted during v1.2 development. The `.wapproj` approach
was abandoned due to PRI resource conflicts when packaging multiple WinUI 3 apps in one
package. Satellite EXE packaging is solved instead via `Content Include` items in
`Archiver.App.csproj` conditioned on `GenerateAppxPackageOnBuild=true`.

**Rule:** `Archiver.Core` must have **zero** references to WinUI, Microsoft.UI,
Windows.ApplicationModel.Resources, or any UI assembly.

---

## Folder Structure

```
src/
├── Archiver.Core/
│   ├── Interfaces/
│   │   └── IArchiveService.cs
│   ├── Services/
│   │   └── ZipArchiveService.cs
│   └── Models/
│       ├── ArchiveOptions.cs
│       ├── ExtractOptions.cs
│       ├── ArchiveResult.cs
│       ├── ArchiveError.cs
│       └── SkippedFile.cs
│
├── Archiver.App/              ← WinUI 3 main app; packages all three EXEs via MSIX
│   ├── MainWindow.xaml / .cs
│   ├── ViewModels/
│   │   └── MainViewModel.cs
│   ├── Services/
│   │   ├── IDialogService.cs
│   │   ├── DialogService.cs
│   │   ├── ILogService.cs
│   │   └── LogService.cs
│   ├── Models/
│   │   └── FileItem.cs
│   ├── Converters/
│   │   └── BoolToVisibilityConverter.cs
│   └── Strings/
│       └── en-US/
│           └── Resources.resw
│
├── Archiver.Shell/            ← shell extension entry point; net8.0-windows; WinExe; no WinUI
│   ├── Program.cs
│   ├── ShellArgumentParser.cs
│   └── NativeProgressDialog.cs   ← IProgressDialog COM interop (in-process progress UI)
│
└── Archiver.ShellExtension/   ← IExplorerCommand COM DLL (T-F61); C++/WRL, x64+ARM64, static CRT
    ├── dllmain.cpp                    ← DllGetClassObject, DllCanUnloadNow
    ├── ExplorerCommands.cpp/.h        ← PakkoRootCommand, SubCommandEnum, ExtractHereCommand,
    │                                    ExtractFolderCommand, ArchiveCommand
    └── ShellExtUtils.cpp/.h           ← COM-free logic, unit-tested via Archiver.ShellExtension.Tests
```

---

## Core Models — Current Signatures

```csharp
// Models/ArchiveOptions.cs
public sealed record ArchiveOptions
{
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public string? ArchiveName { get; init; }
    public ArchiveMode Mode { get; init; } = ArchiveMode.SingleArchive;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteSourceFiles { get; init; } = false;
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
}

public enum ArchiveMode { SingleArchive, SeparateArchives }

public enum ConflictBehavior { Overwrite, Skip, Rename }
// Note: ConflictBehavior.Ask was removed — default is Skip
```

```csharp
// Models/ExtractOptions.cs
public sealed record ExtractOptions
{
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public ExtractMode Mode { get; init; } = ExtractMode.SeparateFolders;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteArchiveAfterExtraction { get; init; } = false;

    // T-F94: invoked when an archive looks like a decompression bomb (declared uncompressed
    // size vs. compressed size exceeds the ratio threshold) AND the destination has enough free
    // space to hold it. True proceeds with extraction, false declines. Null (default) auto-
    // declines — the safe behavior for callers that don't wire a callback (Archiver.Shell).
    public Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction { get; init; }
}

public enum ExtractMode { SeparateFolders, SingleFolder }
```

```csharp
// Models/CompressionBombWarning.cs
public sealed record CompressionBombWarning
{
    public string ArchivePath { get; init; } = string.Empty;
    public long DeclaredUncompressedSize { get; init; }
    public long CompressedSize { get; init; }
    public long Ratio => CompressedSize > 0 ? DeclaredUncompressedSize / CompressedSize : 0;
}
```

```csharp
// Models/ArchiveResult.cs
public sealed record ArchiveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> CreatedFiles { get; init; } = [];
    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
    public IReadOnlyList<SkippedFile> SkippedFiles { get; init; } = [];
}
```

```csharp
// Models/ArchiveError.cs
public sealed record ArchiveError
{
    public string SourcePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
```

```csharp
// Models/SkippedFile.cs
public sealed record SkippedFile
{
    public string Path { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
```

---

## Interface — Current Signature

```csharp
// Interfaces/IArchiveService.cs
public interface IArchiveService
{
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    // T-F62: verifies every entry's CRC-32 against its declared header value without
    // writing anything to disk. Never throws — mismatches surface as ArchiveResult.Errors.
    Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    // T-F05: lists entries without extracting — flat, not hierarchical. Never throws — a
    // failure is reported via ArchiveListResult.Success/ErrorMessage.
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
```

`ProgressReport` carries `Percent`, `BytesTransferred`, `TotalBytes`, and `CurrentFile` (T-F16).

---

## App Services — Current Signatures

```csharp
// Services/IDialogService.cs
public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> PickDestinationFolderAsync();
    Task<IReadOnlyList<string>> PickFilesAsync();
    Task<IReadOnlyList<string>> PickFoldersAsync();
    Task ShowOperationSummaryAsync(string operationName, ArchiveResult result);
    Task ShowAboutAsync();

    // T-F94: called from a thread-pool thread by the extractors — implementation must marshal
    // onto the window's DispatcherQueue before showing a ContentDialog. See DECISIONS.md's
    // T-F94 entry.
    Task<bool> ShowCompressionBombConfirmAsync(CompressionBombWarning warning);

    // T-F05: single-entry Info dialog for the archive browser (ArchiveEntryViewModel lives in
    // Archiver.App.Core).
    Task ShowEntryInfoAsync(ArchiveEntryViewModel entry);
}
```

```csharp
// Services/ILogService.cs
public interface ILogService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
// Log path: %LocalAppData%\Pakko\logs\pakko.log
// Rotation: 1 MB → .log.1, max 3 rotated files
```

---

## ZipArchiveService — Key Behaviors

- **ZIP detection:** magic bytes `50 4B 03 04` — no extension check
- **Encryption detection:** bit 0 of general purpose bit flag in local file header
- **Known non-ZIP formats:** RAR, 7-Zip, GZip, BZip2, XZ, LZ4 detected by magic bytes → `SkippedFiles`
- **Smart extract (T-14):** single root folder → strips prefix; multiple roots → creates subfolder; single root file → direct
- **ZIP slip protection:** every entry path validated against destination before extraction
- **Progress:** `progress?.Report((i+1)*100/total)` per file in both Archive and Extract
- **Threading:** `Task.Run` wraps all IO — never blocks UI thread
- **Indeterminate:** single source/archive >10 MB → `progress?.Report(-1)`
- **Lazy enumeration:** `Directory.EnumerateFiles` — no upfront collection for large directories

---

## Dependency Injection & Startup

> Merged in from the former `BOOTSTRAP.md` (2026-07-05) — same content, one owner.

```csharp
// App.xaml.cs — ConfigureServices()
services.AddSingleton<IArchiveService, ZipArchiveService>();
services.AddSingleton<IDialogService, DialogService>();
services.AddSingleton<ILogService, LogService>();
services.AddTransient<MainViewModel>();
```

| Type | Lifetime | Reason |
|------|----------|--------|
| `ZipArchiveService` | Singleton | Stateless |
| `DialogService` | Singleton | Holds window reference |
| `LogService` | Singleton | Holds file path, lock object |
| `MainViewModel` | Transient | Fresh state per window |

### ViewModel Resolution

```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    InitializeComponent();
    ViewModel = App.Services.GetRequiredService<MainViewModel>();
    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 700));
    this.AppWindow.Title = "Pakko";
    // ... icon, tray setup
}
```

### Rules

- Never call `new ZipArchiveService()` outside `ConfigureServices()`
- Never access `App.Services` from inside `Archiver.Core`
- `Archiver.Core` has zero references to any app service

---

## Planned Layer Additions (v1.2+)

### v1.2 — Shell Extension (in progress)

`Archiver.Shell` (net8.0-windows, WinExe) is implemented and included in the MSIX package,
showing progress via the in-process `IProgressDialog` COM object (`NativeProgressDialog.cs`).

**T-F61 — `Archiver.ShellExtension` (in-process COM DLL, C++/WRL):**
- One registered CLSID: `PakkoRootCommand` (`1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A`), `ThreadingModel STA`
- Sub-commands (`ExtractDialogCommand`, `ExtractHereCommand`, `ExtractFolderCommand`,
  `CompressDialogCommand`, `ArchiveCommand`, `TestCommand`) returned at runtime via
  `IExplorerCommand::EnumSubCommands` — not separately registered in the manifest
- Selection logic in `EnumSubCommands` (order mirrors NanaZip's real `ContextMenu.cpp`: dialog
  command before its one-click sibling in each group; `TestCommand` always last per Pakko's own
  primary-actions-before-diagnostic rule, `CLAUDE.md`): `ExtractDialogCommand`/`TestCommand` shown
  whenever selection contains ≥1 `.zip` (`AnyPathIsZip`); `ExtractHereCommand`/`ExtractFolderCommand`
  shown only when all paths are `.zip` (`AllPathsAreZip`); `CompressDialogCommand` always shown;
  `ArchiveCommand` shown unless all paths are `.zip`
- `Invoke` launches `Archiver.Shell.exe` via `CreateProcess` with the correct argument set —
  dialog commands (T-F63) use `--open-ui --extract`/`--open-ui --archive` to route through
  `Archiver.App`'s `pakko://` activation instead of running silently
- Registered via `com:SurrogateServer` in `Package.appxmanifest` — `com:Path` must be a **child
  element** of the server, not a `Path` attribute on `com:Class` (see `DECISIONS.md`); requires
  `MinVersion="10.0.18362.0"` (Windows 10 1903) or higher in `TargetDeviceFamily`
- Rejected alternative: out-of-process COM EXE server inside `Archiver.Shell.exe` — an
  in-process DLL has lower latency and needs no `LocalServer32` infrastructure; see
  `DECISIONS.md` for the full rationale and the crash-isolation risk this accepts

### v1.3 — ITarService Layer

New interface and implementation in `Archiver.Core`:

```csharp
// Models/TarCapabilities.cs
public sealed record TarCapabilities
{
    public bool SupportsRar { get; init; }
    public bool Supports7z { get; init; }
    public bool SupportsZstd { get; init; }
    public bool SupportsXz { get; init; }
    public bool SupportsLzma { get; init; }
    public bool SupportsBz2 { get; init; }
    public string Version { get; init; } = string.Empty;
}
```

```csharp
// Interfaces/ITarService.cs
public interface ITarService
{
    Task<TarCapabilities> DetectCapabilitiesAsync();
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    // T-F05: built on the RunTarAsync primitive (-tf + -tvf) — deliberately does NOT reuse
    // ScanForUnsafeEntriesAsync's pre-scan; listing must never be gated on a policy that only
    // matters once bytes are about to be written to disk.
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
```

Implementation: `TarProcessService` in `Archiver.Core/Services/`.

- Always invokes `C:\Windows\System32\tar.exe` (absolute path)
- Quarantine/staging: same pattern as T-F26/T-F27 (temp dir on same disk, atomic move)
- Whole-archive pre-scan (T-F49) runs before `-xf`: `tar -tf`/`-tvf` reject the archive outright
  if any entry name is unsafe or any entry is a symlink/hardlink/device — see DECISIONS.md's
  T-F49 entry for why post-extraction validation alone isn't sufficient
- Post-extraction validation: ADS, reserved names, reparse points — via `ArchiveEntrySecurity`
  (`Archiver.Core/Services/ArchiveEntrySecurity.cs`), a shared internal static class also used by
  `ZipArchiveService`, so this checklist can't drift between the two extractors
- MOTW propagation: copies `Zone.Identifier` ADS from archive to each extracted file (also via
  `ArchiveEntrySecurity`)

`DetectCapabilitiesAsync` (T-F48) runs `tar.exe --version` and delegates parsing to
`TarVersionParser.Parse(string)` in `Archiver.Core/Services/TarVersionParser.cs` — pulled into
its own class so format detection is unit-testable without launching a process, the same
rationale as `Archiver.Shell`'s `ShellArgumentParser` (T-F57). `Supports7z`/`SupportsRar` are
gated on libarchive >= 3.7.0.

DI registration:

```csharp
services.AddSingleton<ITarService, TarProcessService>();
services.AddSingleton<TarCapabilities>(sp =>
    sp.GetRequiredService<ITarService>().DetectCapabilitiesAsync().GetAwaiter().GetResult());
services.AddSingleton<IExtractionRouter, ExtractionRouter>();
services.AddSingleton<IArchiveListingRouter, ArchiveListingRouter>(); // T-F05
```

### v1.3 — IExtractionRouter (T-F85)

`MainViewModel`/`Archiver.Shell` don't call `IArchiveService`/`ITarService` directly for
extraction — both go through `IExtractionRouter`, which splits a mixed selection by format and
merges the results:

```csharp
// Interfaces/IExtractionRouter.cs
public interface IExtractionRouter
{
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
```

Implementation: `ExtractionRouter` in `Archiver.Core/Services/`. Classifies every
`ArchivePaths` entry via `ArchiveFormatDetector.Detect` (magic-byte only — ZIP/gzip/bzip2/
RAR/7z/xz/zstd via header bytes, plain `.tar` via the `ustar` string at offset 257), routes
ZIP to `IArchiveService` and tar-family formats `TarCapabilities` reports supported to
`ITarService` (both sub-calls get `OpenDestinationFolder = false` — the router opens it itself,
once, after merging), adapts `ITarService`'s `IProgress<int>` into `IProgress<ProgressReport>`,
and merges both `ArchiveResult`s. A tar-family format the installed tar.exe doesn't support
becomes a `SkippedFiles` entry with a specific reason, not a generic message.

`ZipArchiveService.GetKnownArchiveReason` is deliberately not refactored to share
`ArchiveFormatDetector` — see `DECISIONS.md`-equivalent reasoning in `TASKS.md`'s T-F85 entry
(opposite polarity, not behavior-equivalent).

### v1.4 — Low IL Sandbox

`TarSandboxedService` implements `ITarService` — replaces `TarProcessService` via single DI line change:

```csharp
services.AddSingleton<ITarService, TarSandboxedService>(); // was TarProcessService
```

P/Invoke surface:
- `CreateRestrictedToken` — drop privileges from Pakko token
- `DuplicateTokenEx` — duplicate for `CreateProcessAsUser`
- `SetTokenInformation` — set integrity level to Low
- `CreateProcessAsUser` — launch tar.exe with restricted token
- `SetNamedSecurityInfo` — label quarantine directory with Low IL

Flow:
1. Create quarantine directory on same disk as destination
2. Label quarantine directory Low IL via `SetNamedSecurityInfo`
3. Launch `tar.exe` into quarantine with restricted token
4. After process exits, validate all files at Medium IL in C# code
5. Atomic move to final destination
6. Clean up quarantine directory

### v1.4 — T-F05 Archive Browser

New models in `Archiver.Core/Models/`:

```csharp
// Models/ArchiveEntryInfo.cs
public sealed record ArchiveEntryInfo
{
    public required string Path { get; init; }  // '/'-separated, no leading slash
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public DateTime? Modified { get; init; }     // null for tar (date column is locale-mangled)
    public bool IsDirectory { get; init; }
}

// Models/ArchiveListResult.cs
public sealed record ArchiveListResult
{
    public bool Success { get; init; }
    public IReadOnlyList<ArchiveEntryInfo> Entries { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
```

`IArchiveService`/`ITarService` each gain:

```csharp
Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default);
```

Routed by a new interface, mirroring `IExtractionRouter`'s dispatch exactly (same
`ArchiveFormatDetector`/`TarCapabilities` logic, copied rather than shared — see `DECISIONS.md`):

```csharp
// Interfaces/IArchiveListingRouter.cs
public interface IArchiveListingRouter
{
    Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default);
}
```

Implementation: `ArchiveListingRouter` in `Archiver.Core/Services/`. `TarProcessService.ListEntriesAsync`
is built on the existing `RunTarAsync` primitive (`-tf` + `-tvf`), deliberately **not** reusing
`ScanForUnsafeEntriesAsync` — listing must never be gated on the security pre-scan that only
matters once bytes are about to be written to disk.

`ExtractOptions` gains one field for the archive browser's "Extract selected" command:

```csharp
public IReadOnlyList<string>? SelectedEntryPaths { get; init; }
```

Non-null/non-empty restricts extraction to just those archive-internal entry paths (a selected
folder implies its full nested contents); `null` (default) is unaffected — every existing caller
extracts everything, as before. Rides through `ExtractionRouter`'s existing
`options with { ArchivePaths = ... }` pattern for free — `ExtractionRouter.cs` itself needed zero
changes. Both `ZipArchiveService.ExtractWithSmartFolderingAsync` and
`TarProcessService.ExtractSingleArchiveAsync` implement the filtering; the tar side's
whole-archive pre-scan (T-F49) still runs **unconditionally** before the subset is ever computed
— see `DECISIONS.md`'s T-F05 entry for the tar.exe selective-extraction spike this was verified
against.

DI registration adds:

```csharp
services.AddSingleton<IArchiveListingRouter, ArchiveListingRouter>();
```

New project `Archiver.App.Core` (plain `net8.0`, no WinUI — referenced by `Archiver.App`, tested
by `Archiver.App.Core.Tests`) holds the App-layer model and the flat-to-tree helper:

```csharp
// Archiver.App.Core/ArchiveEntryViewModel.cs
public sealed record ArchiveEntryViewModel
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required bool IsFolder { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public DateTime? Modified { get; init; }
    // + ModifiedDisplay/SizeDisplay/Icon computed properties
}

// Archiver.App.Core/ArchiveTreeIndex.cs
public static class ArchiveTreeIndex
{
    public static IReadOnlyDictionary<string, IReadOnlyList<ArchiveEntryViewModel>> Build(
        IReadOnlyList<ArchiveEntryInfo> flatEntries);
}
```

`Build` synthesizes implied folder nodes from `/`-split paths (ZIP archives commonly have no
explicit directory entries) and runs once per archive open — folder navigation afterward is an
O(1) dictionary lookup, never a re-scan of the flat list, which matters at the 65,000+-entry
scale this app's archives can reach (T-F20).

`MainViewModel` (`Archiver.App`) gains an `IArchiveListingRouter` constructor dependency plus
browser state (`IsBrowsingArchive`, `BrowsedArchivePath`, `CurrentFolderPath`,
`CurrentFolderEntries`, `BreadcrumbSegments`, `SelectedBrowserEntries`) and commands
(`EnterBrowseModeAsync`, `NavigateIntoFolder`/`NavigateToBreadcrumbSegment`, `ExitBrowseModeCommand`,
`ExtractSelectedFromBrowserCommand`/`ExtractAllFromBrowserCommand`/`ExtractSingleBrowserEntryAsync`,
`ShowSelectedEntryInfoCommand`). The Extract-related commands all funnel through a shared private
`RunExtractAsync(archivePaths, selectedEntryPaths)` that the pre-existing whole-archive
`ExtractCommand` was refactored to call too — the `IsBusy`/progress/stopwatch/bomb-confirm/
summary-dialog/cleanup sequence is identical for both; only `ArchivePaths` and
`SelectedEntryPaths` differ.

`IDialogService` gains `Task ShowEntryInfoAsync(ArchiveEntryViewModel entry)` — a `ContentDialog`
showing Name/Path/Size/CompressedSize/Modified, matching the existing `ShowFileHashAsync` pattern.

`MainWindow.xaml`'s Row 1 (file table) and Row 3 (action buttons) each gain a sibling `Grid`
toggled by `IsPendingListVisibility`/`IsBrowsingArchiveVisibility` (inline mode-swap, not a new
window or `NavigationView`). The browser `Grid` holds a `BreadcrumbBar` (first use in this
codebase) and a `ListView` (`SelectionMode="Multiple"`, explicit
`VirtualizingStackPanel VirtualizationMode="Recycling"` via `ItemsPanelTemplate`) bound to
`ArchiveEntryViewModel`. Double-clicking a recognized archive in the existing pending-selection
list (gated by `ArchiveFormatDetector.Detect`, not `FileItem.Type`) calls `EnterBrowseModeAsync`.

---

## FileItem Model (UI layer)

```csharp
// Models/FileItem.cs (Archiver.App only)
public sealed class FileItem
{
    public string FullPath { get; }
    public string Name { get; }
    public string Type { get; }           // extension uppercase or "Folder"
    public string Size { get; set; }      // "1.2 MB", "345 KB", "12 bytes"
    public long SizeBytes { get; set; }
    public DateTime Modified { get; }
    public string ModifiedDisplay { get; } // "yyyy-MM-dd HH:mm"
}
```
