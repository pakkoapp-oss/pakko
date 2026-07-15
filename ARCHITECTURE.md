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
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteArchiveAfterExtraction { get; init; } = false;

    // T-F94: invoked when an archive looks like a decompression bomb (declared uncompressed
    // size vs. compressed size exceeds the ratio threshold) AND the destination has enough free
    // space to hold it. True proceeds with extraction, false declines. Null (default) auto-
    // declines — the safe behavior for callers that don't wire a callback (Archiver.Shell).
    public Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction { get; init; }

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
// TarProcessService.ExtractAsync) — see DECISIONS.md's T-F06 entry and DIAGRAMS.md's diagrams 3/5.
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

    // T-F94: called from a thread-pool thread by the extractors — implementation must marshal
    // onto the window's DispatcherQueue before showing a ContentDialog. See DECISIONS.md's
    // T-F94 entry.
    Task<bool> ShowCompressionBombConfirmAsync(CompressionBombWarning warning);

    // T-F06: same DispatcherQueue-marshaling requirement as ShowCompressionBombConfirmAsync above.
    Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo conflict);
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
`ArchiveFormatDetector` — see `DECISIONS.md`-equivalent reasoning in `TASKS.md`'s T-F85 entry
(opposite polarity, not behavior-equivalent).

### v1.4 — AppContainer Sandbox for tar.exe

**Status: implemented (T-F52 Phase 1, steps 1–11 of 13 complete 2026-07-14) — `TarSandboxedService`
is real, shipping code, not a design description.** It implements `ITarService`, replacing
`TarProcessService` (deleted outright, not kept as a fallback — fail-closed posture, see
`TASKS.md`/`DECISIONS.md`'s T-F52 entries). Mechanism is an **AppContainer**, not a Low-IL
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
    public uint? Crc32 { get; init; }
    public DateTime? Modified { get; init; }
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
tar-routed format (RAR/7z/tar.*) — `TarProcessService`'s listing path never populates
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
    public string Crc32Display { get; set; } // "..." while computing, "?" on read error, hex once done, empty for folders
    public uint? Crc32 { get; set; }
    public DateTime Modified { get; }
    public string ModifiedDisplay { get; } // "yyyy-MM-dd HH:mm"
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
