# ARCHITECTURE.md — Architecture and Layer Contracts

> **Current as of v1.0.** All signatures reflect actual implemented code.

---

## Layer Diagram

```
┌─────────────────────────────────────┐
│           Archiver.App              │
│           (WinUI 3, net8.0-win)     │
│                                     │
│  MainWindow.xaml / .cs              │
│  ViewModels/MainViewModel.cs        │
│  Services/ (Dialog, Log)            │
│  Strings/en-US/Resources.resw       │
└──────────────┬──────────────────────┘
               │  project reference
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
└── Archiver.App/
    ├── MainWindow.xaml / .cs
    ├── ViewModels/
    │   └── MainViewModel.cs
    ├── Services/
    │   ├── IDialogService.cs
    │   ├── DialogService.cs
    │   ├── ILogService.cs
    │   └── LogService.cs
    ├── Models/
    │   └── FileItem.cs
    ├── Converters/
    │   └── BoolToVisibilityConverter.cs
    └── Strings/
        └── en-US/
            └── Resources.resw
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
}

public enum ExtractMode { SeparateFolders, SingleFolder }
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
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
```

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

## DI Registration

```csharp
services.AddSingleton<IArchiveService, ZipArchiveService>();
services.AddSingleton<IDialogService, DialogService>();
services.AddSingleton<ILogService, LogService>();
services.AddTransient<MainViewModel>();
```

---

## Planned Layer Additions (v1.2+)

### v1.2 — Shell Extension

New project `Archiver.ShellExtension` (net8.0-windows, COM):

```
Archiver.ShellExtension/
  Commands/
    ArchiveHereCommand.cs       ← IExplorerCommand
    ExtractHereCommand.cs       ← IExplorerCommand
    ExtractToFolderCommand.cs   ← IExplorerCommand
```

Registered via MSIX AppExtension in `Package.appxmanifest`. Communicates with `Archiver.App` via named pipe or protocol activation.

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
}
```

Implementation: `TarProcessService` in `Archiver.Core/Services/`.

- Always invokes `C:\Windows\System32\tar.exe` (absolute path)
- Quarantine/staging: same pattern as T-F26/T-F27 (temp dir on same disk, atomic move)
- Post-extraction validation: ADS, reserved names, reparse points
- MOTW propagation: copies `Zone.Identifier` ADS from archive to each extracted file

DI registration:

```csharp
services.AddSingleton<ITarService, TarProcessService>();
services.AddSingleton<TarCapabilities>(sp =>
    sp.GetRequiredService<ITarService>().DetectCapabilitiesAsync().GetAwaiter().GetResult());
```

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
