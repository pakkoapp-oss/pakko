# CHANGELOG.md — Release History

Human-readable summary of what shipped in each tagged release. One section per version tag,
newest first. This file starts at `v1.4.1` — earlier releases (`v1.0.0`, `v1.1.0`, `v1.4.0`) are
not backfilled; see `docs/TASKS_DONE.md` and `git log` if you need that history.

Each entry lists the `T-Fxx` tasks completed since the previous release tag, in plain language —
not a re-statement of `docs/TASKS_DONE.md`'s full acceptance-criteria detail. See `docs/TASKS_DONE.md` for
the technical account of any task named here.

---

## v1.4.12 — 2026-08-11

Localization follow-up to v1.4.11's new conflict dialog, plus a CI stability fix.

- **T-F163** — Explorer's "Extract Here"/"Extract to"/"Extract" summary dialogs (shown after a
  Skip, or after any error) were hardcoded English regardless of the active Windows UI language —
  found by a user running under Ukrainian right after using v1.4.11's new conflict dialog. Now
  localized across all 37 supported languages, matching every other native dialog in this app.
- **T-F162** — Fixed intermittent CI test failures in two archive-extraction progress-reporting
  tests, caused by a background-thread timing race under heavy CI load (not a real product bug).

---

## v1.4.11 — 2026-08-11

Extraction-conflict release — a new interactive conflict dialog for the Explorer extraction
commands, two wrapping-behavior corrections, a shared decision-table refactor, and a crash fix
found via real use of the new dialog on the same day it shipped.

- **T-F154** — extracting an archive created from a single file used to land it inside a
  same-named wrapper folder (`<Destination>\photo\photo.png`) instead of directly
  (`<Destination>\photo.png`) when using the App's default Extract button or Explorer's "Extract
  Here." Now matches a real archiver's behavior.
- **T-F156** — "extract to one flat folder" mode still wrapped a genuinely multi-root archive
  (several files, no common folder) in a subfolder, contradicting its own "no wrapper, ever"
  contract. Fixed to never wrap in this mode; the per-archive Explorer "Extract Here" mode still
  wraps, unchanged.
- **T-F157 / T-F158** — the destination-conflict and smart-foldering decisions that
  `ZipArchiveService` and `TarSandboxedService` used to hand-duplicate (and had to keep manually
  in sync across T-F154/T-F156 in a single day) are now two shared, tested decision tables
  (`ExtractionDestinationPlanner`, `DestinationConflictResolver`) — a pure refactor, no behavior
  change.
- **T-F155** — `Archiver.Shell`'s three Explorer extract commands now show a real interactive
  Overwrite/Rename/Skip + "apply to all" conflict dialog, matching the WinUI app's own dialog.
  Previously these commands resolved conflicts silently with no prompt at all.
- **T-F161** — found via real use of T-F155 the same day it shipped: extracting an archive with
  files plus a folder, choosing Rename + "apply to all" on a conflict, could crash with an
  "Access is denied" error on a temporary staging folder — files extracted, but the archive's own
  folder and the operation as a whole did not complete. Caused by a Windows filesystem behavior
  where a whole-folder move fails outright if any single file inside is briefly locked by another
  process (antivirus, cloud sync, Search Indexer); now falls back to moving files individually
  when that happens, and no longer leaves a stray temp folder behind on any other failure either.

---

## v1.4.10 — 2026-08-11

Bug-fix release — a real archive-creation defect found during a broad pre-Store-submission
smoke test.

- **T-F153** — a source path ending in a trailing directory separator (typed or tab-completed in
  a terminal — never produced by the GUI's own folder picker or drag-and-drop) silently corrupted
  archive creation two ways: entries were written without their real parent folder name, and the
  Explorer "Add to X.zip" one-click command (and the equivalent CLI switch) placed the new archive
  **inside the folder being archived** instead of next to it, naming it generically instead of
  after the real source. Both are now fixed at the source.

---

## v1.4.9 — 2026-08-10

Small follow-up to v1.4.8's antivirus scanning — raises the per-entry size limit and improves
progress feedback for large scans.

- **T-F151** — the "Scan for threats" size cap was raised from 64 MiB to 256 MiB per entry. An
  empirical spike compared AMSI's real disk-streaming mechanism (`IAmsiStream`) against the
  simpler, already-shipped buffer-based call against the actual registered antivirus provider —
  the streaming approach turned out to fail above ~16-20 MiB in practice, while the existing
  method scanned real content up to 256 MiB with no error, so the existing mechanism was kept and
  its limit raised instead. Scan progress now also shows the name of the specific file being
  scanned (not just the containing archive), since a single large entry's scan can now take
  several seconds.

---

## v1.4.8 — 2026-08-10

Security feature + code-quality release — AMSI-based threat scanning for archives, a large
SonarCloud triage/refactor pass, and mandatory static-analysis gating for every language in the
repo.

- **T-F146** — new "Scan for threats" action for archives (Explorer context menu + Archive
  Browser), via AMSI (`amsi.dll`) — works with whatever antivirus/EDR is registered on the
  machine, no elevation required. ZIP entries scan entirely in memory; tar-family archives reuse
  the existing AppContainer sandbox up through the quarantine stage only (no extraction to
  destination). Reports `Inconclusive` rather than a false "clean" when no AMSI provider is
  registered. Localized across all 37 locales. Two same-day follow-up fixes: the Scan button's
  enabled state wasn't being re-evaluated (stayed permanently disabled), and the button was moved
  next to About instead of crowding the Extract Selected/Extract All pair — it's a diagnostic
  action, not a primary one.
- **T-F147** — triaged the SonarCloud findings backlog (134 issues) down to 44 (42 deferred as
  their own task, 2 accepted TODOs) — every real cognitive-complexity hotspot was refactored,
  including `ZipArchiveService.ArchiveAsync` (was the single highest-complexity method in the
  whole report). Also fixed a temp-folder cleanup bug found the same week: a hidden per-operation
  chunk folder was regularly left behind, empty, after archiving, due to an unretried delete
  losing a transient file-lock race (antivirus/cloud-sync/Search Indexer) — now retries.
- **T-F149** — raised automated test coverage on new code to clear SonarCloud's Quality Gate
  (86.3%, comfortably above the 80% threshold) by excluding a handful of files that are
  structurally unreachable by the test runner (real Explorer-launching side effects, a spawned
  child process's own entry point) rather than writing tests that could only exercise a mock.
- **T-F150** — static analyzers and linters now run on every build, for every language in the
  repo (C#, C++, PowerShell), each gated to fail the build/CI on any new, undocumented finding.
  Found and fixed 2 real bugs along the way: two COM entry points in the shell extension were
  missing SAL annotations the Windows SDK declares for them, and a test helper was silently
  ignoring a COM initialization failure. Also fixed 4 PowerShell deploy/build scripts that were
  missing a UTF-8 byte-order mark, the same encoding-corruption risk class an earlier release
  (T-F84) had already been bitten by once.

---

## v1.4.7 — 2026-08-04

Feature + bug-fix release — real progress reporting for archive creation and extraction, plus a
live speed readout.

- **T-F129** — Pakko's Microsoft Store listing is now live
  (https://apps.microsoft.com/detail/9p5mw010d8pr) — certification passed and the listing was
  confirmed genuinely public via a real `winget install --source msstore`, followed by a
  functional smoke test (test/extract/archive, including `.7z`/`.rar` through the AppContainer
  sandbox) against that exact Store-installed package.
- **T-F140** — fixed archive-creation progress reporting for both ZIP and TAR, found from a real
  user report of a frozen-looking dialog on a large multi-folder source. ZIP's parallel
  compression pipeline was passing no progress at all into its temp-file path; TAR's percent was
  computed from the wrong (too-small) denominator. Both now report real, live bytes and the
  current filename during compression.
- **T-F141** — hardened `ParallelSingleArchiveWriter`'s chunk-temp-file handling: the writer
  thread's read-back of a finished chunk requested needless exclusive access, so a transient
  external file lock (antivirus, cloud-sync client, Search Indexer) could abort an entire archive
  operation instead of just one file. Narrowed to a shared read.
- **T-F142** — TAR extraction now reports real byte progress instead of always showing 0 bytes
  (`ITarService.ExtractAsync` gained the same `IProgress<ProgressReport>` contract ZIP already
  had), and both the app's status line and the Explorer right-click progress dialog now show a
  live compression/decompression speed readout, backed by a new shared, tested speed-sampling
  helper.

---

## v1.4.6 — 2026-08-01

Bug-fix release — restores full localization to the shipped MSIX. Affects `v1.4.5` (and likely
earlier CI-built releases) too: only English shipped despite 37 supported locales.

- **T-F139** — the MSIX packaging pipeline's default auto-resource-package split
  (`AppxBundleAutoResourcePackageQualifiers=Language|Scale|DXFeatureLevel`) tried to carve each
  locale's resources into its own resource-only package, then silently failed to actually produce
  them — the shipped package ended up with only `en-US`, even though every locale was correctly
  detected during the build. Confirmed on a truly clean build (not just CI) via
  `docs/DECISIONS.md`'s T-F139 entry. Fixed by dropping `Language` from the auto-split qualifier
  list, so all 37 locales are embedded directly in the single package again, matching the
  pre-regression shape. If you installed `v1.4.5` (or possibly earlier), update to this release to
  restore non-English UI.

---

## v1.4.5 — 2026-08-01

CI/tooling release — no user-facing app changes. Builds Pakko's first Microsoft Store submission
pipeline (T-F129).

- **T-F129** — added `build-store-msix` and `bundle-store-msix`, two new `workflow_dispatch`-only
  CI jobs that produce a single, correctly-identified multi-architecture (`x64`+`arm64`) MSIX
  bundle signed with Pakko's reserved Partner Center Publisher identity — distinct from the
  tester-facing `CN=Pakko Dev` cert used everywhere else. Fixed four real, previously-undiscovered
  MSIX/Partner Center packaging quirks along the way (Publisher gets silently rewritten to match
  the signing cert at build time; a `.msixbundle`'s own identity carries no architecture, so
  separate single-arch bundles collide; a re-signed bundle needs its cert trusted, not just
  present, for signature verification to read `Valid`; Partner Center's package-identity
  uniqueness check is scoped to the developer account and survives submission deletion). First
  real Partner Center upload of the resulting package succeeded 2026-08-01. See
  `docs/TASKS.md`'s T-F129 entry for the full trail.

---

## v1.4.4 — 2026-08-01

Security-hardening and CI/tooling release — no user-facing feature changes.

- **T-F135** — SonarCloud static analysis wired into CI (`build.yml`'s `test` job), with code
  coverage fed into the quality gate. Real analysis confirmed against the live SonarCloud
  dashboard, not just a green Actions run.
- **T-F136** — All 269 findings from SonarCloud's first scan individually triaged: real bugs
  fixed, false positives suppressed with a documented reason, genuine refactor-scale debt tracked
  as new follow-up tasks rather than silently dropped.
- **T-F137** — Local static-analysis tooling (SonarLint-equivalent analyzer config) added so the
  same rule set SonarCloud enforces in CI is visible during local development too.
- **T-F138** — Fixed the real defect behind SonarCloud's `S3869` findings: several
  `SafeHandle.DangerousGetHandle()` calls in the AppContainer sandbox's P/Invoke layer
  (`QuarantineAcl`, `SecurityCapabilitiesAttributeList`, `SandboxedProcessLauncher`) are now
  `SafeHandle`-typed parameters instead of raw handle extraction, closing a handle-recycling race
  window during sandboxed `tar.exe` launches. Confirmed via SonarCloud: `bugs: 0`,
  `reliability_rating: A`.
- Dependency hygiene: `CommunityToolkit.Mvvm` version synced between `Archiver.App` and
  `Archiver.App.Core`; `System.Net.Http`/`System.Text.RegularExpressions` pinned to patched
  versions in test projects.
- CI fixes: explicit workflow-level permissions on `build.yml`, cross-platform NuGet restore
  enabled, `global.json` SDK version corrected for `setup-dotnet`, Dependabot reverted to
  security-only updates.

---

## v1.4.3 — 2026-07-26

Point release adding a version flag to the CLI.

- **T-F134** — `pakko -v`/`--version` prints the CLI's own version and exits 0. A released
  `pakko.exe` now always reports the exact release tag it shipped under.

---

## v1.4.2 — 2026-07-25

Point release widening ZIP-format recognition to two more container-file families, plus
Microsoft Store submission preparation.

- **T-F131** — Explorer/file-association recognition of `.jar`/`.war`/`.ear` (Java) and `.apk`
  (Android) as ZIP-format archives, so the full Pakko context menu (Extract/Test/etc.) works on
  them directly.
- **T-F133** — Same recognition extended to `.asice`/`.asics` (ASiC-E/ASiC-S signed containers)
  and `.bdoc` (Estonia's ASiC-E profile), at a user's request.
- **T-F129** — Microsoft Store submission preparation: WACK (Windows App Certification Kit)
  fixes, vector-accurate brand assets regenerated from the canonical SVG source, and an
  `Package.appxmanifest` revision-segment fix required for submission. The Store listing itself
  is not live yet.
- **T-F130** — Fixed intermittent CI test failures in `Archiver.Core.IntegrationTests` caused by
  sandboxed tests racing each other under parallel execution.

---

## v1.4.1 — 2026-07-20

Point release covering Explorer hash commands and a `pakko.exe` CLI addition.

- **T-F128** — Explorer context-menu hash commands: CRC-32/SHA-256 submenu for files, folder
  DataSum/NamesSum, a progress-bar fix, a Size line, full 37-locale localization, and CRC-32
  performance work (intra-file parallel hashing).
- **T-F128/T-F09 follow-up** — `pakko.exe` gained an `h` (hash) command and `-si` zero-copy
  streaming support.

---
