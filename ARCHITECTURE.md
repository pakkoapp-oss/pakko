# ARCHITECTURE.md — Architecture and Layer Contracts

---

## Layer Diagram

```
┌─────────────────────────────────────┐
│           Archiver.App              │
│           (WinUI 3)                 │
│                                     │
│  Views (XAML)                       │
│  ViewModels (INotifyPropertyChanged)│
│  App-level Services                 │
└──────────────┬──────────────────────┘
               │  project reference
               ▼
┌─────────────────────────────────────┐
│           Archiver.Core             │
│         (Class Library)             │
│                                     │
│  Interfaces                         │
│  Services (ZipArchiveService)       │
│  Models (ArchiveOptions, etc.)      │
└──────────────┬──────────────────────┘
               │  uses
               ▼
┌─────────────────────────────────────┐
│       Windows / .NET APIs           │
│                                     │
│  System.IO.Compression              │
│  System.IO (File, Directory, Path)  │
│  Windows.System.Launcher (optional) │
└─────────────────────────────────────┘
```

**Rule:** `Archiver.Core` must have **zero** references to WinUI, Microsoft.UI, or Windows.UI namespaces.

---

## Folder Structure

```
src/
├── Archiver.Core/
│   ├── Archiver.Core.csproj
│   ├── Interfaces/
│   │   └── IArchiveService.cs
│   ├── Services/
│   │   └── ZipArchiveService.cs
│   └── Models/
│       ├── ArchiveOptions.cs
│       ├── ExtractOptions.cs
│       ├── ArchiveResult.cs
│       └── ArchiveError.cs
│
└── Archiver.App/
    ├── Archiver.App.csproj
    ├── Views/
    │   └── MainWindow.xaml (.cs)
    ├── ViewModels/
    │   └── MainViewModel.cs
    ├── Services/
    │   └── DialogService.cs
    └── Models/
        └── (UI-only models if needed)
```

---

## Core Models — Exact Signatures

Agent must implement these exactly. Do not rename or add required constructor parameters.

```csharp
// Models/ArchiveOptions.cs
namespace Archiver.Core.Models;

public sealed record ArchiveOptions
{
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public string? ArchiveName { get; init; }              // null = auto-name from source
    public ArchiveMode Mode { get; init; } = ArchiveMode.SingleArchive;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Ask;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteSourceFiles { get; init; } = false;
}

public enum ArchiveMode
{
    SingleArchive,      // all sources → one .zip
    SeparateArchives    // one .zip per source item
}

public enum ConflictBehavior
{
    Ask,
    Overwrite,
    Skip
}
```

```csharp
// Models/ExtractOptions.cs
namespace Archiver.Core.Models;

public sealed record ExtractOptions
{
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public ExtractMode Mode { get; init; } = ExtractMode.SeparateFolders;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Ask;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteArchiveAfterExtraction { get; init; } = false;
}

public enum ExtractMode
{
    SeparateFolders,    // each archive → own subfolder
    SingleFolder        // all archives → one flat folder
}
```

```csharp
// Models/ArchiveResult.cs
namespace Archiver.Core.Models;

public sealed record ArchiveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> CreatedFiles { get; init; } = [];
    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
}
```

```csharp
// Models/ArchiveError.cs
namespace Archiver.Core.Models;

public sealed record ArchiveError
{
    public string SourcePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
```

---

## Interface — Exact Signature

```csharp
// Interfaces/IArchiveService.cs
namespace Archiver.Core.Interfaces;

public interface IArchiveService
{
    /// <summary>
    /// Creates one or more ZIP archives from the provided options.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts one or more ZIP archives.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
```

---

## ZipArchiveService — Implementation Notes

File: `Services/ZipArchiveService.cs`

```csharp
namespace Archiver.Core.Services;

public sealed class ZipArchiveService : IArchiveService
{
    // Use ZipFile.CreateFromDirectory for folders
    // Use ZipArchive for fine-grained control over individual files
    // Wrap all IO in try/catch — append to errors list, continue processing
    // Report progress as percentage of processed items (0–100)
    // Check cancellationToken.IsCancellationRequested between items
}
```

Compression API reference:
```csharp
using System.IO.Compression;

// Create from directory
ZipFile.CreateFromDirectory(sourceDir, destZip, CompressionLevel.Optimal, includeBaseDirectory: true);

// Create from files (manual)
using var archive = ZipFile.Open(destZip, ZipArchiveMode.Create);
archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);

// Extract
ZipFile.ExtractToDirectory(sourceZip, destDir, overwriteFiles: true);
```

---

## ViewModel — MainViewModel Outline

```csharp
// ViewModels/MainViewModel.cs
namespace Archiver.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IArchiveService _archiveService;

    [ObservableProperty] private ObservableCollection<string> _selectedPaths = [];
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy = false;
    [ObservableProperty] private int _progress = 0;

    // Commands
    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync() { ... }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync() { ... }

    private bool CanArchive() => !IsBusy && SelectedPaths.Count > 0;
    private bool CanExtract() => !IsBusy && SelectedPaths.Count > 0;
}
```

Use `CommunityToolkit.Mvvm` for `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`.  
This is the only allowed NuGet package in `Archiver.App`.

---

## Project File Targets

```xml
<!-- Archiver.Core.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
</Project>
```
