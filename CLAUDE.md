# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows with an in-progress shell extension (IExplorerCommand) and planned tar.exe integration for RAR/7z/tar extraction.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

**v1.1 complete** — tagged `v1.1.0`. GitHub-only release for early testers.
**v1.2 (shell extension) in progress** — Archiver.Shell, protocol activation, file association,
and MOTW are complete; `IExplorerCommand` COM DLL (T-F61) is complete. Progress UI is shown via
the Windows Shell's built-in `IProgressDialog` (see `Archiver.Shell/NativeProgressDialog.cs`) —
the earlier `Archiver.ProgressWindow` satellite WinUI 3 app was removed (T-F65; see
`DECISIONS.md`). Both T-F61 and T-F65 manually smoke-tested end-to-end and confirmed 2026-07-05.
T-F62 (Test archive) is complete — manually smoke-tested in Explorer and confirmed 2026-07-06.
T-F68 (shell extract silently ignoring `SkippedFiles`) and T-F63 (Extract…/Compress… dialogs) are
both complete — manually smoke-tested and confirmed 2026-07-06. All planned v1.2 shell-extension
work is now done. T-F63's manual test surfaced T-F83 — a cold-start protocol/file activation bug
in `Archiver.App` (pre-dating T-F63, in already-shipped T-F44/T-F56 code) — fixed the same day;
see `DECISIONS.md`. T-F83's own last criterion (reverifying T-F44's file-activation cold-start
claim on a machine where Pakko owns the `.zip` association) remains open.
- T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39/T-F44/T-F45 complete
- 135/135 .NET tests pass (`dotnet test --filter "Category!=Slow"`: 99 Archiver.Core.Tests +
  36 Archiver.Shell.Tests). 3 additional Zip64 tests (T-F20) are tagged `[Trait("Category",
  "Slow")]` and excluded from this default run — they cost real wall-clock time (>65535-file
  archiving/extraction, a >4 GiB round trip) that isn't worth paying on every change; run them
  explicitly with `dotnet test --filter "Category=Slow"` before a release or when touching
  Zip64-adjacent code. C++ `Archiver.ShellExtension.Tests` (Google Test, 33/33) run separately,
  not covered by `dotnet test`
- MSIX signed with dev cert via Deploy.ps1 (see T-F10 for production-grade cert)
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

Version-to-focus table: see `SPEC.md`'s "Future Roadmap" section (the sole owner, per T-F72 —
`README.md`'s roadmap links there too now). Per-version completion detail beyond a one-line scope
description lives in this file's "Current State" section above instead of a second table.

---

## Documentation Map

**This is the single index for every doc in the repo.** An earlier `AGENT.md` was a second,
competing entry point (its own "Read Order", its own stale hard-constraints subset) — it was
deleted 2026-07-05 once this map fully absorbed its role (see git history if you need it).
`BOOTSTRAP.md` was deleted the same day — its content is now the "Dependency Injection &
Startup" section of `ARCHITECTURE.md` (it had drifted into a near-duplicate of a section
`ARCHITECTURE.md` already had). Do not create a third map file or a new DI-wiring file; extend
this table and its owners instead.

| File | Purpose | Read when | Update when |
|---|---|---|---|
| **CLAUDE.md** (here) | Session context, hard constraints, build commands, this map | Every session (auto-loaded) | Project status changes, a hard constraint changes, build/deploy commands change |
| `TASKS.md` | Active/future task backlog, acceptance criteria, `T-Fxx` numbering | Starting any implementation task | A task starts/completes/changes scope; a new `T-Fxx` is claimed |
| `TASKS_DONE.md` | Archive of completed v1.0 tasks | Need historical task detail | Never — append-only via tasks graduating out of `TASKS.md` |
| `ARCHITECTURE.md` | Current C# layer diagram + signatures + DI wiring/startup | Before writing code that touches a public signature or a DI-registered service | A public signature/model/interface in `Archiver.Core` changes, or DI registration/lifetime changes |
| `XAML.md` | Current `MainWindow.xaml` structure + WinUI 3 gotchas | Touching `Archiver.App`'s XAML | XAML structure changes, a new WinUI 3 constraint is discovered |
| `CONVENTIONS.md` | Coding style, naming, async, error-handling, per-project package whitelist | Before writing any code | A new convention is adopted, or a code example goes stale |
| `SECURITY.md` | Threat model — **canonical owner of all security/CVE/supply-chain/MOTW rationale** | Modifying compression, traversal, or extraction logic | Threat model changes, a new mitigation is added |
| `DECISIONS.md` | Architectural decisions + rejected approaches, with root-cause detail | Before implementing packaging, COM, or shell integration | An approach is chosen, rejected, or corrected |
| `DIAGRAMS.md` | Required sequence/state/activity/component diagrams, Ground Truth Rule | Touching COM/shell, operation lifecycle, `ZipArchiveService` branching, or the manifest | Per its own DoD table — same commit as the code |
| `TESTING.md` | Test plan and fixture inventory for `Archiver.Core` | Writing or running tests | New test category, fixture, or test count changes |
| `tests/Archiver.Core.Tests.GenerateFixtures/README.md` | Fixture-generation mechanics only (subordinate to `TESTING.md`) | Adding a fixture-dependent test | A new fixture scenario is added |
| `SPEC.md` | Product specification — **canonical owner of the version roadmap table, feature scope, non-goals** | Scoping a new feature, checking what's out of scope | Scope or roadmap changes |
| `README.md` | Public GitHub landing page | User-facing — not an agent instruction source | Public messaging changes; must link to `SECURITY.md`/`SPEC.md`, never restate their tables |
| `CONTRIBUTING.md` | Contributor onboarding summary | Before a contributor's first build | Build/deploy steps change — update `scripts/README.md` first, then sync the summary here |
| `scripts/README.md` | **Canonical owner of build/sign/deploy steps** (`Deploy.ps1`, `Setup-DevCert.ps1`) | Running or changing the deploy scripts | `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes |

**Canonical topic owners — do not duplicate, link instead:**
- Security/threat-model/CVE/supply-chain rationale → `SECURITY.md` only. `SPEC.md`/`README.md` keep at most a 2-line teaser with a link.
- Version roadmap table → `SPEC.md` only. `CLAUDE.md`/`README.md`/`TASKS.md` reference it by version number instead of repeating the table (existing duplicates tracked as `T-F72`).
- Build/sign/deploy steps → `scripts/README.md` only. `CONTRIBUTING.md` and this file's "Build Commands" section link to it rather than repeating steps.
- Hard constraints → `CLAUDE.md` (this file) only — the richest and most current copy.
- Current C# signatures and DI wiring → `ARCHITECTURE.md` only (stale signature there tracked as `T-F73`).

If you're updating a doc and find yourself retyping a table that already exists elsewhere in
this list, stop — link to the canonical owner instead. If no owner is obvious for a new topic,
ask before creating a new file.

### Update Cascades

Some changes ripple beyond their primary doc. After updating the primary doc for a change below,
check whether the cascade docs still agree with it — don't let them silently drift (this is how
the `com:InProcessServer`/`com:SurrogateServer` drift and the `ARCHITECTURE.md`/`BOOTSTRAP.md`
DI duplication happened).

| Change | Primary doc | Cascade — check these too |
|---|---|---|
| Public signature/model change in `Archiver.Core` | `ARCHITECTURE.md` | `CONVENTIONS.md` (XML-doc example), `TASKS.md` (mark task done) |
| DI registration or lifetime change | `ARCHITECTURE.md` | — (single owner now, no cascade) |
| `MainWindow.xaml` structure or new WinUI 3 gotcha | `XAML.md` | — (leaf doc) |
| New coding convention adopted | `CONVENTIONS.md` | — |
| Threat model or mitigation changes | `SECURITY.md` | `SPEC.md` (teaser), `README.md` (teaser) |
| Approach chosen/rejected/corrected (COM, packaging, shell) | `DECISIONS.md` | `ARCHITECTURE.md`, `CLAUDE.md` (hard constraints), `scripts/README.md`, `DIAGRAMS.md` |
| Task starts/completes, or a new `T-Fxx` is claimed | `TASKS.md` | `TASKS_DONE.md` (graduation on completion), `CLAUDE.md` (Current State), `README.md` (Project Status) |
| Version scope/roadmap changes | `SPEC.md` | `CLAUDE.md` (Roadmap Summary), `README.md` (Roadmap) |
| `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes | `scripts/README.md` | `CONTRIBUTING.md`, `README.md` (Building and Deploying), `CLAUDE.md` (Build Commands) |
| COM/shell, operation lifecycle, `ZipArchiveService` branching, or manifest changes | `DIAGRAMS.md` | Per its own DoD table |
| New test or fixture added | `TESTING.md` | `tests/Archiver.Core.Tests.GenerateFixtures/README.md`, `CONTRIBUTING.md` |

Before deleting or merging any `.md` file, grep the whole repo for its filename first — dead
references are easy to miss otherwise (this session found 5 lingering mentions of `AGENT.md`/
`BOOTSTRAP.md` after removing them).

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
- **Context-menu ordering:** primary action commands (Extract/Archive) always precede
  diagnostic/verification ones (Test archive) in `PakkoRootCommand::EnumSubCommands` —
  deliberate deviation from NanaZip's Test-first order. See `DECISIONS.md`'s
  "Test Archive (T-F62)" entry before copying NanaZip's menu order for a new command.
- **COM HRESULTs:** never return `S_FALSE` alongside a null/unset out-parameter — `S_FALSE` is a
  *success* code (`SUCCEEDED()` is true), so callers checking only `SUCCEEDED()` will dereference
  the null. Use `E_NOTIMPL` instead (verified against Microsoft's own `IExplorerCommand` sample).
- **.NET COM interop (`[ComImport]` interfaces consuming external COM objects):** check the real
  SDK header before declaring the interface — if a method returns a plain type (e.g. `BOOL`)
  instead of `HRESULT`, mark it `[PreserveSig]`. Without it, the marshaller assumes the
  HRESULT + hidden-`[out]`-param convention and silently misreads the return value. Real bug:
  `IProgressDialog.HasUserCancelled` always read back `false` (Cancel appeared to do nothing)
  until `[PreserveSig]` was added — see `Archiver.Shell/NativeProgressDialog.cs`.
- **Low IL sandbox:** P/Invoke is acceptable for security-critical process isolation code (v1.4)
- **Solution platforms:** x64 and ARM64 only — never add `Any CPU` or `x86` configuration entries
  to the `.sln` file. When adding a new project, mirror the `Debug|x64` / `Release|x64` entries
  from `Archiver.Shell` exactly (two lines per config, right-hand side maps to project's `Any CPU`).
- When adding or modifying tests, always run `dotnet test --filter "Category!=Slow"` with no path
  argument — never scope to a single test project. All projects must stay green after every
  change. The `Category!=Slow` filter excludes T-F20's Zip64 tests (real multi-second/multi-GB
  cost); run `dotnet test --filter "Category=Slow"` too before a release or when the change
  touches Zip64-adjacent code (entry counts, large files, Zip64 boundary conditions).
- If a change modifies a public interface, model, or contract in `Archiver.Core`, check whether
  tests in other projects (`Archiver.Shell.Tests`, future `Archiver.CLI.Tests`) need to be updated
  or extended. Internal implementation changes (private methods, buffers, sorting) require only
  `Archiver.Core.Tests` coverage.
- Prefer simple and explicit over clever and implicit. If a task can be solved with a
  straightforward script step (copy, move, delete) versus a complex MSBuild/pipeline hook, choose
  the script. Reserve MSBuild targets and build pipeline customization for cases where a script
  genuinely cannot work. This applies to all tooling decisions — not just MSBuild.
- **MSIX packaging:** never use `BeforeTargets` hooks or manual `MakeAppx` calls to inject files
  into packages. Use `Content Include` items in `.csproj` with `CopyToOutputDirectory` — this is
  the only reliable approach that survives incremental builds. `dotnet publish` with
  `AppxPackageSigningEnabled=true` is the only confirmed working signing method; manual
  `SignTool` calls fail on MSIX because `New-SelfSignedCertificate` generates CNG keys on modern
  Windows and SignTool cannot use CNG keys to sign MSIX directly.
- **3-attempt rule:** if the same problem persists after 3 different implementation attempts,
  stop immediately. Report what was tried, what failed, and what is unknown. Do not attempt a
  4th approach without explicit direction. This applies especially to build tooling, packaging,
  and signing issues.
- **Pre-implementation research:** for tasks involving COM interop, shell integration, or Windows
  packaging — always research existing working examples before writing any code. "Check NanaZip"
  means fetch the actual shipped source (github.com/M2Team/NanaZip, e.g.
  `NanaZipPackage/Package.appxmanifest`) and quote/compare its real XML or code — not a
  description from memory or search-result summaries. A manifest schema that merely looks
  plausible is not enough; verify it against a working reference before writing it. Also check
  Windows Community Toolkit and Microsoft docs. Document findings in `DECISIONS.md` before
  implementing. (The `com:InProcessServer` schema in the original T-F61 decision was never
  actually verified this way and shipped with an undeclared XML namespace for ~4 months before
  being caught — see the "Correction — SurrogateServer" entry in `DECISIONS.md`.)
  `gh` CLI is not installed in this environment, and GitHub's code search requires sign-in even
  for public repos. Instead: `curl -s "https://api.github.com/repos/<owner>/<repo>/git/trees/main?recursive=1"`
  lists every file path unauthenticated — grep it for the area you need, then WebFetch the raw
  file (`raw.githubusercontent.com/<owner>/<repo>/main/<path>`) to read real code.
- Before tagging an ad-hoc fix with a new `T-Fxx` comment/reference, grep the highest existing
  number **across the entire repo**, not just `TASKS.md`/`TASKS_DONE.md`/`CLAUDE.md`/
  `DECISIONS.md` — don't guess a number. Some `T-Fxx` tags exist only as code comments with no
  `TASKS.md` entry (e.g. `T-F66` in `ZipArchiveService.cs`, `T-F67` in `Program.cs`); a
  markdown-only grep misses them and risks a collision. `T-F62`/`T-F63` are already claimed by
  *different* future tasks in `TASKS.md`; reusing them for an unrelated fix creates a lasting
  mismatch between code comments and the task log.
- `ConflictBehavior.Rename` on `ZipArchiveService.ExtractAsync` means **per-file rename inside a
  merged existing folder** (the GUI app's tested behavior) — it does NOT mean "always create a
  fresh whole folder." For shell-only "always fresh" behavior (numbered folder), use
  `ExtractOptions.SeparateFolderName` computed by the caller instead of changing this semantic.
- **Context-menu flicker on first open of a new Explorer window** (e.g. showing a stale/other
  entry before repainting to Pakko's) is a known Explorer verb/icon-cache artifact, not a
  Pakko code bug — Explorer caches top-level shell-extension verbs across COM DLL
  (re)registrations until it requeries `GetTitle`/`GetIcon`. Don't chase this with code changes
  without first confirming the cache-artifact explanation is wrong.

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/              ← net8.0 class library, no UI deps
│   ├── Archiver.App/               ← WinUI 3 app
│   │   └── Strings/en-US/          ← ResW localization
│   ├── Archiver.Shell/             ← net8.0-windows WinExe, shell-triggered ops, no WinUI
│   │   └── NativeProgressDialog.cs ← IProgressDialog COM interop (in-process progress UI)
│   └── Archiver.ShellExtension/    ← C++ COM DLL, IExplorerCommand (T-F61), x64+ARM64
├── tests/
│   ├── Archiver.Core.Tests/        ← xunit, 70 tests
│   ├── Archiver.Shell.Tests/       ← xunit, 25 tests
│   ├── Archiver.ShellExtension.Tests/  ← C++ Google Test, run separately (see Build Commands)
│   └── Archiver.Core.Tests.GenerateFixtures/  ← fixture generator
├── CLAUDE.md                       ← you are here
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
dotnet test --filter "Category!=Slow"   # runs all test projects, skips T-F20's Zip64 Slow tests
dotnet test --filter "Category=Slow"    # Zip64 tests only — real multi-second/multi-GB cost

# Build core only
dotnet build src/Archiver.Core

# Generate test fixtures
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures

# Build MSIX (requires Windows SDK)
dotnet publish src/Archiver.App/Archiver.App.csproj \
    /p:Configuration=Release /p:Platform=x64 \
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true \
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

# Archiver.ShellExtension (C++ COM DLL) — not built or tested by dotnet build/test
# Build via Visual Studio / MSBuild (x64 or ARM64 platform).
# Any dotnet build/publish/test command with /p:Key=Value flags must run via the PowerShell
# tool, not Bash — Bash (Git Bash/MSYS) mangles "/p:" into a path-like token, failing with
# "MSB1008: Only one project can be specified."
# First-time test project setup:
nuget restore tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj -SolutionDirectory .
# Build directly (NOT via .sln — .sln + /t:<ProjectName> applies that target to every project).
# $(SolutionDir) is only auto-set when building through the .sln, so pass it explicitly:
MSBuild tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj /p:SolutionDir=<repo-root>\ /p:Configuration=Debug /p:Platform=x64
# If MSBuild.exe isn't on PATH, locate it with vswhere — use the PowerShell tool for this,
# not Bash: Bash strips backslashes from patterns like "MSBuild\**\Bin\MSBuild.exe".
# Then run: tests\Archiver.ShellExtension.Tests\bin\x64\Debug\Archiver.ShellExtension.Tests.exe
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
- Do not modify `CLAUDE.md`, `SECURITY.md` unless explicitly asked
- Do not implement features not listed in `TASKS.md` or `SPEC.md`
- Do not use `Thread.Sleep` — use `await Task.Delay` if needed
- Do not use `static` mutable fields in services
- Do not use legacy `IContextMenu` shell extension — use `IExplorerCommand`
- Do not call `tar.exe` via PATH — always absolute path `C:\Windows\System32\tar.exe`
- Do not extract tar/RAR/7z formats in-process — only via `tar.exe` subprocess

---

## Known test gaps — manual verification required

- **NativeProgressDialog (Archiver.Shell)** — the `IProgressDialog` COM wrapper is not covered
  by automated tests (COM UI object, not unit-testable). Manual verification required: progress
  bar and status line update during Extract/Archive, Cancel button stops the operation.

---

## Windows Packaging Best Practices

Lessons learned during v1.2 MSIX packaging work — follow these to avoid known failure modes:

- **Satellite EXEs** are included via `Content Include` in `Archiver.App.csproj` with
  `Condition="'$(GenerateAppxPackageOnBuild)'=='true'"` — invisible to normal VS builds,
  activated only during `Deploy.ps1` packaging.
- **Never sign MSIX manually** with `SignTool` — use `AppxPackageSigningEnabled=true` and
  `PackageCertificateThumbprint` in `dotnet publish` instead. The SDK's built-in pipeline
  handles MSIX format requirements correctly; direct SignTool calls produce `ERROR_BAD_FORMAT`.
- **`New-SelfSignedCertificate` generates CNG keys** by default on modern Windows. SignTool
  cannot use CNG keys for MSIX signing. Always pass
  `-Provider "Microsoft Strong Cryptographic Provider"` to force CryptoAPI (RSA 2048).
- **`.wapproj` does not work** for projects with multiple WinUI 3 apps. The DesktopBridge
  targets generate PRI resources for each WinUI app (producing duplicate `Files/App.xbf`
  entries) — this conflict cannot be resolved within the `.wapproj` model.
- **`BeforeTargets` hooks are fragile** — the correct MSBuild hook point (`_CreateAppxPackage`,
  `_GenerateAppxUploadPackageFile`, etc.) changes across SDK versions and `dotnet publish` vs
  VS build contexts. Use `Content Include` instead.
- **Any EXE launched via `CreateProcess` from outside its own package must be declared as its
  own `<Application>` in `Package.appxmanifest`** (use `EntryPoint="Windows.FullTrustApplication"`,
  `AppListEntry="none"` to hide it). Otherwise Windows returns `ERROR_ACCESS_DENIED` — confirmed
  via `microsoft/WindowsAppSDK#4651`. This applies to every satellite EXE the shell extension or
  any other external process spawns (e.g. `Archiver.Shell.exe`).
- **Satellite EXEs must be built self-contained**, not framework-dependent (`--self-contained` in
  `Deploy.ps1`, not `--no-self-contained`). A framework-dependent apphost inside an MSIX package
  has no system runtime to fall back on and fails with a modal ".NET not found" dialog that never
  surfaces to an automated caller — looks exactly like "nothing happens." A self-contained apphost
  additionally needs its own `.dll`/`.deps.json`/`.runtimeconfig.json` shipped via `Content Include`
  alongside the `.exe` — the bare `.exe` alone is not enough.
- **A COM surrogate (`dllhost.exe`) hosting `Archiver.ShellExtension.dll` can lock the DLL/PDB**
  after testing the context menu, causing `C1041`/file-in-use errors on the next rebuild. Run
  `taskkill /F /IM dllhost.exe` (or find the specific PID) before rebuilding if this happens.
- **To verify a shell-triggered EXE actually runs** (Explorer/COM invocation can't be scripted):
  launch it directly the same way the COM caller would (`Start-Process <path> -ArgumentList ...`)
  and check `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'}`
  for silent apphost failures — these never produce console output or a visible error otherwise.
  For a *native* crash (WinUI/WindowsAppRuntime init failure, access violation, etc.) instead
  check `ProviderName='Application Error'` — these show as event ID 1000 with the faulting
  module/offset/exception code and never appear under the `.NET Runtime` provider at all.

---

## Deployment

- `Deploy.ps1` automatically increments the last segment of the `Version` attribute in
  `src/Archiver.App/Package.appxmanifest` after every successful build+install (not in
  `-DeployOnly` mode, which reinstalls an already-built package). No manual bump needed.
  Pass `-SkipVersionBump` to suppress this for a given run.
- The version format is `1.1.0.X` — only the last segment changes.
  Example: `1.1.0.3` → `1.1.0.4`.
- Do not change the first three segments unless explicitly instructed.
- If bumping manually (e.g. outside `Deploy.ps1`), only edit the `Version` attribute on
  `<Identity>` — do not touch `MinVersion`/`MaxVersionTested` on `TargetDeviceFamily`.
- Full build+sign+install command (user's dev cert thumbprint):
  ```powershell
  .\scripts\Deploy.ps1 -Thumbprint "D2EC5F2C451ED0EBE94B8168A68E5B813954CC75"
  ```

---

## Workflow Tips

- For complex tasks (architecture changes, new services, multi-file refactoring)
  use Plan Mode before writing any code — activate with /plan in Claude Code.
- **Before committing any task marked complete or partial:** run the full
  `.\scripts\Deploy.ps1 -Thumbprint "D2EC5F2C451ED0EBE94B8168A68E5B813954CC75"` build+sign+install, and
  ask the user to do the manual on-device verification (context menu, extraction, etc.) before
  the commit. Don't commit a task as done/partial on the strength of `dotnet test` /
  `Archiver.ShellExtension.Tests.exe` alone if it touches shell-triggered or UI behavior.
