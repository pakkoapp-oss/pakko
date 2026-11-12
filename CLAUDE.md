# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows with a shell extension (IExplorerCommand); tar, 7z, RAR and the other formats go through Windows' own `tar.exe` in an AppContainer sandbox.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

v1.1 through v1.4 (shell extension, tar.exe integration, Group Policy) and the v1.5-v1.7 waves
are complete. **v1.8.0** (PAR2) was released 2026-10-11, **v1.8.1** (7z creation, Test for
tar.exe's formats) 2026-10-16; **v1.9.0** (7-Zip's command line in `pakko`, a source never replaced
by its archive, no tar over a hard-link pair) 2026-10-30. The Store bundle 1.7.2.0, the first
Native AOT build, passed certification 2026-10-11; 1.8.x was never submitted; the 1.9.0 Store bundle
went live 2026-10-31 (`docs/DECISIONS.md`). Store listing:
https://apps.microsoft.com/detail/9p5mw010d8pr.

Per-task detail lives in `docs/TASKS_DONE.md` and `docs/DECISIONS.md`, never here. The long
narrative this section used to hold is archived verbatim in `docs/DECISIONS.md`'s "CLAUDE.md as of
2026-10-09 (T-F369)" entry.

**Test count:** run `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` for current ground
truth; never trust a count written in a doc.

**v1.10.0** (the `archiveint.dll` helper, read-only ISO/CAB/cpio/ar/.Z/lzip, taskbar/tray progress;
SDK 10.0.404) tagged 2026-11-11. **Next work:** the open tasks at the top of `docs/TASKS.md`.

## Roadmap Summary

Version-to-focus table: `docs/SPEC.md`'s "Future Roadmap" section, the sole owner.

---

## Documentation Map

The table below is the single index for every doc in the repo — extend it and its owners, never a
second map file. Only files GitHub/tooling look for at repo root stay there (`README.md`,
`LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `CHANGELOG.md`) plus this file;
every other doc lives under `docs/`.

Full table (purpose, read when, update when): `.claude/rules/docs.md`.

| File | Owns / read when |
|---|---|
| `docs/TASKS.md` / `docs/TASKS_DONE.md` | open tasks, `T-Fxx` numbering / finished tasks (append-only) |
| `docs/ARCHITECTURE.md` | C# layers, signatures, DI, single owners — before a signature, DI, or any path/walk/temp/error rule |
| `docs/XAML.md` | `MainWindow.xaml` structure, WinUI 3 gotchas |
| `docs/CONVENTIONS.md` | coding style, naming, per-project package whitelist, Won't-Fix analyzer findings — before writing code |
| `SECURITY.md` | threat model, all security/CVE/supply-chain/MOTW rationale |
| `docs/DECISIONS.md` | decisions and rejected approaches, root causes, history |
| `docs/DIAGRAMS.md` | required diagrams — COM/shell, operation lifecycle, `ZipArchiveService` branching, manifest |
| `docs/TESTING.md` / `docs/SMOKE.md` | test plan, fixtures, categories / on-device checks by tier |
| `docs/SPEC.md` | product scope, non-goals, the version roadmap table |
| `docs/CLI.md` | `pakko` command/switch specification |
| `docs/POLICIES.md` / `docs/SIGNING.md` | Group Policy reference / code-signing policy |
| `scripts/README.md` | build/sign/deploy steps |
| `CHANGELOG.md` | per-release history |
| `README.md`, `CONTRIBUTING.md`, `docs/index.html`, `docs/uk/index.html`, `docs/privacy.html` | public pages — not agent instructions |
| `docfx.json` + `toc.yml` | the DocFX developer site |
| `tests/Archiver.Messages.Tests/Glossary.tsv` | the term each locale uses — before any translated string |
| `docs/store-listing/` | Store listing text in 37 languages |
| `.claude/rules/*.md` | area rules for agents (T-F369), see "Area Rules" below |

**Canonical topic owners — do not duplicate, link instead:**
- Security/threat-model/CVE/supply-chain rationale → `SECURITY.md` only. `docs/SPEC.md`/`README.md` keep at most a 2-line teaser with a link.
- Version roadmap table → `docs/SPEC.md` only. `CLAUDE.md`/`README.md`/`docs/TASKS.md` reference it by version number instead of repeating the table.
- Build/sign/deploy steps → `scripts/README.md` only. `CONTRIBUTING.md` and this file's "Build Commands" section link to it rather than repeating steps.
- Hard constraints → `CLAUDE.md` (this file) only — the richest and most current copy.
- Format support table (what reads/creates what) → `README.md` only; `docs/SPEC.md` and the site link to it.
- Current C# signatures and DI wiring → `docs/ARCHITECTURE.md` only.

If you're updating a doc and find yourself retyping a table that already exists elsewhere in
this list, stop — link to the canonical owner instead. If no owner is obvious for a new topic,
ask before creating a new file.

Update cascades and the dangling-link grep: `.claude/rules/docs.md` (loads with any `.md` file).

---

## Area Rules (`.claude/rules/`, T-F369)

This file loads into every session and is size-gated (`AgentInstructionsSizeTests`, 26,000 bytes):
pair every addition with a deletion; history goes to `docs/DECISIONS.md`. Area rules load only when
a matching file is read or edited — **read the named file first when an action needs it without
touching a matching file**:

| File | Loads with | Covers |
|---|---|---|
| `core.md` | `src/Archiver.Core/**`, Core test projects | SafeHandle, accessibility errors, Registry, Deflate/ZIP format, Core contract changes |
| `app-winui.md` | App, App.Core, OperationUi | Native AOT detail, WinUI 3 gotchas, UI-thread marshaling, resw keys, localization |
| `packaging.md` | manifest, `.csproj`, `Deploy.ps1` | MSIX packaging, signing, satellite exes, TFM literals, Store gotchas |
| `shell-extension.md` | `src/Archiver.ShellExtension/**` | menu order, HRESULTs, icons, Packaged COM, C++ build, `dllhost` locks |
| `shell-cli.md` | Shell, CLI and their tests | COM interop `[PreserveSig]`, console-frontend tests, Shell commands, native modals |
| `tests.md` | `tests/**` | test filters in full, flakiness history, deliberately uncovered code |
| `ci.md` | `.github/**` | action pinning, SonarCloud API, CI gotchas, **cutting a release** |
| `scripts.md` | `scripts/**` | PowerShell 5.1 vs 7, execution policy, file-lock build errors, appcert |
| `device-verification.md` | `Deploy.ps1`, the manifest | **read before any on-device check**: freshness proof, `windows` MCP, Explorer menu, logs |
| `text-encoding.md` | `.cpp`/`.h`/`.ps1`/`.cs`/`.resw`/`.md` | non-ASCII literals, `\uXXXX` corruption in tool params, `Localization.cpp` edits |
| `docs.md` | `docs/**`, root `.md` | update cascades, dangling-link grep, mermaid validation, task graduation |

A reference to "`CLAUDE.md`'s <rule or section>" in code or docs predates this split: the rule is in
this file or in one of these (grep `.claude/rules/`).

---

## Hard Constraints — Never Violate

- `Archiver.Core` has **zero** WinUI / Microsoft.UI references
- `Archiver.Core` has **zero** references to `ResourceLoader` or `ILogService`
- Use only `System.IO.Compression` for ZIP compression — no NuGet compression packages
- Services injected via constructor — never `new ZipArchiveService()` in ViewModels
- Every engine/router takes a required `GroupPolicyOptions`; Shell and CLI build services only via
  `PakkoServices.Create(policy)` (T-F261)
- All IO exceptions caught per-item → `ArchiveError` — methods never throw to callers, except
  `OperationCanceledException` on cancellation (T-F260), even between two sources
- MVVM: no business logic in `.xaml.cs` files
- **Native AOT (T-F355): App, Shell, OperationUi and `pakko` ship Native AOT; the five libraries are
  `IsAotCompatible`.** `dotnet test` runs under JIT and cannot see an AOT-only failure. No
  `Assembly.Load*`, `Reflection.Emit`, reflection over unknown types, `[ComImport]` (use
  `[GeneratedComInterface]`) or reflection JSON (use a `JsonSerializerContext`). Never
  `UseSystemResourceKeys`/`InvariantGlobalization`. Verify on the deployed package, not under the
  debugger. WinUI detail: `.claude/rules/app-winui.md`.
- **tar.exe:** always use `C:\Windows\System32\tar.exe` (absolute path) — never via PATH; likewise
  `C:\Windows\System32\archiveint.dll`, loaded only by `Archiver.ArchiveHelper.exe` (T-F470)
- **Any `Process.Start` of a system-provided executable uses an absolute path** (PATH-hijack
  resistance, S4036); helper: `Archiver.Core/Services/ExplorerLauncher.cs` (T-F136).
- **tar.exe format support:** creates tar/gz/bz2/xz/zst/lzma, and on a new enough Windows also
  real 7z, but only with `--format=7zip` or `-a` (measured on build 26300, libarchive 3.8.8;
  T-F342). A plain `tar -cf out.7z` silently writes ustar under that name. RAR is read-only.
- **MOTW:** propagate `Zone.Identifier` by default (v1.2+); off only per user choice or policy (T-F360)
- **Shell extension:** `IExplorerCommand` only — no legacy `IContextMenu` COM shell extensions
- **Low IL sandbox:** P/Invoke is acceptable for security-critical process isolation code (v1.4)
- **Every intentionally-empty `catch` block needs a one-line comment stating why** (e.g.
  `/* best-effort */`) — an empty catch's WHY is exactly the non-obvious case the no-comments
  rule (global `CLAUDE.md`) carves out. Also satisfies SonarCloud's S108/S2486 by
  construction instead of accumulating findings (44 found at once in one first scan, T-F136).
- **Solution platforms:** the `.sln` has `Any CPU`/`x64`/`x86` solution configs, every C# project
  mapped to `Any CPU` (ARM64 builds go through `dotnet publish -r`, not the `.sln`). Add a project
  with `dotnet sln add`, then check its entries mirror `Archiver.Shell`'s (T-F268, 2026-09-26).
- Tests: always `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` with no path argument
  (plain `Category!=Slow` does not exclude `VeryLarge`); all projects stay green after every
  change. `Category=Slow` before a release or a Zip64/performance change. Full rule:
  `.claude/rules/tests.md`.
- **A regression test for a just-fixed bug must be seen failing:** revert the fix, confirm red,
  restore (details: `.claude/rules/tests.md`).
- Prefer simple and explicit over clever and implicit. If a task can be solved with a
  straightforward script step (copy, move, delete) versus a complex MSBuild/pipeline hook, choose
  the script. Reserve MSBuild targets and build pipeline customization for cases where a script
  genuinely cannot work. This applies to all tooling decisions — not just MSBuild.
- No mocking library (Moq/NSubstitute/etc.) is used anywhere in this repo — write hand-rolled
  fake implementations of interfaces for tests instead (see `ExtractionRouterTests.cs`).
- **Pre-implementation research** (COM, shell, packaging, and any design copied from NanaZip or
  7-Zip): fetch the real shipped source and quote it, never a description from memory; document
  findings in `docs/DECISIONS.md` before implementing. Unauthenticated: `curl -s
  "https://api.github.com/repos/<owner>/<repo>/git/trees/main?recursive=1"`, then WebFetch the raw
  file from `raw.githubusercontent.com`.
- **Git:** `main` takes pull requests only (T-F367 ruleset): push a branch, `gh pr create`, check
  SonarCloud, then `gh pr merge --auto --rebase`; the required `test` check gates the merge. `v*`
  tags cannot be moved or deleted. `git push` goes as the active `gh` account (normally `user137`,
  a write collaborator); a repo-admin call needs `gh auth switch --hostname github.com --user
  pakkoapp-oss` first and a switch back after (the wrong account fails with a misleading 404).
  SonarCloud findings through the REST API: `.claude/rules/ci.md`.
- Before tagging an ad-hoc fix with a new `T-Fxx` comment/reference, grep the highest existing
  number **across the entire repo**, not just `TASKS.md`/`TASKS_DONE.md`/`CLAUDE.md`/
  `DECISIONS.md` — don't guess a number. Some `T-Fxx` tags exist only as code comments with no
  `TASKS.md` entry (e.g. `T-F66` in `ZipArchiveService.cs`, `T-F67` in `Program.cs`); a
  markdown-only grep misses them and risks a collision. `T-F62`/`T-F63` are claimed by
  *different* tasks (`TASKS_DONE.md`); reusing them for an unrelated fix creates a lasting
  mismatch between code comments and the task log.
- **Non-ASCII glyphs in string literals and tool parameters** (C++, PowerShell, C#, Markdown): never
  type a literal glyph or a `\uXXXX` escape through Edit/Write; use a `py -3` script. Full rule:
  `.claude/rules/text-encoding.md`.

---

## Repo Layout

Every project and its role: `CONTRIBUTING.md`'s "Project structure" table; files inside
`Archiver.Core`: `docs/ARCHITECTURE.md`'s "Folder Structure". All docs except the root-convention files
live under `docs/` (T-F126); `.claude/rules/` holds the area rules.

---

## Build Commands

```bash
# Run tests (always works from CLI)
dotnet test --filter "Category!=Slow&Category!=VeryLarge"  # the actual default — plain
                                            # "Category!=Slow" alone does NOT exclude VeryLarge
                                            # tests, since they aren't tagged Slow (see the
                                            # hard-constraint note above)
dotnet test --filter "Category=Slow"    # Zip64 + T-F114 perf tests — real multi-second cost
dotnet test -c Release --filter "Category=VeryLarge"  # >4 GiB Zip64 test + T-F114's one-large-file
                                            # scenarios — on demand only, Release only (T-F272)
# Fuzz (T-F240): 10 iterations in the default run; longer runs and knobs: docs/TESTING.md

# Build core only
dotnet build src/Archiver.Core

# Generate test fixtures
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures

# DocFX developer site (T-F172): dotnet docfx docfx.json, steps in scripts/README.md

# Build MSIX (requires Windows SDK)
dotnet publish src/Archiver.App/Archiver.App.csproj \
    /p:Configuration=Release /p:Platform=x64 \
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true \
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

# Publish the standalone Archiver.CLI (T-F09) — independent of the MSIX, no cert needed.
# Must run via the PowerShell tool (uses /p: flags). See scripts/README.md.
.\scripts\Publish-Cli.ps1                    # both architectures -> artifacts/cli/
.\scripts\Publish-Cli.ps1 -Architecture x64  # one architecture only
```

C++ build and test commands: `.claude/rules/shell-extension.md`.

> **Toolchain (T-F492):** .NET 11; `global.json` pins the SDK exactly (`rollForward: disable`;
> the lock files carry its ILCompiler version, T-F364; bump: `scripts/README.md`). **C# 15** (T-F492),
> `LangVersion` set once in `Directory.Build.props`; the idioms whose absence fails the build (partial
> `[ObservableProperty]`, `Lock`, `field`, IDE0330/0340/0360/0032/0031 under `TreatWarningsAsErrors`):
> `docs/CONVENTIONS.md` and `.editorconfig`.
> Every C# build goes through `dotnet`; `msbuild.exe` builds only the `.vcxproj` files (Visual Studio
> and MSVC versions: `.claude/rules/shell-extension.md`). CI: `setup-dotnet` from `global.json` (the
> canary floats it). `dotnet build src/Archiver.App` is a quick compile check for ViewModel/DI changes;
> MSIX packaging, signing and running need Deploy.ps1 or VS.

Agent tool notes (the rest are in `.claude/rules/scripts.md` and `device-verification.md`):
- `dotnet` with `/p:` flags and any `.ps1` go through the PowerShell tool, never Bash (Git Bash
  mangles `/p:`; Bash's `powershell.exe` refuses scripts). The T-F370 hook enforces the first.
- Python is `py -3 <script.py>` with a Windows-style path, never bare `python`, never a `-c`
  one-liner holding a Windows path, never a `/tmp` path.
- **PowerShell tool's cwd persists across calls:** if a PowerShell call `cd`s/`Set-Location`s
  into a scratch folder (e.g. while building a test fixture), a later `rm -rf`/`Remove-Item` on
  that folder — even from Bash — fails with "in use" until a PowerShell call explicitly
  `Set-Location`s back out first.
- **PowerShell tool's *initial* cwd is not guaranteed to be the repo root** — a bare
  `dotnet test`/`dotnet build` can fail with `MSB1003: Specify a project or solution file`.
  Prefix with `Set-Location "<repo-root>";` when running dotnet commands via the PowerShell tool.
- **Monitor tool commands run in POSIX/Git-Bash syntax**, even when polling a Windows path —
  use `[ -f "/c/Program Files/..." ]`, not `Test-Path`, or the wait-loop never fires.
- **On-device verification:** always the full `.\scripts\Deploy.ps1 -Thumbprint ...`, never a bare
  `dotnet build` (it can install a stale package); the title-bar build time must be minutes old.
  Read `.claude/rules/device-verification.md` first; the checks are rows of `docs/SMOKE.md`.

---

## Key Current Signatures

`docs/ARCHITECTURE.md` is the only owner (interfaces, `ArchiveResult`, `ILogService`,
`IDialogService`, DI wiring); read it, or the interface file itself, before relying on a signature.

---

## Do Not

- Do not re-implement anything from `docs/TASKS_DONE.md`
- Do not add NuGet packages to `Archiver.Core` (zero dependencies)
- Do not modify `CLAUDE.md`, `SECURITY.md` unless explicitly asked
  (a Plan that merely *proposes* editing one of these two is not itself "explicitly asked" —
  get separate explicit confirmation before touching either, even after plan approval)
- Do not implement features not listed in `docs/TASKS.md` or `docs/SPEC.md`
- Do not use `Thread.Sleep` — use `await Task.Delay` if needed
- Do not use `static` mutable fields in services
- Do not use legacy `IContextMenu` shell extension — use `IExplorerCommand`
- Do not read tar/RAR/7z formats in-process — only in a sandboxed subprocess: `tar.exe` or
  `Archiver.ArchiveHelper.exe` (T-F470); `archiveint.dll` never loads into a Pakko .NET process

**Public-repo hygiene (this repo is public — audited 2026-07-24, clean, keep it that way):**
- Do not commit real secret/credential/token/private-key *values* into any file. GitHub Actions
  secret **names** (`$env:PFX_PASSWORD`, etc.) are fine to reference by name — the values only
  ever live in GitHub Actions Secrets, never in tracked files. A certificate **thumbprint** (e.g.
  `Deploy.ps1`'s signing thumbprint) is a public hash, not the private key — safe to keep visible.
- Do not commit personal email/phone/home-address, unless it's a deliberate, required public
  disclosure (e.g. `docs/SIGNING.md`'s SignPath-mandated Author/Reviewer/Approver names/handles).
- Do not hardcode an absolute path containing the real OS username (`C:\Users\<name>\...`) in any
  tracked file — machine-specific paths belong in `.claude.local.md` (gitignored), not here.
- If a secret is ever accidentally committed, `git rm`/deleting the file does **not** remove it
  from git history — needs `git filter-repo`/BFG on the whole history, and the secret must be
  rotated regardless of whether history gets scrubbed.

---

## Deployment

- `Deploy.ps1` packages a generated copy of the manifest (`obj/PakkoDev/`, `/p:PakkoAppxManifest`)
  whose revision is one past the installed dev package; the tracked file stays `X.Y.Z.0` (T-F368).
  `-SkipVersionBump` packages at `.0`. Change the first three segments only when told to, on
  `<Identity>` only, never `TargetDeviceFamily`'s `MinVersion`/`MaxVersionTested`.
- Full build+sign+install command (user's dev cert thumbprint):
  ```powershell
  .\scripts\Deploy.ps1 -Thumbprint "D2EC5F2C451ED0EBE94B8168A68E5B813954CC75"
  ```
- **Cutting a public release** (`vX.Y.Z` tag): a `CHANGELOG.md` section `## v<tag> — <date>` in the
  bump commit (the release job reads it) and the nightly checks on the release commit first —
  read `.claude/rules/ci.md` before tagging.

---

## Workflow Tips

- For complex tasks (architecture changes, new services, multi-file refactoring)
  use Plan Mode before writing any code — activate with /plan in Claude Code.
- **Before committing any task marked complete or partial:** run the full
  `.\scripts\Deploy.ps1 -Thumbprint "D2EC5F2C451ED0EBE94B8168A68E5B813954CC75"` build+sign+install, and
  ask the user to do the manual on-device verification (context menu, extraction, etc.) before
  the commit. Don't commit a task as done/partial on the strength of `dotnet test` /
  `Archiver.ShellExtension.Tests.exe` alone if it touches shell-triggered or UI behavior.
  If the user explicitly directs it, performing that verification yourself via the local
  `windows` MCP server (see `.claude.local.md`) is an accepted substitute for asking — still
  don't graduate a task on `dotnet test` alone without one or the other.
