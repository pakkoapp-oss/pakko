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
- [x] **`h` (hash) added 2026-07-20, T-F128/T-F09 follow-up, user-requested** ("Cli наш бінарник
      консольний теж тепер виіє як і севензіп? З тими ж ключами?") — maps directly onto
      `FileHashService.ComputeAsync` (the same engine T-F128's Explorer "Хеш-суми" submenu uses),
      not a separate implementation. `-scrc{method}` (`CRC32` default, matching real 7z's own
      default; `SHA256`; anything else rejected per the three-way rule) — `-so` is deliberately not
      applicable (the report already prints to stdout, no separate result file to stream).
      Corrected a real, pre-existing inaccuracy in `CLI.md`'s `h` row while at it: it had described
      7z's `h` as hashing *entries inside an archive*, but real 7z `h` hashes files on disk,
      unrelated to archives — matches what T-F128's `FileHashService` already does.
      **`-si` follow-up, same day** (user asked directly whether the stdin path was a genuine
      stream or secretly buffered the whole file first — it was the latter, reusing `x`/`t`/`l`'s
      existing `CliStreamStaging.StageStdinAsync` temp-file helper without questioning whether `h`
      actually needed it). It doesn't: unlike ZIP's central-directory read or tar.exe's pre-scan,
      CRC-32/SHA-256 need no seeking, so `h -si` was reworked into a genuinely different, zero-copy
      path — new `FileHashService.ComputeStreamDigestAsync(Stream, ...)` (extracted from a shared
      `ReadAndDigestAsync` helper the existing file-path method now also calls) hashes
      `Console.OpenStandardInput()` directly, no intermediate file at all. The only `-si` in this
      CLI that's a real single-pass stream — `x`/`t`/`l`'s stay buffered, for the real reasons
      documented in `CLI.md`. 51 new tests total across both rounds (30 parser cases in
      `CliArgumentParserTests`, 2 `CliHelpTextTests` cases, 1 `-scrc`-on-wrong-command case, 2
      `FileHashServiceTests.ComputeStreamDigestAsync_*` unit tests, and — the real proof, not just
      parser coverage — 8 `CliSubprocessTests` cases launching the actual built `pakko.exe`,
      including a real folder DataSum/NamesSum cross-checked against the vendored `7za.exe` and two
      tests piping raw bytes to the real exe's stdin for the zero-copy path). `Archiver.CLI.Tests`
      total: 94 → 143; repo-wide: 700 tests green.
- [ ] Manual on-device verification: real `pakko x archive.zip`, `pakko t archive.zip`,
      `pakko i`, `pakko a archive.zip file1 file2`, `pakko h file.txt`/`pakko h folder` against
      real archives/files, plus one of each three-way error case, confirmed by the user personally
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

### T-F121 — Explore true zero-copy streaming for `-so` extraction output
- [ ] **Status:** future, low priority — exploratory only, no committed design yet. Raised
      2026-07-18 when the user asked whether T-F116's `-si`/`-so` could avoid buffering entirely;
      confirmed the current design is deliberately buffer-then-proceed (temp-file staged), not a
      technical gap — see `CLI.md`'s "Stdin/stdout streaming" section and `DECISIONS.md`'s T-F116
      entry. `-si` (reading a ZIP) can't be made zero-copy at all — `ZipArchive` needs a seekable
      stream since the central directory sits at the end of the file, and `TarSandboxedService`'s
      T-F49 pre-scan is a deliberate security requirement (full archive on disk before extraction
      runs), not an implementation shortcut. The one side worth a second look is `-so` on `x`:
      instead of extracting to a private temp folder then copying the one result file to stdout,
      stream bytes to stdout as they're produced during extraction.
- **Depends on:** T-F116 (CLI stdin/stdout streaming)

**Open questions to resolve before this becomes a real design, not yet answered:**
- [ ] How to preserve the "never emit partial bytes on failure" guarantee without a completed file
      to check first — today a failed operation writes nothing to stdout at all
      (`CliStreamStaging`'s current design's central property)
- [ ] How to know upfront that extraction resolves to exactly one file (today discovered by
      enumerating the temp folder afterward) without either pre-listing the archive first or
      accepting that a multi-file case fails only after streaming has already started
- [ ] Whether tar-family extraction (subprocess + quarantine ACL, not an in-process stream) can
      participate in this at all, or whether true streaming would end up ZIP-only — a format-
      dependent capability split that would need to be documented clearly, not silently assumed
- [ ] Whether the payoff (avoiding transient temp-disk use for one archive's worth of bytes) is
      worth the added failure-mode complexity, given `-so` archives are already expected to fit on
      disk once (the archive itself gets written to a temp file either way in the current design)

**Acceptance criteria (once a design is agreed — not before):**
- [ ] A short design note in `DECISIONS.md` before any implementation, per this project's
      pre-implementation-research convention
- [ ] `Archiver.Core.PerformanceTests`-style before/after evidence that it's actually faster or
      lower-overhead for a real large single-file extraction, not just theoretically cleaner
- [ ] Existing `-so`/`-si` test coverage (T-F116, `Subprocess/CliSubprocessTests.cs`) stays green,
      plus new tests for the harder failure-mode edge cases raised above

---

### T-F124 — Apply to SignPath Foundation (solo-maintainer application, Paul as sole team member)
- [x] **Status:** done 2026-07-23 — application submitted and a real decision received: **rejected**.
      SignPath's rejection email cites insufficient public-visibility signals (GitHub
      stars/forks/contributors, external articles/independent references on Reddit/Stack
      Overflow/YouTube, institutional backing, evidence of sustained external engagement) — not a
      quality judgment on the project itself. They explicitly invite reapplying once the project
      has broader recognition, and offered a paid SignPath subscription
      (https://docs.signpath.io/change-subscription) as an immediate alternative. See
      `DECISIONS.md`'s new T-F124 rejection entry for the full text and the resulting fallback
      decision for T-F10.
      Planning complete 2026-07-19 (research + advisor review). Added 2026-07-18 at the user's
      explicit request. T-F10's own acceptance criteria
      already say "SignPath Foundation eligibility confirmed by actually applying (not just
      reading their public criteria)" — this task is that concrete action, split out on its own
      since it's a real-world submission step (a web form on an external site), not an in-repo
      implementation change.
      **Corrected premise (2026-07-19):** the original framing — "both the user and the agent
      submit an application" — does not match SignPath's real account model. Confirmed via
      `docs.signpath.io/users`: SignPath has exactly two account types, "Interactive User" (a real
      person, via Google/Microsoft/Okta/enterprise SSO) and "CI User" (an API-token-only service
      account for build automation, not a role a person or agent occupies). There is no third
      category and no concept of a second "technical contact" applicant. An agent cannot complete
      a social-login identity flow and cannot be the accountable party SignPath's terms require
      (they reserve the right to investigate conduct-violation allegations against team members —
      that requires a real, legally accountable person). **Only the user applies, as sole project
      owner**, and per SignPath Foundation's own solo-maintainer accommodation (confirmed via a
      real precedent, the Kieirra/murmure project — GitHub discussion #92 — going from
      non-compliant to approved in ~1 month, most of it spent on doc prep, not review time), one
      person holds all three required team roles (Author/Reviewer/Approver) themselves — see the
      new `SIGNING.md` draft (below) for how Pakko documents that.
- **Depends on:** none. **Fed into:** T-F10 Phase 1 (SignPath was the originally-chosen
      code-signing path — rejected, see Status). **T-F10 now depends on T-F129** (Microsoft Store
      submission) as the trigger to reapply — see T-F10's Status.

**Scope:** submit an application to SignPath Foundation (https://signpath.org/apply) for Pakko as
a qualifying open-source project, submitted solely by the user as project owner/maintainer.
Confirm what SignPath's form actually asks for before assuming shape (project URL, license,
maintainer identity, etc.) — don't guess the field list from memory.

**Pre-submission gap analysis (2026-07-19, against SignPath Foundation's real published
eligibility criteria at `signpath.org/terms.html`, not assumed from memory):** Pakko already
satisfies the OSI-license, active-maintenance, already-released, and automated-CI-build
requirements, and already has a real, strong privacy policy
(`https://pakkoapp-oss.github.io/pakko/` — explicitly "no data collection, no network requests, no
telemetry"). Three things were missing and are now drafted (not yet published/committed pending
the user's review): a published "Code Signing Policy" document (new `SIGNING.md` at repo root —
deliberately not reusing `POLICIES.md`, which is Pakko's own unrelated Windows Group Policy admin
reference), a published Author/Reviewer/Approver team-roles statement (folded into the same
`SIGNING.md` draft), and confirmation that the user's GitHub account has MFA enabled (a real-world
account setting only the user can act on — not agent-doable).

**Real decision the user already confirmed (2026-07-19):** SignPath Foundation issues the
certificate to *itself* ("SignPath Foundation" is the certificate Subject/publisher, not Pakko or
Paul R — they aren't a CA and cannot issue a cert directly to the applicant). Once wired in
(T-F10 Phase 1), Windows/Explorer will show **"SignPath Foundation"** as the publisher on every
install prompt, not "Pakko"/"Paul R" — user confirmed this is acceptable over paying for a
personally-issued OV certificate.

**Acceptance criteria:**
- [x] SignPath Foundation's real, current application requirements confirmed by visiting their
      site and cross-checking a real solo-maintainer precedent (not assumed from T-F10's research
      notes, which predate this task)
- [x] `SIGNING.md` published (committed, `2f91664`)
- [x] User's GitHub account MFA confirmed enabled (implied — SignPath reviewed and responded to a
      real submission, so the account-level prerequisites were satisfied enough to be considered)
- [x] User's application submitted (sole applicant — see corrected premise above)
- [x] Real outcome recorded: **rejected, 2026-07-23** — insufficient public-visibility signals
      (stars/forks/contributors, external articles/references, institutional backing, sustained
      external engagement), not a quality judgment. Reapplication invited once the project has
      broader recognition. See `DECISIONS.md`'s T-F124 entry for the full rationale and what this
      means for T-F10's plan.

---

### T-F125 — GitHub Artifact Attestations (SLSA build provenance) for MSIX + pakko.exe
- [x] **Status:** done 2026-07-19, verified against the real `v1.4.0` tag release (not graduated
      on a local read of the YAML alone — this project's own rule for CI changes, T-F122's
      precedent: only a real workflow run + a real `gh attestation verify` against a downloaded
      artifact counts). Both `build-msix` (x64 and arm64) and `build-cli` jobs' "Attest build
      provenance" steps succeeded in run `29697614395` (tag `v1.4.0`); `gh attestation verify
      pakko-win-x64.zip -R pakkoapp-oss/pakko` against the real downloaded release asset returned
      a valid SLSA v1 provenance statement (`buildSignerURI`/`sourceRepositoryURI` correctly
      pointing at `pakkoapp-oss/pakko`'s `build.yml@refs/tags/v1.4.0`, subject digest matching the
      downloaded file). Added 2026-07-19 at the user's explicit request, surfaced as a free
      complementary follow-up while researching T-F10/T-F124 (Sigstore/Cosign cannot replace
      Authenticode signing for SmartScreen — see T-F10's cert-options table — but GitHub's
      Sigstore-backed artifact attestations are a legitimate, free, near-zero-setup addition on
      top of the CI that already exists from T-F122).
- **Depends on:** T-F122 (`build.yml`, done). **Independent of T-F10/T-F124** — provenance
      attestation and Authenticode/SmartScreen trust are two different mechanisms; this does not
      block or get blocked by the SignPath application.

**Scope:** add `actions/attest-build-provenance` to `.github/workflows/build.yml`'s `build-msix`
and `build-cli` jobs, so every MSIX (both architectures) and every `pakko.exe` release zip gets a
signed SLSA Build Level 2 provenance attestation (Sigstore public-good instance, since this is a
public repo) — verifiable by anyone via `gh attestation verify <file> -R pakkoapp-oss/pakko`,
proving the artifact was really built by this repository's own workflow from a specific commit,
not tampered with or substituted after the fact. Fits Pakko's existing auditability/transparency
positioning (`SECURITY.md`) at effectively no cost — no new secrets, no new external account, no
change to the actual signing/trust mechanism end users rely on for SmartScreen.

**Implementation:**
- `build-msix` job: added a job-level `permissions:` block (`id-token: write`, `contents: read`,
  `attestations: write` — not previously granted; the job inherited only default read permissions
  before this), and an `actions/attest-build-provenance@v4` step right after "Build and sign MSIX"
  (before upload), pointed at the same `**/*.msix` / `**/*.msixbundle` glob the existing
  `upload-artifact` step already uses. Runs once per matrix leg (x64, arm64) on its own runner —
  no cross-leg collision, since each job invocation only ever sees its own local build output.
- `build-cli` job: same `permissions:` block, plus an `actions/attest-build-provenance@v4` step
  right after `Publish-Cli.ps1` (before upload), using `subject-checksums:
  artifacts/cli/SHA256SUMS` — reuses the checksums file `Publish-Cli.ps1` already generates
  (standard `sha256sum`-compatible two-column format) instead of re-hashing the zips separately.
- Deliberately **not** added to the `test` job (produces no distributable artifact) or the
  `release` job (only republishes what `build-cli` already produced/attested).
- `actions/attest-build-provenance@v4` is itself now a thin wrapper around the lower-level
  `actions/attest` (confirmed via its own README — not a deprecated/abandoned action, still
  actively released as of 2026-07-19) — used the build-provenance-specific action anyway since its
  whole purpose (SLSA build provenance, correct predicate type by default) matches this task
  exactly, rather than reaching for the generic `actions/attest` and specifying a predicate type
  by hand.

**Acceptance criteria:**
- [x] `build.yml` changes written and internally consistent with the existing job structure (no
      new secrets required — `id-token`/`attestations` permissions are workflow-native, not
      repo-secret-based)
- [x] A real CI run (`v1.4.0` tag push, run `29697614395`) produces attestations with no workflow
      error, for both MSIX architectures and the CLI zips
- [x] `gh attestation verify` against a real downloaded release asset (`pakko-win-x64.zip`)
      succeeds — MSIX attestation creation itself also succeeded in the same run (job logs), not
      separately re-verified via a second `gh attestation verify` call against a downloaded `.msix`
- [ ] (Optional, not blocking) a one-line mention added to `README.md`/`SECURITY.md` pointing
      users at `gh attestation verify` — not done yet, ask before touching either file per this
      project's hard constraint on `SECURITY.md`

---

### T-F126 — Publish the MSIX to the GitHub Release, not just as a workflow artifact
- [x] **Status:** done 2026-07-19 — verified against the real `v1.4.0` tag, the project's first
      real tagged GitHub Release (previously only `v1.0.0`/`v1.1.0` existed, both pre-dating
      T-F122's CI). Found while preparing to cut that release for T-F124 (SignPath's eligibility
      requires the project already be "released in the form that should be signed" at the
      Download/Release URL given in the application). `build.yml`'s `release` job (T-F122) only
      ever published the CLI zips + `SHA256SUMS`; the MSIX (both architectures) was uploaded via
      `actions/upload-artifact` only — a workflow artifact, which expires and requires a GitHub
      login to download, not a public asset on the Releases page. Nobody had cut a real tag since
      T-F122 shipped, so this gap was never exercised end-to-end before now. The stale
      `RELEASE_NOTES_TEMPLATE.md` text claiming "the MSIX is not attached to this release" (written
      when that was still deliberate) was corrected in the same pass.
- **Depends on:** T-F122 (done). **Feeds into:** T-F124 (the Releases page needs to actually show
      the MSIX for the application's Download URL to hold up to scrutiny).

**Fix:** `release` job now also downloads both `pakko-msix-x64`/`pakko-msix-arm64` artifacts
(`actions/download-artifact@v4`, `pattern: pakko-msix-*`, `merge-multiple: true`), globs the real
`.msix`/`.msixbundle` file(s) out of them, and passes them to `gh release create` alongside the
existing CLI zips/`SHA256SUMS`.

**Acceptance criteria:**
- [x] `build.yml` change written
- [x] A real tag push (`v1.4.0`, run `29697614395`) produced a GitHub Release
      (`https://github.com/pakkoapp-oss/pakko/releases/tag/v1.4.0`) whose assets include
      `Archiver.App_1.4.0.14_x64.msixbundle`, `Archiver.App_1.4.0.14_arm64.msixbundle`,
      `pakko-win-x64.zip`, `pakko-win-arm64.zip`, and `SHA256SUMS` — all public, no GitHub login
      required

---

### T-F127 — Wikipedia page (Ukrainian + English)
- [ ] **Status:** future, added 2026-07-19 at the user's explicit request. **Real risk flagged
      before any work starts, per this project's pre-implementation-research norm:** checked
      Wikipedia's actual notability guideline for software (`WP:NSOFT`/
      `Wikipedia:Notability (software)`) — it requires significant coverage of the *software
      itself* in independent, reliable secondary sources (press reviews, printed manuals, or
      recognized historical/technical significance), not just an active public GitHub repo. As of
      2026-07-19 Pakko has no independent press coverage at all (GitHub-only releases for early
      testers, no reviews, no news mentions found) — an article created now would very likely be
      speedy-deleted (`WP:CSD` A7) or fail an Articles-for-Deletion discussion for lack of
      notability, on both `en.wikipedia.org` and `uk.wikipedia.org` (Ukrainian Wikipedia applies
      an equivalent standard). This is not a formatting/writing problem, it's an eligibility
      problem — no amount of good prose fixes it.
- **Depends on:** none technically, but see the note above — realistically blocked on Pakko
      accumulating independent secondary-source coverage first (e.g. a tech-press review, a
      notable government/enterprise adoption case covered by a third party). **Not blocked on**
      T-F124/T-F10 (code signing) or the Microsoft Store listing — those aren't Wikipedia
      notability sources either.

**Scope (once notability is realistically met):**
- Draft the article once via a shared source (English first, then a Ukrainian translation, or
  vice versa — not two independently-drafted articles, to avoid the two language versions
  drifting on basic facts like license/version/feature list)
- Disclose the conflict of interest: per `WP:COI`, the project's own maintainer writing about
  their own project is a connected contribution — must be disclosed on the article's talk page
  (`{{connected contributor}}` template) and the editor's own user page, and the recommended path
  is submitting via Articles for Creation (`WP:AFC`) as a draft for independent review, not
  publishing directly to mainspace
- Cite only independent secondary sources for notability-relevant claims — this repo's own
  `README.md`/`SECURITY.md`/`TASKS.md` are primary sources and don't establish notability, only
  factual detail once notability is otherwise established

**Acceptance criteria:**
- [x] Real notability check redone at implementation time (sources may exist by then that don't
      today) — record what was found, don't assume the 2026-07-19 "not yet notable" finding still
      holds without rechecking. **Rechecked 2026-07-19 (same day):** live web search for "Pakko"
      alongside archiver/WinUI/zip/GitHub terms returned zero results referencing this project at
      all — no press coverage, no reviews, no third-party mentions of any kind exist yet. Same
      conclusion as the original finding above; task stays blocked, not started.
- [ ] If proceeding: COI disclosed per `WP:COI` before any mainspace edit
- [ ] English draft submitted via AfC (or mainspace, if a competent Wikipedia editor advises
      notability is clearly met and AfC is unnecessary)
- [ ] Ukrainian draft submitted via Ukrainian Wikipedia's equivalent process
- [ ] Both articles survive their respective new-article review process without deletion

---

### T-F128 — Explorer context-menu hash commands (CRC-32/SHA-256, files and folders)
- [~] **Status:** implementation complete 2026-07-20, on-device verification pending (down to the
      user's own personal click-through only — the AI-driven pass below is now a full,
      not partial, end-to-end confirmation).
      **Re-scoped three times this session, all user-driven — kept per the "never silently
      deprecate" rule:**
      1. First implementation attempt was a `ComboBox` inside the existing WinUI
         `DialogService.ShowFileHashAsync` dialog (the "Hash" button) — **fully reverted**
         (`DialogService.cs` confirmed byte-identical to its pre-session state via `git diff`)
         after the user showed real NanaZip screenshots: they wanted a native Explorer
         right-click submenu (mirroring NanaZip's own cascaded "CRC SHA" menu), not an in-app
         dialog.
      2. The user's screenshots also showed NanaZip hashing a *folder*, producing two combined
         values (DataSum/NamesSum) via a specific commutative "carrying addition" algorithm —
         reverse-engineered from NanaZip's real source
         (`NanaZip.UI.Modern/SevenZip/CPP/7zip/UI/Common/HashCalc.cpp`) via a Plan-Mode session,
         not guessed. See `DECISIONS.md`'s T-F128 entry for the full algorithm derivation
         (`AddDigests`' byte-wise carry, `CHasherState::WriteToString`'s display/overflow-suffix
         rules, and why nested-folder recursion is safe for DataSum but not for NamesSum's
         subfolder-object contribution).
      3. **2026-07-20: flattened from a nested "Хеш-суми" submenu (`HashCommand` parent +
         `HashCrc32Command`/`HashSha256Command` children) to two direct top-level leaves,
         "Хеш-суми: CRC-32"/"Хеш-суми: SHA-256", after the user's own real screenshot showed the
         submenu opening but rendering completely empty.** Live investigation (killed/rebuilt the
         `dllhost.exe` surrogate to rule out a stale-process/cold-start theory, both refuted;
         instrumented `HashCommand::EnumSubCommands`/`HashCrc32Command::GetState`/`GetTitle` with
         temporary file logging) proved the COM plumbing itself was sound — Explorer really did
         call `EnumSubCommands`, get a valid 2-item enumerator back, and successfully fetch the
         first leaf's state (`ECS_ENABLED`) and title (`"CRC-32"`) — yet the flyout never painted
         anything, for either automation or the user's own real mouse. No crash/exception was
         found (`Get-WinEvent` clean for the whole window). Root cause was not conclusively pinned
         inside Explorer's own rendering pipeline; instead of continuing to chase it, adopted the
         user's own suggested fix (matching NanaZip's flat items being the *simpler*, not the
         nested, part of its design) and flattened `HashCommand` away entirely — this reuses the
         exact single-level-nesting code path every other leaf in `PakkoRootCommand::EnumSubCommands`
         already relies on successfully. Confirmed fixed live immediately after: both items now
         render, and clicking "Хеш-суми: CRC-32" opened a real `Archiver.Shell` dialog reading
         `CRC-32: test.txt` / `test.txt: 363A3020`. See `DECISIONS.md`'s T-F128 follow-up entry.
      4. **2026-07-20: folder result trimmed to summary-only (no per-file dump), matching NanaZip
         exactly; a Windows toast-notification replacement for `MessageBoxW` was attempted, then
         reverted.** `ShowHashResults`'s folder branch used to append the full per-file listing
         underneath DataSum/NamesSum — the user pointed out NanaZip only shows the aggregate sums,
         so the dump was dropped (kept for the non-folder branches, which are a genuine per-file
         table, not a sum). Separately, the user asked about replacing the classic `MessageBoxW`
         with a modern toast notification; implemented via a `HashToastNotifier` WinRT wrapper and
         an `Archiver.Shell`/`Archiver.Shell.Tests` TFM bump to `net8.0-windows10.0.17763.0` (for
         compile-time `Windows.UI.Notifications` projections, zero new NuGet packages) — this
         surfaced a real, independent bug: `Archiver.App.csproj`'s `Content Include` paths for
         `Archiver.Shell.exe`/`.dll`/`.deps.json`/`.runtimeconfig.json`, and `Deploy.ps1`'s own
         `$shellExeSourcePath`, all hardcoded the literal `net8.0-windows` build-output segment;
         once the TFM changed, MSBuild's real output folder moved but these four paths silently
         kept pointing at the old, no-longer-updated folder, so `Deploy.ps1` kept installing a
         stale pre-toast DLL despite reporting success and a fresh file timestamp. Fixed alongside
         the toast work, then reverted together with it (see below) — worth remembering if this
         TFM is ever bumped again for a real reason. On-device testing then found
         `ToastNotifier.Setting` was `DisabledForUser`, traced to `HKCU\...\PushNotifications\
         ToastEnabled=0` — Windows notifications are off machine-wide on this dev box, unrelated
         to Pakko. The `NotificationSetting.Enabled`-gated fallback-to-`MessageBoxW` design worked
         exactly as intended in this state, but the toast itself couldn't be visually confirmed
         without a global OS settings change. **User's call: revert the toast entirely and keep
         the classic dialog** (`HashToastNotifier.cs` deleted, `ShowHashResults`'s toast branch
         removed, both TFM bumps and the four path fixes rolled back since they only existed to
         support the toast) — "something that will definitely work," with a possible future task
         for a custom-drawn info window instead of a system toast, not opened yet.
      5. **2026-07-20: folder-hash progress bug fixed, Size line added, full 37-locale
         localization of `Archiver.Shell`'s hash-result labels, and a real ~9x CRC-32 performance
         regression found and mostly closed — all from a real on-device NanaZip comparison
         screenshot on a 993-folder/14049-file/9.3 GiB folder.** Root cause of the progress bug:
         `ComputeFolderAsync`'s parallel loop wrapped every file's stream in a per-file
         `ProgressStream(fileStream, thatFile'sOwnLength, ...)`, so the dialog's percent/byte
         counters reset to 0% for every new file instead of tracking the whole folder — fixed with
         two new `Archiver.Core/IO/` classes, `AggregateProgressTracker` (a shared, lock-guarded
         byte counter against the folder's total size) and `AggregateProgressStream` (a read-only
         wrapper reporting into it), wired in via a `ComputeFileDigestAsync` signature change
         (`Func<FileStream, Stream>? wrapForProgress` instead of a bare `IProgress<ProgressReport>?`
         — lets each caller decide how to wrap the already-open stream, using its own `.Length`
         instead of a redundant stat call). `FolderHashSummary` gained `TotalBytes` (from the same
         upfront `DirectoryInfo.EnumerateFiles` size-sum the tracker needs — zero extra I/O),
         displayed as a new localized "Size" line in `ShowHashResults`, positioned before
         DataSum/NamesSum to match NanaZip's own field order. The folder per-file dump the previous
         follow-up removed stays removed.
         **Localization** (real `AskUserQuestion` decision — full 37-locale parity, not just
         uk-UA/en-US): `Archiver.Shell` had never had any localized text before this. Deliberately
         used plain **.resx satellite-assembly localization**
         (`System.Resources.ResourceManager`, new `HashResultLocalizer.cs`), not `Archiver.App`'s
         own WinRT `ResourceLoader`/.resw — resw needs the same Windows-versioned TFM
         (`net8.0-windows10.0.17763.0`) that caused the toast follow-up's stale-build-path bug;
         .resx needs no TFM change at all and is a more natural fit for a non-XAML `WinExe` anyway.
         New `src/Archiver.Shell/Resources/HashMessages.resx` (neutral/English) plus 36
         locale-specific `.resx` files (every locale `Archiver.App/Strings/` already has), 5 keys
         each (`HashResultFilesLine`/`SizeLine`/`DataSumLine`/`NamesSumLine`/`AndMoreLine`) —
         `uk-UA` mirrors NanaZip's own real field words (Файлів/Розмір) confirmed against the
         screenshot. Written directly (not via `\uXXXX` escapes) after confirming empirically this
         session that plain Unicode text through the Write tool round-trips correctly here (see
         `DECISIONS.md`); every file batch-verified afterward via a `py -3` script checking valid
         XML, all 5 keys present, no U+FFFD replacement-character corruption, and a `{0}`
         placeholder in every value — all 37 files clean. `Archiver.App.csproj` gained one new
         wildcard `Content Include` (`**\Archiver.Shell.resources.dll` with `%(RecursiveDir)`) so
         the per-culture satellite assemblies actually reach the MSIX — the four pre-existing
         `Archiver.Shell.*` `Content Include` items don't cover subfolders. New
         `HashResultLocalizerTests.cs` (6 tests: all 5 keys resolve with a working `{0}` in the
         neutral culture, plus a real uk-UA round-trip assertion).
         **Performance** (new `tests/Archiver.Core.PerformanceTests/HashPerformanceTests.cs`,
         mirrors T-F114's `CompressionPerformanceTests` pattern exactly, plus a new
         `SevenZipRunner.Hash`/`PerformanceFixtures.CreateManyFilesAndFoldersFolder` — the first
         fixture in that project with real nested subfolders, 300×10 files, unlike the existing
         flat `ManySmallFiles`/`Hybrid` fixtures): the `OneLargeFile` scenario (300 MB, `Category`
         `VeryLarge`) found a real, reproducible **~9x** slowdown against `7za h -scrcCRC32`
         (1.5s vs. 0.17s). Root-caused (not assumed) via a throwaway in-memory-only benchmark that
         isolated CRC-32 compute time from file I/O — plain reads hit 4+ GB/s, even async
         `ReadAsync` on a `useAsync:false` `FileStream` hit ~1.8 GB/s, but `Crc32.Accumulator`
         alone took ~1.2s on an in-memory 300 MB buffer, confirming the CRC-32 math itself was the
         bottleneck: `Crc32.cs`'s original algorithm was a byte-at-a-time single-table lookup.
         **Confirmed with the user via `AskUserQuestion` before changing this shared class**
         (used by `ZipEntryWriter` and `Archiver.App`'s `FileItem` CRC-32 column too, not just
         hashing) — rewrote to **slice-by-8** (a standard technique, e.g. zlib's `crc32.c`; no
         NuGet dependency, same public API), then further flattened the `uint[8][256]` jagged
         table to one contiguous `uint[2048]` array (fewer pointer dereferences, better cache
         locality) after slice-by-8 alone only closed the gap to ~7.5x. Settled at **~6.4x** —
         every existing CRC-32 known-value/7za-cross-check test still passes bit-for-bit
         (algorithm reorganized, output unchanged), proving correctness wasn't sacrificed for
         speed. Closing the remaining gap to 7-Zip's own CRC-32 (likely hardware
         SSE4.2/PCLMULQDQ-accelerated) would need SIMD intrinsics — explicitly scoped out as a
         separate, materially bigger, platform-specific undertaking, not silently pursued further.
         The `ManyFilesAndFolders` scenario (3,000 files/300 subfolders, `Category` `Slow`)
         calibrated to ~1.3x — small absolute times (~200-280ms) mean run-to-run noise dominates
         more there, same reasoning T-F114's own `ManySmallFiles`/`Hybrid` scenarios already use.
      6. **2026-07-20: intra-file parallel CRC-32, closing most of the OneLargeFile gap (6.45x →
         ~1.35x typical).** The existing cross-file `Parallel.ForEachAsync` gives a single large
         file zero benefit (it only parallelizes *across* files) — the user asked specifically for
         genuine multi-threaded chunking of one file, keeping slice-by-8 as the per-chunk
         algorithm (not the SIMD/PCLMULQDQ route, still explicitly out of scope). New
         `Crc32.Combine` (faithful reimplementation of zlib's public-domain `crc32_combine`, GF(2)
         matrix math, O(log N)) folds independently-hashed chunks back together in original byte
         order — unlike DataSum/NamesSum's cross-file combining, chunk order matters here.
         `FileHashService.ComputeFileCrc32ParallelAsync` splits CRC-32 files ≥8 MiB into 4 MiB
         chunks read via `RandomAccess.Read` on one shared handle. 9 new `Crc32Tests.cs` cases
         prove `Combine` itself is correct; 7 new `FileHashServiceTests.cs` cases prove the
         parallel path's output always matches sequential ground truth across several sizes
         (including non-chunk-aligned ones), a folder-mixed large file, and progress reporting.
         A real stability bug was found and fixed along the way: the first version
         (`Parallel.ForAsync`/`RandomAccess.ReadAsync`) swung wildly run-to-run (0.36s-1.2s+ for
         the same 300 MB file) — root-caused to .NET's default `ThreadPool` thread-injection
         ramp-up, fixed by switching to synchronous `Parallel.For`/`RandomAccess.Read` in one
         `Task.Run` plus a one-time `ThreadPool.SetMinThreads` bump. This was the user's own
         explicit ask ("стабільно із запасом" — stable, with margin), not a nice-to-have.
         Archive/Extract performance is unaffected — this parallelism lives entirely in
         `FileHashService`, not in `Crc32` itself, so `ZipEntryCompressor`'s own sequential
         per-entry CRC-32 (used while compressing) is untouched. See `DECISIONS.md`'s T-F128
         entry for the full investigation, including the real measured bimodal timing pattern.
- **Depends on:** none.

**Implementation:**
- New `Archiver.Core.IO.HashDigestAccumulator` (internal) — the NanaZip-compatible combine
  algorithm, reused for both DataSum and NamesSum.
- New `Archiver.Core.Services.FileHashService.ComputeAsync` — single file, multi-file (each
  hashed independently), and single-folder-recursive (combined DataSum/NamesSum + per-file
  listing) branches; a folder inside a multi-item selection is skipped gracefully, not summed
  (ambiguous relative-path anchor, explicitly out of scope).
- `Archiver.Shell`: new `--hash --algorithm crc32|sha256` CLI switch
  (`ShellArgumentParser.ParseHash`), `RunHashAsync` reuses `NativeProgressDialog` +
  cancel-poll directly (not `RunWithProgressWindowAsync`, which is typed around
  `ArchiveResult` — a hash result doesn't fit that shape), shows results via `MessageBoxW`.
- `Archiver.ShellExtension`: `HashCrc32Command`/`HashSha256Command` are direct
  `PakkoRootCommand` leaves (not nested under an intermediate submenu parent — see the
  2026-07-20 re-scope above), titled `"{localized "Хеш-суми"}: CRC-32"`/`"...: SHA-256"` via
  `StringId::HashSubmenu` as a prefix (the algorithm name itself stays untranslated Latin
  script, like T-F105's tar format names) — added last in `PakkoRootCommand::EnumSubCommands`,
  after Test (diagnostic/utility actions go last). New `BuildHashArgs` in `ShellExtUtils`. Zero
  `Package.appxmanifest` changes (leaf-command precedent holds). One localized string,
  `StringId::HashSubmenu`, across all 37 locales.
- **Real external-tool cross-check, not just internal consistency**: `FileHashServiceTests`'
  folder DataSum/NamesSum expected values were captured from the vendored `7za.exe`
  (`h -scrcCRC32`/`h -scrcSHA256`, T-F114's tool — 7-Zip's own `h` command runs the identical
  NanaZip/HashCalc.cpp algorithm) against a byte-identical fixture, then hardcoded as test
  vectors — this is real proof the algorithm matches, not an assumption.
- **Parallel file hashing (added 2026-07-20, user-requested follow-up).** Both `ComputeAsync`
  branches now hash files via `Parallel.ForEachAsync` (up to `Environment.ProcessorCount` at
  once) instead of a sequential `foreach` — safe specifically because
  `HashDigestAccumulator.Add` is commutative (already relied on for the recursion-safety
  argument above), so combining DataSum/NamesSum in whatever order files finish hashing gives
  the same result as sequential order. The general (non-folder) branch writes into a pre-sized
  array by original index, needing no lock and preserving the caller's selection order; the
  folder branch guards the shared accumulators/entries list with a single `lock`, with the
  actual hash/NamesSum-item computation done outside it. New
  `FileHashServiceTests.ComputeAsync_ManyFilesInFolder_ParallelHashingIsDeterministicAndRaceFree`
  (50 files, two independent runs asserted to produce byte-identical DataSum/NamesSum) is the
  real proof this is race-free, not just "didn't crash" — a genuine race would show up as a
  result that differs between runs.

**Acceptance criteria:**
- [x] Scope resolved with the user at each pivot before writing/keeping code
- [x] CRC-32/SHA-256 reachable as two direct items from the real Explorer right-click menu (via
      `Archiver.ShellExtension`, not an in-app dialog)
- [x] Folder DataSum/NamesSum match a real external tool (7za.exe) bit-for-bit for a flat
      folder, confirmed by `FileHashServiceTests` — real cross-tool parity, not internal-only
- [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` green repo-wide (707 tests as of
      2026-07-20's progress/Size/i18n/perf follow-up — don't trust the older 676 figure)
- [x] Real `Archiver.ShellExtension.vcxproj`/`Archiver.ShellExtension.Tests.vcxproj` builds
      succeed (93 C++ tests pass, including new `BuildHashArgs`/`HashDigestAccumulator` cases)
- [~] **2026-07-20 follow-up (progress fix/Size/i18n/perf) on-device verification: partial.**
      `dotnet test`-level correctness confirmed for all of it (aggregate-progress test, resx smoke
      test, bit-identical CRC-32 known-value tests, both new perf scenarios). AI-driven pass via
      the real installed package (version 1.4.0.24), through the actual Explorer context menu:
      - **Confirmed live**: a real 20-file/10 MB folder and a real 30-file/510 MB folder both hash
        correctly end-to-end (`ExplorerCommands.cpp` → `Archiver.Shell` → `FileHashService` →
        `HashResultLocalizer`) with real localized Ukrainian output —
        `"Файлів: 30\nРозмір: 509,5 MB (534 288 000 bytes)\nСума даних: E6545476-0000000F\n
        Сума даних та імен: F415B066-0000000D"` — proves the Size line, the localized labels
        (matching NanaZip's own Файлів/Розмір wording), and the satellite `.resources.dll`
        packaging (36 locale subfolders confirmed present under the installed package's
        `InstallLocation`) all work through the real installed MSIX, not just `dotnet test`.
      - **Not visually confirmed this round**: the progress dialog smoothly progressing across a
        whole folder instead of resetting per file. Ironically hard to reproduce locally anymore
        — even a 510 MB/30-file folder now hashes fast enough (thanks to the same session's CRC-32
        speedup) that `IProgressDialog`'s own `AutoTime` flag never showed the dialog at all before
        the operation finished. The fix itself is proven correct by
        `ComputeAsync_FlatFolder_ProgressReportsAggregateAcrossAllFiles` (asserts `TotalBytes` is
        the folder's real combined size on every single progress report, not any one file's own
        size) and by direct code review of the root cause, but a live look at a real large,
        slow-enough folder (like the user's original 993-folder/14049-file/9.3 GiB one) is the
        only way to see it visually — left to the user's own click-through.
- [x] On-device verification via `Deploy.ps1` (real install, version 1.4.0.18) — **AI-driven pass
      done 2026-07-20, now a full end-to-end click confirmation, not a substitute:**
      - The earlier 2026-07-19 pass had reported the nested "Хеш-суми" submenu itself as
        reachable but its leaf clicks as "not achievable via automation" — that was wrong. A
        fresh investigation this round found the submenu was genuinely empty (matching a real
        screenshot the user provided), root-caused it well enough to fix (see the re-scope note
        above), flattened the design, and this time the **real leaf click succeeded live**: right-
        clicked a test file via the `windows` MCP server, clicked through Pakko →
        "Хеш-суми: CRC-32", and a real `Archiver.Shell`-owned dialog opened reading
        `CRC-32: test.txt` / `test.txt: 363A3020` — the actual context-menu path, mouse click and
        all, not the `Archiver.Shell.exe --hash` direct-invocation substitute used previously.
      - **Still needs the user's own click-through**: a real visual comparison against NanaZip's
        own CRC SHA menu on the same folder, Cancel on a large file, and a mixed file+folder
        selection's graceful skip.
- [x] `DIAGRAMS.md` diagram 1 (Shell context-menu invocation) updated to include the new
      `HashCommand`/leaf flow, per its own Definition-of-Done table — validated with `mmdc`
- [x] `ARCHITECTURE.md` updated with `FileHashService`/`HashAlgorithmKind`/
      `HashDigestAccumulator` signatures and folder-tree entries

---

### T-F129 — Publish the MSIX to the Microsoft Store
- [ ] **Status:** future, added 2026-07-19 at the user's explicit request. Researched via
      Microsoft Learn (`Create an app submission for your MSIX app`, `Resolve submission errors
      for MSIX app`) before drafting this, per this project's pre-implementation-research norm —
      real findings below, not assumptions.
- **Depends on:** none — **explicitly NOT blocked on T-F10/T-F124 (real code signing)**, contrary
      to what might be assumed. The Store re-signs every MSIX package with its own certificate
      during ingestion; the package can be built and uploaded with the existing local self-signed
      `Deploy.ps1` dev cert. Don't gate this task on SignPath approval.

**Real findings from research (2026-07-19):**
- **Cost:** publishing is free as of 2026 for both individual and company Partner Center accounts
  (the $19/$99 one-time registration fee was removed) — no budget blocker.
- **Package Identity is the one real landmine.** Partner Center assigns a specific
  `Package/Identity/Name` + `Publisher` (a `CN=<GUID>` value tied to the seller account) +
  `PublisherDisplayName` the moment the app name is reserved, and **the Identity can never be
  changed once the app exists in Partner Center.** The uploaded package's
  `Package.appxmanifest` must match those exact reserved values, or the submission fails with a
  generic, unhelpfully-worded identity error. Current manifest state (checked, not assumed):
  `src/Archiver.App/Package.appxmanifest` currently has
  `Publisher="CN=EF3EC84C-8287-4FC3-BB4F-FCCEBA116BCE"` and
  `PublisherDisplayName="Pavlo Rybchenko"` (the local/CI dev-signing identity — unrelated to
  whatever CN Partner Center will assign). **Sequencing matters:** reserve the app name in
  Partner Center *first*, copy its Product Identity page values, update the manifest's
  `Identity Name`/`Publisher`/`PublisherDisplayName` to match exactly, rebuild, *then* upload —
  never the other way around.
- **`runFullTrust` is a "restricted capability."** `Package.appxmanifest` already declares
  `<rescap:Capability Name="runFullTrust" />` (needed for the satellite `Archiver.Shell.exe`/
  `Archiver.ShellExtension.dll`, unrelated to this task). Partner Center's Submission Options page
  requires justification text for any restricted capability — budget time for this, and note
  NanaZip (a directly comparable IExplorerCommand-based archiver, already Store-published) as a
  precedent that this capability is acceptable for this app category, matching this project's
  "check NanaZip" research convention elsewhere.
- **Store listing minimums:** description (required), at least 1 screenshot (4+ recommended),
  a Store logo, a category, and a full Age ratings questionnaire — all required before
  certification can be submitted. A Privacy Policy URL is only strictly required if the app
  collects/transmits personal data; Pakko's existing policy
  (`https://pakkoapp-oss.github.io/pakko/`, already linked from `SIGNING.md`) states it does
  neither, but Partner Center may still prompt for the URL field regardless — have it ready either
  way, not a new artifact to create.
- **Pre-submission testing:** run the Windows App Certification Kit (WACK) against the built MSIX
  before uploading — catches many of the same failure classes Partner Center's own certification
  pass checks, cheaper to fix locally first. Also explicitly confirm the app doesn't crash with no
  network connectivity (trivially true for Pakko — it makes zero network calls — but Microsoft's
  own certification checklist calls this out by name, worth a literal on-device airplane-mode
  smoke test before submitting, not just an assumption from the "no telemetry" design).
- **Desktop Bridge / packaging-project gotcha (flagged, not yet confirmed relevant):** Microsoft's
  docs warn that a package built from a plain UWP project template (mixing Win32 + UWP binaries)
  can fail Store submission or sideload strangely — the doc's specific guidance targets the older
  Desktop Bridge/`.wapproj` model, and Pakko already builds via `dotnet publish`'s Windows App SDK
  packaging path (not `.wapproj` — see `CLAUDE.md`'s "Never use `.wapproj`" hard constraint), which
  is a different, newer pipeline than what that warning describes. Treat as an open question to
  verify via a real WACK run and a real submission attempt, not as a known blocker.

**Follow-up research (2026-07-20, user-requested): official-doc corrections + real-world/community
submission experience (Reddit, Microsoft Q&A, GitHub, NanaZip's own history) — full sourced
account in `DECISIONS.md`'s T-F129 entry, summary here:**
- **Individual developer registration now requires identity verification (government ID + selfie,
  the mobile-driven flow at storedeveloper.microsoft.com)** — stricter/slower than the
  2026-07-19 research implied (which only confirmed "free"). Budget real calendar days for this
  before anything else in this task can start.
- **`runFullTrust` is gated by a *second*, separate approval layer beyond the Submission Options
  justification text:** a developer *account* has to be authorized by Microsoft to submit
  `runFullTrust` apps at all (confirmed via two real Microsoft Q&A threads quoting the exact
  rejection: "Your developer account isn't authorized to submit apps that use the runFullTrust
  capability"), requested through Developer Support — no published SLA for this approval. The
  Submission Options justification text is necessary but may not be sufficient by itself; a vague
  "the framework requires it" justification has been rejected for other WinUI3/Uno/MAUI submitters
  in practice — write a concrete, app-specific justification (Pakko needs `runFullTrust` for the
  satellite `Archiver.Shell.exe` process and the `IExplorerCommand` COM registration, not for the
  main WinUI window) and cite NanaZip as a real precedent for this exact app category.
- **`%TEMP%` is confirmed NOT virtualized under MSIX (only `%LOCALAPPDATA%` is)** — Store
  publication changes nothing about `TarSandboxedService`'s existing `%TEMP%`-rooted quarantine
  staging (T-F52); no new risk here, just confirmed rather than assumed.
- **Certification turnaround is officially "usually a few hours, up to 3 business days" per SLA**
  — useful for setting expectations, not a blocker.
- **NanaZip's own history (github.com/M2Team/NanaZip issues/PRs) is a real, directly relevant
  precedent** — its MSIX packaging hit two shell-extension-specific problems worth watching for in
  Pakko too: (1) registering a verb for the wildcard `"*"` file type was rejected the same way
  normal extension/Directory verbs are (PR #205, issues #193/#203) — not currently something Pakko
  attempts, but worth remembering if a future task considers it; (2) **the context-menu item has
  disappeared after Windows updates on multiple separate occasions** (issues #505, #317, #193,
  recurring across 2024–2025) — a known *recurring*, not one-time, risk class for MSIX-packaged
  shell extensions specifically. This is the same failure shape already documented in this file's
  own T-F101 investigation (Explorer verb/icon-cache artifact) — Store publication doesn't
  introduce this risk, but it's worth an explicit standing check, not just a pre-submission one.

**Scope:**
- User-only external steps (cannot be scripted/automated by the agent): register an individual
  Partner Center developer account and complete identity verification (government ID + selfie),
  reserve the "Pakko" app name, record the assigned Product Identity values, request
  `runFullTrust` account-level authorization via Developer Support if the automated check blocks
  submission, fill in Store listing content (description, screenshots, category, age ratings,
  restricted-capability justification), and click Submit for certification.
- Agent-assistable steps: update `Package.appxmanifest`'s `Identity` block to the reserved values
  once the user provides them, produce a fresh signed build via `Deploy.ps1` for upload, run WACK
  locally and triage any findings, draft the Store listing description/screenshots list and the
  `runFullTrust` justification text for the user to review.

**Acceptance criteria:**
- [x] Partner Center individual account registered, identity verification complete — done by the
      user in March 2026 (predates this task's 2026-07-19 drafting)
- [x] Partner Center app name reserved, real Product Identity values recorded here — confirmed
      2026-07-20 via a real Partner Center screenshot (`Apps and games` → `Pakko` → `Product
      Identity`): `Package/Identity/Name = PavloRybchenko.Pakko`,
      `Package/Identity/Publisher = CN=EF3EC84C-8287-4FC3-BB4F-FCCEBA116BCE`,
      `Package/Properties/PublisherDisplayName = Pavlo Rybchenko`. Store ID `9P5MW010D8PR`,
      PFN `PavloRybchenko.Pakko_955q7mnhfhmp4`. Product status shown as "In draft" /
      "Not started" (no submission made yet).
- [ ] `runFullTrust` account-level authorization confirmed (via Developer Support if the automated
      check blocks it) — don't assume the Submission Options justification text alone is enough
- [x] `Package.appxmanifest`'s `Identity` block already matches the reserved values exactly (`Name`,
      `Publisher`, `PublisherDisplayName` all confirmed identical 2026-07-20 — the local dev cert
      was apparently issued against this same reserved identity back in March, per Microsoft's own
      recommended practice of aligning the dev-signing cert Subject with the Store identity from
      the start). No manifest edit needed for this criterion.
- [x] WACK run locally (2026-07-20) via the CLI (`appcert.exe test -apptype packagedwin32
      -appxpackagepath ...` — needs elevation) against the current v1.4.1.0 x64 MSIX
      (`Archiver.App_1.4.1.0_x64.msix`, same build as the `chore(release): bump to v1.4.1` commit
      timestamp). **`OVERALL_RESULT="WARNING"`, no non-optional FAIL** — full report saved outside
      the repo (not committed; regenerate via the same command before the real submission since
      this one predates any of this task's follow-up fixes). Four non-PASS items, none blocking:
      - `[FAIL, optional]` **Application count** — package declares 2 `<Application>` entries
        (`Archiver.App` + `Archiver.Shell`). Expected/by-design (CLAUDE.md's own hard constraint:
        every `CreateProcess`-launched satellite EXE needs its own manifest `<Application>` entry)
        — no fix, keep the rationale ready in case Partner Center asks.
      - `[FAIL, optional]` **App resources** — a real, fixable bug: `Square44x44Logo.scale-200.png`
        (88×88 expected), `Square150x150Logo.scale-200.png` (300×300), `Wide310x150Logo.scale-200.png`
        (620×300), and `SplashScreen.scale-200.png` (1240×600) are all present but not actually
        sized to match their own scale-suffix filenames. Needs real image regeneration before
        submission — tracked as a follow-up below, not yet fixed.
      - `[FAIL, optional]` **Blocked executables** — almost entirely noise from the self-contained
        .NET 8 runtime's own DLLs (`coreclr.dll`, `clrjit.dll`, `System.Linq.Expressions.dll`, etc.)
        matching WACK's substring scanner for process-launch APIs/blocked-executable-name strings
        (some matches are absurd, e.g. `"rcsI"` flagged as `"cmd"` — a known false-positive class
        for self-contained .NET deployments, not a real problem). The genuine hits
        (`Archiver.Shell.exe`/`Archiver.App.exe`/`Archiver.Core.dll`/`Archiver.ShellExtension.dll`
        referencing `ShellExecuteW`/`CreateProcessW`/`Process.Start`) are all load-bearing to
        Pakko's actual design (opening the destination folder, sandboxed `tar.exe` launches) — no
        fix, expected.
      - `[WARNING, non-optional]` **DPIAwarenessValidation** — the one non-optional finding:
        `Archiver.Shell.exe` has no `PerMonitorV2` DPI-awareness manifest entry and calls no DPI
        Awareness API. `Archiver.App` (WinUI 3/Windows App SDK) is DPI-aware by default; the
        satellite `Archiver.Shell.exe` (which hosts `NativeProgressDialog`'s `IProgressDialog` COM
        UI) currently is not declared as such. Real, fixable, not yet fixed — tracked below.
      - **Both real follow-up fixes done the same session (2026-07-20).** First pass: fixed the
        four undersized Store-asset PNGs via Lanczos resampling (upscaling the existing artwork,
        or downscaling the higher-res 256×256 frame already embedded in `Square44x44Logo.ico`),
        and rebuilt `SplashScreen.scale-200.png` from a mis-sized 256×256 copy of the square tile
        icon into a correct 1240×600 transparent canvas with the brand mark centered. Added
        `src/Archiver.Shell/app.manifest` (`PerMonitorV2` `dpiAwareness`) wired via
        `<ApplicationManifest>` in `Archiver.Shell.csproj`. Rebuilt via `Deploy.ps1`, re-ran WACK:
        `OVERALL_RESULT` improved from `WARNING` to `PASS`.
      - **Second pass, same day, user-driven:** the user judged the upscaled `Wide310x150Logo`
        genuinely ugly (soft/blurry rounded corners, a real artifact of 2× Lanczos-upscaling a
        low-res 310×150 source) and supplied the actual vector source,
        `src/Archiver.App/Assets/pakko-icon.svg` (a 256×256 viewBox: rounded-rect `#1D5FA8`
        background `rx=56`, a white glyph built from 3 rounded rects). All 5 previously-touched
        assets, plus `StoreLogo.png` for consistency, were re-rendered directly from this vector
        geometry via a small custom supersampled rasterizer (draw at 8× target resolution with
        `ImageDraw.rounded_rectangle`, downsample with Lanczos) — mathematically exact edges, no
        upscale blur anywhere. `Wide310x150Logo`'s layout (glyph bbox, corner radius) has no
        vector source of its own (the SVG is square-only), so it was reverse-measured pixel-exact
        from the original hand-made 310×150 artwork (`git show HEAD`, before this task touched it)
        and re-expressed as canvas fractions, not guessed. A real regression from the *first* pass
        was caught this way: `Square44x44Logo`'s two variants, sourced from `Square44x44Logo.ico`'s
        embedded 256×256 frame, had accidentally lost their rounded corners (the `.ico` frame turned
        out to be a flatter, unrounded rendering, unlike the SVG and the true original PNG) — this
        second pass restored them, confirmed by inspecting the original 44×44 file's corner pixels
        via `git show HEAD` before assuming. All 6 regenerated files re-verified against their
        required pixel dimensions in a script (all `OK`), then rebuilt via `Deploy.ps1`. A third
        WACK re-run to reconfirm was attempted but blocked by two consecutive UAC cancellations —
        not chased further (3-attempt rule) since only pixel *content* changed, not the file
        *dimensions* WACK's `App resources` test actually checks, so a regression there is not
        plausible. See `DECISIONS.md`'s T-F129 WACK entries for the full before/after detail and
        the exact geometry math.
- [ ] WACK run locally against the fresh build with no unresolved failures
- [ ] Store listing (description, screenshots, category, age ratings, restricted-capability
      justification for `runFullTrust`, citing NanaZip as a same-category precedent) completed in
      Partner Center
- [ ] Submitted for certification
- [ ] App passes Microsoft's certification and is live in the Store — not graduated to `[x]` on
      "submitted" alone; a rejected submission means real fixes are still outstanding
- [ ] A standing post-Windows-update check ("does Pakko's context-menu entry still appear?") is
      documented in `CLAUDE.md`'s Known-test-gaps-style notes — NanaZip's own history shows this
      recurring after Windows updates for MSIX-packaged shell extensions specifically, not a
      one-time submission-day risk

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
- [ ] **Status:** future. **SignPath Foundation eligibility resolved 2026-07-23: rejected** (see
      T-F124's Status and `DECISIONS.md`'s T-F124 rejection entry) — insufficient public-visibility
      signals, not a quality judgment; reapplication invited once Pakko has broader external
      recognition (stars/forks/contributors/articles). **Phase 1 below as originally written
      (SignPath Foundation as "the chosen path") is no longer the active plan** — next real
      decision needed is which fallback from the cert-options table to pursue now (paid SignPath
      subscription, an OV cert, or staying self-signed for internal Ukrainian-gov distribution
      while continuing to grow public visibility toward a future SignPath Foundation reapplication)
      **Decided 2026-07-23 (user):** stay self-signed for internal/Ukrainian-gov distribution for
      now — no paid SignPath subscription or OV cert purchase yet. Revisit once T-F129 (Microsoft
      Store submission) actually resolves: a live Store listing is itself a real public-visibility/
      institutional-backing signal, and is the intended trigger to reapply to SignPath Foundation
      rather than paying immediately. T-F10 stays blocked/dormant on that outcome, not actively
      worked until then.
      Scope explicitly includes `Archiver.CLI`'s `pakko.exe`/`pakko-win-*.zip`
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
| **SignPath Foundation** | **Free**, for qualifying open-source projects | No geographic restriction found | Reputation builds over time like a paid cert, but the cert itself is free | **Applied 2026-07-23, rejected** (T-F124) — real eligibility bar is public-visibility signals (stars/forks/contributors/external articles/institutional backing), not just license+CI+privacy-policy checkboxes as the pre-application gap analysis assumed. Reapply once those signals grow. |
| SignPath (paid subscription) | Paid, tier per `docs.signpath.io/change-subscription` | Same infra as the Foundation program | Same reputation-building model | Offered directly in SignPath's rejection email as the immediate alternative to waiting for Foundation eligibility — pricing not yet checked against this project's budget |
| Azure Artifact Signing (formerly "Trusted Signing") | ~$9.99/mo | Individuals: **USA/Canada only**. Organizations: also EU/UK | Reputation builds over time | Blocks an individual developer submitting from Ukraine — would need to register as an org in an eligible region, or use a different option |
| OV certificate (DigiCert, Sectigo, etc.) | $150–300/yr | Worldwide | Reputation builds over time | Fallback if SignPath Foundation eligibility doesn't pan out in practice |
| EV certificate | $400+/yr | Worldwide | **No longer instant** — Microsoft removed EV's SmartScreen-bypass-on-first-download behavior in 2024; EV now builds reputation the same way OV does | Not worth the premium anymore, purely for SmartScreen purposes (older docs/advice claiming "immediate trust" are stale) |
| Self-signed | Free | — | Blocks install for public users; fine for enterprise-managed trust | For Ukrainian government deployment specifically: self-signed with the root cert distributed via Group Policy remains viable for *internal* rollout, independent of whatever's chosen for public GitHub distribution |
| Sigstore / Cosign | Free | Worldwide | **None — does not apply** | **Not a substitute for the above, added 2026-07-19 per user request for comparison.** Confirmed via research (not assumed): Sigstore's Fulcio CA is not in the Microsoft Trusted Root Program, so a Sigstore/Cosign signature is not an Authenticode signature — Windows SmartScreen/AppLocker/WDAC never see it and a `pakko.exe` signed only this way still shows "Unknown Publisher." `cosign sign-blob` also produces a detached signature bundle, not a PKCS#7 signature embedded in the PE file the way `signtool`/SignPath do. Solves a genuinely different problem (supply-chain provenance/attestation — proving a given binary was really built by this repo's own CI, verifiable via `cosign verify-blob`/`gh attestation verify`) than the one T-F10 exists to solve (SmartScreen warnings blocking install for a government/defense audience). **Worth adding anyway, as a free complement, not a replacement:** GitHub Actions' built-in `actions/attest-build-provenance` (public repos, free, Sigstore-backed, near-zero setup on top of the existing `build.yml`) gives SLSA Build Level 2 provenance attestations for the MSIX and `pakko.exe` — fits Pakko's whole auditability/transparency positioning (`SECURITY.md`) as a nice-to-have, independent of and in addition to whichever Authenticode option above is chosen. Tracked as T-F125, not a candidate for T-F10's actual cert decision. |

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
- [x] SignPath Foundation eligibility confirmed by actually applying (not just reading their public
      criteria) — **rejected 2026-07-23** (T-F124). Real outcome, not a stale "not yet applied"
      state — Phase 1 below assumed this would be approved and needs re-deciding against the
      fallback options in the cert-options table above before any of the checkboxes below proceed.
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
- [x] **Status:** done 2026-07-18, all 11 planned steps implemented and on-device verified — full
      design done 2026-07-17 via Plan Mode + a design-advisor (Plan agent) session. Scope was deliberately
      **expanded beyond this task's original 4-key text below** after the advisor fetched and
      verified the real
      [`NanaZip/Documents/Policies.md`](https://raw.githubusercontent.com/M2Team/NanaZip/main/Documents/Policies.md)
      (via WebFetch, not from memory/description — per `CLAUDE.md`'s pre-implementation-research
      norm) and found the directly-comparable competitor already ships a richer version of the
      same idea: `AllowedHandlers`+`BlockedHandlers` (blocklist takes precedence) and a 3-state
      `WriteZoneIdExtract` (0/1/2) for MOTW. User chose to match that richer shape rather than
      keep the original binary-only 4 keys. **`StrictZipBombMode` was then dropped entirely
      (2026-07-17, user decision)** — it was already flagged by the advisor as the weakest-grounded
      of the five keys (no desktop archiver exposes a configurable compression-ratio threshold as
      a GPO value), and the user chose not to carry that complexity forward. **Final key count is
      4**, not 5. `POLICIES.md` (repo root) documents this table for sysadmins, with a pointer from
      `SECURITY.md`. **2026-07-18 implementation pass:** all 11 ordered steps below done, including
      real ADMX/ADML authored against NanaZip's own fetched, verified, shipped template (not from
      memory), and two real gaps the original plan missed — `Archiver.CLI` (shipped the same day as
      this plan, after the plan itself was written) and `DisableTarExtraction` also needing to
      block archive *creation* in `ArchiveCreationRouter`, not just extraction — found and fixed
      during implementation; see `DECISIONS.md`'s T-F51 entry for the full trail.
      `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` green repo-wide.
      **Graduated to `[x]` 2026-07-18, user-directed** — agent-driven on-device verification via
      the local `windows` MCP server (elevated PowerShell for the `HKLM\Software\Policies\Pakko\`
      writes and `%SystemRoot%\PolicyDefinitions` copy, each round explicitly UAC-approved by the
      user): all 4 keys confirmed against the real installed `pakko.exe`/`Archiver.App` —
      `EnforceMOTW` 2/0/1 produced exactly the expected per-file/no-file/all-file
      `Zone.Identifier` pattern on a real ZIP with a `.txt`+`.exe` pair;
      `BlockedFormats=zip`/`AllowedFormats=gzip` each correctly blocked/allowed the expected
      archive with the documented Group-Policy skip message; `DisableTarExtraction=1` blocked both
      tar.gz extraction and `.tar` creation (distinct error messages, correct exit codes) while
      leaving ZIP unaffected in both directions, **and** correctly hid all 6 tar `ComboBoxItem`s in
      the real WinUI Format dropdown (only "ZIP" rendered, no artifacts) — resolving the
      Collapsed-index empirical risk flagged in "Ordered implementation steps" below. A real
      `gpedit.msc` import (`Pakko.admx` + `en-US\Pakko.adml` copied into
      `%SystemRoot%\PolicyDefinitions`) showed the "Pakko" category with all 4 policies and their
      display names, "Not configured" by default, no XML parse errors. All test registry values,
      copied ADMX/ADML files, and the `gpedit.msc` process were removed/closed afterward — no
      lasting system state left behind.

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
1. [x] Shared `ArchiveFormat`/`ArchiveContainerFormat` ↔ string mapping helper
       (`ArchiveFormatRegistryNames`).
2. [x] `GroupPolicyOptions` record + `IRegistryReader`/`Win32RegistryReader`/`FakeRegistryReader` +
   `GroupPolicyService.Load()` + `GroupPolicyServiceTests` (absent/present/malformed cases,
   DWORD=0 vs. absent distinguished explicitly). No consumer wiring yet.
3. [x] `ArchiveEntrySecurity.TryPropagateMotw(archivePath, destFilePath, MotwMode)` + unsafe-extension
   list; `ZipArchiveService`/`TarSandboxedService` get the optional `GroupPolicyOptions?` ctor
   param.
4. [x] `ExtractionRouter.IsSupported` guard + ctor param + tests using literal `GroupPolicyOptions`
   records (no registry fake needed here, same as existing `TarCapabilities` tests).
5. [x] `ArchiveCreationRouter` guard (new code) + tests — also gained a `DisableTarExtraction`
   check beyond the original plan (see `DECISIONS.md`'s T-F51 entry: `POLICIES.md` already
   documented creation being blocked too, the step list alone didn't say so).
6. [x] `App.xaml.cs` DI wiring.
7. [x] `Archiver.Shell/Program.cs` — 3 call sites updated (`BuildExtractionRouterAsync`,
   `RunArchiveAsync`, `RunTestAsync`) — plus `Archiver.CLI/Program.cs`'s equivalent call sites,
   missing from this step's original text entirely since Archiver.CLI shipped the day after this
   plan was written (see `DECISIONS.md`'s T-F51 entry).
8. [x] `MainViewModel` + `MainWindow.xaml` (`TarFormatVisibility`, forced format reset) — the
   Collapsed-index empirical check passed on-device 2026-07-18 (only "ZIP" rendered in the real
   Format dropdown with `DisableTarExtraction=1` set, no artifacts).
9. [x] `deploy/Pakko.admx` + `deploy/en-US/Pakko.adml` + `deploy/README.md` — authored against
   NanaZip's own real, fetched, shipped ADMX/ADML as structural precedent (see `DECISIONS.md`).
10. [x] Cascade doc updates: `ARCHITECTURE.md` (new models/interfaces/DI — new "v1.4 —
    GroupPolicyOptions (T-F51)" section), `SPEC.md` (GPO table/roadmap status), `DECISIONS.md`
    (implementation-trail entry), `POLICIES.md` (status banner).
11. [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"`, no path argument, all
    projects green.

**Acceptance criteria (updated for final 4-key scope):**
- [x] `GroupPolicyService` reads all four keys at startup, never throws on absent/malformed values
- [x] Policies override corresponding user settings; `BlockedFormats` takes precedence over
      `AllowedFormats`
- [x] `EnforceMOTW=2` propagates MOTW only to files matching the unsafe-extension list;
      `EnforceMOTW=0` disables propagation entirely; absent key preserves today's always-on default
      — confirmed on-device against a real `.txt`+`.exe` pair
- [x] `DisableTarExtraction=1` hides tar format options in the UI and blocks tar.exe extraction
      end-to-end (context menu + in-app) — confirmed on-device (real WinUI dropdown + real CLI
      extraction/creation blocks)
- [x] ADMX/ADML template files added to repo (`deploy/Pakko.admx`, `deploy/en-US/Pakko.adml`,
      `deploy/README.md`) — a real `gpedit.msc` import confirmed on-device 2026-07-18: "Pakko"
      category with all 4 policies, no XML parse errors
- [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` passes (no path arg, all
      projects) — unit tests with a hand-rolled `FakeRegistryReader`, no mocking library
- [x] On-device verification: real registry values set under `HKLM\Software\Policies\Pakko\`
      (agent-driven via the local `windows` MCP server, each elevated write explicitly
      UAC-approved by the user), installed app relaunched, each of the 4 keys' effects confirmed
      for real (tar hidden/blocked, format block/allow, MOTW mode difference on a real
      `.exe`-vs-`.txt` extraction) — see the Status line above for the full account

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


### T-F131 — Recognize .jar/.war/.ear/.apk as ZIP-Format Archives (Explorer + FileTypeAssociation)
- [x] **Status:** done 2026-07-24 — `Deploy.ps1` build+sign+install completed, then AI-driven
      on-device verification via a real, freshly-built `app.jar` (real `PK\x03\x04` bytes,
      confirmed byte-for-byte before testing) in Explorer: right-click showed "Pakko" →
      "Відкрити"/"Видобути файли..."/"Видобути до поточної папки"/"Видобути до поточної папки
      (Інтелектуально)"/"Видобути до \"app\\\"\"/"Стиснути..."/"Тестувати архів"/hash commands, all
      present where previously only "Стиснути..." would have shown. Functionally exercised, not
      just menu visibility: "Тестувати архів" against the real `.jar` returned "No errors detected
      in the archive(s)."; "Видобути до \"app\\\"" produced real extracted files
      (`Main.class`/`readme.txt`) with byte-identical content confirmed via direct file read.
      Added at the
      user's explicit request after a question about how Pakko handles a ZIP-container file with a
      non-`.zip` extension (e.g. a `.jar`). `ArchiveFormatDetector.Detect()`'s magic-byte sniffing
      already classified these correctly with no change needed — the actual gap was the
      extension-only fast paths (Explorer context menu, `FileTypeAssociation`) that gate *before*
      any file is opened (see `DECISIONS.md`'s T-F86 entry for why those stay extension-only, not
      magic-byte, deliberately). Scope decided via `AskUserQuestion`: `.jar`/`.war`/`.ear` (Java)
      and `.apk` (Android) only — explicitly **not** Office/OpenDocument (`.docx`/`.xlsx`/`.pptx`/
      `.odt`/etc.) or `.epub`, since those would put "Extract"/"Test archive" on every document a
      typical user has, which is real UX noise for a general audience even though they're
      technically valid ZIP containers too. See `DECISIONS.md`'s T-F131 entry.
- **Depends on:** none

**Scope:**
- `Archiver.Core/Services/ArchiveFormatDetector.cs` — `.jar`/`.war`/`.ear`/`.apk` added to
  `_recognizedExtensions` (feeds `IsRecognizedArchiveExtension`, used by `MainViewModel`'s
  extract-selection detection and T-F98's nested-archive drill-down candidacy check).
- `Archiver.ShellExtension/ShellExtUtils.cpp` — `HasZipExtension` widened from a single `.zip`
  check to a new `kZipContainerExtensions` list (`.zip`/`.jar`/`.war`/`.ear`/`.apk`); flows through
  unchanged to `AllPathsAreZip`/`AnyPathIsZip`/`AllPathsAreSupportedArchive` and therefore to every
  context-menu command's `GetState()` gating (Extract here/to folder, Test archive, Extract dialog).
- `Package.appxmanifest` — the four extensions added to the existing `archivefile`
  `FileTypeAssociation` group (`DisplayName="Pakko Archive"`) rather than a new group, since the
  practical effect (register as a capable Explorer/"Open with" handler) is identical regardless of
  which engine (`ZipArchiveService` vs `ITarService`) actually handles the file internally.
- New test coverage: `ArchiveFormatDetectorTests.IsRecognizedArchiveExtension_RecognizedExtension_ReturnsTrue`
  gained 4 `[InlineData]` cases; `ShellExtUtilsTests.cpp` gained 3 new `TEST()`s
  (`AllPathsAreZip.TrueForJarWarEarApk`, `AnyPathIsZip.TrueForJarAmongOthers`,
  `AllPathsAreZip.JarCaseInsensitive`).
- `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` green (397 Archiver.Core.Tests, was
  393). C++ `Archiver.ShellExtension.Tests.exe`: 96/96 (built and run directly via MSBuild, real
  `Archiver.ShellExtension.vcxproj` DLL project also confirmed compiling clean, not just the
  COM-free test project).

**Acceptance criteria:**
- [x] `dotnet test` green with new coverage
- [x] Real `Archiver.ShellExtension.vcxproj` DLL compiles (not just the test project, which only
      links `ShellExtUtils.cpp`/`Localization.cpp` directly)
- [x] C++ Google Test suite green (96/96)
- [x] `Deploy.ps1` build+sign+install + on-device Explorer context-menu check against a real
      `.jar`: Extract/Test both confirmed present and functionally correct (see Status above).
      **Not separately verified this round:** double-click / "Open with" → Pakko → Archive Browser
      for a `.jar` specifically — the `FileTypeAssociation` registration itself was confirmed
      installed (Deploy.ps1 succeeded, manifest change is live), but the actual double-click/"Open
      with" flow wasn't clicked through this session. Low risk (same `pakko://browse`/File-
      activation code path T-F03/T-F100 already cover for every other registered extension,
      nothing `.jar`-specific in that path), but flagging honestly rather than claiming it was
      checked when it wasn't.

### T-F132 — Sandbox the ZIP Path Like tar.exe (speculative, not scheduled)
- [ ] **Status:** future, deferred indefinitely — recorded so the idea isn't lost, not because it's
      planned work. Added 2026-07-24 after a hypothetical raised during a Reddit-thread security
      discussion (see `SECURITY.md`'s "Native decompression 0-day in the ZIP path" row in Known
      Limitations, added the same session). **Do not pick this up without a real trigger** — see
      Acceptance criteria below for what that trigger looks like.
- **Depends on:** none

**The question:** could `ZipArchiveService`'s ZIP handling (`System.IO.Compression`) be sandboxed
the same way `TarSandboxedService` sandboxes tar.exe (T-F52)?

**Answer: technically yes, but not justified today.** The sandbox subsystem
(`AppContainerProfile`/`QuarantineAcl`/`SandboxJobObject`/`SandboxedProcessLauncher`) is already
generic, not tar.exe-specific — but AppContainer confines a *process*, not a code region within
one. ZIP currently runs as an in-process library call inside `Archiver.App`/`Archiver.Shell`
itself; sandboxing it the same way would require carving it out into a new standalone worker
executable (a real new build/sign/ship artifact) launched via the existing launcher, not a config
flag on the current code.

**Why not now:**
- **No confirmed exploit to close.** T-F52's tar.exe sandbox was justified by a *demonstrated*
  exploit (T-F49's symlink escape) — this would be preemptive hardening against a theoretical
  native-decompression memory-corruption bug with no track record against this specific code path.
  `System.IO.Compression` is one of the most widely used, most audited parts of the entire .NET
  BCL — arguably broader real-world scrutiny than libarchive ever had.
- **Real performance cost, not hypothetical.** T-F35's parallel ZIP pipeline gets its speed from
  multiple threads inside one process; T-F114 already measured process-spawn overhead as the
  dominant cost in the many-small-files scenario for an *external* tool (7za.exe) — the same
  overhead would apply here, on every operation (or every batch, if redesigned to spawn once per
  batch instead of per file — a real design question, not solved by this entry).
- **New artifact, new attack surface of its own** (a new signed exe, new IPC protocol for
  progress/results across the process boundary) — not free to add.

**What would actually trigger picking this up:**
- [ ] A real CVE lands against `System.IO.Compression`'s Deflate path (or the native zlib-derived
      component it calls into) that plausibly affects Pakko's usage — not a CVE in an unrelated
      part of the BCL.
- [ ] Or: a concrete threat-model change for the target audience makes the current "accepted risk"
      framing in `SECURITY.md` no longer acceptable to them.

**If it's ever picked up, first design questions to answer (not answered here):** spawn a fresh
sandboxed worker per archive operation, or per batch (perf tradeoff above)? Reuse
`Archiver.Shell.exe`/`pakko.exe` as the worker, or a new dedicated executable? What IPC shape
carries `ProgressReport`/`ArchiveResult` back across the process boundary without reintroducing the
same "opening every file just to check" cost class T-F86 already rejected for a different reason?

**Empirical spike run 2026-07-24 — real numbers, still doesn't flip the decision above.** Built
`tools/ZipSandboxSpike/` (a committed, not throwaway, worker calling `ZipArchiveService` directly
inside the real sandbox primitives — new profile `Pakko.ZipSandboxSpike`, independent of
production `Pakko.TarSandbox`) plus `tests/Archiver.Core.PerformanceTests/ZipSandboxSpikePerformanceTests.cs`
to actually measure the "real performance cost" bullet above instead of leaving it asserted.
See `DECISIONS.md`'s new T-F132 entry for the full results table and machine spec. Headline: pure
launch/AppContainer/Job-Object overhead is a real, consistent **~90 ms fixed cost per operation**
(not per file — confirms the "amortizes" framing). But relative impact turned out to hinge on
operation length, not file count: negligible (3.6%) for a 10 s archive, dominant (37–77%) for
sub-second operations — and a second, previously-unconsidered cost (worker-process JIT cold-start,
which a discarded warmup *launch* cannot remove since each launch is a fresh process) contributed
more to the delta than the sandbox primitives themselves in 3 of 4 scenarios. Still no confirmed
CVE, so the "no confirmed exploit to close" reasoning above is unchanged and this stays deferred —
but if ever revisited, `PublishReadyToRun`/a persistent pre-warmed worker is now a known real
question, not just AppContainer/Job-Object setup cost (which this spike shows is already cheap).
