# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.
Do not delete or rename this file.

---

## Project

**Windows Archiver Wrapper** — WinUI 3 desktop app.  
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.

---

## Read These First

Before touching any code, read in this order:

```
AGENT.md        → entry point, hard constraints, bootstrap commands
SPEC.md         → what to build, security rationale
ARCHITECTURE.md → layer contracts, exact C# signatures (use these, do not invent)
BOOTSTRAP.md    → DI wiring in App.xaml.cs (do not use `new` for services)
XAML.md         → MainWindow skeleton (implement as-is)
TASKS.md        → tasks with acceptance criteria (check before starting)
CONVENTIONS.md  → naming, async rules, error handling patterns
TESTING.md      → test project setup and all test cases
SECURITY.md     → threat model (read if modifying compression logic)
```

---

## Hard Constraints — Never Violate

- `Archiver.Core` has **zero** WinUI / Microsoft.UI references
- Use only `System.IO.Compression` for compression — no NuGet compression packages
- Services injected via constructor — never `new ZipArchiveService()` in ViewModels
- All IO exceptions caught per-item → `ArchiveError` — methods never throw to callers
- MVVM: no business logic in `.xaml.cs` files

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/          ← net8.0 class library, no UI deps
│   └── Archiver.App/           ← WinUI 3 app
├── tests/
│   └── Archiver.Core.Tests/    ← xunit
├── docs/
├── CLAUDE.md                   ← you are here
├── AGENT.md
├── SPEC.md
├── ARCHITECTURE.md
├── BOOTSTRAP.md
├── XAML.md
├── TASKS.md
├── CONVENTIONS.md
├── TESTING.md
├── SECURITY.md
├── README.md
└── windows-archiver-wrapper.sln
```

---

## Build Commands

```bash
# Build entire solution
dotnet build

# Build core only
dotnet build src/Archiver.Core

# Run tests
dotnet test tests/Archiver.Core.Tests

# Check for warnings (treat as errors in CI)
dotnet build -warnaserror
```

> WinUI app (Archiver.App) must be built and run from Visual Studio 2022.
> `dotnet build` on Archiver.App from CLI may fail — this is expected for WinUI projects.
> Use `dotnet build src/Archiver.Core` and `dotnet test` freely from terminal.

---

## Current Task Status

Check `TASKS.md` for up-to-date status.  
When starting work: read the task, check its acceptance criteria, implement, mark `[x]`.

---

## Workflow for Each Task

```
1. cat TASKS.md                          → find next pending task
2. cat ARCHITECTURE.md                   → check relevant signatures
3. Implement the file(s) listed in task
4. dotnet build src/Archiver.Core        → verify no errors
5. dotnet test tests/Archiver.Core.Tests → verify tests pass (if applicable)
6. Update TASKS.md — mark task [x]
```

---

## Key Interfaces (quick reference)

```csharp
// IArchiveService — Archiver.Core/Interfaces/IArchiveService.cs
Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

// IDialogService — Archiver.App/Services/IDialogService.cs
Task ShowErrorAsync(string title, string message);
Task<bool> ShowConfirmAsync(string title, string message);
Task<string?> PickDestinationFolderAsync();
Task<IReadOnlyList<string>> PickFilesAsync();
```

---

## Do Not

- Do not create `src/Archiver.Packaging` until T-11 is reached
- Do not add NuGet packages to `Archiver.Core` (zero dependencies)
- Do not modify `CLAUDE.md`, `AGENT.md`, `SECURITY.md` unless explicitly asked
- Do not implement features not listed in `SPEC.md` (no encryption, no RAR, no shell extensions)
- Do not use `Thread.Sleep` — use `await Task.Delay` if needed
- Do not use `static` fields in services — they are registered as singletons, state must be explicit
