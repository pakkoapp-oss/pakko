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
- [ ] **Status:** future — scope pivoted 2026-07-12 from the original GNU-style
      `--src/--dest` sketch (kept below the divider, per the "never silently deprecate" rule) to
      a `7z`-*familiar* command syntax, per user request. Advisor-reviewed before writing this.
- **Depends on:** none

**Full command/switch specification lives in [`CLI.md`](CLI.md)** — the goal statement,
architecture rationale, the 11-command 7z→Pakko support table, the switch-fidelity table, and the
three-way unknown-input rule are all there now (moved 2026-07-13 to stop duplicating the same
tables in both files; `CLI.md` is the canonical owner per `CLAUDE.md`'s Documentation Map).

**Acceptance criteria:**
- [ ] New `src/Archiver.CLI/` project, references `Archiver.Core` directly (in-process, no
      subprocess indirection) — mirrors `Archiver.Shell`'s DI/constructor pattern
- [ ] Supported commands implemented: `x` (extract), `t` (test, ZIP only — tar reports "not
      supported" per the three-way rule), `i` (info/capabilities), `a` (archive, ZIP-create only)
- [ ] `l` (list) implemented only after T-F05's listing API exists in `Archiver.Core` — this task
      consumes that API, doesn't duplicate archive-listing logic
- [ ] Three-way unknown-command/switch handling implemented and tested (unparseable vs.
      deliberately-unsupported vs. unsupported-switch-on-a-supported-command)
- [ ] Per-switch fidelity table above reflected in actual behavior — no switch silently accepted
      and ignored; unsupported switches hit the three-way rule, not silent no-ops
- [ ] `-mx` bucketing onto `CompressionLevel` documented (in `--help` output and in
      `ARCHITECTURE.md`), not left as an undocumented approximation
- [ ] Argument parsing extracted into its own testable class (e.g. `CliArgumentParser`, mirroring
      `Archiver.Shell`'s existing `ShellArgumentParser`/`ShellArgumentParserTests` split — parsing
      logic never inline in `Main`), unit-tested in-process, no process spawned — covers the
      three-way unknown-command/switch handling and every supported command's argument shape
- [ ] **Real subprocess invocation tests against real archive fixtures — a genuinely new test
      layer for this repo, not an existing pattern to reuse.** Checked first: `Archiver.Shell.Tests`
      only unit-tests its parser class, never spawns `Archiver.Shell.exe`; no C# test project in
      this repo currently launches a built `.exe` and asserts on its real exit code/stdout — that's
      only done manually per `TESTING.md`'s smoke-test cycle. This doesn't transfer to
      `Archiver.CLI` as-is: `Archiver.Shell.exe`'s arguments are only ever generated
      programmatically by the shell extension, never typed by a person, so testing its parser
      class in isolation is sufficient. `Archiver.CLI` is different — a user or script invokes it
      directly, so its actual exit code and stdout/stderr text **are** the public contract, not an
      implementation detail; a parser-only test suite would never catch a real process returning
      the wrong exit code or malformed output. New tests (own test class/project, or a clearly
      separated section of `Archiver.CLI.Tests` — decide during implementation) that `Process.Start`
      the actual built `Archiver.CLI.exe` against real archive fixtures (reuse
      `Archiver.Core.IntegrationTests/Fixtures/` where formats overlap, e.g. `valid.7z`/`valid.rar`,
      rather than a third fixture set) covering: each supported command's happy path (`x`, `t`, `i`,
      `a`) with real output verified on disk/in stdout, and at least one real case of each of the
      three unknown-input categories (unparseable token, deliberately-unsupported real 7z command,
      unsupported switch on a supported command) with the real exit code and real stderr text
      asserted, not just that *some* non-zero exit happened
- [ ] `dotnet test --filter "Category!=Slow"` passes with both new test layers included
- [ ] Manual on-device verification: real `pakko x archive.zip`, `pakko t archive.zip`,
      `pakko i`, `pakko a archive.zip file1 file2` against real archives, plus one of each
      three-way error case, confirmed by the user personally

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
- [ ] **Status:** future

**Why critical for target audience:** government/defense environments often block unsigned executables via AppLocker/WDAC. Unsigned MSIX triggers SmartScreen.

**Two levels:**
- MSIX package signature — required for sideload installs
- Authenticode on binaries — visible in file Properties → Digital Signatures

**Certificate options:**

| Option | Cost | Trust |
|--------|------|-------|
| Commercial EV (DigiCert, Sectigo) | ~$300–500/yr | Immediate SmartScreen trust |
| Standard OV | ~$100–200/yr | Trust builds over time |
| Microsoft Store | Free | Full trust, Store review required |
| Self-signed | Free | Manual install only |

For Ukrainian government deployment: self-signed with distributed root cert via Group Policy is viable for internal use.

**Acceptance criteria (when implemented):**
- [ ] All `.exe` and `.dll` binaries signed
- [ ] MSIX package signed — installs without SmartScreen warning
- [ ] Timestamp applied
- [ ] Signing in release build process
- [ ] Certificate not in repository
- [ ] `Get-AuthenticodeSignature` returns `Valid` on all binaries

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
- [ ] **Status:** future
- **Priority:** low
- **Depends on:** T-F12 (Parallel Compression)

**What:** Replace sequential file-by-file compression with a pipeline architecture that separates reading, compression, and writing into parallel stages.

**Architecture:**
```
filesystem reader → Channel<FileWorkItem> → compression workers → archive writer
```

**Implementation primitives:**
- System.Threading.Channels for work queues
- Parallel compression tasks (bounded by ProcessorCount)
- Single-threaded archive writer (ZIP format constraint)

**Expected benefit:** 2x–4x faster compression on large archives with many files.

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

**Acceptance criteria:**
- [ ] FileWorkItem record defined: path, entryName, bytes/stream
- [ ] Reader stage enqueues files into Channel<FileWorkItem>
- [ ] Compression workers consume channel in parallel
- [ ] Writer stage is single-threaded — ZIP format requires sequential entry writes
- [ ] CancellationToken respected in all stages
- [ ] Progress reporting thread-safe — Interlocked.Increment
- [ ] SingleArchive mode only — SeparateArchives already parallelized in T-F12
- [ ] dotnet test passes — existing archive tests unchanged
- [ ] Verified: no file corruption in parallel pipeline

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
- [ ] **Status:** future (v1.4)

**What:** Registry-based Group Policy support for enterprise deployment. `PolicyService` reads at startup, overrides user settings. ADMX/ADML template provided.

**Registry path:** `HKLM\Software\Policies\Pakko\`

**Keys:**
| Key | Type | Effect |
|-----|------|--------|
| `EnforceMOTW` | DWORD | Force MOTW propagation (cannot be disabled by user) |
| `AllowedFormats` | multi-string | Whitelist of allowed archive formats |
| `StrictZipBombMode` | DWORD | Lower compression ratio threshold |
| `DisableTarExtraction` | DWORD | Block all tar.exe extraction |

**Acceptance criteria:**
- [ ] `PolicyService` reads all four keys at startup
- [ ] Policies override corresponding user settings
- [ ] `EnforceMOTW=1` forces MOTW on even if user would disable
- [ ] `DisableTarExtraction=1` hides tar format options in UI
- [ ] ADMX/ADML template file added to repo (`deploy/Pakko.admx`, `deploy/Pakko.adml`)
- [ ] `dotnet test` passes — unit tests with mocked registry

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

