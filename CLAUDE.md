# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows with a completed shell extension (IExplorerCommand) and in-progress tar.exe integration for RAR/7z/tar extraction.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

**v1.1** tagged `v1.1.0` (GitHub-only early-tester release). **v1.2 (shell extension)**,
**v1.3 (tar.exe integration)**, and **v1.4** are all complete except **T-F51** (Group
Policy/ADMX support, still open — see `docs/SPEC.md`'s roadmap table). Full per-task detail for
everything marked `[x]` below lives in `docs/TASKS_DONE.md` and `docs/DECISIONS.md` (each task's
own entry there) — this section only tracks current status, not the investigation trail.

**v1.2 shell extension:** `Archiver.Shell`, protocol activation, file association, MOTW, and the
`IExplorerCommand` COM DLL (T-F61) are complete. Progress UI uses the Shell's native
`IProgressDialog` (T-F61/T-F65 — the earlier `Archiver.ProgressWindow` satellite app was removed,
see `docs/DECISIONS.md`). T-F62 (Test archive), T-F68 (shell extract silently ignoring
`SkippedFiles`), T-F63 (Extract/Compress dialogs), and T-F83 (a cold-start protocol/file
activation bug T-F63's testing surfaced, predating T-F63 itself) are all done.

**v1.3/v1.4 tar.exe integration:** T-F47/T-F48 (`ITarService`/`TarCapabilities` scaffolding +
capability detection) done. T-F49 (`TarProcessService.ExtractAsync`) done — while designing it, a
real sandbox-escape exploit was confirmed against a naive tar.exe quarantine-then-validate model
(a symlink entry writes outside quarantine before validation runs); `ExtractAsync` instead
pre-scans and rejects the whole archive before extraction runs. The ADS/reserved-name/
reparse-point/MOTW checks were shared into `ArchiveEntrySecurity` so both extractors (later ZIP
and Tar) stay in sync. T-F95 (root context-menu icon missing — `Archiver.App.csproj` had no
`<ApplicationIcon>`) fixed. **T-F96** (`Deploy.ps1`/`dotnet publish` intermittent `MSB3231` on its
own `AppPackages`/`obj` cleanup) is `[~]` **closed as non-blocking** — root cause unconfirmed
(leading suspect: Search Indexer race), but `Deploy.ps1`'s own tolerance mitigation has absorbed
every recurrence since 2026-07-07; see `docs/TASKS.md`'s T-F96 entry if this needs revisiting.

**T-F05 (Archive Browser) is `[~]` partial** — all implementation done (Core
`ListEntriesAsync`/`IArchiveListingRouter`, `ExtractOptions.SelectedEntryPaths`, the
`Archiver.App.Core` project, full breadcrumb/per-folder browser + Extract Selected/All/Info
wiring), AI-driven on-device verification passed 2026-07-13; stays partial until the user's own
on-device click-through. A same-day UI design-review pass (comparing a real screenshot against
NanaZip) found and fixed a genuine bug (Row 0's Add Files/Add Folder/Hash never hid during browse
mode) and resized the window `800x700` -> `1100x650`. Three same-day follow-ups: the Info dialog
was deleted (fields folded into the browse-mode table as columns, plus a ZIP-only CRC-32 column);
the standalone Close button was replaced by a single up-arrow; and CRC-32 was extended to the
pending list too, which both surfaced and fixed a real blank-row regression (an unneeded explicit
`VirtualizingStackPanel` racing an async CRC completion) — see `docs/DECISIONS.md`'s three T-F05
follow-up entries for the full account, including a native-crash root-cause from two invented,
unverified `x:Uid` patterns that was fixed the same round.

**T-F99/T-F100 (drive-root context menu / file-activation routing)** are `[x]` done — on-device
testing surfaced and fixed a command-line-corrupting `QuotePath` trailing-backslash bug and two
independent archive-auto-naming bugs for drive-root sources. **T-F101** (Pakko missing from the
classic "Show more options" menu) is `[x]` resolved with no code fix — stopped reproducing after
T-F100 shipped; leading guess is an Explorer verb/icon-cache side effect of T-F100's manifest
change. **T-F103** (extraction destination misnamed for compound extensions, e.g.
`archive.tar.gz` -> `archive.tar` instead of `archive`) fixed via a shared `ArchiveNaming` helper
wired into every affected call site plus the native title-display equivalent.

**T-F06 (Ask on Conflict dialog)** done — `ConflictBehavior` gained a 4th value `Ask`, resolved
per-conflict through a Core->UI callback (`ConflictResolver` helper), wired into both
Archive-creation modes and both Zip/Tar extraction engines.

**T-F52 (AppContainer Sandbox for tar.exe)** is `[x]` complete — `TarProcessService` was deleted
outright (fail-closed, no unsandboxed fallback) and replaced by `TarSandboxedService`, routing
every tar.exe launch through a new `Archiver.Core/Services/Sandbox/` subsystem
(`AppContainerProfile`, `QuarantineAcl`, `QuarantineStaging`, `SandboxJobObject`,
`SandboxedProcessLauncher`, `SecurityCapabilitiesAttributeList`, `TarSignatureVerifier`,
`TarSandboxScope`). Confirmed on real hardware from the actual packaged (MSIX
`FullTrustApplication`) process identity, not just a test host. Several real bugs found and fixed
along the way (wrong `CERT_FIND_SUBJECT_CERT` constant, hardlinked staged files not inheriting
the quarantine ACL, libarchive's implicit parent-directory creation failing under AppContainer, a
quarantine-location correction to a fixed `%TEMP%`-rooted path) — see `docs/DECISIONS.md`'s several
T-F52 entries. Graduated via an MCP-driven on-device pass (user-directed accepted substitute for
a personal click-through) plus a 4th bug found via advisor review post-Step-13: sandbox-setup
`InvalidOperationException` wasn't caught, now wrapped in `SandboxSetupException`.

**T-F105 (TAR archive creation)** is `[x]` complete, all four phases — `ITarService.CompressAsync`
(deliberately unsandboxed, since creation reads trusted local files, not an untrusted archive; see
`SECURITY.md`), a Format combobox in `MainWindow.xaml` (localized across all 37 locales), a
one-click "Add to X.tar" `IExplorerCommand`, and a `--format zip|tar` CLI switch. On-device
verification (via `windows` MCP) confirmed all three entry points.

**T-F106** (pending-list `ListView` rows rendering blank at window-activation time) is `[x]`
resolved — root cause was never a WinUI rendering bug: `RootGrid`'s file-table row had no
`MinHeight` on its own `RowDefinition` (only the `ListView` child did, which doesn't force the
row to grow), so at a fixed window height the other rows' `Auto` sizing clamped the table's Star
row to 0. Fixed via a larger default window (`1100x900`), an explicit `MinHeight="200"` on the
row, and `PreferredMinimumWidth`/`Height` via `OverlappedPresenter`. Same session: the title bar
now shows `Pakko - build <timestamp>`, read from the running assembly's own file `LastWriteTime`
(see this file's Build Commands section for why this matters).

**T-F107** (Archive Browser's "Up" button now climbs past the archive root into the real
containing folder, up to a drive root, and up to a synthetic "This PC" node) is `[x]` done — new
`ArchiveBrowseScope` + `FileSystemBrowser` helper. **T-F97** (double-clicking an image/text file
in the Archive Browser silently previews it via a shared `%TEMP%\PakkoPreview\` cache instead of
running a full Extract) is `[x]` done — new `PreviewPolicy` allowlist + `PreviewCache`, reusing
the real `IExtractionRouter` pipeline so T-F49's pre-scan and MOTW propagation both apply for
free. Two real bugs fixed along the way: `Launcher.LaunchFileAsync` silently failing for an
arbitrary `%TEMP%` path (fixed via `Process.Start(UseShellExecute=true)`), and
`ArchiveResult.CreatedFiles` listing destination folders rather than individual file paths.
**T-F93** (Ko-fi donate link in `README.md` and the About dialog) is `[x]` done.

**T-F108/T-F98/T-F109/T-F110** (all `[x]` done, same session) — T-F108 fixed the extraction
destination defaulting to Desktop instead of the archive's own folder when browsing with no
pending files queued; T-F98 lets double-clicking a nested archive inside the browser drill
straight into it (up to 4 levels, `NestedArchivePolicy.MaxDepth`), reusing T-F49/T-F90/T-F94's
security machinery unmodified at every level; T-F109 widened the safe-preview allowlist to
video/audio, with anything else now confirming before extracting to a subfolder next to the
archive; T-F110 added a preview-vs-extract-only icon per row. All four verified on-device.

**T-F114** (ZIP-only compression/extraction performance-regression tests vs. a vendored,
hash-verified `7za.exe` reference) is `[x]` done — 6 scenarios (archive+extract x
one-large-file/many-small-files/hybrid), same-run ratio comparison against a per-scenario
calibrated constant with 3x cross-machine tolerance, tar-family explicitly out of scope. Every
`7za.exe` launch runs under tar.exe's own `SandboxJobObject` (Job Object only, no
AppContainer/quarantine, so timing is unaffected). Many-small-files/hybrid tests are tagged
`Category=Slow`; the one-large-file tests are tagged `Category=VeryLarge` (on-demand only).

**T-F35** (parallel ZIP compression above a 64-file threshold) is `[~]` **implementation complete,
on-device verification pending** — a new `Archiver.Core/Services/Zip/` subsystem
(`WorkItemEnumerator`, `ParallelSingleArchiveWriter`, `ZipEntryWriter`, `ZipEntryCompressor`,
`DosDateTime`) compresses every non-placeholder file in parallel (small files in memory,
everything else via a per-worker temp file) through a hand-rolled ZIP container writer, since
`ZipArchive` gives no API to compress independently and splice the result in later. Built to fix
the ~6x gap T-F114 measured for many-small-files archiving. Two bugs were caught by tests before
first ship (a bounded-channel concurrency bug, a Zip64 field-offset swap rejected by `7za.exe`
but not .NET's own lenient reader). Follow-ups: merged three redundant directory walks into one;
replaced the original 4 MiB "stream sequentially" fallback with per-worker temp-file compression
at all sizes (surfaced and fixed a temp-file-cleanup/cancellation race); relocated temp files to
a hidden subfolder next to the destination (after visible chunk-file flicker in Explorer) and
added a disk-space pre-check. A real on-device NanaZip comparison then caught a genuine
compatibility bug invisible to `dotnet test`: zero-byte files were tagged `Deflate` even though
`DeflateStream` emits 0 bytes for empty input (not a valid deflate stream) — real `ZipArchiveEntry`
always uses `Store` for empty entries; fixed to match. Final T-F114 ratios:
`ManySmallFiles` 6.02 -> ~1.0, `Hybrid` 3.47 -> ~1.3, `OneLargeFile` 1.22 -> 1.18 (unaffected, as
expected). Stays `[~]` until a manual on-device archive+verify of 100+ real small files including
a genuinely empty one — not graduated on `dotnet test` alone, per this project's workflow rule.
See `docs/DECISIONS.md`'s T-F35 entry and its four follow-ups for the full stage-by-stage trail.

**T-F09 (`Archiver.CLI`, 7z-familiar CLI)** is `[~]` **implementation complete** — a fourth thin
frontend over `Archiver.Core` (no DI container, manual construction like `Archiver.Shell`),
supporting `x`/`t`/`i`/`a`/`l` and the full three-way unknown-input rule from `docs/CLI.md`, shipped as
its own standalone self-contained per-architecture download. New `Archiver.CLI.Tests` includes a
`Subprocess/` layer that `Process.Start`s the real built exe against real fixtures — the first
test layer in this repo to do that. Stays `[~]` until the user's own on-device terminal run of all
five commands plus the three error cases.

**T-F116** (`Archiver.CLI` `-si`/`-so` stdin/stdout streaming) is `[~]` **implementation
complete** — implemented via private `%TEMP%` staging in `CliStreamStaging.cs`, zero
`Archiver.Core` changes. Empirically confirmed native PowerShell 5.1 silently corrupts binary
data piped between two executables while PowerShell 7+/`cmd /c` do not (documented in `docs/CLI.md`).
Same session: the built exe was renamed `Archiver.CLI.exe` -> **`pakko.exe`** (not added to PATH
automatically, matching ripgrep/fd/bat convention). Stays `[~]` until the user's own on-device
confirmation of a real piped round trip.

**T-F120** was closed 2026-07-18 and merged into **T-F122** (its acceptance criteria folded in,
not separately implemented). **T-F122** (GitHub Actions CI, `.github/workflows/build.yml`) is
`[x]` done — builds the MSIX + `pakko.exe` on every push/tag and publishes CLI zips + `SHA256SUMS`
to a GitHub Release on a version tag. Uncovered a real external environment change mid-
implementation: `windows-latest` silently relabeled to `windows-2025`, which lacks the ARM64
`v143` toolset variant — fixed by pinning `windows-2022` for the `build-msix` job specifically.
Graduated only after downloading and running a real CI-produced MSIX + `pakko.exe`.

**T-F117** (a silent no-op in `ExtractAsync`/`TestAsync` for a truly unrecognized archive format)
is `[x]` done — now records a real `ArchiveError` instead of silently succeeding; a
known-but-unsupported format keeps its existing `SkippedFile` behavior. **T-F118** (ZIP-vs-tar
extraction smart-foldering asymmetry — a multi-root archive wrapped in a subfolder for ZIP but
landed flat for tar-family) is `[x]` done — tar-family now matches ZIP's existing T-14
smart-foldering algorithm exactly. **T-F03** (a new Explorer "Open" command that launches
straight into the Archive Browser, mirroring NanaZip's real `kOpen`/`kExtract` split) is `[x]`
done — new `BrowseCommand`, a third `pakko://browse` protocol route, and a `--browse` Shell
switch.

**T-F131/T-F133** (`[x]` done) widened ZIP-format recognition to `.jar`/`.war`/`.ear`/`.apk` and
`.asice`/`.asics`/`.bdoc`. **T-F129's prep work** (`[x]` done) did Microsoft Store submission prep
(WACK fixes, brand assets, manifest revision fix) — the actual submission/certification/publish
landed later (see below). **T-F130** (`[x]` done) fixed intermittent CI sandbox-test flakiness via
a `DisableParallelization` xUnit collection. Released as `v1.4.2`. **T-F134** (`[x]` done) added a
`pakko -v`/`--version` flag (real 7z has no `version` subcommand; see `docs/DECISIONS.md` for why
Pakko diverges). Released as `v1.4.3`.

**Core implemented features (quick reference):** MSIX signed with dev cert via `Deploy.ps1` (see
T-F10 for production-grade cert); async streaming (`CopyToAsync`) with `CancellationToken`
respected mid-file; temp file/dir pattern — no partial files on cancel or failure; ZIP bomb
detection via compression ratio (1000:1 threshold); UTF-8 round-trip verified for Cyrillic and
emoji filenames; button text changes to "Archiving..."/"Extracting..." during operation; post-op
cleanup (`DeleteSourceFiles`, `DeleteArchiveAfterExtraction`) runs with `IsBusy=true`; SHA-256
integrity manifest removed (redundant with ZIP built-in CRC-32); ADS blocking (T-F38), reserved
filename filtering (T-F39), reparse point protection (T-F37); byte-accurate progress reporting
(T-F16) — `ProgressStream` wraps IO streams, `IsIndeterminate` removed; option controls disabled
during operations via `IsNotBusy`/`IsArchiveNameAndNotBusy`, all bind `IsEnabled`; FileStream
perf uses `useAsync: false`, `bufferSize: 262144` in all `ZipArchiveService` streams (faster on
local disks from ThreadPool); `.zip` file type association (T-F44) — double-click opens Pakko
with the archive pre-loaded, `AppInstance.Activated` handles both cold-start and warm file
activation; MOTW propagation (T-F45) — `Zone.Identifier` ADS copied to every extracted file,
best-effort, never fatal, no P/Invoke; status line shows operation name/file stats/speed/ETA
during an operation, elapsed time after completion.

**Microsoft Store release is live** (T-F129, done 2026-08-04) —
https://apps.microsoft.com/detail/9p5mw010d8pr. Certification passed and the listing was
confirmed genuinely public via `winget install --id 9P5MW010D8PR --source msstore`. An
agent-driven functional smoke test against that exact Store-installed package confirmed
`--test`/`--extract-here`/`--archive` all work, including a real `.7z`/`.rar` extraction through
`TarSandboxedService`'s AppContainer sandbox from the Store-signed identity specifically.

**T-F140** (`[x]` done) fixed archive-creation progress reporting for both formats (found from a
real user report that a 4-large-folder archive looked frozen) — ZIP's parallel writer was passing
`progress: null` into temp-file compression (fixed via a new throttled `ProgressTracker`); TAR's
percent denominator used top-level selected-path count instead of the real recursive entry count
(fixed via a `CountRecursiveEntriesAndBytes` pre-scan). Two same-day follow-ups added real
filenames and byte totals to both dialogs, and fixed a throttle bug that could swallow the very
first progress report for a small-file-dominated archive. **T-F141** (`[x]` done, same day) fixed
a related risk the user raised independently: `ParallelSingleArchiveWriter`'s hidden chunk temp
files were reopened with `FileShare.None`, which could abort the entire operation if a cloud-sync
client or AV briefly opened a finished chunk file — the read-back never needed exclusivity in the
first place, so this was a one-word fix to `FileShare.Read`.

**T-F142** (`[~]` **implementation complete, on-device visual check pending**) — real TAR
extraction byte progress via a poll of the sandboxed quarantine output directory (no streamed
subprocess channel exists for a sandboxed launch), plus a new shared `ProgressSpeedSampler`
consumed by both `MainViewModel` and `Archiver.Shell`'s dialog. Advisor review caught two real
bugs before shipping: a mixed zip+tar selection would have restarted tar's progress from 0% after
zip already reached 100%; a selected-subset extraction would have reported the whole archive's
byte total instead of the subset's. Both fixed. The visible speed-readout rendering itself still
needs the user's own on-device look.

**T-F146** (`[~]` **implementation complete, on-device verification pending**) — AMSI-based "Scan
for threats" for archives (Explorer context menu + Archive Browser). New standalone
`IAntivirusScanService`/`AntivirusScanService` (deliberately not folded into
`IArchiveService`/`ITarService`), a real P/Invoke `amsi.dll` wrapper, and `AmsiProviderCheck`
(forces `Inconclusive` when no AV provider is registered). ZIP entries scan entirely in-memory;
tar-family reuses T-F49/T-F52's `TarSandboxScope` quarantine but stops before the move-to-
destination phase. A Phase 0 empirical spike (real EICAR through a real `.tar.gz`) corrected the
original design assumption that AMSI never quarantines anything — Defender's own real-time
on-access scanner intercepted the file independently of AMSI; see `docs/DECISIONS.md`. New entry
points across all three frontends, full 37-locale localization. A same-day follow-up fixed
progress reporting from one-report-per-archive to real per-entry progress at zero extra I/O
cost. Stays `[~]` until the user's own on-device verification (a real EICAR-in-archive detection
through both entry points, plus the no-AMSI-provider `Inconclusive` path).

**T-F147** (`[x]` done) — SonarCloud triage of the findings backlog (134 -> 44), including
splitting `ZipArchiveService.ArchiveAsync` (cognitive complexity 132, the highest in the report)
and `TarSandboxedService` into purpose-specific context/sink records, keeping
`ExtractWithSmartFolderingAsync`/`ExtractSingleArchiveAsync` algorithmically identical per the
T-F118 invariant. Won't-Fix findings (P/Invoke struct naming, hardcoded tar.exe/quarantine paths,
internal-only exception types, xUnit's `[CollectionDefinition]` convention) are now documented in
`docs/CONVENTIONS.md`'s "SonarCloud Won't-Fix Conventions" section, closing the gap that let this
same finding category resurface after earlier rounds. `SYSLIB1054` conversion (~40 findings) was
scoped out as its own task, **T-F148**.

**T-F150** (`[x]` done) — static analyzers now run on every build for every language, with
mandatory fix-or-documented-suppress: C# `TreatWarningsAsErrors=true`; C++ MSVC `/analyze` on
both `Archiver.ShellExtension` `.vcxproj` files (found 2 real bugs — missing SAL annotations, an
ignored `CoInitializeEx` return); PowerShell `PSScriptAnalyzer` as a new CI job (found 4 real
missing-BOM files, same corruption class as T-F84). See `docs/CONVENTIONS.md`'s "Static-Analysis
Won't-Fix Conventions" section.

**T-F151** (`[x]` done) — the AMSI scan's per-entry size cap was raised from 64 MiB to 256 MiB
after a Phase 0 spike found AMSI's `IAmsiStream` COM-streaming mechanism fails above ~16-20 MiB
in practice against the real Defender provider, while the existing simpler `AmsiScanBuffer` call
scans up to 256 MiB with no error — kept the existing mechanism, raised its limit instead of
building new streaming code.

**T-F152** (deferred, user-directed) — a VirusTotal hash-lookup link for the Archive Browser was
proposed then explicitly declined once it was found to conflict with the published Privacy
Policy/`SECURITY.md`/`README.md`'s unqualified "zero network requests" claim.

**T-F153** (`[x]` done) — a source path ending in a trailing directory separator (realistic via
CLI tab-completion) silently corrupted archive creation two ways (wrong entry root in both
engines; `Archiver.Shell`'s `RunArchiveAsync` placing the new archive inside its own source
folder with a generic name). Fixed via `Path.TrimEndingDirectorySeparator` at each affected entry
point (chosen over a bare `TrimEnd` so a real drive root like `"C:\"` stays untouched).

**T-F154** (`[x]` done) — extracting a single-file archive landed the file inside a redundant
same-named wrapper folder under `ExtractMode.SeparateFolders` (Explorer's "Extract Here" and the
App's default Extract) — the `isSingleRootFile` flag was computed but never consulted there.
Fixed via an explicit `unisolatedDestDir` parameter. Also surfaced (not yet built) a new
collision-dialog gap in `Archiver.Shell`, tracked as the second, later T-F155 entry below.

**T-F156** (`[x]` done, immediately after T-F154 shipped) — `ExtractMode.SingleFolder` still
wrapped a genuinely multi-root archive in a subfolder, contradicting T-F118's deliberate
smart-foldering decision. Surfaced the conflict via `AskUserQuestion`; **user confirmed reversing
it for `SingleFolder` mode only** — `SeparateFolders` mode's unconditional per-archive wrapping is
unchanged.

**T-F157** (`[x]` done) — new shared `ExtractionDestinationPlanner` (`Classify`/`Resolve`)
replaces the hand-duplicated `actualDest`/`isSingleRootFolder` decision logic between
`ZipArchiveService`/`TarSandboxedService` that T-F118's own comment had called "kept
algorithmically in sync" — a promise T-F154/T-F156 both had to honor manually in one day. Advisor
review corrected two design points before implementation (a discard-less `switch` does not get
real compiler exhaustiveness under `TreatWarningsAsErrors`, confirmed via a scratch build). Pure
refactor, mutation-checked. **T-F158** (`[x]` done, same day) — the archive-creation-side
analogue: new shared `DestinationConflictResolver` replaces three hand-duplicated copies of the
Skip/Overwrite/Rename decision. Advisor caught two real issues pre-implementation and a third was
found independently (a stale test-coverage claim). The one arm only reachable through the WinUI
App's `SeparateArchives` mode was closed via a real `windows` MCP pass against the actual protocol
activation.

**T-F155** (`[x]` done) — `Archiver.Shell`'s three extract commands now show a real interactive
Overwrite/Rename/Skip + "apply to all" conflict dialog (`ShellConflictDialog`, `TaskDialogIndirect`
— the only Win32 primitive with custom button labels), at parity with the WinUI App's own T-F06
dialog. A Phase 0 spike caught three real bugs before any production code shipped: `TASKDIALOG_
BUTTON` needs `Pack = 1`; a missing/broken comctl32 v6 activation context fails at process
activation itself, not as a catchable exception; and the Windows SxS manifest parser rejected a
syntactically-valid XML comment between two manifest elements. New `StickyApplyToAllConflictResolver`
bridges a real scope gap (Core's `ConflictResolver` only remembers "apply to all" for one
`ExtractAsync` call; Shell's three commands each build a fresh one per archive in a loop). Opened
**T-F160** for the identical gap in `Archiver.CLI`'s `pakko x`.

**T-F161** (`[x]` done) — a real user report found the same day T-F155 shipped: extraction's
commit-phase `Directory.Move` fast path failed the *whole* tree with a misleading error (naming
only the top-level `_tmp` path) if any single file anywhere inside was transiently locked by
another process, even after Pakko itself had finished writing every file. Fixed via
`CommitTempDestToActualDest`, falling back to the existing per-file merge on `IOException`; also
fixed an independent `_tmp`-folder leak on any mid-loop failure.

**T-F163** (`[x]` done) — `Archiver.Shell`'s operation-result dialogs (skip/error header lines,
"operation failed", "no errors detected") were hardcoded English, predating and surviving three
earlier localization passes on Shell's *other* native dialogs. Fixed via a new
`ResultMessages.resx` across all 37 locales.

**v1.4.12 pre-release verification pass** (2026-08-12, user-directed, agent-driven via `windows`
MCP against the real installed release MSIX + release `pakko.exe`) — a full action inventory
across all 4 frontends cross-referenced against the test suite's 20 toxic/adversarial-input
categories; live smoke tests confirmed no blocking issues (all security gates hold; the reactive
tar.exe stderr "encrypt"-substring detection is not locale-sensitive even under real `uk-UA`; all
three documented `-si`/`-so` pipe recipes behave as documented). Opened **T-F164**/**T-F165** (two
real findings) and **T-F166**-**T-F170** (five pre-existing test-coverage gaps, not bugs).

**Test count:** run `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` for current ground
truth (as of 2026-08-11: ~826 .NET tests across `Archiver.Core.Tests`,
`Archiver.Core.PerformanceTests`, `Archiver.Shell.Tests`, `Archiver.App.Core.Tests`,
`Archiver.Core.IntegrationTests`, `Archiver.CLI.Tests`; C++ `Archiver.ShellExtension.Tests.exe`
separately, 100/100 as of T-F150) — don't trust any older count in git history as current.

**Next work:** Future tasks in `docs/TASKS.md`, including **T-F148** (SYSLIB1054 conversion, split
out of T-F147), **T-F159** (unify `GetUniqueFilePath`, split out of T-F158), **T-F160**
(interactive conflict dialog for `Archiver.CLI`'s `pakko x`, parity with T-F155), **T-F164** (GUI
Hash lacks CRC-32, not routed through `FileHashService`), **T-F165** (`docs/DIAGRAMS.md` diagram 3
stale after T-F161), **T-F166**-**T-F170** (test-coverage gaps: real junctions, AES-256 ZIP, Tar
duplicate-entry-names, in-flight Tar cancellation, locked destination on extract).

## Roadmap Summary

Version-to-focus table: see `docs/SPEC.md`'s "Future Roadmap" section (the sole owner, per T-F72 —
`README.md`'s roadmap links there too now). Per-version completion detail beyond a one-line scope
description lives in this file's "Current State" section above instead of a second table.

---

## Documentation Map

**This is the single index for every doc in the repo.** An earlier `AGENT.md` was a second,
competing entry point (its own "Read Order", its own stale hard-constraints subset) — it was
deleted 2026-07-05 once this map fully absorbed its role (see git history if you need it).
`BOOTSTRAP.md` was deleted the same day — its content is now the "Dependency Injection &
Startup" section of `docs/ARCHITECTURE.md` (it had drifted into a near-duplicate of a section
`docs/ARCHITECTURE.md` already had). Do not create a third map file or a new DI-wiring file; extend
this table and its owners instead.

**Root layout (2026-07-23, T-F126):** only files GitHub/tooling specifically look for at repo
root stay there — `README.md`, `LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
`CHANGELOG.md` — plus `CLAUDE.md` itself (Claude Code only auto-loads a *root* `CLAUDE.md`, so it
can never move). Every other doc below lives under `docs/`. **This table gives the real, current
path for each file — trust it over any bare filename mentioned in this file's own "Current State"
history narrative below, which predates the move and was not mechanically rewritten throughout
(too large a diff for a cosmetic path change; the content itself is still accurate).**

| File | Purpose | Read when | Update when |
|---|---|---|---|
| **CLAUDE.md** (here) | Session context, hard constraints, build commands, this map | Every session (auto-loaded) | Project status changes, a hard constraint changes, build/deploy commands change |
| `docs/TASKS.md` | Active/future task backlog, acceptance criteria, `T-Fxx` numbering | Starting any implementation task | A task starts/completes/changes scope; a new `T-Fxx` is claimed |
| `docs/TASKS_DONE.md` | Archive of completed v1.0 tasks | Need historical task detail | Never — append-only via tasks graduating out of `docs/TASKS.md` |
| `docs/ARCHITECTURE.md` | Current C# layer diagram + signatures + DI wiring/startup | Before writing code that touches a public signature or a DI-registered service | A public signature/model/interface in `Archiver.Core` changes, or DI registration/lifetime changes |
| `docs/XAML.md` | Current `MainWindow.xaml` structure + WinUI 3 gotchas | Touching `Archiver.App`'s XAML | XAML structure changes, a new WinUI 3 constraint is discovered |
| `docs/CONVENTIONS.md` | Coding style, naming, async, error-handling, per-project package whitelist | Before writing any code | A new convention is adopted, or a code example goes stale |
| `SECURITY.md` | Threat model — **canonical owner of all security/CVE/supply-chain/MOTW rationale** | Modifying compression, traversal, or extraction logic | Threat model changes, a new mitigation is added |
| `docs/DECISIONS.md` | Architectural decisions + rejected approaches, with root-cause detail | Before implementing packaging, COM, or shell integration | An approach is chosen, rejected, or corrected |
| `docs/DIAGRAMS.md` | Required sequence/state/activity/component diagrams, Ground Truth Rule | Touching COM/shell, operation lifecycle, `ZipArchiveService` branching, or the manifest | Per its own DoD table — same commit as the code |
| `docs/TESTING.md` | Test plan and fixture inventory for `Archiver.Core` | Writing or running tests | New test category, fixture, or test count changes |
| `tests/Archiver.Core.Tests.GenerateFixtures/README.md` | Fixture-generation mechanics only (subordinate to `docs/TESTING.md`) | Adding a fixture-dependent test | A new fixture scenario is added |
| `docs/SPEC.md` | Product specification — **canonical owner of the version roadmap table, feature scope, non-goals** | Scoping a new feature, checking what's out of scope | Scope or roadmap changes |
| `docs/CLI.md` | **Canonical owner of Archiver.CLI's (T-F09) command/switch specification** — 7z→Pakko command table, switch fidelity, three-way unknown-input rule | Implementing or extending T-F09 | The planned CLI command/switch surface changes |
| `docs/POLICIES.md` | Group Policy/ADMX admin reference (T-F51) | Touching GPO-controlled behavior | GPO-controlled behavior changes |
| `docs/SIGNING.md` | Code Signing Policy (team roles, build process, artifacts covered) — published for SignPath Foundation eligibility (T-F124) | Touching signing/release process | Signing process or team roles change |
| `README.md` | Public GitHub landing page | User-facing — not an agent instruction source | Public messaging changes; must link to `SECURITY.md`/`docs/SPEC.md`, never restate their tables |
| `CONTRIBUTING.md` | Contributor onboarding summary | Before a contributor's first build | Build/deploy steps change — update `scripts/README.md` first, then sync the summary here |
| `scripts/README.md` | **Canonical owner of build/sign/deploy steps** (`Deploy.ps1`, `Setup-DevCert.ps1`) | Running or changing the deploy scripts | `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes |
| `CHANGELOG.md` | **Canonical owner of per-release history** — one section per version tag, plain-language summary of the `T-Fxx` tasks shipped since the previous tag | Cutting a release | Every version tag — see this file's "Deployment" section |
| `docs/index.html` + `docs/uk/index.html` | Public project website (GitHub Pages, served from `/docs`) — bilingual EN/UK landing page: trust model, what's implemented, download links | User-facing — not an agent instruction source | Supported-format list changes, a major feature ships, download/release mechanics change, or roadmap/version-status changes — keep both language versions in sync with each other and with `README.md`'s "Project Status"/"Supported Formats" |

**Canonical topic owners — do not duplicate, link instead:**
- Security/threat-model/CVE/supply-chain rationale → `SECURITY.md` only. `docs/SPEC.md`/`README.md` keep at most a 2-line teaser with a link.
- Version roadmap table → `docs/SPEC.md` only. `CLAUDE.md`/`README.md`/`docs/TASKS.md` reference it by version number instead of repeating the table (existing duplicates tracked as `T-F72`).
- Build/sign/deploy steps → `scripts/README.md` only. `CONTRIBUTING.md` and this file's "Build Commands" section link to it rather than repeating steps.
- Hard constraints → `CLAUDE.md` (this file) only — the richest and most current copy.
- Current C# signatures and DI wiring → `docs/ARCHITECTURE.md` only (stale signature there tracked as `T-F73`).

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
| Public signature/model change in `Archiver.Core` | `docs/ARCHITECTURE.md` | `docs/CONVENTIONS.md` (XML-doc example), `docs/TASKS.md` (mark task done) |
| DI registration or lifetime change | `docs/ARCHITECTURE.md` | — (single owner now, no cascade) |
| `MainWindow.xaml` structure or new WinUI 3 gotcha | `docs/XAML.md` | — (leaf doc) |
| New coding convention adopted | `docs/CONVENTIONS.md` | — |
| Threat model or mitigation changes | `SECURITY.md` | `docs/SPEC.md` (teaser), `README.md` (teaser) |
| Approach chosen/rejected/corrected (COM, packaging, shell) | `docs/DECISIONS.md` | `docs/ARCHITECTURE.md`, `CLAUDE.md` (hard constraints), `scripts/README.md`, `docs/DIAGRAMS.md` |
| Task starts/completes, or a new `T-Fxx` is claimed | `docs/TASKS.md` | `docs/TASKS_DONE.md` (graduation on completion), `CLAUDE.md` (Current State), `README.md` (Project Status) |
| Version scope/roadmap changes | `docs/SPEC.md` | `CLAUDE.md` (Roadmap Summary), `README.md` (Roadmap) |
| Supported-format list or a major feature ships/changes | `README.md` (Supported Formats / Project Status) | `docs/index.html` + `docs/uk/index.html` (What's Implemented section, kept identical in substance across both languages) |
| `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes | `scripts/README.md` | `CONTRIBUTING.md`, `README.md` (Building and Deploying), `CLAUDE.md` (Build Commands) |
| A release is tagged (`vX.Y.Z`) | `CHANGELOG.md` | — (single owner, see "Deployment") |
| COM/shell, operation lifecycle, `ZipArchiveService` branching, or manifest changes | `docs/DIAGRAMS.md` | Per its own DoD table |
| New test or fixture added | `docs/TESTING.md` | `tests/Archiver.Core.Tests.GenerateFixtures/README.md`, `CONTRIBUTING.md` |
| New project added to `src/` or `tests/` | `docs/ARCHITECTURE.md` (folder tree) | `CONTRIBUTING.md` (Project structure table) |
| A root `.md` file is added, removed, or moved | `CLAUDE.md` (Documentation Map + Repo Layout) | Re-run the dangling-link grep below |

Before deleting or merging any `.md` file, grep the whole repo for its filename first — dead
references are easy to miss otherwise (this session found 5 lingering mentions of `AGENT.md`/
`BOOTSTRAP.md` after removing them).

**Dangling-link grep after moving/renaming any `.md` file (T-F126):** markdown-link syntax only —
`rg '\]\([A-Za-z0-9_./-]*\.md[^)]*\)' --glob '*.md'` from repo root. Real cross-references show up
in non-obvious places beyond the doc itself: `.github/*_TEMPLATE.md`, `deploy/README.md`,
`scripts/*.ps1` comments — grep those separately for bare filename mentions too, not just `.md`
files.

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
- **Any `Process.Start` of a system-provided executable must use an absolute path, not a bare
  relative name** — same reasoning as the tar.exe rule above (PATH-hijack resistance), generalized
  after SonarCloud (S4036) caught 5 `Process.Start("explorer.exe", ...)` call sites doing exactly
  the relative-name thing the tar.exe rule was meant to prevent. See
  `Archiver.Core/Services/ExplorerLauncher.cs` for the shared helper (T-F136).
- **tar.exe format support:** can *create* tar/gz/bz2/xz/zst/lzma (compression filters on a
  ustar/pax/cpio/shar writer) but can only *read* 7z/rar — libarchive has no writer for either
  (`tar --help`'s `--format` lists only `ustar|pax|cpio|shar`). Confirmed empirically while
  building T-F50's fixtures — don't assume `tar -cf out.7z ...` produces real 7z (it silently
  writes a plain ustar tar under that filename instead).
- **MOTW:** always propagate `Zone.Identifier` ADS on extracted files (v1.2+)
- **Shell extension:** `IExplorerCommand` only — no legacy `IContextMenu` COM shell extensions
- **Context-menu ordering:** primary action commands (Extract/Archive) always precede
  diagnostic/verification ones (Test archive) in `PakkoRootCommand::EnumSubCommands` —
  deliberate deviation from NanaZip's Test-first order. See `DECISIONS.md`'s
  "Test Archive (T-F62)" entry before copying NanaZip's menu order for a new command.
- **COM HRESULTs:** never return `S_FALSE` alongside a null/unset out-parameter — `S_FALSE` is a
  *success* code (`SUCCEEDED()` is true), so callers checking only `SUCCEEDED()` will dereference
  the null. Use `E_NOTIMPL` instead (verified against Microsoft's own `IExplorerCommand` sample).
- **Shell-extension icons referencing another exe** (e.g. `PakkoRootCommand::GetIcon` →
  `Archiver.App.exe,0`): the target exe needs `<ApplicationIcon>` set in its `.csproj` — a
  `Content Include` of an `.ico` (used for the MSIX tile logo) does NOT embed a Win32 icon
  resource in the exe. Verify with `ExtractIconEx(path, -1, $null, $null, 0)`'s total count, not
  `[System.Drawing.Icon]::ExtractAssociatedIcon()` — the latter can return a non-null fallback
  icon even for a file with zero real icon resources (T-F95).
- **.NET COM interop (`[ComImport]` interfaces consuming external COM objects):** check the real
  SDK header before declaring the interface — if a method returns a plain type (e.g. `BOOL`)
  instead of `HRESULT`, mark it `[PreserveSig]`. Without it, the marshaller assumes the
  HRESULT + hidden-`[out]`-param convention and silently misreads the return value. Real bug:
  `IProgressDialog.HasUserCancelled` always read back `false` (Cancel appeared to do nothing)
  until `[PreserveSig]` was added — see `Archiver.Shell/NativeProgressDialog.cs`.
- **Low IL sandbox:** P/Invoke is acceptable for security-critical process isolation code (v1.4)
- **`SafeHandle.DangerousGetHandle()` must be paired with `DangerousAddRef`/`DangerousRelease`
  spanning the actual dereference** — without it, nothing keeps the handle reachable for the GC
  between the two calls (a handle-recycling race), even though `using`/async-state-machine capture
  often makes it work by accident. Same "provable from the line itself, not hand-traced" standard
  as bounds checks. Found 16 real instances via SonarCloud S3869 — see
  `Archiver.Core/Services/Sandbox/` for the pattern (T-F136).
- **A `private` nested class cannot be a parameter type on an `internal` (or more accessible)
  method — `CS0051` "Inconsistent accessibility."** Bump the nested class to `internal` instead
  (still invisible outside the assembly without `InternalsVisibleTo`). Hit adding
  `ParallelSingleArchiveWriter.ProgressTracker` as a parameter on the existing `internal static
  CompressToTempFileAsync` (T-F140).
- **A `file`-scoped type (e.g. a hand-rolled `file sealed class FakeX` test fake) cannot appear in
  the signature of a non-`file`-scoped member — `CS9051`.** If a test helper method needs the fake
  as an explicit parameter type, drop the `file` modifier on the fake class instead (plain
  top-level `internal` is fine — it's still test-assembly-only). Only matters when a shared helper
  takes the fake by type; a fake only ever assigned to `var` never hits this (T-F146).
- **Every intentionally-empty `catch` block needs a one-line comment stating why** (e.g.
  `/* best-effort */`) — an empty catch's WHY is exactly the non-obvious case this file's own
  comment policy already carves out an exception for. Also satisfies SonarCloud's S108/S2486 by
  construction instead of accumulating findings (44 found at once in one first scan, T-F136).
- **`Microsoft.Win32.Registry` (`RegistryKey`) is usable from `Archiver.Core` (plain `net8.0`,
  not `net8.0-windows`) with zero new NuGet package reference** — confirmed via a throwaway probe
  build; it's already part of the Windows runtime pack pulled in transitively, not something this
  project's "zero dependencies" constraint blocks. Mark the call site
  `[SupportedOSPlatform("windows")]` to make the resulting `CA1416` warning meaningful instead of
  leaving it unaddressed (T-F51, `GroupPolicyService`/`Win32RegistryReader`).
  **Don't over-annotate:** only the member that directly touches the Windows-only BCL API needs
  `[SupportedOSPlatform("windows")]` — a raw P/Invoke wrapper class calling its own `DllImport`s
  (e.g. `Services/Sandbox/`, `Services/Antivirus/AmsiScanner.cs`) needs no annotation at all, since
  `DllImport` itself isn't BCL-platform-tagged. Annotating the whole class anyway makes `CA1416`
  propagate into every caller, including test projects on a plain `net8.0` TFM — confirmed
  T-F146, where a class-level annotation forced two unrelated test classes to also carry the
  attribute before the warnings cleared.
- **UI-thread marshaling for Core→App callbacks:** any delegate `Archiver.Core` invokes that ends
  up showing WinUI (e.g. `ExtractOptions.ConfirmCompressionBombExtraction` → `ContentDialog`) must
  marshal onto `Window.DispatcherQueue` inside the App-layer implementation —
  `ZipArchiveService`/`TarProcessService` run their extraction bodies off the UI thread, and
  `ContentDialog.ShowAsync()` requires the calling thread to own the DispatcherQueue. Found via
  design review before shipping (T-F94) — would have crashed on first real use otherwise.
- **Solution platforms:** x64 and ARM64 only — never add `Any CPU` or `x86` configuration entries
  to the `.sln` file. When adding a new project, mirror the `Debug|x64` / `Release|x64` entries
  from `Archiver.Shell` exactly (two lines per config, right-hand side maps to project's `Any CPU`).
- **`Archiver.Shell/Program.cs` is top-level statements — local functions there can forward-
  reference each other, but a local `const` cannot be used before its own textual declaration
  (`CS0841`), unlike a real class's fields.** `MB_ICONERROR`/`MB_ICONWARNING`/`MaxErrorLinesShown`
  are declared partway through the file, not at the top. When adding a new `--xxx` command's
  handler function, insert it **after** any consts it reads (e.g. right after the most similar
  existing command's own function), not simply appended after the dispatch `switch` — confirmed
  T-F146, moving `RunScanAsync`/`ShowScanResults` down past those consts fixed it.
- **Pin third-party GitHub Actions (`org/action@vX`) to a full commit SHA, not a mutable version
  tag** — `actions/*` (first-party GitHub actions) are exempt by convention; everything else
  (`microsoft/setup-msbuild`, `nuget/setup-nuget`, etc.) should be SHA-pinned with a `# vX.Y.Z`
  trailing comment. Found via SonarCloud S7637 (T-F136).
- **Checking current SonarCloud findings:** the dashboard
  (`sonarcloud.io/summary/overall?id=pakkoapp-oss-1_pakko&branch=main`) is a JS SPA a plain fetch
  won't render — use the public REST API instead, no auth needed for this public project:
  `sonarcloud.io/api/issues/search?componentKeys=pakkoapp-oss-1_pakko&branch=main&resolved=false&ps=100`
  (WebFetch renders it fine). Reflects the last CI-analyzed push, not uncommitted local changes —
  re-check after pushing if verifying a specific fix landed clean.
- When adding or modifying tests, always run `dotnet test --filter "Category!=Slow&Category!=VeryLarge"`
  with no path argument — never scope to a single test project. **Plain `Category!=Slow` alone is
  not sufficient** — a test tagged only `VeryLarge` (not `Slow`) is not excluded by `!=Slow`, so it
  would run automatically, defeating the entire point of the `VeryLarge` tier (confirmed empirically
  2026-07-17: `Category!=Slow` alone picked up T-F114's two one-large-file tests). All projects must
  stay green after every change. This combined filter excludes T-F20's Zip64 Slow tests and T-F114's
  Slow-tagged performance tests (real multi-second cost); run `dotnet test --filter "Category=Slow"`
  too before a release or when the change touches Zip64-adjacent code (entry counts, large files,
  Zip64 boundary conditions) or compression/extraction performance. `dotnet test --filter
  "Category=VeryLarge"` (the >4 GiB Zip64 test, T-F114's one-large-file scenarios) is on-demand
  only — never run automatically as part of either of the above, only when deliberately verifying
  that specific path.
- **When writing a regression test for a just-fixed bug, temporarily revert the fix and confirm
  the new test actually fails before restoring it and leaving the test green** — a test that only
  ever ran against already-fixed code can pass for the wrong reason (e.g. testing a whitebox seam
  that bypasses the real bug). This discipline itself caught a second, independent bug this way
  (T-F140): a progress-throttle timestamp initialized at construction silently swallowed the very
  first report for any fast/small-file operation — invisible until a fast-operation test was
  deliberately run against the pre-fix code and didn't fail as expected.
- If a change modifies a public interface, model, or contract in `Archiver.Core`, check whether
  tests in other projects (`Archiver.Shell.Tests`, future `Archiver.CLI.Tests`) need to be updated
  or extended. Internal implementation changes (private methods, buffers, sorting) require only
  `Archiver.Core.Tests` coverage.
- Before threading a new `Archiver.Core` constructor parameter (e.g. a new cross-cutting service)
  through every consumer, grep the whole repo for every `new ZipArchiveService(`/
  `new TarSandboxedService(`/etc. call site rather than trusting an older written plan's
  enumerated list — a plan can predate a newer frontend shipping. Real gap: T-F51's plan (written
  2026-07-17) enumerated only `Archiver.Shell`'s call sites; `Archiver.CLI` shipped the next day
  and was missing from it entirely.
- Prefer simple and explicit over clever and implicit. If a task can be solved with a
  straightforward script step (copy, move, delete) versus a complex MSBuild/pipeline hook, choose
  the script. Reserve MSBuild targets and build pipeline customization for cases where a script
  genuinely cannot work. This applies to all tooling decisions — not just MSBuild.
- No mocking library (Moq/NSubstitute/etc.) is used anywhere in this repo — write hand-rolled
  fake implementations of interfaces for tests instead (see `ExtractionRouterTests.cs`).
- **Console-frontend testing (`Archiver.Shell`, future `Archiver.CLI`):** extract argument
  parsing into its own testable class (e.g. `ShellArgumentParser`) and unit-test it in-process —
  never parse inline in `Main`. No test in this repo spawns a built `.exe` and asserts on a real
  exit code/stdout yet — `Archiver.Shell.Tests` only unit-tests the parser, which is fine there
  since its args are always generated programmatically, never typed by a person. A frontend a
  user/script invokes directly (`Archiver.CLI`) needs that real-process layer too, since its
  exit code/stdout *is* the public contract — see T-F09's acceptance criteria for the shape.
- To unit-test an `internal` `Archiver.Core` class directly, add
  `<InternalsVisibleTo Include="Archiver.Core.Tests" />` to `Archiver.Core.csproj` rather than
  making it/its members `public` just for test access (first used for `ArchiveEntrySecurity`, T-F94).
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
  `gh` CLI **is** installed and authenticated in this environment (confirmed T-F122, 2026-07-19 —
  used extensively for `gh run`/`gh release`/`gh secret`).
  **It is authenticated as the `pakkoapp-oss` GitHub account itself** (`gh auth status`) — real
  push/release/tag/API write access to the live repo, not a read-only token. `pakkoapp-oss` is a
  personal **User** account, not an Organization — collaborators only get push access, never
  Admin/Maintain/Triage (those roles only exist on org-owned repos).
  **This machine can have a second `gh`-logged-in account (e.g. `user137`) active instead of
  `pakkoapp-oss`** — check `gh auth status`'s `Active account: true` line before any repo-admin
  call (topics, settings, branch protection, etc.). The wrong active account fails such calls
  with a misleading `HTTP 404: Not Found`, not a `403`, since GitHub reports resources the active
  token can't administer as not-found rather than forbidden. Fix: `gh auth switch --hostname
  github.com --user pakkoapp-oss` before the call, then switch back afterward
  (`gh auth switch --hostname github.com --user <other>`) so the machine's default identity isn't
  left changed for unrelated work. `gh run`/`gh release`/`gh secret`/reads generally work fine
  either way — this specifically bit `gh repo edit --add-topic` (2026-08-02).
  GitHub's code search still requires sign-in even for public repos, so for reading a
  third-party repo's source, prefer:
  `curl -s "https://api.github.com/repos/<owner>/<repo>/git/trees/main?recursive=1"`
  lists every file path unauthenticated — grep it for the area you need, then WebFetch the raw
  file (`raw.githubusercontent.com/<owner>/<repo>/main/<path>`) to read real code.
  Same method applies beyond COM/shell/packaging: fetching NanaZip's real `NanaZip.Modern/` source
  settled an archive-browser UI design (T-F05), and fetching its vendored real 7-Zip
  `ArchiveCommandLine.cpp` settled the CLI command/switch table (T-F09) — don't restrict this
  research discipline to COM work just because that's where it was first written down.
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
- **WinUI 3 cold-start activation gotcha:** `AppInstance.Activated` (Windows App SDK) only fires
  for activations *redirected* to an already-running instance — never for a process's own initial
  activation. `OnLaunched` must pull it explicitly via
  `AppInstance.GetCurrent().GetActivatedEventArgs()` and route File/Protocol kinds through the same
  handler `OnActivated` uses, or a cold `pakko://`/file-association launch silently opens a blank
  window (see T-F83 in `DECISIONS.md`).
- **Non-ASCII glyphs (ellipsis, em-dash, Cyrillic) in C++/PowerShell string literals**: never write
  the literal character — full rule + `\uXXXX` escape pattern is in `CONVENTIONS.md`. Shipped
  three times already (T-F64, T-F76, T-F63) despite being documented — check every new string
  literal before considering a change done.
  **Fixing an already-corrupted literal is not exempt:** typing the `\uXXXX` escape as Edit-tool
  replacement text silently re-decodes to the same literal glyph (confirmed T-F105) — the Edit
  reports `old_string`/`new_string` identical instead of erroring. Build the escape from raw char
  codes (`[char]0x5C + "u2026"`) and write via `System.IO.File`/byte-level replacement instead.
  **Not limited to C++/PowerShell:** the same corruption hit C# (an icon-font PUA glyph in
  `ArchiveEntryViewModel.cs`'s `Icon` property, T-F110) and Markdown prose (`TASKS.md`, T-F110) —
  any Edit/Write call whose params contain a raw `\uXXXX` escape or a raw PUA/icon-font glyph
  (Segoe MDL2/Fluent, e.g. codepoint U+E890) risks silent corruption regardless of file type. Use a
  throwaway Python script via the `py` launcher, building the exact bytes with `chr(0xEXXX)`,
  for any edit touching such content.
  **Before concluding a Write/Edit call corrupted non-ASCII text, verify via actual bytes/
  codepoints, not by eyeballing terminal output.** Git Bash's console can visually render
  correctly-encoded UTF-8 (e.g. via `cat -A`, or a Python `print()`) as mangled/replacement-looking
  characters even when the file on disk is byte-perfect — confirmed a false alarm (T-F128) where a
  real ellipsis (U+2026) looked corrupted in `print(repr(...))` output but was proven correct via
  `ord()` on the parsed string. Plain `Write` calls with direct Unicode text (Cyrillic, CJK, RTL)
  for a brand-new file worked correctly across 36 locale files in this harness — the corruption
  risk documented above is real but narrower than "any non-ASCII in a tool param": it's
  specifically Edit `old_string` matching against complex scripts, and literal `\uXXXX` escape
  sequences getting re-decoded, not plain direct-Unicode `Write` calls for new content.
- **Editing `Localization.cpp`'s per-locale table:** an Edit `old_string` containing a full
  complex-script field (confirmed with Devanagari, T-F03) can silently fail to match even though
  `Read` shows it identical to the file — likely invisible normalization variance. Don't retype the
  translated text as a match target; use a `py` script that anchors on the line's ASCII locale tag
  (e.g. finds `{ L"hi-IN",`) and inserts/edits by string index instead.
  **`py -3` heredocs from the Bash tool silently no-op on a `/tmp/...` path** — native Windows
  Python doesn't resolve Git-Bash's `/tmp`, so a script reports success but writes nothing.
  Use a full Windows-style path (e.g. this session's scratchpad dir) instead.
  **Plain `python` (no `py -3`) fails outright via the Bash tool** — exit code ~49, no real
  error text, even for a trivial script. Always invoke `py -3 <script.py>`, never bare `python`.
  **`py -3 -c "..."` one-liners with an embedded Windows backslash path are fragile** — produced
  `SyntaxError: unterminated string literal`. Always write the script to a real `.py` file
  (Write tool or a heredoc to a Windows-style scratchpad path) and run `py -3 script.py`, never a
  `-c` one-liner with a literal Windows path inside it.
- **Shared WinUI `x:Uid` across elements with different property sets is fatal, not a no-op:**
  giving a `Button` (`.Content`) and a `TextBlock` (`.Text`) the same `x:Uid` applies both resource
  keys to both elements regardless of which properties exist — crashes natively (`0xc000027b`) at
  `InitializeComponent()`. Give every distinct element/property combo its own key (T-F05,
  `DECISIONS.md`).
- **`ListView` already virtualizes by default** (its own `ItemsStackPanel`) — don't add an explicit
  `VirtualizingStackPanel` `ItemsPanel` without a specific reason. Doing so gratuitously can race
  with an async-loaded bound property (a fire-and-forget `Task.Run` setting a value after
  construction), leaving a freshly realized row blank until a forced re-layout (T-F05,
  `DECISIONS.md`).
- **A child element's `MinHeight` (e.g. a `ListView`'s own `MinHeight="80"`) does NOT force a
  Grid's Star-sized (`*`) row to grow past what the row-sizing algorithm allocates** — the
  `RowDefinition` itself needs the `MinHeight`. Enough sibling `Auto` rows can otherwise clamp
  the Star row to 0, and every child inside measures/arranges within zero height regardless of
  data, binding mode, or population timing — cost five separate disproven fix hypotheses before
  being found (T-F106, `DECISIONS.md`).
- **A dotted resw key (`"Foo.Content"`) manually looked up via `_res.GetString("Foo.Content")`
  silently returns an empty string if no element in XAML actually has `x:Uid="Foo"`.** The dotted
  naming convention only gets populated by the XAML framework's implicit `x:Uid` + property-suffix
  lookup — a key that exists in every locale's `.resw` but was never wired to an `x:Uid` is dead,
  and manual `GetString()` won't resolve it either. For any string accessed manually from C# (not
  via `x:Uid`), use a plain, non-dotted key name, matching `StatusReady`/`StatusArchiving`/etc.
  Real bug: `MainViewModel.ArchiveButtonText`/`ExtractButtonText` looked up `"ArchiveButton.Content"`
  and got blank buttons in every locale until renamed to plain `ArchiveButtonLabel` (T-F104).
- **MSIX Packaged COM registration lives entirely under
  `HKLM\SOFTWARE\Classes\PackagedCom\Package\<PackageFullName>\...` and
  `PackagedCom\ClassIndex\<CLSID>`** — namespaced by the full versioned package identity, with
  zero classic `HKCR\CLSID\{...}` entry ever written. Confirmed empirically (T-F55/T-F40): a full
  `HKEY_CLASSES_ROOT` search for the verb ID string returned 0 matches even while installed, and
  `Remove-AppxPackage`/`Add-AppxPackage` cleanly removes/restores both `PackagedCom` subtrees.
  There is no orphan-registry-key risk to chase for this app's shell extension — it never used
  classic `regsvr32`-style registration to begin with.
- **A new leaf `IExplorerCommand` class needs zero `Package.appxmanifest` entry.** Only
  `PakkoRootCommand`'s own CLSID is ever registered there (`com:Class`/`desktopN:Verb`); every
  leaf command (`ArchiveCommand`, `TarArchiveCommand`, etc.) is instantiated internally via
  `Make<T>()` inside `PakkoRootCommand::EnumSubCommands` — confirmed T-F105 by grepping the
  manifest for every existing leaf CLSID and finding none.
- **`System.IO.Compression.DeflateStream` writes literally 0 output bytes for zero-byte input**
  (not a minimal valid empty final block) — confirmed empirically. Any hand-rolled ZIP writer
  that tags a zero-length entry's method as Deflate based on the requested compression level
  (instead of checking actual output length) produces an entry real deflate readers (7-Zip)
  reject as corrupt, while .NET's own lenient reader accepts it silently — invisible to
  `dotnet test` unless checked against an independent reader. Real `ZipArchiveEntry` always
  uses `Store` for empty entries regardless of requested level; match that. Real bug: found via
  on-device NanaZip cross-check on `ZipEntryCompressor` (T-F35 follow-up, `DECISIONS.md`).
- **Diagnosing ZIP format bugs:** `7za.exe l -slt <archive>` (the vendored copy under
  `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/x64/`) dumps per-entry technical fields
  (Method, Size, Packed Size, CRC, Attributes) — the fastest way to see exactly what a hand-rolled
  writer actually produced, and to reproduce a real-world `7za`/NanaZip extraction failure
  without needing NanaZip itself installed.

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/              ← net8.0 class library, no UI deps
│   ├── Archiver.App.Core/          ← net8.0 class library, no WinUI deps (T-F05: ArchiveEntryViewModel,
│   │                                  ArchiveTreeIndex — split out so the flat-to-tree helper is
│   │                                  unit-testable without a WinUI test host)
│   ├── Archiver.App/               ← WinUI 3 app
│   │   └── Strings/en-US/          ← ResW localization
│   ├── Archiver.Shell/             ← net8.0-windows WinExe, shell-triggered ops, no WinUI
│   │   └── NativeProgressDialog.cs ← IProgressDialog COM interop (in-process progress UI)
│   ├── Archiver.CLI/                ← net8.0 Exe (real console), 7z-familiar CLI (T-F09), no
│   │                                   WinUI, standalone self-contained distribution
│   └── Archiver.ShellExtension/    ← C++ COM DLL, IExplorerCommand (T-F61), x64+ARM64
├── tests/
│   ├── Archiver.Core.Tests/        ← xunit (see "Current State" for current count)
│   ├── Archiver.App.Core.Tests/    ← xunit, ArchiveTreeIndex coverage (T-F05)
│   ├── Archiver.Core.IntegrationTests/ ← xunit, real tar.exe via [Integration]/TarBuilder
│   ├── Archiver.Core.PerformanceTests/ ← xunit, T-F114: ZIP perf vs. vendored 7za.exe reference,
│   │                                     [Trait("Category","Slow")], see docs/TESTING.md
│   ├── Archiver.Shell.Tests/       ← xunit (see "Current State" for current count)
│   ├── Archiver.CLI.Tests/          ← xunit, parser/mapper unit tests + a Subprocess/ layer that
│   │                                  Process.Starts the real built exe (T-F09), see docs/TESTING.md
│   ├── Archiver.ShellExtension.Tests/  ← C++ Google Test, run separately (see Build Commands)
│   └── Archiver.Core.Tests.GenerateFixtures/  ← fixture generator
├── docs/                            ← everything except the root-convention files below (T-F126)
│   ├── TASKS.md                     ← active/future tasks
│   ├── TASKS_DONE.md                ← completed tasks archive
│   ├── ARCHITECTURE.md
│   ├── CONVENTIONS.md
│   ├── DECISIONS.md
│   ├── DIAGRAMS.md
│   ├── SPEC.md
│   ├── CLI.md
│   ├── POLICIES.md
│   ├── SIGNING.md
│   ├── TESTING.md
│   ├── XAML.md
│   ├── index.html / uk/index.html  ← public project website (GitHub Pages serves /docs directly)
│   ├── privacy.html                ← Privacy Policy (linked from the app's About dialog)
│   └── assets/                     ← site-only CSS/OG image/brand-mark copy, no build step
├── CLAUDE.md                        ← you are here — stays at root, Claude Code only auto-loads it here
├── SECURITY.md                      ← stays at root — GitHub-recognized community-health file
└── README.md
```

---

## Build Commands

```bash
# Run tests (always works from CLI)
dotnet test --filter "Category!=Slow&Category!=VeryLarge"  # the actual default — plain
                                            # "Category!=Slow" alone does NOT exclude VeryLarge
                                            # tests, since they aren't tagged Slow (confirmed
                                            # 2026-07-17; see the hard-constraint note above)
dotnet test --filter "Category=Slow"    # Zip64 + T-F114 perf tests — real multi-second cost
dotnet test --filter "Category=VeryLarge"  # >4 GiB Zip64 test + T-F114's one-large-file scenarios
                                            # — on demand only, never run automatically

# Build core only
dotnet build src/Archiver.Core

# Generate test fixtures
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures

# Build MSIX (requires Windows SDK)
dotnet publish src/Archiver.App/Archiver.App.csproj \
    /p:Configuration=Release /p:Platform=x64 \
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true \
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

# Publish the standalone Archiver.CLI (T-F09) — independent of the MSIX, no cert needed.
# Must run via the PowerShell tool (uses /p: flags). See scripts/README.md.
.\scripts\Publish-Cli.ps1                    # both architectures -> artifacts/cli/
.\scripts\Publish-Cli.ps1 -Architecture x64  # one architecture only

# Archiver.ShellExtension (C++ COM DLL) — not built or tested by dotnet build/test
# Build via Visual Studio / MSBuild (x64 or ARM64 platform).
# Archiver.ShellExtension.Tests.vcxproj only compiles the two COM-free files (ShellExtUtils.cpp,
# Localization.cpp) directly — it does NOT compile ExplorerCommands.cpp/dllmain.cpp. To validate
# a change to an IExplorerCommand class actually compiles, build the real DLL project too:
# MSBuild src\Archiver.ShellExtension\Archiver.ShellExtension.vcxproj /p:Configuration=Debug /p:Platform=x64
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
> `dotnet build src/Archiver.App` also compiles via CLI (confirmed producing ARM64 output) —
> useful for a quick compile-check on ViewModel/DI changes without opening VS. Full MSIX
> packaging/signing/run still needs Deploy.ps1 or VS.
>
> **A quick `dotnet build` can silently install a stale MSIX.** Its `DeployMsix` post-build
> target reports success even when MSBuild's incremental packaging step skipped repackaging a
> changed DLL into the `.msix` (confirmed via file timestamp — 55 min old after a rebuild that
> changed a XAML-bound command). Don't trust a bare `dotnet build`'s installed package when
> verifying a UI change on-device — run the full `.\scripts\Deploy.ps1` first (it wipes old
> `AppPackages` output before rebuilding).
>
> **`DeployMsix`'s post-build `Add-AppxPackage` also actively fails a Release `dotnet
> publish`/`build` outright (not just silently) on any machine without the signing cert in
> `LocalMachine\TrustedPeople`** — e.g. a fresh CI runner (`0x800B0109`, "root certificate ...
> must be trusted"). Set `$env:PAKKO_DEPLOYING = '1'` before the call and clear it after (see
> `Deploy.ps1`'s own use of this exact guard) to suppress the target entirely. Found T-F122,
> 2026-07-19 — the first real CI run failed on this before it was noticed.
>
> **Correction (recurred 2026-07-18, T-F123, worse than the original symptom):** a bare
> `dotnet build src/Archiver.App/Archiver.App.csproj /p:Platform=x64` was used to verify a
> `MainWindow.xaml.cs` event-handler fix (an `IsBusy` guard on `ArchiveBrowserList_DoubleTapped`).
> The fix appeared to fail identically across three separate rebuild-and-retest cycles — even
> after the title-bar `Pakko — build <timestamp>` freshness check (below) looked correct each
> time. A `File.AppendAllText` trace planted at the top of the handler proved the handler wasn't
> being invoked at all: the installed package was running stale event-handler code despite a
> fresh-looking title-bar timestamp and a "Build succeeded" log. Switching to the full
> `.\scripts\Deploy.ps1 -Thumbprint ...` fixed it on the very next attempt. **Always use
> `Deploy.ps1`, never a bare `dotnet build`, before any on-device verification of
> `Archiver.App` — do not treat the title-bar timestamp as sufficient proof by itself; it can be
> fresh while the packaged binary's actual logic is stale.**
>
> **Never trust build logs alone to prove an on-device check ran against fresh code — always
> have the running window itself prove it.** `Archiver.App`'s title bar shows
> `Pakko — build <yyyy-MM-dd HH:mm:ss>`, read from the running assembly's own file timestamp
> (`MainWindow.xaml.cs` constructor) — not a manually-bumped version, not a build-log claim, but
> the actual installed binary's own on-disk timestamp, visible in every screenshot. Before
> treating any on-device verification result as valid (especially a repeated "still broken"
> result across several fix attempts), confirm this timestamp is within the last few minutes of
> the current time. If it's stale, the deploy didn't actually pick up the latest change and the
> verification must be redone — don't reason from build-log output alone. If a UI element already
> on screen needs a freshness check and the title bar isn't convenient to read in a given
> screenshot, add a similar visible, runtime-computed marker to the relevant page instead of
> trusting logs.
>
> **Testing `scripts/*.ps1` fixes:** these scripts require Windows PowerShell 5.1
> (`#Requires -Version 5.1`). The PowerShell tool runs pwsh 7+, which defaults to UTF-8 and
> will NOT reproduce non-BOM-file ANSI-codepage bugs (see T-F84). To actually verify a fix,
> invoke `powershell.exe` explicitly rather than relying on the tool's default interpreter.
>
> **Running `Deploy.ps1`/any `.ps1` via the Bash tool's `powershell.exe` fails outright** —
> `cannot be loaded because running scripts is disabled on this system` (default Restricted
> execution policy for that invocation path). Use the PowerShell tool instead (its pwsh 7 session
> already runs unrestricted) — don't try `-ExecutionPolicy Bypass` workarounds from Bash.
>
> **Writing a new throwaway script with non-ASCII content (translations, Cyrillic, etc.):**
> the opposite applies — run it via the PowerShell tool's default pwsh 7, NOT `powershell.exe`.
> `powershell.exe` (5.1) decodes a UTF-8-no-BOM `.ps1` via the system ANSI codepage, corrupting
> every non-ASCII character before the script even runs (confirmed T-F105, a 37-locale insert
> script). Only reach for explicit `powershell.exe` when deliberately reproducing a codepage bug.
>
> **PowerShell tool's cwd persists across calls:** if a PowerShell call `cd`s/`Set-Location`s
> into a scratch folder (e.g. while building a test fixture), a later `rm -rf`/`Remove-Item` on
> that folder — even from Bash — fails with "in use" until a PowerShell call explicitly
> `Set-Location`s back out first.
>
> **PowerShell tool's `Add-Type` classes do NOT persist across separate calls** (only cwd does) —
> a `Win32`-style helper class defined in one call is gone in the next ("Unable to find type"). If
> you need it again (e.g. for a follow-up screenshot), redefine the whole `Add-Type` block in the
> same call that uses it, not just once at the start of a multi-call sequence. Also: `Get-Item` on
> a registry path containing `{...}` (a GUID/CLSID) silently returns nothing unless you pass
> `-LiteralPath` instead of the default `-Path` — curly braces are wildcard syntax otherwise.
>
> **PowerShell tool's *initial* cwd is not guaranteed to be the repo root** — a bare
> `dotnet test`/`dotnet build` can fail with `MSB1003: Specify a project or solution file`.
> Prefix with `Set-Location "<repo-root>";` when running dotnet commands via the PowerShell tool.
>
> **Monitor tool commands run in POSIX/Git-Bash syntax**, even when polling a Windows path —
> use `[ -f "/c/Program Files/..." ]`, not `Test-Path`, or the wait-loop never fires.
>
> **Pass `& $exe` arguments as separate array elements, never manually quoted inside a string** —
> `& $exe $path1 $path2`, not `` & $exe "`"$path1`"" ``. The latter embeds literal `"` characters
> into the argument itself once PowerShell's own tokenizer is done, corrupting the path (confirmed:
> `IOException` with a visibly quote-mangled path, T-F142 on-device check). Let PowerShell's own
> array-argument passing handle spaces — don't hand-roll quoting.
>
> **Windows MCP (`mcp__windows__*`) synthesizing a WinUI `DoubleTapped` gesture is coordinate-
> space sensitive, not fundamentally unreliable.** `mouse_control`'s `double_click` failed across
> ~6 attempts in one session (T-F98/T-F109) when driven by `windowHandle`-relative coordinates or
> a coordinate guess. The combination that works reliably (confirmed T-F110, a full 4-level
> Archive Browser drill-down entirely via automation): call `ui_find` for the row to get its
> `click` coordinates, then pass those coordinates straight to `mouse_control`'s `double_click`
> with `target: "primary_screen"` and no `windowHandle` at all — same fix T-F107 found for plain
> single clicks (see that entry above), it turns out to also fix double-clicks. Explorer's
> right-click context menu (Shift+F10) remains unconfirmed either way. If a double-click still
> doesn't register after trying the `ui_find` + `primary_screen` combination once, then fall back
> to asking the user to reproduce manually rather than burning further attempts.
>
> **`winget install`/`uninstall` needing elevation fails non-interactively** with
> `0x800704c7` ("canceled by the user") — the UAC prompt has nothing to click it. Retry once
> and ask the user to approve the UAC prompt that appears; the retry succeeds.
>
> **To verify a new/changed `IExplorerCommand`'s behavior via `windows` MCP, don't fight
> Explorer's right-click menu automation** (already noted above as unconfirmed) — instead launch
> the installed `Archiver.Shell.exe` directly with the exact args that command's `Invoke()`
> constructs (e.g. `--open-ui --browse "<path>"`). This exercises the identical
> `Archiver.Shell`→`pakko://`→`Archiver.App` pipeline the real menu click would trigger, minus
> only the COM click itself (covered separately by `Archiver.ShellExtension.Tests`). Confirmed
> T-F03.
>
> **`Archiver.Shell.exe`'s CLI commands (`--archive`, `--extract-here`, `--extract-folder`,
> `--test`, `--hash`) take only source/archive paths — never an explicit destination.** The
> destination is always auto-computed (folder-of-source for extract, `<name>.zip` next to the
> source for archive — same naming Explorer's "Add to X.zip" verb produces). Passing an extra path
> as a destination gets silently treated as another source/archive path instead (T-F142).
>
> **Any Pakko command that shows a native modal (`IProgressDialog`, `MessageBoxW` result dialogs —
> `--archive`, `--extract-*`, `--test`, `--scan`, `--hash`) blocks forever if invoked directly
> (`& $exe args`) from the PowerShell tool** — the call never returns because the dialog waits for
> a click. Launch via `Start-Process -FilePath $exe -ArgumentList @(...)` (detached) instead, then
> poll for the dialog with `EnumWindows`/`GetWindowThreadProcessId` filtered to the child PID, read
> its result text via `EnumChildWindows`, and dismiss with `SendMessage(hWnd, 0x00F5, ...)`
> (BM_CLICK) on the OK button's handle. If a call hangs anyway, `taskkill /F /IM
> Archiver.Shell.exe` clears the stuck modal before retrying (T-F151/T-F153 smoke tests).
>
> **A build failing with a file-lock-shaped error** (`MSB3231`/`Access to the path ... is
> denied` on something under `bin`/`obj`/`AppPackages`) — first try `dotnet build-server
> shutdown` (kills lingering MSBuild/VBCSCompiler nodes that can hold output handles open)
> before assuming a stuck folder needs a version bump (see the `AppPackages` wedge note above).
>
> **`git stash push -u` can silently half-fail**: if cleaning untracked content hits
> `Permission Denied` on an unrelated empty directory (e.g. leftover build-artifact folders),
> the stash entry is still created correctly, but the working tree may NOT actually revert —
> `git status` can still show the same modified files. Always verify with `git status` after
> any `stash push`; if changes persist, finish the revert manually with `git checkout --
> <files>` (the stash already has a safe backup, so this is not destructive).
>
> **GitHub Actions CI (`.github/workflows/build.yml`, T-F122):**
> - `gh run view --job=<id> --log`/`--log-failed` only returns output **after the whole workflow
>   run completes**, not just that one job — "run ... is still in progress" otherwise, even if the
>   specific job you want logs for already finished.
> - `gh attestation verify` (and possibly other `gh` subcommands) can print nothing to stdout/
>   stderr yet still exit 0 via the Bash tool — pass `--format json` for reliable output instead
>   of trusting empty plain-text as a failure signal.
> - `vs_installer.exe modify --add <component>` is unreliable on GitHub-hosted Windows runners —
>   confirmed it returns exit code 0 in under 30ms regardless of `--wait`/`--nocache`/running it
>   twice, without actually installing anything. Don't trust it for CI component installation;
>   pin a runner image that already ships what you need instead (see next point).
> - The `windows-latest` GitHub Actions runner label is not a stable OS pin — it silently moved
>   from the `windows-2022` image to `windows-2025` mid-project (confirmed T-F122, 2026-07-19),
>   breaking ARM64 C++ builds that worked before. Pin an explicit version (`windows-2022`) for any
>   job where toolchain reproducibility matters.
> - **`gh workflow run build.yml --ref <tag>` (workflow_dispatch) is safe to run again on a tag that
>   already has a push-triggered release** — `build-msix`/`build-cli`/`release` all correctly report
>   `skipped` (not a conflict/failure) on a manual dispatch, since their own `if:` conditions gate
>   them to the push/tag-trigger path only; just `build-store-msix`/`bundle-store-msix` actually
>   run. Confirmed T-F142/v1.4.7 — no duplicate-release error, no wasted red X.
> - **`gh release download`/other repo-scoped `gh` commands need `--repo pakkoapp-oss/pakko`**
>   when the Bash tool's cwd isn't inside the git repo (e.g. downloading a release artifact into
>   the scratchpad for a real-artifact smoke test) — otherwise it fails with a misleading `fatal:
>   not a git repository`, not a permissions/network error (T-F151/T-F153 smoke tests).
>
> **Windows App Certification Kit (`appcert.exe`) requires elevation** — a bare invocation fails
> with "requires elevation." Run it via `Start-Process -Verb RunAs -Wait` from the PowerShell
> tool. Its report is XML: `<REPORT OVERALL_RESULT="...">`, per-check `<TEST><RESULT>PASS/FAIL
> </RESULT><MESSAGES><MESSAGE TEXT="..."/></MESSAGES></TEST>` — parse for `FAIL`/`WARNING` rather
> than reading the whole report by eye.
>
> **Deploy shortcuts:**
> Release build in VS triggers `Deploy.ps1 -DeployOnly` automatically (post-build event).
> For manual deploy from terminal: `.\scripts\Deploy.ps1` (full build + sign + install)
> or `.\scripts\Deploy.ps1 -DeployOnly` (install only, no build).
>
> **Distinguishing an installed dev build from a Store build (or confirming only one is present):**
> `Get-AppxPackage *Pakko* | Select-Object Name, PackageFullName, Publisher, SignatureKind,
> InstallLocation` — `Publisher: CN=Pakko Dev` + `SignatureKind: Developer` is the local sideload;
> a Store install shows a different Publisher and `SignatureKind: Store`. Combine with `Get-Item
> <InstallLocation>\Archiver.App.exe | LastWriteTime` vs. current time to confirm freshness,
> complementing (not replacing) the title-bar build-timestamp trick above.
>
> **Localization (`Strings/<locale>/Resources.resw`, T-F91):** `Package.appxmanifest`'s
> `<Resource Language="x-generate"/>` auto-detects every `Strings/<locale>/` folder at build —
> no manual `<Resources>` edit needed when adding a locale. A key missing from a locale's
> `Resources.resw` falls back to `en-US` automatically, so non-translatable keys (URLs) should
> be omitted from locale files, not duplicated. Verify a new locale is wired without opening VS:
> `dotnet build src/Archiver.App/Archiver.App.csproj /p:Platform=x64`, then check
> `bin/x64/Release/net8.0-windows10.0.17763.0/win-x64/AppxManifest.xml` for the `<Resource
> Language>` entries.
>
> **25+ locale resource packages force a `.msixbundle`, not a flat `.msix`** (found 2026-07-07
> right after T-F91 added 24 locale folders, taking the app from 1 to 25 total resource
> packages). MSBuild's packaging pipeline needs a bundle once there are enough per-language
> resource packages that the device must selectively install a subset — a flat `.msix` can't
> hold multiple resource-qualified sub-packages. `Deploy.ps1`'s "locate the final package" step
> only searched `-Filter '*.msix'`, so it silently found nothing and failed with `No .msix file
> found under ...AppPackages` even though `dotnet publish` had already succeeded and produced a
> real `.msixbundle`. Fixed by widening that search to `-Include '*.msix', '*.msixbundle'` —
> `Add-AppxPackage` installs either directly. If you ever reduce the shipped locale count back
> down, expect the output to flip back to a flat `.msix` — both are handled now.
>
> **Correction (2026-07-15, 37 locales):** the 25-locale threshold above didn't hold — adding 12
> more locales (37 total) still produced a flat `.msix`, not a bundle. Don't assume a specific
> locale count triggers the switch; `Deploy.ps1`'s dual `.msix`/`.msixbundle` search already
> handles either output, so this isn't actionable — just don't be surprised either way.
>
> **A stuck `AppPackages\Archiver.App_<version>_Test\`/`obj\...\PackageLayout\` folder can look
> like a process lock but isn't one.** Hit this the same day: `dotnet publish` failed with
> `MSB3231: Unable to remove directory ... Access to the path ... is denied` on a specific
> version's output folder — reproducible even after `dotnet build-server shutdown`, killing
> stray `dotnet`/`MSBuild`/`dllhost.exe` processes, and a full machine reboot. Reducing the
> locale count also didn't help (a real experiment, not just a guess — ruled it out cleanly).
> What actually worked: bump `Package.appxmanifest`'s `Version` to get a **fresh** output
> folder name, and separately clean the `obj\` folder (not just `AppPackages\`) — something in
> that specific version's `obj`/`AppPackages` state was wedged, not a live handle. Don't spend
> time chasing process locks for this error; a version bump + `obj` clean is faster and fixed
> it outright.
>
> **Correction (recurred a 3rd time, 2026-07-07):** the lesson above isn't universal — distinguish
> a wedged/stale folder from a live-handle race before reaching for a version bump. Test: if a
> manual `rm -rf`/`Remove-Item` on the "locked" path succeeds immediately right after
> `dotnet publish` fails on that same path, it's a transient live handle (Search Indexer is the
> top suspect), not a wedged folder — a version bump won't reliably fix this variant.
> `Deploy.ps1` now tolerates this specific shape (MSB3231 on `AppPackages`/`PackageLayout` with a
> valid `.msix` already written) instead of aborting a good build — see T-F96 in `docs/TASKS.md`.

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

- Do not re-implement anything from `docs/TASKS_DONE.md`
- Do not add NuGet packages to `Archiver.Core` (zero dependencies)
- Do not modify `CLAUDE.md`, `SECURITY.md` unless explicitly asked
  (a Plan that merely *proposes* editing one of these two is not itself "explicitly asked" —
  get separate explicit confirmation before touching either, even after plan approval)
- Do not implement features not listed in `docs/TASKS.md` or `docs/SPEC.md`
- Do not use `Thread.Sleep` — use `await Task.Delay` if needed
- Do not use `static` mutable fields in services
- Do not use legacy `IContextMenu` shell extension — use `IExplorerCommand`
- Do not call `tar.exe` via PATH — always absolute path `C:\Windows\System32\tar.exe`
- Do not extract tar/RAR/7z formats in-process — only via `tar.exe` subprocess

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

## Known test gaps — manual verification required

- **NativeProgressDialog (Archiver.Shell)** — the `IProgressDialog` COM wrapper is not covered
  by automated tests (COM UI object, not unit-testable). Manual verification required: progress
  bar and status line update during Extract/Archive, Cancel button stops the operation.
- **Observed test flakiness (2026-07-07):** `Extract_ValidUnicodeFilenames_Succeeds` and
  `ExtractAsync_ZipWithMotw_PropagatesZoneIdentifierToExtractedFiles` each failed once in a
  run, then passed immediately on rerun in isolation — looks like parallel-execution timing
  noise, not a real regression. If a test fails once, rerun before treating it as caused by
  your change.
  **Recurred 2026-07-18** (1–5 `Archiver.Core.IntegrationTests` failures in a full repo-wide
  `dotnet test` run, always passing in isolation and on a plain rerun) right after
  `Archiver.CLI.Tests`' new `Subprocess/` layer (T-F09) started launching real
  `TarSandboxedService`-driven subprocesses concurrently with `Archiver.Core.IntegrationTests`'
  own sandbox tests — same shared `Pakko.TarSandbox` AppContainer profile/quarantine ACL under
  more concurrent load than before. Same rule applies: rerun once before treating a failure here
  as a real regression.
  **Confirmed the same flakiness also reproduces in GitHub Actions CI, not just on a local dev
  machine (2026-07-19, T-F122's `build.yml` `test` job):** a full `dotnet test` run failed on
  `TarSandboxScopeTests.RunAsync_PreScanThenExtractionWithinOneScope_BothSucceed` and
  `TarSandboxedServiceCompressedFormatsTests.ExtractAsync_TarGz_SeparateFoldersMode_StripsCompoundExtensionForSubfolderName`
  (2 of 60 `Archiver.Core.IntegrationTests`, every other project 100% green) on the very first
  real CI run after both this doc's prior 2026-07-18 entry and T-F117/T-F118 shipped, then passed
  100% clean on an immediate `gh run rerun --failed` with zero code changes in between — same
  root cause (AppContainer/Job-Object contention under CI's own parallel test execution), not a
  new bug.
  **Root-caused and fixed 2026-07-24 (T-F130):** all 10 `Archiver.Core.IntegrationTests` classes
  that drive real AppContainer/Job Object/quarantine ACL calls were racing against *each other*
  under xUnit's default parallel-by-class execution — grouped into one
  `[Collection("TarSandbox", DisableParallelization = true)]` (see `docs/TESTING.md`) so they run
  sequentially relative to each other while still running in parallel with unrelated projects.
  **Confirmed in a real CI run on the actual fix** (run `30037580723`, 2026-07-23: 60/60,
  0 failures, 8s — not just the local `dotnet test` pass or the earlier pre-fix "clean rerun,"
  which only demonstrated the intermittent-failure pattern, not this fix's effect). If this specific
  flakiness class recurs
  recurs anyway, the likely remaining vector is cross-*project* contention (`Archiver.CLI.Tests`'
  `Subprocess/` layer launching real sandboxed subprocesses concurrently with this project, not
  just within it) — that would need a similar fix scoped across both projects, not assumed already
  covered by the single-project Collection above.
  **Recurred 2026-08-11 (T-F162), same predicted vector, different symptom:** the v1.4.11 release
  CI run failed `TarSandboxedServiceExtractTests.ExtractAsync_SingleArchive_
  ReportsRealBytesTransferredNotHardcodedZero` and
  `TarSandboxedServiceCompressTests.CompressAsync_TarWithMultipleFiles_ReportsRealFilenameAndByteTotals`
  twice in a row (`Percent` 99 instead of the expected terminal 100), then passed clean on a third
  rerun with zero code changes — not an AppContainer-setup race this time, but `System.Progress<T>`
  posting its callback via `ThreadPool.QueueUserWorkItem` (no captured `SynchronizationContext` in
  a console test host) racing against each test's own bounded 5-second wait-loop. An isolated probe
  (saturate the ThreadPool with 5,000 blocking work items, then call `Progress<T>.Report` and time
  the callback) confirmed the callback can be delayed past 30 seconds under contention, not just a
  few milliseconds — exactly the shape of a CI runner under full-suite parallel load. Fixed by
  switching both tests to the same hand-rolled synchronous `IProgress<T>` fake already used this
  way in ~8 other files across `Archiver.Core.Tests` (e.g.
  `TarSandboxedServiceProgressPollingTests`), which reports on the calling thread with no
  marshaling at all — removes the race by construction rather than widening the timeout. See
  `docs/DECISIONS.md`'s T-F162 entry for the full probe methodology.
- **T-F143 SonarCloud coverage triage (2026-08-06) — categories left deliberately uncovered by
  design, not by oversight:** `ExplorerLauncher`'s OS-side-effect callers (4 call sites — opening
  a real Explorer window isn't something a unit test should trigger); native Win32/subprocess
  fault-injection paths (`SandboxedProcessLauncher`, `TarSandboxedService.RunUnsandboxedTarAsync`'s
  `process.Kill()` cleanup — forcing these requires simulating OS-level failures, not worth the
  brittleness); best-effort estimation-helper `catch` blocks; duplicate `UnauthorizedAccessException`/
  generic-`Exception` catch variants where only the `IOException` sibling is tested (same code
  shape, marginal value); and `ZipArchiveService.ArchiveSingleSeparatePathAsync`'s "zero entries
  written" branch (line ~448) plus `ParallelSingleArchiveWriter`'s CAS-retry-loop race — both left
  open questions, the exact real-world trigger for the former wasn't confirmed within that task's
  budget (T-F66 already makes plain empty folders write a placeholder entry, so what else still
  reaches it is unclear). See `docs/TASKS.md`'s T-F143 entry for the full triage and the 40 tests
  that *were* added to close the actual gate-blocking gaps.

---

## Windows Packaging Best Practices

Root-cause detail for the first six points below lives in `docs/DECISIONS.md` ("MSIX Satellite EXE
Packaging", "MSIX Signing", "Context Menu Appeared But Commands Did Nothing") — this is the
quick-reference list only, to avoid known failure modes without re-reading the full postmortems:

- Satellite EXEs: `Content Include` in `Archiver.App.csproj`
  (`Condition="'$(GenerateAppxPackageOnBuild)'=='true'"`), never `BeforeTargets`/manual `MakeAppx`
- MSIX signing: `AppxPackageSigningEnabled=true` + `PackageCertificateThumbprint` in
  `dotnet publish`, never manual `SignTool` (`ERROR_BAD_FORMAT` on MSIX)
- Self-signed certs: pass `-Provider "Microsoft Strong Cryptographic Provider"` to
  `New-SelfSignedCertificate` (default CNG keys break SignTool)
- Never use `.wapproj` with multiple WinUI 3 apps (duplicate `Files/App.xbf` PRI entries)
- Every EXE launched via `CreateProcess` from outside its own package needs its own
  `<Application>` entry in `Package.appxmanifest` (`EntryPoint="Windows.FullTrustApplication"`,
  `AppListEntry="none"` to hide it) — otherwise `ERROR_ACCESS_DENIED`
- Satellite EXEs must be built self-contained (`--self-contained`, not `--no-self-contained`) —
  a framework-dependent apphost in an MSIX package has no runtime to fall back on; also needs its
  own `.dll`/`.deps.json`/`.runtimeconfig.json` via `Content Include`, not just the bare `.exe`

Two more, not duplicated elsewhere:

- **A hidden satellite `<Application>` (`AppListEntry="none"`, e.g. `Archiver.Shell.exe`'s entry)
  triggers a Store "headless app" rejection.** Requires a separate account-level
  `HeadlessAppBypass` waiver request from Microsoft — not a manifest fix, since removing
  `AppListEntry="none"` would break the intended hidden-process UX. Budget real calendar time for
  Microsoft's response before assuming a Store submission is close to done.
- **`Package.appxmanifest`'s `Version` 4th segment (revision) must be `0` at Store submission
  time** — a nonzero revision (e.g. from `Deploy.ps1`'s auto-bump) is rejected outright. Rebuild
  with `-SkipVersionBump` (or manually reset to `X.X.X.0`) before uploading to Partner Center.
- **`src/Archiver.App/Assets/pakko-icon.svg` is the canonical vector source for every brand-mark
  asset** (Square44x44/150x150Logo, Wide310x150Logo, SplashScreen, StoreLogo). Regenerate raster
  assets from this SVG's real geometry, never by upscaling an existing `.png` — confirmed via a
  real regression this session (upscaling silently lost rounded corners present in the true
  original, caught only by checking `git show HEAD:<path>` pixel values, not by eyeballing output).
- **A satellite project's `TargetFramework` is embedded literally in other projects' `Content
  Include` paths and in `Deploy.ps1`.** Bumping `Archiver.Shell.csproj`'s TFM (e.g. to
  `net8.0-windows10.0.17763.0` for WinRT APIs) silently moved its real build output to a new
  folder, but `Archiver.App.csproj`'s four `Content Include` items and `Deploy.ps1`'s
  `$shellExeSourcePath` kept pointing at the old TFM segment — `Deploy.ps1` kept reporting
  "installed successfully" with a fresh version number and a fresh `.exe` apphost timestamp while
  silently installing a stale managed `.dll`. Caught only by comparing the `.dll`'s file *size*,
  not the `.exe`'s timestamp (the apphost stub barely changes across builds). Grep every
  `net8.0-windows`-style TFM literal across `.csproj`/`.ps1` files before changing any project's
  TFM, not just the one project's own file (T-F128).
- **A COM surrogate (`dllhost.exe`) hosting `Archiver.ShellExtension.dll` can lock the DLL/PDB**
  after testing the context menu, causing `C1041`/file-in-use errors on the next rebuild. Run
  `taskkill /F /IM dllhost.exe` (or find the specific PID) before rebuilding if this happens.
  The same surrogate can also lock unrelated scratch files/folders touched during that
  right-click (e.g. a smoke-test directory) — same fix if cleanup fails with "in use".
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
- The version format is `1.4.0.X` — only the last segment changes.
  Example: `1.4.0.0` → `1.4.0.1`. (Bumped from `1.2.0.x` 2026-07-17 — this is `Package.appxmanifest`'s
  internal MSIX packaging number, tracked independently of the roadmap version labels in
  `docs/SPEC.md`; it was already `1.2.0.x` throughout all of v1.3's development, so don't read the
  first three segments as a live indicator of roadmap completeness.)
- Do not change the first three segments unless explicitly instructed.
- If bumping manually (e.g. outside `Deploy.ps1`), only edit the `Version` attribute on
  `<Identity>` — do not touch `MinVersion`/`MaxVersionTested` on `TargetDeviceFamily`.
- Full build+sign+install command (user's dev cert thumbprint):
  ```powershell
  .\scripts\Deploy.ps1 -Thumbprint "D2EC5F2C451ED0EBE94B8168A68E5B813954CC75"
  ```
- **The vendored `7za.exe` test dependency (T-F114, `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`)
  never enters this pipeline.** `Deploy.ps1` only publishes `src/Archiver.App`; nothing under
  `tests/` is packaged, signed, or installed. See `SECURITY.md`'s "Vendored 7-Zip" section if this
  ever needs re-confirming.
- **Cutting a public release (a `vX.Y.Z` git tag, distinct from the internal MSIX packaging
  number above):** before the `chore(release): bump to vX.Y.Z` commit, add a new section to
  `CHANGELOG.md` (newest first) listing the `T-Fxx` tasks completed since the previous tag, in
  plain language — check `docs/TASKS_DONE.md`/`git log <prev-tag>..HEAD` for what actually shipped,
  don't guess from memory. Keep it in the same commit as the version bump. `CHANGELOG.md` is the
  canonical, human-browsable release history; `.github/RELEASE_NOTES_TEMPLATE.md` stays a static
  per-release download blurb, not a task list.
  **The section header format is load-bearing, not just style (fixed 2026-08-04):** `build.yml`'s
  `release` job extracts the new tag's own `## v<tag> — <date>` section (sections split on a bare
  `---` line) and prepends it to the actual GitHub Release notes, ahead of the static template —
  before this fix, every prior release's GitHub-side notes were the template alone, with no
  changelog content at all. Get the header wrong or omit a tag's section and that release's notes
  silently fall back to template-only again.

---

## Workflow Tips

- **Benchmarking new CPU-bound parallel code in a fresh `dotnet test` process can show wildly
  bimodal timing** (e.g. 0.36s vs 1.2s+ for the identical 300 MB CRC-32 chunk-hash) — root cause
  was .NET's default `ThreadPool` thread-injection ramp-up (~1 new thread per ~500 ms under
  demand), not the algorithm. Fix: a one-time `ThreadPool.SetMinThreads(Environment.ProcessorCount,
  ...)` before the parallel section, and prefer synchronous `Parallel.For`/`RandomAccess.Read`
  over `Parallel.ForAsync`/`RandomAccess.ReadAsync` for CPU+I/O-bound chunked work — avoids
  async-state-machine/completion-port scheduling entirely (same reasoning as the `useAsync: false`
  `FileStream` convention already noted above). See `FileHashService.
  ComputeFileCrc32ParallelAsync`/`Crc32.Combine` (T-F128) for the working pattern.
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
- **`docs/TASKS.md`'s task-graduation edits** (moving completed entries to `docs/TASKS_DONE.md`) tend to
  land in large diff hunks that intermingle several unrelated tasks — `git add -p` can't
  cleanly split one task's doc update out of such a hunk. When committing narrowly, stage
  specific files/whole hunks deliberately, or commit the doc consolidation separately.
- **Debugging via Pakko's log file:** when running as an installed MSIX, the log is NOT at the
  plain `%LOCALAPPDATA%\Pakko\logs` `LogService.cs` constructs — MSIX virtualizes
  `LocalApplicationData` per-package. Find it at
  `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Pakko\logs\pakko.log`
  (get `<PackageFamilyName>` via `Get-AppxPackage *Pakko*`).
- **Editing unicode-heavy docs (`docs/DIAGRAMS.md` mermaid blocks, `docs/DECISIONS.md`) with the Edit
  tool:** a multi-line `old_string` spanning several em-dash (—)/arrow (→) characters can
  silently fail to match even though `Read` shows it verbatim. Split into smaller edits
  (isolate one such character per edit) to work around it.
- **`docs/DIAGRAMS.md` mermaid blocks are never auto-validated — nothing in this repo's workflow
  renders them.** After editing, run each block through `npx @mermaid-js/mermaid-cli` (`mmdc -i
  diagram.mmd -o diagram.svg`) before considering the edit done. A bare `;` or an unescaped
  `"quoted phrase"` inside unquoted label/message/transition text breaks the parser in
  sequence/state/flowchart diagrams alike — use `—` instead of `;`, and quote the whole label if
  it needs literal parentheses or quotes.
