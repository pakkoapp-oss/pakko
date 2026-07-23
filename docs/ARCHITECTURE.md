# ARCHITECTURE.md — Architecture and Layer Contracts

> **Current as of v1.4/v1.5 (last synced 2026-07-18).** All signatures reflect actual implemented
> code, verified by reading it — see `CLAUDE.md`'s Documentation Map for when to update this file.

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
├── Archiver.Core/              ← net8.0, zero UI deps, zero NuGet packages
│   ├── Interfaces/
│   │   ├── IArchiveService.cs
│   │   ├── IArchiveCreationRouter.cs   ← T-F105: routes ArchiveAsync by ArchiveContainerFormat
│   │   ├── IArchiveListingRouter.cs    ← T-F05: routes ListEntriesAsync by detected format
│   │   ├── IExtractionRouter.cs        ← T-F85: routes ExtractAsync/TestAsync by detected format
│   │   └── ITarService.cs
│   ├── Services/
│   │   ├── ZipArchiveService.cs
│   │   ├── TarSandboxedService.cs      ← T-F52: replaced TarProcessService outright, no fallback
│   │   ├── ArchiveCreationRouter.cs / ArchiveListingRouter.cs / ExtractionRouter.cs
│   │   ├── ArchiveEntrySecurity.cs     ← ADS/reserved-name/reparse-point/bomb checks, shared
│   │   ├── ArchiveFormatDetector.cs    ← magic-byte sniffing, not extension-based
│   │   ├── ArchiveNaming.cs            ← compound-extension-aware naming (T-F103)
│   │   ├── ConflictResolver.cs         ← T-F06: resolves ConflictBehavior.Ask
│   │   ├── PreviewPolicy.cs            ← T-F97/T-F109: safe-preview allowlist
│   │   ├── TarVersionParser.cs
│   │   ├── FileHashService.cs          ← T-F128: single-file/multi-file/single-folder-recursive hashing
│   │   ├── Sandbox/                    ← T-F52: AppContainer subsystem for tar.exe
│   │   │   ├── AppContainerProfile.cs / QuarantineAcl.cs / QuarantineStaging.cs
│   │   │   ├── SandboxJobObject.cs / SandboxedProcessLauncher.cs / SandboxHandles.cs
│   │   │   ├── SecurityCapabilitiesAttributeList.cs
│   │   │   └── TarSandboxScope.cs / TarSignatureVerifier.cs
│   │   └── Zip/                        ← T-F35: parallel SingleArchive compression pipeline
│   │       ├── WorkItemEnumerator.cs / FileWorkItem.cs / WorkResult.cs
│   │       ├── ParallelSingleArchiveWriter.cs
│   │       └── ZipEntryWriter.cs / ZipEntryCompressor.cs / DosDateTime.cs
│   ├── IO/
│   │   ├── Crc32.cs                    ← public (T-F110); slice-by-8 (T-F128 follow-up, was
│   │   │                                  byte-at-a-time — real ~9x perf gap vs. 7-Zip found via
│   │   │                                  HashPerformanceTests), reused by pending-list CRC too.
│   │   │                                  Combine() (zlib crc32_combine reimplementation) lets
│   │   │                                  FileHashService hash one large file's chunks in
│   │   │                                  parallel then fold results back together in order
│   │   ├── ProgressStream.cs           ← T-F16: byte-accurate IProgress<int> wrapper, per-stream
│   │   ├── AggregateProgressTracker.cs ← T-F128 follow-up: shared byte counter across many
│   │   │                                  concurrently-hashed files, folder-wide total (not
│   │   │                                  per-file) — fixes ComputeFolderAsync's progress bug
│   │   ├── AggregateProgressStream.cs  ← T-F128 follow-up: read-only wrapper reporting into the
│   │   │                                  tracker above, used only by ComputeFolderAsync
│   │   └── HashDigestAccumulator.cs    ← T-F128: NanaZip-compatible folder DataSum/NamesSum combine
│   └── Models/
│       ├── ArchiveOptions.cs / ExtractOptions.cs / ArchiveResult.cs
│       ├── ArchiveError.cs / SkippedFile.cs / ProgressReport.cs
│       ├── ArchiveFormat.cs / ArchiveContainerFormat.cs   ← detection vs. creation enums
│       ├── ArchiveEntryInfo.cs / ArchiveListResult.cs     ← T-F05: browse-mode listing
│       ├── ConflictInfo.cs / ConflictDecision.cs          ← T-F06
│       ├── CompressionBombWarning.cs                      ← T-F94
│       ├── HashAlgorithmKind.cs                           ← T-F128: Crc32 | Sha256
│       └── TarCapabilities.cs
│
├── Archiver.App/               ← WinUI 3 main app; packages all satellite EXEs via MSIX
│   ├── MainWindow.xaml / .cs
│   ├── App.xaml.cs             ← ConfigureServices (DI), OnLaunched/OnActivated (T-F83/T-F100)
│   ├── ViewModels/
│   │   └── MainViewModel.cs
│   ├── Services/
│   │   ├── IDialogService.cs / DialogService.cs
│   │   └── ILogService.cs / LogService.cs
│   ├── Models/
│   │   └── FileItem.cs         ← ObservableObject, [ObservableProperty]-generated (CommunityToolkit MVVM)
│   ├── Converters/
│   │   └── BoolToVisibilityConverter.cs
│   └── Strings/                ← 37 locales (T-F91), en-US is the fallback
│       └── en-US/Resources.resw
│
├── Archiver.App.Core/          ← net8.0, WinUI-free helpers for Archiver.App (T-F05), unit-testable
│   │                              without a WinUI test host
│   ├── ArchiveEntryViewModel.cs / ArchiveTreeIndex.cs   ← Archive Browser tree/breadcrumb building
│   ├── FileSystemBrowser.cs                             ← T-F107: real-filesystem climb past archive root
│   ├── FileActivationRouter.cs                          ← T-F100: file activation routing
│   ├── ProtocolActivationRouter.cs                      ← T-F03: pakko://browse detection
│   ├── NestedArchiveCache.cs / NestedArchivePolicy.cs   ← T-F98: nested-archive drill-down
│   ├── PreviewCache.cs                                  ← T-F97: preview extraction cache
│   └── DeferredActionGate.cs                            ← T-F106: defers activation past first layout pass
│
├── Archiver.Shell/             ← shell-triggered operation entry point; net8.0-windows; WinExe; no WinUI
│   ├── Program.cs
│   ├── ShellArgumentParser.cs
│   ├── ShellResultPresenter.cs         ← T-F68: classifies ArchiveResult into Failed/SkippedOnly/Success
│   ├── NativeProgressDialog.cs         ← IProgressDialog COM interop (in-process progress UI)
│   ├── HashResultLocalizer.cs          ← T-F128 follow-up: first localized text in Archiver.Shell —
│   │                                      plain .resx/ResourceManager (not App's WinRT/.resw — needs
│   │                                      no Windows-versioned TFM, see DECISIONS.md)
│   └── Resources/
│       ├── HashMessages.resx           ← neutral (English)
│       └── HashMessages.<locale>.resx  ← 36 locales, matches Archiver.App/Strings/'s own set
│
├── Archiver.CLI/                ← standalone console frontend (T-F09); net8.0; Exe (real console,
│   │                                not WinExe); no WinUI; built as pakko.exe; ships independently
│   │                                of the MSIX (scripts/Publish-Cli.ps1)
│   ├── Program.cs
│   ├── CliArgumentParser.cs
│   ├── CliStreamStaging.cs             ← T-F116: -si/-so buffer-then-proceed staging, zero Core changes
│   ├── CliCompressionLevelMapper.cs / CliEntryFormatter.cs / CliHelpText.cs
│
└── Archiver.ShellExtension/    ← IExplorerCommand COM DLL (T-F61); C++/WRL, x64+ARM64, static CRT
    ├── dllmain.cpp                     ← DllGetClassObject, DllCanUnloadNow
    ├── ExplorerCommands.cpp/.h         ← PakkoRootCommand, SubCommandEnum, and every leaf command
    │                                     (ExtractDialog/ExtractHereFlat/ExtractHere/ExtractFolder/
    │                                     CompressDialog/Archive/TarArchive/Test/Browse/Hash) —
    │                                     T-F128: HashCommand (ECF_HASSUBCOMMANDS) + its two leaves
    │                                     HashCrc32Command/HashSha256Command
    ├── ShellExtUtils.cpp/.h            ← COM-free logic, unit-tested via Archiver.ShellExtension.Tests
    └── Localization.cpp/.h             ← T-F115: 37-locale context-menu string table
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
    // T-F105 (v1.4): which container format to CREATE. Default Zip preserves all pre-T-F105
    // callers/tests unchanged. Deliberately separate from the detection-only ArchiveFormat enum
    // (Models/ArchiveFormat.cs) used by extraction routing — that one includes read-only formats
    // (Rar, SevenZip) that can never be a creation target.
    public ArchiveContainerFormat Format { get; init; } = ArchiveContainerFormat.Zip;

    // T-F06: invoked once per conflicting destination path when OnConflict == Ask. Null (e.g.
    // Archiver.Shell, or a test that doesn't wire it) falls back to Skip — see ConflictResolver.
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }
}

public enum ArchiveMode { SingleArchive, SeparateArchives }

// Models/ArchiveContainerFormat.cs
public enum ArchiveContainerFormat { Zip, Tar, TarGz, TarBz2, TarXz, TarZst, TarLzma }

public enum ConflictBehavior { Overwrite, Skip, Rename, Ask }
// T-23 (v1.0): Ask was cut from scope, default Skip. T-F06 (2026-07-14) reintroduced it as a
// real interactive per-conflict dialog — see ConflictResolver and DECISIONS.md's T-F06 entry.
```

```csharp
// Models/ExtractOptions.cs
public sealed record ExtractOptions
{
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public ExtractMode Mode { get; init; } = ExtractMode.SeparateFolders;

    // Overrides the per-archive subfolder name Mode.SeparateFolders would otherwise derive from
    // the archive's own file name. Only meaningful when ArchivePaths has exactly one entry — used
    // by Archiver.Shell for "always create a fresh named folder" behavior (see CLAUDE.md's hard
    // constraint on ConflictBehavior.Rename vs. this field).
    public string? SeparateFolderName { get; init; }

    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteArchiveAfterExtraction { get; init; } = false;

    // T-F94: invoked when an archive looks like a decompression bomb (declared uncompressed
    // size vs. compressed size exceeds the ratio threshold) AND the destination has enough free
    // space to hold it. True proceeds with extraction, false declines. Null (default) auto-
    // declines — the safe behavior for callers that don't wire a callback (Archiver.Shell).
    public Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction { get; init; }

    // T-F05: non-null/non-empty restricts extraction to just these archive-internal entry paths
    // instead of every entry — "Extract selected" from the Archive Browser. A selected directory
    // path implies its full nested contents. Only meaningful with exactly one archive path.
    public IReadOnlyList<string>? SelectedEntryPaths { get; init; }

    // T-F06: invoked once per conflicting entry when OnConflict == Ask. Same null-safe-default
    // (Skip) and delegate shape as ArchiveOptions.ResolveConflictAsync above.
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }
}

public enum ExtractMode { SeparateFolders, SingleFolder }
```

```csharp
// Models/ConflictInfo.cs
public sealed record ConflictInfo
{
    public required string ExistingPath { get; init; }
}

// Models/ConflictDecision.cs
public enum ConflictResolution { Skip, Overwrite, Rename }  // no Ask — a resolved decision, not a policy

public sealed record ConflictDecision
{
    public required ConflictResolution Resolution { get; init; }
    public bool ApplyToAll { get; init; }  // suppresses further ResolveConflictAsync calls this operation
}
```

```csharp
// Services/ConflictResolver.cs — internal, Archiver.Core.Services
// Resolves ConflictBehavior.Ask into a concrete Skip/Overwrite/Rename by invoking the caller's
// ResolveConflictAsync callback, remembering an ApplyToAll choice for its own lifetime. One
// instance is constructed per ArchiveAsync/ExtractAsync call, before any loop, so "apply to all"
// spans every archive/entry in that call. Wired into all four existing conflict-resolution call
// sites (ZipArchiveService.ArchiveAsync's two modes, ZipArchiveService.ExtractAsync,
// TarSandboxedService.ExtractAsync) — see DECISIONS.md's T-F06 entry and DIAGRAMS.md's diagrams 3/5.
internal sealed class ConflictResolver(
    ConflictBehavior configured,
    Func<ConflictInfo, Task<ConflictDecision>>? resolveConflictAsync)
{
    public Task<ConflictBehavior> ResolveAsync(string existingPath);
}
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
    Task ShowFileHashAsync();

    // T-F94: called from a thread-pool thread by the extractors — implementation must marshal
    // onto the window's DispatcherQueue before showing a ContentDialog. See DECISIONS.md's
    // T-F94 entry.
    Task<bool> ShowCompressionBombConfirmAsync(CompressionBombWarning warning);

    // T-F06: same DispatcherQueue-marshaling requirement as ShowCompressionBombConfirmAsync above.
    Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo conflict);

    // T-F97: opens a previewed/extracted file with the OS default handler. Process.Start
    // (UseShellExecute:true), not StorageFile/Launcher — the latter silently fails for an
    // arbitrary %TEMP% path even from this app's full-trust packaged identity (see DECISIONS.md's
    // T-F97 entry).
    Task<bool> OpenFileWithDefaultAppAsync(string filePath);
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

## `Archiver.Core/Services/Zip/` — T-F35 Parallel `SingleArchive` Pipeline

Gated inside `ZipArchiveService.ArchiveAsync`'s `SingleArchive` branch: below
`ParallelPipelineFileCountThreshold` (64 files), the original always-sequential
`ZipFile.Open`/`AddDirectoryToArchiveAsync`/`AddEntryFromFileAsync` code runs completely
unchanged. Above it, archiving routes into this subsystem instead — see `DECISIONS.md`'s T-F35
entry (and its follow-up entries) for the full design rationale (why `ZipArchive` can't be
reused for the write side once gated, the Option A/B trade-off, the temp-file-compression
redesign that removed the original design's file-size ceiling, and the real bugs the test suite
caught along the way).

Every non-placeholder file is compressed in parallel now, regardless of size — small files
(≤1 MiB) fully in memory, everything else via a private per-worker temp file (bounded memory
either way: a `byte[]` capped at 1 MiB, or a fixed copy-buffer streamed to disk, never O(file
size) in RAM). There is no longer a "some files skip the parallel path" case.

```csharp
// FileWorkItem.cs — one unit of work, in the exact final ZIP entry order
internal readonly record struct FileWorkItem(
    string SourcePath, string EntryName, FileWorkKind Kind, long FileSize, DateTime LastWriteTime);
internal enum FileWorkKind { File, DirectoryPlaceholder }

// WorkItemEnumerator.cs — deterministic, lazy; reshapes AddDirectoryToArchiveAsync's recursive
// walk into a flat IEnumerable<FileWorkItem>, preserving T-F31/T-F32 order, T-F30 collision
// renaming (reuses ZipArchiveService.GetUniqueEntryName, widened to internal), T-F66 empty-dir
// placeholders, T-F23 reparse-point skip, T-F75 fixed rootDir. Uses DirectoryInfo.EnumerateFiles()/
// EnumerateDirectories() (not the plain string-path overloads) so Length/LastWriteTime/Attributes
// come from the same FindNextFile data the enumeration already read — zero extra stat calls.
internal static class WorkItemEnumerator
{
    public static IEnumerable<FileWorkItem> Enumerate(
        IReadOnlyList<string> sortedSourcePaths,
        Action<SkippedFile> reportSkipped, Action<ArchiveError> reportError);
}

// WorkResult.cs — outcome of processing one FileWorkItem, consumed strictly in enqueue order.
// TempFileCompressed replaced the original "large files stream sequentially" design outright —
// both compressed cases know crc/compressed/uncompressed size fully upfront by the time the
// writer sees them (the temp file is already fully written by a background worker).
internal enum WorkResultKind { Compressed, TempFileCompressed, DirectoryPlaceholder, Error }
internal sealed record WorkResult { /* Kind + payload; static WorkResult.For*() factories */ }

// ZipEntryCompressor.cs — compresses a file's bytes fully into memory via DeflateStream directly
// (no ZipArchiveEntry involved), for the in-memory (≤1 MiB) parallel path.
internal readonly record struct CompressedEntryData(
    byte[] CompressedBytes, uint Crc32, long UncompressedLength, ushort Method);
internal static class ZipEntryCompressor
{
    public static CompressedEntryData Compress(Stream sourceStream, CompressionLevel compressionLevel);
}

// ParallelSingleArchiveWriter.cs — dispatch/drain orchestration
internal static class ParallelSingleArchiveWriter
{
    public const long InMemoryCompressByteThreshold = 1L * 1024 * 1024; // 1 MiB — memory-shape
        // boundary (RAM buffer vs. disk-streamed buffer), not a parallelism-eligibility boundary.
    public static int ComputeWindowCapacity(); // Clamp(ProcessorCount, 2, 16)

    public static Task WriteAsync(
        string tempPath, IReadOnlyList<string> sortedSourcePaths, CompressionLevel compressionLevel,
        long totalBytes, Action<SkippedFile> reportSkipped, Action<ArchiveError> reportError,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken);

    // Decoupled from real compression so whitebox tests can inject controllable delegates for
    // both compress paths — see ParallelSingleArchiveWriterTests (including the whitebox test
    // that caught a real "bounded channel alone doesn't bound concurrency" bug).
    internal static Task RunPipelineAsync(
        string tempPath, IEnumerable<FileWorkItem> items,
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressInMemory,
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressToTempFile,
        int windowCapacity, long totalBytes, IProgress<ProgressReport>? progress,
        Action<ArchiveError> reportError, CancellationToken cancellationToken);
}

// ZipEntryWriter.cs — hand-rolled ZIP container writer (local file header/central directory/
// EOCD, conditional Zip64 — never "always on", see DECISIONS.md). Owns the whole output file
// once gated; a ZipArchive and this writer never share one output stream. Both write methods
// take crc/compressed/uncompressed size fully known upfront — no unknown-size placeholder-then-
// patch mechanism (removed once the "large files stream sequentially, sizes unknown until done"
// design was replaced by temp-file compression, which always knows sizes before the writer runs).
internal sealed class ZipEntryWriter : IAsyncDisposable
{
    internal const ushort StoredMethod = 0;
    internal const ushort DeflateMethod = 8;
    internal static ushort SelectMethod(CompressionLevel compressionLevel);

    public ZipEntryWriter(string path);
    public int EntryCount { get; }
    public Task WriteCompressedEntryAsync(string entryName, CompressedEntryData data, DateTime lastWriteTime, CancellationToken ct); // in-memory byte[]
    public Task WriteCompressedEntryFromStreamAsync(string entryName, Stream compressedSource,
        long compressedLength, long uncompressedLength, uint crc32, ushort method,
        DateTime lastWriteTime, CancellationToken ct); // temp-file-sourced, streamed copy, no size ceiling
    public Task WriteDirectoryPlaceholderAsync(string entryName, DateTime lastWriteTime, CancellationToken ct);
    public ValueTask DisposeAsync(); // writes central directory + EOCD (+ Zip64 if needed)

    // internal — reused by ParallelSingleArchiveWriter's temp-file compression worker so a file's
    // CRC is computed in the same single read pass as compression, not a second full file read.
    internal static Task<(long Total, uint Crc32)> CopyWithCrcAsync(
        FileStream source, Stream destination, byte[] buffer, IProgress<ProgressReport>? progress,
        long totalBytes, long startOffset, string entryName, CancellationToken ct);
}

// DosDateTime.cs — MS-DOS date/time packing, byte-identical to ZipArchiveEntry's own encoding
// (verified by a dedicated test that compares against a real ZipArchiveEntry-written header).
internal static class DosDateTime
{
    public static uint Encode(DateTime dateTime);
    public static DateTime Decode(uint packed);
}
```

`Archiver.Core.IO.Crc32` gained an `Accumulator` struct (incremental CRC-32 — `Update(ReadOnlySpan<byte>)`/
`Finish()`) alongside its existing whole-stream `Compute(Stream)`, so uncompressed bytes can be
hashed in the same single read pass as compression instead of a second full file read.

**Temp-file lifecycle (T-F35 follow-up, twice-revised):** `WriteAsync` creates a per-operation,
uniquely-named, **hidden** subfolder next to the destination archive —
`{destinationDir}\.pakko-tmp-{Guid}\` — not loose files scattered in that folder (on-device
verification showed those visibly flickering in Explorer mid-operation) and not a shared
`%TEMP%` location either (considered and rejected in turn: a different, possibly smaller/fuller
volume than the destination, which matters now that there's no per-file size ceiling). Same-
volume-as-destination plus `FileAttributes.Hidden` gets both disk-space locality and
invisible-by-default in Explorer at once. `CompressToTempFileAsync` (in
`ParallelSingleArchiveWriter`) creates one uniquely-named chunk file (`chunk-{Guid}.tmp`) inside
that folder per file above the in-memory threshold, streams the compressed result into it, and
hands the finished path to the writer via `WorkResult.TempFileCompressed`. The writer copies its
bytes into the archive and deletes it immediately after. A tracked set (`ConcurrentDictionary<string,byte>`)
plus an outer `finally` that awaits every dispatched compress task before sweeping guarantees no
orphaned temp file survives cancellation or an unhandled exception — a real race (a straggler task
finishing and creating its temp file *after* an earlier sweep attempt already ran) was caught by a
test that failed intermittently under full-suite parallel load before this was fixed; see
`DECISIONS.md`.

Before creating a chunk file, `CompressToTempFileAsync` also checks
`ArchiveEntrySecurity.GetAvailableFreeSpace(chunkDirectory)` against the file's declared size —
reusing the same T-F94 helper the extraction-side compression-bomb check already uses. Archive
*creation* never had any disk-space check before this (only extraction did, and only as part of
the bomb defense); the temp-file redesign introduced a real, if best-effort-only-mitigated, new
disk-space risk that direct streaming never had. Insufficient space is reported as a per-file
`ArchiveError`, no disk touched — see `DECISIONS.md`.

---

## FileHashService — Current Signature (T-F128)

Static, stateless — mirrors `ArchiveNaming`/`ArchiveFormatRegistryNames`'s "no DI needed" shape,
not a constructor-injected service. Consumed directly by `Archiver.Shell.Program.RunHashAsync`
(the Explorer context-menu's "Хеш-суми" submenu — see `ExplorerCommands.cpp`'s `HashCommand`).

```csharp
// Models/HashAlgorithmKind.cs
public enum HashAlgorithmKind { Crc32, Sha256 }

// Services/FileHashService.cs
public sealed record HashEntry(string SourcePath, string? Hash, string? Error);
public sealed record FolderHashSummary(string DataSum, string NamesSum, int FileCount, long TotalBytes); // TotalBytes: T-F128 follow-up

public sealed class HashResult
{
    public IReadOnlyList<HashEntry> Entries { get; init; }
    public FolderHashSummary? Folder { get; init; } // non-null only for a single-folder input
}

public static class FileHashService
{
    public static Task<HashResult> ComputeAsync(
        IReadOnlyList<string> paths,
        HashAlgorithmKind algorithm,
        IProgress<ProgressReport>? progress,
        CancellationToken ct);
}

// IO/HashDigestAccumulator.cs (internal — NanaZip-compatible combine algorithm, see
// DECISIONS.md's T-F128 entry for the full byte-level derivation against real NanaZip source)
internal sealed class HashDigestAccumulator
{
    public HashDigestAccumulator(int digestSize);
    public void Add(ReadOnlySpan<byte> itemDigest);
    public string ToDisplayString(); // e.g. "3B4FE1AC-00000001" once 2+ items are combined
}
```

**Branching in `ComputeAsync`:** exactly one folder path → recursive whole-tree hash with a
combined `Folder` summary (DataSum always NanaZip-exact regardless of nesting; NamesSum exact per
file, but omits each subfolder object's own contribution — see `DECISIONS.md`). Anything else
(one or more files, or a folder mixed into a larger selection) → each path hashed independently
into `Entries`, `Folder` stays `null`; a folder encountered in this branch is recorded as a
skipped `HashEntry`, not summed.

---

## Dependency Injection & Startup

> Merged in from the former `BOOTSTRAP.md` (2026-07-05) — same content, one owner.

```csharp
// App.xaml.cs — ConfigureServices()
services.AddSingleton(GroupPolicyService.Load());
services.AddSingleton<ILogService, LogService>();
services.AddSingleton<IArchiveService, ZipArchiveService>();
services.AddSingleton<IDialogService, DialogService>();
services.AddSingleton<ITarService, TarSandboxedService>();
services.AddSingleton<TarCapabilities>(sp =>
    sp.GetRequiredService<ITarService>().DetectCapabilitiesAsync().GetAwaiter().GetResult());
services.AddSingleton<IExtractionRouter, ExtractionRouter>();
services.AddSingleton<IArchiveListingRouter, ArchiveListingRouter>();
services.AddSingleton<IArchiveCreationRouter, ArchiveCreationRouter>();
services.AddTransient<MainViewModel>();
// T-F48: TarCapabilities is force-resolved once right after BuildServiceProvider() — a
// factory-registered singleton only runs on first resolution, and nothing else injects it eagerly.
// T-F51: GroupPolicyOptions is registered first so ActivatorUtilities can inject it into every
// consumer below via their optional `GroupPolicyOptions? policy = null` ctor param — a registered
// concrete instance is used over the null default automatically.
```

| Type | Lifetime | Reason |
|------|----------|--------|
| `GroupPolicyOptions` | Singleton, eager (`GroupPolicyService.Load()`) | One synchronous registry read at startup (T-F51); never changes mid-session |
| `LogService` | Singleton | Holds file path, lock object |
| `ZipArchiveService` | Singleton | Stateless (besides the injected `GroupPolicyOptions`) |
| `DialogService` | Singleton | Holds window reference |
| `TarSandboxedService` | Singleton | Stateless (per-call sandbox scope, not per-instance state) |
| `TarCapabilities` | Singleton, factory-resolved | Probed once at startup (T-F48), never changes at runtime |
| `ExtractionRouter` / `ArchiveListingRouter` / `ArchiveCreationRouter` | Singleton | Stateless — route by detected/requested format only |
| `MainViewModel` | Transient | Fresh state per window |

### ViewModel Resolution

```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    // Tray commands (TrayOpenCommand/TrayAboutCommand/TrayExitCommand/TrayLeftClickCommand/
    // HashFilesCommand) are constructed here, before InitializeComponent() — omitted below.
    InitializeComponent();
    ViewModel = App.Services.GetRequiredService<MainViewModel>();

    // 1100x780, floor 900x780 via OverlappedPresenter.PreferredMinimumWidth/Height — re-tuned
    // twice by T-F106; see DECISIONS.md's T-F106 entry for the full before/after account. Not
    // 800x700 — that size predates T-F105/T-F106 and could clamp the file table to zero height.
    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));
    if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
    {
        presenter.PreferredMinimumWidth = 900;
        presenter.PreferredMinimumHeight = 780;
    }

    // Title bar shows the running assembly's own file-write timestamp, not a static "Pakko" or a
    // manually-bumped version — makes every on-device screenshot self-certifying proof a fresh
    // Deploy.ps1 build is actually installed (see CLAUDE.md's "never trust build logs alone" note).
    var buildTime = System.IO.File.GetLastWriteTime(
        System.Reflection.Assembly.GetExecutingAssembly().Location);
    this.AppWindow.Title = $"Pakko — build {buildTime:yyyy-MM-dd HH:mm:ss}";

    this.AppWindow.SetIcon("Assets/Square44x44Logo.ico");
    this.Activated += OnFirstActivated;
    RootGrid.Loaded += RootGrid_Loaded;
    this.Closed += (_, _) =>
    {
        TrayIcon.Dispose();
        ActivationGate.Cancel();
        PreviewCache.DeleteAll();      // T-F97
        NestedArchiveCache.DeleteAll(); // T-F98
    };
}
```

`ActivationGate` (`DeferredActionGate`, `Archiver.App.Core`) defers File/Protocol-activation
mutations (`FileItems`, `IsBrowsingArchive`) until after `RootGrid`'s first `Loaded`/layout pass —
mutating them synchronously right after `Activate()` realizes `ListView` containers against an
incomplete layout, leaving rows permanently blank (T-F106).

### Rules

- Never call `new ZipArchiveService()` outside `ConfigureServices()`
- Never access `App.Services` from inside `Archiver.Core`
- `Archiver.Core` has zero references to any app service

---

## Planned Layer Additions (v1.2+)

### v1.2 — Shell Extension

`Archiver.Shell` (net8.0-windows, WinExe) is implemented and included in the MSIX package,
showing progress via the in-process `IProgressDialog` COM object (`NativeProgressDialog.cs`).

**T-F61 — `Archiver.ShellExtension` (in-process COM DLL, C++/WRL):**
- One registered CLSID: `PakkoRootCommand` (`1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A`), `ThreadingModel STA`
- Sub-commands (`BrowseCommand`, `ExtractDialogCommand`, `ExtractHereCommand`, `ExtractFolderCommand`,
  `CompressDialogCommand`, `ArchiveCommand`, `TestCommand`) returned at runtime via
  `IExplorerCommand::EnumSubCommands` — not separately registered in the manifest
- Selection logic in `EnumSubCommands` (order mirrors NanaZip's real `ContextMenu.cpp`: `BrowseCommand`
  ("Open") first, mirroring NanaZip's own `kOpen`-before-`kExtract` order (T-F03) — a separate,
  coexisting command, not a replacement for `ExtractDialogCommand`; dialog command before its
  one-click sibling in each group; `TestCommand` always last per Pakko's own
  primary-actions-before-diagnostic rule, `CLAUDE.md`): `BrowseCommand` shown only for a single-item
  selection that's a supported archive (`paths.size() == 1 && AllPathsAreSupportedArchive`);
  `ExtractDialogCommand`/`TestCommand` shown
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

    // T-F105 (v1.4): archive CREATION, not extraction — deliberately unsandboxed (trusted local
    // input, not an untrusted archive; see SECURITY.md's tar.exe Trust Model). IProgress<
    // ProgressReport>, not IProgress<int> like ExtractAsync above, to match
    // IArchiveService.ArchiveAsync's contract for IArchiveCreationRouter below.
    Task<ArchiveResult> CompressAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
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
services.AddSingleton<IArchiveCreationRouter, ArchiveCreationRouter>(); // T-F105
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
`ArchiveFormatDetector` — see `DECISIONS.md`-equivalent reasoning in `TASKS_DONE.md`'s T-F85 entry
(opposite polarity, not behavior-equivalent).

### v1.4 — AppContainer Sandbox for tar.exe

**Status: implemented (T-F52 Phase 1, steps 1–11 of 13 complete 2026-07-14) — `TarSandboxedService`
is real, shipping code, not a design description.** It implements `ITarService`, replacing
`TarProcessService` (deleted outright, not kept as a fallback — fail-closed posture, see
`TASKS_DONE.md`/`DECISIONS.md`'s T-F52 entries). Mechanism is an **AppContainer**, not a Low-IL
restricted token (superseded design, see `DECISIONS.md`'s T-F52 tradeoff entry — network isolation
falls out of AppContainer's empty capability list for free, avoiding a global firewall rule). DI
swap touches three call sites, not one — `Archiver.App/App.xaml.cs`, `Archiver.Shell/Program.cs`
(no DI container there, a direct `new`), and `SkipIfFormatUnsupportedAttribute.cs` (test infra):

```csharp
services.AddSingleton<ITarService, TarSandboxedService>(); // was TarProcessService
```

`src/Archiver.Core/Services/Sandbox/` — single-concern classes, no P/Invoke god-class:
`SandboxHandles.cs` (4 `SafeHandle` types), `AppContainerProfile.cs`, `QuarantineAcl.cs`,
`QuarantineStaging.cs`, `SandboxJobObject.cs`, `SandboxedProcessLauncher.cs`,
`SecurityCapabilitiesAttributeList.cs`, `TarSignatureVerifier.cs`, and `TarSandboxScope.cs` — the
disposable orchestration class every sandboxed tar.exe launch actually goes through
(`TarSandboxScope.RunAsync`), tying profile + ACL + staging + Job Object + signature check
together per archive operation.

P/Invoke surface:
- `CreateAppContainerProfile` — created lazily once, reused for the lifetime of the install
  (never per-operation; tolerates `ERROR_ALREADY_EXISTS`)
- `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` (`InitializeProcThreadAttributeList`/
  `UpdateProcThreadAttribute`) — attaches the AppContainer SID + an empty capability list (no
  network) to raw `CreateProcessW`'s extended startup info (`STARTUPINFOEX`)
- `SetEntriesInAclW`/`SetNamedSecurityInfoW` — grants the AppContainer SID access to the
  quarantine `in\`/`out\` subfolders and the staged archive file itself (a hardlinked staged file
  shares its security descriptor with the *original* archive, not the containing folder's — found
  empirically, see `DECISIONS.md`'s T-F52 entry) via the standard NTFS simple-permission masks:
  `in\` = Read&Execute (`0x1200A9`), `out\` = Modify (`0x1301BF`), quarantine-root = traverse-only
  (`0x0020`, non-inherited)
- `CreateJobObject`/`SetInformationJobObject`/`AssignProcessToJobObject` — `ActiveProcessLimit = 1`
  + RAM/CPU limits + UI restrictions (absorbed from T-F13)
- `WinVerifyTrust` + `CryptQueryObject`/`CryptMsgGetParam`/`CertGetNameStringW` — Authenticode
  signature + Microsoft-Organization check before every scope creation (cheap, defense-in-depth
  only, not managed `X509Certificate2` — that only extracts the embedded cert without verifying it
  against the file's actual bytes)

Flow (`TarSandboxScope.CreateAsync` + `RunAsync`):
1. Verify `tar.exe`'s Authenticode signature (Microsoft Organization) once per scope — covers
   every `RunAsync` call made through that scope (pre-scan and extraction both share one scope)
2. Ensure the AppContainer profile exists (lazy, once, never deleted)
3. Create a two-subfolder quarantine directory rooted under a fixed, Pakko-owned
   `%TEMP%\PakkoTarSandbox\<guid>\` location — **not** "same disk as the destination" (an earlier
   design assumption corrected during implementation: an AppContainer token has no
   bypass-traverse-checking privilege, so `FILE_TRAVERSE` is enforced on every ancestor directory,
   and the user's arbitrary destination folder sits under an ancestor chain Pakko doesn't own —
   see `DECISIONS.md`'s T-F52 entry). `in\` gets Read&Execute, `out\` gets Modify, both plus the
   quarantine root get a traverse-only grant on every Pakko-created ancestor level
4. Stage the source archive into `in\` via hardlink (same volume) or copy (cross-volume), then
   explicitly grant Read&Execute on the staged file path itself too — the AppContainer SID never
   gets an ACE on the archive's original, user-chosen path
5. Create a fresh Job Object per tar.exe launch (`ActiveProcessLimit = 1`, RAM/CPU limits,
   `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`)
6. Run tar.exe inside the AppContainer for **both** the T-F49 whole-archive pre-scan (`-tf`/`-tvf`)
   and the extraction (`-xf`) — the pre-scan is not exempt from sandboxing. Before `-xf` runs,
   Pakko itself (at its own trusted identity, not tar.exe's sandboxed one) pre-creates every
   directory the archive implies via `Directory.CreateDirectory` — libarchive's own implicit
   parent-directory creation was found to fail under the AppContainer even with a correctly
   ACL'd `out\` (see `DECISIONS.md`'s T-F52 entry)
7. After the process exits, validate all files in `out\` at Pakko's normal process identity
   (existing `ArchiveEntrySecurity` checks)
8. Move each file from `out\` to the final destination via `File.Move` (a per-file move, already
   cross-volume-safe — not a directory rename, so rooting the quarantine under `%TEMP%` instead of
   next to the destination costs at most an extra copy, never a correctness problem). MOTW
   propagation reads the *original* archive path, never the staged copy
9. Dispose the scope: delete the quarantine directory (`in\`+`out\`, including the staged archive
   copy) and release the AppContainer SID handle — the AppContainer profile itself is **not**
   deleted, it persists for reuse

### v1.4 — T-F05 Archive Browser

New models in `Archiver.Core/Models/`:

```csharp
// Models/ArchiveEntryInfo.cs
public sealed record ArchiveEntryInfo
{
    public required string Path { get; init; }  // '/'-separated, no leading slash
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public uint? Crc32 { get; init; }            // null for tar-routed formats — no per-entry CRC
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

Implementation: `ArchiveListingRouter` in `Archiver.Core/Services/`. `TarSandboxedService.ListEntriesAsync`
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
`TarSandboxedService.ExtractSingleArchiveAsync` implement the filtering; the tar side's
whole-archive pre-scan (T-F49) still runs **unconditionally** before the subset is ever computed
— see `DECISIONS.md`'s T-F05 entry for the tar.exe selective-extraction spike this was verified
against. (`TarSandboxedService` replaced `TarProcessService` outright in T-F52 — fail-closed, no
unsandboxed fallback; every reference to `TarProcessService` elsewhere in this file describes
pre-T-F52 history and is dated accordingly.)

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
    public uint? Crc32 { get; init; }
    public DateTime? Modified { get; init; }

    // T-F98: true only for a nested-archive row where drilling in would exceed
    // NestedArchivePolicy.MaxDepth — the one case where double-clicking an archive entry does
    // NOT transparently drill in. Drives the Icon property's View-vs-Hide glyph choice (T-F110).
    public bool NestedDepthLimitReached { get; init; }

    // + ModifiedDisplay/SizeDisplay/CompressedSizeDisplay/CrcDisplay/Icon computed properties
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
(`EnterBrowseModeAsync`, `NavigateIntoFolder`/`NavigateToBreadcrumbSegment`,
`NavigateUpOrExitBrowserCommand`,
`ExtractSelectedFromBrowserCommand`/`ExtractAllFromBrowserCommand`/`ExtractSingleBrowserEntryAsync`).
The Extract-related commands all funnel through a shared private
`RunExtractAsync(archivePaths, selectedEntryPaths)` that the pre-existing whole-archive
`ExtractCommand` was refactored to call too — the `IsBusy`/progress/stopwatch/bomb-confirm/
summary-dialog/cleanup sequence is identical for both; only `ArchivePaths` and
`SelectedEntryPaths` differ. `NavigateUpOrExitBrowserCommand` (added in a follow-up round, same
day) replaced the standalone `ExitBrowseModeCommand`/Close button entirely: it steps up one
archive folder level when not at the archive's own root, and exits browse mode (the same effect
`ExitBrowseMode` — kept as a private method, no longer its own command — always had) when already
there. A second, unrelated up-navigation command, `NavigateDestinationUpCommand`, was added the
same round for the Destination Path row (Row 2, shared by both modes): it sets
`DestinationPath = Path.GetDirectoryName(DestinationPath)`, disabled via its own `CanExecute`
when that returns `null` (a drive root or an unrooted path).

`ArchiveEntryViewModel` (`Archiver.App.Core`) exposes `SizeDisplay`/`CompressedSizeDisplay`/
`CrcDisplay` — all three render as the entry table's own columns (Row 1 browse in
`MainWindow.xaml`) rather than a separate Info dialog; `IDialogService.ShowEntryInfoAsync` was
removed the same day it shipped (design review 2026-07-13) once every field it showed had a
table-column equivalent. Note `CompressedSizeDisplay`/`CrcDisplay` are both blank for every
tar-routed format (RAR/7z/tar.*) — `TarSandboxedService`'s listing path never populates
`CompressedSize`/`Crc32` (no per-entry concept for either in a tar-family archive) — so both
columns only ever show a value for ZIP. `CrcDisplay` guards on `Crc32 is null`, not `<= 0` —
unlike a size, `0` is a legitimate CRC-32 (an empty file), so it cannot double as a
"not available" sentinel the way `CompressedSizeDisplay`'s `<= 0` guard safely does.

`MainWindow.xaml`'s Row 1 (file table) and Row 3 (action buttons) each gain a sibling `Grid`
toggled by `IsPendingListVisibility`/`IsBrowsingArchiveVisibility` (inline mode-swap, not a new
window or `NavigationView`). The browser `Grid` holds a `BreadcrumbBar` (first use in this
codebase) and a `ListView` (`SelectionMode="Multiple"`, explicit
`VirtualizingStackPanel VirtualizationMode="Recycling"` via `ItemsPanelTemplate`) bound to
`ArchiveEntryViewModel`. Double-clicking a recognized archive in the existing pending-selection
list (gated by `ArchiveFormatDetector.Detect`, not `FileItem.Type`) calls `EnterBrowseModeAsync`.

### v1.4 — IArchiveCreationRouter (T-F105, TAR Archive Creation)

Archive **creation** gains the same one-interface-per-operation dispatch pattern
`IExtractionRouter`/`IArchiveListingRouter` already established — but simpler, since the format
is a single explicit `ArchiveOptions.Format` choice for the whole call, not something detected
per-path from file content:

```csharp
// Interfaces/IArchiveCreationRouter.cs
public interface IArchiveCreationRouter
{
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
```

Implementation: `ArchiveCreationRouter` in `Archiver.Core/Services/` — a single branch, no
per-path splitting/merging like `ExtractionRouter`:

```csharp
public sealed class ArchiveCreationRouter(IArchiveService archiveService, ITarService tarService)
    : IArchiveCreationRouter
{
    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
        options.Format == ArchiveContainerFormat.Zip
            ? archiveService.ArchiveAsync(options, progress, cancellationToken)
            : tarService.CompressAsync(options, progress, cancellationToken);
}
```

`TarSandboxedService.CompressAsync` runs `tar.exe` **unsandboxed** — no `TarSandboxScope`/
AppContainer/Job Object — since creation reads trusted local files, not an untrusted archive; see
`SECURITY.md`'s "Why Archive Creation Is NOT Sandboxed" for the full reasoning. It builds one
`tar.exe -cf` invocation per output archive (`ArchiveMode.SingleArchive` = one invocation for all
sources via a repeated `-C <parent> <name>` pair per source, confirmed to preserve relative
structure correctly; `ArchiveMode.SeparateArchives` = one invocation per source), using the
temp-file-then-atomic-move pattern `ZipArchiveService.ArchiveAsync` already uses (no partial files
on cancel/failure). Compression level (the existing ZIP `System.IO.Compression.CompressionLevel`
enum, reused rather than adding a second one) maps to tar.exe's real
`--options <filter>:compression-level=N` mechanism — confirmed empirically that a bare `-9`-style
flag does not work, but `--options` does, for all five write filters (gzip/bzip2/xz/zstd/lzma);
plain `Tar` gets no filter flag and no `--options` at all (passing `--options` without an active
filter fails outright — confirmed). Progress is file-count-based (parsed from `-v`'s per-entry
`stderr` lines — confirmed empirically that verbose creation output goes to stderr, not stdout),
not byte-accurate like ZIP's `ProgressStream` — an accepted v1 trade-off, same precedent as the
tar-family listing path being coarser than ZIP's.

`ArchiveNaming` (`Archiver.Core/Services/ArchiveNaming.cs`) gains the inverse of its existing
`GetBaseName` — `GetExtension(ArchiveContainerFormat)` maps the enum to `.zip`/`.tar`/`.tar.gz`/
etc.; `ZipArchiveService.ArchiveAsync`'s two previously-hardcoded `".zip"` literals now call it
too, so both creation paths share one source of truth for extensions.

DI registration adds:

```csharp
services.AddSingleton<IArchiveCreationRouter, ArchiveCreationRouter>();
```

**Phase B (2026-07-16):** `MainViewModel`'s constructor now takes `IArchiveCreationRouter` instead
of `IArchiveService` — that was its only `IArchiveService` call site. `ArchiveAsync` calls
`_archiveCreationRouter.ArchiveAsync(options, progress, _cts.Token)` with a new
`SelectedContainerFormat` (`ArchiveContainerFormat`, default `Zip`) plumbed into
`ArchiveOptions.Format`. `MainWindow.xaml` gained a "Формат" `ComboBox` (Row 5, right before the
existing Compression combobox) bound to a new `FormatIndex` int property, following the same
`get`/`set` switch-expression pattern as `CompressionLevelIndex`/`OnConflictIndex`. The
Compression combobox's `IsEnabled` now binds to a new `IsCompressionLevelEnabled` property
(`IsNotBusy && !IsPlainTarFormatSelected`) instead of plain `IsNotBusy`, so it greys out only when
plain `Tar` is selected (the Phase A empirical finding — every other format, ZIP included, keeps
a working compression-level control). All 37 locale `Resources.resw` files gained the
`FormatLabel`/`FormatZipItem`/.../`FormatTarLzmaItem` keys; the 7 format-name item values are
identical, untranslated Latin script in every locale (technical identifiers, not prose — same
convention Windows Explorer itself follows), only the `FormatLabel.Text` word is translated per
locale.

**Phase C (2026-07-16):** a new `TarArchiveCommand` leaf `IExplorerCommand`
(`Archiver.ShellExtension/ExplorerCommands.h`/`.cpp`, CLSID `5F440071-6288-4446-AE25-3F4EDA490DDC`)
mirrors `ArchiveCommand` exactly but always passes `L".tar"`/`L"tar"` where `ArchiveCommand` passes
the ZIP defaults — plain/uncompressed tar only, since one-click commands never prompt the user for
a filter choice. Registered in `PakkoRootCommand::EnumSubCommands` right after `ArchiveCommand`
("Add to X.zip"); needs no `Package.appxmanifest` entry — only the root command's CLSID is ever
registered there, every leaf command is instantiated internally via `Make<T>()`. Two shared
`ShellExtUtils` functions gained parameters instead of new twin functions:
`BuildAddToArchiveTitle(paths, ext = L".zip")` and `BuildArchiveArgs(paths, format = L"zip")` (the
latter only emits `--format <value>` for a non-`"zip"` value, so the pre-existing zip command line
is unchanged). On the .NET side, `ShellArgumentParser.ParseArchive` consumes an optional
`--format zip|tar` pair right after `--archive` into a new `ParsedCommand.Format`
(`ArchiveContainerFormat`, default `Zip`); `Archiver.Shell/Program.cs`'s `RunArchiveAsync` now
constructs `new ArchiveCreationRouter(new ZipArchiveService(), new TarSandboxedService())` directly
(no DI container in this console entry point) instead of calling `ZipArchiveService.ArchiveAsync`,
and sets `ArchiveOptions.Format` from the parsed switch.

---

### v1.4 — GroupPolicyOptions (T-F51, Group Policy Support)

Four registry-backed policies under `HKLM\Software\Policies\Pakko\` — see `POLICIES.md` for the
sysadmin-facing spec (value vocabulary, defaults, interaction rules) and `deploy/` for the
ADMX/ADML templates. This section documents the code shape only.

```csharp
// Models/MotwMode.cs
public enum MotwMode { Disabled = 0, AllFiles = 1, UnsafeExtensionsOnly = 2 }

// Models/GroupPolicyOptions.cs
public sealed record GroupPolicyOptions
{
    public MotwMode MotwMode { get; init; } = MotwMode.AllFiles;
    public IReadOnlyList<string>? AllowedFormats { get; init; }
    public IReadOnlyList<string>? BlockedFormats { get; init; }
    public bool DisableTarExtraction { get; init; }

    public bool IsFormatAllowed(string registryName); // BlockedFormats takes precedence over AllowedFormats
}

// Interfaces/IRegistryReader.cs — minimal seam, hand-rolled FakeRegistryReader in tests (no
// mocking library anywhere in this repo)
public interface IRegistryReader
{
    int? GetDword(string keyPath, string valueName);
    string[]? GetMultiString(string keyPath, string valueName);
}

// Services/Win32RegistryReader.cs — [SupportedOSPlatform("windows")], reads HKEY_LOCAL_MACHINE,
// swallows every failure (absent key, access denied, wrong type) and returns null
public sealed class Win32RegistryReader : IRegistryReader { /* ... */ }

// Services/GroupPolicyService.cs — static, never throws; absent/malformed values fall back to
// today's shipped (unrestricted) defaults
public static class GroupPolicyService
{
    [SupportedOSPlatform("windows")]
    public static GroupPolicyOptions Load();                 // real registry, used by App/Shell/CLI
    public static GroupPolicyOptions Load(IRegistryReader r); // testable overload
}

// Services/ArchiveFormatRegistryNames.cs — maps ArchiveFormat/ArchiveContainerFormat to the
// registry-string vocabulary (zip/tar/gzip/bz2/xz/zstd/lzma/rar/sevenzip) AllowedFormats/
// BlockedFormats use. ArchiveContainerFormat.TarGz maps to "gzip" — the same name
// ArchiveFormat.GZip detection uses — since the two enums don't line up 1:1.
public static class ArchiveFormatRegistryNames
{
    public static string ToRegistryName(ArchiveFormat format);
    public static string ToRegistryName(ArchiveContainerFormat format);
}
```

**Consumer wiring** — `GroupPolicyOptions? policy = null` was added as an optional constructor
parameter (default = "everything allowed", so every pre-T-F51 `new XService()` call site keeps
compiling) to:

- `ZipArchiveService` / `TarSandboxedService` — `_policy.MotwMode` threaded down into
  `ArchiveEntrySecurity.TryPropagateMotw(archivePath, destFilePath, motwMode)`'s new third
  parameter (`Disabled` no-ops, `UnsafeExtensionsOnly` checks `destFilePath`'s extension against a
  fixed unsafe-extension list modeled on Windows Attachment Manager/SmartScreen). Both extractors'
  smart-foldering helper methods (`ExtractWithSmartFolderingAsync` / `ExtractSingleArchiveAsync`)
  are `private static`, so `motwMode` is threaded through as an explicit parameter rather than
  captured — a `static` local/private method cannot close over an instance field.
- `ExtractionRouter` — gained a 4th ctor param; `AllowedFormats`/`BlockedFormats` are checked for
  every non-`Unknown` detected format before the existing Zip/tar-family switch, and
  `DisableTarExtraction` is checked only for the tar-family branch (both produce a `SkippedFile`,
  never a thrown exception).
- `ArchiveCreationRouter` — gained a 3rd ctor param; this router had **zero** capability/whitelist
  check before T-F51, so both the format-block check and the `DisableTarExtraction` check
  (POLICIES.md documents `DisableTarExtraction` as blocking creation too, not just extraction) are
  wholly new code, returning an `ArchiveResult` with `Success = false` rather than throwing.
- `MainViewModel` — gained a 6th ctor param; exposes `TarFormatVisibility` (`Visibility.Collapsed`
  when `DisableTarExtraction`), bound from `MainWindow.xaml`'s 6 tar `ComboBoxItem`s via
  `Visibility="{x:Bind ViewModel.TarFormatVisibility}"`. `SelectedContainerFormat` is defensively
  reset to `Zip` in the constructor if policy disables tar — normally unreachable today since
  nothing else sets it away from the `Zip` default, kept in case a future caller (e.g. protocol
  activation) sets a tar format directly.

**DI (`Archiver.App`)** — `services.AddSingleton(GroupPolicyService.Load());`, registered before
every consumer above so `ActivatorUtilities` injects the real instance instead of falling back to
each optional parameter's `null` default. **No DI (`Archiver.Shell`, `Archiver.CLI`)** —
`GroupPolicyOptions policy = GroupPolicyService.Load();` once near the top of `Program.cs`,
threaded explicitly into every inline `new ZipArchiveService(policy)` /
`new TarSandboxedService(policy)` / `new ExtractionRouter(..., policy)` /
`new ArchiveCreationRouter(..., policy)` call site (this supersedes the parameterless
constructor calls shown in the T-F105/T-F09 sections above, which predate T-F51).

`ArchiveListingRouter` and `RunInfoAsync`/`RunListAsync`'s inline service construction are
**deliberately not threaded with a policy** — listing is read-only (no MOTW propagation, nothing
written to disk) and out of this task's scope; see `ITarService.ListEntriesAsync`'s own doc
comment on why listing must never be gated on an extraction-time policy.

---

### v1.5 — Archiver.CLI (T-F09)

A fourth thin frontend over `Archiver.Core`, alongside App/Shell/ShellExtension — 7z-familiar
single-letter commands (`x`/`t`/`i`/`a`/`l`), specified in full in `CLI.md`. Ships as a separate,
standalone, self-contained downloadable artifact (see `CLI.md`'s "Distribution" section and
`scripts/README.md`) — it does not require the MSIX/GUI to be installed and is never packaged
into it.

**No DI container** — mirrors `Archiver.Shell/Program.cs`'s pattern exactly (manual `new
ZipArchiveService(policy)`/`new TarSandboxedService(policy)` construction, `await
tarService.DetectCapabilitiesAsync()` once, then `new` the relevant router directly per command),
not `Archiver.App`'s `ServiceCollection`. `GroupPolicyService.Load()` is called once near the top
of `Program.cs` and threaded through explicitly (T-F51) — see that section below for why this
project no longer has "nothing to inject" for these two services.

`CliArgumentParser.Parse(string[])` (in `Archiver.CLI/CliArgumentParser.cs`) never throws — mirrors
`ShellArgumentParser`'s shape:

```csharp
public enum CliCommandType { Extract, Test, Info, Archive, List, Help, Invalid }

public sealed record ParsedCliCommand
{
    public CliCommandType Type { get; init; }
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];   // x, t, l
    public IReadOnlyList<string> SourcePaths { get; init; } = [];    // a
    public string? ArchivePathArg { get; init; }                     // a: raw positional[0]
    public string? OutputDirectory { get; init; }                    // -o{dir}, x only
    public bool AssumeYes { get; init; }                              // -y
    public ConflictBehavior? OverwriteMode { get; init; }             // -ao{a|s|u}, x only
    public ArchiveContainerFormat ArchiveFormat { get; init; } = ArchiveContainerFormat.Zip; // a
    public CompressionLevel? CompressionLevel { get; init; }          // -mx=N, a only
    public string? ErrorMessage { get; init; }
}
```

Per-command allowed-switch enforcement lives inside the parser itself — any switch token not on a
command's own allowed list is rejected with CLI.md's three-way rule (case 1: unparseable token;
case 2: a real 7z command Pakko deliberately doesn't implement, e.g. `u`/`d`/`rn`/`b`/`e`; case 3: a
real, supported command with an unsupported switch). Never a silent no-op.

**Command → Core API mapping:**

| Command | Core API | Notes |
|---|---|---|
| `x` | `IExtractionRouter.ExtractAsync` | `ExtractMode.SingleFolder`; `OnConflict = -ao ?? (-y ? Overwrite : Skip)`; `ConfirmCompressionBombExtraction` set only when `-y` |
| `t` | `IArchiveService.TestAsync` (ZIP-only, called directly — `ITarService` has no Test method) | tar-family paths become `SkippedFile`s with a named reason, not silently dropped |
| `i` | `ITarService.DetectCapabilitiesAsync` | no other Core call; ZIP/Tar/GZip always listed, the rest gated on the live `TarCapabilities` |
| `a` | `IArchiveCreationRouter.ArchiveAsync` | always `ArchiveMode.SingleArchive`; `ArchiveNaming.GetBaseName` derives the name |
| `l` | `IArchiveListingRouter.ListEntriesAsync` | looped once per archive path (the router itself takes one path at a time) |

`-y` wiring: `ArchiveOptions`/`ExtractOptions` already default to `Skip`/auto-decline when their
callback delegates are null — the CLI needs to do nothing special in `-y`'s *absence*. `-y` only
*overrides* those defaults (`ConfirmCompressionBombExtraction` → always-confirm,
`OnConflict` → `Overwrite`), and an explicit `-ao` always wins over `-y` when both are given.

**`-mx` bucketing** (`CliCompressionLevelMapper.TryMap(int)`), documented rather than a naive
`/9*4` approximation:

| `-mx` | `CompressionLevel` |
|---|---|
| `0` | `NoCompression` |
| `1`–`2` | `Fastest` |
| `3`–`6` | `Optimal` (7z's own default `-mx5` lands here, matching `ArchiveOptions`' own default) |
| `7`–`9` | `SmallestSize` |

**Exit codes** (7z-familiar, documented in `--help`/`CLI.md`):

| Code | Meaning |
|---|---|
| `0` | Success, nothing skipped |
| `1` | Success, but `SkippedFiles` were present (e.g. a conflict/bomb declined without `-y`, or `t` hit a tar-family archive) |
| `2` | Operation failed (`ArchiveResult.Success == false`, listing failed, or real Test corruption) |
| `7` | Command-line error — any of the three three-way-rule categories, distinguished by stderr text, not exit code |

Test project `Archiver.CLI.Tests` has two layers: parser/mapper/help-text/formatter unit tests
(no process spawn), and a new `Subprocess/` layer that `Process.Start`s the real built
`pakko.exe` (the `Archiver.CLI` project's `AssemblyName`) against real fixtures and asserts real
exit codes/stdout/stderr — see `TESTING.md`. Distribution (`scripts/Publish-Cli.ps1`) is
documented in `scripts/README.md`.

**`-si`/`-so` stdin/stdout streaming (T-F116):** `ParsedCliCommand` gained `ReadFromStdin`/
`WriteToStdout` bools. `-si` (valid on `x`/`t`/`l`) and `-so` (valid on `x`/`a`) are implemented
entirely inside `Archiver.CLI/CliStreamStaging.cs` — **zero `Archiver.Core` changes**. `-si` copies
`Console.OpenStandardInput()` into a private `%TEMP%\Archiver.CLI.Stdin\<guid>\stdin.bin` file
before the command runs, then proceeds exactly as if that path had been typed; `-so` runs the
operation into a private `%TEMP%\Archiver.CLI.Stdout\<guid>\` folder instead of the real
destination, then streams the single resulting file to `Console.OpenStandardOutput()`
(`CliStreamStaging.StreamSingleFileAsync` takes the destination `Stream` as a parameter
specifically so the broken-pipe path is unit-testable without a real OS pipe). Both staging
locations are deleted in a `finally` block in every `Program.cs` command handler that uses them.
Rejected out of scope: true zero-copy streaming through Core — `ZipArchive` needs a seekable
stream to read its central directory, `TarSandboxedService`'s T-F49 pre-scan needs a real file to
scan before extraction runs, and `SandboxedProcessLauncher` has no stdin-redirection plumbing —
see `DECISIONS.md`'s T-F116 entry, which also records the empirical finding that native
PowerShell 5.1 (not just old cmd.exe) silently corrupts binary data piped between two native
executables, while `cmd /c "..."` does not, on any PowerShell version.

---

## FileItem Model (UI layer)

```csharp
// Models/FileItem.cs (Archiver.App only)
// CommunityToolkit.Mvvm ObservableObject, not plain mutable auto-properties — Size/SizeBytes/
// Crc32Display/Crc32 are all [ObservableProperty] source-generated fields (real property names
// Size/SizeBytes/Crc32Display/Crc32, backing fields _size/_sizeBytes/_crc32Display/_crc32), so
// LoadFolderSizeAsync/LoadCrc32Async's writes to them raise INotifyPropertyChanged for free.
public sealed partial class FileItem : ObservableObject
{
    public string FullPath { get; }
    public string Name { get; }
    public string Type { get; }              // extension uppercase or "Folder"
    public DateTime Modified { get; }
    public string ModifiedDisplay { get; }   // "yyyy-MM-dd HH:mm"

    [ObservableProperty] private string _size = "...";       // "1.2 MB", "345 KB", "12 bytes"
    [ObservableProperty] private long _sizeBytes = -1;
    [ObservableProperty] private string _crc32Display = "";  // "..." while computing, "?" on read error, hex once done, empty for folders
    [ObservableProperty] private uint? _crc32;

    // Real constructor also starts LoadFolderSizeAsync (folders) or LoadCrc32Async (files) —
    // both async, fire-and-forget, throttled via a shared static SemaphoreSlim(4) for CRC reads.
}
```

`Crc32`/`Crc32Display` (added alongside a pending-list "CRC-32" column, per user request) mirror the
existing `Size`/`SizeBytes` async-load pattern (`LoadFolderSizeAsync`) but in reverse: size is async
only for *folders* (walking the tree), CRC is async only for *files* (folders have no single
meaningful CRC to aggregate, so `LoadCrc32Async` is never started for one). Computation reuses
`Archiver.Core.IO.Crc32.Compute(Stream)` — made `public` (was `internal`, previously only used by
`ZipArchiveService`'s own integrity check) rather than adding a NuGet hashing package to
`Archiver.App` or reimplementing the algorithm a second time. A `static readonly SemaphoreSlim`
(capacity 4) throttles concurrent CRC reads across every `FileItem` instance — reading a whole
file's bytes is real disk I/O, and queuing many/large files at once (loose files added
individually, not one collapsed folder row) would otherwise spawn an unbounded number of
concurrent `Task.Run` reads. No cancellation if an item is removed mid-read — same tradeoff
`LoadFolderSizeAsync` already accepts.
