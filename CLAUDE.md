# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows with a planned shell extension (IExplorerCommand) and tar.exe integration for RAR/7z/tar extraction.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

**v1.1 complete** — tagged `v1.1.0`. GitHub-only release for early testers.
- T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39/T-F44/T-F45 complete
- 60/60 tests pass
- MSIX builds unsigned (see T-F10 for signing)
- Async streaming (CopyToAsync) — CancellationToken respected mid-file
- Temp file/dir pattern — no partial files on cancel or failure
- ZIP bomb detection via compression ratio (1000:1 threshold)
- UTF-8 round-trip verified for Cyrillic and emoji filenames
- Button text changes to "Archiving..." / "Extracting..." during operation
- Post-op cleanup (DeleteSourceFiles, DeleteArchiveAfterExtraction) runs with IsBusy=true
- SHA-256 integrity manifest removed — redundant with ZIP built-in CRC-32
- ADS blocking (T-F38), reserved filename filtering (T-F39), reparse point protection (T-F37)
- Byte-accurate progress reporting (T-F16) — `ProgressStream` wraps IO streams; `IsIndeterminate` removed
- Option controls disabled during operations — `IsNotBusy` / `IsArchiveNameAndNotBusy` properties; all option controls bind `IsEnabled`
- FileStream perf: `useAsync: false`, `bufferSize: 262144` in all `ZipArchiveService` streams (faster on local disks from ThreadPool)
- `.zip` file type association (T-F44) — double-click opens Pakko with archive pre-loaded; `AppInstance.Activated` handles both cold-start and warm file activation
- MOTW propagation (T-F45) — `Zone.Identifier` ADS copied from archive to every extracted file; best-effort, never fatal; no P/Invoke
- Status line shows operation name, file stats, speed, and ETA during operation; elapsed time after completion
- **Store release planned for v1.3** — when shell extension, MOTW propagation,
  and tar.exe integration are complete. v1.1 and v1.2 are GitHub-only releases.
- Next work: Future tasks in `TASKS.md`

## Roadmap Summary

| Version | Focus |
|---------|-------|
| v1.1 | Store release — ZIP only (current sprint) |
| v1.2 | Shell extension (IExplorerCommand) + MOTW + file associations + hash viewer |
| v1.3 | ITarService + tar.exe integration — RAR/7z/tar extraction + capability detection |
| v1.4 | GPO/ADMX + Low IL P/Invoke sandbox + strict mode policy |
| v1.5 | TAR creation via tar.exe + additional format fixtures |

---

## Read These First

```
AGENT.md        → hard constraints, entry point
TASKS.md        → active/future tasks (v1.0 done tasks in TASKS_DONE.md)
ARCHITECTURE.md → current C# signatures — use these, do not invent
CONVENTIONS.md  → naming, async rules, error handling
SECURITY.md     → threat model (read if modifying compression logic,
                  file traversal, extraction paths, or any file I/O)
```

---

## Hard Constraints — Never Violate

- `Archiver.Core` has **zero** WinUI / Microsoft.UI references
- `Archiver.Core` has **zero** references to `ResourceLoader` or `ILogService`
- Use only `System.IO.Compression` for ZIP compression — no NuGet compression packages
- Services injected via constructor — never `new ZipArchiveService()` in ViewModels
- All IO exceptions caught per-item → `ArchiveError` — methods never throw to callers
- MVVM: no business logic in `.xaml.cs` files
- `PublishTrimmed` must be `false` for `Archiver.App` — WinUI 3 `x:Bind` generated code is not trim-compatible. Trimming silently breaks event handlers and Command bindings in Release builds.
- **tar.exe:** always use `C:\Windows\System32\tar.exe` (absolute path) — never via PATH
- **MOTW:** always propagate `Zone.Identifier` ADS on extracted files (v1.2+)
- **Shell extension:** `IExplorerCommand` only — no legacy `IContextMenu` COM shell extensions
- **Low IL sandbox:** P/Invoke is acceptable for security-critical process isolation code (v1.4)
- **Solution platforms:** x64 and ARM64 only — never add `Any CPU` or `x86` configuration entries
  to the `.sln` file. When adding a new project, mirror the `Debug|x64` / `Release|x64` entries
  from `Archiver.Shell` exactly (two lines per config, right-hand side maps to project's `Any CPU`).
- When adding or modifying tests, always run `dotnet test` with no path argument — never scope to
  a single test project. All projects must stay green after every change.
- If a change modifies a public interface, model, or contract in `Archiver.Core`, check whether
  tests in other projects (`Archiver.Shell.Tests`, future `Archiver.CLI.Tests`) need to be updated
  or extended. Internal implementation changes (private methods, buffers, sorting) require only
  `Archiver.Core.Tests` coverage.
- Prefer simple and explicit over clever and implicit. If a task can be solved with a
  straightforward script step (copy, move, delete) versus a complex MSBuild/pipeline hook, choose
  the script. Reserve MSBuild targets and build pipeline customization for cases where a script
  genuinely cannot work. This applies to all tooling decisions — not just MSBuild.

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/              ← net8.0 class library, no UI deps
│   └── Archiver.App/               ← WinUI 3 app
│       └── Strings/en-US/          ← ResW localization
├── tests/
│   ├── Archiver.Core.Tests/        ← xunit, 45 tests
│   └── Archiver.Core.Tests.GenerateFixtures/  ← fixture generator
├── CLAUDE.md                       ← you are here
├── AGENT.md
├── TASKS.md                        ← active/future tasks
├── TASKS_DONE.md                   ← completed tasks archive
├── ARCHITECTURE.md
├── CONVENTIONS.md
├── SECURITY.md
└── README.md
```

---

## Build Commands

```bash
# Run tests (always works from CLI)
dotnet test   # runs all test projects in the solution

# Build core only
dotnet build src/Archiver.Core

# Generate test fixtures
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures

# Build MSIX (requires Windows SDK)
dotnet publish src/Archiver.App/Archiver.App.csproj \
    /p:Configuration=Release /p:Platform=x64 \
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true \
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false
```

> WinUI app must be built and run from Visual Studio 2022.
> `dotnet test` and `dotnet build src/Archiver.Core` work freely from terminal.
>
> **Deploy shortcuts:**
> Release build in VS triggers `Deploy.ps1 -DeployOnly` automatically (post-build event).
> For manual deploy from terminal: `.\scripts\Deploy.ps1` (full build + sign + install)
> or `.\scripts\Deploy.ps1 -DeployOnly` (install only, no build).

---

## Key Current Signatures (quick reference)

```csharp
// IArchiveService
Task<ArchiveResult> ArchiveAsync(ArchiveOptions, IProgress<int>?, CancellationToken);
Task<ArchiveResult> ExtractAsync(ExtractOptions, IProgress<int>?, CancellationToken);

// ArchiveResult
bool Success
IReadOnlyList<string> CreatedFiles
IReadOnlyList<ArchiveError> Errors
IReadOnlyList<SkippedFile> SkippedFiles

// ILogService
void Info(string message)
void Warn(string message)
void Error(string message, Exception? ex = null)

// IDialogService
Task ShowOperationSummaryAsync(string operationName, ArchiveResult result)
Task ShowErrorAsync(string title, string message)
Task<string?> PickDestinationFolderAsync()
Task<IReadOnlyList<string>> PickFilesAsync()
Task<IReadOnlyList<string>> PickFoldersAsync()
```

---

## Do Not

- Do not re-implement anything from `TASKS_DONE.md`
- Do not add NuGet packages to `Archiver.Core` (zero dependencies)
- Do not modify `CLAUDE.md`, `AGENT.md`, `SECURITY.md` unless explicitly asked
- Do not implement features not listed in `TASKS.md` or `SPEC.md`
- Do not use `Thread.Sleep` — use `await Task.Delay` if needed
- Do not use `static` mutable fields in services
- Do not use legacy `IContextMenu` shell extension — use `IExplorerCommand`
- Do not call `tar.exe` via PATH — always absolute path `C:\Windows\System32\tar.exe`
- Do not extract tar/RAR/7z formats in-process — only via `tar.exe` subprocess

---

## Known test gaps — manual verification required

- **ProgressViewModel (Archiver.ProgressWindow)** — named pipe state machine,
  JSON message dispatch, and UI lifecycle are not covered by automated tests.
  Manual verification required: progress updates render correctly, Cancel signal
  propagates, auto-close triggers after 1.5 s on success, error dialog appears
  and stays open on failure.

---

## Workflow Tips

- For complex tasks (architecture changes, new services, multi-file refactoring)
  use Plan Mode before writing any code — activate with /plan in Claude Code.
