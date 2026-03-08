# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

**v1.0 complete** — tagged `v1.0.0`.
- T-01 through T-35 + T-11 all complete and committed
- 45/45 tests pass
- MSIX builds unsigned (see T-F10 for signing)
- Next work: Future tasks in `TASKS.md`

---

## Read These First

```
AGENT.md        → hard constraints, entry point
TASKS.md        → active/future tasks (v1.0 done tasks in TASKS_DONE.md)
ARCHITECTURE.md → current C# signatures — use these, do not invent
CONVENTIONS.md  → naming, async rules, error handling
SECURITY.md     → threat model (read if modifying compression logic)
```

---

## Hard Constraints — Never Violate

- `Archiver.Core` has **zero** WinUI / Microsoft.UI references
- `Archiver.Core` has **zero** references to `ResourceLoader` or `ILogService`
- Use only `System.IO.Compression` for compression — no NuGet compression packages
- Services injected via constructor — never `new ZipArchiveService()` in ViewModels
- All IO exceptions caught per-item → `ArchiveError` — methods never throw to callers
- MVVM: no business logic in `.xaml.cs` files
- `PublishTrimmed` must be `false` for `Archiver.App` — WinUI 3 `x:Bind` generated code is not trim-compatible. Trimming silently breaks event handlers and Command bindings in Release builds.

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
dotnet test tests/Archiver.Core.Tests

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
IReadOnlyList<string> Warnings          // SHA-256 mismatches (T-34)

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
