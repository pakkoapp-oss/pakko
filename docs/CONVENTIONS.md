# CONVENTIONS.md — Coding Conventions

AI agents must follow these rules in all generated code.

---

## C# Language Rules

| Rule | Value |
|------|-------|
| Language version | C# 12 |
| Nullable reference types | `enable` (all projects) |
| Implicit usings | `enable` |
| File-scoped namespaces | Required |
| `var` | Use only when type is obvious from right-hand side |
| String interpolation | Prefer over `string.Format` |
| `record` vs `class` | Use `record` for data-only types (models, options) |
| `sealed` | Apply to all classes not designed for inheritance |

---

## Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Namespace | `PascalCase` | `Archiver.Core.Models` |
| Class / Record | `PascalCase` | `ZipArchiveService` |
| Interface | `IPascalCase` | `IArchiveService` |
| Public property | `PascalCase` | `SourcePaths` |
| Private field | `_camelCase` | `_archiveService` |
| Method | `PascalCase` | `ArchiveAsync` |
| Local variable | `camelCase` | `archivePath` |
| Async method | Suffix `Async` | `ExtractAsync` |
| Enum | `PascalCase` | `ArchiveMode.SingleArchive` |

---

## Method Complexity & Parameter Count

- Keep cognitive complexity ≤ 15 (SonarCloud's `S3776` threshold) and parameter count ≤ 7
  (`S107`) for new/touched methods. When a method drifts past either, extract before it grows
  further — see T-F147's actual before/after examples across `ZipArchiveService.cs`/
  `TarSandboxedService.cs`/`Archiver.CLI`'s `CliArgumentParser.cs` for the real patterns used:
  - **A linear sequence of independent checks** (magic-byte signatures, per-switch-token
    validation) → one small predicate/`TryParseXxx` method per check, or a table-driven dispatch
    (`ArchiveFormatDetector.Detect`, `CliArgumentParser.UnsupportedSwitchReason`).
  - **A per-item loop body with several skip-gates** → extract the loop body into its own
    `TryXxxAsync` helper returning what the caller needs to keep accumulating (extracted/bytes
    consumed/etc.), not a `ref`/`out` parameter on an `async` method (C# disallows those) —
    return a tuple instead (`ZipArchiveService.TryExtractSingleEntryAsync`).
  - **Too many parameters that cluster by purpose** → bundle into small, purpose-specific
    `sealed record`s (a report-sink record, a progress-context record), not one large catch-all
    options bag — matches `Zip/ParallelSingleArchiveWriter.cs`'s own existing style of several
    small typed pieces (`FileWorkItem`, `WorkResult`) over a single mega-type.
  - **Two call sites with the identical block** (a conflict-resolution `switch`, a temp-file
    delete-on-error `try/catch`) → extract once, reuse — this alone often resolves the complexity
    finding as a side effect of removing the duplication.
- **When a method's complexity is provably load-bearing, don't force a risky split for the
  metric.** If every branch exists because of a specific, already-debugged race or OS-lifecycle
  requirement (documented inline — see `SandboxedProcessLauncher.RunAsync`'s `CreateProcessW`→
  `AssignProcessToJobObject`→`ResumeThread` sequence, or `ParallelSingleArchiveWriter.
  RunPipelineAsync`'s producer/consumer/ordered-cleanup `finally`), extract only the genuinely
  boundary-safe pieces and leave the core sequence as one unit. Add
  `// NOSONAR: S3776 — <specific reason, pointing at the inline race/lifecycle comment>` on the
  residual finding rather than restructuring code whose current shape is the fix for a real,
  previously-found bug. This is the same "provable from the line itself" standard this file
  already holds bounds/security checks to (see `CLAUDE.md`'s Hard Constraints).

---

## Async Rules

- All IO operations must be `async/await`
- Never use `.Result` or `.Wait()` on tasks
- Always accept `CancellationToken` in public async methods
- Always name the parameter `cancellationToken` (not `ct` or `token`)
- Use `ConfigureAwait(false)` in `Archiver.Core` (no UI context needed)

```csharp
// Correct
await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);

// Wrong
File.WriteAllBytes(path, data);
```

---

## Error Handling Rules

- `Archiver.Core` services must **never throw** to callers
- Catch `Exception` only at service boundaries, not inside helpers
- All exceptions → `ArchiveError` with `SourcePath`, `Message`, `Exception`
- Log-friendly: `ArchiveError.Message` must be human-readable (not exception type name)

```csharp
// Correct pattern in ZipArchiveService
try
{
    // ... operation
}
catch (IOException ex)
{
    errors.Add(new ArchiveError
    {
        SourcePath = sourcePath,
        Message = $"Cannot access file: {ex.Message}",
        Exception = ex
    });
}
```

---

## MVVM Rules

- ViewModels do **not** reference WinUI controls directly
- ViewModels do **not** use `Dispatcher` — use `ObservableCollection` thread-safe updates
- Code-behind (`.xaml.cs`) contains only:
  - Constructor with `InitializeComponent()`
  - Event handlers that immediately delegate to ViewModel
  - Drag-and-drop wiring
- Services injected via constructor (no service locator, no static access)

```csharp
// Correct ViewModel constructor — real current shape, not a simplified example. MainViewModel
// depends on the routers (IArchiveCreationRouter/IExtractionRouter/IArchiveListingRouter), never
// IArchiveService/ITarService directly — routing by detected/requested format is the router's job.
public MainViewModel(
    IArchiveCreationRouter archiveCreationRouter,
    IExtractionRouter extractionRouter,
    IArchiveListingRouter archiveListingRouter,
    IDialogService dialogService,
    ILogService logService)
{
    _archiveCreationRouter = archiveCreationRouter;
    _extractionRouter = extractionRouter;
    _archiveListingRouter = archiveListingRouter;
    _dialogService = dialogService;
    _logService = logService;
}

// Wrong
public MainViewModel()
{
    _extractionRouter = new ExtractionRouter(...); // hard dependency
}
```

---

## File Organization

- One type per file
- File name matches type name exactly: `ZipArchiveService.cs` contains `ZipArchiveService`
- No partial classes unless required by WinUI XAML code-behind
- `using` directives: system namespaces first, then project namespaces, no blank lines between

---

## XML Documentation

Required on:
- All `public` interfaces
- All `public` service classes
- All `public` methods in `Archiver.Core`

Not required on:
- `private` members
- ViewModels (UI layer)
- Models (self-documenting by property names)

```csharp
/// <summary>
/// Creates ZIP archives from the provided options.
/// </summary>
/// <param name="options">Archive configuration including source paths and destination.</param>
/// <param name="progress">Optional progress reporter (percent, bytes transferred/total, current file).</param>
/// <param name="cancellationToken">Token to cancel the operation between items.</param>
/// <returns>Result containing created file paths and any per-item errors.</returns>
Task<ArchiveResult> ArchiveAsync(
    ArchiveOptions options,
    IProgress<ProgressReport>? progress = null,
    CancellationToken cancellationToken = default);
```

---

## .editorconfig

Place this file at the repository root:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
indent_style = space
indent_size = 4
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
# Namespace style
csharp_style_namespace_declarations = file_scoped:error

# var preferences
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = false:silent
csharp_style_expression_bodied_properties = true:suggestion

# Null checking
dotnet_style_null_propagation = true:suggestion
dotnet_style_coalesce_expression = true:suggestion

# Using directives
csharp_using_directive_placement = outside_namespace:error

# Modifier order
csharp_preferred_modifier_order = public,private,protected,internal,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,volatile,async:suggestion

# Naming rules
dotnet_naming_rule.private_fields_should_be_camel_case.severity = warning
dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore_prefix

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore_prefix.capitalization = camel_case
dotnet_naming_style.camel_case_underscore_prefix.required_prefix = _

[*.xaml]
indent_size = 4

[*.md]
trim_trailing_whitespace = false

[*.json]
indent_size = 2

[*.{csproj,props,targets}]
indent_size = 2
```

---

## Archiver.Shell Conventions

### ShellArgumentParser — validation boundary

`ShellArgumentParser` validates only command structure (command name and minimum
argument count). It never validates file path content, existence, or format.
Path validation is the responsibility of dispatch logic in `Program.cs` and
downstream service calls. Do not add path content checks to `ShellArgumentParser`.

---

## C++ Language Rules (Archiver.ShellExtension)

- C++17 (`stdcpp17`), MSVC only, `v143` toolset — no portability requirement, this DLL is
  Windows-only COM.
- RAII everywhere. No manual `new`/`delete`, no owning raw pointers. COM objects are created
  via `Microsoft::WRL::Make<T>()` and held in `ComPtr<T>` — never a raw `IUnknown*` with manual
  `AddRef`/`Release`.
- Every COM interface method implementation is `noexcept override` (see `ExplorerCommands.h`).
  COM methods must never throw across the ABI boundary — catch internally, return an `HRESULT`.
- Naming: members `m_camelCase`, free functions `PascalCase` (matches `ShellExtUtils.h`), classes
  `PascalCase` with `final` when not designed for inheritance (matches `SubCommandEnum`,
  `ExtractHereCommand`, etc.).
- COM interface parameter names follow the Windows SDK signature exactly (`psia`, `ppszName`,
  `pCmdState`, ...) even though they read like Hungarian notation — do not rename them; they are
  copy-pasted from `shobjidl_core.h` and renaming makes cross-referencing MSDN/Explorer-sample
  code harder.
- No template metaprogramming, SFINAE, or concepts — the DLL is small and fixed-shape; none of
  that machinery is needed here.
- Prefer `std::vector` + linear scan for the small collections this DLL deals with (path lists,
  sub-command lists — always a handful of items). Don't reach for `unordered_map`/`set` at this
  scale.
- Keep COM-free logic (path/arg building, `.zip` classification) as free functions in
  `ShellExtUtils.cpp`/`.h`, testable without loading the DLL or touching COM — this split already
  exists and must be preserved so `ShellExtUtilsTests.cpp` keeps running without a COM apartment.
- Comment WHY, not WHAT (same rule as C#) — e.g. the existing comment on `BuildExtractHereArgs`
  explaining why no path escaping is needed (`"` is invalid in NTFS filenames).
- **Never write a literal non-ASCII character (`…`, `—`, Cyrillic, etc.) directly in a string
  literal.** Always use the `\uXXXX` escape (e.g. `L"…"` for the ellipsis). Reason: a `.cpp`
  file saved as UTF-8 **without a BOM** gets decoded by MSVC using the *active system code page*
  (e.g. Windows-1251 on a Ukrainian-locale machine), not UTF-8 — the multi-byte UTF-8 sequence
  then gets split into garbage individual characters at compile time (a real bug found in
  `ShellExtUtils.cpp`: `L"…"` compiled into `вЂ¦`; see T-F64 in `TASKS.md`). `\uXXXX` escapes are
  pure ASCII in the source file, so they're immune to this regardless of BOM/locale. This applies
  to comments too, though there it's cosmetic rather than a functional bug.

---

## PowerShell Scripts (`scripts/`)

- **Never write a literal non-ASCII character (`…`, `—`, Cyrillic, etc.) inside a string literal.**
  Same root cause as the C++ rule above: a `.ps1` file saved as UTF-8 **without a BOM** gets
  decoded by Windows PowerShell 5.1 (`powershell.exe`) using the *active system code page*, not
  UTF-8, corrupting the glyph and — if it lands inside a quoted string — breaking the string's
  terminator and cascading into parser errors elsewhere in the file (real bug: `Deploy.ps1`'s
  em-dash broke `Write-Warning`'s string, reported as `Missing closing '}'` several lines away;
  see T-F84 in `TASKS_DONE.md`). Unlike the C++ case, there is **no `\uXXXX`-equivalent escape available**
  — PowerShell's backtick-`u{}` Unicode escape requires PowerShell 6.2+/pwsh core, and these scripts
  target Windows PowerShell 5.1 (`#Requires -Version 5.1`). Use a plain ASCII substitute instead
  (e.g. `-` for an em-dash). Non-ASCII characters in comments are safe (comments are skipped
  verbatim regardless of how their bytes decode) — this rule applies only to string literals.

---

## SonarCloud Won't-Fix Conventions

These rule categories are **intentionally left unaddressed** in this codebase — not oversights.
Which suppression mechanism to use depends on **which engine reports the rule** — get this wrong
and the marker silently does nothing (found T-F147: a `// NOSONAR` sat on a real finding across
two full analysis cycles before the mismatch was caught):

- **`csharpsquid:SXXXX` rules** (SonarCloud's own native C# analyzer — cognitive complexity,
  naming, commented-out code, etc.): mark the exact flagged line with
  `// NOSONAR: SXXXX — <one-line reason>` (see any existing `Sandbox/*.cs` file for the style).
  This suppresses the finding on the SonarCloud dashboard itself (confirmed working, T-F138 and
  re-confirmed T-F147 — the S101/S1075/S3871 markers below all disappeared from the very next
  scan). The local Roslyn analyzer still emits its own build-time warning regardless (it doesn't
  honor `NOSONAR`, a SonarCloud-server-side convention, not a compiler one) — that's expected
  noise, not a bug.
- **`external_roslyn:CAXXXX`/`IDEXXXX`/`SYSLIB1054` rules** (imported from the real `dotnet build`
  warning log, not SonarCloud's own engine): `NOSONAR` has **no effect** — confirmed empirically
  T-F147, where an already-`NOSONAR`'d `CA1835` finding was still open on the very next scan while
  a same-round `csharpsquid:S101` NOSONAR next to it was gone. Use
  `#pragma warning disable CAXXXX // <reason>` / `#pragma warning restore CAXXXX` (or
  `[SuppressMessage]`) instead — this stops the warning from being emitted into the build log at
  all, so it can never be imported. This has the added benefit of also silencing the local build
  warning, unlike the `csharpsquid` case above. **Verify locally before pushing** — every
  `external_roslyn` finding already appears verbatim in a plain `dotnet build`, so a clean build
  (0 warnings) is a reliable pre-push proxy for this whole rule family; no CI/SonarCloud round
  trip needed to confirm.

Won't-fix categories recorded so far:

- **S101 (naming convention) on P/Invoke struct names** (`SECURITY_ATTRIBUTES`, `STARTUPINFO`,
  `TRUSTEE_W`, etc., in `Archiver.Core/Services/Sandbox/`): these deliberately mirror the real
  Win32 SDK struct names for MSDN/sample-code cross-referencing — the same reasoning this file's
  C++ section already applies to COM parameter names. Renaming to PascalCase would break that.
- **S1075 (hardcoded absolute path/URI) on `TarExecutablePath`** (`TarSandboxScope.cs`,
  `TarSandboxedService.cs`): `CLAUDE.md`'s Hard Constraints mandate this exact hardcoded absolute
  path (`C:\Windows\System32\tar.exe`), specifically to resist PATH-hijacking. Moving it to
  configuration would reopen that exact risk — this is a security-motivated hardcode, not laziness.
- **S3871 (exception should be `public`) on `TarSignatureVerificationException`,
  `SandboxSetupException`, `TarArchiveRejectedException`**: all three are deliberately `internal`,
  never escape `Archiver.Core`'s public surface (always caught and converted to `ArchiveError`,
  per this file's own "`Archiver.Core` services must never throw to callers" rule). Making them
  `public` would be pure API-surface bloat against that rule, not a fix — a genuinely public
  exception type implies external callers should catch it specifically, which none ever do here.
- **CA1711 (type name ends in "Collection") on `TarSandboxTestCollection`**
  (`tests/Archiver.Core.IntegrationTests/`): xUnit's own `[CollectionDefinition]` marker-class
  convention names these classes `XCollection` (xUnit's own docs use `DatabaseCollection` as the
  canonical example) — no name avoiding the suffix stays idiomatic, so renaming further (as an
  earlier T-F147 pass tried, `TarSandboxCollection` → `TarSandboxTestCollection`) cannot actually
  satisfy this rule; only `#pragma warning disable CA1711` (per the mechanism note above, since
  this is an `external_roslyn` rule) stops it.
- **S1135 (complete this TODO) on `ArchiveEntrySecurity.cs:56` and `.github/workflows/build.yml`**:
  both TODOs are legitimate, already-tracked future work (not abandoned placeholders) — left as
  plain TODOs, not suppressed. Don't "fix" these by deleting the comment or completing the task
  out of scope of whatever triage pass finds them.

**`SYSLIB1054` (`DllImport` → `LibraryImportAttribute`, ~40 findings across
`Archiver.Core/Services/Sandbox/`) is deferred, not won't-fix** — it needs its own focused pass
with its own design-first review and full sandbox test run (security-critical native interop),
not a mechanical batch conversion inside an unrelated triage task. Tracked as **T-F148**.

A rule not in this list that gets flagged and reasoned through as a genuine won't-fix should be
added here at the same time its suppression marker is added — that's the whole point (see
T-F147): prose reasoning in a task-history doc alone doesn't stop the finding from resurfacing
next triage.

---

## SonarCloud Coverage Exclusions

`.github/workflows/build.yml`'s `sonar.coverage.exclusions` (added T-F149) removes exactly four
files from the `new_coverage` gate's denominator — they stay fully analyzed for every other rule
(bugs, vulnerabilities, code smells); this is `sonar.coverage.exclusions`, not `sonar.exclusions`,
which would drop a file from analysis entirely and lose its findings too. All four are genuinely
unreachable by `coverlet` (the coverage collector `dotnet test --collect:"XPlat Code Coverage"`
uses), not merely untested:

- **`Archiver.Shell/Program.cs`, `Archiver.Shell/NativeProgressDialog.cs`**: drive a real COM
  `IProgressDialog` — `CLAUDE.md`'s "Known test gaps" section already names
  `NativeProgressDialog` explicitly as a COM UI object that isn't unit-testable.
- **`Archiver.Core/Services/ExplorerLauncher.cs`**: opens a real Explorer window as its only
  meaningful behavior — already accepted as deliberately uncovered by design in T-F143's
  coverage triage.
- **`Archiver.CLI/Program.cs`**: the one different case — this file **is** exercised, by
  `Archiver.CLI.Tests`' `Subprocess/` layer, which `Process.Start`s the real built `pakko.exe`
  against real fixtures (this project's console-frontend testing convention, above, requires
  exactly this for a directly-user-invoked frontend). `coverlet` cannot instrument a spawned
  child process, so these lines read as 0% covered despite being under real test coverage — the
  exclusion here is about a collector limitation, not an untestable-by-design admission.

Adding a 5th exclusion needs the same bar as adding a new won't-fix rule above: a specific,
verified reason the collector (or a unit test) genuinely cannot reach the code — not "coverage is
inconvenient to write."

---

## Packages Allowed per Project

| Package | Project | Purpose |
|---------|---------|---------|
| `CommunityToolkit.Mvvm` | `Archiver.App` only | `ObservableObject`, `RelayCommand` |
| None | `Archiver.Core` | Pure .NET, no NuGet dependencies |

**Vendored native binaries (test-only, not a package reference) — `Archiver.Core.PerformanceTests`
only:** `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/{x64,arm64}/7za.exe` (T-F114) is a
pinned, hash-verified, LGPL-attributed reference binary used purely to time-compare against
Pakko's own ZIP path in performance-regression tests. This is **not** a violation of
`CLAUDE.md`'s "No 7-Zip"/"zero third-party dependencies" hard constraint — that constraint governs
the *shipped product* (`Archiver.Core`/`Archiver.App`/`Archiver.Shell`) only. If you see a 7-Zip
binary checked into this repo and wonder why, this is why — see that folder's `NOTICE.md`.
