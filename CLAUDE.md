# CLAUDE.md — Claude Code Session Context

This file is automatically read by Claude Code at session start.

---

## Project

**Pakko** — WinUI 3 desktop ZIP archiver for Windows with a completed shell extension (IExplorerCommand) and in-progress tar.exe integration for RAR/7z/tar extraction.
Minimal GUI over `System.IO.Compression`. No 7-Zip. No WinRAR. No third-party compression code.
Target audience: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Current State

**v1.1 complete** — tagged `v1.1.0`. GitHub-only release for early testers.
**v1.2 (shell extension) complete** — Archiver.Shell, protocol activation, file association,
and MOTW are complete; `IExplorerCommand` COM DLL (T-F61) is complete. Progress UI is shown via
the Windows Shell's built-in `IProgressDialog` (see `Archiver.Shell/NativeProgressDialog.cs`) —
the earlier `Archiver.ProgressWindow` satellite WinUI 3 app was removed (T-F65; see
`DECISIONS.md`). Both T-F61 and T-F65 manually smoke-tested end-to-end and confirmed 2026-07-05.
T-F62 (Test archive) is complete — manually smoke-tested in Explorer and confirmed 2026-07-06.
T-F68 (shell extract silently ignoring `SkippedFiles`) and T-F63 (Extract…/Compress… dialogs) are
both complete — manually smoke-tested and confirmed 2026-07-06. All planned v1.2 shell-extension
work is now done. T-F63's manual test surfaced T-F83 — a cold-start protocol/file activation bug
in `Archiver.App` (pre-dating T-F63, in already-shipped T-F44/T-F56 code) — fixed the same day;
see `DECISIONS.md`. T-F83's last criterion (reverifying T-F44's file-activation cold-start claim
on a machine where Pakko owns the `.zip` association) was completed 2026-07-06 — Pakko was set as
the default `.zip` handler and a cold-start double-click (simulated via `Start-Process`) correctly
populated the file list. T-F83 is now fully complete.
**v1.3 (tar.exe integration) complete; v1.4 complete except Group Policy/ADMX support (T-F51,
still open/future — see `SPEC.md`'s roadmap table)** — T-F47 (`ITarService`/`TarCapabilities`
scaffolding)
and T-F48 (capability detection) are complete. T-F49 (`TarProcessService.ExtractAsync()`
pipeline) is `[x]` complete — implementation, tests, and a `Deploy.ps1`-driven on-device
`.tar.gz`/`.7z` extraction through the installed app were all confirmed 2026-07-07 (graduated by
the agent at the user's explicit request that round, not a personal user click-through — flagged
in `TASKS.md`; real `.rar` stayed unverified then, since no RAR-capable encoder existed on this
machine — since fixed, see `TASKS_DONE.md`'s T-F85 entry). While designing
T-F49, empirically confirmed a real sandbox-escape exploit against a naive tar.exe
quarantine-then-validate model (a symlink entry writes outside the quarantine directory before
any validation code runs) — see `DECISIONS.md`'s T-F49 entry; `ExtractAsync` instead pre-scans
and rejects the whole archive before extraction ever runs. The ADS/reserved-name/reparse-point/
MOTW checks `ZipArchiveService` already had were moved into a new shared
`ArchiveEntrySecurity` class so both extractors stay in sync.
T-F95 (root "Pakko" context-menu icon missing in Explorer) is complete — `Archiver.App.csproj` had
no `<ApplicationIcon>`, so the built exe had zero icon resources; fixed by pointing it at the
existing `Assets\Square44x44Logo.ico`, confirmed on-device by the user 2026-07-07. Found along the
way: `Deploy.ps1`/`dotnet publish` currently fails with `MSB3231` cleaning up its own
`AppPackages`/`obj` output *after* a valid `.msix` is already written — worked around this once via
a direct `Add-AppxPackage`, root cause still open, tracked as T-F96 (recurred 4 consecutive times
during the 2026-07-13 T-F99/T-F100 session — see `DECISIONS.md`, `[~]` on hold, not being actively
chased right now).
**T-F05 (Archive Browser) is `[~]` partial** — versioned into v1.4, all implementation done
(Core `ListEntriesAsync`/`IArchiveListingRouter`, `ExtractOptions.SelectedEntryPaths`, new
`Archiver.App.Core` project, full `MainWindow.xaml`/`MainViewModel` wiring for the breadcrumb +
per-folder browser view and Extract Selected/All/Info commands), `dotnet test` green, and a full
`Deploy.ps1` build+sign+install completed 2026-07-13. AI-driven on-device verification passed
2026-07-13 (browsed real ZIP/7z/rar/tar.gz fixtures, extracted a selection and all, viewed Info) —
stays partial until the user's own on-device click-through is confirmed. See `DECISIONS.md`'s
T-F05 entry for the tar.exe selective-extraction spike findings and the subset-compression-bomb-
check tradeoff made while implementing it.
**Same-day UI design-review pass on T-F05** (user-driven, comparing a real on-device screenshot
against NanaZip's own archive-viewing UI): added `DIAGRAMS.md`'s new diagram 6 (no prior category
covered `MainWindow`'s row-visibility state machine — the gap itself was real), which formalized a
genuine bug — Row 0 (Add Files/Add Folder/Hash) never hid during browse mode. Fixed by splitting
Row 0 into mode-gated variants; `Info`/`Close` moved into the new browse-mode Row 0 as a
text-labeled top command bar (advisor: `frontend-design` skill), while `Extract Selected`/
`Extract All` deliberately stayed anchored next to the destination/options below rather than also
moving up, to avoid a "configure below, commit above" flow. Window's initial size grew
`800x700` → `1100x650` (file listings want width, not a near-square shape). See `DECISIONS.md`'s
T-F05 "UI Design-Review Pass" entry.
**Three same-day follow-ups on T-F05, all user-driven** (see `DECISIONS.md`'s three follow-up
entries for full detail): (1) the Info dialog was deleted entirely and its fields (Size, Packed)
folded into the browse-mode table as columns; (2) the standalone Close button was also removed,
replaced by a single up-arrow (in front of the breadcrumb, and separately next to the Destination
Path row) that steps up a folder level or exits the browser at the archive root — plus a CRC-32
column (`uint?`, ZIP-only) and a full localization pass (`en-US`/`uk-UA`) converting every
remaining hardcoded string in `MainWindow.xaml` to `x:Uid`; this round's first real on-device
launch hard-crashed (`0xc000027b` in `Microsoft.UI.Xaml.dll`) from two invented, unverified `x:Uid`
patterns (a `Uid` shared between a `Button`'s `.Content` and a `TextBlock`'s `.Text`, and a bracket-
syntax `ToolTipService.ToolTip` key) — root-caused and fixed the same round; (3) CRC-32 was
extended to the pending (archive-creation) list too (`FileItem`, async + throttled via a shared
`SemaphoreSlim(4)`, reusing `Archiver.Core.IO.Crc32` made `public`), and a genuine blank-row
regression was found and fixed — an unneeded explicit `VirtualizingStackPanel` added to the
pending-list `ListView` this session (it already virtualizes by default) raced with a large file's
async CRC completion, leaving a second item's row visually/UIA-blank until a forced re-layout; data
was never lost (count/archiving always read the real collection). Reverting that `ItemsPanel`
fixed it, confirmed via direct on-device relaunch. `ArchiveTreeIndex`/browse-mode rendering were
separately reviewed for the large-entry-count question this same round and found already sound
(O(n) build, per-folder-scoped sort, pre-existing virtualization, no per-item async work) — no fix
needed there.
**T-F99 (drive-root context menu) and T-F100 (file-activation routing) are both `[x]` done** —
implemented and AI-driven on-device verified 2026-07-13, then re-confirmed 2026-07-14 via a
user-directed Windows MCP automation pass (see `DECISIONS.md`'s T-F99/T-F100 entries). T-F99's
on-device verification surfaced three more real bugs beyond the manifest fix — a
command-line-corrupting `QuotePath` trailing-backslash bug, and two independent archive-auto-naming
code paths both producing a bare `.zip` for a drive-root source — all fixed and tested; see
`DECISIONS.md`. **T-F101** (Pakko missing from the classic "Show more options" menu) was diagnosed
2026-07-13 (repro confirmed, stale-build and crash-during-enumeration theories both ruled out, no
fix made) but stopped reproducing on its own by 2026-07-14 — root cause still unconfirmed, leading
guess is a side effect of T-F100's manifest change invalidating an Explorer verb/icon cache; see
`DECISIONS.md`. **T-F103** (extraction destination folder misnamed for compound extensions,
`browse_test.tar.gz` → `browse_test.tar` instead of `browse_test`) is now `[x]` fixed — new shared
`Archiver.Core.Services.ArchiveNaming` helper strips tar.exe's five compound extensions as a unit,
wired into all five buggy call sites across `ZipArchiveService.cs`/`TarProcessService.cs`/
`Archiver.Shell/Program.cs`, plus the native `ShellExtUtils.cpp` title-display equivalent;
on-device verified 2026-07-14. See `DECISIONS.md`'s T-F103 entry.
**T-F06** (Ask on Conflict Dialog) is now `[x]` done — designed via Plan Mode, `ConflictBehavior`
gained a 4th value `Ask` resolved per-conflict through a new Core→UI callback mirroring T-F94's
existing `ConfirmCompressionBombExtraction` pattern, wired into both Archive-creation modes and
both Zip/Tar extraction engines via a shared `ConflictResolver` helper; on-device verified
2026-07-14 for both Archive and Extract directions, all three resolutions plus "apply to all". See
`DECISIONS.md`'s T-F06 entry.
**T-F52 (AppContainer Sandbox for tar.exe) is `[x]` complete — Phase 1 implementation (11 of 13
planned steps) complete 2026-07-14.** `TarProcessService.cs` is deleted outright (fail-closed, no
unsandboxed fallback) and replaced by `TarSandboxedService`, which routes every tar.exe launch
through a new `Archiver.Core/Services/Sandbox/` subsystem (`AppContainerProfile`, `QuarantineAcl`,
`QuarantineStaging`, `SandboxJobObject`, `SandboxedProcessLauncher`,
`SecurityCapabilitiesAttributeList`, `TarSignatureVerifier`, `TarSandboxScope`). Confirmed on
real hardware, not just `dotnet test`: an on-device probe proved AppContainer +
`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` works from the actual packaged (MSIX
`FullTrustApplication`) process identity, not only from an unpackaged test host. DI wired at all 3
touch points (`Archiver.App`, `Archiver.Shell`, `SkipIfFormatUnsupportedAttribute`); the 3 existing
integration test files plus 2 unit test files were renamed to match, and a new
`TarSandboxedServiceSandboxBehaviorTests.cs` proves the three properties that matter (writes
outside quarantine denied, a spawned child process never completes under the Job Object, a
socket-connect fails inside the AppContainer while succeeding unsandboxed against the same
listener). Several real bugs found and fixed along the way (wrong `CERT_FIND_SUBJECT_CERT`
constant, hardlinked staged files not inheriting the quarantine folder's ACL, libarchive's
implicit parent-directory creation failing under AppContainer, a quarantine-location design
correction from "same disk as destination" to a fixed `%TEMP%`-rooted path) — see `DECISIONS.md`'s
several T-F52 entries for the full empirical trail. All 13 planned steps are now done, including a
full `Deploy.ps1` build+sign+install and AI-driven on-device verification (`.tar.gz` with nested
subdirectories, `.7z`, and `.rar` all extracted correctly through the installed, packaged
`Archiver.Shell.exe`). **Graduated to `[x]` 2026-07-15, user-directed**, after a second on-device
pass through the actual packaged GUI app itself (not just `Archiver.Shell.exe`): the `windows`
MCP UI-automation server drove a real `pakko://extract` launch with three fresh archives
(a `.tar.gz` built with a nested subdirectory, plus the committed `valid.7z`/`valid.rar`
fixtures) and a real click on "Розпакувати" — all resulting files confirmed present with correct
content on disk afterward. The user explicitly directed this MCP-driven pass as an accepted
substitute for their own personal click-through (see the Workflow Tips section below). A fourth
real bug was found via advisor review right after
Step 13 (no existing test exercised it): `ExtractAsync`/`ListEntriesAsync` didn't catch
`InvalidOperationException`, the type every sandbox-setup call (`AppContainerProfile`,
`QuarantineAcl`, `SecurityCapabilitiesAttributeList`, `SandboxJobObject`) throws on a Win32
failure — so a blocked/misconfigured sandbox would have crashed instead of yielding an
`ArchiveError`. Fixed with a new `SandboxSetupException` caught at the same boundary as
`TarSignatureVerificationException`; see `DECISIONS.md`'s T-F52 entry.
- T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39/T-F44/T-F45 complete
- **T-F105 (TAR archive creation) — `[x]` complete 2026-07-16, all four phases done.** Pulled
  forward from v1.5 to v1.4 at user's explicit request, overriding T-F36's 2026-07-07 deferral
  (re-scoped exactly as that deferral recommended: a `CompressAsync` method on the existing
  `ITarService`, not a new `IArchiveEngine`). **Phase A (Core):** `ArchiveContainerFormat` enum,
  `ArchiveOptions.Format`, `ITarService.CompressAsync`/`TarSandboxedService` (deliberately
  **unsandboxed** — creation reads trusted local files, not an untrusted archive; see
  `SECURITY.md`), `IArchiveCreationRouter`, and `ArchiveNaming.GetExtension()`. **Phase B (App
  layer):** a "Формат" `ComboBox` in `MainWindow.xaml` (ZIP default + 6 tar variants) localized
  across all 37 locales (format names stay untranslated Latin script everywhere, matching Windows
  Explorer's own convention — only the label word is translated); the Compression combobox now
  greys out only when plain `Tar` is selected (`IsCompressionLevelEnabled`); `MainViewModel` calls
  `IArchiveCreationRouter` instead of `IArchiveService` directly. **Phase C (Shell layer):** a new
  one-click "Add to X.tar" `IExplorerCommand` (`TarArchiveCommand`, plain tar only — the full
  6-variant selector stays dialog-only) registered right after "Add to X.zip"; needs no
  `Package.appxmanifest` entry (only the root command's CLSID is ever registered there); new
  `--format zip|tar` CLI switch through `ShellArgumentParser`/`Archiver.Shell/Program.cs`. Caught
  and fixed a real mojibake bug mid-session (a literal ellipsis typed into a new C++ string
  literal — the same bug class that's shipped 3 times before) via re-reading the file, not at
  deploy time. **Phase D (on-device, user-directed via the `windows` MCP server):** full
  `Deploy.ps1` build+sign+install, then three real checks against the installed packaged app —
  the one-click path via direct `Archiver.Shell.exe --archive --format tar` invocation (correct
  `.tar` produced), the Compress dialog's format selector via real `pakko://archive` GUI
  activation (all 7 formats present, created a real gzip-compressed `.tar.gz`, confirmed via
  `tar -tzvf`), and the Compression combobox visibly greying out for plain TAR only (screenshot-
  confirmed) — all three passed with no fixes needed. 316/316 .NET tests pass, 68/68 C++ tests
  pass (was 309/59 before Phase C). See `TASKS_DONE.md`'s T-F105 entry and `DECISIONS.md`'s T-F105
  entry (Phase 0 empirical findings on tar.exe's real compression-level mechanism, plus the Phase
  B/C addendum).
- **T-F106 (`[x]` resolved 2026-07-16)** — pending-list `ListView` rows rendered blank
  (`ui_find` showed correct bound data but `(0,0)` position) when files were added at
  window-activation time. **Root cause: never a WinUI rendering bug** — `RootGrid`'s file-table
  row had no `MinHeight` on its own `RowDefinition`, only on the `ListView` child (which doesn't
  force the row to grow); at a fixed 650px window height, the pending-list mode's other rows
  (Archive Options — 4 rows, taller since T-F105's new Format row — plus Shared Options/action
  buttons/status bar) collectively demanded more height than existed, clamping the table's Star
  row to 0. Five unrelated fix hypotheses (population-timing gates, collection-mutation shape,
  immutable-record rewrites, `ListView` structural copies, a full data-pipeline duplication) were
  tried and disproven before this was found. Fixed by raising the default window size to
  1100×900, giving the table row an explicit `MinHeight="200"`, and setting
  `PreferredMinimumWidth="900"`/`PreferredMinimumHeight` (**850** — corrected same day from an
  initial 700, which left the table visible but clipped the Shared Options checkboxes and status
  bar below it off the bottom of the window) via `OverlappedPresenter` — confirmed on-device at
  both default size and the enforced minimum (Windows clamps a smaller resize request to ~900×850,
  everything — table, all options, both checkboxes, status bar — stays fully visible). This also
  resolved the responsive/minimum-window-size sub-scope that had been widened into this same
  ticket — see `DECISIONS.md`'s final T-F106 entry for the full account, including why Archive
  Browser mode never showed the bug and why it first appeared during T-F105.
  **Also added, same session:** the app's title bar now shows `Pakko — build <timestamp>`, read
  from the running assembly's own file `LastWriteTime` — see this file's "Build Commands" section
  for why (never trust build logs alone to prove an on-device check ran against fresh code).
- **T-F107 (`[x]` done 2026-07-16)** — the Archive Browser's "Up" button used to exit the browser
  entirely once it reached an archive's own root; now it keeps climbing past the archive root into
  the archive's real containing folder, up through real parent folders, up to a drive root, and up
  to a synthetic "This PC" node listing all drives (greying out only there), patterned after
  NanaZip's classic FileManager. New `ArchiveBrowseScope` (`Archive`/`RealFileSystem`/`ThisPc`) on
  `MainViewModel` plus a new `FileSystemBrowser` static helper in `Archiver.App.Core`
  (`ListFolder`/`ListDrives`, reusing `ArchiveEntryViewModel` unchanged) drive it;
  `NavigateUpOrExitBrowser` was renamed to `NavigateUp`/`CanNavigateUp()` and `ExitBrowseMode()`
  deleted outright (no callers left). AI-driven on-device verification (2026-07-16) confirmed
  climbing from inside a real archive up through Desktop/Users/`C:\` to "Цей комп'ютер" (up button
  UIA-confirmed disabled there), descending back into a drive via double-click, opening a different
  real archive fresh via double-click while browsing real folders, and Extract Selected/All staying
  disabled throughout real-filesystem browsing and re-enabling immediately back inside an archive.
  See `DECISIONS.md`'s T-F107 entry.
- **T-F97 (`[x]` done 2026-07-16)** — double-clicking an image/text file inside the Archive
  Browser now silently extracts just that entry to a shared `%TEMP%\PakkoPreview\` cache and opens
  it with the OS default handler (only a quiet "Opening..."/"Відкриття..." status-line change, no
  progress bar/summary dialog), instead of always running a full Extract. New
  `Archiver.Core.Services.PreviewPolicy.IsPreviewable` allowlist (images + plain text only — see
  `SECURITY.md`); new `Archiver.App.Core.PreviewCache` (one shared root, fresh `Guid` subfolder per
  preview, deleted on window close). Deliberately reuses the real `IExtractionRouter` pipeline via
  `ExtractOptions.SelectedEntryPaths` rather than a bespoke extraction path, so T-F49's whole-
  archive pre-scan and MOTW propagation both apply with zero new code. Two real bugs found via
  on-device testing, neither caught by `dotnet test`: `Launcher.LaunchFileAsync`/`StorageFile`
  silently fails for an arbitrary `%TEMP%` path even from this app's full-trust packaged identity
  (fixed with `Process.Start(UseShellExecute=true)`, same mechanism already used elsewhere for
  "open destination folder"); `ArchiveResult.CreatedFiles` lists per-archive destination
  *folders*, not individual file paths (the previewed file's path has to be computed directly).
  AI-driven on-device verification confirmed a ZIP's `.txt`/`.jpg` and a `.tar.gz`'s `.txt` all
  preview correctly with a real propagated MOTW tag, a non-allowlisted `.docx` still runs the full
  Extract flow, and the cache is gone after closing the window. See `DECISIONS.md`'s T-F97 entry.
- **T-F93 (`[x]` done 2026-07-16)** — Ko-fi donate link (`https://ko-fi.com/pakko_app`) added to
  both `README.md` (small link under the title) and the About dialog (a third `HyperlinkButton`,
  "Ko-fi", in the existing GitHub/Privacy Policy row — same style/weight, no icon, no redesign).
  User explicitly asked whether the dialog needed a redesign to fit the link well; the design
  answer was no — see `DECISIONS.md`'s T-F93 entry for why more visual weight on a donate link
  would work against Pakko's minimal/trust-focused positioning for its government/defense
  audience. New `AboutKofiUrl` resw key (en-US only, non-translatable, matching
  `AboutGitHubUrl`/`AboutPrivacyUrl`'s existing convention). On-device verified.
- **T-F108/T-F98/T-F109/T-F110 (all `[x]` done 2026-07-17)** — same session, Archive Browser
  work. **T-F108:** fixed the extraction destination defaulting to Desktop instead of the
  archive's own folder when browsing without any pending files queued
  (`MainViewModel.EnterBrowseModeAsync`). **T-F98:** double-clicking an archive found *inside*
  the currently browsed archive drills straight into it, up to 4 nesting levels
  (`NestedArchivePolicy.MaxDepth`), reusing T-F49/T-F90/T-F94's per-extraction security
  machinery unmodified at every level (`Archiver.App.Core.NestedArchiveCache`,
  `MainViewModel`'s browse-stack model). **T-F109:** widened the Archive Browser's safe-preview
  allowlist to include video/audio (`PreviewPolicy`, PDF deliberately excluded); anything still
  outside it now shows a confirm dialog and extracts to a subfolder next to the archive on disk
  instead of the old silent full-extract-to-Destination-field behavior. **T-F110:** the entry
  table's icon column distinguishes preview-vs-extract-only per row (Segoe MDL2 `View`/`Hide`
  glyphs); a nested-archive row shows `View` (it drills in transparently) unless drilling in
  would exceed T-F98's depth limit, in which case it shows `Hide`
  (`ArchiveEntryViewModel.NestedDepthLimitReached`). All four verified on-device, both via a real
  automated Windows MCP pass and the user's own personal click-through.
- **T-F114 (`[x]` done 2026-07-17)** — ZIP-only compression/extraction performance-regression
  tests comparing Pakko's own `ZipArchiveService` against a vendored, pinned, hash-verified
  `7za.exe` reference (`tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`, LGPL-attributed,
  test-only — never shipped in the MSIX). Designed via a design-advisor session plus real
  engineering-practice research (BenchmarkDotNet/criterion.rs/benchstat all converge on
  same-machine, same-invocation ratio comparison as the only pattern that generalizes across
  arbitrary machines — no cross-machine cached-baseline mechanism exists in any of them, so that
  approach — floated during scoping — was explicitly dropped). Each of the 6 scenarios (archive +
  extract × one-large-file ~300 MB / 5,000-small-files / hybrid) runs one discarded warmup pass
  then one timed pass per engine and asserts the elapsed-time ratio against a per-scenario
  calibrated constant (observed 2026-07-17: 1.06–6.02 depending on scenario — the many-small-files
  case is highest since 7za's absolute time there is dominated by near-instant process-spawn
  overhead, not real compression work) with a 3x tolerance multiplier for cross-machine headroom.
  tar-family formats are explicitly out of scope — `TarSandboxedService`'s AppContainer/Job-Object
  overhead would make a shared tolerance band meaningless. The many-small-files/hybrid scenarios
  (4 tests) are tagged `[Trait("Category","Slow")]`, run via `dotnet test --filter "Category=Slow"`
  alongside T-F20's Zip64 tests; the one-large-file scenarios (2 tests, ~300 MB) are tagged
  `[Trait("Category","VeryLarge")]` instead — on demand only via `Category=VeryLarge`, per user
  request, alongside Zip64's own >4 GiB test (also moved to `VeryLarge`, see below). Every
  `7za.exe` launch runs under a basic sandbox reused from tar.exe's own subsystem (`SandboxJobObject`
  via `SandboxedProcessLauncher` — Job Object only, no AppContainer/quarantine, so raw I/O
  performance is unaffected) — mitigates the vendored-binary-compromised risk without biasing the
  timing being measured; see `SECURITY.md`. A perf-test failure should be rerun once before being
  treated as a real regression (unlike Zip64's tests, it carries a nonzero chance of being machine
  noise). See `TESTING.md`'s new section and `DECISIONS.md`'s T-F114 entry for the full
  research/rationale.
- 414/414 .NET tests pass (`dotnet test --filter "Category!=Slow"`: 269 Archiver.Core.Tests +
  43 Archiver.Shell.Tests + 47 Archiver.Core.IntegrationTests + 55 Archiver.App.Core.Tests — the
  jump from 284 to 309 reflects T-F105 Phase A's new `TarSandboxedServiceCompressTests` (real
  tar.exe round-trips for all 6 creation formats), `ArchiveNamingTests.GetExtension`, and
  `ArchiveCreationRouterTests`; 309 to 316 reflects Phase C's new `ShellArgumentParser`
  `--format` switch tests; 316 to 326 reflects T-F107's new `FileSystemBrowserTests`; 326 to 353
  reflects T-F97's new `PreviewPolicyTests`/`PreviewCacheTests`; 353 to 387 reflects T-F98's new
  `ArchiveFormatDetectorTests`/`NestedArchivePolicyTests`/`NestedArchiveCacheTests`/
  `NestedArchiveDrillDownSecurityTests`; 387 to 402 reflects T-F109's widened
  `PreviewPolicyTests`; 402 to 414 reflects T-F110's `ArchiveEntryViewModelTests.Icon*` cases).
  4 Zip64 tests (T-F20) are split across two tiers — 3 are tagged
  `[Trait("Category", "Slow")]` (excluded from this default run, real wall-clock cost from
  >65535-file archiving/extraction/listing; run via `dotnet test --filter "Category=Slow"` before
  a release or when touching Zip64-adjacent code), and 1 (the >4 GiB round trip) is tagged
  `[Trait("Category", "VeryLarge")]` instead — on demand only, via
  `dotnet test --filter "Category=VeryLarge"` (2026-07-17: gated separately from `Slow` per user
  request, so a routine pre-release `Slow` run never pays its cost unless deliberately asked to).
  **Current true total (2026-07-18, after T-F35): 462 tests** via
  `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` — 305 Archiver.Core.Tests (+19 from
  T-F35: `DosDateTimeTests`, `ParallelSingleArchiveWriterTests`,
  `ZipArchiveServiceParallelPipelineTests`) + 4 Archiver.Core.PerformanceTests
  (`ZipEntryWriterCompatibilityTests`, new) + 43 Archiver.Shell.Tests + 55 Archiver.App.Core.Tests
  + 55 Archiver.Core.IntegrationTests. Don't trust the 414/316/269 figures in the paragraph below
  as current — this bullet is the freshest count; run `dotnet test` for ground truth either way.
  **T-F114 (2026-07-17)** added a second project, `Archiver.Core.PerformanceTests` (6 tests:
  archive+extract × one-large-file/many-small-files/hybrid, each comparing Pakko's ZIP path
  against a sandboxed, vendored `7za.exe` reference on a same-run ratio basis — see
  `TESTING.md`/`DECISIONS.md`/`SECURITY.md`) — the many-small-files/hybrid tests (4) are tagged
  `Slow`, the one-large-file tests (2) are tagged `VeryLarge`, same on-demand-only split as Zip64's.
  C++ `Archiver.ShellExtension.Tests` (Google Test,
  68/68 — was 59, +9 from T-F105 Phase C's `BuildArchiveArgs`/`BuildAddToArchiveTitle` `.tar`
  cases) run separately, not covered by `dotnet test`
- **T-F35 (`[~]` implementation complete, on-device verification pending, 2026-07-18)** —
  `ZipArchiveService.ArchiveAsync`'s `SingleArchive` mode gates on file count
  (`ParallelPipelineFileCountThreshold = 64`): below it, archiving is completely unchanged
  (original sequential `ZipArchive`-based code); above it, a new `Archiver.Core/Services/Zip/`
  subsystem (`WorkItemEnumerator`, `ParallelSingleArchiveWriter`, `ZipEntryWriter`,
  `ZipEntryCompressor`, `DosDateTime`) compresses small files (≤4 MiB) in parallel and streams
  large files sequentially, writing the final ZIP through a hand-rolled container-format writer
  since `System.IO.Compression.ZipArchive` gives no API to compress independently of the live
  archive and splice the result in later. Built to fix the ~6x `SingleArchive`-vs-7z gap T-F114
  measured for many-small-files archiving. Two real bugs were caught by the test suite before
  shipping: a bounded-channel-alone-doesn't-bound-concurrency concurrency bug (whitebox test) and
  a Zip64 local-header field-offset swap that corrupted output silently as far as .NET's own
  reader was concerned but was rejected outright by an independent `7za.exe` integrity check — see
  `DECISIONS.md`'s T-F35 entry for the full design/bug trail. T-F114's perf ratios were
  re-measured after this change: `ArchiveAsync_ManySmallFiles` 6.02 → 2.39, `ArchiveAsync_Hybrid`
  3.47 → 3.03, `ArchiveAsync_OneLargeFile` 1.22 → 1.23 (unaffected, as designed — a single large
  file never crosses the gate). Stays `[~]` until a manual on-device verification (archive 100+
  real small files via the installed Pakko GUI/context menu, confirm the result opens cleanly in
  Explorer/7-Zip/WinRAR) — not graduated on `dotnet test` alone, per this project's workflow rule.
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
| `CLI.md` | **Canonical owner of Archiver.CLI's (T-F09) command/switch specification** — 7z→Pakko command table, switch fidelity, three-way unknown-input rule | Implementing or extending T-F09 | The planned CLI command/switch surface changes |
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
- **UI-thread marshaling for Core→App callbacks:** any delegate `Archiver.Core` invokes that ends
  up showing WinUI (e.g. `ExtractOptions.ConfirmCompressionBombExtraction` → `ContentDialog`) must
  marshal onto `Window.DispatcherQueue` inside the App-layer implementation —
  `ZipArchiveService`/`TarProcessService` run their extraction bodies off the UI thread, and
  `ContentDialog.ShowAsync()` requires the calling thread to own the DispatcherQueue. Found via
  design review before shipping (T-F94) — would have crashed on first real use otherwise.
- **Solution platforms:** x64 and ARM64 only — never add `Any CPU` or `x86` configuration entries
  to the `.sln` file. When adding a new project, mirror the `Debug|x64` / `Release|x64` entries
  from `Archiver.Shell` exactly (two lines per config, right-hand side maps to project's `Any CPU`).
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
- If a change modifies a public interface, model, or contract in `Archiver.Core`, check whether
  tests in other projects (`Archiver.Shell.Tests`, future `Archiver.CLI.Tests`) need to be updated
  or extended. Internal implementation changes (private methods, buffers, sorting) require only
  `Archiver.Core.Tests` coverage.
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
  `gh` CLI is not installed in this environment, and GitHub's code search requires sign-in even
  for public repos. Instead: `curl -s "https://api.github.com/repos/<owner>/<repo>/git/trees/main?recursive=1"`
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
│   └── Archiver.ShellExtension/    ← C++ COM DLL, IExplorerCommand (T-F61), x64+ARM64
├── tests/
│   ├── Archiver.Core.Tests/        ← xunit (see "Current State" for current count)
│   ├── Archiver.App.Core.Tests/    ← xunit, ArchiveTreeIndex coverage (T-F05)
│   ├── Archiver.Core.IntegrationTests/ ← xunit, real tar.exe via [Integration]/TarBuilder
│   ├── Archiver.Core.PerformanceTests/ ← xunit, T-F114: ZIP perf vs. vendored 7za.exe reference,
│   │                                     [Trait("Category","Slow")], see TESTING.md
│   ├── Archiver.Shell.Tests/       ← xunit (see "Current State" for current count)
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
> **Deploy shortcuts:**
> Release build in VS triggers `Deploy.ps1 -DeployOnly` automatically (post-build event).
> For manual deploy from terminal: `.\scripts\Deploy.ps1` (full build + sign + install)
> or `.\scripts\Deploy.ps1 -DeployOnly` (install only, no build).
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
> valid `.msix` already written) instead of aborting a good build — see T-F96 in `TASKS.md`.

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
  (a Plan that merely *proposes* editing one of these two is not itself "explicitly asked" —
  get separate explicit confirmation before touching either, even after plan approval)
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
- **Observed test flakiness (2026-07-07):** `Extract_ValidUnicodeFilenames_Succeeds` and
  `ExtractAsync_ZipWithMotw_PropagatesZoneIdentifierToExtractedFiles` each failed once in a
  run, then passed immediately on rerun in isolation — looks like parallel-execution timing
  noise, not a real regression. If a test fails once, rerun before treating it as caused by
  your change.

---

## Windows Packaging Best Practices

Root-cause detail for the first six points below lives in `DECISIONS.md` ("MSIX Satellite EXE
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
  `SPEC.md`; it was already `1.2.0.x` throughout all of v1.3's development, so don't read the
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
- **`TASKS.md`'s task-graduation edits** (moving completed entries to `TASKS_DONE.md`) tend to
  land in large diff hunks that intermingle several unrelated tasks — `git add -p` can't
  cleanly split one task's doc update out of such a hunk. When committing narrowly, stage
  specific files/whole hunks deliberately, or commit the doc consolidation separately.
- **Debugging via Pakko's log file:** when running as an installed MSIX, the log is NOT at the
  plain `%LOCALAPPDATA%\Pakko\logs` `LogService.cs` constructs — MSIX virtualizes
  `LocalApplicationData` per-package. Find it at
  `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Pakko\logs\pakko.log`
  (get `<PackageFamilyName>` via `Get-AppxPackage *Pakko*`).
- **Editing unicode-heavy docs (`DIAGRAMS.md` mermaid blocks, `DECISIONS.md`) with the Edit
  tool:** a multi-line `old_string` spanning several em-dash (—)/arrow (→) characters can
  silently fail to match even though `Read` shows it verbatim. Split into smaller edits
  (isolate one such character per edit) to work around it.
- **`DIAGRAMS.md` mermaid blocks are never auto-validated — nothing in this repo's workflow
  renders them.** After editing, run each block through `npx @mermaid-js/mermaid-cli` (`mmdc -i
  diagram.mmd -o diagram.svg`) before considering the edit done. A bare `;` or an unescaped
  `"quoted phrase"` inside unquoted label/message/transition text breaks the parser in
  sequence/state/flowchart diagrams alike — use `—` instead of `;`, and quote the whole label if
  it needs literal parentheses or quotes.
