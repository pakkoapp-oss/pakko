# TASKS.md — Active and Future Tasks

> Completed tasks (T-01 through T-35, T-11) are archived in [`TASKS_DONE.md`](TASKS_DONE.md).
> **v1.0 is complete.** All items below are post-v1.0 future work.

---

## ⚠ Agent Rules — Read Before Every Task

These rules apply to ALL tasks. Violating them = task is NOT complete.

**Completion rules:**
- NEVER mark `[x]` unless every single acceptance criterion is checked `[x]`
- `[~]` means partially complete — UI done but logic missing, or logic done but untested
- A task with ANY `[ ]` criterion must stay `[ ]` or `[~]` — never `[x]`

**Testing rules:**
- Test-run commands and when to run the Slow filter are `CLAUDE.md`'s Hard Constraints — the
  canonical copy; don't restate them here.
- If tests fail → fix before marking anything complete
- Every new behavior in `ZipArchiveService` needs at least one test

**UI vs Logic rules:**
- UI-only implementation = `[~]` not `[x]`
- If a task touches both XAML and a service, BOTH must be done before `[x]`
- Options passed from ViewModel to service must actually be READ and ACTED ON in the service

**Scope rules — which options apply to which action:**
- Archive-only options (Name, Mode, Compression, DeleteSourceFiles) → `ArchiveOptions` only
- Extract-only options (DeleteArchiveAfterExtraction) → `ExtractOptions` only
- Shared options (Destination, OnConflict, OpenDestinationFolder) → both

---

## Current State — v1.1 Complete

- All T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39 complete and committed
- 95/95 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` via `Deploy.ps1` (signed with dev cert)
- Satellite EXEs (Archiver.Shell.exe, Archiver.ProgressWindow.exe) included via Content Include in Archiver.App.csproj
- Git tag: `v1.1.0` — GitHub-only release for early testers
- **Store release planned for v1.3** (when shell extension + MOTW + tar.exe complete)

---

## Future Tasks

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** SUPERSEDED by T-F53–T-F57 — kept for historical reference
- **Depends on:** T-F09 (CLI Core)

**What:** Right-click context menu in Windows Explorer for archiving and extracting without opening the main UI window.

**User experience:**

Right-click on any files/folders (non-ZIP or mixed):
```
Pakko ►
  ├── Add to "first_item.zip"    ← immediate, no window, single archive
  ├── Add to separate ZIPs       ← immediate, no window, one ZIP per item
  └── Archive with Pakko...      ← opens main window with items pre-loaded
```

Right-click on one or more ZIP files:
```
Pakko ►
  ├── Extract here               ← immediate, no window, extract next to archive
  ├── Extract here (new folder)  ← immediate, subfolder per archive
  └── Extract with Pakko...      ← opens main window with archives pre-loaded
```

Right-click on mixed selection (ZIP + non-ZIP):
```
Pakko ►
  ├── Add to "first_item.zip"
  ├── Extract ZIPs here
  └── Open with Pakko...
```

**Technical approach — two components:**

**1. `Archiver.Shell` project** (new, `src/Archiver.Shell/`)
Lightweight console exe invoked by the context menu with arguments:
```
Archiver.Shell.exe --archive --dest same "file1" "file2" "file3"
Archiver.Shell.exe --archive --separate --dest same "file1" "file2"
Archiver.Shell.exe --extract --dest same "archive1.zip" "archive2.zip"
Archiver.Shell.exe --open-ui --archive "file1" "file2"
```
Uses `Archiver.Core` directly — no WinUI dependency. Runs silently (`<OutputType>WinExe</OutputType>`, no console window).

**2. Shell extension registration**
Windows 11 (build 22621+): sparse package manifest — no COM DLL needed.
Windows 10 fallback: classic COM `IContextMenu` shell extension DLL.

Declared in `Package.appxmanifest` for MSIX distribution.

**Silent operation — no window flicker:**
- `Archiver.Shell.exe` runs with `CreateNoWindow = true`
- Progress shown via Windows Toast notification on completion:
  ```
  Pakko
  Archived 3 files → backup.zip
  ```
- Errors shown via Toast, not dialog

**Acceptance criteria (when implemented):**
- [ ] `Archiver.Shell` project added to solution, references `Archiver.Core`
- [ ] `--archive` flag: archives all passed paths into single ZIP next to first item
- [ ] `--archive --separate` flag: one ZIP per item
- [ ] `--extract` flag: extracts all passed ZIPs next to each archive (T-14 smart folder logic)
- [ ] `--open-ui` flag: launches `Archiver.App` with items pre-loaded
- [ ] No console window shown during silent operations
- [ ] Toast notification on completion — success and error
- [ ] Context menu appears for ZIP files with Extract options
- [ ] Context menu appears for non-ZIP files/folders with Archive options
- [ ] Multi-selection works — all selected items passed in single invocation
- [ ] Works on Windows 10 1809+ and Windows 11
- [ ] Registered via MSIX manifest — no manual registry editing
- [ ] Uninstall removes all context menu entries cleanly
- [ ] `dotnet test` passes — basic invocation tests for Archiver.Shell

---

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

Separate window for archive configuration instead of inline controls.

---

### T-F03 — Dedicated Extract Window
- [ ] **Status:** future

Separate window for extract configuration.

---

### T-F04 — TAR/GZip/BZip2/XZ Support via Windows tar.exe
- [ ] **Status:** future

Uses Windows built-in `tar.exe` (available since Windows 10 1803, based on libarchive).
No third-party binaries — `tar.exe` is part of the OS.
Invoke via `System.Diagnostics.Process`.

---

### T-F05 — Archive Browser (Navigate, Select, Extract Selected/All)
- [~] **Status:** partial — versioned into v1.4 (`SPEC.md`) 2026-07-13; Core listing API,
      `Archiver.App.Core`, and the full WinUI wiring (mode-swap, breadcrumb, browser `ListView`,
      Extract Selected/All/Info commands) are all implemented and `dotnet test` is green
      (208/208, `Category!=Slow`; Zip64 `Category=Slow` also green). Stays partial until the
      user's manual on-device verification (last acceptance criterion) is confirmed — see
      `DECISIONS.md` for the tar.exe selective-extraction spike and subset-bomb-check tradeoff
      made along the way.
      **UI design-review pass (2026-07-13, same day, after T-F99/T-F100):** user-driven visual
      audit against NanaZip's real archive-browsing view (see `DIAGRAMS.md`'s new diagram 6, added
      specifically because no diagram category previously covered this window's row-visibility
      state) surfaced and fixed three things — Row 0 (Add Files/Add Folder/Hash) never hid during
      browse mode (a real bug, not by design — `MainViewModel.cs`'s own comment only lists
      destination/conflict/checkboxes as intentionally shared); `Info`/`Close` moved from the
      bottom action row into a new browse-mode Row 0 top command bar (text-labeled, matching
      modern Explorer's command-bar-above-the-list convention) while `Extract Selected`/
      `Extract All` deliberately stayed anchored next to the destination/options they consume
      (advisor: `frontend-design` skill flagged that moving *those* to the top too would create a
      "configure below, commit above" backwards flow, since Pakko's inline-always-visible options
      aren't a self-contained dialog the way WinRAR/7-Zip/NanaZip's own top-toolbar buttons are);
      and the window's initial size grew from `800x700` to `1100x650` (`MainWindow.xaml.cs`) since
      a file/archive listing is tabular and wants width, not a near-square shape. AI-driven
      on-device verification passed (Info/Close both confirmed working from the new position,
      Row 0 confirmed toggling correctly in both modes). See `DECISIONS.md`'s T-F05 UI-review entry.
      **Follow-up same day — Info button removed, Size/Packed columns added:** user feedback that
      the Info+Close pair still read as a confusing combination. Resolution: deleted the Info
      dialog entirely (`IDialogService.ShowEntryInfoAsync`, `DialogService.ShowEntryInfoAsync`,
      `MainViewModel.ShowSelectedEntryInfoCommand`, the `EntryInfoButton` XAML button/string) and
      added the two fields it showed that weren't already table columns — `Size` and `Packed`
      (compressed size) — directly to the browse-mode entry table (`MainWindow.xaml` Row 1), fixing
      a pre-existing header/row column-alignment mismatch in the same `Grid`s while touching them.
      Row 0 (browse) is now just `Close` + `About`. Note: `Packed` reads blank for every tar-routed
      format (RAR/7z/tar.*) since `TarProcessService` never populates a per-entry `CompressedSize`
      (the underlying compression stream is whole-archive, not per-entry) — real only for ZIP; see
      `DECISIONS.md`'s follow-up entry. `dotnet test` green (217/217 — added
      `ArchiveEntryViewModelTests` for `CompressedSizeDisplay`'s folder/zero/positive cases).
      **Second follow-up same day — Close removed, CRC-32 column, destination up-button,
      localization pass:** four more user-reported items, batched into one round (see
      `DECISIONS.md`'s second follow-up entry for full reasoning on each):
      (1) the standalone Close button was removed too; replaced by a single up-arrow in front of
      the breadcrumb (`MainViewModel.NavigateUpOrExitBrowserCommand`) that steps up one archive
      folder level, or exits the browser when already at the archive's own root — Row 0 (browse)
      now holds only `About`;
      (2) a `CRC-32` column was added (`ArchiveEntryInfo.Crc32`/`ArchiveEntryViewModel.CrcDisplay`,
      both `uint?` — nullable, not a `<= 0` sentinel, since `0` is a legitimate CRC unlike a size),
      populated for ZIP only (`TarProcessService` has no per-entry CRC concept for tar-routed
      formats);
      (3) a separate up-arrow was added next to the Destination Path row
      (`MainViewModel.NavigateDestinationUpCommand`), disabled at a real filesystem drive root via
      `Path.GetDirectoryName(...) is null` — functionally unrelated to (1) despite the matching
      icon;
      (4) a full localization audit converted every remaining hardcoded English string in
      `MainWindow.xaml` (tray menu, Hash/About buttons, both tables' column headers, the pending
      list's Remove context-menu item, Mode/compression/conflict radio+combo items, the archive-
      name placeholder, Cancel) to `x:Uid` with `en-US`+`uk-UA` `Resources.resw` entries (other 22
      locales still fall back to `en-US` per T-F91's existing design), plus added the two
      `uk-UA` translations (`ExtractSelectedButton`/`ExtractAllButton`) that were missing since
      T-F05 originally shipped them `en-US`-only. `dotnet test` green (221/221 — added `CrcDisplay`
      folder/null/zero/positive cases).
      **Crash found and fixed on first real on-device launch of this round's build:** a hard
      native crash (`0xc000027b` in `Microsoft.UI.Xaml.dll`, at `MainWindow.InitializeComponent()`)
      caused by two invented `x:Uid`/`Resources.resw` patterns — a shared `Uid` applying a
      mismatched `.Content`/`.Text` pair to elements that don't have both properties, and an
      unverified `Uid.[ToolTipService.ToolTip]` bracket-key syntax for the up-arrows' tooltips.
      `dotnet build`/`dotnet test` and even a `dotnet build`-triggered "installed successfully"
      MSIX never actually launches the app, so none of this was caught until a direct
      `Start-Process` launch — see `DECISIONS.md`'s "Correction" entry for the full root-cause
      writeup and fix. Fixed by giving every header its own separate, single-property `x:Uid`
      (pending-list `Button` headers vs. distinct `Browse*ColumnHeader` `TextBlock` keys) and
      dropping the tooltip `x:Uid`s in favor of a hardcoded `"Up"` string. Redeployed (1.2.0.21),
      relaunched directly, confirmed no crash, and all four items above verified on-device via
      screenshots: Row 0 (browse) shows only "Про програму", the up-arrow correctly exits the
      browser at the archive root back to the pending list, the CRC-32 column shows a real hex
      value for the ZIP fixture, the Destination Path row has its own working up-arrow, and the
      whole window now displays in Ukrainian (headers, Mode/compression/conflict options,
      buttons) matching the system locale.
      **Third follow-up same day - pending-list CRC-32, a real blank-row regression found and
      fixed, large-entry-count review:** user asked for CRC-32 in the archive-creation (pending)
      list too, that hashing stay async/non-blocking, and whether Pakko has display problems for
      folders with very many entries. `FileItem` gained async, throttled (`SemaphoreSlim(4)`)
      CRC-32 computation reusing `Archiver.Core.IO.Crc32` (made `public`, not reimplemented).
      Reviewed `ArchiveTreeIndex`/`CurrentFolderEntries` for the large-entry-count question first -
      already O(n)/per-folder-scoped/virtualized with no per-item async work, so no fix was needed
      there. But testing item 1 surfaced a real regression matching item 3's concern directly: a
      large file added together with a small file in the same batch left the small file's row
      blank (UI and accessibility tree both) until a forced re-layout - underlying data was never
      lost (count/archiving both read the collection, not rendered text), root-caused to an
      unnecessary explicit `VirtualizingStackPanel` added to the pending-list `ListView` this
      session (the control already virtualizes by default); reverted, confirmed fixed on-device.
      See `DECISIONS.md`'s third follow-up entry for full detail. `dotnet test` green (221/221, no
      test changes - XAML-only revert). Deployed and verified as 1.2.0.23.
- **Depends on:** none

**What:** let the user browse an archive's internal folder structure — without extracting
everything first — and run basic commands from that view: navigate in/out of folders, select
one or more entries, Extract selected, Extract all, and view an entry's Info/properties. Opens
from the existing pending-selection list (double-click an archive) by swapping the main window's
content area into a browser view — not a new window, not `NavigationView` (see Design below for
why). Explicitly **not** an archive manager: no in-place edit, no Add/Copy/Move/Delete-within-
archive, no Benchmark — this stays "minimal GUI over `System.IO.Compression`", not a 7-Zip/NanaZip
clone.

**Research done before scoping (per `CLAUDE.md`'s pre-implementation-research norm, extended here
to a UI feature since a real reference existed):** fetched NanaZip's actual shipped source
(`NanaZip.Modern/`, via the GitHub trees API) to check what a modern Windows archiver's browsing
UI looks like. **Negative result, stated plainly so it isn't re-attempted:** NanaZip's "modern"
WinUI layer (`MainWindowToolBarPage.xaml`, `AddressBar.*`) is only *chrome* — toolbar, breadcrumb,
status bar — wrapping the legacy vendored Win32 7-Zip `FileManager` C++ control
(`NanaZip.Core/SevenZip/CPP/7zip/UI/FileManager/`) for the actual file list. That control cannot be
reused here (`CLAUDE.md`'s "no 7-Zip, no third-party compression code" hard constraint) — the file
list itself must be designed natively. What *is* reusable: the **command vocabulary** (Extract,
Info) and the **breadcrumb/address-bar navigation shape** — NanaZip's toolbar also has Add, Test,
Copy, Move, Delete-in-archive, Benchmark, all of which are archive-*editing*/manager features
deliberately **out of scope** here (see What, above) since they clash with Pakko's positioning and
aren't expressible without add/delete-in-place support Pakko doesn't have.

**Design (advisor + `frontend-design` skill consulted, 2026-07-12; user confirmed the navigation-
surface choice below):**
- **Inline mode-swap in the existing main window**, not a separate window or a `NavigationView`
  page. A separate window breaks Pakko's established one-shot/single-focus model (see T-F88's
  multi-instance decision — same "one task, one window" reasoning applies within a window, not
  just across windows). `NavigationView` is an architectural commitment (multi-top-level-section
  shell) too heavy for one feature in a "minimal GUI" app. Double-clicking an archive in the
  existing pending-selection `ListView` swaps the window's content area (`Visibility` toggle on
  two `Grid` sections, not a new `Frame`/page) into the browser view; clicking the breadcrumb's
  root segment (or a "Back" affordance) swaps back. Matches File Explorer's own drill-in behavior
  (double-clicking a folder doesn't open a new window).
- **Breadcrumb + flat per-level `ListView`, not a `TreeView`.** WinUI `TreeView` virtualizes
  poorly; this app has real Zip64 archives with 65,000+ entries (T-F20's Slow tests exercise this
  scale). A flat per-folder list reuses the existing 4-column `ListView` pattern
  (Name/Type/Size/Modified) already in `MainWindow.xaml`, just with checkboxes
  (`SelectionMode="Multiple"`) and a folder/file-type icon per row, plus
  `VirtualizingStackPanel.VirtualizationMode="Recycling"` set explicitly for that scale.
- Use the real `Microsoft.UI.Xaml.Controls.BreadcrumbBar` (Windows App SDK 1.4+; Pakko is on
  1.8.260209005, so it's available and not yet used anywhere in this codebase) instead of a
  hand-rolled breadcrumb from `ItemsRepeater`/chevrons.
- New `ArchiveEntryViewModel` (name, size, modified, isFolder, icon glyph) — **separate from** the
  existing `FileItem` model, which represents top-level pending-selection paths queued for an
  Archive/Extract operation, not entries inside an archive. Don't overload one model with two
  meanings.
- **Navigation/selection behavior:** double-click a folder row descends (breadcrumb appends a
  segment, list refreshes to that folder's direct children only — no recursive flattening);
  double-click a file row extracts just that file; clicking a breadcrumb segment jumps directly to
  that level. Selection is **per-current-folder only and clears on every navigation** — matches
  File Explorer's own behavior; deliberately not attempting cross-folder "select things in
  multiple folders, then extract together" (the kind of scope creep flagged during design). Extract
  all ignores selection state entirely and reuses the existing whole-archive extract pipeline.
- **tar-family listing is async, non-blocking.** ZIP central-directory listing is fast in-memory
  (`ZipFile.OpenRead`), but tar-family archives need an external `tar -tvf` process per listing —
  show the existing indeterminate "Finalizing..."-style loading state (T-F58's pattern), never a
  blocking modal, while that listing runs.

**Hard design constraint — partial (selected-entries) extraction through `TarProcessService` must
NOT weaken T-F49's security model.** T-F49 deliberately pre-scans and rejects the *whole archive*
before any extraction runs (a symlink entry can escape the quarantine directory before per-entry
validation code ever executes — see `DECISIONS.md`'s T-F49 entry). "Extract selected" for a tar-
family archive must still run that same whole-archive pre-scan first (reject the entire archive as
a unit if anything is unsafe), and only then extract the subset of safe, selected members — it must
never become a per-entry-only validation shortcut. Call this out explicitly in the implementation
plan; don't let it get "optimized" into a hole later.

**Core-layer boundary:** the listing method returns a flat `IReadOnlyList<ArchiveEntryInfo>`
(path, size, compressedSize, modified, isDirectory) from `Archiver.Core` — the App layer builds the
folder hierarchy from `/`-split paths in a view-model helper, not Core. `Archiver.Core` has zero
WinUI references (hard constraint); a tree-shaped model belongs in the App layer only. No existing
listing API exists today — confirmed by reading `ZipArchiveService.cs`/`TarProcessService.cs`:
`ZipFile.OpenRead` and `tar -tvf` are both used today, but only as `private`/internal helpers
inside `TestAsync`/extraction, not exposed as a reusable structured listing method; both need a new
public method rather than reusing what's there unchanged.

**Explicitly out of scope (confirmed during design):** anything that mutates the archive (Add,
Copy, Move, Delete-in-archive), Benchmark, cross-folder multi-select, in-app content preview of a
file's own contents (opening/viewing a text file or image from inside the archive — a separate,
bigger feature if ever wanted).

**Acceptance criteria:**
- [x] Version slot decided with the user and added to `SPEC.md`'s roadmap table — v1.4
- [x] `Archiver.Core`: new listing method(s) on `IArchiveService`/`ITarService` (`ListEntriesAsync`,
      routed by new `IArchiveListingRouter`/`ArchiveListingRouter`) returning
      `IReadOnlyList<ArchiveEntryInfo>` — flat, not hierarchical
- [x] tar-family listing still runs T-F49's whole-archive pre-scan before any partial extraction;
      no per-entry-only validation path introduced (`ExtractOptions.SelectedEntryPaths` +
      `TarProcessService.ExpandSelection` built on the existing scan's name list)
- [x] App layer: new `ArchiveEntryViewModel` + folder-hierarchy-building helper (`ArchiveTreeIndex`)
      from flat entries (kept separate from `FileItem`) — lives in new `Archiver.App.Core` project
- [x] `MainWindow.xaml`: inline mode-swap (not a new window, not `NavigationView`) between the
      existing pending-selection view and the new browser view, triggered by double-clicking an
      archive in the pending-selection list
- [x] Real `BreadcrumbBar` control (not hand-rolled) + per-folder `ListView` with
      `SelectionMode="Multiple"` and explicit `VirtualizationMode="Recycling"`
- [x] Extract selected / Extract all / Info commands wired, reusing existing extraction pipeline
      (`IExtractionRouter` via a shared `RunExtractAsync`) — no new extraction logic duplicated
- [x] Selection clears on navigation; double-click file = extract that file; double-click folder =
      descend; breadcrumb segment click = jump to that level
- [x] New tests: `Archiver.Core.Tests`/`Archiver.Core.IntegrationTests` for the listing method(s)
      (ZIP + tar-family, including a large-entry-count case exercising the flat-not-hierarchical
      contract), new `Archiver.App.Core.Tests` project for the flat-to-tree helper
- [x] `dotnet test --filter "Category!=Slow"` passes (208/208); Zip64 Slow-tagged coverage extended
      with a `ListEntriesAsync` 65,600-entry test, confirmed green under `Category=Slow`
- [x] Manual on-device verification: browse a real multi-folder ZIP and a real multi-folder
      tar.gz/7z/rar, extract a selection, extract all, view Info — confirmed by the user personally
      per this project's UI-verification workflow tip. **Full `Deploy.ps1` build+sign+install
      completed 2026-07-13** (Pakko v1.2.0.11 on-device). **Confirmed 2026-07-14, user-directed via
      Windows MCP automation:** browsed `browse_test.zip`/`.7z`/`.rar`/`.tar.gz` (via "Open with →
      Pakko"), descended into a subfolder, ran Extract Selected on one file (correct content, only
      that file written) and Extract All on all four formats (correct structure, correct
      rename-on-conflict behavior for a repeat name). The Info dialog itself no longer exists (see
      the "Info button removed" follow-up above) so that half of this criterion is void by design,
      not skipped. Graduated to `[x]`.

---

### T-F05 (original, pre-2026-07-12 scope, superseded by the expanded entry above — kept per the
"never silently deprecate" rule)

Click ZIP in list → read-only tree view of contents via `ZipFile.OpenRead`. No extraction.

---

### T-F07 — Optional 7-Zip Extraction Support
- [ ] **Status:** CANCELLED — replaced by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` (Microsoft-signed) supports 7z extraction on Windows 11 23H2+ without requiring a third-party binary.

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** CANCELLED — covered by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` supports RAR extraction on Windows 11 23H2+, eliminating the need for `unrar.exe`.

---

### T-F09 — CLI Core (Archiver.CLI, 7z-Familiar Syntax)
- [~] **Status:** implementation complete 2026-07-18, on-device verification pending. Scope
      pivoted 2026-07-12 from the original GNU-style `--src/--dest` sketch (kept below the divider,
      per the "never silently deprecate" rule) to a `7z`-*familiar* command syntax, per user
      request. Advisor-reviewed before writing this. `CLI.md`'s `a`/`l`/`-t{type}` rows were found
      stale relative to T-F105/T-F05 (both shipped after `CLI.md` was last edited) and corrected
      before implementation started — see `DECISIONS.md`'s T-F09 "Implementation" entry.
- **Depends on:** none

**Full command/switch specification lives in [`CLI.md`](CLI.md)** — the goal statement,
architecture rationale, the 11-command 7z→Pakko support table, the switch-fidelity table, and the
three-way unknown-input rule are all there now (moved 2026-07-13 to stop duplicating the same
tables in both files; `CLI.md` is the canonical owner per `CLAUDE.md`'s Documentation Map).

**Acceptance criteria:**
- [x] New `src/Archiver.CLI/` project, references `Archiver.Core` directly (in-process, no
      subprocess indirection) — mirrors `Archiver.Shell`'s constructor pattern (no DI container —
      confirmed during implementation this is Shell's actual pattern, not "DI" in the
      `ServiceCollection` sense `Archiver.App` uses)
- [x] Supported commands implemented: `x` (extract), `t` (test, ZIP only — tar reports "not
      supported" per the three-way rule), `i` (info/capabilities), `a` (archive — ZIP **and** all
      6 tar-family creation formats, per the `CLI.md` correction above)
- [x] `l` (list) implemented, consuming `IArchiveListingRouter` (T-F05)
- [x] Three-way unknown-command/switch handling implemented and tested (unparseable vs.
      deliberately-unsupported vs. unsupported-switch-on-a-supported-command)
- [x] Per-switch fidelity table above reflected in actual behavior — no switch silently accepted
      and ignored; unsupported switches hit the three-way rule, not silent no-ops
- [x] `-mx` bucketing onto `CompressionLevel` documented (in `--help` output and in
      `ARCHITECTURE.md`), not left as an undocumented approximation
- [x] Argument parsing extracted into its own testable class (`CliArgumentParser`, mirroring
      `Archiver.Shell`'s existing `ShellArgumentParser`/`ShellArgumentParserTests` split — parsing
      logic never inline in `Main`), unit-tested in-process, no process spawned — covers the
      three-way unknown-command/switch handling and every supported command's argument shape
      (`tests/Archiver.CLI.Tests/CliArgumentParserTests.cs`, 46 tests)
- [x] **Real subprocess invocation tests against real archive fixtures** —
      `tests/Archiver.CLI.Tests/Subprocess/CliSubprocessTests.cs`, a genuinely new test layer for
      this repo (plain `System.Diagnostics.Process`, not Core's internal
      `SandboxedProcessLauncher` — that machinery sandboxes untrusted external binaries, not a
      trusted first-party sibling exe). Reuses `Archiver.Core.IntegrationTests/Fixtures/valid.7z`/
      `valid.rar` via a `Link`-mapped `None` item; builds its own ZIP/`.tar.gz` fixtures inline
      rather than depending on that project further. Covers each command's happy path (`x`
      against ZIP/`.tar.gz`/`valid.7z`/`valid.rar`, `t` against ZIP and a tar-family skip, `i`,
      `a` creating both ZIP and `.tar.gz`, `l`) with real output verified on disk/in stdout, plus
      one real instance of each of the three unknown-input categories with real exit code and
      real stderr text asserted
- [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` passes with both new test layers
      included (94 tests in `Archiver.CLI.Tests`, part of a 567-test green run repo-wide)
- [ ] Manual on-device verification: real `pakko x archive.zip`, `pakko t archive.zip`,
      `pakko i`, `pakko a archive.zip file1 file2` against real archives, plus one of each
      three-way error case, confirmed by the user personally
- [x] `Archiver.CLI` published self-contained per architecture (`win-x64`, `win-arm64`) as a
      standalone downloadable artifact via `scripts/Publish-Cli.ps1`, with a `SHA256SUMS` file for
      verification — separate from the MSIX, confirmed to run standalone outside the repo/dev
      machine's SDK-adjacent state with no GUI/MSIX installed. See `CLI.md`'s "Distribution"
      section. (GitHub Release publication itself is a release-time action, not part of this
      implementation round.)
- [x] No bundled copy of `tar.exe` — `Archiver.CLI` calls the OS-provided
      `C:\Windows\System32\tar.exe` via the existing `TarSandboxedService`, same as every other
      frontend (decision + rationale in `DECISIONS.md`'s T-F09 "Distribution" entry)

---

### T-F116 — Archiver.CLI stdin/stdout streaming (`-si`/`-so`)
- [~] **Status:** implementation complete 2026-07-18, on-device verification pending. Scoped as a
      separate task, split out of T-F09 at the user's explicit request. Plan redone through
      `advisor` before implementation — see `DECISIONS.md`'s T-F116 entry for the empirical
      PowerShell/cmd binary-pipe findings that materially changed the test/doc plan. Same session,
      the built exe was renamed `Archiver.CLI.exe` → `pakko.exe` (`AssemblyName` only) after
      research into how ripgrep/fd/bat handle Windows distribution/`PATH` — see `DECISIONS.md`'s
      T-F116 follow-up entry.
- **Depends on:** T-F09 (CLI Core)

**Full specification lives in [`CLI.md`](CLI.md)'s "Stdin/stdout streaming" section** — switch
table rows, per-command applicability, and the empirically-verified shell-compatibility table
(native `|`/`>` byte-perfect on PowerShell 7+, silently corrupts binary data on Windows
PowerShell 5.1, `cmd /c "..."` is byte-perfect everywhere).

**Acceptance criteria:**
- [x] `-si` (read archive from stdin) implemented on `x`/`t`/`l`; rejected with a named reason on
      `a`/`i` and when combined with an explicit archive-path argument
- [x] `-so` (write output to stdout) implemented on `x` (only when extraction resolves to exactly
      one file — named error otherwise) and `a`; rejected with a named reason on `t`/`l`/`i` and
      when combined with `-o` on `x`
- [x] Zero `Archiver.Core` changes — implemented entirely via private `%TEMP%` staging inside
      `Archiver.CLI/CliStreamStaging.cs` (see `ARCHITECTURE.md`'s T-F116 entry for why true
      zero-copy streaming through Core was rejected)
- [x] Broken-downstream-pipe handling (e.g. `pakko a -so ... | head`) exits cleanly (2), no
      unhandled exception — unit-tested deterministically via an injectable destination `Stream`
      (`CliStreamStagingTests`), after a real-subprocess broken-pipe simulation proved racy/
      unreliable in practice (see `DECISIONS.md`)
- [x] `CliArgumentParserTests.cs` covers every `-si`/`-so` valid/rejected combination per command
- [x] `Subprocess/CliSubprocessTests.cs` covers: a full `a -so` → `x -si` byte round trip via real
      subprocess `RedirectStandardInput`/`RedirectStandardOutput`; `-so` on `x` against real
      `valid.7z`/`valid.rar` fixtures; `-so` on `x` against a multi-file archive (named-count
      error); `-si` on `a` (three-way-rule case); and a `cmd.exe /c "pakko ... | pakko ... > out"`
      subprocess test that launches `cmd.exe` itself, proving the documented shell recipe actually
      works, not just .NET's own `Process` plumbing
- [x] `CliHelpText.Text` and `CLI.md` document `-si`/`-so`, the buffered-not-zero-copy note, and
      the shell-compatibility table (`cmd /c "..."` recipe) — public-facing, not just an
      implementation note
- [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` passes repo-wide (594 tests,
      `Archiver.CLI.Tests` grew from 94 to 121)
- [ ] Manual on-device verification: real `pakko a -so ...` piped into `pakko x -si ...` via both
      a real PowerShell 7 session and (if available) real Windows PowerShell 5.1 using the
      documented `cmd /c "..."` recipe, confirmed byte-correct by the user personally

---

### T-F117 — Silent Success on an Unrecognized Single Archive Path (Extraction)
- [ ] **Status:** future — found 2026-07-18 while testing T-F116, not fixed there (a
      `Archiver.Core` behavior change, out of scope for a CLI-only task). See `DECISIONS.md`'s
      T-F116 entry for the full repro.
- **Depends on:** none

**What:** `ZipArchiveService.ExtractAsync`'s per-item gate (`if (!IsZipFile(archivePath))`) only
records a `SkippedFile` when `GetKnownArchiveReason` recognizes the bytes as a *different* known
archive format (GZip/BZip2/RAR/etc.). For bytes matching no known magic number at all — an empty
file, plain garbage, a truncated/corrupted download — `reason` is `null` and the loop just
`continue`s with **nothing recorded**: `ArchiveResult.Success` stays `true`, no error, no skipped
entry. Confirmed via both the CLI's `-si` path and a real on-disk garbage `.zip` fed to plain `x`
— identical silent exit 0. This contradicts this project's own "loud error always" rule
(`CLI.md`'s three-way-input philosophy, `CLAUDE.md`'s general stance) even though it predates T-F09
and isn't CLI-specific — it's just never been noticed before because the GUI always shows an
operation summary either way.

**Acceptance criteria:**
- [ ] `ZipArchiveService.ExtractAsync`/`TestAsync`'s per-item loop records a real `ArchiveError` (or
      at minimum a `SkippedFile` with a named reason) when a path is neither a valid ZIP nor
      matches any of `GetKnownArchiveReason`'s known-format signatures — decide which (error vs.
      skip) matches this project's existing severity conventions for "not a valid archive at all"
- [ ] `Archiver.Core.Tests` covers: empty file, plain garbage bytes, truncated real ZIP
- [ ] `Archiver.CLI.Tests`' `Extract_SiWithEmptyStdin_SilentlyNoOpsPerPreExistingCoreBehavior`/
      `Extract_SiWithGarbageBytes_SilentlyNoOpsPerPreExistingCoreBehavior` updated to assert the
      new (non-silent) behavior, renamed to match
- [ ] Checked whether `TarSandboxedService`'s extraction path has the same gap for a
      non-tar/non-recognized input, and fixed there too if so (for parity, not assumed)
- [ ] `Archiver.App`'s operation-summary dialog still reads correctly for this case (a real error
      now appears where none did before — confirm it doesn't regress a currently-passing manual
      test path)

---

### T-F118 — ZIP vs. Tar-Family Extraction Smart-Foldering Asymmetry
- [ ] **Status:** future — found 2026-07-18 while implementing T-F09, not fixed there (CLI is a
      thin frontend reusing Core's extraction engines unmodified). See `DECISIONS.md`'s T-F09
      "Implementation" entry for the full repro.
- **Depends on:** none

**What:** extracting a multi-root-item archive (two or more root-level files/folders with no single
common containing folder) behaves differently depending on format: `ZipArchiveService.ExtractAsync`
wraps the result in a `<destDir>/<archive-base-name>/` subfolder (T-14's "smart foldering",
confirmed to apply even under `ExtractMode.SingleFolder`), while `TarSandboxedService.ExtractAsync`
has no equivalent — files land directly in `destDir`, unwrapped. `Archiver.Shell`'s
`--extract-flat` (T-F115) inherits the ZIP behavior too despite its own doc comment claiming "no
wrapper folder ever created," which is only accurate for the common single-root case.

**Acceptance criteria:**
- [ ] Decide the intended behavior — match tar-family to ZIP's smart-foldering, match ZIP to
      tar-family's flat behavior, or keep the asymmetry but fix `Archiver.Shell`'s misleading doc
      comment — this is a product decision, not purely technical; needs a Plan Mode session before
      touching code
- [ ] Whichever direction is chosen, both `ZipArchiveServiceTests` and
      `TarSandboxedServiceExtractionTests` (or equivalent) cover the multi-root-item, no-common-
      folder case explicitly
- [ ] `Archiver.Shell/Program.cs`'s `--extract-flat` doc comment corrected to match actual verified
      behavior, not the aspirational "no wrapper folder ever" claim
- [ ] `Archiver.CLI.Tests`' existing test asserting the ZIP-wraps/tar-doesn't-wrap asymmetry
      updated to match whichever new unified behavior is chosen

---

### T-F119 — Archiver.CLI PATH Distribution (winget/scoop manifest)
- [ ] **Status:** future — flagged 2026-07-18 during T-F116's naming/distribution discussion. See
      `CLI.md`'s Distribution section and `DECISIONS.md`'s T-F116 follow-up entry.
- **Depends on:** T-F09 (CLI Core)

**What:** `pakko.exe` currently ships only as a manually-downloaded, manually-unzipped artifact —
confirmed (via research into ripgrep/fd/bat) that this matches the norm for zip-distributed CLI
tools, but it means there is no "install once, available everywhere" path today. A `winget`
manifest (Microsoft's own package manager, pre-installed on Windows 10 1709+/Windows 11) is the
natural next step — its `Portable` installer type registers the exe in a PATH-managed `Links`
folder without needing a custom installer or elevated MSI. `scoop` is a plausible second target
for the same reason (also PATH-shim-based, no admin rights needed) but lower priority.

**Acceptance criteria:**
- [ ] `winget` manifest (`pakkoapp.pakko` or similar id — check availability) targeting the
      `win-x64`/`win-arm64` zips already produced by `scripts/Publish-Cli.ps1`, using the
      `Portable` installer type
- [ ] Manifest validated via `winget validate` and a real local install
      (`winget install --manifest <path>`) confirming `pakko` resolves from a fresh terminal with
      no manual PATH edit
- [ ] Submission process to `microsoft/winget-pkgs` documented in `scripts/README.md` (this is a
      recurring release step, not a one-time action — new manifest version needed per release)
- [ ] `CLI.md`'s Distribution section updated once this ships, replacing the "not yet scheduled"
      note

---

### T-F120 — Publish Archiver.CLI to GitHub Releases
- [ ] **Status:** future — deliberately left out of T-F09's implementation scope ("a release-time
      action, not part of this implementation round"). `scripts/Publish-Cli.ps1` already produces
      everything needed (`pakko-win-x64.zip`, `pakko-win-arm64.zip`, `SHA256SUMS`) — this task is
      the actual publication step plus getting it into the repo's release workflow.
- **Depends on:** T-F09 (CLI Core)

**Acceptance criteria:**
- [ ] `.\scripts\Publish-Cli.ps1` output attached to a real GitHub Release (either a new release or
      an existing one, per user decision at the time)
- [ ] Release notes/`README.md` link to the CLI download alongside the existing MSIX download,
      matching the "GitHub-only release" pattern already used for v1.1–v1.4
- [ ] `SHA256SUMS` verification instructions visible on the release page or linked from it, not
      just buried in `CLI.md`
- [ ] Decide whether this becomes part of a scripted release checklist (so it isn't a manually
      remembered step every future release) or stays a one-off manual action — document whichever
      is chosen in `scripts/README.md`

---

### T-F09 (original, pre-2026-07-12 scope, superseded by the expanded entry above — kept per the
"never silently deprecate" rule)

Expose `Archiver.Core` as standalone CLI executable for scripting, using a GNU-style
`--long-flag` syntax instead of 7z-familiar single-letter commands:

```
archiver archive --src C:\files --dest C:\output --name backup
archiver extract --src C:\backup.zip --dest C:\output
```

---

### T-F10 — Code Signing
- [ ] **Status:** future. Scope explicitly includes `Archiver.CLI`'s `pakko.exe`/`pakko-win-*.zip`
      (T-F09/T-F116, added 2026-07-18) — not just the MSIX. `pakko.exe` is downloaded and run
      standalone, outside any package-manager trust chain, so it hits the exact SmartScreen/
      AppLocker friction described below on its own, independent of whether the MSIX is signed.
      Don't scope this task down to "just the MSIX" without an explicit decision to split it.

**Why critical for target audience:** government/defense environments often block unsigned executables via AppLocker/WDAC. Unsigned MSIX triggers SmartScreen.

**Two levels, two different signing mechanisms — don't conflate them:**
- MSIX package signature — required for sideload installs, covers `Archiver.App`'s package as a
  whole (and everything bundled inside it, including the satellite EXEs `Archiver.Shell.exe`/
  `Archiver.ShellExtension.dll`)
- Authenticode on standalone binaries — visible in file Properties → Digital Signatures; this is
  the mechanism that matters for `pakko.exe`, since it's downloaded and run **outside** the MSIX/
  package trust boundary entirely (T-F09/T-F116)

**Certificate options — researched 2026-07-18 (Microsoft Learn's "Code signing options for Windows
app developers," updated 2026-04-20 — verified current, not from memory) after the user asked
whether free Microsoft Store signing could also cover `pakko.exe`. It cannot: Store re-signing is
free but applies **only to an MSIX package actually submitted through the Store** — a standalone
loose `.exe` distributed via GitHub Releases (never submitted as a package) is never touched by
it. Submitting `pakko.exe` to the Store via the MSI/EXE-installer path (a separate path from MSIX)
doesn't help either — Microsoft explicitly does not re-sign Win32 MSI/EXE installer submissions;
you're required to already hold your own Authenticode cert before submitting.**

| Option | Cost | Availability | SmartScreen | Notes |
|--------|------|------|------|------|
| **Microsoft Store (MSIX)** | Free | Worldwide | No warnings | Already covers `Archiver.App`'s MSIX if/when submitted — doesn't touch `pakko.exe` |
| **SignPath Foundation** | **Free**, for qualifying open-source projects | No geographic restriction found | Reputation builds over time like a paid cert, but the cert itself is free | **Best fit for `pakko.exe`** — Pakko is already Apache 2.0 and public on GitHub, matching their eligibility criteria; provides real OV-level Authenticode signing through a managed CI pipeline |
| Azure Artifact Signing (formerly "Trusted Signing") | ~$9.99/mo | Individuals: **USA/Canada only**. Organizations: also EU/UK | Reputation builds over time | Blocks an individual developer submitting from Ukraine — would need to register as an org in an eligible region, or use a different option |
| OV certificate (DigiCert, Sectigo, etc.) | $150–300/yr | Worldwide | Reputation builds over time | Fallback if SignPath Foundation eligibility doesn't pan out in practice |
| EV certificate | $400+/yr | Worldwide | **No longer instant** — Microsoft removed EV's SmartScreen-bypass-on-first-download behavior in 2024; EV now builds reputation the same way OV does | Not worth the premium anymore, purely for SmartScreen purposes (older docs/advice claiming "immediate trust" are stale) |
| Self-signed | Free | — | Blocks install for public users; fine for enterprise-managed trust | For Ukrainian government deployment specifically: self-signed with the root cert distributed via Group Policy remains viable for *internal* rollout, independent of whatever's chosen for public GitHub distribution |

**Working plan — two phases, researched 2026-07-18 after the user asked whether one SignPath
certificate could cover both the MSIX and `pakko.exe`, and how that combines with an eventual
Store submission:**

**Phase 1 (now — MSIX and CLI both ship via direct GitHub download, no Store submission yet):**
SignPath Foundation explicitly supports EXE/DLL **and MSIX/AppX** ("deep signing") through the
same account/pipeline, per their own changelog — one application covers both `pakko.exe` and the
MSIX for as long as the MSIX keeps shipping outside the Store. **Real gotcha, confirmed via
research, not assumed:** an MSIX's `Package.appxmanifest`'s `<Identity Publisher="CN=...">` must
match the signing certificate's Subject exactly — SignPath's own error messages specifically flag
a mismatch here. Today that field holds the local self-signed dev cert's CN (`Setup-DevCert.ps1`);
switching to SignPath means updating `Identity Publisher` to SignPath's issued cert's Subject, and
re-pointing `Deploy.ps1`'s signing step from local `SignTool` + dev cert to SignPath's CI
integration (their PowerShell module, called from GitHub Actions or wherever the release build
runs) instead of a locally-installed cert.

**Phase 2 (later — if/when the MSIX is actually submitted to the Store, per the roadmap's "planned
once T-F51/GPO is done"):** confirmed via Microsoft's own "Publish your first Windows app" guide —
an MSIX submitted to the Store needs **no CA-trusted signature at all** to be accepted; Microsoft
re-signs it with its own certificate after certification, regardless of what it was built/signed
with beforehand (even the existing self-signed dev cert is fine for the submission itself). What
Store submission *does* require is a **separate, later** manifest change: Partner Center assigns
its own Publisher ID (a different GUID-format CN) that `Identity Publisher` must match *at
submission time* — unrelated to whatever cert SignPath issued in Phase 1. Once certification
passes, SignPath is no longer needed for the MSIX specifically (Store re-signs every future
release automatically) — but `pakko.exe` keeps needing its own binary signing indefinitely, since
a portable CLI exe isn't something that goes through Store certification the way an MSIX does.

**Acceptance criteria (when implemented):**
- [ ] SignPath Foundation eligibility confirmed by actually applying (not just reading their public
      criteria) — record the real outcome here before relying on it as the plan
- [ ] Phase 1: `Identity Publisher` in `Package.appxmanifest` updated to match the SignPath-issued
      certificate's Subject; `Deploy.ps1`'s signing step re-pointed from local `SignTool` + dev
      cert to SignPath's CI-integrated signing
- [ ] Phase 1: all `.exe`/`.dll` binaries signed via SignPath, including standalone `pakko.exe`
      (`Archiver.CLI`/T-F09) published via `scripts/Publish-Cli.ps1` — not just binaries inside
      the MSIX
- [ ] Phase 1: MSIX signed via SignPath — installs without SmartScreen warning for direct/GitHub
      downloads (current distribution model)
- [ ] Timestamp applied to every signature
- [ ] Signing wired into the actual release process, not a manual one-off step, for both
      `scripts/Publish-Cli.ps1` (CLI) and `Deploy.ps1` (MSIX)
- [ ] Certificate/signing credentials not in the repository
- [ ] `Get-AuthenticodeSignature` returns `Valid` on all binaries, including `pakko.exe`
- [ ] Phase 2 (deferred, tracked here for when it becomes relevant): when the MSIX is actually
      submitted to the Store, `Identity Publisher` updated again to the Partner-Center-assigned
      Publisher ID before that specific submission, and SignPath signing dropped for the MSIX
      going forward (kept for `pakko.exe` regardless)

---

### T-F13 — Process Sandbox Isolation for External Binaries
- [ ] **Status:** SUPERSEDED by T-F52 — reassessed 2026-07-14. Written when the project still
      planned to bundle optional third-party binaries (`7z.exe`/`unrar.exe`, T-F07/T-F08); both
      of those tasks were cancelled 2026-07-12 when the project pivoted entirely to Windows'
      built-in `tar.exe` (T-F47–T-F49), so this task's `Depends on` target no longer exists and
      its threat model ("binary passes SHA-256 but is compromised") doesn't fit a Microsoft-
      signed OS component nobody downloads or hash-verifies. T-F52 (AppContainer Sandbox for
      tar.exe — retitled 2026-07-14 when the mechanism moved from a Low-IL token to an
      AppContainer, see `DECISIONS.md`) is this task's tar.exe-specific descendant, already
      planned for v1.4 per `SPEC.md`. Layers 1/3/6 below (restricted token, filesystem restriction
      via IL labeling, staging validation) are superseded outright by T-F52's flow (filesystem
      restriction now via AppContainer SID ACLs, not IL labeling). Layers 2 and 4/5 (Job Object
      resource limits; network isolation) are real additional hardening not covered by T-F52 as
      originally scoped — folded into T-F52's acceptance criteria below rather than implemented as
      a second, separate sandboxing task; network isolation is now AppContainer-native (empty
      capability list), not a WFP firewall rule — Layer 5's firewall-rule approach is dropped, not
      carried forward. Kept per the "never silently deprecate" rule instead of deleted.
- **Depends on:** T-F07 or T-F08 (both cancelled — see Status)

**Threat model:** binary passes SHA-256 but has undiscovered vulnerability, or is compromised between verification and execution, or attempts network exfiltration or filesystem traversal.

**Layer 1 — Restricted token:**
- Create process with restricted token: no debug privileges, no driver privileges
- Drops all unnecessary privilege groups before `Process.Start`

**Layer 2 — Windows Job Object (P/Invoke):**
- `ActiveProcessLimit = 1` — cannot spawn child processes
- RAM limit 512 MB — prevent resource exhaustion
- CPU time limit — maximum runtime enforced
- UI restrictions — no clipboard, no desktop manipulation

**Layer 3 — Filesystem restriction:**
- Filesystem access limited to two directories: sandbox/input (read-only) and sandbox/output (write-only)
- All other filesystem paths denied via DACL or AppContainer policy

**Layer 4 — Network isolation:**
- Network access completely disabled for worker process
- No outbound or inbound connections permitted

**Layer 5 — WFP firewall rule:**
Added at optional component install time (requires elevation once):
```powershell
New-NetFirewallRule -DisplayName "Pakko — block 7z.exe outbound" `
    -Direction Outbound -Program "$env:LOCALAPPDATA\Pakko\tools\7z.exe" -Action Block
```
Rule removed on uninstall.

**Layer 6 — Staging directory validation:**
- Files extracted to staging directory first
- Staging output validated (path traversal check, no reparse points) before move to final destination
- TOCTOU mitigation: resolve real paths immediately before file creation
- Staging directory cleaned up on both success and failure

**Acceptance criteria (when implemented):**
- [ ] External binary process assigned to Job Object before execution
- [ ] Worker process runs with restricted token (no debug, no driver privileges)
- [ ] `ActiveProcessLimit = 1`
- [ ] RAM limit enforced (512 MB)
- [ ] CPU time limit enforced — maximum runtime applied
- [ ] UI restrictions applied
- [ ] Filesystem access limited to sandbox/input and sandbox/output only
- [ ] Network access completely disabled for worker process
- [ ] Firewall rule added at install, removed at uninstall
- [ ] Files extracted to staging directory first, validated, then moved to final destination
- [ ] TOCTOU mitigation: real paths resolved immediately before file creation
- [ ] Staging directory cleaned up on success and failure
- [ ] Job Object handle closed after process exits — no leak
- [ ] `dotnet test` passes
- [ ] Verified: spawning child process from sandboxed binary fails

---

### T-F15 — Microsoft Store Publication
- [ ] **Status:** future

**What:** Publish Pakko to Microsoft Store via Partner Center. Store handles MSIX signing, hosting, distribution, and automatic updates.

**Cost:** $0 for individual developers (as of September 2025).

**Prerequisites before submission:**
- Proper app icon in all required sizes
- About dialog with version and links (T-F14) ✓ done
- Store listing assets: screenshots, description, privacy policy URL

**Required icon sizes for Store:**
| File | Size |
|------|------|
| `StoreLogo.png` | 50×50 |
| `Square44x44Logo.png` | 44×44 |
| `Square150x150Logo.png` | 150×150 |
| `Wide310x150Logo.png` | 310×150 |
| `Square71x71Logo.png` | 71×71 |
| `Square310x310Logo.png` | 310×310 |

**Submission process:**
1. Register at storedeveloper.microsoft.com (individual, free, ID verification)
2. Create app reservation — reserve "Pakko" name
3. Build MSIX bundle (x64, optionally + arm64 per T-F11)
4. Upload to Partner Center
5. Fill Store listing: description, screenshots, category (Utilities), privacy policy
6. Submit for certification (1–3 business days)
7. Store signs the package — no separate code signing certificate needed

**Privacy policy note:**
Store requires a privacy policy URL even for apps that collect no data.
Acceptable: simple GitHub Pages page stating "Pakko collects no data."

**Automatic updates:**
Once published, Store delivers updates automatically when new version is submitted.
Version bump: increment `Package.appxmanifest` `Version` attribute before each submission.

**Acceptance criteria (when implemented):**
- [ ] Partner Center account registered (individual, free)
- [ ] App name "Pakko" reserved in Store
- [ ] All required icon sizes present in `Assets/`
- [ ] Privacy policy page published (GitHub Pages or similar)
- [ ] MSIX bundle built and uploaded
- [ ] Store listing complete: description (EN), screenshots, category
- [ ] App passes Store certification
- [ ] Published app installs and runs correctly from Store
- [ ] Version update flow tested — submit new version, confirm auto-update delivers

---

### T-F33 — Archive Verify Command
- [ ] **Status:** cancelled — integrity manifest removed; ZIP CRC-32 is sufficient

**What:** CLI command to verify archive integrity without extraction.
Checks ZIP structure and PAKKO-INTEGRITY-V1 manifest if present.

**Acceptance criteria:**
- [ ] verify command reads ZIP structure — reports corrupted entries
- [ ] If PAKKO-INTEGRITY-V1 manifest present — verifies SHA-256 per entry
- [ ] Exit code 0 = valid, 1 = invalid
- [ ] Human-readable output: per-entry status
- [ ] dotnet test passes

---

### T-F34 — Archive Metadata in ZIP Comment
- [ ] **Status:** cancelled — integrity manifest removed; ZIP CRC-32 is sufficient

**What:** Store Pakko version and creation timestamp in ZIP comment
alongside existing PAKKO-INTEGRITY-V1 manifest.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] PAKKO-VERSION written to ZIP comment on archive creation
- [ ] PAKKO-CREATED (UTC ISO 8601) written to ZIP comment
- [ ] Existing PAKKO-INTEGRITY-V1 format unchanged — new fields appended
- [ ] dotnet test passes — existing integrity tests unchanged

---

### T-F35 — Streaming Pipeline Architecture
- [x] **Status:** complete — implemented 2026-07-18, see `DECISIONS.md`'s T-F35 entry for the
      full design/research trail (design-advisor session, Option A vs. B trade-off, the real
      compute-gate bug the whitebox concurrency test caught, the Zip64-local-header offset-swap
      bug the cross-tool `7za.exe` test caught).
- **Priority:** low → promoted after T-F114 measured a real ~6x regression for many-small-files
  `SingleArchive` archiving against a 7z reference (user-driven investigation, 2026-07-17/18).
- **Depends on:** T-F12 (Parallel Compression) — complete.

**What:** `ZipArchiveService.ArchiveAsync`'s `SingleArchive` mode gates on file count
(`ParallelPipelineFileCountThreshold = 64`). Below it, archiving still runs through the
original, completely unmodified, always-sequential `ZipArchive`-based code (proven, low-risk,
covers the overwhelming majority of real usage). Above it, it routes into a new
`Archiver.Core/Services/Zip/` subsystem:

```
WorkItemEnumerator (deterministic T-F31/T-F32/T-F30/T-F66/T-F23/T-F75-preserving traversal)
  → ParallelSingleArchiveWriter (bounded Channel<Task<WorkResult>> + SemaphoreSlim compute gate —
     EVERY non-placeholder file compresses in parallel, regardless of size: small files (≤1 MiB)
     compressed in memory via ZipEntryCompressor; everything else compressed into a private
     per-worker temp file, bounded by a fixed copy buffer, not file size)
  → ZipEntryWriter (hand-rolled ZIP container writer — local file headers, central directory,
     EOCD/Zip64 — since System.IO.Compression.ZipArchive gives no API to compress independently
     of the live archive and splice the result in later)
```

**Deviations from the original one-line sketch, found necessary during implementation:**
- **`System.IO.Compression.ZipArchive` cannot be reused for the write side at all** once gated —
  it and a hand-rolled writer can't share one output stream, so *every* entry above the gate goes
  through `ZipEntryWriter`.
- **Zip64 is conditional, not "always on."** Decided per-field (local header per-entry from
  exactly-known sizes; central directory/EOCD per-record/globally at dispose time) —
  unconditional Zip64 on every entry was considered and rejected as needless per-entry bloat
  for a decision that's fully covered by a small, exhaustively-tested boundary function. See
  `DECISIONS.md`.
- **No data descriptor, ever — not even for large files.** The original design needed one for
  "large files stream sequentially, sizes unknown until done"; a same-day follow-up (user-driven
  discussion — "why does a size limit need to exist at all?") replaced that whole design with
  per-worker **temp-file compression** for anything above the (lowered, 1 MiB) in-memory
  threshold: a background worker streams a file into its own private temp file, so crc/
  compressed/uncompressed size are fully known by the time the writer touches the entry, same as
  the in-memory case. `WorkResultKind.LargePassthrough`/`ZipEntryWriter.WriteStreamedEntryAsync`
  and the placeholder-then-patch mechanism they needed were deleted outright, replaced by
  `WorkResultKind.TempFileCompressed`/`ZipEntryWriter.WriteCompressedEntryFromStreamAsync`. A
  temp file's bytes can't be spliced into the final ZIP for free (no Windows zero-copy primitive
  for inserting bytes mid-file) but the required copy is pure I/O, not repeated compression — see
  `DECISIONS.md`'s follow-up entry.
- **The bounded channel alone does NOT bound compute concurrency** — a real `SemaphoreSlim`
  compute gate was required in addition (caught by a whitebox test before it shipped; see
  `DECISIONS.md`).
- **Temp-file cleanup needed a second fix**: tracking created-but-unconsumed temp files in a
  `ConcurrentDictionary` and sweeping in a `finally` isn't sufficient by itself — a straggler
  compress task can finish and register its temp file *after* an earlier sweep (triggered by
  cancellation) already ran. Fixed by awaiting every dispatched compress task before sweeping.
  Caught by a test that failed intermittently only under full-suite parallel load, not in
  isolation — see `DECISIONS.md`.
- **Progress reporting needs no `Interlocked`** — since exactly one thread (the writer/consumer)
  ever calls `progress.Report`, unlike T-F12's `SeparateArchives` mode.

**Files:**
- `src/Archiver.Core/Services/Zip/DosDateTime.cs`, `ZipEntryCompressor.cs`, `ZipEntryWriter.cs`,
  `FileWorkItem.cs`, `WorkResult.cs`, `WorkItemEnumerator.cs`, `ParallelSingleArchiveWriter.cs`
- `src/Archiver.Core/Services/ZipArchiveService.cs` — the gate (`ParallelPipelineFileCountThreshold`,
  `ComputeSingleArchiveTotals`/`ComputeDirectoryTotals`), `GetUniqueEntryName` widened to
  `internal` for reuse
- `src/Archiver.Core/IO/Crc32.cs` — added `Accumulator` (incremental CRC-32, reused by
  `ZipEntryCompressor` and `ZipEntryWriter`'s `CopyWithCrcAsync`) alongside the existing `Compute(Stream)`

**Acceptance criteria:**
- [x] FileWorkItem record defined: path, entryName, size, kind, last-write-time
- [x] Deterministic enumerator produces the exact same T-F31/T-F32 order as the old recursive walk
- [x] Compression workers run in parallel (bounded `SemaphoreSlim` compute gate + bounded channel)
- [x] Writer stage is single-threaded — ZIP format requires sequential entry writes
- [x] CancellationToken respected in all stages (already-cancelled graceful no-op; mid-flight
      throws and leaves no orphaned background tasks, both covered by tests)
- [x] Progress reporting — no `Interlocked` needed (single-threaded reporter by construction)
- [x] SingleArchive mode only — SeparateArchives already parallelized in T-F12, untouched
- [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` passes — existing archive tests
      unchanged (all stay below the gate threshold, so they still exercise the untouched
      sequential path) — 462 tests total across the solution, up from the pre-T-F35 baseline
- [x] Verified: no file corruption in the parallel pipeline — cross-tool validity (`ZipFile.OpenRead`
      + vendored `7za.exe` integrity check + an independent raw structural byte parser),
      byte-identical/entry-order-identical determinism at scale (120 files), per-file error
      isolation at scale, mixed small+large files in one archive
- [x] T-F114 perf ratios re-measured, then re-measured again after a profiling follow-up: a
      user-requested `Stopwatch`/`GC`-instrumented diagnostic (`OverheadProfilingProbe.cs`,
      temporary, deleted after use) found the real dominant remaining cost was NOT inside the
      parallel pipeline (which was already performing close to the 7z reference) but THREE
      redundant full directory-tree walks before any real work started — `ComputeTotalBytes`
      (pre-existing), the gate's own new `CountFiles` (T-F35's own added cost), and
      `WorkItemEnumerator`'s real walk. Merged the first two into one `ComputeSingleArchiveTotals`
      walk. Then re-measured a THIRD time after replacing the file-size ceiling with per-worker
      temp-file compression (see the "Deviations" section above). Final ratio history:
      `ArchiveAsync_ManySmallFiles` 6.02 → 2.39 (pipeline) → 2.2 (stat fix) → 1.45
      (enumeration-merge fix) → **~1.0** (temp-file redesign — unaffected in principle, since all
      its files are under even the new 1 MiB threshold, but re-measured for completeness: 0.92-1.03
      observed, essentially parity with 7za); `ArchiveAsync_Hybrid` 3.47 → 3.03 → 2.85 →
      **~1.3** (the real target of the temp-file redesign — this scenario's 4 medium 5-20 MB files
      were exactly what the old 4 MiB ceiling excluded from parallelism; 0.93-1.54 observed);
      `ArchiveAsync_OneLargeFile` 1.22 → 1.23 → 1.20 → **1.18** (unaffected throughout — a single
      file's total count never crosses the gate). See `DECISIONS.md`'s two T-F35 profiling/
      follow-up entries for the full stage-by-stage breakdown. The real 65,536-file Zip64 `Slow`
      test also dropped from ~1m10s to ~37-39s under these fixes and re-confirmed passing through
      the final temp-file-based design.
- [ ] Manual on-device verification (archive a real folder of 100+ small files via the installed
      Pakko GUI/context menu, confirm the result opens without corruption warnings in Explorer/
      7-Zip/WinRAR) — per this project's workflow rule, not graduated on `dotnet test` alone.

---

### T-F36 — Pluggable Archive Engine Interface
- [ ] **Status:** SUPERSEDED (partially) / deferred to v1.5 — reassessed 2026-07-07, see note below.
      Kept per the "never silently deprecate" rule, not deleted.
- **Priority:** low
- **Depends on:** T-F04 (superseded — see below)

> **2026-07-07 reassessment:** this task predates T-F47–T-F50/T-F85's actual tar.exe
> integration and no longer matches the shipped architecture or `SPEC.md`'s roadmap. Two
> separate things were conflated under one task:
> 1. **Multi-format *extraction*** — the motivation this task and T-F48's blocked criterion
>    both cite. Already solved, differently: `ArchiveFormatDetector` + `IExtractionRouter`
>    (T-F85) auto-detect format and route to `IArchiveService`/`ITarService`, surfacing a
>    specific `SkippedFiles` message for anything `TarCapabilities` reports unsupported. No
>    format *selector* exists or is needed for extraction — nothing here to unblock.
> 2. **Multi-format *archive creation*** (the literal "Format: ZIP/TAR/TAR.GZ" dropdown next to
>    the Archive button) — this is real, unbuilt work, but `SPEC.md`'s roadmap table places
>    "TAR creation via tar.exe" at **v1.5**, not now. Building a full `IArchiveEngine`
>    abstraction today for one real engine (`ZipEngine`) plus a `TarEngine` *stub* would be a
>    premature abstraction for a feature nobody has asked to pull forward — confirmed with user
>    2026-07-07, who chose to defer rather than build it now.
>
> T-F04 (the "Depends on") is equally stale — its generic "TAR/GZip/BZip2/XZ Support" scope was
> superseded by the actual T-F47–T-F50 tar.exe integration long ago; T-F36's dependency line
> should be read as "the tar.exe subprocess plumbing already exists" (true today), not as a
> pointer to unfinished work.
>
> **When this becomes real work (v1.5):** re-scope as "add archive creation to `ITarService`"
> rather than a from-scratch `IArchiveEngine` interface — `ITarService`/`TarCapabilities`
> already exist and are the natural place to add a `CompressAsync`-shaped method, with the UI
> format selector wired to `TarCapabilities` the same way `TASKS.md`'s original text intended.

**What (original, pre-reassessment text — see note above for current status):** Introduce IArchiveEngine abstraction to decouple core logic from ZIP-specific implementation. Enables TAR, tar.gz, and future formats without UI changes.

**Architecture:**
```
Archiver.Core
  IArchiveEngine
    ZipEngine       ← current ZipArchiveService refactored
    TarEngine       ← T-F04
    FutureEngines
```

**UI impact:** Archive Format dropdown added to UI:
```
Format: [ ZIP ▾]   ZIP / TAR / TAR.GZ
```

**File:** `src/Archiver.Core/Interfaces/IArchiveEngine.cs` (new)

**Acceptance criteria:**
- [ ] IArchiveEngine interface defined with ArchiveAsync and ExtractAsync
- [ ] ZipArchiveService refactored to implement IArchiveEngine
- [ ] IArchiveService updated or replaced — no breaking changes to existing callers
- [ ] TarEngine stub created — ready for T-F04 implementation
- [ ] Format selector in UI — ZIP default, extensible
- [ ] DI registration updated — engine selected based on format choice
- [ ] dotnet test passes — existing 45 tests unchanged
- [ ] Adding new engine requires: new class + DI registration — no other changes

---

### T-F91 — Multi-Language Localization (OS-Language Auto-Match, English Fallback)
- [~] **Status:** partial — first batch (all 24 European locales) implemented 2026-07-07;
      Arabic/Japanese/Chinese/etc. (the non-European half of the target list) not started;
      on-device verification, layout-corruption check, and native-speaker translation review
      still outstanding for the European batch. See `DECISIONS.md`'s T-F91 entry.
      **Parity gap found and fixed 2026-07-14:** every string key added by later features
      (T-F05's browse-mode columns/buttons/tray menu/archive-option items, T-F06's conflict
      dialog) had only ever been added to `en-US` and `uk-UA` — the other 22 European locales
      were silently 37 keys behind (31/68 real keys), falling back to English for a large
      fraction of the UI. Found by diffing `<data name=` counts across all 25 `Resources.resw`
      files rather than trusting this doc. Translated the missing 37 keys into all 22 locales
      (bg/cs/da/de/el/es/et/fi/fr/hr/hu/it/lt/lv/nb/nl/pl/pt/ro/sk/sl/sr-Latn/sv), matching each
      locale's existing established terminology; all 25 locale files now carry the same 68 real
      keys (`en-US` stays at 70 — its 2 non-translatable URL keys are deliberately absent from
      every other locale, per this task's own design). `dotnet build src/Archiver.App.csproj`
      confirmed 0 errors with the expanded resources.
      **Non-European batch AI-translated 2026-07-15** (user-directed — see below on why this is
      AI translation, not native-speaker review): all 12 previously-unstarted locales now have a
      full `Resources.resw` with the same 68 real keys — `ar-SA` (Arabic), `ja-JP` (Japanese),
      `zh-Hans` (Chinese, Simplified — no region/dialect specified by the user, chose the
      Simplified/mainland default per Windows' own MUI convention), `id-ID` (Indonesian), `hi-IN`
      (Hindi), `vi-VN` (Vietnamese), `tr-TR` (Turkish), `ko-KR` (Korean), `ur-PK` (Urdu), `th-TH`
      (Thai), `he-IL` (Hebrew), `sw-KE` (Swahili). `dotnet build src/Archiver.App/Archiver.App.csproj
      /p:Platform=x64` confirmed 0 errors/warnings and the generated `AppxManifest.xml` lists all
      37 `<Resource Language>` entries (was 25) with no manual manifest edit — `x-generate` picked
      up all 12 new folders automatically, as designed. **Known limitation, not fixed by this
      batch:** Arabic/Urdu/Hebrew text is translated into the correct RTL script and will shape
      correctly character-by-character (Windows' own text renderer handles that), but Pakko's XAML
      never sets `FlowDirection` — the overall UI layout (button order, alignment) stays
      left-to-right rather than mirroring, which is its own separate scope this task's acceptance
      criteria never asked for (only "layout corruption" — clipping/truncation — was in scope, not
      full RTL mirroring). Worth a follow-up task if true RTL mirroring is ever wanted.
      Native-speaker review pass for all 36 non-English locales remains outstanding — text is
      AI-translated to a professional-UI standard throughout, consistent with this task's own
      "don't ship an unreviewed MT dump" caution; see the criterion below for why this batch could
      only close the missing-language gap, not the review requirement itself.
- **Priority:** low ("nice to have" bonus, per user)
- **Depends on:** none

**What:** `src/Archiver.App/Strings/` currently has only `en-US/Resources.resw` — the app is
English-only. WinUI 3 + MSIX already auto-select the UI language from the OS display language
via resource qualifiers (folder name = BCP-47 locale, declared in `Package.appxmanifest`'s
`<Resources>` element) — no app code is needed for the matching itself, only the translated
`Resources.resw` per locale plus the manifest declarations.

**Explicitly out of scope (confirmed with user):**
- No installer-time language picker — MSIX has no install-time UI to add one to.
- No install-location picker — MSIX always installs to the sandboxed `WindowsApps` path;
  there is no user-choosable install directory on this platform. Document as a non-goal in
  `DECISIONS.md` rather than revisiting.
- No in-app manual language override — OS-language auto-match only, per user's stated scope.

**Target language list (confirm before starting translation work — large scope, deliver
incrementally per locale rather than all at once):**
- European, human-quality translation, **excluding Russian and Belarusian**: Ukrainian, German,
  French, Spanish, Italian, Polish, Portuguese, Dutch, Romanian, Czech, Slovak, Hungarian, Greek,
  Swedish, Danish, Finnish, Norwegian, Bulgarian, Croatian, Serbian, Slovenian, Estonian, Latvian,
  Lithuanian
- Additional (user-requested, beyond Europe): Arabic, Japanese, Chinese, Indonesian, Hindi,
  Vietnamese, Turkish, Korean, Urdu, Thai, Hebrew, Swahili
- **Explicitly excluded:** Persian/Farsi (per user — Iran)
- Any OS language not on this list falls back to `en-US` — WinUI 3's `ResourceManager` does this
  automatically as long as `en-US` stays the manifest's neutral/default language.

**Note on translation quality:** user asked for "human" quality, not raw machine translation —
each locale needs a native-speaker pass or at minimum a correctness review before shipping;
don't ship an unreviewed MT dump under a locale folder.

**Acceptance criteria:**
- [x] Final language list confirmed with user before translation work begins — European batch
      (all 24 locales) confirmed 2026-07-07; non-European batch (Arabic, Japanese, Chinese,
      Indonesian, Hindi, Vietnamese, Turkish, Korean, Urdu, Thai, Hebrew, Swahili) confirmed
      2026-07-15
- [x] `Resources.resw` created under `Strings/<locale>/` for each confirmed locale, translating
      every key already in `en-US/Resources.resw` — all 36 non-English locales (24 European +
      12 non-European) now carry the same 68 real keys (2 URL keys deliberately omitted
      everywhere except `en-US`, see `DECISIONS.md`)
- [x] `Package.appxmanifest`'s `<Resources>` element declares every shipped locale — confirmed
      automatic via the existing `<Resource Language="x-generate"/>`; generated `AppxManifest.xml`
      lists all 37 locales after a `dotnet build`, no manual manifest edit needed (was 25 before
      this round's 12 non-European additions)
- [x] OS display language automatically selects the matching `Resources.resw` with no app code
      change — verified on-device for `uk-UA` 2026-07-15 via a direct screenshot of the installed,
      packaged app (Windows UI-automation MCP wasn't loaded this session — used a self-contained
      PowerShell `GetWindowRect`/`CopyFromScreen` capture instead, see `.claude.local.md`). This
      pass is also what found and fixed **T-F104** — the Archive/Extract buttons never actually
      respected any locale (dead resw keys), and the empty-state hint text was clipped — both real
      bugs unrelated to this criterion itself but caught while verifying it
- [ ] An excluded/unsupported OS language (e.g. `ru-RU`) falls back to `en-US` text, not a
      blank string or resource-load crash — **not verified this round.** The only way to exercise
      this without an in-app language override (a confirmed non-goal above) is to reorder the
      Windows display-language list, which is a system-settings change outside what an agent should
      do unattended — needs either the user doing it themselves, or the Windows MCP automation tool
      (not loaded this session) if it has its own mechanism
- [x] No installer-time language picker or install-location picker added (confirmed non-goal)
- [~] Max text-length budget determined per UI string (buttons, labels, dialog titles) — not
      systematically done across all 36 locales, but the one real overflow this round's on-device
      pass surfaced (the empty-state hint text, locale-independent — see T-F104) was found and
      fixed. No locale-specific "this translation is too long for its control" case confirmed yet
- [~] Manual on-device check for layout corruption (clipped/overlapping/truncated text, buttons
      that no longer fit their label) on at least one long-text locale (e.g. German) and one
      wide-glyph/RTL locale (e.g. Arabic or Hebrew) — done for `uk-UA` only (found and fixed a real
      clipping bug, T-F104); German/Arabic/Hebrew specifically still need either the user's own
      on-device pass with the display language changed, or the Windows MCP tool
- [x] `dotnet build src/Archiver.App` succeeds with all new resources — 0 warnings, 0 errors
- [x] `DECISIONS.md` entry: MSIX install-location non-goal + language auto-match mechanism
- [ ] Native-speaker/correctness review pass on all 36 non-English translations before shipping —
      current text (including the 12 non-European locales added 2026-07-15) is AI-translated to a
      professional-UI standard but unreviewed, per this task's own "don't ship an unreviewed MT
      dump" requirement. An AI agent cannot substitute for this — genuinely needs a human native
      speaker per locale

---

## v1.2 — Shell Extension

> **Minimum supported OS:** Windows 10 1809 (10.0.17763.0).
> Shell extension uses dual registration:
> - `desktop4:FileExplorerContextMenus` — Win10 1809+, classic context menu
> - `IExplorerCommand` via COM — Win11 22000+, modern context menu
>
> Both mechanisms invoke `Archiver.Shell.exe`. No separate code paths needed.

---

### T-F41 — Context Menu: Extract Here
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
      implemented as `ExtractHereCommand` and smoke-tested. Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Extract here" command on ZIP files — extracts to same folder as archive. Runs silently via `Archiver.Shell.exe --extract-here`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of `.zip` files
- [ ] Invokes `Archiver.Shell.exe --extract-here "<path>"` for each selected ZIP
- [ ] Extraction runs silently — `Archiver.ProgressWindow` shows progress (T-F54)
- [ ] Extracts to same directory as archive (T-14 smart folder logic)
- [ ] Multi-selection: all selected ZIPs extracted in a single `Archiver.Shell` invocation
- [ ] `Archiver.ProgressWindow` auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

### T-F42 — Context Menu: Extract to Folder
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
      implemented as `ExtractFolderCommand` and smoke-tested. Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Extract to `<folder_name>`" on ZIP files — creates a named subfolder automatically. Runs silently via `Archiver.Shell.exe --extract-folder`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of `.zip` files
- [ ] Invokes `Archiver.Shell.exe --extract-folder "<path>"` for each selected ZIP
- [ ] Creates `<archive_name>\` subfolder next to archive; extracts into it
- [ ] Multi-selection: each ZIP gets its own named subfolder
- [ ] `Archiver.ProgressWindow` shows progress, auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

### T-F43 — Context Menu: Archive with Pakko
- [ ] **Status:** future (v1.2) — **superseded by T-F61, see the NanaZip Parity Review note above**; already
      implemented as `ArchiveCommand` and smoke-tested (label/naming gap tracked separately
      as T-F64). Do not re-implement.
- **Depends on:** T-F53, T-F54, T-F55

**What:** "Add to `<name>.zip`" on any files/folders — single archive, Fast compression, destination = source folder. Runs silently via `Archiver.Shell.exe --archive`; progress shown in `Archiver.ProgressWindow`.

**Acceptance criteria:**
- [ ] Appears in Pakko submenu on right-click of any files/folders
- [ ] Invokes `Archiver.Shell.exe --archive "file1" "file2" ...`
- [ ] Creates single `.zip` archive next to the first selected item
- [ ] Uses Fast compression level
- [ ] Supports multi-selection (all selected items passed in one invocation)
- [ ] `Archiver.ProgressWindow` shows progress, auto-closes 1.5 sec after success
- [ ] Error shown in `Archiver.ProgressWindow` dialog on failure

---

## Context Menu — NanaZip Parity Review (2026-07-04)

Per project direction, NanaZip is the reference implementation for what the Pakko context
menu should offer. Reviewed NanaZip's actual modern (`IExplorerCommand`-based) shell
extension source —
[`NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`](https://github.com/M2Team/NanaZip/blob/main/NanaZip.UI.Modern/NanaZip.ShellExtension.cpp)
— the direct architectural equivalent of `Archiver.ShellExtension`, not the legacy classic
`IContextMenu` implementation (`NanaZip.UI.Classic/.../ContextMenu.cpp`), which is
irrelevant here per this project's `IExplorerCommand`-only constraint.

**NanaZip's full modern-menu command set** (flat list, no separate folder/file/mixed
submenus — conditions are evaluated per-command against the selection, not via distinct
menu trees):

| Command | Condition | Pakko status |
|---|---|---|
| Open | single file, needs extraction | done differently — double-click file association (T-F44); no explicit context-menu verb |
| Test | ≥1 file needs extraction | done — `TestCommand` (see `TASKS_DONE.md`'s T-F62) |
| Extract (dialog, picks destination) | ≥1 file needs extraction | done — `ExtractDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
| Extract Here | ≥1 file needs extraction | done — `ExtractHereCommand` (already smart: `SeparateFolders` mode strips/wraps as needed, equivalent to NanaZip's separate "Extract Here (Smart)") |
| Extract Here (Smart) | ≥1 file needs extraction | n/a — folded into Pakko's "Extract here" above, not a separate verb |
| Extract to "\<folder\>" | ≥1 file needs extraction | done — `ExtractFolderCommand` |
| Compress (dialog, format/options) | any selection | done — `CompressDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
| Compress to "\<name\>.zip" (one click) | any selection | done, but see T-F64 (label says "Add to archive…" though behavior is already the one-click no-dialog path) |
| Compress to "\<name\>.7z" | any selection | out of scope — 7z creation forbidden (`CLAUDE.md`: ZIP only, no third-party compression code) |
| Compress + Email variants (×4) | any selection | **out of scope, deliberately** — mail client integration adds attack surface and a dependency the gov/defense trust model doesn't need; not tracked as a task |
| CRC/Checksum submenu (CRC-32/64, SHA-1/256/384/512, BLAKE2/3, etc.) | any selection | covered by existing T-F46 (File Hash Viewer), which already targets SHA-256; T-F46 is in-app UI only today, not a context-menu verb — cross-referenced, no new task |

**Note on T-F41/T-F42/T-F43:** these three older task entries (below, still `future`/unchecked)
describe "Extract Here", "Extract to Folder", and "Archive with Pakko" as if unimplemented.
They predate T-F61 and are now superseded by it — all three behaviors are implemented and
smoke-tested there. Left in place with a note rather than deleted, per the "never silently
deprecate" rule; do not re-implement them as new work.

---

## v1.3 — tar.exe Integration

### T-F48 — tar.exe Capability Detection
- [~] **Status:** partial (v1.3) — detection logic complete (all other criteria `[x]`). The one
      remaining criterion (grey out unsupported formats in a tar format selector) is reassessed
      as of 2026-07-07: not "blocked on T-F36" so much as **not applicable to extraction at
      all** — `IExtractionRouter` (T-F85) auto-detects format and reports unsupported ones via a
      specific `SkippedFiles` message, with no selector in the loop. The criterion only makes
      sense once T-F36's real remaining scope (an *archive-creation* format selector, v1.5) is
      built — see T-F36's note. Left `[~]` rather than `[x]` since the literal criterion is still
      unmet, but it is no longer this task's blocker to chase

**What:** At app startup, run `C:\Windows\System32\tar.exe --version` to detect version and probe which formats are supported. Cache result as `TarCapabilities` singleton. UI greys out unsupported formats with tooltip "Requires Windows 11 23H2+".

**Implementation:** `TarProcessService.DetectCapabilitiesAsync` invokes `tar.exe --version`
(absolute path, stdout captured) and delegates parsing to the new `TarVersionParser.Parse`,
extracted into its own class so format detection is unit-testable without launching a process
(same rationale as `ShellArgumentParser`, T-F57). `Supports7z`/`SupportsRar`/`SupportsZstd` are
gated on libarchive >= 3.7.0 (matches `TESTING.md`'s documented "requires Win 11 23H2+ tar.exe"
note on all three formats — zstd is version-gated, not just token-gated, since a hypothetical
older libarchive build linking `libzstd` would still contradict that documented threshold).
`SupportsXz`/`SupportsLzma`/`SupportsBz2` are detected from the corresponding library tokens in
the version string, since `TESTING.md` does not flag those as 23H2+-only. Any failure to start
the process, or unrecognized output, returns the
all-unsupported `TarCapabilities` default — never throws. Found along the way: the T-F47
factory-registered `TarCapabilities` singleton only runs on first *resolution*, not at container
build — since nothing yet injects `TarCapabilities`, detection would silently never run. Fixed by
explicitly resolving it once in `App.xaml.cs`'s `ConfigureServices` right after
`BuildServiceProvider()`. Since that forced resolution runs synchronously on every app launch
(including the T-F83 cold-start path), `DetectCapabilitiesAsync` enforces a 5-second timeout via
an internal `CancellationTokenSource` and kills the process on expiry — a hung `tar.exe --version`
must not hang app launch indefinitely.

**Acceptance criteria:**
- [x] `DetectCapabilitiesAsync()` runs `C:\Windows\System32\tar.exe --version` (absolute path)
- [x] Parses version string and probes format support
- [x] Returns sensible defaults if tar.exe absent or probe fails
- [x] Result cached — detection runs once at startup (`App.xaml.cs` forces resolution explicitly;
      see note above — a bare DI registration alone does not run it)
- [ ] UI greys out formats not supported by detected tar.exe — no tar format selector exists for
      extraction (not needed — see T-F36's 2026-07-07 note: `IExtractionRouter` already handles
      unsupported formats without a selector); applies once T-F36's v1.5 archive-creation format
      selector is built instead
- [x] `dotnet test` passes — unit test with mocked process output (`TarVersionParserTests`, no
      process launch)

---

### T-F89 — Cosmetic: Operation Summary Dialog Mislabels Every Skip Reason as "Unsupported Format"
- [~] **Status:** partial — fix applied, compile-checked, and on-device verified for the
      conflict-skip case (2026-07-07, AI-driven); the unsupported-format case still can't be
      triggered on this machine — this system's tar.exe/bsdtar 3.8.4 supports every format
      `TarCapabilities` tracks, same known limitation already noted on T-F85/T-F86
- **Depends on:** none

**What:** `Resources.resw`'s `SkippedSectionHeader` string is hardcoded to *"Skipped — unsupported
format"* and is used as the section header for `SkippedFiles` regardless of the actual skip
reason. While verifying T-F87's fix (a ZIP whose only entry conflict-skipped because a file with
the same name already existed at the destination), the summary dialog showed:

```
Completed with issues
Skipped — unsupported format (1)
  skiptest.zip
  No entries were extracted from this archive — every entry was skipped.
```

The per-item reason text underneath is correct and specific; only the section *header* is wrong —
it claims "unsupported format" for what was actually a conflict skip. This is pre-existing
(the header string predates T-F87) and not something T-F87 introduced — T-F87 just made a
previously-rare all-skipped-archive dialog appearance (conflict-skip-all) common enough to notice
the mislabel in practice.

**Scope:** change `SkippedSectionHeader` to a reason-neutral label (e.g. plain "Skipped (N)") in
`src/Archiver.App/Strings/en-US/Resources.resw`, or — if per-category headers are wanted — group
`SkippedFiles` by reason category before rendering. Check `IDialogService`'s
`ShowOperationSummaryAsync` implementation for how the header is consumed before choosing an
approach.

**Fix:** `SkippedSectionHeader` in `Resources.resw` changed from `"Skipped — unsupported format"`
to plain `"Skipped"` — `DialogService.cs`'s existing `$"{header} ({count})"` composition renders
this as "Skipped (N)", matching the reason-neutral, count-suffixed shape `ErrorSectionHeader`
("Errors") already uses. Per-item reason text underneath (already correct and specific) is
unchanged.

**Acceptance criteria:**
- [x] Section header no longer claims "unsupported format" for skips that aren't format-related
      (conflict skips, ADS/reserved-name/reparse-point/zip-bomb skips, whole-archive skips)
- [x] Existing "unsupported format" skips (RAR/7z on pre-23H2 tar.exe) still read sensibly under
      whatever header replaces it — "Skipped (N)" plus the existing specific per-item reason text
- [x] `dotnet build src/Archiver.App` succeeds (WinUI 3, CLI-buildable per `CLAUDE.md`)
- [x] Manual on-device verification, conflict-skip case (done 2026-07-07, AI-driven via
      `pakko://extract?...` protocol activation + Windows UI automation): built a ZIP with one
      entry, pre-created a same-named conflicting file at the extraction destination, extracted
      with default `OnConflict=Skip` — summary dialog showed the exact text "⊘ Skipped (1)" (not
      the old "Skipped — unsupported format"), with the specific per-item reason ("No entries were
      extracted from this archive — every entry was skipped.") intact underneath
- [ ] Unsupported-format case: still not triggerable on this machine (see Status above)

---

### T-F96 — Bug: `Deploy.ps1`/`dotnet publish` Fails Cleaning Up PackageLayout After a Valid `.msix` Is Written
- [~] **Status:** closed as non-blocking, not on active investigation — the tolerance mitigation
      (2026-07-07) has now absorbed the race live on at least two separate occasions (2026-07-15's
      T-F52 deploy, and again during this same T-F107 session's `Deploy.ps1` run on 2026-07-16,
      producing 1.2.0.35) without ever failing a build. Root cause is still genuinely unconfirmed
      (none of the four ranked scenarios below have been tested), so this stays `[~]`, not `[x]`,
      per this project's completion rules — but since the workaround has proven reliable across
      multiple real recurrences and isn't costing any deploy time, it's not worth further
      investigation right now. Re-open (resume the `Stop-Service WSearch` test, etc.) only if the
      tolerance guard itself ever fails to catch a real recurrence, or if deploy reliability
      becomes a problem again.
- **Depends on:** none

**Diagnostic update (this round, advisor session):** the earlier `ExtractAssociatedIcon`-adjacent
theory that this was a *wedged/stale* directory (per the "Deploy.ps1 Failed After T-F91" entry in
`DECISIONS.md`) does not fit here — every manual `rm -rf` on the "locked" path succeeded
immediately (`exit=0`) moments after MSBuild's own `RemoveDir` failed on the identical path. A
wedged directory or DACL problem would block a manual delete too; a handle that's gone by retry
time means a **transient live handle held during the build**, not stale state. `RemoveDirectory`
also returns `ACCESS_DENIED` (not `SHARING_VIOLATION`) when a *child file* still has an open
handle — the earlier "ACCESS_DENIED must mean wedged, not a live handle" heuristic was based on
reasoning about opening a single file, which doesn't transfer to removing its parent directory.

**What:** `dotnet publish` (both directly and via `Deploy.ps1`) reliably fails with
`MSB3231: Unable to remove directory "..."` — `Access to the path '...' is denied` — on a
just-created `AppPackages\Archiver.App_<version>_Test\` or `obj\...\PackageLayout\` folder,
**after** the `.msix` inside it has already been written successfully. Reproduced identically
across three clean-state attempts in one session (`dotnet build-server shutdown` + targeted folder
removal; `obj\...\PackageLayout` clean; full `obj`+`AppPackages` clean plus a version bump to get
a guaranteed-fresh folder name) and independently by the user running `Deploy.ps1` themselves.
Windows Defender was ruled out — the user has a project-wide exclusion already in place, and the
error is `ACCESS_DENIED` on a delete, not a sharing-violation shape typical of AV scanning a file
mid-write.

**Workaround used this session (not a fix):** since the `.msix` is valid and complete by the time
the error fires, uninstall the old package and `Add-AppxPackage` the freshly-built `.msix`
directly, bypassing `Deploy.ps1`'s own install step for that one run.

**Leading hypothesis, not yet tested:** a parallel-MSBuild-node race between the 25-locale
resource-generation work (T-F91 added 24 locale folders) and the packaging pipeline's own
directory cleanup — more parallel work in that stage than before T-F91, and the folder implicated
differs run to run (`cs-CZ` resources one run, `Assets` another), consistent with a timing race
rather than a fixed permissions problem.

**Root-cause scenarios (ranked, from an advisor-built menu — not yet individually tested against
a live recurrence, since the race didn't reproduce during this round's two follow-up `Deploy.ps1`
runs):**
1. **Windows Search Indexer** (top suspect) — the failing subpaths seen so far (`cs-CZ` text
   resources, `Assets` images) both fall under content types the indexer touches. Decisive test:
   `Stop-Service WSearch` (elevated) before a `Deploy.ps1` run; if the failure stops recurring,
   confirmed — permanent fix is excluding the build output folders from indexing.
2. **Third-party EDR/AV beyond Defender** — plausible given the project's government/defense
   target audience (a managed dev machine could run an endpoint agent that ignores a Defender-only
   exclusion). Check: `Get-MpPreference | Select -ExpandProperty ExclusionPath` (confirm the
   exclusion actually covers this path, not just assumed), and look for other running
   protection/EDR services.
3. **`/m:1 /nodeReuse:false` on the `dotnet publish`** — cheap test for an MSBuild-node-level race;
   inconclusive if negative, since MakeAppx/PRI-generation may parallelize internally regardless
   of `/m`.
4. **Suppress the `_Test\Add-AppDevPackage.resources\<locale>` sideload artifacts entirely** —
   `Deploy.ps1` never uses them (it `Add-AppxPackage`s the `.msix`/`.msixbundle` directly); if an
   MSBuild property gates their generation, disabling it removes one whole class of files this
   race could be racing against. Needs reading the real
   `Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets` lines involved (1831, 3140), not
   guessing a property name.

**Mitigation implemented now (unblocks deploys regardless of which theory above is correct):**
`Deploy.ps1` captures `dotnet publish`'s combined output and, only on failure, checks whether (a)
the captured output matches `MSB3231.*Unable to remove directory.*(AppPackages|PackageLayout)` and
(b) a `.msix`/`.msixbundle` newer than the publish start time actually exists under
`AppPackages\`. Only when both hold does it `Write-Warning` and continue to the existing
uninstall/install steps instead of aborting — any other publish failure (real compile/sign errors)
still fails hard, unchanged. Verified: the regex matches both real historical error variants
captured this session (`AppPackages\..._Test\` and `obj\...\PackageLayout\`) and correctly does
**not** match an unrelated real C# compile error (negative control) — tested in isolation since the
race itself didn't reproduce live in this round's two clean `Deploy.ps1` runs, so the "continue"
branch couldn't be exercised end-to-end this time.

**Acceptance criteria:**
- [x] `Deploy.ps1` tolerates the specific MSB3231-after-valid-package failure shape instead of
      aborting a successful build; any other failure still fails hard (narrow regex + freshness
      check, not a blanket try/continue)
- [x] Tolerance logic verified against real captured historical error text (positive) and a real
      unrelated compile error (negative control) — isolated regex test, not yet exercised via a
      live recurrence of the race in this round
- [x] Two clean-state `Deploy.ps1` end-to-end runs completed successfully this round (neither hit
      the race — expected, since it's confirmed intermittent, not deterministic)
- [x] **Live recurrence exercised end-to-end, 2026-07-15** (T-F52 deploy run): the exact MSB3231
      shape recurred for real (`dotnet publish exited 1 ... Archiver.App_1.2.0.31_x64.msix`) and
      the tolerance guard correctly caught it, printed the warning, and continued to a successful
      install — the "continue" branch is no longer only isolated-regex-tested, it has now run for
      real. Root cause still unconfirmed (see below); this only confirms the mitigation itself
      works live, not which of the four ranked scenarios is the actual cause
- [ ] Root cause identified (not just tolerated) — none of the four ranked scenarios above tested
      yet; next step is the `Stop-Service WSearch` test the next time the race recurs
- [ ] `CLAUDE.md`'s Build Commands section updated once a root cause (not just the tolerance
      guard) is confirmed, if it implies a standing environmental fix (e.g. an indexing exclusion)

---

## v1.4 — GPO + Low IL Sandbox

### T-F51 — Group Policy Support
- [ ] **Status:** planned, not implemented — full design done 2026-07-17 via Plan Mode + a
      design-advisor (Plan agent) session. Scope was deliberately **expanded beyond this task's
      original 4-key text below** after the advisor fetched and verified the real
      [`NanaZip/Documents/Policies.md`](https://raw.githubusercontent.com/M2Team/NanaZip/main/Documents/Policies.md)
      (via WebFetch, not from memory/description — per `CLAUDE.md`'s pre-implementation-research
      norm) and found the directly-comparable competitor already ships a richer version of the
      same idea: `AllowedHandlers`+`BlockedHandlers` (blocklist takes precedence) and a 3-state
      `WriteZoneIdExtract` (0/1/2) for MOTW. User chose to match that richer shape rather than
      keep the original binary-only 4 keys. **`StrictZipBombMode` was then dropped entirely
      (2026-07-17, user decision)** — it was already flagged by the advisor as the weakest-grounded
      of the five keys (no desktop archiver exposes a configurable compression-ratio threshold as
      a GPO value), and the user chose not to carry that complexity forward. **Final key count is
      4**, not 5. `POLICIES.md` (new, repo root) now documents this table for sysadmins, with a
      pointer from `SECURITY.md`. See "Expanded design" below for the full remaining plan; nothing
      has been coded yet — resume from here.

**Real-world grounding (advisor research, not invented scope):** checked what sysadmins/competitor
products actually ship before finalizing keys.
- `HKLM\Software\Policies\<Vendor>\` is the standard, precedented convention — 7-Zip has no native
  GPO support at all (years-long sysadmin complaint on SourceForge); WinRAR has no official ADMX
  either (third parties like PolicyPak fill the gap). Pakko shipping this natively is a real
  differentiator.
- `AllowedFormats`/`BlockedFormats` — **strongly grounded**: NanaZip ships exactly this
  (`AllowedHandlers`/`BlockedHandlers`, `REG_MULTI_SZ`, blocklist takes precedence). Keep as
  specified, now as a matched pair.
- `EnforceMOTW` — NanaZip's real `WriteZoneIdExtract` is a 3-state DWORD (0=no, 1=all files,
  2=unsafe-extensions-only), not a binary force-on. Matched to that shape (see below) — the
  original binary criterion was under-scoped relative to the shipping competitor feature it's
  modeled on.
- `DisableTarExtraction` — **no direct precedent** in 7-Zip/WinRAR/NanaZip (NanaZip achieves the
  equivalent generically via `BlockedHandlers` listing "tar"). Kept anyway, as its own key: it's
  architecturally motivated by Pakko's own tar.exe-via-AppContainer-sandbox design (T-F52) — a
  "don't spawn tar.exe at all" kill switch is a real, distinct lever (reduces process-spawn/
  sandbox-escape surface, not just format surface) that a NanaZip-style in-process libarchive
  binding doesn't need. Overlaps conceptually with excluding all non-Zip formats via
  `BlockedFormats` — document why both exist, don't merge them.
- `StrictZipBombMode` — was the **weakest-grounded of the original five**: no desktop archiver
  (7-Zip/WinRAR/NanaZip) exposes a configurable compression-ratio threshold as a GPO/registry
  value; the general zip-bomb threat is well documented, but an admin-configurable ratio looked
  like a security-team wishlist item, not a documented sysadmin ask. **Dropped from scope
  entirely** (user decision, 2026-07-17) rather than implemented — not worth the added
  `EvaluateCompressionBombAsync` signature complexity for a key with no real precedent. The
  existing hardcoded `ArchiveEntrySecurity.MaxCompressionRatio = 1000` constant is untouched.

**Naming:** the new class is `GroupPolicyService`/`GroupPolicyOptions`, **not**
`PolicyService`/`PolicyOptions` — `Archiver.Core` already has `PreviewPolicy.cs`/
`NestedArchivePolicy.cs`, business-rule classes unrelated to Windows Group Policy; a same-named
class next to those would read as ambiguous.

**Registry path:** `HKLM\Software\Policies\Pakko\` (confirmed: zero existing registry-reading code
anywhere in this repo today, including the C++ shell extension — this is genuinely new ground).

**Expanded design — keys (final, 4 keys — full sysadmin-facing detail now lives in
[`POLICIES.md`](POLICIES.md), this table is the implementation-facing summary):**
| Key | Type | Values | Effect |
|-----|------|--------|--------|
| `EnforceMOTW` | DWORD | 0=disabled, 1=all files (default when key absent — today's shipped behavior), 2=unsafe extensions only | controls MOTW propagation mode |
| `AllowedFormats` | REG_MULTI_SZ | format name list (`zip`,`tar`,`gzip`,`bz2`,`xz`,`zstd`,`lzma`,`rar`,`sevenzip`) | whitelist; absent = no restriction |
| `BlockedFormats` | REG_MULTI_SZ | same format vocabulary | blocklist; **takes precedence over `AllowedFormats`** (matches NanaZip) |
| `DisableTarExtraction` | DWORD | 0/1 | 1 = tar.exe never spawned at all (architecture-specific kill switch, see grounding above) |

**Architecture / integration points (file:line references confirmed against real code, not
guessed):**
- Lives in `Archiver.Core` (not `Archiver.App`-only) — confirmed `Archiver.Shell/Program.cs` has
  **no DI container**, constructs services directly (`new ZipArchiveService()` etc.), so both
  hosts need to call the same plain `GroupPolicyService.Load()`.
- Testability: minimal `IRegistryReader` seam (`int? GetDword(...)`, `string[]? GetMultiString(...)`)
  + one untested `Win32RegistryReader` + one hand-rolled `FakeRegistryReader` test fake, same
  `file sealed class FakeX : IX` pattern as `FakeArchiveService`/`FakeTarService` in
  `tests/Archiver.Core.Tests/Services/ExtractionRouterTests.cs:9-55` (repo has zero mocking
  library, hand-rolled fakes only). Keep this abstraction exactly this small.
- `ArchiveEntrySecurity.TryPropagateMotw` (`src/Archiver.Core/Services/ArchiveEntrySecurity.cs:
  98-118`) — currently unconditional, zero params, called from `ZipArchiveService.cs:950-951` and
  `TarSandboxedService.cs:314-318`. Needs a new `MotwMode` param (`AllFiles`/`Disabled`/
  `UnsafeExtensionsOnly`); `UnsafeExtensionsOnly` checks `destFilePath`'s extension against a list
  modeled on Windows Attachment Manager/SmartScreen's real known-executable extension set (`.exe
  .bat .cmd .com .cpl .msi .msp .scr .vbs .vbe .js .jse .ws .wsf .wsc .wsh .ps1 .ps1xml .ps2
  .ps2xml .psc1 .psc2 .msh .mshxml .scf .lnk .inf .reg .hta`) — a real, precedented list, not
  invented.
- `ArchiveEntrySecurity.MaxCompressionRatio` (`ArchiveEntrySecurity.cs:16`) is **out of scope for
  this task** — `StrictZipBombMode` was dropped, so this constant stays untouched, no injection
  point needed.
- `ZipArchiveService`/`TarSandboxedService` are currently parameterless-constructed — add an
  optional `GroupPolicyOptions? policy = null` ctor param (default = "everything allowed", matches
  today's behavior, keeps every existing `new XService()` call site compiling).
- `ExtractionRouter.IsSupported` (`src/Archiver.Core/Services/ExtractionRouter.cs:85-95`) hardcodes
  `ArchiveFormat.Tar or ArchiveFormat.GZip => true` **bypassing `TarCapabilities` entirely** — a
  policy guard for `DisableTarExtraction`/`BlockedFormats`/`AllowedFormats` must sit before/outside
  this switch, not inside it, since those two formats don't go through the `TarCapabilities`
  branch at all. Add `GroupPolicyOptions` as a new ctor param.
- `ArchiveCreationRouter` (`src/Archiver.Core/Services/ArchiveCreationRouter.cs`) has **zero**
  capability/whitelist check today — the policy guard here is wholly new code, not a modification.
  Must return an `ArchiveResult` error/skip, never throw (matches `IArchiveService`'s contract).
- Need one shared `ArchiveFormat`/`ArchiveContainerFormat` ↔ registry-string mapping function — the
  two enums don't line up 1:1 (e.g. creating `TarGz` is later *detected* as `ArchiveFormat.GZip`),
  so `AllowedFormats`/`BlockedFormats` need one consistent mapping used by both routers, decided
  once, not improvised per call site.
- UI: `MainWindow.xaml:446-456` — 7 `ComboBoxItem`s are **static hardcoded XAML, not
  `ItemsSource`-bound**; `MainViewModel.cs:249-273`'s `FormatIndex` is a hand-written index↔enum
  switch. Plan: add one shared `Visibility="{x:Bind ViewModel.TarFormatVisibility}"` binding across
  the 6 tar `ComboBoxItem`s (Collapsed items reportedly keep their slot in the `Items` index
  sequence, so `FormatIndex`'s switch keeps working unchanged — **verify this empirically with one
  real run before relying on it**, don't trust from memory). `MainViewModel` gains a
  `GroupPolicyOptions` ctor param (6th, alongside its existing 5 services); force-reset
  `SelectedContainerFormat` to Zip if the persisted selection is a now-hidden tar variant.
- DI (`src/Archiver.App/App.xaml.cs:26-48`, full `ConfigureServices()` already documented in
  `ARCHITECTURE.md`): `services.AddSingleton(GroupPolicyService.Load());` — eager, not the lazy
  factory + forced-resolve dance `TarCapabilities` needs, since a registry read is cheap/
  synchronous. Thread into the 5 consumers above.
- `Archiver.Shell/Program.cs`: no container — call `GroupPolicyService.Load()` once near the top of
  `Main`, thread into all 4 inline service-construction call sites (`BuildExtractionRouterAsync`
  ~line 125, `RunArchiveAsync` ~line 170, plus 2 other command handlers).
- `deploy/Pakko.admx` + `deploy/Pakko.adml` + `deploy/README.md` (new folder, nothing to extend) —
  standard ADMX/ADML XML, one category, 5 policy elements, `multiText` elements for
  `AllowedFormats`/`BlockedFormats`. README covers copying into `%SystemRoot%\PolicyDefinitions` or
  a Central Store (a real, non-obvious step admins need told).

**Ordered implementation steps (small, individually testable, per T-F52's phased-build precedent):**
1. Shared `ArchiveFormat`/`ArchiveContainerFormat` ↔ string mapping helper.
2. `GroupPolicyOptions` record + `IRegistryReader`/`Win32RegistryReader`/`FakeRegistryReader` +
   `GroupPolicyService.Load()` + `GroupPolicyServiceTests` (absent/present/malformed cases,
   DWORD=0 vs. absent distinguished explicitly). No consumer wiring yet.
3. `ArchiveEntrySecurity.TryPropagateMotw(archivePath, destFilePath, MotwMode)` + unsafe-extension
   list; `ZipArchiveService`/`TarSandboxedService` get the optional `GroupPolicyOptions?` ctor
   param.
4. `ExtractionRouter.IsSupported` guard + ctor param + tests using literal `GroupPolicyOptions`
   records (no registry fake needed here, same as existing `TarCapabilities` tests).
5. `ArchiveCreationRouter` guard (new code) + tests.
6. `App.xaml.cs` DI wiring.
7. `Archiver.Shell/Program.cs` — 4 call sites updated.
8. `MainViewModel` + `MainWindow.xaml` (`TarFormatVisibility`, forced format reset) — with the
   Collapsed-index empirical check.
9. `deploy/Pakko.admx` + `deploy/Pakko.adml` + `deploy/README.md`.
10. Cascade doc updates: `ARCHITECTURE.md` (new models/interfaces/DI), `SPEC.md` (GPO table —
    add `BlockedFormats`, make `EnforceMOTW` 3-state, drop `StrictZipBombMode`), `DECISIONS.md`
    (naming rationale, real-world grounding summary, why `DisableTarExtraction` stays separate
    from `BlockedFormats`, why `StrictZipBombMode` was dropped rather than implemented). Also
    confirm `POLICIES.md` (already added, see below) still matches the shipped behavior once coded.
11. `dotnet test --filter "Category!=Slow"`, no path argument, all projects green.

**Acceptance criteria (updated for final 4-key scope):**
- [ ] `GroupPolicyService` reads all four keys at startup, never throws on absent/malformed values
- [ ] Policies override corresponding user settings; `BlockedFormats` takes precedence over
      `AllowedFormats`
- [ ] `EnforceMOTW=2` propagates MOTW only to files matching the unsafe-extension list;
      `EnforceMOTW=0` disables propagation entirely; absent key preserves today's always-on default
- [ ] `DisableTarExtraction=1` hides tar format options in the UI and blocks tar.exe extraction
      end-to-end (context menu + in-app)
- [ ] ADMX/ADML template files added to repo (`deploy/Pakko.admx`, `deploy/Pakko.adml`,
      `deploy/README.md`), importable via `gpedit.msc` with no XML parse errors
- [ ] `dotnet test --filter "Category!=Slow"` passes (no path arg, all projects) — unit tests with
      a hand-rolled `FakeRegistryReader`, no mocking library
- [ ] Manual on-device verification: real registry values set under
      `HKLM\Software\Policies\Pakko\`, installed app relaunched, each of the 4 keys' effects
      confirmed for real (tar hidden/blocked, format block/allow, MOTW mode difference on a real
      `.exe`-vs-`.txt` extraction)

---

### T-F111 — Archive Browser double-click dispatch: no diagram coverage (stub, not scoped)
- [ ] **Status:** future — stub only, flagged 2026-07-17 during a `DIAGRAMS.md` audit, not
      implemented or scoped this round.

**What:** `MainWindow.xaml.cs`'s `ArchiveBrowserList_DoubleTapped`/`PendingList_DoubleTapped` have
grown real branching complexity across T-F97/98/107/109/110 (folder vs. real-filesystem-scope
archive vs. in-archive nested archive vs. previewable file vs. extract-only file — 5 distinct
outcomes from one double-click) with zero diagram coverage — `DIAGRAMS.md`'s Definition of Done
table has no row that triggers on this file at all (diagram 6 covers row *visibility*, not
double-click *dispatch*). This is exactly the "silently dropped branch" shape diagrams 3/5 exist
to catch, just in a file/method neither of those diagrams' trigger conditions names.

**Scope not yet decided:** extend diagram 6 to also cover dispatch (same subject — the window's
UI-mode behavior), add a new diagram 7, or add a new DoD table row pointing at one of the above.
Needs a decision before implementation, per this project's usual practice for diagram-affecting
changes.

---

### T-F112 — Generate state-transition tests from DIAGRAMS.md's mermaid state diagram (stub, not scoped)
- [ ] **Status:** future — proposed 2026-07-17, not implemented or scoped this round.

**What:** `DIAGRAMS.md`'s mermaid blocks are prose/diagram-only — nothing in the repo parses or
executes them, so a diagram can silently drift from the real UI state machine between audits (this
is exactly what happened to Diagram 6 across T-F97/98/105/106/107/108/109/110 before the
2026-07-17 sync pass caught it). Mermaid itself has no test runner; the diagram cannot be
"compiled" and executed directly.

**Proposed approach:** write a small one-off parser/generator (Python, `py` launcher per
project convention) that reads Diagram 6's `stateDiagram-v2` block, extracts every
`State1 --> State2 : trigger` line, and emits a skeleton per transition — either an
xUnit test skeleton (for pieces reachable without WinUI, e.g. `ArchiveBrowseScope`/
`_browseStack` transitions in `MainViewModel`) or a Windows-MCP automation script skeleton (for
pieces that need the real WinUI tree, e.g. row-visibility). Each skeleton would still need a human
to fill in the actual assertion — the generator's value is walking every diagrammed transition
mechanically so none get silently skipped, not producing complete tests unattended.

**Scope not yet decided:**
- Whether this only targets Diagram 6 (state diagram — direct fit) or is also attempted for
  diagrams 3/5 (activity diagrams — worse fit, no natural state/transition pairs to walk).
- Whether generated skeletons live as a new test project/script under `tests/`, or as a one-shot
  scratch tool re-run manually after each `DIAGRAMS.md` edit (no CI in this repo to wire it into
  automatically).
- Whether "generate" happens once per diagram edit (manual re-run) or whether the *absence* of a
  test for a diagrammed transition should itself be a checked condition (would need the parser to
  be a permanent repo tool, not a throwaway script).

Needs a decision on the above before implementation. Candidate first target if scoped: Diagram 6's
`InsideArchive` self-loop transitions added this session (T-F98's nested-archive drill-down).

---

### T-F114 — Performance/Regression Tests vs. a 7-Zip Reference (ZIP only)
- [~] **Status:** implemented and passing 2026-07-17 — all 9 implementation steps done (7za.exe
      vendored, project scaffolded, fixtures/runner built, all 6 scenarios implemented and
      calibrated against real observed ratios, doc cascade complete). Stays `[~]` rather than `[x]`
      for one reason only: the design's own verification criterion ("run on a second,
      differently-specced machine if available, to confirm the ratio actually travels across
      machines") could not be exercised this session — no second machine was available. Everything
      else is done; see `DECISIONS.md`'s T-F114 entry for the observed baseline ratios and full
      rationale.

**What:** automated tests that compress/extract fixture data with Pakko's own ZIP path
(`System.IO.Compression`) and, in the same test invocation on the same machine, run `7za.exe`
against the identical fixture — then assert on the **ratio** between the two elapsed times. Goal:
catch a code change that silently makes compression/extraction meaningfully slower (the project's
actual regression history — accidental sync-over-async, wrong buffer sizes — has been gross, not
subtle), without a flaky absolute-time threshold that breaks the moment the test runs on a
different machine.

**Real-world grounding (research done before designing, not invented scope):**
- Checked whether end users/sysadmins actually track version-over-version archiver speed
  regressions as a demand signal: **thin evidence.** The genre that exists is cross-tool
  comparison ("which archiver is fastest" — 7-Zip's own `7z b` benchmark subcommand, various
  zstd-vs-7z-vs-zip speed/ratio comparison articles), not "this specific tool got slower in its
  last release." Conclusion: this is a sound *internal engineering discipline* to self-impose, not
  something externally demanded — not framed as a user-requested feature.
- Checked how BenchmarkDotNet (.NET's own microbenchmarking library), Rust's `criterion.rs`, and
  Go's `benchstat` solve "compare speed fairly across unknown machines." **None of the three
  attempt true cross-machine baseline portability.** BenchmarkDotNet's `[Benchmark(Baseline =
  true)]` reports every other result as a ratio computed *within the same run on the same
  machine*. `criterion.rs`/`benchstat` compare against a *stored baseline from a prior run on the
  same machine* (not cross-machine). **The only mechanism that generalizes to an arbitrary,
  never-before-seen machine is running the reference and the subject side-by-side, right now, in
  the same invocation, and taking the ratio** — this directly resolves the "what do we compare
  against, given all machines are different speeds" question raised when scoping this task.
- The user's own tentative idea — cache a result in a temp file and compare against it going
  forward — was explicitly considered and **dropped**: that pattern's one legitimate use (per
  `criterion.rs`) is catching drift on the *same machine over time* (e.g. a persistent CI runner),
  and this repo has no CI today (confirmed — manual, occasionally-different-machine, pre-release
  testing cadence per `TESTING.md`). Building that infrastructure now would be speculative; revisit
  only if Pakko ever gets a persistent CI runner.
- 7-Zip's core code is LGPL v2.1+; bundling a portable `7za.exe` for test-only use requires
  attribution (state 7-Zip is used, state LGPL, link to 7-zip.org, include license text) — real,
  small, non-blocking.
- Known pitfalls confirmed from the same research: JIT/cold-start skew (needs one discarded
  warmup pass per engine before timing); process-spawn overhead for the `7za.exe` subprocess
  (fixed per-invocation cost, negligible for large files, noisiest for the many-small-files
  shape — argues for real tolerance headroom, not for dropping that shape); Windows Defender/AV
  interference is real but largely self-cancelling within one ratio (both engines get scanned in
  the same run) — documented as a known flakiness source, not coded around.

**Decisions (confirmed with user, not left as advisor-only recommendations):**
- **Bundle `7za.exe`** (portable, console-only, LGPL) directly in the new test project — pinned
  exact version, SHA-256 verified against the official 7-zip.org release, committed alongside a
  `LICENSE-7-Zip.txt` and a `NOTICE.md` (version/source URL/hash/vendored date). Rejected
  "require a system-installed 7-Zip, skip if absent": `CLAUDE.md`'s own hard constraint is
  "No 7-Zip" for the *shipped product* — the population of machines that build/test Pakko is
  disproportionately likely to not have 7-Zip installed, so "skip if absent" would silently turn
  the gate into decoration on most contributor machines. Explicitly document (code comment near
  the runner + `DECISIONS.md`) that this is a test-only, dev-time dependency, never shipped in the
  MSIX — distinct from the "zero third-party dependencies" rule, which governs `Archiver.Core`'s
  shipped surface only. Absolute-path-only to the bundled binary, mirroring `tar.exe`'s existing
  "never via PATH" convention. Still add a thin `File.Exists` presence guard as defense-in-depth
  (someone deleted the file / `.gitignore` mistake) — documented as an edge case, not the routine
  path `IntegrationAttribute` represents for tar.exe.
- **Drop the cache-in-a-temp-file/persistent-baseline idea entirely** — not built, not stubbed.
  Revisit only if a persistent CI runner is ever added.
- **ZIP only for this task** (`System.IO.Compression` vs. `7za.exe -tzip`), both archive creation
  and extraction. Explicitly **excludes tar-family** — `TarSandboxedService` routes through
  AppContainer/ACL/Job-Object sandbox machinery (T-F52), a deliberate, accepted security cost; a
  shared tolerance band against unsandboxed `7za.exe` would almost certainly "fail" on sandbox
  setup overhead alone, not a real regression. A future tar-family perf task would need its own
  separate calibration accounting for sandbox overhead as a known constant — not bolted onto this
  one.

**Core comparison mechanism:**
- Per (fixture shape × operation): one discarded warmup pass + one timed pass, for both Pakko and
  `7za.exe`, on the identical fixture, in the same test method. Assert
  `r = pakkoElapsed / referenceElapsed <= calibratedBaselineRatio * toleranceMultiplier` — 6
  hardcoded constants total (3 shapes × archive/extract), each derived by running the suite
  locally a few times first and observing real ratios, then picking a multiplier with generous
  headroom (starting point ~2–3x) to absorb machine-to-machine noise. Document the observed
  baseline numbers and chosen multiplier's rationale in `DECISIONS.md` — no bare unexplained magic
  constants.
- Also assert basic sanity (operation succeeded, output entry count/size matches expectation,
  elapsed > 0) alongside the ratio — cheap insurance against a broken timer or short-circuited
  operation silently "passing" on garbage data.
- No repeated-iteration statistics (medians/confidence intervals, criterion/benchstat-style) — a
  single timed run per engine after one warmup is proportionate for a coarse "catch a gross
  slowdown" gate; this repo has no CI to make repeated-run statistics meaningful anyway. Realistic
  sensitivity: catches a ~2x+ slowdown, not a 5–10% regression — matches the actual ask.
- Known residual risk to document, not solve: the ratio assumption holds well for raw clock-speed
  differences between machines, only approximately for *core-count* differences, since `7za.exe`
  is multi-threaded and Pakko's `System.IO.Compression` path is single-threaded — another reason
  the tolerance band needs real headroom rather than a tight one.

**Fixture shapes (generated at test-run time into a `TempDirectory`, matching
`ZipArchiveServiceZip64Tests`' precedent — never committed to git, distinct from
`GenerateFixtures`' small committed correctness fixtures; state this distinction explicitly in
`TESTING.md` so the two mechanisms aren't conflated):**
- **Many small files:** 5,000 files, 1–10 KB each (~25–30 MB total) — deliberately not Zip64's
  65,600 (that count targets the 16-bit entry-count boundary, not perf; at that scale fixture
  creation itself would dominate the perf test's own wall-clock). Kept despite being the noisiest
  shape (process-spawn overhead) because it exercises a genuinely distinct code path (per-entry
  overhead across many small Deflate streams) and was explicitly requested.
- **One large file:** ~300 MB of semi-compressible generated content (a repeating pseudo-text
  pattern — not all-zeros, not pure random noise; either extreme misrepresents realistic
  throughput/ratio behavior), streamed to disk.
- **Hybrid:** ~500 small files (1–50 KB) + 3–5 medium files (5–20 MB), total ~50–80 MB —
  resembles a realistic project-folder archive, kept smaller than the large-file fixture so its
  cost stays proportionate.
- All three hardcoded as `const` fixture parameters directly in the test class, matching
  `ZipArchiveServiceZip64Tests`' `const int fileCount = 65_600` style — no configurable sizing
  system.

**Test project placement:** new dedicated project, `Archiver.Core.PerformanceTests` — not folded
into `Archiver.Core.IntegrationTests`, even though that project already spawns real external
processes (the closest existing precedent). `IntegrationTests` has a settled identity as
"deterministic correctness/security proofs against real OS/tar.exe behavior"; mixing in a timing
assertion with inherent (if small) flake risk blurs that meaning and risks a real regression being
dismissed as "oh, that's the flaky suite." A dedicated project also cleanly owns the bundled
`7za.exe` + its license file rather than attaching an unrelated binary to the sandbox/tar.exe test
project. Every test tagged `[Trait("Category","Slow")]` — picked up automatically by the existing
`dotnet test --filter "Category!=Slow"`/`"Category=Slow"` convention, no new filtering mechanism.
Add to the `.sln` with `Debug|x64`/`Release|x64`-only config entries per `CLAUDE.md`'s hard
constraint (never `Any CPU`/`x86`).

**Category/tagging and failure-handling note (differs from Zip64's Slow tests, document
distinctly in `TESTING.md`):** a Zip64 test failure is always a real bug (deterministic, no
timing) — "just rerun it" is never right there. A perf-test failure carries a nonzero chance of
being a one-off machine hiccup (background scan, thermal throttling, a stray process). Document:
rerun once before treating a perf-test failure as a real regression; a *repeatable* failure across
reruns is the real signal. This repo has no CI — this suite is a manual pre-release gate, same
cadence as the existing Zip64 Slow tests (`TESTING.md`'s Manual Smoke Test Cycle step 6).

**Ordered implementation steps:**
1. Pin an exact 7-Zip release, verify `7za.exe`'s SHA-256, commit it + `LICENSE-7-Zip.txt` +
   `NOTICE.md` (version/URL/hash/date) under `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`.
2. Scaffold `Archiver.Core.PerformanceTests` (net8.0, xunit, FluentAssertions — matching every
   other test project), add to `.sln` with correct x64-only config entries.
3. Shared fixture-generation helper (in a `TempDirectory`, the three shapes as named
   methods/constants).
4. `7za.exe` runner wrapper (absolute path only, `Stopwatch`-timed `Process` invocation for
   `a -tzip` / `x`) plus the thin presence-check safety net.
5. Implement **one** scenario end-to-end first (recommend large-file archive) — get the ratio
   math, warmup handling, and assertion shape right before replicating.
6. Calibrate: run that one scenario locally several times, record observed `r`, pick and hardcode
   the tolerance constant with documented rationale.
7. Replicate across the remaining 5 combinations (2 more shapes × archive+extract, plus extract
   for the first shape).
8. Update docs (see below).
9. Run the full new Slow-tagged suite at least once end-to-end; if a second, differently-specced
   machine is available, run it there too — the whole premise is that the ratio travels across
   machines, worth actually checking once rather than just asserting it.

**Overbuilding — explicitly avoid:** no generic pluggable-benchmark-harness abstraction (e.g. an
`IPerformanceReference` interface anticipating a future WinRAR comparison); no persisted-
history/trend-tracking mechanism (no CI to make it valuable); no statistical rigor beyond one
warmup + one timed run per engine; no configurable fixture-size system; no tar-family coverage in
this task; no auto-download/bootstrap mechanism for the reference binary (commit it like any other
fixture).

**Doc/cascade touches:**
- `TESTING.md` — new section (mirroring the Zip64/Integration structure), update "Running
  Tests"/Manual Smoke Test Cycle step 6, the rerun-once-before-treating-as-regression caveat, and
  the GenerateFixtures-vs-this-suite distinction.
- `CLAUDE.md` — add `tests/Archiver.Core.PerformanceTests/` to Repo Layout; a "Current State"
  entry once implemented; confirm Build Commands' `Category=Slow` line still accurately describes
  what it covers.
- `DECISIONS.md` — new T-F114 entry: why same-run ratio over cross-machine cached baseline (citing
  the BenchmarkDotNet/criterion/benchstat research), why the cache-in-temp-file idea was
  considered and dropped, why `7za.exe` bundled vs. system-installed (with the LGPL attribution
  note), why ZIP-only/tar-family excluded, the calibrated tolerance constants and how they were
  derived, why a dedicated new test project rather than folding into `Archiver.Core.IntegrationTests`.
- `CONVENTIONS.md` — note that `Archiver.Core.PerformanceTests` bundles a test-only, LGPL-licensed
  native binary (`7za.exe`), explicitly distinct from the "zero third-party dependencies" rule
  (shipped product only) — otherwise a future reader hitting `CLAUDE.md`'s "No 7-Zip" hard
  constraint could reasonably be confused finding a 7-Zip binary checked into the repo.
- `tests/Archiver.Core.Tests.GenerateFixtures/README.md` — not modified directly, but the
  distinction from this suite's throwaway perf fixtures should be stated in `TESTING.md`.

**Acceptance criteria:**
- [x] `7za.exe` (x64 + arm64) + `LICENSE-7-Zip.txt` + `NOTICE.md` committed under
      `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`, hash-verified against the official
      GitHub release digest before extraction
- [x] `Archiver.Core.PerformanceTests` project scaffolded, added to `.sln` — mirrors every other
      C# test project's existing Any CPU/x64/x86-all-map-to-Any-CPU config block (not literally
      "x64-only": that reading of the hard constraint doesn't match how any existing C# project in
      this `.sln` is actually configured — see `DECISIONS.md`'s T-F114 entry)
- [x] All 6 scenarios (3 fixture shapes × archive/extract) implemented, each asserting a
      calibrated ratio + basic operation-sanity checks
- [x] Tolerance constants calibrated from real local runs, documented with rationale in
      `DECISIONS.md` (observed ratios 1.06-6.02 depending on scenario, 3x multiplier)
- [x] **Revised same day, user-directed:** the many-small-files/hybrid scenarios (4 tests) tagged
      `[Trait("Category","Slow")]` as originally designed; the one-large-file scenarios (2 tests)
      moved to a new `[Trait("Category","VeryLarge")]` instead — on demand only, never part of the
      normal `Category=Slow` pre-release run. Zip64's own >4 GiB test
      (`ArchiveAndExtract_FileOver4Gb_RoundTripsWithoutError`) moved to `VeryLarge` the same way,
      for consistency (same "genuinely oversized, opt-in only" reasoning). `dotnet test --filter
      "Category!=Slow"` unaffected (confirmed: 43+55+280+55 still pass); `Category=Slow` now runs 4
      perf tests + 3 Zip64 tests (was 6+4); `Category=VeryLarge` runs exactly the 3 gated tests (2
      perf + 1 Zip64), confirmed by name in test output, all passing
- [x] **7za.exe launches sandboxed, user-directed:** every `7za.exe` invocation now runs under a
      basic sandbox reusing `SandboxJobObject`/`SandboxedProcessLauncher` from tar.exe's own T-F52
      subsystem — Job Object only (no child-process creation, 2 GiB RAM / 10 min CPU caps),
      deliberately **without** the AppContainer/quarantine-staging layer (that layer defends
      against untrusted *input*, which doesn't apply to Pakko's own generated fixtures — adding it
      would also risk biasing the very timing being measured via ACL/staging overhead). Mitigates
      the risk of the vendored binary itself being compromised (bounds worst-case resource use,
      blocks spawning further processes) without touching filesystem access. Required adding
      `Archiver.Core.PerformanceTests` to `Archiver.Core.csproj`'s `InternalsVisibleTo` list (same
      mechanism as the two existing entries) since the Sandbox classes are `internal`. Re-ran all 6
      scenarios after the switch — ratios unchanged within normal run-to-run variance (e.g.
      Archive/Hybrid 3.463 vs. the original 3.469-3.519 range), confirming negligible overhead;
      existing calibrated constants did not need adjusting
- [~] Full suite run at least once end-to-end (done, multiple times, confirmed stable both before
      and after the sandboxing change) — a second, differently-specced machine was **not available
      this session** to confirm the ratio actually travels across machines as designed; this
      remains the one open item
- [x] `TESTING.md`, `CLAUDE.md`, `DECISIONS.md`, `CONVENTIONS.md`, `SECURITY.md` updated per the
      cascade above

---

### T-F115 — Shell-extension context-menu localization (37 locales) + Extract-here split

- [~] **Status:** implementation complete 2026-07-18, all automated tests green (.NET + C++).
      Stays `[~]` — not graduated to `[x]` — pending the required `Deploy.ps1` build+sign+install
      and on-device Explorer check per this project's workflow rule (shell-triggered/UI change,
      never graduated on `dotnet test`/`Archiver.ShellExtension.Tests.exe` alone).

**What:** user compared a real on-device screenshot of Pakko's Explorer context menu against
NanaZip's own (Windows UI in Ukrainian) and found two real gaps: (1) every `IExplorerCommand`
menu title in `Archiver.ShellExtension` was a hardcoded English literal — T-F91 localized only
`Archiver.App`'s WinUI XAML (resw), never the native COM shell extension; (2) `ExtractHereCommand`
("Extract here") never actually extracted flat into the current folder — it already ran
`ExtractMode.SeparateFolders`'s smart single-root-detection logic (NanaZip's own "Інтелектуально"
behavior), just mislabeled, and neither existing command ever unconditionally dumped into the
current folder without creating some destination folder first.

**Delivered:**
- [x] New `Archiver.ShellExtension/Localization.h`/`.cpp` — a plain compiled-in `StringId` →
      per-locale-text lookup table (37 BCP-47 tags, mirroring `Archiver.App/Strings/<locale>/`
      exactly), `GetCurrentUILanguageTag()` (`GetThreadPreferredUILanguages`), single-level
      en-US fallback for an unrecognized tag, and `ApplyTemplate()` for the two templated titles
      (quoted archive/folder name substitution via literal `{0}`, never `printf`-style formatting
      since the substituted value is a user-controlled filename)
- [x] All 8 pre-existing `GetTitle` overrides + `BuildAddToArchiveTitle`/`BuildExtractFolderTitle`
      (`ShellExtUtils.cpp`) now pull from the table instead of hardcoded English; the two title
      builders gained an optional `localeTag` parameter defaulting to `L"en-US"` so every
      pre-existing English-text test kept passing unchanged — production call sites
      (`ExplorerCommands.cpp`) pass `GetCurrentUILanguageTag()` explicitly
- [x] `ExtractHereCommand`'s title relabeled to "Extract to current folder (Intelligently)" /
      uk-UA "Видобути до поточної папки (Інтелектуально)" (matching NanaZip's own text verbatim) —
      zero behavior change
- [x] New genuinely-flat command, `ExtractHereFlatCommand` (new CLSID), took over the "Extract
      here" label — dumps directly into the archive's own containing folder, no wrapper folder
      ever, via `ExtractMode.SingleFolder` with `DestinationFolder` pointed straight at the
      archive's folder (no subfolder computed) — zero `Archiver.Core` changes needed, since
      `SingleFolder` already meant exactly this once no fresh subfolder path is passed in. New
      `--extract-flat` CLI switch (`ShellArgumentParser`/`CommandType.ExtractHereFlat`), new
      `RunExtractHereFlatAsync` in `Archiver.Shell/Program.cs`, new `BuildExtractHereFlatArgs`
      in `ShellExtUtils`. `PakkoRootCommand::EnumSubCommands` now lists all three extract variants
      in NanaZip's own order: `Extract…`, `Extract here` (flat), `Extract to current folder
      (Intelligently)`, `Extract to "name\"`
- [x] Translation content for all ~9 strings × 37 locales freshly authored (no existing
      `Archiver.App` resw phrase covered "Test archive"/"Compress…"/"Add to archive…"; only
      partial "Extract" vocabulary existed to seed from)
- [x] Tests: new `LocalizationTests.cpp` (lookup/fallback/data-integrity — every locale's two
      templated strings contain `{0}` exactly, every locale resolves to itself not the en-US
      fallback), new `BuildExtractHereFlatArgs`/`BuildAddToArchiveTitle`/`BuildExtractFolderTitle`
      uk-UA cases in `ShellExtUtilsTests.cpp`, new `ShellArgumentParser` `--extract-flat` cases.
      468 + 85 C++ tests (was 468/68) all pass
- [x] **Real root-cause bug found and fixed along the way:** MSVC silently decoded the non-BOM
      UTF-8 source files (`Localization.cpp`'s literal-glyph translations) using the system ANSI
      codepage instead of UTF-8, corrupting every non-ASCII literal at compile time (confirmed:
      "Д" U+0414 became "Р"+U+201D under cp1251) — invisible to a same-file literal-vs-literal test
      comparison (both sides mis-decode identically) but caught immediately once compared against
      a `\uXXXX`-escaped expected value in a different test file. Fixed at the root with MSVC's
      `/utf-8` compiler flag on both `Archiver.ShellExtension.vcxproj` and
      `Archiver.ShellExtension.Tests.vcxproj`, rather than converting ~370 authored strings to
      escapes. See `DECISIONS.md`'s T-F115 entry — this is very likely the actual mechanism behind
      the three prior mojibake incidents (T-F64/T-F76/T-F63) that were previously only worked
      around, never root-caused
- [ ] `Deploy.ps1` build+sign+install + on-device Explorer check (Ukrainian and, if switchable,
      en-US) confirming the three extract items render correctly localized and the new flat
      command truly extracts without creating a wrapper folder — **not yet done this session**

---

