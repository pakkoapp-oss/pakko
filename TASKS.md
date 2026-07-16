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

### T-F97 — Archive Browser: Preview a File (Open Without Manual Extract)
- [x] **Status:** done — implemented and on-device verified 2026-07-16 (user-driven,
      non-intrusive-preview design requested directly: a status-line indicator only, one shared
      temp cache, deleted on window close), planned via Plan Mode. Two real bugs found and fixed
      during on-device verification, neither caught by `dotnet test`: (1)
      `Launcher.LaunchFileAsync`/`StorageFile.GetFileFromPathAsync` failed silently for an
      arbitrary `%TEMP%` path from this app's full-trust packaged identity — fixed by switching to
      `Process.Start(UseShellExecute=true)`, the same mechanism already used elsewhere in this
      codebase for "open destination folder"; (2) `ArchiveResult.CreatedFiles` turned out to list
      per-archive *destination folders*, not individual file paths — the previewed file's path had
      to be computed directly from the scope dir + entry path instead. See `DECISIONS.md`'s T-F97
      entry for the full trace. `dotnet test --filter "Category!=Slow"` green (353/353 — was
      326/326 before this task; +23 `PreviewPolicyTests`, +4 `PreviewCacheTests`). Full
      `Deploy.ps1` build+sign+install completed (1.2.0.38), AI-driven on-device verification via
      the `windows` MCP server confirmed: a `.txt` entry opens in Notepad with correct content and
      a real propagated MOTW `Zone.Identifier` tag (`ZoneId=3`, matching the source archive); a
      `.jpg` entry opens in the system's registered image viewer; a non-allowlisted `.docx` entry
      still runs the full Extract flow unchanged (landed in a real `SeparateFolders`-mode
      subfolder, no preview shortcut taken); `%TEMP%\PakkoPreview\` existed with the extracted
      files while the window was open and was fully removed after closing it. Graduated to `[x]`.
- **Depends on:** T-F05 (Archive Browser) — needs the browser view to exist first

**What:** from the archive browser (T-F05), double-clicking a previewable file (image, text —
not every file type, see Security below) silently extracts just that one entry to a temp location
and opens it with the OS's default handler, instead of requiring the user to Extract-to-disk first
just to look at one file. Matches the equivalent 7-Zip/NanaZip double-click-to-preview behavior
the user pointed at. **Design correction from the original scoping below:** rather than a
lightweight custom extraction (`ZipArchiveEntry.Open()`/a bespoke single-member `tar.exe` call),
the shipped implementation reuses the real `IExtractionRouter.ExtractAsync` pipeline via
`ExtractOptions.SelectedEntryPaths` — the same mechanism T-F05's "Extract Selected" already uses.
This was a deliberate decision made during planning, not scope creep: it means T-F49's
whole-archive pre-scan and MOTW propagation both apply automatically with zero new code (they
already run inside `ZipArchiveService`/`TarSandboxedService`), instead of needing to be
re-implemented and separately verified for a new, parallel extraction path. See `DECISIONS.md`'s
T-F97 entry for the full reasoning.

**Security constraints (both non-negotiable, confirmed with user before scoping this in):**
- **MOTW must propagate to the temp preview file.** If the source archive carries a
  `Zone.Identifier` ADS (came from the internet), the temp-extracted preview file must carry it
  too — otherwise the OS handler opens a from-the-internet file with no warning, silently
  defeating the MOTW propagation this project already treats as a hard constraint for every other
  extraction path.
- **Restrict auto-open to a safe-type allowlist** (images, plain text — not an executable, script,
  `.lnk`, or macro-capable document type) — `ShellExecute`-ing an arbitrary extracted file is its
  own attack surface (a malicious file inside an archive, opened with one click, no "Extract
  to..." friction first). Anything outside the allowlist still requires the existing explicit
  Extract flow, no preview shortcut.
- tar-family single-member extraction must still run T-F49's whole-archive pre-scan first — same
  constraint already called out in T-F05's "Extract selected" design; a preview is not exempt from
  it just because it's one file.

**Scope:** temp file cleanup strategy — decided: best-effort whole-cache-root delete on window
close (not tracked-per-file deletion); see `DECISIONS.md`'s T-F97 entry.

**Acceptance criteria:**
- [x] Safe-preview-type allowlist defined (images, text at minimum) and documented in `SECURITY.md`
      — `Archiver.Core.Services.PreviewPolicy.IsPreviewable`
- [x] MOTW propagated to the temp-extracted preview file — automatic, no new code: preview reuses
      the real extraction pipeline (`ArchiveEntrySecurity.TryPropagateMotw` already runs inside
      `ZipArchiveService`/`TarSandboxedService` for every extraction, preview included). Not
      re-verified by a *new* test — covered by T-F45's existing MOTW extraction tests, since no
      new MOTW code path was introduced. Still needs the on-device internet-zone check below.
- [x] tar-family preview still runs the whole-archive pre-scan before extracting the single member
      — automatic for the same reason (identical code path as T-F05's "Extract Selected", already
      covered by T-F49's pre-scan tests)
- [x] Non-allowlisted file types show the existing Extract flow instead of auto-opening —
      `MainWindow.xaml.cs`'s `ArchiveBrowserList_DoubleTapped` branches on `PreviewPolicy.IsPreviewable`
- [x] Temp file cleanup strategy decided and recorded in `DECISIONS.md`
- [x] New tests: `PreviewPolicyTests` (allowlist enforcement, `Archiver.Core.Tests`),
      `PreviewCacheTests` (cache-root/scope mechanics, `Archiver.App.Core.Tests`) — MOTW
      propagation and tar pre-scan intentionally not re-tested under new names, per above
- [x] `dotnet test --filter "Category!=Slow"` passes
- [x] Manual on-device verification: preview a real image and a real text file from inside a ZIP
      and a tar.gz downloaded from the internet (real MOTW tag present) — confirm the extracted
      preview file also carries the internet zone tag; confirm the status line shows the
      "Opening..."/"Відкриття..." indicator with no progress bar/summary dialog; confirm a
      non-allowlisted file still runs the full Extract flow; confirm `%TEMP%\PakkoPreview\` is
      removed after closing the Pakko window. Confirmed via the `windows` MCP server 2026-07-16
      (Pakko 1.2.0.38): a ZIP's `.txt` and `.jpg` entries and a `.tar.gz`'s `.txt` entry all
      previewed correctly (Notepad/image viewer, correct content, real `ZoneId=3` MOTW tag on
      each extracted preview file); a ZIP's `.docx` entry still ran the full Extract flow into a
      real `SeparateFolders`-mode subfolder; `%TEMP%\PakkoPreview\` existed with files while the
      window was open and was gone immediately after closing it, both times tested.

---

### T-F98 — Archive Browser: Transparent Drill-Down Into Nested Archives
- [ ] **Status:** future, low priority — split out of T-F05's design discussion 2026-07-12; real
      risk (recursive archive-bomb DoS) means this needs deliberate scoping before it's picked up,
      not casual "while we're in there" scope creep onto T-F05 or T-F97
- **Depends on:** T-F05 (Archive Browser)

**What:** in 7-Zip/NanaZip, double-clicking an archive file (`.rar`/`.zip`/etc.) found *inside*
another archive transparently "enters" it — extracts just that nested archive to a temp location
and browses its contents, recursively. Pakko currently does nothing special for this case: a
nested archive is just another file; extracting the outer archive leaves it sitting on disk as a
normal file, and the user re-opens it through Pakko's normal top-level flow.

**Why this is a bigger ask than it looks, not a natural T-F05 extension:** each nesting level needs
its **own** whole-archive pre-scan (T-F49's model doesn't compose automatically across nesting —
running the pre-scan once on the outer archive says nothing about what's safe inside a nested
archive found within it) and its own temp-directory lifecycle (created, cleaned up on both success
and failure, at every level of nesting). More importantly: **automatic recursive drill-down
multiplies decompression-bomb risk** — a zip-containing-a-zip-containing-a-zip expands
exponentially per level, a materially worse DoS shape than the single-level compression-ratio bomb
T-F90/T-F94 already defend against. This clashes directly with this project's "minimal attack
surface" positioning for its government/defense target audience unless the nesting-depth and
per-level-bomb-check design is deliberate, not incidental.

**Scope (not yet designed):** at minimum needs a hard nesting-depth limit (reject drilling past N
levels, N to be decided) and confirmation that T-F94's compression-bomb confirm-and-extract model
applies independently at every nesting level, not just the outermost. Needs a `DECISIONS.md` entry
recording the depth limit and bomb-check composition before implementation, per this project's
usual practice for extraction-security changes (see T-F90/T-F94's entries for the expected shape).

**Acceptance criteria:**
- [ ] Nesting-depth limit decided and documented (`DECISIONS.md`) before implementation starts
- [ ] Each nesting level runs its own whole-archive pre-scan and compression-bomb check
      independently — not inherited or skipped based on the outer archive's result
- [ ] Temp directories cleaned up on success and failure at every nesting level, not just the
      outermost
- [ ] New tests: nested-archive-bomb rejection at depth 2+, nesting-depth-limit enforcement,
      temp-directory cleanup across multiple nesting levels including a mid-nesting failure
- [ ] `dotnet test --filter "Category!=Slow"` passes
- [ ] Manual on-device verification: drill into a real nested archive (e.g. a `.zip` containing a
      `.7z`), confirm contents browse correctly and temp state is cleaned up after closing the
      browser view

---

### T-F99 — Context Menu Missing on Drive Root (Type="Drive")
- [x] **Status:** done — manifest fix implemented and, while verifying it end-to-end on-device,
      three more real bugs were found and fixed (2026-07-13): `QuotePath` in
      `ShellExtUtils.cpp` produced `"Z:\"` for a drive-root path — a trailing backslash right
      before the closing quote escapes the quote under Win32/CRT command-line parsing instead of
      closing the argument, corrupting everything after it, so `Compress…`/`Add to X.zip` against
      a drive root silently opened with an empty pending list. Separately,
      `ZipArchiveService.ArchiveAsync`'s null-`ArchiveName` fallback and `Archiver.Shell/Program.cs`'s
      `RunArchiveAsync` (the one-click "Add to X.zip" path, which computes its own name and
      destination independently) both produced a bare `.zip` filename for a drive-root source
      (`Path.GetFileNameWithoutExtension("Z:\\")` returns `""`), and `RunArchiveAsync`'s
      `destFolder` fell back to `.` (the process's own working directory) since
      `Path.GetDirectoryName` returns `null` for a root path. `BuildAddToArchiveTitle`'s own
      existing drive-root fallback (`name.back() == L':'`) didn't catch this either —
      `PathFindFileNameW` returns a path ending in `\` unchanged, not an empty tail, so the check
      needed a trailing-backslash branch too. All four fixed; see `DECISIONS.md`'s T-F99 entry.
      AI-driven on-device verification passed via both `Compress…` and the one-click `Add to
      "archive.zip"` command against a `subst`-mapped scratch drive letter. **Second confirmation,
      2026-07-14, this time user-directed via Windows MCP automation** (`subst Z:`, right-click in
      "Цей ПК", one-click "Add to archive.zip") — Pakko's drive-root entry present, archive content
      correct. Two new minor, unfixed observations from this pass (not blocking, see `DECISIONS.md`):
      the archive lands on Desktop rather than "near" the drive (expected given the existing
      no-parent-folder fallback, just worth a UX look later), and the zip entry is stored as
      `/file1.txt` (leading slash) rather than `file1.txt`. Graduated to `[x]`.
- **Depends on:** none

**What:** right-clicking a drive root (e.g. `C:\` in Explorer's left-hand tree or "This PC") shows
NanaZip's "Compress to..."-style entries but no Pakko entry at all.

**Root cause, confirmed against NanaZip's real shipped manifest** (fetched
`raw.githubusercontent.com/M2Team/NanaZip/main/NanaZipPackage/Package.appxmanifest` per
`CLAUDE.md`'s pre-implementation-research rule): NanaZip registers its context-menu verb on
**three** item types —
```xml
<desktop4:ItemType Type="*">          <!-- files -->
<desktop5:ItemType Type="Directory">  <!-- folders -->
<desktop10:ItemType Type="Drive">     <!-- drive roots -->
```
Pakko's `Package.appxmanifest` only has the first two (`src/Archiver.App/Package.appxmanifest`,
`desktop4:FileExplorerContextMenus` block). Neither `xmlns:desktop10` nor a `Drive` `ItemType`
entry exists — the drive-root case was simply never registered.

**Acceptance criteria:**
- [x] `xmlns:desktop10="http://schemas.microsoft.com/appx/manifest/desktop/windows10/10"` declared
      in `Package.appxmanifest` (added to `IgnorableNamespaces` too)
- [x] `<desktop10:ItemType Type="Drive"><desktop10:Verb Id="0000PakkoShellExtension"
      Clsid="1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A" /></desktop10:ItemType>` added alongside the
      existing `*`/`Directory` entries
- [x] `PakkoRootCommand::GetState`/`EnumSubCommands` behavior checked against a drive-root
      `IShellItemArray` selection — confirm it doesn't assume a real file path where a drive root
      (e.g. `C:\`) is passed (path-parsing edge case, not just the manifest registration).
      `GetState`/`EnumSubCommands` themselves needed no change (`AllPathsAreSupportedArchive`/
      `AllPathsAreZip` already degrade safely for a no-extension path); the real edge cases were
      in the argument-quoting and archive-naming code invoked by `Invoke`, see above.
- [x] Full `Deploy.ps1` build+sign+install, on-device right-click on a real drive root (via
      `subst`, a small scratch folder mapped to a drive letter — avoids risking a real
      multi-hundred-GB volume) confirms the Pakko entry now appears, and its Archive command
      produces a valid archive of the drive's contents, tested via both `Compress…` and the
      one-click `Add to "archive.zip"` command — confirmed twice: AI-driven 2026-07-13, and again
      2026-07-14 user-directed via Windows MCP automation.

---

### T-F100 — File Activation Opens Archive-Creation UI Instead of Browsing the Archive
- [x] **Status:** done — both root causes fixed 2026-07-13. New `FileActivationRouter` static
      class in `Archiver.App.Core` (WinUI-free, mirrors `ArchiveTreeIndex`'s testability split)
      decides Browse vs. AddToList; `App.xaml.cs`'s `HandleActivation` File case now awaits
      `EnterBrowseModeAsync` for a single recognized archive instead of unconditionally calling
      `AddPaths`. `Package.appxmanifest`'s `windows.fileTypeAssociation` extended with a second
      `archivefile` extension covering `.rar .7z .tar .gz .tgz .bz2 .tbz2 .xz .txz .zst .tzst
      .lzma` — reusing `ShellExtUtils.cpp`'s existing `kSupportedNonZipArchiveExtensions` list
      (already the project's canonical non-ZIP-format list) rather than deciding a new one; see
      `DECISIONS.md`. `dotnet test` green (4 new `FileActivationRouterTests`). AI-driven on-device
      verification passed for all four formats (`.zip`, `.7z`, `.rar`, `.tar.gz` — the last built
      with real `tar.exe`) via "Open with → Pakko", each opening directly into the T-F05 browser
      view. **Second confirmation, 2026-07-14, user-directed via Windows MCP automation:**
      re-verified all four formats the same way, plus exercised T-F05's Extract Selected/Extract
      All from each (see T-F05's entry below) — every format opened directly into the browser and
      extracted correctly. Graduated to `[x]`.
- **Depends on:** T-F05 (Archive Browser) — the correct destination behavior (browse mode) only
      exists because of T-F05's `EnterBrowseModeAsync`

**What:** double-clicking a `.zip` (or opening via `pakko://`/file association) opens Pakko with
the archive added to the "files to archive" list — i.e. the Archive-creation UI — instead of
opening the archive browser to show its contents. Separately, Windows never offers Pakko in the
"Open with" list for any archive format other than `.zip`.

**Root cause #1, confirmed in code:** `src/Archiver.App/App.xaml.cs:HandleActivation`, the
`ExtendedActivationKind.File` case, unconditionally calls `_window.ViewModel.AddPaths(paths)` —
the same method used for "add these files to the pending archive-creation list." It never checks
whether the activated file is itself a supported archive, and never calls T-F05's
`MainViewModel.EnterBrowseModeAsync(path)`.

**Root cause #2, confirmed in manifest:** `Package.appxmanifest`'s `windows.fileTypeAssociation`
extension lists only `<uap:FileType>.zip</uap:FileType>` — no `.rar`/`.7z`/`.tar`/`.gz`/etc. entry
exists, so Windows has no association to offer for any other format, regardless of what
`TarCapabilities`/`tar.exe` can actually read at runtime.

**Acceptance criteria:**
- [x] `HandleActivation`'s File case: when exactly one file was activated and
      `ArchiveFormatDetector.Detect(path)` reports a supported format, call
      `EnterBrowseModeAsync(path)` instead of `AddPaths`. Multi-file activation (e.g. selecting
      several files and using "Open with → Pakko") keeps today's `AddPaths` behavior — browsing
      only makes sense for a single archive.
- [x] `Package.appxmanifest`'s `FileTypeAssociation` extended to cover every format Pakko can
      actually read today (ZIP always; tar-family formats gated by what `tar.exe` on a supported
      Windows build reads per `TarCapabilities` — decide the static list against that capability
      table, not NanaZip's full ~60-extension list, since Pakko doesn't support most of those)
- [x] Opening a format Pakko is associated with but the runtime `TarCapabilities` doesn't actually
      support (older Windows without a capable `tar.exe`) still shows the existing
      capability-gap error message — not a silent failure or crash (unchanged code path —
      `ArchiveListingRouter`'s existing `IsSupported`/`BuildUnsupportedReason` branch; confirmed by
      reading it, no new gap introduced by this task)
- [x] New test(s) covering the File-activation routing decision (single supported-archive path →
      browse; multi-file or unsupported-format path → existing add-to-list behavior) — likely on
      a testable seam extracted from `HandleActivation`, mirroring this project's existing
      "extract decision logic out of `.xaml.cs`/`App.xaml.cs` into something testable" pattern.
      `FileActivationRouter`/`FileActivationRouterTests` in `Archiver.App.Core`(`.Tests`).
- [x] `dotnet test --filter "Category!=Slow"` passes
- [x] Manual on-device verification: double-click a real `.zip` → opens directly into the T-F05
      browser view (not the Archive UI); double-click a real `.rar`/`.7z`/`.tar.gz` → same;
      confirm Pakko now appears in Windows' "Open with" list for at least one non-`.zip` format.
      AI-driven verification done (2026-07-13, via "Open with → Pakko" since `.zip`'s system
      default had reverted to Windows' built-in `CompressedFolder` handler after the MSIX
      reinstall — a real, separate observation, not this task's bug) for all four formats.
      Re-confirmed 2026-07-14, user-directed via Windows MCP automation, same method, same result.

---

### T-F101 — Pakko Missing From Classic "Show More Options" Context Menu
- [x] **Status:** resolved (no code fix; cause unconfirmed) — diagnosed 2026-07-13 (AI-driven
      on-device investigation, root cause not identified, two candidate explanations ruled out).
      Re-tested 2026-07-14 (user-directed, via Windows MCP automation, repro run twice
      reproducibly): Pakko now appears in the classic "Показати додаткові параметри" menu right
      next to NanaZip. No code changed between the two dates — leading (unverified) hypothesis is
      that T-F100's `Package.appxmanifest` `FileTypeAssociation` change, landed the same day as the
      original diagnosis, also invalidated whatever Explorer verb/icon cache was suppressing Pakko
      from the classic menu. See `DECISIONS.md`'s T-F101 entry for the full resolution note and an
      automation gotcha (UI-Automation tree-walks collapse open Win32 popup menus — use plain
      screenshots + pixel clicks instead).
- **Depends on:** none

**Symptom:** right-clicking a file in Explorer shows NanaZip's entry in both the modern
(top-level, Windows 11) context menu and the classic "Show more options" menu. Pakko's entry only
appears in the modern menu — it is missing from "Show more options" entirely.

**Why this isn't a quick manifest fix:** NanaZip's real shipped manifest
(`NanaZipPackage/Package.appxmanifest`, verified via `raw.githubusercontent.com`) uses the exact
same "low ID prefix" workaround Pakko already has —
`Id="0000NanaZipShellExtension"` / Pakko's `Id="0000PakkoShellExtension"` — with a comment citing
`github.com/MediaArea/MediaInfo#998` for why the prefix matters for classic-menu visibility.
Structurally, Pakko's `desktop4:FileExplorerContextMenus` block already matches NanaZip's on this
point, so the manifest is not an obvious explanation by itself.

**Diagnosis this round (AI-driven, on-device):**
- **Repro confirmed real**, on a fresh Explorer window: modern menu shows NanaZip and Pakko both
  directly (no "Show more options" needed to see them); clicking "Показати додаткові параметри"
  transitions to the classic Win32 menu, which lists `Відкрити`/`Відкрити за допомогою`/…/
  `Властивості`/`NanaZip`/**no Pakko**/`Показати додаткові параметри` — matches the reported
  symptom exactly.
- **Ruled out — stale installed build:** `Get-AppxPackage`/the installed `AppxManifest.xml`
  confirmed the running package (1.2.0.17 at diagnosis time) matches current source byte-for-byte
  on the relevant `FileExplorerContextMenus`/`ItemType` block (trivially true this round since
  T-F99/T-F100 had just redeployed) — the original bug report's "maybe it's an old build" theory
  doesn't hold, at least not for this repro.
- **Ruled out — a crash during classic-menu enumeration:** `Get-WinEvent` against `.NET Runtime`
  and `Application Error` providers across the whole repro window returned zero events. One
  unrelated `Application Hang` event (ID 1002, `dllhost.exe`, `HangType: Quiesce`,
  `PackageFullName: ...Pakko_1.2.0.16...`) was found, but its timestamp and `Quiesce` hang type
  match this session's own rapid `Deploy.ps1` uninstall/reinstall cycling (Windows asking the old
  package's COM surrogate to quiesce during an MSIX replace), not Explorer querying the classic
  menu — a red herring, not evidence for this bug.
- **Not yet tried:** Process Monitor / ETW trace of `explorer.exe` actually invoking
  `IExplorerCommand::EnumSubCommands`/`GetState` specifically during classic-menu population, to
  see whether Explorer even calls into `Archiver.ShellExtension.dll` for that code path at all
  (vs. some earlier verb-caching/enumeration step deciding not to). This is the natural next step
  but wasn't attempted this round — UI-automation-driven repro is unreliable for capturing the
  classic Win32 popup menu's exact call timing (it closes faster than tool round-trips in this
  environment), so a real trace tool is likely needed rather than more UI automation attempts.

**Acceptance criteria:** none were written (root cause was never pinned down) — symptom stopped
reproducing as of 2026-07-14 (see status line above). Re-open this task with a proper root-cause
investigation (Process Monitor/ETW trace, per the diagnosis section above) if it ever regresses.

---

### T-F103 — Extraction Destination Folder Misnamed for Compound Extensions (.tar.gz etc.)
- [x] **Status:** done — found 2026-07-13 while smoke-testing T-F05/T-F100 against a real
      `.tar.gz` fixture, not part of any task's original scope. Fixed 2026-07-14: root cause was
      exactly as suspected — `Path.GetFileNameWithoutExtension` (and the C++ equivalent) strip only
      the last dot segment. New shared `Archiver.Core.Services.ArchiveNaming.GetBaseName()` helper
      strips the five compound extensions `tar.exe` itself creates (`.tar.gz`, `.tar.bz2`,
      `.tar.xz`, `.tar.zst`, `.tar.lzma`) as a unit before falling back to the single-dot rule;
      wired into all four call sites that had the bug (`ZipArchiveService.cs` × 2 — the
      `SeparateFolders`-mode destination and the smart-foldering wrapper-folder case,
      `TarProcessService.cs` × 1, `Archiver.Shell/Program.cs` × 2 — `RunExtractHereAsync`/
      `RunExtractFolderAsync`). The native C++ `ShellExtUtils.cpp::GetFileNameWithoutExtension`
      helper (used by both `BuildAddToArchiveTitle` and `BuildExtractFolderTitle`) got the same
      fix, kept in sync via a cross-reference comment — this incidentally also fixes the inverse,
      out-of-scope case (archiving a source file itself named like a compound archive, e.g.
      `backup.tar.gz`, would have produced `backup.tar.zip`) for free, since both title builders
      share the one helper. `dotnet test --filter "Category!=Slow"` green (235/235, +14 new:
      `ArchiveNamingTests` × 12 theory cases, one `ZipArchiveServiceExtractTests` case, one real
      `TarProcessService` `SeparateFolders`-mode integration test against an actual `tar.exe`
      `.tar.gz` fixture — the last exercises a code path this test file never covered before,
      since every other test there used `SingleFolder` mode with an explicit `destDir`).
      `Archiver.ShellExtension.Tests.exe` green (59/59, +3 new: two C++ compound-extension title
      cases). **Full `Deploy.ps1` build+sign+install completed and on-device verified 2026-07-14**
      (user-directed via Windows MCP automation) against the real `browse_test.tar.gz` fixture:
      the shell's "Extract to..." title itself now reads `Extract to "browse_test\"` (was
      `browse_test.tar\`); "Extract to folder" created `browse_test\` (confirmed via the
      dir's own mtime, since a stale `browse_test.tar\` from the original bug repro was still
      sitting on disk and stayed untouched); "Extract here" created the correctly-named
      `browse_test (1)\` (numbered — a same-named folder from an earlier test already existed);
      and the Archive Browser's Extract All routed its content into the correct `browse_test\`
      (confirmed via a `root (1).txt` rename-on-conflict landing there, not in `browse_test.tar\`).
      All three previously-buggy code paths (Core's two services + `Archiver.Shell`) now agree.
      Graduated to `[x]`.
- **Depends on:** none

**What:** extracting `browse_test.tar.gz` (via the T-F05 Archive Browser's Extract All) created
its destination folder as `browse_test.tar`, not `browse_test` — the archive's *contents* were
extracted completely and correctly, only the destination folder's name is wrong.

**Root cause (not yet located precisely, candidate site):** whatever computes the destination
folder name from the archive path almost certainly uses `Path.GetFileNameWithoutExtension`, which
only strips the *last* extension (`.gz`), leaving `.tar` on the end — the same single-extension
assumption this task's siblings (T-F99) found breaking for a drive root. Likely in
`MainViewModel.cs` (T-F05's Extract All/Selected commands) and/or `Archiver.Shell/Program.cs`'s
`RunExtractHereAsync`/`RunExtractFolderAsync` (both already call
`Path.GetFileNameWithoutExtension(archivePath)` for the same purpose) — needs checking both, since
they're independent code paths (same pattern as T-F99's fix needing changes in three separate
places for one conceptual bug).

**Acceptance criteria:**
- [x] Root cause located (likely more than one call site, per the note above) — five call sites
      across three files, plus a sixth (cosmetic, title-only) in the native shell extension
- [x] `.tar.gz`/`.tar.bz2`/`.tar.xz`/`.tar.zst` (every double-barrelled extension `tar.exe` itself
      creates, per `CLAUDE.md`'s tar.exe format-support hard constraint) strip both components,
      not just the last — `.tar.lzma` included too, same shape
- [x] New tests covering a `.tar.gz`-named archive's extraction destination folder name
- [x] `dotnet test --filter "Category!=Slow"` passes
- [x] Manual on-device verification: extract a real `.tar.gz` via the Archive Browser and via the
      shell's Extract-here/Extract-to-folder commands, confirm the destination folder is named
      after the full compound extension stripped, not just the last segment

---

### T-F104 — Bugs: Archive/Extract Buttons Never Localized + Empty-State Hint Text Clipped
- [x] **Status:** done — both found and fixed 2026-07-15 while doing T-F91's own on-device
      layout-corruption verification pass (uk-UA, direct screenshot via a native Win32
      `GetWindowRect`/`CopyFromScreen` capture — the Windows UI-automation MCP server referenced
      in `.claude.local.md` was not loaded this session, so this used a self-contained PowerShell
      screenshot instead). Neither bug is specific to the 12 non-European locales added this same
      round — both are pre-existing, locale-independent bugs that simply hadn't been looked at this
      closely before.
      **Bug 1 (localization plumbing):** `MainWindow.xaml`'s two primary action buttons bind
      `Content` to `MainViewModel.ArchiveButtonText`/`ExtractButtonText` (`MainViewModel.cs:78-83`)
      via `x:Bind` — needed because their text must swap to "Archiving..."/"Extracting..." during
      an operation, which a static `x:Uid` can't express. Both getters had **hardcoded English
      string literals** (`"Archive"`, `"Archiving..."`, `"Extract"`, `"Extracting..."`) instead of
      going through `_res.GetString(...)` like every other status string in this ViewModel — so the
      app's two most prominent buttons never respected any locale, in any of the 36 non-English
      `Resources.resw` files, the entire time T-F91 has existed. Confirmed via direct screenshot:
      the rest of the uk-UA window rendered correctly in Ukrainian while these two buttons alone
      showed plain English. Fixed by routing both through `_res.GetString("ArchiveButtonLabel")`/
      `_res.GetString("ExtractButtonLabel")` (idle state) and the pre-existing, already-translated
      `_res.GetString("StatusArchiving")`/`_res.GetString("StatusExtracting")` (busy state) — no
      new busy-state strings needed, they already existed for the status line.
      **Root cause of why a resw key existed but did nothing:** `ArchiveButton.Content`/
      `ExtractButton.Content` were already present in all 37 `Resources.resw` files (correctly
      translated in every locale) but were **dead resources** — `MainWindow.xaml` never has
      `x:Uid="ArchiveButton"`/`x:Uid="ExtractButton"` on these two buttons (confirmed by grep — zero
      matches), so nothing ever applied them. `ResourceLoader.GetString()` on a key that exists in
      the `.resw` but was never linked via `x:Uid` still works as a plain key lookup in principle,
      but this codebase had never actually exercised a *dotted* key through manual `GetString()`
      before (confirmed — this was the only such call site in the repo) and it silently returned
      an empty string rather than throwing, so the bug produced blank-looking button backgrounds
      with no error, not a crash. Renamed the key to plain, non-dotted `ArchiveButtonLabel`/
      `ExtractButtonLabel` across all 37 locale files (mechanical `sed` rename, same translated
      values) to match this codebase's own established convention for every other manually-accessed
      string (`StatusReady`, `StatusArchiving`, etc. are all plain keys) — avoids relying on the
      `x:Uid`-implicit dotted-key convention for a key that was never actually meant to be
      `x:Uid`-driven in the first place.
      **Bug 2 (layout, unrelated to Bug 1):** the pending-list empty-state hint overlay (two
      `TextBlock`s + a `FontIcon`, `MainWindow.xaml` ~line 171) had its second line's bottom edge
      visibly clipped by the Grid row boundary below it, in the standard 1100x650 window (T-F05's
      fixed size) — confirmed via a zoomed crop of the on-device screenshot, not a rendering
      artifact of the capture itself. Root cause: `Grid.Row="1"` in the pending-list body is
      star-sized and gets whatever vertical space is left after all the `Auto`-sized rows below it
      (destination, action buttons, mode, name, compression, conflict, checkboxes, status) claim
      theirs — at 1100x650 there isn't quite enough left for the icon + two-line hint text at their
      original size, and WinUI's `Grid` clips children to their layout slot by default. Not specific
      to Ukrainian text length (en-US's equivalent strings are a comparable length — this would
      likely reproduce in English too, just hadn't been screenshotted this closely before). Fixed
      by shrinking the empty-state `FontIcon` (`FontSize="28"` → `"20"`) and `StackPanel`
      `Spacing="4"` → `"2"`, freeing enough height for both hint lines to render fully — confirmed
      via a second on-device screenshot. A deeper fix (resizing rows, or measuring available space)
      wasn't attempted since this small change fully resolved the observed clipping without
      touching the window's fixed size or other rows' layout.
      `dotnet test --filter "Category!=Slow"` re-confirmed green (284/284, no test changes — both
      fixes are ViewModel-string-source/XAML-only) after Bug 1's fix; Bug 2 is XAML-only with no
      test surface. Full `Deploy.ps1` build+sign+install completed for both fixes (v1.2.0.28 for
      Bug 1, v1.2.0.30 for Bug 2), each confirmed via a fresh on-device screenshot after redeploy —
      the first quick `dotnet build` attempt for Bug 1 silently installed a stale MSIX (the exact
      `CLAUDE.md`-documented `DeployMsix` incremental-packaging gotcha), caught by re-screenshotting
      and seeing no change, then resolved by using the full `Deploy.ps1` instead.
- **Depends on:** none

**Acceptance criteria:**
- [x] `ArchiveButtonText`/`ExtractButtonText` route through `_res.GetString(...)` for both idle and
      busy states, using real (non-dead) resource keys present in all 37 locale files
- [x] Dead `ArchiveButton.Content`/`ExtractButton.Content` resw keys renamed to plain
      `ArchiveButtonLabel`/`ExtractButtonLabel` across all 37 `Resources.resw` files, values
      unchanged
- [x] Empty-state hint overlay's two-line text no longer clipped by the row boundary below it at
      the standard 1100x650 window size
- [x] `dotnet test --filter "Category!=Slow"` passes (284/284)
- [x] On-device verification via direct screenshot (uk-UA): both buttons show translated text
      ("Архів"/"Розпакувати"), both hint lines render fully

---

### T-F06 — Ask on Conflict Dialog
- [x] **Status:** done — designed via Plan Mode 2026-07-14 (approved plan:
      `floofy-swimming-sifakis.md`), implemented the same day. `ConflictBehavior` gained a 4th
      value, `Ask`, resolved per-conflict via a new Core→UI callback
      (`ArchiveOptions`/`ExtractOptions.ResolveConflictAsync`) mirroring T-F94's existing
      `ConfirmCompressionBombExtraction` precedent (same `DispatcherQueue.TryEnqueue` marshaling).
      A new shared internal `Archiver.Core.Services.ConflictResolver` (one instance per
      `ArchiveAsync`/`ExtractAsync` call) resolves `Ask` into a concrete `Skip`/`Overwrite`/`Rename`
      before reaching each of the four existing conflict switches — those switches themselves are
      unchanged. New `ConflictInfo`/`ConflictDecision`/`ConflictResolution` models in
      `Archiver.Core.Models`. New `IDialogService.ShowConflictDialogAsync` +
      `DialogService` implementation (code-first `ContentDialog`, 3-button
      Overwrite/Rename/Skip + an "apply to all" `CheckBox`, `DefaultButton = Close` so Enter never
      resolves to the destructive Overwrite). `MainWindow.xaml`'s conflict `ComboBox` gained a 4th
      `ConflictAskItem`; `MainViewModel.OnConflictIndex` extended to the 4-way mapping;
      `en-US`/`uk-UA` `Resources.resw` both updated. `Archiver.Shell` is unaffected by construction
      (hardcodes `Rename`, never wires the callback). See `DECISIONS.md`'s T-F06 entry for the full
      design rationale (apply-to-all's whole-operation scope, the `ContentDialogResult.None →
      Skip` mapping, and the `SeparateArchives`/T-F12 same-run-collision interaction).
      `dotnet test --filter "Category!=Slow"` green (254/254, +19 new: `ConflictResolverTests`,
      plus Ask-mode cases added to `ZipArchiveServiceArchiveTests`/`ZipArchiveServiceExtractTests`/
      `TarProcessServiceExtractTests`). **Full `Deploy.ps1` build+sign+install and on-device
      verification completed 2026-07-14** (user-directed via Windows MCP automation): the dialog
      appeared correctly for both Extract (multiple real conflicts in `browse_test.zip`) and
      Archive (a pre-existing `big_test_file.zip`) — correct title/message/localization, all
      three buttons (Перезаписати/Перейменувати/Пропустити) individually confirmed via real
      filesystem checks (rename created a numbered copy; overwrite replaced a 12-byte placeholder
      with the real 20 MB archive), and "Застосувати до всіх" confirmed suppressing further
      prompts for subsequent conflicts in the same operation. `Archiver.Shell` reconfirmed
      unaffected (unchanged file; already exercised silently-renaming behavior during this same
      session's T-F103 verification). Graduated to `[x]`.
- **Depends on:** none

**Acceptance criteria:**
- [x] `ConflictBehavior.Ask` added; resolved via a new Core→UI callback at all four existing
      conflict-resolution call sites (`ZipArchiveService.ArchiveAsync` × 2 modes,
      `ZipArchiveService.ExtractAsync`, `TarProcessService.ExtractAsync`)
- [x] "Apply to all remaining conflicts" — a single decision suppresses the dialog for the rest
      of the current Archive/Extract operation (verified via callback-invocation-count assertions,
      not just final on-disk state)
- [x] `Archiver.Shell` unaffected — still hardcodes `Rename`, never shows a dialog
- [x] New tests: `ConflictResolverTests` (unit, isolated resolver logic) plus Ask-mode extensions
      to the existing Archive/Extract test files for both Zip and (real `tar.exe`) Tar paths
- [x] `dotnet test --filter "Category!=Slow"` passes
- [x] `Deploy.ps1` build+sign+install, on-device: select "Ask", trigger a real conflict during
      both Extract and Archive, confirm the dialog appears with the correct file name, test all 3
      buttons, and confirm "apply to all" suppresses further prompts within one operation

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

### T-F11 — ARM64 Support
- [x] **Status:** complete

One-line change. Windows on ARM increasingly common in government/enterprise.

```xml
<!-- Before -->
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>

<!-- After -->
<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
```

No code changes required — .NET 8 JIT handles ARM64 natively.

**Acceptance criteria:**
- [x] `win-arm64` added to `RuntimeIdentifiers`
- [x] App builds for ARM64 without errors
- [x] MSIX bundle includes both architectures
- [ ] Smoke test on ARM64: archive and extract work correctly

---

### T-F12 — Parallel Compression (SeparateArchives Mode)
- [x] **Status:** complete — implemented 2026-07-07, see `DECISIONS.md`'s T-F12 entry for the
      same-basename collision fix this required beyond the original one-line pseudocode

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

`SeparateArchives` archives are fully independent — can run in parallel.

```csharp
await Parallel.ForEachAsync(
    options.SourcePaths,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
    async (sourcePath, token) => await CreateSingleArchiveAsync(sourcePath, options, progress, token));
```

Note: `SingleArchive` mode stays sequential. Progress reporting needs `Interlocked.Increment`.

**Acceptance criteria:**
- [x] `SeparateArchives` uses `Parallel.ForEachAsync`
- [x] `MaxDegreeOfParallelism` capped at `Environment.ProcessorCount`
- [x] Progress reporting thread-safe — shared `Interlocked`-updated byte counter per worker,
      plus a forced final 100% report after the parallel loop completes (see DECISIONS.md —
      needed to keep the existing `reports.Last().Percent == 100` test deterministic)
- [x] `CancellationToken` respected — mid-flight cancellation still propagates the same way the
      old sequential loop did; a token already cancelled before the call now needs an explicit
      guard since `Parallel.ForEachAsync` throws immediately on an already-cancelled token where
      a plain `for` loop's `IsCancellationRequested` check did not
- [x] `SingleArchive` unchanged
- [x] `dotnet test --filter "Category!=Slow"` passes — 190/190 (135 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 19 Archiver.Core.IntegrationTests, was 187/187 before this task);
      3 new tests added covering many-file parallel correctness, same-basename collision
      handling, and a batch larger than typical core counts; reran the affected test classes 5x
      each with no flakiness observed
- [x] Manual on-device verification (2026-07-07, AI-driven via Windows UI automation): full
      `Deploy.ps1` build+sign+install (Pakko 1.2.0.5 — this same round also found and fixed an
      unrelated `Deploy.ps1` bug, see `DECISIONS.md`'s "Deploy.ps1 Failed After T-F91" entry),
      launched via `pakko://archive?files=...` protocol activation with 5 real files, toggled
      "Separate archives", clicked Archive. Confirmed via filesystem (5 correctly-named `.zip`
      files, each containing exactly its own source file's content, byte-for-byte) and
      `pakko.log` ("Archive completed — 5 file(s) → ...", no Warn/Error lines) that the parallel
      path works end-to-end through the real UI

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

### T-F92 — Context Menu Icon Missing on Submenu Items
- [x] **Status:** CLOSED, reverted 2026-07-07 — implemented, on-device verified, then reverted
      the same day after the user saw the actual on-device screenshots and decided submenu
      icons are visual clutter. Final code state matches pre-T-F92 (root "Pakko" entry keeps its
      icon; all six subcommands are back to `E_NOTIMPL`). Kept here rather than deleted, per the
      "never silently deprecate" rule — do not re-implement without a fresh explicit request
- **Priority:** medium (visible cosmetic gap in shipped v1.2 shell extension)
- **Depends on:** none

**What:** `PakkoRootCommand::GetIcon` (`ExplorerCommands.cpp:495`) returns a real icon
(`Archiver.App.exe,0` via the cached `GetAppIconPath()` helper), so the top-level "Pakko" entry
shows correctly. Every child command's `GetIcon` returns `E_NOTIMPL`/`nullptr` instead:
`ExtractHereCommand` (:88), `ExtractFolderCommand`, `ArchiveCommand` (:225), `TestCommand` (:288),
`ExtractDialogCommand` (:360), `CompressDialogCommand` (:430). Result: the submenu ("Extract
here", "Extract to folder…", "Add to archive…", "Test archive", the two dialog commands) shows no
icon in Explorer's dropdown.

**Original decision (per user 2026-07-07):** use the same single Pakko icon for every subcommand
— no per-action icon set. Simplest change, matches the icon already cached for the root command.

**Fix (implemented, then reverted the same day):** changed each subcommand's `GetIcon` to mirror
`PakkoRootCommand::GetIcon`'s existing safe pattern exactly — call `GetAppIconPath()`, return
`E_NOTIMPL` if it's empty, otherwise `SHStrDupW` the path into `*ppszIcon` and return its
`HRESULT`. Built, unit-tested (55/55), and on-device verified (right-clicked a ZIP and a `.txt`
file; every submenu item showed the icon, Explorer did not crash). Shown to the user via
screenshot, who then decided the per-item icons look cluttered and asked to revert to root-only.

**Reversal (per user 2026-07-07, after seeing the on-device result):** all six subcommands'
`GetIcon` reverted to the original `E_NOTIMPL` stub — only the root "Pakko" entry keeps an icon.
Rebuilt and reconfirmed `Archiver.ShellExtension.Tests` still 55/55 after the revert.

**Acceptance criteria (historical — task is closed/reverted, not open work):**
- [x] `ExtractHereCommand::GetIcon`, `ExtractFolderCommand::GetIcon`, `ArchiveCommand::GetIcon`,
      `TestCommand::GetIcon`, `ExtractDialogCommand::GetIcon`, `CompressDialogCommand::GetIcon`
      all returned `Archiver.App.exe,0` via `GetAppIconPath()` — implemented and verified, then
      reverted back to `E_NOTIMPL` per the user's follow-up decision
- [x] No `S_FALSE` + null-out-pointer combination introduced anywhere in either the fix or the
      revert
- [x] `Archiver.ShellExtension.Tests` (Google Test) still pass — 55/55, both after the fix and
      after the revert
- [x] Manual on-device verification of the fix: right-clicked a ZIP (`tf92test.zip`) and a `.txt`
      file in a scratch folder — confirmed all submenu items showed the icon, Explorer did not
      crash; done via Windows UI automation, not personally by the user
- [x] `DECISIONS.md` note — added, since the decision changed post-implementation (see the
      "T-F92 — Reverted" entry)

---

### T-F93 — Non-Intrusive Donate Link (Ko-fi)
- [~] **Status:** partial — README done 2026-07-16; About-dialog link (the app-code half) not
      started. Scope confirmed with user 2026-07-07; real URL provided 2026-07-16
      (`https://ko-fi.com/pakko_app` — Ko-fi, not Buy Me a Coffee as originally scoped; platform
      corrected below, link mechanics/placement unchanged), no longer blocked
- **Priority:** low ("not urgent," per user)
- **Depends on:** T-F14 (About dialog with version/links — already done)

**What:** add a small, non-pushy donate link to Pakko's About section and to the GitHub README,
pointing to Pakko's Ko-fi page: `https://ko-fi.com/pakko_app`. Explicitly not a banner, popup, or
nag — a small link/button only, consistent with how Ko-fi itself is typically presented.

**Scope:**
- About dialog (wherever T-F14 already put version/links, `Archiver.App`) gains one additional
  small link/button (e.g. "☕ Support the project") opening `https://ko-fi.com/pakko_app` in the
  system default browser.
- `README.md` gets an equivalent small link/badge, placed near the top or bottom — not inline
  with technical content.

**Acceptance criteria:**
- [x] Real donate URL obtained from user — `https://ko-fi.com/pakko_app`
- [ ] About dialog shows one small, non-modal donate link/button
- [x] `README.md` shows one small donate link/badge — added right under the title/tagline
- [ ] Link opens in the system default browser (not a modal or embedded frame) — applies to the
      About-dialog link; README's is a plain Markdown link, opens however GitHub/the reader
      handles external links
- [ ] `dotnet build src/Archiver.App` succeeds
- [ ] Manual on-device verification: click the link in the About dialog, confirm it opens the
      correct URL

---

## v1.2 — Shell Extension

> **Minimum supported OS:** Windows 10 1809 (10.0.17763.0).
> Shell extension uses dual registration:
> - `desktop4:FileExplorerContextMenus` — Win10 1809+, classic context menu
> - `IExplorerCommand` via COM — Win11 22000+, modern context menu
>
> Both mechanisms invoke `Archiver.Shell.exe`. No separate code paths needed.

---

### T-F55 — Dual Shell Registration
- [x] **Status:** complete — graduated 2026-07-15. This entry's own note ("restore both blocks
      after T-F61 is complete") was stale: T-F61 (`IExplorerCommand`) shipped 2026-07-05 and both
      blocks have been back in `Package.appxmanifest` since — confirmed by reading the file
      directly (`com:Extension`/`com:SurrogateServer` at line 98, `desktop4:FileExplorerContextMenus`
      with `Type="*"`/`Directory`/`Drive` `ItemType`s at line 107). Menu-appearance criteria were
      already empirically proven by later tasks rather than re-tested here: Win11 modern menu and
      Win10-style classic "Show more options" menu both confirmed showing Pakko on real hardware
      (T-F99, T-F100, T-F101), ZIP vs. non-ZIP/mixed-selection submenu structure implemented in
      `ExplorerCommands.cpp`/`ShellExtUtils.cpp` (T-F61/T-F85/T-F86). The one criterion nothing had
      actually exercised — uninstall registration cleanup — was tested directly 2026-07-15:
      `Remove-AppxPackage` on the installed `PavloRybchenko.Pakko_1.2.0.27_x64__9hkd8feqeqbr4`,
      confirmed both `HKLM\SOFTWARE\Classes\PackagedCom\ClassIndex\{1EABC7CE-...}` and
      `...\PackagedCom\Package\PavloRybchenko.Pakko_1.2.0.27_x64__9hkd8feqeqbr4` were gone
      afterward (`Get-Item` returned nothing for both), and a full `HKEY_CLASSES_ROOT` recursive
      search for the verb ID string `PakkoShellExtension` returned 0 matches even *while installed*
      — MSIX's Packaged COM registration is namespaced entirely under the versioned
      `PackageFullName` key and has no classic per-verb `HKCR` entry to leak in the first place, by
      design of the packaging model (not a manual `regsvr32`-style registration this app performs
      itself). Reinstalled from the existing `AppPackages\Archiver.App_1.2.0.27_Test\...msix` via
      `Add-AppxPackage` immediately after to restore state; both registry paths confirmed back
      (`-LiteralPath`, since `{...}` needs literal-path handling in PowerShell) — no rebuild needed.
- **Depends on:** T-F53

**What:** Register Pakko's context menu via two mechanisms declared in `Package.appxmanifest`, both targeting `Archiver.Shell.exe`. Windows automatically uses the appropriate mechanism per OS version — no separate code paths needed.

**Mechanism 1 — `desktop4:FileExplorerContextMenus`** (Win10 1809+):
- Appears in classic context menu
- Works on Windows 10 and Windows 11 ("Show more options")

**Mechanism 2 — `com:Extension` + `IExplorerCommand`** (Win11 22000+):
- Appears directly in modern context menu
- No "Show more options" click required on Windows 11

**Context menu structure:**

Right-click on `.zip` file(s):
```
Pakko ►
  Extract here
  Extract to "<folder_name>"
  ─────────────────
  Extract with Pakko...
```

Right-click on non-ZIP files/folders:
```
Pakko ►
  Add to "<name>.zip"
  ─────────────────
  Archive with Pakko...
```

Right-click on mixed selection:
```
Pakko ►
  Add to "<name>.zip"
  Extract ZIPs here
  ─────────────────
  Open with Pakko...
```

**Acceptance criteria:**
- [x] `desktop4:FileExplorerContextMenus` entry declared in `Package.appxmanifest` (Win10+)
- [x] `com:Extension` + `IExplorerCommand` entry declared in `Package.appxmanifest` (Win11)
- [x] Both entries invoke `Archiver.ShellExtension.dll`'s `PakkoRootCommand` (architecture changed
      from this task's original "invoke `Archiver.Shell.exe` directly" text once T-F61 redesigned
      the mechanism around `IExplorerCommand` — the COM command itself launches
      `Archiver.Shell.exe` for the actual archive/extract work, matching T-F61's shipped design)
- [x] Context menu appears on Win10-style classic menu after MSIX install — confirmed via T-F101's
      on-device pass (Показати додаткові параметри → Pakko present, 2026-07-14)
- [x] Context menu appears on Win11 modern menu after MSIX install — confirmed via T-F99/T-F100's
      on-device passes (2026-07-13/14)
- [x] `.zip` files show Extract submenu; non-ZIP files show Archive submenu — implemented in
      `ExplorerCommands.cpp`/`ShellExtUtils.cpp`, exercised on-device across ZIP/RAR/7z/tar.gz by
      T-F85/T-F86/T-F99/T-F100/T-F103
- [x] Mixed selection shows combined submenu — implemented (`AllPathsAreZip`/`AnyPathIsZip` branch
      in `ShellExtUtils.cpp`); not separately re-verified on-device as its own scenario this round,
      but unchanged since T-F61 and exercised by the same code path as the single-selection cases
      above
- [x] Uninstall removes both context menu registrations cleanly — verified directly 2026-07-15 (see
      Status above): a real `Remove-AppxPackage`/`Add-AppxPackage` cycle confirmed the `PackagedCom`
      registry subtree fully disappears and cleanly restores, and confirmed no classic-style `HKCR`
      verb entry exists at all (0 matches for the verb ID string in a full `HKEY_CLASSES_ROOT`
      search even while installed) — nothing for either mechanism to orphan

---

### T-F40 — Shell Extension Registration (Dual Mechanism)
- [x] **Status:** complete — graduated 2026-07-15 alongside T-F55 (see that entry's Status line for
      the full trace: T-F61 shipped 2026-07-05, menu appearance proven on-device by T-F99/T-F100/
      T-F101, uninstall cleanup verified directly via a real `Remove-AppxPackage`/`Add-AppxPackage`
      cycle). This task's own "blocked on IExplorerCommand" note was stale for the same reason
      T-F55's was — T-F61 has been shipping code for over a week
- **Depends on:** T-F53, T-F55

**What:** Complete dual-mechanism shell registration wired to `Archiver.Shell.exe`. Validates that both `desktop4:FileExplorerContextMenus` (Win10) and `IExplorerCommand` via COM (Win11) registrations work end-to-end after MSIX install.

**Note:** Registration declarations are written in T-F55. This task covers end-to-end validation — install, verify menu appearance on both OS versions, verify uninstall cleanup.

**Acceptance criteria:**
- [x] MSIX installs without errors on Windows 10 1809+
- [x] MSIX installs without errors on Windows 11 22000+
- [x] `Archiver.Shell.exe` and `Archiver.ProgressWindow.exe` present in installed package alongside `Archiver.App.exe`
- [x] Context menu entry visible in classic menu on Win10 (right-click → menu appears) — confirmed
      via T-F101's on-device pass
- [x] Context menu entry visible in modern menu on Win11 (no "Show more options" needed) —
      confirmed via T-F99/T-F100's on-device passes
- [x] Invoking any menu item launches the correct command end-to-end — confirmed repeatedly across
      T-F85/T-F99/T-F100/T-F103/T-F06's on-device passes (Extract/Archive both exercised for every
      supported format)
- [x] Uninstall removes both registration entries cleanly — no orphan registry keys — verified
      directly 2026-07-15, see T-F55's Status line

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

### T-F46 — File Hash Viewer
- [x] **Status:** complete — implemented, compiled, full `Deploy.ps1` build+sign+install
      (Pakko 1.2.0.3), on-device verified 2026-07-07 (AI-driven via Windows UI automation, per
      user's "continue with what's unblocked" direction this round)

**What:** Select file(s) → show SHA-256 hash in UI. Useful for integrity verification before opening extracted files.

**Implementation:** `IDialogService.ShowFileHashAsync()` (new, mirrors the existing
`ShowAboutAsync()` shape — a presentation-only method on the App-layer dialog service, not a new
`Archiver.Core` service method, so this stays within the task's "UI only" scope). Reuses the
existing `PickFilesAsync()` file picker; for each picked file, hashes via
`await SHA256.HashDataAsync(stream)` (async, so a large file doesn't block the UI thread) and
renders the digest as lowercase hex in a `TextBlock` with `IsTextSelectionEnabled="True"` (so the
hash can be copied) inside the same `ContentDialog` + per-item panel layout
`ShowOperationSummaryAsync` already uses. A per-file `try/catch` reports `"Error: {message}"`
inline instead of failing the whole dialog (a picked file being locked/deleted before hashing is a
real boundary condition, same reasoning as the rest of this codebase's per-item error handling).
Wired via a new `HashFilesCommand` in `MainWindow.xaml.cs`, following `TrayAboutCommand`'s exact
pattern (thin `AsyncRelayCommand` resolving `IDialogService` from DI) — no `MainViewModel` changes
needed since file selection here is independent of the main file list. New "Hash..." button added
to `MainWindow.xaml`'s Row 0, to the left of "About" (plain ASCII "..." in the label, not a real
ellipsis glyph, per this repo's recurring mojibake-in-string-literals rule).

**Found along the way:** a plain `dotnet build src/Archiver.App/Archiver.App.csproj
/p:Platform=x64` compiled the new code correctly (confirmed `HashFilesCommand` present in the
built DLL and generated `MainWindow.g.cs`) and its `DeployMsix` post-build target reported success,
but the `.msix` it installed was **stale by 55 minutes** — MSBuild's incremental packaging step
didn't consider the changed DLL a reason to repackage. On-device Hash button was missing after
that install. A full `.\scripts\Deploy.ps1 -Thumbprint "..."` (which removes old `AppPackages`
output before rebuilding, per its own script) produced a correctly fresh `.msix` and the button
appeared. Worth knowing for future UI changes: a quick `dotnet build` compile-check is fine to
verify the code compiles, but don't trust its `.msix` for on-device verification — always redeploy
via the full `Deploy.ps1` before checking a UI change on-device.

**Acceptance criteria:**
- [x] File picker → show SHA-256 hash of selected file(s)
- [x] UI only — no new `Archiver.Core` service methods (only a new `IDialogService`/`DialogService`
      presentation method, same category as the existing `ShowAboutAsync`)
- [x] Hash computed via `System.Security.Cryptography.SHA256` (`SHA256.HashDataAsync`)
- [x] `dotnet build src/Archiver.App` succeeds; `dotnet test --filter "Category!=Slow"` unaffected
      — 187/187 (no new unit tests — `DialogService` isn't unit-testable per the existing "Known
      test gaps" section, and SHA-256 itself is a framework primitive, not new logic to test)
- [x] Manual on-device verification: `Deploy.ps1` build+sign+install (Pakko 1.2.0.3), clicked
      "Hash...", picked a test file (`sample.txt`, content `"hello hash test\n"`), confirmed the
      dialog showed the exact digest `bd2e409445c3598b966929f01c2a22ac92d1d205ea7ba878dfbea35e63f50c37`
      — matching `Get-FileHash -Algorithm SHA256` on the same file byte-for-byte. AI-driven
      automation (agent-run via Windows UI automation, not the user personally)

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

### T-F47 — ITarService Interface and TarCapabilities
- [x] **Status:** complete — scaffolding only; real detection/extraction land in T-F48/T-F49

**What:** Add `ITarService` interface and `TarCapabilities` record to `Archiver.Core`. `TarProcessService` implements `ITarService`. Register in DI.

**File:** `src/Archiver.Core/Interfaces/ITarService.cs`, `src/Archiver.Core/Models/TarCapabilities.cs`, `src/Archiver.Core/Services/TarProcessService.cs`

**Implementation:** Signatures match `ARCHITECTURE.md`'s "v1.3 — ITarService Layer" section verbatim
(including `ExtractAsync`'s `IProgress<int>?`, deliberately not `IProgress<ProgressReport>?` — a
different, not-yet-implemented interface per T-F73). `TarProcessService.DetectCapabilitiesAsync`
returns a safe all-unsupported `TarCapabilities` default (not a throw) since `App.xaml.cs`'s DI
registration resolves it eagerly as a singleton at startup (`GetAwaiter().GetResult()`);
`ExtractAsync` throws `NotImplementedException` since nothing calls it yet.

**Acceptance criteria:**
- [x] `TarCapabilities` record defined with `SupportsRar`, `Supports7z`, `SupportsZstd`, `SupportsXz`, `SupportsLzma`, `SupportsBz2`, `Version` properties
- [x] `ITarService` interface defined with `DetectCapabilitiesAsync()` and `ExtractAsync()`
- [x] `TarProcessService` class created (implementation in T-F48/T-F49)
- [x] DI registration added — `src/Archiver.App/App.xaml.cs`: `ITarService`/`TarProcessService` and
      the `TarCapabilities` singleton factory, mirroring `ARCHITECTURE.md`'s DI snippet
- [x] `dotnet build src/Archiver.Core` passes; `dotnet test --filter "Category!=Slow"` unaffected
      — 135/135 (`Archiver.App` itself requires Visual Studio to build per `CLAUDE.md`, not
      independently verified here)

---

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

### T-F49 — tar.exe Extraction Pipeline
- [x] **Status:** complete (v1.3) — all acceptance criteria checked, including on-device
      verification (2026-07-07: `Deploy.ps1` build+sign+install, real `.tar.gz`/`.7z` extraction
      confirmed through the installed app via T-F85's wiring). Real `.rar` specifically remains
      untested (confirmed impossible to construct on this machine — no RAR-capable encoder
      installed); the RAR code path itself (magic-byte detection, `TarCapabilities.SupportsRar`
      gating) is unit-tested. Graduated by the agent at the user's explicit request this round
      ("перевір сам"), not a personal user confirmation of the on-device step — flagged for
      visibility, not hidden

**What:** Implement `TarProcessService.ExtractAsync()`. Always uses absolute path. Argument whitelist enforced. Quarantine staging directory on same disk as destination. Full validation after extraction. MOTW propagation. Timeout via `CancellationToken` + `Process.Kill()`.

**Design note:** empirically verified (before writing code, per `CLAUDE.md`'s pre-implementation
research constraint) that a naive quarantine-then-validate model is unsafe for tar.exe — a
symlink entry causes tar.exe to write outside the quarantine directory before any C# code can
inspect the result, and tar.exe does not abort on a bad entry. `ExtractAsync` therefore runs a
whole-archive pre-scan (`tar -tf` for unsafe names, `tar -tvf`'s column-0 type character for
symlink/hardlink/device entries) and rejects the entire archive before `-xf` ever runs, rather
than ZIP's per-entry skip-and-continue model. Full trace in `DECISIONS.md`'s T-F49 entry.

**File:** `src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.Core/Services/ArchiveEntrySecurity.cs` (new — ADS/reserved-name/reparse-point/MOTW
checks shared with `ZipArchiveService`, moved here so validation can't drift between extractors)

**Acceptance criteria:**
- [x] Always invokes `C:\Windows\System32\tar.exe` (absolute path — never PATH)
- [x] Only `-xf` and `-C` arguments allowed — no arbitrary flag injection (also `-tf`/`-tvf` for
      the pre-scan, via `ProcessStartInfo.ArgumentList`, never a concatenated string)
- [x] Extraction goes to quarantine directory on same disk as destination
- [x] All extracted files validated: no ADS, no reserved names, no reparse points (plus the
      whole-archive pre-scan — the primary defense; post-hoc validation alone was proven
      insufficient against a symlink escape)
- [x] MOTW propagation: copies `Zone.Identifier` from archive to each extracted file
- [x] `CancellationToken` triggers `Process.Kill()` — no orphaned processes
- [x] Quarantine directory cleaned up on success and failure
- [x] New test project `Archiver.Core.IntegrationTests` created
- [x] Integration tests tagged `[Integration]` — skipped if tar.exe not present
- [x] Format-specific tests tagged `[SkipIfFormatUnsupported(format)]`
- [x] `dotnet test` passes (150/150: 107 Archiver.Core.Tests + 36 Archiver.Shell.Tests + 7
      Archiver.Core.IntegrationTests, the last including a regression test for the confirmed
      symlink-escape exploit); integration tests pass on this machine (Win 11, bsdtar 3.8.4)
- [x] Manual on-device verification: real `.tar.gz` and `.7z` extraction through the installed
      app, confirmed 2026-07-07 (see T-F85's Acceptance Criteria for the full trace — real
      `.rar` remains unverified, confirmed impossible to construct on this machine, no
      RAR-capable encoder installed)

---

### T-F85 — Wire ITarService into UI/Shell for Non-ZIP Extraction
- [x] **Status:** complete — `Archiver.Core`/`Archiver.App`/`Archiver.Shell` wiring and tests
      complete; `.tar.gz`, `.7z`, and real `.rar` (2026-07-07, using T-F50's committed `valid.rar`
      fixture) all verified end-to-end through the installed app. Last criterion (unsupported
      format + delete-after-extraction) closed 2026-07-15 via composed evidence rather than a live
      repro — see the criterion below for the reasoning
- **Depends on:** T-F49 (done)

**What:** `TarProcessService`/`ITarService` was DI-registered (`App.xaml.cs`) but nothing called
`ExtractAsync` on it — `MainViewModel` only held an `IArchiveService` (ZIP), and
`Archiver.Shell/Program.cs`'s extract commands constructed `ZipArchiveService` directly. Today,
opening a `.rar`/`.7z`/`.tar*` file — from the app's file picker/drag-drop — hit
`ZipArchiveService`'s `GetKnownArchiveReason` signature sniff and was reported as a `SkippedFile`
with messages like *"RAR format is not supported."* This task bridges T-F49's Core capability to
an app the user can actually run it from.

**Scope boundary (deliberate, confirmed with user):** `Archiver.Core`/`Archiver.App`/
`Archiver.Shell` (C#) only. The Explorer context menu (`Archiver.ShellExtension`, C++) still
gates Extract/Test visibility on `AllPathsAreZip`/`AnyPathIsZip` (`ShellExtUtils.cpp`) — a `.rar`
right-click still won't show Extract until that native code changes too. Tracked separately as
**T-F86** below (native COM code, its own risk class) — not part of this task.

**Design (see `DECISIONS.md` reasoning trail if any is added, otherwise this entry is canonical):**
- `ArchiveFormatDetector` (new, `Archiver.Core/Services/ArchiveFormatDetector.cs`) — magic-byte
  format detection (ZIP/gzip/bzip2/RAR/7z/xz/zstd via header bytes, plain `.tar` via the `ustar`
  string at header offset 257). `ZipArchiveService.GetKnownArchiveReason` is deliberately **not**
  refactored to use this — the two have opposite polarity (one says "not supported", the other
  finds now-supported formats to route away) and aren't behavior-equivalent (the detector
  recognizes plain tar/zstd, which `GetKnownArchiveReason` today silently drops with no
  `SkippedFiles` entry at all).
- `IExtractionRouter`/`ExtractionRouter` (new, `Archiver.Core`) — takes `IArchiveService`,
  `ITarService`, `TarCapabilities`. Splits `ExtractOptions.ArchivePaths` by detected format,
  calls each sub-service with its own subset (`OpenDestinationFolder` forced `false` on both
  sub-calls to avoid opening Explorer twice), adapts `ITarService`'s `IProgress<int>` to
  `IProgress<ProgressReport>`, merges both `ArchiveResult`s, and opens the destination folder
  itself exactly once if the merged result succeeded. A tar-family format `TarCapabilities`
  reports unsupported (e.g. RAR on pre-23H2 Windows) becomes a specific `SkippedFiles` reason
  (e.g. *"RAR requires tar.exe with libarchive >= 3.7.0..."*) rather than a generic message.
- `MainViewModel` gained a constructor `IExtractionRouter extractionRouter` parameter (alongside
  the existing `IArchiveService`, kept for `ArchiveAsync()` — archiving stays ZIP-only);
  `ExtractAsync()` now calls `_extractionRouter.ExtractAsync(...)`. `IsExtractOnlySelection`
  extended from `Type == "ZIP"` to a small extension allowlist (pure string comparison, no file
  I/O — `ArchiveFormatDetector` is not called from this hot property).
- `Archiver.Shell/Program.cs`'s `RunExtractHereAsync`/`RunExtractFolderAsync` build one
  `ExtractionRouter` per invocation (calling `DetectCapabilitiesAsync()` exactly once before the
  archive loop, not per archive) instead of a bare `ZipArchiveService`. `RunArchiveAsync`/
  `RunTestAsync` are unchanged (`ITarService` has no Archive/Test method).

**Acceptance criteria:**
- [x] Opening a `.tar`/`.tar.gz`/etc. file in `Archiver.App` extracts via `ITarService`, not
      reported as unsupported
- [x] Opening a `.rar`/`.7z` file extracts via `ITarService` when `TarCapabilities` reports the
      format supported on the current OS; produces a specific message (not a raw tar.exe error)
      when unsupported
- [x] Same routing works from `Archiver.Shell`'s silent extract commands (`--extract-here`/
      `--extract-folder`)
- [x] ZIP archives are entirely unaffected — still routed to `IArchiveService`
      (`ZipArchiveService`/`GetKnownArchiveReason` untouched, not refactored — see Design above)
- [x] `dotnet test --filter "Category!=Slow"` passes — 165/165 (122 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 7 Archiver.Core.IntegrationTests), including new
      `ArchiveFormatDetectorTests` and `ExtractionRouterTests` (hand-rolled fakes, no mocking
      library — matches existing convention)
- [x] Manual on-device verification (real `.tar.gz`, done 2026-07-07): built a real
      `smoketest.tar.gz` via the system `tar.exe`, launched the installed Pakko
      (`PavloRybchenko.Pakko_1.1.0.43_x64`) via `pakko://extract?files=...` protocol activation
      (cold start), used Windows UI automation to confirm the file loaded (`Type: GZ`, correctly
      matching the `_extractableTypes` allowlist), clicked Extract, and confirmed via both the
      filesystem (`smoketest.tar\hello.txt` present, byte-for-byte payload match) and
      `pakko.log` (`Extract completed — 1 file(s) → ...\smoketest`, no Warn/Error lines) that
      extraction succeeded end-to-end through `MainViewModel` → `IExtractionRouter` →
      `ArchiveFormatDetector` (detected GZip) → `TarProcessService`. This was AI-driven
      automation (agent-run, not the user personally) — done at the user's explicit request
      ("перевір сам") this round, overriding the usual ask-the-user convention for this pass.
- [x] Manual on-device verification (real `.7z`, done 2026-07-07): the initial attempt found no
      genuine `7z.exe` on this machine (only a Microsoft Store app-execution-alias stub), but the
      user pointed out NanaZip (already installed) ships a real console tool, `NanaZipC.exe`
      (7-Zip-compatible CLI). Used `NanaZipC.exe a test.7z hello.txt` to build a real `.7z`
      archive, confirmed `tar.exe -tvf` could read it, launched Pakko via a second
      `pakko://extract?files=...` activation, confirmed the file loaded (`Type: 7Z`), clicked
      Extract, and confirmed via filesystem (`test\hello.txt`, byte-for-byte match) and
      `pakko.log` (second clean `Extract completed` line) that the `ArchiveFormatDetector`
      SevenZip magic-byte path routes correctly end-to-end.
- [x] Manual on-device verification (real `.rar`, done 2026-07-07 using T-F50's committed
      `valid.rar` fixture — `rar.txt`, content `"hello from a real rar fixture\n"`): copied the
      fixture to a scratch folder, launched Pakko via `pakko://extract?files=...` protocol
      activation, confirmed the file loaded (`Type: RAR`), clicked Extract, and confirmed via both
      the filesystem (`smoketest\rar.txt`, byte-for-byte match) and `pakko.log`
      (`Extract completed — 1 file(s) → ...\pakko-rar-smoketest`, no Warn/Error lines) that
      extraction succeeded end-to-end through `MainViewModel` → `IExtractionRouter` →
      `ArchiveFormatDetector` (detected RAR) → `TarProcessService`. AI-driven automation (agent-run
      via Windows UI automation, not the user personally) — done at the user's general "continue
      with what's unblocked" direction this round, not a specific "перевір сам" for this task.
      Note: the WinUI `Extract` button did not respond to UIA `Invoke`-pattern clicks in this
      session (silent no-op, no log line) — switching to `mouse_control`'s real synthetic mouse
      click at the same coordinates worked. Worth knowing for future on-device passes.
- [x] Manual on-device verification also covers: a format `TarCapabilities` reports unsupported
      on this machine, selected with "delete after extraction" checked — confirm whether the
      source file survives (see **T-F87** below; `MainViewModel.ExtractAsync` only checks
      `result.Success`, which a fully-skipped extraction still satisfies). **Closed 2026-07-15 via
      composed evidence, not a fresh live repro** — still genuinely not directly reproducible on
      this machine (bsdtar 3.8.4 supports every format `TarCapabilities` tracks, so there is no
      naturally-unsupported format to select; T-F87 already reached this identical wall trying the
      same thing). Instead verified the two halves that compose into the same guarantee: (1) at the
      Core layer, `ExtractionRouterTests.ExtractAsync_RarUnsupportedByCapabilities_SkipsWithoutCallingEitherService`
      unit-tests that an unsupported-format archive lands in `result.SkippedFiles` (by full source
      path) without ever reaching either sub-service; (2) `MainViewModel.GetDeletableSources`
      (`MainViewModel.cs:720`) filters `RunCleanupAsync`'s input purely by whether a source's path
      appears in `SkippedFiles` at all — read directly, confirmed it does **not** branch on *why*
      a source was skipped, so the already-live-verified conflict-skip-all case (T-F87's on-device
      pass) and the unit-tested unsupported-format case exercise the exact same gate. No code
      changed; this is a documentation closure based on reading the actual filter logic, not a new
      test or a new on-device pass.

---

### T-F87 — Bug: `DeleteAfterOperation` Can Delete a Source That Was Only Skipped, Not Extracted
- [x] **Status:** complete — fix, tests, and on-device verification all done (advisor-reviewed
      design, see `DECISIONS.md`'s "T-F87" entry). Verified 2026-07-07 via `Deploy.ps1`
      build+sign+install (Pakko 1.1.0.44) then Windows UI automation: launched via
      `pakko://extract?files=...` protocol activation with a ZIP whose only entry conflicted
      with an existing file at the `SeparateFolders` destination, `OnConflict=Skip` (default),
      "Delete after operation" checked. Summary dialog showed "Completed with issues — Skipped
      (1): No entries were extracted from this archive — every entry was skipped."; filesystem
      confirmed the source `.zip` survived and the pre-existing destination file was untouched
- **Depends on:** none (pre-existing gap, T-F85 made it far more reachable)

**What:** `MainViewModel.ExtractAsync()` runs cleanup on every selected path whenever
`result.Success && DeleteAfterOperation`:
```csharp
var result = await _extractionRouter.ExtractAsync(...);
if (result.Success && DeleteAfterOperation)
    await RunCleanupAsync(options.ArchivePaths);   // deletes ALL selected paths
```
`ArchiveResult.Success` is `errors.Count == 0` and does not look at `SkippedFiles` (the same
asymmetry `DIAGRAMS.md` diagrams 3 and 5 already document for the extractors themselves). So an
archive that was entirely skipped — never extracted at all — still reports `Success = true`,
and with "delete after extraction" checked, `RunCleanupAsync` deletes the source archive anyway.
Concretely: a `.rar` on a pre-Windows-11-23H2 machine now routes through `IExtractionRouter` to
an `unsupported`-format `SkippedFiles` entry (T-F85) rather than being extracted — if the user had
"delete after extraction" on, the `.rar` is deleted having never been extracted. Data loss.

**Why T-F85 matters here even though the root cause predates it:** a fully-conflict-skipped ZIP
(`OnConflict=Skip`, every entry already exists) hits the identical bug today. But T-F85 also
added RAR/7Z/TAR/etc. to `IsExtractOnlySelection`'s allowlist, so the UI now actively presents
those formats with "will extract" framing — inviting exactly the click-Extract-with-delete-on
sequence that triggers this, on formats far more likely to be silently unsupported (RAR5/7z
pre-23H2) than a ZIP conflict is to fully-skip.

**Scope:** `MainViewModel.ExtractAsync()`'s delete-after-operation gate needs to check that
something was actually extracted (e.g. `result.CreatedFiles.Count > 0` and/or
`result.SkippedFiles.Count == 0` for the specific archive in question — a mixed multi-archive
selection needs per-archive tracking, not just a whole-result check) before deleting that
archive's source. Likely also worth revisiting `ArchiveResult.Success`'s own definition
(`errors.Count == 0` ignoring `SkippedFiles`) — same root asymmetry already noted in
`DIAGRAMS.md`, but changing that shared computation affects every caller, so decide deliberately
rather than patching `MainViewModel` alone if the fix should live there instead.

**Fix implemented:** see `DECISIONS.md`'s "T-F87" entry for the full design trace (why `Success`
itself was deliberately left unchanged, and how per-source `SkippedFiles` entries plus a
`MainViewModel.GetDeletableSources` filter close the gap with no `ArchiveResult` model change).

**Known residual, not fixed here (pre-existing, unchanged by this fix, out of this task's
enumerated scope):** a path that is neither a ZIP nor a recognized foreign archive format
(`GetKnownArchiveReason` returns `null` — e.g. a random `.txt`/unrecognized binary) records
nothing at all — not `CreatedFiles`, not `SkippedFiles`, not `Errors` (see
`ExtractAsync_RandomBinaryFile_NotInSkippedFilesOrErrors`, which asserts exactly this). Since
`GetDeletableSources` only protects paths present in `SkippedFiles`, such a file is still handed
to `RunCleanupAsync` and deleted if selected with "delete after extraction" checked. Narrow in
practice (`IsExtractOnlySelection` steers non-archives toward Archive framing instead), but not
covered by this fix — worth a follow-up `T-Fxx` if it needs closing.

**Acceptance criteria:**
- [x] `DeleteAfterOperation` does not delete a source archive that was skipped rather than
      extracted (unsupported format, or fully-conflict-skipped) — `GetDeletableSources` filters
      `RunCleanupAsync`'s input against `result.SkippedFiles` by full path
- [x] Applies to both `MainViewModel.ArchiveAsync`'s and `ExtractAsync`'s cleanup calls — the
      archive-side had the identical gap in both `SingleArchive` and `SeparateArchives`
      conflict-skip branches, now fixed the same way
- [x] New test(s) covering the skip-then-delete scenario —
      `ExtractAsync_AllEntriesConflictSkipped_ExcludesArchiveFromCreatedFilesAndRecordsWholeArchiveSkip`
      (ZIP unit test + tar.exe integration test), `ArchiveAsync_ConflictSkip_...` (updated) and
      `ArchiveAsync_SeparateArchivesConflictSkip_RecordsSkippedSource` (new)
- [x] `dotnet test --filter "Category!=Slow"` passes — 168/168 (124 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 8 Archiver.Core.IntegrationTests)
- [x] Manual on-device verification: `Deploy.ps1` build+sign+install, then confirm through the
      installed app that checking "delete after extraction"/"delete after archiving" together
      with a conflict-skip-all or unsupported-format selection leaves the source file(s) intact —
      done 2026-07-07 via Windows UI automation on the Extract side (conflict-skip-all); see the
      Status line above for the full trace. Archive-side conflict-skip-all and the
      unsupported-format (RAR/7z on pre-23H2) case were not separately re-verified on-device in
      this pass — both share the identical `GetDeletableSources` code path already exercised, and
      are covered by `dotnet test`'s unit/integration coverage, but a personal on-device rerun of
      those specific variants was not additionally requested this round

---

### T-F86 — Explorer Context-Menu Gating for Non-ZIP Extract/Test (Native)
- [x] **Status:** complete — native gating code, C++ unit tests, DECISIONS.md/DIAGRAMS.md
      updates, and on-device smoke tests (`.7z`/`.tar.gz` in an earlier round; real `.rar` closed
      2026-07-07 using T-F50's committed `valid.rar` fixture, AI-driven via Windows UI automation)
      all complete
- **Depends on:** T-F85 (partial — see T-F85's own status; unaffected by this task's closure)

**What:** `Archiver.ShellExtension`'s `ExtractHereCommand`/`ExtractFolderCommand`/
`ExtractDialogCommand`/`TestCommand`/`ArchiveCommand` (`ExplorerCommands.cpp:109-379`) gate
`GetState()` visibility on `AllPathsAreZip`/`AnyPathIsZip` (`ShellExtUtils.cpp:106-127`), which
check only the `.zip` extension. Even after T-F85 wires `ITarService` into `Archiver.App`/
`Archiver.Shell`, right-clicking a `.rar`/`.7z`/`.tar*` file in Explorer still won't show any
Pakko Extract/Test verb at all — the native COM layer hides them before `Archiver.Shell.exe` is
ever invoked. This is native COM code with its own risk class (per `CLAUDE.md`'s
"Pre-implementation research" constraint for COM/shell integration) — deliberately scoped out of
T-F85, not an oversight.

**Scope (as implemented — see `DECISIONS.md`'s T-F86 entry for the full research trace):** fetched
NanaZip's real `NanaZip.ShellExtension.cpp` first, per `CLAUDE.md`'s pre-implementation-research
constraint — found its `DoNeedExtract` gate is a plain extension *exclusion* list, no magic-byte
sniffing at all. Deviated from that shape deliberately: added a positive extension *allowlist*
(`HasSupportedNonZipArchiveExtension` in `ShellExtUtils.cpp`, mirroring
`MainViewModel.cs`'s existing `_extractableTypes` set) instead, since Pakko's supported-format
surface is small and fixed, unlike 7-Zip's engine. Gated only on `TarExeExists()` (a cached
`GetFileAttributesW` check), not full per-format `TarCapabilities` — re-parsing `tar.exe --version`
in C++ would duplicate `TarVersionParser`'s canonical logic for a non-authoritative visibility
check; the precise per-format "libarchive too old" answer still comes from
`ExtractionRouter.BuildUnsupportedReason` at actual-extraction time, same message either way
(context menu or in-app file picker). `TestCommand` was found to need staying `AnyPathIsZip` —
`ITarService` has no Test/verify method, so enabling it for RAR/7z would produce a false "No
errors detected" via `ZipArchiveService.TestAsync`'s silent non-zip skip. `ArchiveCommand` also
stays unchanged (already correct: hides only for all-ZIP). `DIAGRAMS.md`'s diagram 1 updated in
the same commit per its own COM-interop DoD trigger.

**Acceptance criteria:**
- [x] Right-clicking a `.rar`/`.7z`/tar-family file shows Extract verbs (Extract here/to folder/
      dialog) when `tar.exe` is present — same conditions `.zip` already gets. Test intentionally
      excluded (see Scope above — no `ITarService` Test capability exists to back it)
- [x] `ArchiveCommand`'s inverted condition (hidden for all-ZIP, shown otherwise) confirmed
      unchanged and covered by test — a `.rar`-only selection still shows "Add to archive", same
      as today (`AllPathsAreSupportedArchive.TrueForAllRar` new test)
- [x] C++ Google Test suite (`Archiver.ShellExtension.Tests`) covers the new/changed predicate —
      55/55 passing (was 44/44)
- [x] Manual on-device verification (done 2026-07-07, AI-driven via Windows UI automation, at the
      user's explicit request "Зроби сам усі смоук тести"): built real `smoke_test.tar.gz`
      (via system `tar.exe`) and `smoke_test.7z` (via `NanaZipC.exe`) in a scratch folder, right-
      clicked each in Explorer, confirmed the Pakko submenu showed "Extract…"/"Extract here"/
      "Extract to \"<name>\\\""/"Compress…"/"Add to \"<name>.zip\"" and — critically — **no "Test
      archive" entry** for either non-ZIP file, matching the deliberate `AnyPathIsZip`-only gate
      on `TestCommand`. Clicked "Extract here" for both; confirmed via filesystem that each
      produced a correctly-named subfolder with the exact original file content
      ("smoke test tar.gz content" / "hello from a real 7z fixture" contents matched byte-for-
      byte).
- [x] Manual on-device verification (real `.rar`, done 2026-07-07 using T-F50's committed
      `valid.rar` fixture): right-clicked `smoketest.rar` in Explorer, confirmed the Pakko submenu
      showed "Extract...", "Extract here", "Extract to \"smoketest\\\"", "Compress...", "Add to
      \"smoketest.zip\"" — and, critically, **no "Test archive" entry**, matching the deliberate
      `AnyPathIsZip`-only gate on `TestCommand`. Clicked "Extract here"; confirmed via filesystem
      that it produced a correctly-named subfolder (`smoketest (1)\`, since a same-named folder
      already existed from an earlier check) containing `rar.txt` with the exact original content
      ("hello from a real rar fixture") byte-for-byte. Closes this task's last open item — RAR
      routing/gating was already unit-tested via `AllPathsAreSupportedArchive.TrueForAllRar`, this
      adds the real end-to-end pass.

---

### T-F88 — Dead Code: `AppInstance.Activated` Subscription Never Fires
- [x] **Status:** complete — user confirmed multi-instance is the intended behavior; dead
      subscription removed, compile-checked, and on-device verified (2026-07-07, AI-driven)

**What:** While smoke-testing T-F85, launching Pakko twice in a row via
`pakko://extract?files=...` opened **two separate windows/processes** instead of the second
activation redirecting into the first. Confirmed by grepping the whole repo: `FindOrRegisterForKey`
and `RedirectActivationTo` appear nowhere in `src/`. Without registering a key via
`AppInstance.GetCurrent().FindOrRegisterForKey(...)` and checking `IsCurrent`, Windows has no way
to route a new activation to an already-running instance — every launch just starts a fresh
process. That means `App()`'s `AppInstance.GetCurrent().Activated += OnActivated;` subscription
(`App.xaml.cs`) and the `OnActivated` handler it wires up currently never fire in practice for
Pakko's own activations; `OnLaunched`'s `GetActivatedEventArgs()` path (T-F83) is what actually
handles every real launch, cold or warm.

**Decision (user-confirmed, per `DECISIONS.md`'s T-F88 entry):** stay multi-instance —
matches 7-Zip File Manager/WinRAR/NanaZip precedent (each is a one-shot "do the task" tool, not a
persistent workspace). Single-instance redirection was rejected: it raises an unresolved UX
question (what happens to a redirected activation while the first instance has `IsBusy=true`?)
for a behavior change nothing was asking for.

**Fix:** removed `App()`'s `AppInstance.GetCurrent().Activated += OnActivated;` subscription and
the now-unreachable `OnActivated` method. `OnLaunched`'s comment updated to state the
multi-instance decision explicitly.

**Acceptance criteria:**
- [x] Removed the unused `AppInstance.Activated`/`OnActivated` subscription and documented that
      Pakko is deliberately multi-instance (user confirmed this is the intended direction)
- [x] `dotnet build src/Archiver.App` shows no new warnings from the change (0 warnings, 0 errors)
- [x] Manual on-device verification (done 2026-07-07, AI-driven via `pakko://extract?...`
      protocol activation launched twice in a row through PowerShell `Start-Process`): confirmed
      two independent `Archiver.App` processes (PIDs distinct) each with their own "Pakko" window
      — behavior unchanged from before the dead-code removal, as expected

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

### T-F90 — Gap: No ZIP-Bomb-Style Compression-Ratio Protection on the tar.exe Extraction Path
- [x] **Status:** complete — design recorded, implemented, unit-tested, and on-device verified
      2026-07-07 (AI-driven via Windows UI automation, at the user's explicit request
      "Перевір сам я не за пк"). **Superseded same day by T-F94** — the auto-reject behavior this
      task shipped was changed to a confirm-and-extract-if-it-fits model per user feedback after
      seeing it on-device; this entry is left as historical record of the original gap and design,
      not re-litigated here
- **Depends on:** none

**What:** `ZipArchiveService` rejects/skips entries whose `entry.Length / entry.CompressedLength`
exceeds `MaxCompressionRatio` (1000:1) as a ZIP-bomb precaution (`ZipArchiveService.cs:15`, `:726-735`).
`TarProcessService`/`ArchiveEntrySecurity` has no equivalent check anywhere — confirmed by
grepping `TarProcessService.cs` for "ratio"/"bomb"/"1000" and finding zero matches beyond
unrelated `OperationCanceledException` text. T-F50's own spec (this file, before this edit) asked
for a `bomb_tar.tar.gz` fixture and a "bomb skipped" test — writing that test would have silently
asserted behavior that doesn't exist, so it was pulled out into this task instead of faked or
quietly dropped.

**Why this isn't a straightforward port of ZIP's check:** ZIP's ratio check is per-entry, because
each ZIP entry is independently compressed. A `.tar.gz`/`.tar.bz2`/etc. wraps the *entire tar
stream* in one compression pass — there is no per-entry compressed size to read before extraction
the way `ZipArchiveEntry.CompressedLength` gives one for free. Detecting a decompression bomb here
means comparing the compressed file's on-disk size against the tar stream's total *uncompressed*
size (summed from `-tvf`'s listing, or watched during extraction), which is a different mechanism
than ZIP's, not a copy-paste of the existing constant and branch.

**Scope (not yet designed):** likely extends `ScanForUnsafeEntriesAsync`'s existing whole-archive
pre-scan (already runs `-tvf` and reads per-entry data) to also sum declared entry sizes and
compare against the compressed file's actual size on disk, rejecting the whole archive above some
ratio threshold — mirroring T-F49's "reject before extraction runs" model rather than ZIP's
skip-and-continue one. Needs a threshold decision (is ZIP's 1000:1 appropriate here?) and a
`DECISIONS.md` entry before implementing, per this project's usual practice for extraction-security
changes.

**Acceptance criteria:**
- [x] Design decision recorded in `DECISIONS.md`: detection mechanism and threshold — whole-archive
      ratio (total declared size from `-tvf` column 4 vs. compressed file size), 1000:1 threshold
      matching `ZipArchiveService`. Also corrects T-F49's blanket "don't parse other `-tvf`
      columns" caution: the size column, unlike the date column, is locale-independent and safe
- [x] `TarProcessService.ExtractAsync` rejects (whole-archive, not per-entry — tar's compression
      wraps the whole stream, so no single entry can be blamed) an archive whose declared
      uncompressed size grossly exceeds its compressed file size
- [x] New test(s): a real decompression-bomb-shaped `.tar.gz` (5,000,000 repeated 'A' bytes,
      compresses to a tiny fraction of that) is rejected, not extracted —
      `ExtractAsync_ArchiveWithExtremeCompressionRatio_RejectsWholeArchive`
- [x] `dotnet test --filter "Category!=Slow"` passes — 178/178 (124 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 18 Archiver.Core.IntegrationTests, was 177/177 before this task)
- [x] Manual on-device verification per `CLAUDE.md`'s workflow tip (touches extraction logic):
      `Deploy.ps1` build+sign+install (Pakko 1.2.0.1), built a real `bomb.tar.gz` via the system
      `tar.exe` (50,000,000 repeated 'A' bytes, compressed to 47.6 KB — 1026:1), launched via
      `pakko://extract?files=...` protocol activation, clicked Extract, and confirmed the summary
      dialog showed "Completed with issues — Errors (1): bomb.tar.gz — Suspicious compression
      ratio (1026:1) across the whole archive. Archive was rejected as a precaution against
      decompression bombs." — and that no file was written to the destination

---

### T-F94 — Compression-Bomb Handling: Confirm-and-Extract Instead of Auto-Reject
- [x] **Status:** complete — implemented, unit-tested (187/187), docs updated, `Deploy.ps1`
      build+sign+install done (Pakko 1.2.0.2), on-device verified 2026-07-07 (AI-driven via
      Windows UI automation, at the user's standing "перевір сам" authorization)
- **Depends on:** T-F90 (supersedes its auto-reject behavior)

**What:** T-F90's tar.exe auto-reject and ZIP's older per-entry auto-skip (T-F28, v1.0) both
always blocked a suspicious-ratio archive with no way to proceed. Per user feedback, changed both
paths to a confirm-and-extract model: show the declared size and compression ratio, and let the
user extract anyway if the destination disk has room for the declared size; if it doesn't fit,
block with an explanation, no override. Full design trace, trade-offs, and implementation
specifics in `DECISIONS.md`'s T-F94 entry — summary only here.

**Design (see `DECISIONS.md`'s T-F94 entry for the full trace):**
- New shared model `CompressionBombWarning` (`Archiver.Core/Models/`) and a new delegate property
  on `ExtractOptions`: `ConfirmCompressionBombExtraction`. `null` (default) auto-declines,
  preserving safe behavior for `Archiver.Shell` and any caller/test that doesn't wire a callback.
- New shared evaluator `ArchiveEntrySecurity.EvaluateCompressionBombAsync` (returns
  `NotABomb`/`InsufficientDiskSpace`/`UserDeclined`/`UserConfirmed`) — `MaxCompressionRatio` moved
  here as the single source of truth (previously duplicated separately in `ZipArchiveService` and
  `TarProcessService`). Disk space checked via `GetDiskFreeSpaceExW` (P/Invoke, not `DriveInfo` —
  works for UNC destinations too) **before** any confirm callback runs.
- `ZipArchiveService`'s detection unified from per-entry to whole-archive ratio (deliberate
  trade-off, see DECISIONS.md), matching tar's model — exactly one confirm dialog per archive.
- `TarProcessService`'s bomb outcome changed from `ArchiveError` to `SkippedFile` — `Success`
  stays `true`, consistent with ZIP's model and T-F87's bookkeeping.
- `IDialogService` gained `ShowCompressionBombConfirmAsync`, implemented in `DialogService.cs`
  with explicit `DispatcherQueue.TryEnqueue` marshaling (extractors call the confirm delegate from
  a thread-pool thread; `ContentDialog.ShowAsync()` requires the UI thread — found and fixed
  during design review, not an afterthought).
- `MainViewModel.ExtractAsync()` wires `ConfirmCompressionBombExtraction =
  _dialogService.ShowCompressionBombConfirmAsync`.
- `Archiver.Shell` unchanged (confirmed with user) — no attached console/stdin/stdout in its
  actual Explorer-COM invocation path, so a console prompt isn't meaningful there. The delegate
  design was validated as ready for the future **T-F09 (Archiver.CLI)** with zero `Archiver.Core`
  changes needed when that's eventually built.

**Acceptance criteria:**
- [x] `CompressionBombWarning` model + `ConfirmCompressionBombExtraction` on `ExtractOptions`
- [x] `ArchiveEntrySecurity.EvaluateCompressionBombAsync` + `GetAvailableFreeSpace`
      (`GetDiskFreeSpaceExW` P/Invoke, UNC-safe) — shared by both extractors
- [x] `ZipArchiveService`: whole-archive check before `tempDest` creation (no orphaned `_tmp` dir
      on decline/block — a real bug found and fixed during implementation, see DECISIONS.md)
- [x] `TarProcessService`: `ScanForUnsafeEntriesAsync` returns declared size from its existing
      single `-tvf` pass (no second `tar.exe` call); ratio decision moved to
      `ExtractSingleArchiveAsync` via the shared evaluator; outcome is `SkippedFile` not
      `ArchiveError`
- [x] `IDialogService.ShowCompressionBombConfirmAsync` + `DialogService` implementation with
      `DispatcherQueue` marshaling; new `Resources.resw` keys (`CompressionBombDialogTitle`,
      `CompressionBombDialogMessage`); `MainViewModel` wiring
- [x] `Archiver.Shell` unchanged — confirmed in scope discussion with user
- [x] New unit tests for `EvaluateCompressionBombAsync` (all 4 outcomes + warning-detail
      correctness) — `ArchiveEntrySecurityCompressionBombTests.cs` (new)
- [x] Reworked ZIP bomb test (whole-archive skip, not per-entry) + new "callback confirms,
      extracts normally" test
- [x] Reworked tar bomb test (`SkippedFile` not `ArchiveError`) + new "callback confirms, extracts
      normally" test
- [x] `dotnet test --filter "Category!=Slow"` passes — 187/187 (132 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 19 Archiver.Core.IntegrationTests, was 178/178 before this task)
- [x] `ARCHITECTURE.md` (`ExtractOptions`, `CompressionBombWarning`, `IDialogService` snippets)
      and `DECISIONS.md` updated
- [x] `SECURITY.md`'s two T-F90-era spots (mitigation table row, tar.exe Trust Model section)
      updated to describe the new confirm-gated model, with fresh explicit user permission
      obtained separately from this task's main approval, per `CLAUDE.md`'s standing rule
- [x] `Deploy.ps1` build+sign+install (Pakko 1.2.0.2)
- [x] Manual on-device verification: real bomb-shaped `.zip` (47.6 KB compressed, 47.7 MB
      declared, 1026:1) and `.tar.gz` (same shape) launched via `pakko://extract?files=...`
      protocol activation. Both showed the identical "Suspicious archive" dialog with correct
      "47,7 MB of data, a compression ratio of 1026:1" text — confirming the shared evaluator/
      dialog works uniformly for both extractors and the `DispatcherQueue` marshaling fix works
      (no `RPC_E_WRONG_THREAD` crash). ZIP tested with "No": summary dialog showed "Skipped (1)"
      with the exact declined-ratio message, filesystem confirmed nothing was extracted. tar.gz
      tested with "Yes": no summary dialog (matches existing "no errors/skipped = no dialog"
      behavior), filesystem confirmed `bomb.txt` extracted byte-for-byte (50,000,000 bytes,
      content verified). Insufficient-disk-space branch not verifiable on-device (no practical
      way to fill a real disk) — covered by unit tests only (`ArchiveEntrySecurityCompressionBombTests`),
      noted explicitly rather than claimed as on-device-verified

---

### T-F95 — Bug: Root "Pakko" Context-Menu Icon Missing in Explorer (No Icon Resource in Archiver.App.exe)
- [x] **Status:** complete — root cause found, fixed, and confirmed on-device by the user
      personally 2026-07-07 (fresh Explorer window after reinstall, "Pakko" icon now shows). Not
      the already-documented "first-open flicker" cache artifact — a real missing resource, since
      confirmed reproducible on demand and gone after the fix. Reopens the icon-reliability
      assumption T-F61 and T-F92 both shipped with ("root 'Pakko' entry keeps its icon").
- **Depends on:** T-F61 (root `GetIcon` implementation), T-F92 (last touched this code, reverted
  the submenu-icon part, left root icon as-is)

**What:** `PakkoRootCommand::GetIcon` (`ExplorerCommands.cpp:495`) returns
`GetAppIconPath()` — `<dll_dir>\Archiver.App.exe,0` — via `SHStrDupW`. User-provided screenshot
(2026-07-07, real right-click on a folder in Explorer) shows the "Pakko" root entry with a blank
icon slot, while "NanaZip" directly above it shows its icon correctly in the same menu at the same
moment. This is not the already-documented "first-open-of-a-new-Explorer-window flicker" artifact
(`CLAUDE.md`'s hard constraints) — the user is reporting it missing on a menu they were already
looking at, and says it has been flaky across sessions, not just on first paint.

**Investigation so far (this session, before any code change):**
- Confirmed `Package.appxmanifest` declares `Archiver.App.exe` and `Archiver.ShellExtension.dll`
  in the same package (both are `<Application>`/`com:Class Path=` entries with no subfolder), so
  `GetAppIconPath()`'s `<dll_dir>\Archiver.App.exe,0` should be a structurally valid path once
  installed.
- Checked the installed package directly: `Get-AppxPackage *Pakko*` →
  `C:\Program Files\WindowsApps\PavloRybchenko.Pakko_1.2.0.5_x64__9hkd8feqeqbr4\Archiver.App.exe`
  exists, and `[System.Drawing.Icon]::ExtractAssociatedIcon()` against it **succeeds** — the exe's
  icon resource itself is present and extractable via a standard API. Rules out "the exe has no
  icon at index 0" as the root cause.
- Fetched NanaZip's real shipped source
  (`NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`, `ExplorerCommandRoot::GetIcon`, confirmed via
  the GitHub trees API per `CLAUDE.md`'s pre-implementation-research rule) — it uses the **same
  pattern**: `GetModuleDirPrefix() + "NanaZip.Modern.FileManager.exe" + ",-1"`, an external exe
  path plus an icon index, not an icon resource embedded in the shell extension DLL itself. So
  Pakko's basic approach isn't structurally wrong relative to a real shipped competitor — the
  `,0` vs `,-1` (positional index vs resource-ID index) difference is the only notable delta,
  not yet ruled out as relevant.
- `Package.appxmanifest`'s `Version` is `1.2.0.6` but the installed package is `1.2.0.5` — a
  one-version gap consistent with `Deploy.ps1`'s normal post-install version bump (see
  `CLAUDE.md`'s Deployment section), not itself a sign of a stale/broken install, but worth
  re-confirming after the next real `Deploy.ps1` run.
- **Leading hypothesis, not yet confirmed:** `CLAUDE.md`'s existing hard constraint already
  documents Explorer as having a "known Explorer verb/icon-cache artifact" for Pakko's submenu on
  first open of a new Explorer window — this may be a variant of the same OS-level caching/async
  icon-load behavior (`DECISIONS.md`'s "explorer.exe Crash on Context Menu (GetIcon/GetToolTip
  S_FALSE)" entry references a `ShouldLoadIconAsync()` code path inside
  `Windows.UI.FileExplorer.dll` specifically for shell-extension icons), rather than a Pakko code
  defect. Per `CLAUDE.md`'s own instruction on that constraint — "don't chase this with code
  changes without first confirming the cache-artifact explanation is wrong" — this needs a live
  test before any code change: restart `explorer.exe` and/or open a completely fresh Explorer
  window, then check whether the root icon reliably appears immediately, disappears again after
  some time/action, or is consistently absent regardless. User is at the PC now and can run this
  directly.

**Root cause (confirmed empirically, not guessed):** `src/Archiver.App/Archiver.App.csproj` had
no `<ApplicationIcon>` property — only `Assets\Square44x44Logo.ico` copied in as `Content` (used
for the MSIX tile/Start-menu logo, a different mechanism entirely). The built `Archiver.App.exe`
apphost therefore had **zero** classic Win32 icon resources. `PakkoRootCommand::GetIcon`
(`,0` positional index — the same shape NanaZip's real shipped `ExplorerCommandRoot::GetIcon`
uses against its own exe, confirmed via the GitHub trees API) was pointing at an index that never
existed, so it always resolved to nothing. Decisive test (`ExtractIconEx(exe, -1, ...)` for total
count) on the installed exe read `total=0` before the fix — not a `.NET Icon.ExtractAssociatedIcon`
false-positive (that API can return a generic fallback icon even for a resource-less exe, which
had briefly looked like evidence the icon was fine and needed ruling out first).

**Fix:** added `<ApplicationIcon>Assets\Square44x44Logo.ico</ApplicationIcon>` to
`Archiver.App.csproj` (one line, matches NanaZip's proven pattern — an icon embedded in the exe
itself, not a change to `ExplorerCommands.cpp`). Rebuilding raised the apphost's icon count from 0
to 1; reinstalling raised the installed package's count from 0 to 1 too (`ExtractIconEx` rerun
against the fresh install).

**Found along the way — separate, pre-existing packaging pipeline issue:** `Deploy.ps1`/
`dotnet publish` now reliably fails with `MSB3231: Unable to remove directory ...` on a
freshly-created `AppPackages\..._Test\` (or `obj\...\PackageLayout\`) folder, *after* the `.msix`
has already been written successfully — reproduced identically in three different clean-state
attempts (this session) and independently by the user in their own terminal. Windows Defender
real-time protection was ruled out (user already has a project-wide exclusion). Since the `.msix`
file itself is valid and complete by the time the error fires, this session worked around it by
uninstalling the old package and `Add-AppxPackage`-ing the freshly-built `.msix` directly, instead
of relying on `Deploy.ps1`'s own install step. The cleanup failure itself is unexplained — most
likely a parallel-MSBuild-node race between locale-resource generation (25 locale sub-packages,
T-F91) and the packaging pipeline's own directory cleanup, worth a `/m:1` (serialize the build)
experiment if it recurs — **not fixed here**, tracked as a new follow-up below (see T-F96).

**Acceptance criteria:**
- [x] Root cause confirmed via a decisive, reproducible test (`ExtractIconEx` total icon count on
      the installed `Archiver.App.exe`) — not inferred from `Icon.ExtractAssociatedIcon` alone,
      which was checked first and found to be a false-positive-prone API for this purpose
- [x] Real NanaZip shipped source fetched and compared (`ExplorerCommandRoot::GetIcon` in
      `NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`) per `CLAUDE.md`'s pre-implementation-research
      constraint for COM/shell-adjacent changes — confirmed Pakko's `GetIcon` approach (external
      exe path + icon index) already matches a real shipped implementation; the fix needed was in
      the icon *source*, not `ExplorerCommands.cpp`
- [x] `<ApplicationIcon>` added to `Archiver.App.csproj`; rebuilt apphost's icon count raised from
      0 to 1
- [x] Installed package's apphost re-verified with the same `ExtractIconEx` test after reinstall —
      0 → 1
- [x] Manual on-device verification: user personally restarted/opened a fresh Explorer window and
      confirmed the "Pakko" root entry now shows its icon in the real right-click menu (2026-07-07)
- [x] `DECISIONS.md` entry added for this investigation (see "T-F95" entry)

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

### T-F102 — Bug: `Deploy.ps1` Reports Exit Code 1 on Fully Successful Deployments
- [x] **Status:** complete — fixed in code (2026-07-13, fix designed via a second-opinion review
      relayed by the user from another model) and fully verified end-to-end on-device, including
      a real (not artificial) live recurrence of the T-F96 race during verification. **Does not
      change T-F96's root-cause investigation status — that stays a separate open item**; this
      task was only about the script's own exit-code plumbing being wrong, independent of what
      eventually turns out to cause the underlying MSB3231 race.
- **Depends on:** T-F96 (shares the tolerated-race code path, but is a distinct bug)

**What:** found 2026-07-13 while running a full `Deploy.ps1` for T-F05's manual verification —
the script reported process exit code 1 even though the deployment fully succeeded (Pakko
installed, version bumped). Root cause: PowerShell's `$LASTEXITCODE` is only updated by external/
native process invocations, never by built-in cmdlets (`Add-AppxPackage`, `Get-AppxPackage`,
etc.). Once `dotnet publish` hit the tolerated T-F96 race and left a nonzero `$LASTEXITCODE`, none
of the cmdlets running afterward touched it, and the script had no explicit `exit` at the end — so
the stale nonzero value from the *tolerated* failure silently became the script's own reported
outcome, indistinguishable from a real failure to anything checking the exit code (CI, a wrapper
script, a human glancing at `$LASTEXITCODE` after the fact).

**Fix implemented:**
1. Every native call's exit code (`dotnet build` ×2, `dotnet publish`) is now captured into its own
   local variable (`$shellBuildExitCode`, `$shellExtBuildExitCode`, `$publishExitCode`)
   immediately after the call; all decisions are based on those variables, never on
   `$LASTEXITCODE` read later. Every failure branch exits explicitly with its captured code, and
   the script now ends with an explicit `exit 0` — reaching that line means every prior step
   already succeeded or exited on its own.
2. The T-F96 tolerance gate no longer decides on the MSB3231 regex match. It now decides on
   artifact evidence: a package written after publish started (`LastWriteTime -ge
   $publishStartTime`) **and** `Get-AuthenticodeSignature` reporting `Status -eq 'Valid'` on it.
   A real compile error never produces a package at all; a real signing error leaves an invalid
   signature; so a fresh, validly-signed package alongside a nonzero exit can only be this exact
   post-packaging cleanup race. The regex match is kept only as diagnostic text inside the warning
   message (labels which known failure text was seen), never as a decision input — avoids the
   fragility of matching localized MSBuild output.
3. `Add-AppxPackage` now runs inside `try { ... -ErrorAction Stop } catch { ...; exit 1 }` — it had
   no error handling at all before, so a genuinely bad/corrupt package would have silently reported
   success.
4. **Follow-up, same day:** freshness + a valid signature only prove the archive file is new and
   intact — not that its content is current. `Archiver.App.csproj` packages
   `Archiver.Shell.exe`/`Archiver.ShellExtension.dll` via `CopyToOutputDirectory=PreserveNewest`,
   which can silently keep a stale copy (the exact mechanism behind `CLAUDE.md`'s already-documented
   "a quick `dotnet build` can silently install a stale MSIX" gotcha). Added a SHA256 byte-for-byte
   comparison between each packaged entry (read via `System.IO.Compression.ZipFile`, no extraction)
   and the corresponding file in `bin\`, gating the tolerate branch on both files matching exactly.
   A timestamp-based version was tried first and rejected — inspecting a real package showed both
   satellite files getting an *identical* packaged timestamp regardless of their actual build time,
   so a timestamp check couldn't have detected a stale copy at all. Only applies to a flat `.msix`;
   a `.msixbundle` (25+ locales, T-F91) nests these files one zip level deeper, which this check
   doesn't unpack — falls back to freshness+signature only in that case.

**Acceptance criteria:**
- [x] Every native call's exit code captured into a local variable and checked from that variable,
      not `$LASTEXITCODE`, at every subsequent decision point
- [x] Explicit `exit 0` at the end of the script; explicit `exit <code>` on every failure branch
- [x] T-F96 tolerance gate switched from regex-only to artifact-based (fresh + validly-signed
      package); regex retained only as diagnostic warning text
- [x] `Add-AppxPackage` wrapped in `try`/`catch` with `-ErrorAction Stop`
- [x] Live `Deploy.ps1` run completed successfully and confirmed to exit 0 (`$LASTEXITCODE` checked
      immediately after the run, in a fresh shell)
- [x] Race reproduction: not staged artificially — the real MSB3231 cleanup race actually recurred
      live during this session's verification run (`Archiver.App_1.2.0.11_x64.msix`, matched the
      known cleanup-race text). The new fresh-package + valid-Authenticode-signature gate
      correctly tolerated it, installed the package, reported version 1.2.0.11 successfully, and
      the script exited 0 — the exact end-to-end path this criterion asked for, exercised for real
      rather than synthetically
- [x] Confirmed a real compile error still causes a nonzero exit end-to-end: introduced a
      deliberate C# syntax error in `App.xaml.cs`, ran `Deploy.ps1` against it — `dotnet publish`
      failed, no fresh package existed (build never reached packaging), the gate correctly did
      not tolerate it, and the script exited 1. Reverted the deliberate error immediately after
      (`git checkout -- src/Archiver.App/App.xaml.cs`)
- [x] `DECISIONS.md` updated: why the artifact-based gate replaces the regex-only gate, and the
      localized-MSBuild-text fragility of the old approach
- [x] Package-content completeness check added (SHA256 comparison of packaged satellite files
      against `bin\`), verified against a known-good package before wiring into the gate, and
      re-run twice more end-to-end (both hit the real MSB3231 race again, both passed correctly)
- [x] `TASKS.md` updated (this entry)

**Separately, not part of this task's code fix:** the repo root and all subdirectories now have
the `NotContentIndexed` attribute set (no elevation, no security tradeoff, per the user's request)
to reduce Windows Search Indexer's involvement — DECISIONS.md's leading suspect for the T-F96 race
itself. Caveat: `AppPackages\` is wiped and recreated by every `Deploy.ps1` run, so this specific
attribute doesn't survive onto its next incarnation; a persistent fix needs Windows Search's own
path-based exclusion list, which needs elevation and wasn't done this session.

---

### T-F50 — tar.exe Test Fixtures
- [x] **Status:** complete — all achievable coverage implemented. RAR's previously-documented
      "unobtainable on this machine" gap (T-F49/T-F85/T-F86) was closed 2026-07-07 — a `valid.rar`
      fixture was generated via WinRAR's official console `Rar.exe` (installed via `winget`, used
      once, then uninstalled — no RAR-writing tool is shipped with or used by Pakko itself), same
      one-off pattern `valid.7z` already used with `NanaZipC.exe`. Bomb detection, originally
      descoped to T-F90 as a missing-feature gap (not a fixture gap), closed 2026-07-15 once
      T-F90/T-F94 shipped real bomb-shaped-archive test coverage on the tar.exe path — see the
      criterion below

**What (as implemented — deviates from the original "committed `Fixtures/tar/` corpus" spec
below; see Design deviation note):** round-trips every tar-family compression variant
`TarProcessService.ExtractAsync` supports, plus the formats it can only read.

**Design deviation from the original spec (advisor-reviewed before implementing):** the original
text asked for a committed binary corpus under `tests/Archiver.Core.Tests/Fixtures/tar/`,
generated by the `GenerateFixtures` project. Empirically checked what the system's `tar.exe`
(bsdtar 3.8.4) can actually *create*, not just read (`tar --help`'s `--format` only lists
`ustar|pax|cpio|shar` for writing — no 7z/rar writer exists in libarchive at all): tar, tar.gz,
tar.bz2, tar.xz, tar.zst, and tar.lzma can all be created by `tar.exe` itself. Generating these at
test-run time (new `ExternalTarFixtureBuilder.cs`, shells out to `tar.exe`) avoids committing
binary blobs for formats that are perfectly reproducible in CI, and extends the precedent
`TarBuilder.cs` already set for plain `.tar` (self-generated, "T-F50 owns the full multi-format
fixture set later" per its own doc comment). Only 7z needed a committed fixture (`Fixtures/valid.7z`,
built via NanaZip's `NanaZipC.exe` — same tool T-F85 already used for this, documented in
`Fixtures/README.md`) since `tar.exe` can only read it. RAR needed a fixture too but none could be
obtained at the time (no RAR-capable encoder installed anywhere on this machine — same finding as
T-F85/T-F86); closed 2026-07-07, see Status above.
All new tests live in `tests/Archiver.Core.IntegrationTests/`, matching where T-F49's tar tests
already are, not a new `tests/Archiver.Core.Tests/Fixtures/tar/` directory as the original text
named — that directory belongs to the ZIP-fixture/`GenerateFixtures` convention, which this task's
tests don't use.

**Files:** `tests/Archiver.Core.IntegrationTests/ExternalTarFixtureBuilder.cs` (new),
`TarProcessServiceCompressedFormatsTests.cs` (new — tar.gz/bz2/xz/zst/lzma round-trips + a
unicode-filename tar.gz test), `TarProcessServiceExternalFormatsTests.cs` (new — the committed
`valid.7z` and `valid.rar` fixtures), `TarProcessServiceExtractTests.cs` (added a
truncated/corrupted-tar test), `Fixtures/valid.7z`, `Fixtures/valid.rar` (added 2026-07-07), and
`Fixtures/README.md`.

**Acceptance criteria:**
- [x] Valid-format round-trip coverage: tar (already covered pre-existing), tar.gz, tar.bz2,
      tar.xz, tar.zst, tar.lzma (all generated at test time via real `tar.exe`), 7z and RAR
      (committed fixtures — RAR added 2026-07-07)
- [x] Corrupted-archive test: a truncated `.tar` is rejected with an `ArchiveError`, not an
      unhandled exception or silent empty success
- [x] zipslip: already covered by the pre-existing
      `ExtractAsync_ArchiveWithParentTraversalEntry_RejectsWholeArchive` test — no new test needed
- [x] Bomb: **closed 2026-07-15** — was descoped to T-F90 (no compression-ratio protection existed
      on the tar.exe path at the time T-F50 was written). T-F90 (and its T-F94 confirm-and-extract
      successor) since added real bomb-shaped-archive coverage using this task's own
      `ExternalTarFixtureBuilder`: `ExtractAsync_ArchiveWithExtremeCompressionRatio_NoCallback_SkipsWholeArchive`
      and `..._CallbackConfirms_ExtractsNormally` in `TarSandboxedServiceExtractTests.cs` (a
      5,000,000-byte repeated-'A' `.tar.gz`, confirmed rejected without a callback and extracted
      when one confirms) — living in T-F90/T-F94's own test files rather than a new T-F50 one, since
      the behavior belongs to those tasks, but it satisfies this criterion's original intent
- [x] ADS: already covered by the pre-existing
      `ExtractAsync_ArchiveWithAlternateDataStreamEntry_RejectsWholeArchive` test
- [x] Tests tagged `[SkipIfFormatUnsupported]` for bz2/xz/zst/lzma/7z/rar
- [x] Unicode filename coverage: new tar.gz test with Cyrillic+CJK content and a Cyrillic filename
- [x] `dotnet test --filter "Category!=Slow"` passes — 177/177 (124 Archiver.Core.Tests + 36
      Archiver.Shell.Tests + 17 Archiver.Core.IntegrationTests, was 168/168 before this task,
      176/176 before the RAR fixture was added)

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

### T-F52 — AppContainer Sandbox for tar.exe
- [x] **Status:** complete (v1.4) — Phase 1 implementation (all 13 steps) complete 2026-07-14,
      including a full `Deploy.ps1` build+sign+install and AI-driven on-device verification
      (`.tar.gz`/`.7z`/`.rar` all extracted correctly through the installed, packaged
      `Archiver.Shell.exe`). `TarProcessService.cs` is deleted and `TarSandboxedService` is real,
      shipping code. **Graduated to `[x]` 2026-07-15, user-directed**, after a second on-device
      pass through the actual packaged GUI app (real button click via the `windows` MCP
      UI-automation server, not just `Archiver.Shell.exe`) — see the last acceptance criterion
      below for the full method and evidence. See `DECISIONS.md`'s several same-day T-F52 entries for the full
      empirical trail — three real bugs found and fixed while implementing (a wrong
      `CERT_FIND_SUBJECT_CERT` constant, hardlinked staged files not inheriting the quarantine
      folder's ACL, libarchive's implicit parent-directory creation failing under the
      AppContainer), plus a design correction (quarantine rooted under `%TEMP%`, not "same disk as
      destination" — an AppContainer token enforces `FILE_TRAVERSE` on every ancestor directory,
      which the user's arbitrary destination folder tree doesn't grant). A fourth bug found via
      advisor review after Step 13 (not by a test — no existing test exercised a sandbox-setup
      failure): `TarSandboxedService.ExtractAsync`/`ListEntriesAsync` didn't catch
      `InvalidOperationException`, the type `AppContainerProfile`/`QuarantineAcl`/
      `SecurityCapabilitiesAttributeList`/`SandboxJobObject` throw on a Win32 setup failure — so a
      blocked/misconfigured sandbox would have crashed instead of producing an `ArchiveError`,
      violating this project's own "never throw to callers" constraint. Fixed via a new
      `SandboxSetupException` caught at the same boundary as `TarSignatureVerificationException`;
      see `DECISIONS.md`'s T-F52 entry for the two real forced-failure tests added.

      Originally redesigned 2026-07-14 (advisor-reviewed design session, same day as the
      scope-widening that absorbed T-F13's Job Object/network-isolation layers). Mechanism
      changed from a Low-IL restricted token to an **AppContainer** — user-directed choice after
      weighing both; see `DECISIONS.md`'s T-F52 entry for the full two-vector threat-model
      reframing and the AppContainer-vs-Low-IL tradeoff. The global WFP firewall rule from the
      previous draft is **dropped** — AppContainer gets network isolation for free (kernel-
      enforced via capability omission, no firewall rule, no elevation, no system-wide side
      effect) by simply not granting the `internetClient` capability. This is still the single
      task for all process-level tar.exe hardening — do not re-split Job Object/network-isolation
      work back into a separate task.
      **Phase 0 (empirical spikes) complete, 2026-07-14** — a Plan agent produced a full 9-file/
      13-phase implementation design (below); before writing any of the 9 production files, its
      three empirically-unverified assumptions were tested for real on this machine via a
      throwaway console spike (not committed — same precedent as T-F49's own research script).
      All three confirmed with no design changes needed — see `DECISIONS.md`'s "Phase 0" entry for
      full method/evidence: (1) `.tar.xz`/`.tar.zst` extraction completes correctly under a Job
      Object with `ActiveProcessLimit = 1` — bsdtar's compression filters are confirmed statically
      linked, no child-process filter helper; (2) a **regular** (non-LPAC) AppContainer with an
      **empty capability list** successfully launches `tar.exe --version`, reading all its own
      System32 DLL dependencies — LPAC is not needed; (3) least-privilege ACE masks
      (`in\` = Read&Execute, `out\` = Modify, quarantine-root = traverse-only) let a real `-xf` run
      succeed with zero `ERROR_ACCESS_DENIED`, and a negative control confirmed the same sandboxed
      process is denied writing to any path never explicitly ACL'd (`tar.exe: could not chdir to
      ...`) — the core security property this task exists to provide, demonstrated, not assumed.
      **Everything below is now implemented** (steps 1–11 of 13; see the Status line above) — the
      design that follows was the concrete spec used for that implementation session, informed by
      the Phase 0 findings above, and is kept here as the as-built reference (not a future plan).
- **Depends on:** none

**What:** `TarSandboxedService` implements `ITarService`, launching `tar.exe` inside a Low-privilege
AppContainer (no network capability, ACL'd quarantine directory) instead of Pakko's own process
token. Replaces the deleted `TarProcessService` — DI wired at `Archiver.App/App.xaml.cs`,
`Archiver.Shell/Program.cs`, and `SkipIfFormatUnsupportedAttribute.cs`.

**Threat model this actually defends (record in `SECURITY.md`, not just here):** this task does
**not** defend against `tar.exe` itself being replaced/tampered with — reaching
`C:\Windows\System32\tar.exe`'s ACLs requires SYSTEM-level access, at which point the whole host is
already compromised and no sandbox around Pakko's own invocation changes that. The absolute-path
invocation (existing hard constraint) already covers the realistic version of that vector
(PATH-hijacking from a lower privilege level). What this task defends is the other vector: **a
hostile archive triggers a real parsing vulnerability in the otherwise-legitimate, Microsoft-signed
tar.exe** (libarchive is a native parser with a real CVE history, processing attacker-controlled
bytes) — the standard "sandbox the untrusted-input parser" pattern. Don't let a future contributor
re-read "Low IL Sandbox for tar.exe" and think this task is about not trusting Microsoft's binary.

**File:** `src/Archiver.Core/Services/TarSandboxedService.cs`

**Signature check (new, cheap, defense-in-depth only — not a substitute for the sandbox):** verify
`C:\Windows\System32\tar.exe` carries a valid Authenticode signature with Microsoft as the signing
subject (`WinVerifyTrust` or `System.Security.Cryptography.X509Certificates` cert-chain check)
before every launch. Explicitly documented as low-value against a real attacker (TOCTOU between
check and launch; anyone able to swap the binary can do worse) — included because it's nearly free,
not because it's load-bearing.

**P/Invoke surface:**
- `CreateAppContainerProfile` / `DeleteAppContainerProfile` — AppContainer SID + profile lifecycle
- `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` (`InitializeProcThreadAttributeList` /
  `UpdateProcThreadAttribute`) — attach the AppContainer SID + capability list (empty — no
  `internetClient`, no `internetClientServer`) to `CreateProcess`'s extended startup info
- `SetNamedSecurityInfo` — ACL the quarantine directory to grant the AppContainer SID access (an
  AppContainer process cannot touch a directory that hasn't explicitly granted its SID rights,
  unlike a Low-IL token which only needs an IL label)
- `CreateJobObject` / `SetInformationJobObject` / `AssignProcessToJobObject` — Job Object resource
  limits (absorbed from T-F13's Layer 2)
- `WinVerifyTrust` (or equivalent managed cert-chain API) — the signature check above

**Flow:**
1. Verify `tar.exe`'s Authenticode signature (Microsoft subject) before doing anything else
2. Ensure Pakko's AppContainer profile exists — created **once**, lazily, on first sandboxed
   launch (`CreateAppContainerProfile`, tolerating `ERROR_ALREADY_EXISTS`), and **reused for
   every subsequent tar.exe invocation for the lifetime of the install** — never created or
   deleted per-operation. The profile's SID is a fixed, safe-to-share identity; only the
   filesystem grants below are per-operation. This matters for both performance (no
   registry-profile churn per Extract/Archive call) and correctness under T-F12's parallel
   `SeparateArchives` mode (concurrent create/delete of the same profile would race)
3. Create a **two-subfolder** quarantine directory rooted under a fixed, Pakko-owned
   `%TEMP%\PakkoTarSandbox\<guid>\` location — **not** "same disk as the destination" as this
   step originally said. An AppContainer token has no bypass-traverse-checking privilege, so
   `FILE_TRAVERSE` is enforced on every ancestor directory down to `in\`/`out\`, and the user's
   arbitrary destination folder sits under an ancestor chain Pakko doesn't own and shouldn't be
   granting ACEs on; found empirically while implementing (see DECISIONS.md's T-F52 entry) —
   `%TEMP%` itself needs no explicit grant, only the two Pakko-created levels
   (`PakkoTarSandbox` and the per-operation `<guid>`) do. The final `out\`-to-destination move is
   a per-file `File.Move` (already cross-volume-safe), not a directory rename, so this costs at
   most an extra copy instead of a rename when the two are on different volumes — never a
   correctness problem. Grant the AppContainer SID **read-only** access to `in\` and
   **write-only** access to `out\` via `SetNamedSecurityInfo` — an AppContainer process has zero
   filesystem access outside paths explicitly ACL'd to its SID, so both are required (tar.exe
   needs to read the source archive and write extracted output; it gets neither by default)
4. Place the source archive into `quarantine\in\` — hardlink if the archive and the quarantine
   directory are on the same volume (instant, no I/O cost); fall back to a real copy only when
   they're on different volumes. Never grant the AppContainer SID an ACE on the archive's
   original, user-chosen path directly — all AppContainer access stays confined to paths Pakko
   itself created and controls. **A hardlinked staged file shares its security descriptor with
   the original archive, not the containing `in\` folder** — grant Read&amp;Execute on the staged
   file path itself too, immediately after staging, regardless of whether it was hardlinked or
   copied (found empirically as a real bug — see DECISIONS.md's T-F52 entry — a copy would
   already inherit this from `in\` at creation time, but the explicit grant is harmless and keeps
   both paths correct without a branch)
5. Create a Job Object for the tar.exe process (`ActiveProcessLimit = 1`, RAM limit, CPU time
   limit, UI restrictions, `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — see criteria below)
6. Run tar.exe inside the AppContainer (empty capability list — no network) for **both** T-F49's
   whole-archive pre-scan (`-tf`/`-tvf`, reading from `quarantine\in\`) and, only if the pre-scan
   passes, the extraction itself (`-xf`, writing to `quarantine\out\`) — the pre-scan is not
   exempt from sandboxing just because it doesn't write output; it's the same untrusted-parser
   exposure as extraction. Both runs assigned to the Job Object
7. After the process exits, validate all files in `quarantine\out\` at Pakko's normal process
   identity (existing `ArchiveEntrySecurity` checks)
8. Atomic move from `quarantine\out\` to final destination
9. Delete the entire quarantine directory (both `in\` and `out\`, including the hardlinked/copied
   archive), close the Job Object handle. The AppContainer profile itself is **not** deleted —
   it persists for reuse by the next operation

**Job Object resource limits (absorbed from T-F13's Layer 2):**
- `ActiveProcessLimit = 1` — tar.exe cannot spawn child processes (kills the most dangerous
  post-exploit step: spawning `cmd.exe`/`powershell.exe`/`rundll32.exe` for a second stage)
- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — no orphaned tar.exe survives if Pakko's handle is dropped
- RAM limit 512 MB — backstop behind T-F90's existing compression-ratio check, not a primary bomb
  defense
- CPU time limit — maximum runtime enforced; job termination on expiry
- UI restrictions — no clipboard, no desktop manipulation (marginal: tar.exe is a non-interactive
  console app, this restricts an already-near-zero surface — include because it's one flag, don't
  oversell its value)

**Network isolation — now free via AppContainer, not a firewall rule:** the AppContainer is created
with an empty capability list (no `internetClient`/`internetClientServer`). This is enforced by the
kernel at the socket layer for that specific process — no `New-NetFirewallRule`, no install-time
elevation, no system-wide side effect on other applications that also invoke the same system
`tar.exe`. The previous draft's global WFP rule is dropped entirely — do not resurrect it.

**Confirmed via Phase 0 spike (2026-07-14, see `DECISIONS.md`):** Windows' built-in bsdtar keeps
its compression filters statically linked and in-process for both `.tar.xz` and `.tar.zst` — real
extraction of both succeeded under a Job Object with `ActiveProcessLimit = 1`.
`ActiveProcessLimit = 1` is safe to ship as designed.

---

## Concrete implementation design (Plan-agent-produced, Phase-0-informed; implemented 2026-07-14 —
kept below as the as-built reference; see `DECISIONS.md`'s T-F52 entries for what deviated from
this spec during implementation and why)

**New files** — under a new `src/Archiver.Core/Services/Sandbox/` subfolder, split into small,
single-concern classes rather than one `NativeMethods` god-class (this task's P/Invoke surface
spans five distinct Win32 subsystems — AppContainer profiles, DACL editing, Job Objects, raw
process creation, Authenticode — and this codebase's existing P/Invoke precedents,
`ArchiveEntrySecurity.cs`'s one-DllImport-per-concern style and `NativeProgressDialog.cs`'s
one-COM-concern-per-file style, both favor small/focused over consolidated):

| File | Purpose |
|---|---|
| `src/Archiver.Core/Services/TarSandboxedService.cs` | `ITarService` impl — ports `ExtractAsync`/`ExtractSingleArchiveAsync`/`ScanForUnsafeEntriesAsync`/`ListEntriesAsync`/`ExpandSelection`/`IsDangerousEntryName`/`EnumerateFilesGuarded`/`GetUniqueFilePath`/`ParseTarListingSize`/`SplitLines`/`DetectCapabilitiesAsync` from `TarProcessService.cs` verbatim except for the launch primitive |
| `Sandbox/SandboxHandles.cs` | 4 new `SafeHandle`/`CriticalHandle` types (SID, Job Object, attribute-list buffer, process/thread) — this repo's first custom `SafeHandle`s; standard BCL `SafeHandleZeroOrMinusOneIsInvalid`/`CriticalHandle` patterns, no in-repo precedent to deviate from |
| `Sandbox/AppContainerProfile.cs` | Lazy-once profile creation (`EnsureProfileExists()`, tolerates `ERROR_ALREADY_EXISTS`) + per-call SID re-derivation (`GetProfileSid()` via `DeriveAppContainerSidFromAppContainerName` — deterministic, no cached live handle, avoids a lifetime/race question under T-F12's parallel mode for zero benefit) |
| `Sandbox/QuarantineAcl.cs` | Grants the AppContainer SID access to `in\`/`out\` via `SetEntriesInAclW`/`SetNamedSecurityInfoW`. Confirmed-working starting masks (Phase 0): `in\` = Read&Execute, `out\` = Modify, quarantine-root = traverse-only, all inherited `(OI)(CI)` except the root grant. **Translating these to exact raw `ACCESS_MASK` hex values (this task used `icacls.exe` to test, production calls `SetEntriesInAclW` directly) is an open item for this phase — verify via `icacls /save`+parse, don't hardcode a memorized constant** |
| `Sandbox/QuarantineStaging.cs` | Same-volume check (extend `ArchiveEntrySecurity.GetAvailableFreeSpace`'s existing `Path.GetPathRoot(Path.GetFullPath(...))` pattern) + hardlink-or-copy staging of the archive into `in\` — a wrong same-volume guess just costs an unnecessary `File.Copy` fallback when `CreateHardLinkW` fails, never a correctness break |
| `Sandbox/SandboxJobObject.cs` | Job Object create/configure/assign/dispose (T-F13's absorbed limits). Process must be created `CREATE_SUSPENDED`, assigned to the job, then resumed — a fast child could otherwise race `AssignProcessToJobObject` and escape the limits. Dispose after the process-wait completes, not before (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` would kill a still-running tar.exe if the job is closed early) |
| `Sandbox/SandboxedProcessLauncher.cs` | Raw `CreateProcessW` + `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` + stdout/stderr pipes (managed `Process.Start` cannot express extended startup info) — must do its own pipe plumbing with non-inheritable read-ends and async draining (deadlock risk otherwise, same class of bug today's `RunTarAsync` already avoids via `Task.WhenAll` on both streams). `STARTUPINFOEX.StartupInfo.cb` must be `sizeof(STARTUPINFOEX)`, not `sizeof(STARTUPINFO)` |
| `Sandbox/TarSignatureVerifier.cs` | `WinVerifyTrust` (`WINTRUST_ACTION_GENERIC_VERIFY_V2`) for integrity + `CryptQueryObject`/`CryptMsgGetParam(CMSG_SIGNER_CERT_INFO_PARAM)`/`CertGetNameStringW` for the Microsoft-subject check — **not** managed `X509Certificate2`/`X509Certificate.CreateFromSignedFile`, which only extracts the embedded cert blob without verifying it against the file's actual bytes (would defeat the one tampering scenario this check exists for), and this also sidesteps .NET 8's `X509Certificate2` constructor obsoletions (`SYSLIB0057`) entirely. Must call `WTD_STATEACTION_CLOSE` after `WTD_STATEACTION_VERIFY` to release `hWVTStateData` — a documented easy leak |
| `Sandbox/TarSandboxScope.cs` | Disposable orchestration: `CreateAsync(archivePath, quarantineRoot, needsOutputDir, ct)` ties profile+ACL+staging+Job Object into one scope; `RunAsync(tarArguments, ct)` is the actual choke point replacing today's `RunTarAsync` — used by **both** the pre-scan and the extraction within one scope (not two separate scopes), and by `ListEntriesAsync` with `needsOutputDir: false` (no `out\` folder/ACE created at all, since listing never writes) |

**Deleted:** `src/Archiver.Core/Services/TarProcessService.cs` — **outright, not kept as a
fallback** (user-confirmed decision). Rationale: this task's whole premise is fail-closed — if
AppContainer/ACL/Job-Object setup ever fails at runtime, the correct behavior is an `ArchiveError`
for that archive, never a silent fallback to unsandboxed extraction, which would silently reopen
exactly the vector T-F52 exists to close. An unused "just in case" class contradicts that posture.

**Control-flow adaptation:** `ExtractSingleArchiveAsync`'s quarantine dir goes from one shared
folder (`destDir + "_tar_tmp"`) to a `TarSandboxScope` wrapping that same path as a root with
`in\`/`out\` inside it. `ScanForUnsafeEntriesAsync`'s two `RunTarAsync(["-tf"/"-tvf", archivePath],
...)` calls become `scope.RunAsync(["-tf"/"-tvf", scope.StagedArchivePath], ...)` — same method
body, same T-F49 reject logic, only the archive path string changes. The move-and-validate loop
walks `scope.OutputDirectory` instead of the old `quarantineDir`. **`ArchiveEntrySecurity.
TryPropagateMotw(archivePath, finalFilePath)` stays unchanged, reading the ORIGINAL user-chosen
`archivePath`** — never the staged `in\` copy — since MOTW must reflect the real source's
Zone.Identifier. `ConflictResolver`, `ArchiveEntrySecurity.EvaluateCompressionBombAsync`,
`ExpandSelection`, `IsDangerousEntryName`, `EnumerateFilesGuarded`, `GetUniqueFilePath`, the T-F87
all-skipped bookkeeping, and the `finally`-block cleanup shape are all unchanged;
`scope.Dispose()` replaces today's `Directory.Delete(quarantineDir, recursive: true)`.
`ListEntriesAsync` gets its own scope per call with `needsOutputDir: false` and must **not** gain
the reject-on-unsafe-entry behavior (unchanged from today — listing still skips
`IsDangerousEntryName`/type-char rejection) while still running through the same sandboxed
primitive as extraction.

**Signature-check call sites (both real, neither cached):** (1) `SandboxedProcessLauncher.RunAsync`,
immediately before every `CreateProcessW` — structurally correct since every tar.exe launch
(`-tf`/`-tvf`/`-xf`) passes through this one primitive; (2)
`TarSandboxedService.DetectCapabilitiesAsync`, before its own deliberately-unsandboxed
`tar.exe --version` probe — catches a tampered binary at app startup via the same all-false-defaults
failure path that already exists for "tar.exe absent." `DetectCapabilitiesAsync` itself does
**not** go through the AppContainer/Job-Object machinery, so it costs what today's version costs —
preserving the existing 5-second detection timeout's meaning (this method is resolved eagerly and
synchronously at `Archiver.App` startup).

**Three DI/instantiation touch points — NOT just one "DI swap" as originally assumed:**
1. `src/Archiver.App/App.xaml.cs:33` — `services.AddSingleton<ITarService, TarProcessService>()`
   → `services.AddSingleton<ITarService, TarSandboxedService>()`.
2. `src/Archiver.Shell/Program.cs:127` — `var tarService = new TarProcessService();` → `new
   TarSandboxedService();`. This project has **no DI container at all**, so this is a direct swap,
   not something the "one-line DI swap" framing covers on its own.
3. `tests/Archiver.Core.IntegrationTests/SkipIfFormatUnsupportedAttribute.cs:21` — `new
   TarProcessService().DetectCapabilitiesAsync()...` → `new TarSandboxedService()...`. Not a
   judgment call once `TarProcessService.cs` is deleted — there is nothing else for this
   `FactAttribute`'s constructor to instantiate.

**Test file plan:**
- Renamed (`git mv`, preserving history), mechanically adapted (swap only `_sut`'s type — none of
  these assert on the quarantine path/name, only the public `ArchiveResult`/`ArchiveListResult`
  contract): `TarProcessServiceExtractTests.cs` → `TarSandboxedServiceExtractTests.cs`;
  `TarProcessServiceCompressedFormatsTests.cs` → `TarSandboxedServiceCompressedFormatsTests.cs`;
  `TarProcessServiceExternalFormatsTests.cs` → `TarSandboxedServiceExternalFormatsTests.cs`.
- New `tests/Archiver.Core.IntegrationTests/TarSandboxedServiceSandboxBehaviorTests.cs` — the 3
  real sandbox-behavior proofs this task's acceptance criteria require: (a) a file-write attempt
  targeting a path never ACL'd for the AppContainer SID fails (mirrors Phase 0's own negative
  control, which already demonstrated this exact failure mode: `tar.exe: could not chdir to
  ...`); (b) a real child-process-spawn attempt (e.g. `cmd.exe /c "start cmd /c exit"`) through the
  same launcher+Job-Object mechanism fails/is terminated — target the launcher/Job-Object
  mechanism generically, not tar.exe itself, since Phase 0 already confirmed tar.exe never spawns
  children for the formats Pakko supports; (c) a real socket-connect attempt fails — bind a
  loopback `TcpListener` (127.0.0.1, ephemeral port) in the test process itself, launch a present-
  by-default OS binary (e.g. `curl.exe`, gate with a skip-if-absent check mirroring
  `IntegrationAttribute`'s pattern) through the sandbox targeting that listener, and assert
  connection failure while the same listener accepts a connection from an unsandboxed launch of
  the same command — this unambiguously attributes the failure to the AppContainer's missing
  `internetClient` capability, not environment flakiness (no CI config exists in this repo to know
  if real internet access is even available during test runs, so a real-external-host version of
  this test would be environment-fragile).
- New pure-logic unit tests in `tests/Archiver.Core.Tests/Services/Sandbox/`:
  `QuarantineStagingTests.cs` (`IsSameVolume` true/false, hardlink-succeeds/copy-fallback);
  `AppContainerProfileTests.cs` (uses its **own** distinct test-only profile name, e.g.
  `Pakko.TarSandbox.Test.<guid>`, and is allowed to delete only that test profile at teardown —
  the production `Pakko.TarSandbox` profile is never deleted; state this distinction in the test
  file's header comment so a future contributor doesn't "fix" it into deleting the shared profile).
- New trait: `[Trait("Category","Sandbox")]` — **filterable, not excluded** from the default run
  (unlike `Slow`, which has a documented multi-second/multi-GB meaning). AppContainer-profile-reuse
  means per-test cost is expected to be sub-second; measure actual wall time once written and only
  promote to a real exclusion category if it turns out to matter in practice.

**Doc updates required, in order, once implementation happens:**
1. `DECISIONS.md` — new entry once implementation starts: records the final confirmed
   `ACCESS_MASK` hex values (translated from Phase 0's `icacls` letter codes), and that
   `TarProcessService.cs` was deleted outright (fail-closed rationale).
2. `ARCHITECTURE.md`'s `### v1.4 — AppContainer Sandbox for tar.exe` section (already corrected
   2026-07-14 — verify it still matches once real code exists, update if the implementation
   deviates from this spec in any way).
3. `DIAGRAMS.md` diagram 5 ("tar.exe whole-archive pre-scan and extraction") — re-derive from the
   new `TarSandboxedService.cs` source once it exists (per this doc's own Ground Truth Rule),
   replacing the single-`quarantineDir`/`RunTarAsync` nodes with `TarSandboxScope`
   creation/ACL/staging/`RunAsync` nodes and the `in\`/`out\` split. Must run every edited mermaid
   block through `npx @mermaid-js/mermaid-cli` before considering the edit done (this repo's
   documented gotcha — no auto-validation exists); avoid bare `;`/unescaped quoted phrases in
   labels.
4. `TESTING.md` — add rows for the renamed + net-new test files, document the new `Sandbox` trait.
5. `CLAUDE.md` — update the aggregate test-count line, mention the new `Sandbox` trait alongside
   `Slow`.
6. `SECURITY.md` — already carries the two-vector reframing and the v1.4 AppContainer
   isolation-method table row from the 2026-07-14 session; no further change expected unless the
   final ACE masks or regular-vs-LPAC choice turn out to matter at that document's level of detail
   (unlikely — it documents mechanism, not exact mask constants).

**Recommended build/verify order for the next implementation session:**
1. SafeHandle + P/Invoke struct/DllImport declarations compile standalone (`SandboxHandles.cs` and
   the shells in the other `Sandbox/` files) — `dotnet build` green, no call sites yet.
2. Pure/testable helpers get unit tests: `QuarantineStaging.IsSameVolume`/`StageArchive`, the
   `EnsureProfileExists` idempotency mapping.
3. `SandboxedProcessLauncher` smoke test **without** security capabilities yet — raw
   `CreateProcessW`+`STARTUPINFOEX`+pipes launching something trivial (`cmd /c echo hello`),
   confirming exit code + stdout round-trip (pipe deadlocks are the classic bug here — Phase 0's
   own spike already exercises this shape successfully and can be a starting reference).
4. Add `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` + `AppContainerProfile`, smoke-test launching
   `tar.exe --version` inside the container (Phase 0 already confirmed this works; this step
   formalizes it into production code).
5. Add `SandboxJobObject` (create suspended → assign → resume), re-confirm `.tar.xz`/`.tar.zst`
   extraction through the *real* sandboxed path (Phase 0 already confirmed the mechanism; this
   step formalizes it).
6. Add `QuarantineAcl` (translate Phase 0's confirmed icacls masks to real `ACCESS_MASK` values) +
   `QuarantineStaging` + `TarSandboxScope`, tying everything into the `in\`/`out\` quarantine shape.
7. `TarSignatureVerifier` (`WinVerifyTrust`/`CryptQueryObject` chain), validated independently
   against `tar.exe` (should pass) and a non-Microsoft-signed decoy file (should fail on subject
   check).
8. Write `TarSandboxedService.cs`, porting `TarProcessService`'s control flow with the
   `TarSandboxScope`-based launch, and delete `TarProcessService.cs`.
9. Rename + adapt the 3 existing integration test files; add
   `TarSandboxedServiceSandboxBehaviorTests.cs`; add the 2 unit test files.
10. Wire DI in all 3 touch points.
11. `dotnet test --filter "Category!=Slow"` green (includes the new `Sandbox`-tagged tests, since
    that trait is filterable-not-excluded).
12. Doc updates per the ordered checklist above.
13. Full `Deploy.ps1` build+sign+install and on-device verification — required by this project's
    workflow rules for anything touching shell-triggered/security-sensitive behavior, and doubly
    necessary here since AppContainer/Job-Object behavior under a packaged MSIX `runFullTrust`
    process cannot be fully validated by `dotnet test` alone.

---

**Acceptance criteria (all but the last confirmed 2026-07-14 — Phase 1, steps 1–11 of 13):**
- [x] `TarSandboxedService` implements `ITarService` — same interface as the deleted `TarProcessService`
- [x] DI swap is one line: `AddSingleton<ITarService, TarSandboxedService>()`
- [x] `tar.exe`'s Authenticode signature (Microsoft Organization) verified before every launch
- [x] Empirically confirmed `.tar.xz`/`.tar.zst` extraction stays in-process (no child filter
      helper) before shipping `ActiveProcessLimit = 1`
- [x] AppContainer profile created with an empty capability list (no network capability),
      created lazily once and reused across every subsequent operation — never recreated or
      deleted per Extract/Archive call
- [x] Quarantine directory has separate `in\` (read-only) and `out\` (write-only) subfolders,
      each ACL'd to grant only the AppContainer SID the matching access
- [x] Source archive placed into `quarantine\in\` via hardlink when same-volume, real copy
      otherwise — the AppContainer SID never receives an ACE on the archive's original path
      (plus an explicit per-file grant on the staged copy itself — a hardlink doesn't inherit the
      containing folder's ACL, found empirically, see `DECISIONS.md`)
- [x] Both the whole-archive pre-scan (`-tf`/`-tvf`) and the extraction (`-xf`) run inside the
      AppContainer — the pre-scan is not run unsandboxed just because it produces no output
- [x] tar.exe process launched inside the AppContainer via
      `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`
- [x] tar.exe process assigned to a Job Object with `ActiveProcessLimit = 1` and
      `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
- [x] Job Object enforces a 512 MB RAM limit
- [x] Job Object enforces a maximum CPU time / runtime limit
- [x] Job Object applies UI restrictions (no clipboard, no desktop manipulation)
- [x] Job Object handle closed after the tar.exe process exits — no leak
- [x] Network access verified disabled for the tar.exe worker process (real socket-attempt test,
      not just capability-list inspection)
- [x] No firewall rule added anywhere — network isolation is AppContainer-only
- [x] Validation and move run at Pakko's normal process identity in C# after process exits
- [x] Quarantine directory cleaned up on success and failure
- [x] AppContainer profile created lazily once, tolerating `ERROR_ALREADY_EXISTS`, and persists
      across every operation — never deleted mid-operation (this replaces an earlier, contradictory
      draft of this line that said the profile is deleted after use; the Flow section above and
      `DECISIONS.md`'s T-F52 follow-up entry are authoritative — create-once-reuse-forever, no
      per-operation churn, no race under T-F12's parallel mode)
- [x] All P/Invoke handles properly closed — no leaks
- [x] `dotnet test` passes — integration test: file write outside quarantine fails; spawning a
      child process from the sandboxed tar.exe fails (Job Object `ActiveProcessLimit`); a real
      socket-connect attempt from inside the AppContainer fails. 282/282 tests green across two
      full-suite runs (`TarSandboxedServiceSandboxBehaviorTests.cs`)
- [x] `SECURITY.md`'s tar.exe Trust Model section updated with the two-vector reframing and the
      AppContainer isolation method (cascade per `CLAUDE.md`'s Documentation Map) — done during
      the initial design round, verified still accurate against the final implementation
- [x] Full `Deploy.ps1` build+sign+install done (`Archiver.App_1.2.0.31_x64.msix`, 2026-07-15);
      AI-driven on-device verification passed — `.tar.gz` (with nested subdirectories, real
      system tar.exe), `valid.7z`, and `valid.rar` all extracted correctly through the installed
      `Archiver.Shell.exe` via `--extract-here`, matching the real shell context-menu invocation
      path. **Graduated to `[x]` 2026-07-15, user-directed**: re-verified through the actual
      packaged GUI app this time (not just `Archiver.Shell.exe`) via the `windows` MCP UI-
      automation server (`pakko://extract` with three fresh fixtures — a `browse_test.tar.gz`
      built for this run with a nested `sub/` subdirectory, plus the committed `valid.7z`/
      `valid.rar` fixtures — pre-loaded into the pending list, then a real
      `mcp__windows__ui_click` on "Розпакувати"). All four resulting files (`browse_test\root.txt`,
      `browse_test\sub\nested.txt`, `pakko_seven\seven.txt`, `pakko_rar\rar.txt`) confirmed present
      with correct content via direct filesystem read after the click. User explicitly directed
      this as an accepted substitute for their own personal click-through, per this project's own
      "if the user explicitly directs it, performing verification via the local windows MCP server
      is an accepted substitute" workflow rule (`CLAUDE.md`).

---

### T-F105 — TAR Archive Creation via tar.exe (Compress Dialog + One-Click "Add to X.tar")
- [x] **Status:** complete (v1.4) — all four phases (Core, App, Shell, on-device verification)
      done 2026-07-16. **Phase D verification (user-directed via the `windows` MCP server, same
      precedent as T-F52's graduation):** `Deploy.ps1 -Thumbprint "D2EC5F2C..."` ran clean
      (hit the known T-F96 MSB3231 race, tolerated per existing logic, installed
      `PavloRybchenko.Pakko_1.2.0.32_x64__9hkd8feqeqbr4` successfully). Three real checks against
      the installed, packaged app: (1) the one-click path verified by invoking
      `Archiver.Shell.exe --archive --format tar "<path>"` directly (the exact command line
      `TarArchiveCommand::Invoke` builds) — produced a real uncompressed `.tar` with correct
      content; (2) the Compress dialog's format selector verified through the real GUI via
      `pakko://archive` activation — all 7 formats present in the dropdown (ZIP/TAR/TAR.GZ/
      TAR.BZ2/TAR.XZ/TAR.ZST/TAR.LZMA), selected TAR.GZ and created a real archive, confirmed via
      `tar -tzvf`/extraction that it's genuinely gzip-compressed with correct content; (3)
      switched the format to plain TAR and confirmed via screenshot that the Compression combobox
      visibly greys out (`IsCompressionLevelEnabled` correctly wired end-to-end in the shipped
      build), while it stayed enabled for TAR.GZ and the initial ZIP default. All three checks
      passed with no fixes needed. Pulled forward from v1.5 2026-07-16 at user's explicit request,
      overriding T-F36's
      2026-07-07 deferral decision (see `DECISIONS.md`'s T-F36 correction entry for why it was
      deferred originally — re-scoped here exactly as that entry recommended: "add a
      create/compress method to the existing `ITarService`", not a from-scratch
      `IArchiveEngine`). Full design approved via Plan Mode
      (`C:\Users\Pa\.claude-work\plans\jazzy-snacking-pony.md`).
      **Phase A (Core layer) complete 2026-07-16:** `ArchiveContainerFormat` enum (`Zip, Tar,
      TarGz, TarBz2, TarXz, TarZst, TarLzma`), `ArchiveOptions.Format` field (default `Zip`,
      no existing caller/test affected), `ArchiveNaming.GetExtension()` (inverse of the existing
      `GetBaseName`; `ZipArchiveService.ArchiveAsync`'s two hardcoded `".zip"` literals now call
      it too), `ITarService.CompressAsync` + `TarSandboxedService` implementation (deliberately
      **unsandboxed** — see Security decision below), `IArchiveCreationRouter`/
      `ArchiveCreationRouter` (minimal single-branch dispatch, no per-path splitting needed since
      format is one explicit choice per call — simpler than `IExtractionRouter`), DI wired in
      `App.xaml.cs`. `dotnet test --filter "Category!=Slow"` green (309/309, +25: 11 new
      `TarSandboxedServiceCompressTests` integration tests against the real system tar.exe — all
      6 container formats round-tripped, multi-source-from-different-parents structure
      preservation, `SeparateArchives` mode, rename-conflict, missing-source error handling, and
      a real compression-level-has-an-effect check; 7 new `ArchiveNamingTests.GetExtension` theory
      cases; 7 new `ArchiveCreationRouterTests` dispatch cases). `MainViewModel`/XAML/shell
      extension are **not yet wired** to this router — Phases B/C below.
      **Empirical findings from Phase 0 (folded into planning, not a separate committed spike)
      that changed the original plan:** a bare `-9`-style level flag does **not** work on this
      tar.exe (bsdtar 3.8.4) — exit 1 — but `--options <filter>:compression-level=N` does, real
      output-size differences confirmed for all 5 write filters (gzip/bzip2/xz/zstd/lzma);
      `compression-level=0` is a genuine store/no-compression mode; plain `Tar` is the *only*
      format where compression level is truly meaningless (`--options` without an active filter
      fails with `Unknown module name`) — so the UI's compression-level combobox (Phase B) greys
      out only for plain TAR, not for every non-ZIP format as first assumed. `tar -v` writes its
      per-entry creation lines to **stderr**, not stdout (confirmed via direct `Process`
      redirection, not just shell piping) — this is how `CompressAsync`'s file-count progress is
      derived. See `DECISIONS.md`'s T-F105 entry for the full raw command output.
- **Depends on:** none (T-F47–T-F52's tar.exe plumbing already exists)

**What:** lets the user create TAR-family archives (plain `.tar`, and `.tar.gz`/`.tar.bz2`/
`.tar.xz`/`.tar.zst`/`.tar.lzma` via tar.exe's own write filters) in addition to Pakko's existing
ZIP-only creation. Two surfaces, deliberately different in scope (user-confirmed during scoping —
see the Plan file's "Обсяг shell-інтеграції" section):
- **"Compress…" dialog** (existing `Archiver.App` GUI, reached via the shell's
  `CompressDialogCommand`) gets a full format selector: ZIP + all 6 tar variants.
- **One new one-click Explorer context-menu command, "Add to X.tar"** — plain/uncompressed
  `.tar` only, mirroring the existing one-click "Add to X.zip" (`ArchiveCommand`). Deliberately
  **not** "Add to X.tar.gz" or any compressed variant — one-click commands never prompt the user
  for anything, so the one-click branch is limited to the one format with no filter/level choice
  to make; every compressed tar variant (including tar.gz) is reachable only through the dialog,
  where there's something to choose from.

**Security decision (recorded here per `CLAUDE.md`'s Documentation Map; canonical copy in
`SECURITY.md`'s new "Why Archive Creation Is NOT Sandboxed" subsection):** unlike T-F52's
extraction path, `CompressAsync` runs tar.exe **unsandboxed** — no `TarSandboxScope`/
AppContainer/Job Object. T-F52's threat model is specifically "a hostile *archive* drives
libarchive into misbehaving while parsing it" — creation has no untrusted-archive input at all,
just trusted local files the user selected, the same reasoning that has always left
`ZipArchiveService.ArchiveAsync` unsandboxed. The Authenticode signature check still runs before
every tar.exe launch regardless of direction.

**Acceptance criteria:**
- [x] `ArchiveContainerFormat` enum + `ArchiveOptions.Format` field (default `Zip`)
- [x] `ArchiveNaming.GetExtension(ArchiveContainerFormat)` + `ZipArchiveService` refactored onto it
- [x] `ITarService.CompressAsync` — unsandboxed, `SingleArchive`/`SeparateArchives` modes, real
      `-C <parent> <name>`-per-source multi-source structure preservation (empirically verified),
      `--options <filter>:compression-level=N` mapping from the existing ZIP `CompressionLevel`
      enum, temp-file-then-atomic-move (no partial files on cancel/failure), `ConflictResolver`
      reuse, never throws (`ArchiveError` on failure), Authenticode signature check before launch
- [x] `IArchiveCreationRouter`/`ArchiveCreationRouter` + DI registration
- [x] New tests: real tar.exe round-trip for all 6 formats, multi-source structure, both modes,
      conflicts, missing-source handling, compression-level effect — `dotnet test
      --filter "Category!=Slow"` passes (309/309)
- [x] **Phase B — App layer, complete 2026-07-16:** "Формат" `ComboBox` added to `MainWindow.xaml`
      right before the existing Compression combobox (ZIP default + 6 tar variants, `FormatIndex`
      int property mirroring the existing `CompressionLevelIndex`/`OnConflictIndex` pattern),
      localized via `x:Uid` across all 37 locale `Resources.resw` files (mechanical script insert —
      the 7 format names are technical/universal and stay untranslated Latin script in every
      locale, same as Windows Explorer itself; only the "Format:" label word is translated per
      locale). Compression-level combobox's `IsEnabled` now binds to a new
      `IsCompressionLevelEnabled` computed property (`IsNotBusy && !IsPlainTarFormatSelected`) —
      greys out only when plain TAR is selected, matching the Phase A empirical finding.
      `MainViewModel`'s constructor now takes `IArchiveCreationRouter` instead of `IArchiveService`
      (which had exactly one call site, `ArchiveAsync`'s `_archiveService.ArchiveAsync` — now
      `_archiveCreationRouter.ArchiveAsync`, same `ArchiveOptions`/`IProgress<ProgressReport>`
      signature, no adapter needed); `ArchiveOptions.Format = SelectedContainerFormat` added to the
      options object built in `ArchiveAsync`. **Correction on scope:** the "auto-name preview"
      item from the original plan doesn't apply — verified the actual UI first (per this project's
      own "verify UI behavior empirically" lesson) and found `ArchiveNameTextBox`'s `PlaceholderText`
      is a static, non-extension-specific hint ("Auto (based on first file/folder name)"); there is
      no dynamic filename/extension preview anywhere in `MainWindow.xaml` to wire up. The actual
      extension used when the name is left blank is still correct per format — that's
      `ArchiveNaming.GetExtension` inside `TarSandboxedService.CompressAsync`/`ZipArchiveService`
      (Phase A), unrelated to this (nonexistent) preview control. `dotnet build
      src/Archiver.App/Archiver.App.csproj /p:Platform=x64` succeeds; `dotnet test
      --filter "Category!=Slow"` still green (309/309 — no new App-layer tests, `MainViewModel` is
      not unit-tested per the project's existing "Known test gaps" section, consistent with how
      every prior ViewModel change in this file was verified)
- [x] **Phase C — Shell layer, complete 2026-07-16:** new one-click "Add to X.tar" C++ command
      (`TarArchiveCommand` in `ExplorerCommands.h`/`.cpp`, new CLSID
      `5F440071-6288-4446-AE25-3F4EDA490DDC` — needs no `Package.appxmanifest` entry, confirmed
      only the root command's CLSID is registered there and every leaf command is instantiated
      internally via `Make<T>()` inside `PakkoRootCommand::EnumSubCommands`; inserted right after
      `ArchiveCommand`/"Add to X.zip" per this project's context-menu-ordering convention, same
      `AllPathsAreZip`-based `GetState` hiding rule). `BuildAddToArchiveTitle` gained an `ext`
      parameter defaulting to `L".zip"` (existing call sites/tests untouched); `BuildArchiveArgs`
      gained a `format` parameter defaulting to `L"zip"` that only emits `--format <value>` for a
      non-zip format, so the pre-existing zip one-click command line is byte-for-byte unchanged.
      New `--format zip|tar` CLI switch parsed by `ShellArgumentParser.ParseArchive` (only these
      two values accepted — the one-click path never prompts the user, so it never needs the
      dialog's full 6-variant tar selection); `ParsedCommand` gained a `Format` field (default
      `Zip`). `Archiver.Shell/Program.cs`'s `RunArchiveAsync` now builds an `ArchiveCreationRouter`
      directly (`new ArchiveCreationRouter(new ZipArchiveService(), new TarSandboxedService())`)
      instead of calling `new ZipArchiveService().ArchiveAsync()`, and sets
      `ArchiveOptions.Format` from the parsed command.
      **Caught and fixed during implementation:** typed a literal ellipsis character (not the
      safe `…` escape) into `TarArchiveCommand::GetTitle`'s catch-block fallback while
      writing the new code — the exact mojibake bug class this file already documents having
      shipped three times (T-F64/T-F76/T-F63). Caught by re-reading the file immediately after
      the edit (not deploy-time), fixed via a direct byte-level PowerShell replacement (typing
      `…` through the Edit tool's JSON param decodes it back into the literal character —
      confirmed this happens even when deliberately trying to fix it that way, consistent with
      this project's own recorded `\uXXXX`-in-tool-param gotcha) rather than by re-typing it.
- [x] New tests for Phase C: `ShellArgumentParserTests` gained 7 cases (`--format` absent/zip/tar,
      multi-file with `--format tar`, unknown format value, `--format` with no value, `--format
      tar` with no files after) — 43/43 `Archiver.Shell.Tests` pass (was 36).
      `Archiver.ShellExtension.Tests` (Google Test) gained 9 cases: 4 `BuildArchiveArgs` (default/
      explicit-zip omit the flag, tar emits it, tar+multiple files) + 5 `BuildAddToArchiveTitle`
      `.tar`-extension mirrors of the existing `.zip` drive-root/compound-extension/truncation
      cases — 68/68 pass (was 59). No new `Archiver.App.Core.Tests` needed — Phase B's new
      `MainViewModel` logic isn't unit-testable per the project's existing "Known test gaps"
      (WinUI ViewModel, no test host).
- [x] `SPEC.md`'s roadmap/format table updated (v1.5→v1.4 move, format table clarified
      read-vs-write per format), `SECURITY.md`'s creation-vs-extraction subsection,
      `ARCHITECTURE.md`'s `IArchiveCreationRouter`/`ITarService.CompressAsync`/
      `ArchiveContainerFormat`/Phase B/Phase C documentation. **`DIAGRAMS.md` cascade check
      (resolved, no new diagram needed):** the new shell/COM surface is a single new leaf
      `IExplorerCommand` class that mirrors `ArchiveCommand` exactly (same COM lifecycle, same
      `EnumSubCommands` registration shape) — it adds a list entry to an existing diagram's
      command set, not a new branch or state machine; `ArchiveCreationRouter`'s own dispatch is a
      single ternary already fully described in prose in `ARCHITECTURE.md`, far simpler than
      `ExtractionRouter`'s per-path splitting that justified a diagram. No diagram in `DIAGRAMS.md`
      documents `PakkoRootCommand::EnumSubCommands`'s command *list* at that level of detail today
      (it covers COM activation/lifecycle and the extraction/op-lifecycle state machines) — adding
      one now would be a scope expansion beyond what T-F105 itself needs, not a same-commit
      requirement under the DoD table's existing triggers.
- [x] Full `Deploy.ps1` build+sign+install + on-device verification (both surfaces — Compress
      dialog with every format, and the one-click "Add to X.tar") — per this project's workflow
      rule, via the `windows` MCP server (user-directed, same as T-F52's graduation)

---

### T-F106 — Bug: Pending-List Rows Blank on Activation; Responsive Window-Size Design

- [x] **Status:** RESOLVED 2026-07-16. Found while screenshotting T-F105's Phase D verification.
      **Root cause (found in a later same-day follow-up, after five unrelated fix hypotheses were
      tried and disproven): never a WinUI rendering bug at all** — `RootGrid`'s file-table row
      had no `MinHeight` on its own `RowDefinition` (only on the `ListView` child, which doesn't
      force the row itself to grow); at the window's fixed 650px height, the pending-list mode's
      other rows (Archive Options' 4 rows — taller since T-F105 added the Format row — plus Shared
      Options/action buttons/status bar) collectively demanded more height than existed, clamping
      the table's Star row to 0. Every `ListView` item then measured within zero height, reporting
      `(0,0,0)` `ui_find` bounds — exactly the observed symptom, independent of population timing,
      data model, or XAML structure. Fixed by raising the default window size to 1100×900,
      converting `RootGrid`'s `RowDefinitions` to explicit elements with `MinHeight="200"` on the
      table row, and setting `PreferredMinimumWidth="900"`/`PreferredMinimumHeight="700"` via
      `OverlappedPresenter` — confirmed on-device (same 2-file `pakko://archive` repro used
      throughout the investigation) both at default size and at the enforced minimum (a requested
      600×400 resize was clamped to ~886×693 by Windows itself, table still fully visible). See
      `DECISIONS.md`'s final T-F106 entry for the full account, including why Archive Browser mode
      never reproduced this (its Archive Options panel collapses, freeing the Star row) and why it
      first appeared during T-F105 (the new Format row tipped the height balance).

**What:** launching Pakko via `pakko://archive` protocol activation (`Archiver.Shell.exe
--open-ui --archive <path(s)>`) shows the correct file *count* ("Буде заархівовано елементів: N")
but the `ListView` row(s) for the actual file(s) render **completely blank** — no visible Name/
Type/Size/CRC-32/Modified text at all, even though the underlying data is present and correct.

**Root-caused this far (not yet fixed):**
- `mcp__windows__ui_find(controlType: 'Text')` on the live window found the row's text elements
  *do* exist with correct bound values (`"note.txt"`, `"TXT"`, `"15 bytes"`, `"B3AA3209"`,
  `"2026-07-16 02:03"`) — so the ViewModel/binding pipeline itself is correct. But every one of
  those elements reports its UI-Automation bounding position as `(0,0)`, i.e. they're realized
  with effectively zero layout size — a rendering/layout bug, not a data bug.
- Resizing the window afterward (forcing a relayout) does **not** fix it — ruling out the
  previously-fixed T-F05 `VirtualizingStackPanel`/CRC-async-race mechanism (that one *did*
  resolve on a forced relayout; see `DECISIONS.md`'s T-F05 entry). This is a different cause.
- Reproduces identically with 1 file and with 2 files (neither row renders — unlike the old T-F05
  bug's shape, where adding a second item made the *first* item's row start rendering).
- **Leading hypothesis:** `App.xaml.cs::HandleActivation`'s Protocol-activation branch calls
  `EnsureWindow(...)` (which calls `_window.Activate()`, showing the window) and then, still
  synchronously on the same call stack, calls
  `_window!.ViewModel.AddPathsFromProtocolUri(...)` — populating `FileItems` before the
  `ListView` has had its first `Loaded`/layout pass. The File-activation branch (multi-file,
  non-Browse case, used by T-F100's double-click-multiple-files path) has the exact same
  `EnsureWindow(...)` → `AddPaths(...)` shape, so it may share the same bug — **not confirmed**,
  only the Protocol path was actually reproduced this session.
- **Not yet determined:** whether the normal "Додати файли" (Add Files, window already open and
  rendered before the user picks files) path is affected too — attempts to automate the WinRT
  file picker via the `windows` MCP server did not reliably complete a selection this session, so
  this remains unconfirmed either way. This is the first thing to check before assuming the bug
  is activation-only.

**Files likely involved:** `src/Archiver.App/App.xaml.cs` (`HandleActivation`/`EnsureWindow`),
`src/Archiver.App/MainWindow.xaml` (pending-list `ListView`, Row 1), possibly
`src/Archiver.App/ViewModels/MainViewModel.cs` (`AddPaths`/`AddPathsFromProtocolUri`).

**Acceptance criteria:**
- [ ] Confirm whether the "Add Files"/drag-and-drop path (files added after the window is already
      shown and interactive) reproduces the same blank-row symptom, to scope whether this is
      activation-timing-specific or a broader `ListView`/`FileItems` binding issue
- [ ] Confirm whether File-activation's multi-file `AddPaths` branch (T-F100) shares the bug —
      real double-click-multiple-files repro, not just protocol activation
- [ ] Root-cause the exact WinUI mechanism (not just the `EnsureWindow`-before-`AddPaths`
      correlation above) before choosing a fix — per this project's "pre-implementation research"
      rule, especially given this touches the same cold-start activation code T-F83 already had to
      fix once
- [ ] Fix implemented and verified on-device (screenshot showing real file names/sizes rendered,
      not just the correct count text) — **done**, confirmed via `ui_find` (non-zero bounds) and
      a screenshot at both default 1100×900 size and the enforced ~886×693 minimum
- [ ] New test coverage if the fix is unit-testable — not applicable, the fix is pure XAML
      layout (window size + `RowDefinition.MinHeight`), no logic moved into a testable class;
      documented as on-device-only verification like this file's other WinUI-only gaps

**Responsive window-size design (added 2026-07-16, user request) — separate sub-scope, same
task. Turned out to be the actual fix for the bug above, not a separate concern — see
`DECISIONS.md`'s final T-F106 entry:**
- [x] Explicit minimum window size set and enforced — `OverlappedPresenter.PreferredMinimumWidth`
      (900)/`PreferredMinimumHeight` (850, corrected same day from an initial insufficient 700 —
      at 700 the file table itself stayed visible but the two Shared Options checkboxes and the
      status bar clipped off the bottom instead; 850 was reached by testing progressively taller
      minimums until every row simultaneously reported non-zero `ui_find` bounds). Confirmed
      Windows itself clamps a smaller resize request to this floor, and the entire window's
      content — table, all options, both checkboxes, status bar — stays fully visible there
- [ ] Full on-device resize testing across the entire range for **both** modes remains partial —
      pending-list mode confirmed at default size and at the enforced minimum; archive-browser
      mode (T-F05's alternate Row 0/1/3) not separately re-tested this round (it was never
      affected by the original bug, since its Archive Options panel collapses, but its own
      clipping/overlap behavior across the resize range hasn't been explicitly re-checked against
      the new 900×700 floor)
- [x] Chosen minimum based on real constraints, not a guess — 900×700 comfortably fits every row's
      content at that floor (confirmed via the on-device minimum-size screenshot) and is well
      under a typical 1024×768 driver-less floor once taskbar/chrome are subtracted
- [ ] Basic-Display-Adapter/driver-less floor scenario not literally tested on real
      hardware this round (no such machine available) — the 900×700 minimum's arithmetic margin
      under 1024×768 was reasoned through, per `DECISIONS.md`'s reasoning, but not empirically
      confirmed on an actual VGA-fallback display
- [ ] `frontend-design` advisor checkpoint (originally planned for this sub-scope, matching T-F05's
      window-proportions review) not run this round — worth a quick pass to confirm 1100×900 still
      reads as intentionally wide, not accidentally near-square, before fully closing this out
- [ ] Document the chosen minimum dimensions and the reasoning (which row/control was the binding
      constraint) in `XAML.md` or `DECISIONS.md` so a future layout change doesn't silently
      reintroduce a size that no longer fits the driver-less floor

---

### T-F107 — Archive Browser: Climb Past the Archive Root into the Real Filesystem
- [x] **Status:** done — implemented 2026-07-16 (user-driven UX request, planned via Plan Mode),
      `dotnet test --filter "Category!=Slow"` green (326/326: 26 Archiver.App.Core.Tests + 43
      Archiver.Shell.Tests + 211 Archiver.Core.Tests + 46 Archiver.Core.IntegrationTests — the
      jump from 300 to 326 reflects this task's new `FileSystemBrowserTests`), full
      `Deploy.ps1` build+sign+install completed (1.2.0.35), and AI-driven on-device verification
      passed against fresh fixtures (a real two-archive test folder, not the stale
      `browse_test*` fixtures from earlier tasks): climbed from inside `test1.zip` through its
      real containing folder, up through `Desktop`/`Pa`/`Users`/`C:\`, to "Цей комп'ютер" (up
      button confirmed `enabled: false` there via UIA, not just visually); double-clicked
      `C:\` to descend back into the drive; navigated back down to the test folder and
      double-clicked `test2.zip` (a different real archive) while browsing real folders,
      confirming it opens fresh; Extract Selected/All confirmed disabled throughout real-
      filesystem browsing (even with a row selected) and re-enabled immediately once back inside
      an archive. `DIAGRAMS.md` diagram 6 was already updated in the implementing commit
      (adbd759). Graduated to `[x]`.
- **Depends on:** T-F05 (Archive Browser)

**What:** the Archive Browser's "Up" button used to fall through to exiting the browser entirely
(back to the pending list) once you reached the archive's own root — confusing on-device, since it
looked like an unrelated screen appeared. Per user request (patterned after NanaZip), "Up" now
keeps navigating past the archive root: into the archive's real containing folder, up through real
parent folders, up to a drive root, and up to a synthetic "This PC" node listing all drives — only
greying out at "This PC" (nothing higher), mirroring `CanNavigateDestinationUp`'s existing
drive-root disable pattern.

**Research finding (see `DECISIONS.md`):** NanaZip's own equivalent behavior is NOT free Explorer
shell-namespace behavior — a repo-wide grep of `M2Team/NanaZip` found zero `IShellFolder`/
`IPersistFolder2` usage. It comes from NanaZip's own hand-coded classic FileManager
(`NanaZip.UI.Classic`, inherited from 7-Zip's `7zFM`), which hand-builds a unifying
`IFolder`-family abstraction across archive contents, real folders, and a "Computer"/"Network"/
drive-root tree. Pakko has no shell-namespace component at all and had zero prior real-filesystem
browsing code, so this had to be built from scratch — same conclusion NanaZip's own history
reached.

**Explicit non-goal:** Windows "Network" (Network Neighborhood) enumeration is out of scope for
this round — would require COM Shell32 `IShellFolder` interop with no simple .NET equivalent, real
effort for a rarely-used path. Scope is real folders → drive roots → "This PC" (drives list) only.

**Acceptance criteria:**
- [x] `BrowseScope` (`Archive`/`RealFileSystem`/`ThisPc`) added to `MainViewModel`; drives what
      `CurrentFolderPath`/`CurrentFolderEntries`/breadcrumb mean and where "Up" goes next
      (shipped as `ArchiveBrowseScope`/`BrowseScope`)
- [x] New `FileSystemBrowser` static helper in `Archiver.App.Core` (parallel to `ArchiveTreeIndex`,
      unit-testable without a WinUI host): `ListFolder(path)` and `ListDrives()`, both returning
      the existing `ArchiveEntryViewModel` unchanged (no new model needed — its optional
      `CompressedSize`/`Crc32`/`Modified` fields already tolerate "not applicable")
- [x] `NavigateUpOrExitBrowser` renamed to `NavigateUp`, gains `CanNavigateUp()` (false only at
      `ThisPc`) mirroring `CanNavigateDestinationUp`'s exact idiom; `ExitBrowseMode()` deleted
      entirely (no callers left — confirmed via repo grep)
- [x] Double-clicking a different real archive found while browsing real folders opens it fresh
      via the existing `EnterBrowseModeAsync` (same trust level as today's pending-list
      double-click gate — **not** T-F98's deferred nested-archive-drill-down, which is a different,
      higher-risk scenario: archives found *inside* the currently open archive)
- [x] New `FileSystemBrowserTests` in `Archiver.App.Core.Tests` (folders-first/alphabetical
      ordering, correct Size/Modified population, graceful empty list for a nonexistent/
      inaccessible path)
- [x] `dotnet test --filter "Category!=Slow"` passes (326/326)
- [x] Manual on-device verification: climb from inside an archive up through its real containing
      folder, up to a drive root, up to "This PC" (button greys out there); double-click a drive to
      descend; double-click a different real archive to open it fresh; Extract Selected/All grey
      out while outside any archive and re-enable once back inside one
- [x] `DIAGRAMS.md` diagram 6 (T-F05's row-visibility/up-button history) updated to reflect
      `BrowseScope`-driven navigation

---

### T-F75 — Correctness Bug: Nested Subdirectory Entries Lost Their Path Prefix
- [x] **Status:** complete — **confirmed shipped in tagged v1.1.0**, found 2026-07-06 while
      investigating T-F30

**What:** `AddDirectoryToArchiveAsync` computed each entry's relative path against the current
recursion level's own immediate parent, recomputed fresh every level, instead of against the
true archived root held constant. Archiving a directory nested two or more levels deep produced
ZIP entries missing their accumulated prefix — e.g. `notes/sub/file.txt` was written as just
`sub/file.txt`. Worse: two files at different depths whose path relative to their own immediate
parent happened to match could collide into the *same* entry name, and since
`ZipArchive.CreateEntry` doesn't reject duplicates, the second write silently clobbered the
first on extraction. See `DECISIONS.md`'s "T-F75" entry for the full trace and root-cause detail.

**Fix:** `AddDirectoryToArchiveAsync` gained a `rootDir` parameter, fixed across all recursion,
used (with `entryPrefix`) to compute every entry name against the true root regardless of depth.
The T-F66 empty-subdirectory special case had the identical bug and is fixed the same way.

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`,
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`

**Acceptance criteria:**
- [x] `AddDirectoryToArchiveAsync` computes entry names against a fixed `rootDir`, not each
      recursion level's own parent
- [x] Empty-subdirectory entries (T-F66) also computed against the fixed root
- [x] `ArchiveAsync_FolderWithEmptySubfolder_PreservesEmptySubfolderEntry` updated — it asserted
      the bug's own output (`EmptyChild/`) as correct; now expects `Parent/EmptyChild/`
- [x] New test: 3-level nesting — entry names include the full path from root at every depth
- [x] New test: sibling subdirectories with matching relative structure no longer collide into
      one entry name; archive → extract round trip preserves both files' distinct content
- [x] `dotnet test` passes — 79/79 (was 77/77)
- [ ] Decide whether this warrants a v1.1 patch/release note for early testers (flagged to user,
      not yet decided)

---

### T-F84 — Bug: Deploy.ps1's Post-Build Hook Fails on Cyrillic-Locale Machines (Mojibake)
- [x] **Status:** complete — found and fixed 2026-07-07 while verifying T-F47/T-F48 built cleanly
      in Visual Studio

**What:** Found while asking Visual Studio to build the solution (needed since `dotnet build`
cannot build `Archiver.App`, a WinUI 3 project). The Release build failed with `MSB3073`: the
post-build hook's `Deploy.ps1 -DeployOnly` invocation exited with code 1.

**Root cause:** the same mojibake bug class documented three times already in this project's C++
code (T-F64, T-F76, T-F63), now found for the first time in a PowerShell script. `Deploy.ps1` line
204 had a literal em-dash inside a `Write-Warning` string; the file is UTF-8 without a BOM, and
Windows PowerShell 5.1 decoded it via the system ANSI code page (cp1251, Cyrillic locale) instead
of UTF-8, corrupting the em-dash into `вЂ”` and breaking the string's terminator — reported by the
parser as misleading `Missing closing '}'` errors several lines away. See `DECISIONS.md`'s "T-F84"
entry for the full trace.

**Fix:** replaced the em-dash with a plain ASCII hyphen. `grep -P "[^\x00-\x7F]"` run over every
`scripts/*.ps1` (not just `Deploy.ps1`) found one more live instance in `Setup-DevCert.ps1` line
21 — fixed the same way; that script is arguably higher-risk since it explicitly relaunches
itself via `Start-Process powershell` (Windows PowerShell) when not elevated. The many
em-dash/box-drawing comment dividers in both files are unaffected (comments don't need a matching
terminator) and were left alone. `CONVENTIONS.md` gained a new "PowerShell Scripts" section for
this rule.

**Acceptance criteria:**
- [x] `scripts/Deploy.ps1`'s em-dash replaced with an ASCII-safe substitute
- [x] Every other `scripts/*.ps1` file checked (`grep -P "[^\x00-\x7F]"`) — `Setup-DevCert.ps1`'s
      matching bug found and fixed too
- [x] `[System.Management.Automation.Language.Parser]::ParseFile`, run via real `powershell.exe`
      (Windows PowerShell 5.1, the actually-vulnerable interpreter — pwsh 7 would pass either way),
      confirms zero parse errors on both files after the fix
- [x] `Deploy.ps1 -DeployOnly` run directly completes successfully (installed Pakko 1.1.0.42)
- [x] Visual Studio Release build of the full solution completes with 0 errors / 0 warnings
- [x] `CONVENTIONS.md` updated so this bug class is documented for PowerShell scripts too, not
      just C++ (`CLAUDE.md`'s hard constraint intentionally left alone — out of scope without
      explicit sign-off, per its own "Do Not modify CLAUDE.md" rule)

