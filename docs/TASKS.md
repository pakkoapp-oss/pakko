# TASKS.md — Active and Future Tasks

> Completed tasks (T-01 through T-35, T-11) are archived in [`TASKS_DONE.md`](TASKS_DONE.md).
> **v1.0 is complete.** All items below are post-v1.0 future work.
>
> **Graduated 2026-09-30 (after v1.6.0):** these completed tasks now live in [`TASKS_DONE.md`](TASKS_DONE.md), section "Graduated 2026-09-30": T-F05, T-F116, T-F124, T-F125, T-F126, T-F129, T-F35, T-F51, T-F131, T-F133, T-F134, T-F136, T-F137, T-F138, T-F142, T-F143, T-F147, T-F148, T-F149, T-F159, T-F160, T-F164, T-F171, T-F172, T-F195, T-F196, T-F197, T-F198, T-F199, T-F200, T-F201, T-F203, T-F204, T-F205, T-F207, T-F208, T-F209, T-F210, T-F212, T-F213, T-F214, T-F215, T-F216, T-F217, T-F218, T-F219, T-F222, T-F224, T-F227, T-F228, T-F229, T-F230, T-F232, T-F233, T-F234, T-F237, T-F238, T-F239, T-F240, T-F242, T-F248, T-F249, T-F250, T-F252, T-F253, T-F255, T-F256, T-F257, T-F258, T-F259, T-F262, T-F264, T-F265, T-F267, T-F269, T-F270, T-F271, T-F272, T-F283, T-F287, T-F289, T-F273, T-F274, T-F276, T-F277, T-F278, T-F279, T-F223, T-F305, T-F174, T-F175, T-F176, T-F177, T-F178, T-F179, T-F180, T-F181, T-F182, T-F183, T-F184, T-F185, T-F186, T-F187, T-F188, T-F189, T-F190, T-F191, T-F193.

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
      worked until then. **Trigger condition met 2026-08-04** — T-F129 is now `[x]` done and the
      Store listing is live (see its entry above); the SignPath Foundation reapplication decision
      itself is still open and needs the user's call, not auto-started here.
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

### T-F135 — SonarCloud Static Analysis Integration (CI)
- [~] **Status:** partial — 2026-07-27. `build.yml`'s `test` job runs JDK setup →
      `dotnet-sonarscanner begin` → the existing `dotnet test` → `dotnet-sonarscanner end`, guarded
      to skip cleanly (not fail) on fork-originated PRs that have no `SONAR_TOKEN` available. Real
      SonarCloud org/project now exist and are confirmed reachable via SonarCloud's public API
      (org key **`pakkoapp-oss-1`**, project key **`pakkoapp-oss-1_pakko`** — note the `-1`, since
      the plain `pakkoapp-oss` key was already taken when the org was created; this does NOT match
      the GitHub org/account name, don't assume it does). `SONAR_TOKEN` repo secret added by the
      user 2026-07-27 (confirmed present via `gh secret list`, value itself not readable/verified
      by the agent). Both keys wired into `build.yml`'s env vars and `README.md`'s quality-gate
      badge; `scripts/README.md` documents the real keys and remaining manual steps.
      **Correction:** the org/project were created via SonarCloud's "Analyze manually" flow (not
      GitHub-App binding), because the importing account lacked Admin on the repo — `pakkoapp-oss`
      is a personal GitHub User account, and personal-account repos don't grant collaborators
      Admin/Maintain roles (same fact already noted elsewhere in this file for T-F124). This means
      PR decoration/auto-binding is not automatically part of this setup; CI-based analysis via the
      scanner still works regardless. **Analysis Method:** high-confidence CI-based already, by
      construction rather than a direct settings check — the user received SonarCloud's
      "Other CI"/manual `dotnet-sonarscanner begin`/`end` setup snippet, which SonarCloud only
      surfaces for CI-based projects (Automatic Analysis needs the GitHub App installed with repo
      Admin, which was never available here — see the "Analyze manually" correction above; that
      path structurally can't end up in Automatic mode). Not a substitute for eyeballing
      Administration → Analysis Method directly, but strong enough not to block on. **Still open
      before this graduates to `[x]`:** one real CI run (a push, since PR runs against `main` need
      an actual PR) completing successfully, then checking the result against the live SonarCloud
      dashboard, not just a green Actions run. C++ (`Archiver.ShellExtension`) stays deliberately
      out of scope for this pass. This project's own rule for CI changes (T-F122/T-F125
      precedent): don't graduate on YAML/API-reachability alone — a real analyzed commit has to
      show up on the dashboard.
      **Coverage follow-up, same day:** the org's default "Sonar way" quality gate requires new
      code coverage ≥ 80% — with no coverage submitted, that condition reports "no data" and fails
      the gate regardless of everything else. `dotnet test` now runs with
      `--collect:"XPlat Code Coverage"` (via the `coverlet.collector` package every test project
      already references — no new dependency) and `dotnet-sonarscanner begin` picks up the
      resulting reports via `/d:sonar.cs.cobertura.reportsPaths="**/coverage.cobertura.xml"`. This
      feeds a real number into the gate; it does not guarantee that number clears 80% — a real gate
      failure from insufficient coverage on new code is a legitimate signal, not a setup bug.
      **Branch-naming bug found and fixed, same day:** SonarCloud's manual project creation
      defaulted the designated "Main Branch" to the literal name `master`, which this repo has
      never had (only `main`) — every real analysis was landing on a short-lived, non-main branch
      record instead, so `qualitygates/project_status` returned `NONE` and the README badge would
      have shown a stale/unanalyzed state indefinitely. Fixed via SonarCloud's Administration →
      Branches → Rename Branch (`master` → `main`); confirmed via `api/project_branches/list`
      that `main` is now `isMain: true`, `type: LONG`. A second real CI run (empty commit
      `9144efa`, pushed specifically to re-trigger analysis against the corrected branch) confirmed
      via `api/ce/component` (status `SUCCESS`, `branch: main`, `branchType: LONG`) and
      `api/measures/component` real project-wide numbers: 16 bugs, 8 vulnerabilities, 245 code
      smells, 74.8% coverage, 2.1% duplication, reliability E, security C, maintainability A.
      **Quality Gate itself still reads `NONE` — expected, not a bug:** all 6 "Sonar way"
      conditions evaluate against *New Code* (delta from a previous analysis), and this was the
      first-ever analysis of the correctly-named branch, so there's no prior baseline to diff
      against yet. It becomes evaluable starting with the next analysis.
      **Still open before `[x]`:** (1) a third analysis, so the Quality Gate actually evaluates to
      a real PASSED/FAILED instead of `NONE`; (2) the "existing findings triaged" acceptance
      criterion below — 16 bugs / 8 vulnerabilities / 245 code smells currently sit unreviewed,
      real remaining work, not a CI-wiring gap.
- **Depends on:** T-F122 (`build.yml` CI workflow, done)

**What:** wire SonarCloud into `.github/workflows/build.yml` so every push/PR gets a real static-
analysis pass (bugs, vulnerabilities, code smells, duplication) across the .NET projects, not just
`dotnet test` green. Must stay **free** — SonarCloud's free tier only applies to genuinely public
repositories, which this repo is (confirmed: `origin` is `github.com/pakkoapp-oss/pakko`, public).
Directly continues this project's own hard constraint: *"A CI-run static analyzer's findings on
your own PR/commit get fixed in the same pass ... tests prove behavior, an analyzer proves
robustness to future change"* — today that rule has no analyzer feeding it in CI at all.

**Acceptance criteria:**
- [ ] Confirm the project qualifies for SonarCloud's free plan (public GitHub repo, no private
      forks in scope) before doing any setup work — don't assume, check their current published
      terms at the time this is picked up
- [ ] SonarCloud project created and linked to `pakkoapp-oss/pakko` (organization + project key)
- [ ] `SONAR_TOKEN` added as a GitHub Actions secret (repo secret, never committed to a tracked
      file — per this project's public-repo-hygiene rule)
- [ ] New step(s) in `build.yml`'s `test` job (or a new dedicated job) run the SonarCloud C#
      scanner (`dotnet-sonarscanner begin`/`end`) wrapping the existing `dotnet build`/`dotnet
      test` invocation, so coverage/analysis data comes from a real build+test run, not a
      standalone lint pass
- [ ] Scan scope covers the .NET projects (`Archiver.Core`, `Archiver.App`, `Archiver.App.Core`,
      `Archiver.Shell`, `Archiver.CLI`) — decide during implementation whether the C++
      `Archiver.ShellExtension` project is in scope for this same pass or deferred (SonarCloud's
      C++ analysis has separate licensing/setup considerations; don't assume it's free-tier
      covered without checking)
- [ ] A real CI run (not just a local read of the YAML) completes and a real SonarCloud project
      dashboard shows results — this project's own rule for CI changes (T-F122/T-F125 precedent):
      graduate only after a real workflow run + a real check against the actual SonarCloud UI, not
      on green YAML syntax alone
- [ ] `README.md` gets a SonarCloud quality-gate badge (standard practice for public OSS repos
      using it) — small, teaser-only, per this project's canonical-topic-owner rule (no duplicated
      detail, just the badge/link)
- [ ] Any findings SonarCloud reports against **existing** code at first-scan time are triaged (not
      silently ignored) — real bugs/vulnerabilities fixed, accepted false positives suppressed with
      a reason via SonarCloud's own suppression mechanism, not a blanket `// NOSONAR` sweep
- [ ] `docs/TASKS.md`/`CLAUDE.md` updated once this ships, so the existing "CI-run static analyzer"
      hard constraint has a real analyzer to point at

---

### T-F144 — Community: r/dotnet post about Pakko (post-Store-release)

- [ ] **Status:** not started — community/marketing task, not a code change. Tracked here (not a
      GitHub Issue) per this project's existing convention of using this file as the single
      backlog, engineering or not.
- **Context:** an earlier r/foss post ("I built a Windows archiver with zero 7-Zip/WinRAR code in
  it, because I stopped trusting their supply chain for gov work") got real engagement — both
  supportive and skeptical (solo-dev trust, AI-generated-code concerns, "why reinvent the wheel").
  Pakko now has a live, certified Microsoft Store release
  (https://apps.microsoft.com/detail/9p5mw010d8pr), which directly answers the "why trust your
  .exe" objection from that thread — worth a follow-up post in r/dotnet specifically.
- **r/dotnet's actual self-promotion rules (confirmed via the subreddit's own rules panel,
  screenshotted 2026-08-06 — not assumed from a third-party summary):**
  - Self-promo posts only allowed on **weekends**.
  - Must be flaired **"Promotion"**.
  - **Must not be written or generated by AI** — "Put some effort into it." (Rule 2 separately
    reinforces this for the whole subreddit, not just self-promo posts.) This means the agent
    should not draft the final post text — only help structure talking points; the user writes
    the actual post in their own words.
  - Restricted to **major/minor release versions** (not patch releases) — current version is
    `v1.4.7`, which qualifies.
- **Planned content (agreed in conversation, 2026-08-06):**
  - Title: technical/informational framing (e.g. "I built an open-source Windows archiver with
    WinUI 3 and .NET 8 — here's the source"), not a bare "check out my app" pitch.
  - Link order: GitHub repo first (https://github.com/pakkoapp-oss/pakko), Microsoft Store link
    second, as a convenience for people who don't want to build from source.
  - Central technical topic: the sandbox-escape exploit found during T-F49/T-F52 design review —
    a naive "extract-to-quarantine-then-validate" model was vulnerable to a symlink entry writing
    outside the quarantine directory before any validation code ran; fixed by pre-scanning and
    rejecting the whole archive before extraction ever starts, plus running extraction itself
    inside an AppContainer sandbox. This is a concrete, verifiable architecture story, not a
    generic feature list.
  - Secondary talking point (if there's room): the zero-byte-file `DeflateStream`/Zip64
    field-offset bugs from T-F35 — both were invisible to Pakko's own test suite and only caught
    by cross-validating output against an independent reader (real 7-Zip), a methodology point
    likely to land well with an engineering audience.
  - Deliberately avoid the "Russian developers" framing used in the r/foss post — it drew sharp
    pushback there even from a sympathetic audience, and r/dotnet is broader/less primed for it.
    Keep the trust argument on supply-chain/reproducible-builds grounds only.
- **Acceptance criteria:**
  - [ ] User picks and confirms a target Saturday (or other weekend day) to post.
  - [ ] User writes the actual post text themselves (per the no-AI rule above); agent may review
        structure/technical accuracy on request, not author it.
  - [ ] Post published with the "Promotion" flair.
  - [ ] Result (received well / removed / notable feedback) recorded back into this entry after
        the fact, since it may inform whether a similar r/Windows post is worth attempting later.
- **Reported by:** user, 2026-08-06 — "Створи під це таску... таску не забудь під публікацію на
  дотнеті сабредіті для мене" (open a task for this — don't forget the task for the r/dotnet
  subreddit post).

---

### T-F146 — AMSI-based "Scan for threats" for archives (Explorer context menu + Archive Browser)

- ~~Blocked from graduating by T-F247~~ — T-F247 fixed 2026-09-27 (1b0f824/c4fe0d4); pending
  its device check.
- [~] **Status:** implementation complete 2026-08-07 (Core service + tests, `Archiver.Shell`
      CLI/dialog, `Archiver.ShellExtension` context-menu entry, `Archiver.App` Archive Browser
      entry, full 37-locale localization across all three frontends) — on-device verification
      still pending (see acceptance criteria below), per this project's standing rule that
      shell-triggered/UI behavior never graduates on `dotnet test` alone. Built via a plan
      reviewed by `advisor` before any code was written, including a Phase 0 empirical spike
      (real EICAR through a real `.tar.gz`) that corrected the original "AMSI never
      deletes/quarantines" remediation-policy claim and confirmed a no-provider-registered
      discriminator — see `docs/DECISIONS.md`'s T-F146 entry for both findings.
      Supersedes T-F145 (see `docs/TASKS_DONE.md`'s T-F145 entry) — T-F145's own two required
      entry points and government/defense-audience rationale carry over unchanged, only the
      invocation mechanism and scan-target questions it left open are now resolved below.
- **Context:** same as T-F145's — Pakko's threat model already treats every archive as untrusted
  (T-F49's whole-archive pre-scan, T-F52's AppContainer sandbox); an explicit "scan this archive
  for threats" action is a natural complement to the existing "Test archive" (T-F62) diagnostic
  command (verify before you trust).
- **Resolved design decisions (session 2026-08-07 — full rationale, the AMSI probe transcript,
  and the rejected alternatives belong in `docs/DECISIONS.md`'s T-F146 entry before code is
  written):**
  - **Invocation mechanism: AMSI** (`amsi.dll` — `AmsiInitialize`/`AmsiOpenSession`/
    `AmsiScanBuffer`), not `MpCmdRun.exe` or WMI `MSFT_MpScan` as T-F145's stub proposed. Both of
    those require an elevated process per Microsoft's own docs — a permanent context-menu entry
    that UAC-prompts on every click was never going to ship. AMSI needs no elevation (confirmed
    empirically: a non-elevated `AmsiScanBuffer` call against a standard EICAR test buffer
    returned `AMSI_RESULT_DETECTED` on this dev machine, and a clean buffer returned
    `AMSI_RESULT_NOT_DETECTED`), dispatches to whichever AV/EDR is actually registered as the
    AMSI provider (not Defender-specific, so the "third-party EDR shadows Defender" branch T-F145
    worried about no longer needs separate handling), and is plain P/Invoke — zero new NuGet
    references, same pattern as `Services/Sandbox/`. `MpCmdRun.exe` stays a documented-but-not-
    implemented fallback idea for content too large to buffer in memory for `AmsiScanBuffer` (see
    "Not in scope" below) — do not build it speculatively until a real large-archive case proves
    AMSI's practical buffer limit is actually hit.
  - **Scan target/depth: quarantine-expanded contents only, no shallow "scan the archive file as
    one opaque blob" mode.** Windows Explorer already ships a native "Scan with Microsoft
    Defender" verb for any file/folder/drive (confirmed via research) — a shallow scan of the
    archive file itself would just duplicate that. Pakko's actual differentiated value is
    scanning what the OS verb cannot reliably reach: the *expanded* contents, reusing T-F49's
    pre-scan and T-F52's `TarSandboxScope.OutputDirectory` quarantine machinery so this costs no
    extra extraction beyond what a real Extract would already do.
  - **No generic "scan any file/folder" context-menu entry.** Explicitly scoped out — archive
    selections only, both entry points. Rationale: the OS-native verb already covers plain
    files/folders; adding Pakko's own would be pure duplication with no depth/format advantage
    (unlike the archive case).
  - **Result model: three states, not two.** `Clean` / `ThreatDetected` / `Inconclusive` (AV
    unavailable, provider call failed, or content skipped as un-scannable) — `Inconclusive` must
    never be silently rendered as `Clean`. Kept out of `ArchiveResult`/`ArchiveError` entirely
    (T-F145's own note, reaffirmed) — needs its own result type and its own dialog, patterned
    after `RunHashAsync`/`ShowHashResults` (T-F128) rather than the extraction-summary dialog.
  - **Remediation policy: report-only.** AMSI itself never deletes/quarantines anything — it only
    answers "is this safe." Pakko does not take any destructive action on a detection; it reports
    and stops (same "verify, don't act" posture as Test Archive). A detected threat aborts the
    whole operation — no partial extraction is ever left behind, and the quarantine directory is
    deleted whether the scan finds something or not.
  - **Two required entry points (unchanged from T-F145):**
    1. **Explorer right-click dropdown** — a new leaf `IExplorerCommand` in
       `Archiver.ShellExtension` (`PakkoRootCommand::EnumSubCommands`), shown only for archive
       selections, positioned after "Test archive" (same diagnostic-command-ordering rule).
       Default and only mode is the deep/quarantine-expanded scan above — no separate
       shallow-mode menu entry.
    2. **Archive Browser (T-F05/T-F97/T-F98)** — a scan action scoped to the whole open archive or
       the current selection via the same `ExtractOptions.SelectedEntryPaths` machinery T-F97/T-F98
       already use. **Relocated 2026-08-08** (user design feedback, real on-device screenshot):
       originally placed as a third button next to Extract Selected/Extract All in Row 3; moved to
       Row 0 next to "Про програму"/About instead, since it's a diagnostic action (same category as
       "Test archive"), and users expect just the Extract pair in the row they already know from
       every other archiver.
  - **Not in scope for this task (deferred, do not implement speculatively):**
    - A pre-commit auto-scan wired into every ordinary Extract (scanning quarantine contents
      before they're copied to the user's real destination, as an opt-in setting). Real idea,
      genuinely the strongest security posture, but a separate scope/UX decision (a setting,
      default-on-or-off, performance cost on every extraction) — track as a future task if wanted
      once T-F146 itself has shipped and been used.
    - `MpCmdRun.exe` fallback for oversized content (see invocation mechanism above).
    - A generic file/folder scan entry point (see above).
  - **Localization.** New context-menu string needs `StringId` entries across all 37
    `Archiver.ShellExtension` locales (T-F91 precedent); the Archive Browser button needs `x:Uid`
    wiring across `Archiver.App`'s locale `.resw` files; the three result states (Clean/Threat/
    Inconclusive) need their own localized copy, written in the interface's plain, active-voice
    register (no apology on detection, no vague "something went wrong" for Inconclusive — state
    what's actually known: no active AV / provider call failed).
- **Acceptance criteria:**
  - [x] AMSI wrapper (`Archiver.Core/Services/Antivirus/AmsiScanner.cs` + `AmsiProviderCheck.cs`,
        orchestrated by `Archiver.Core/Services/AntivirusScanService.cs`) implemented and
        unit-tested against a real EICAR buffer (generated at runtime, never committed to disk)
        and a clean buffer, mirroring the probe already run during design
        (`tests/Archiver.Core.Tests/Services/Antivirus/AmsiScannerTests.cs`). A shared
        `ArchiveFormatPolicy` (extracted from `ExtractionRouter`, behavior-preserving) keeps
        Group Policy gating identical between Extract and Scan. Tar-family quarantine-scan path
        additionally covered end-to-end against real `tar.exe` in
        `tests/Archiver.Core.IntegrationTests/AntivirusScanServiceTarTests.cs`.
  - [x] Explorer context-menu entry point implemented (`ScanCommand` in
        `Archiver.ShellExtension/ExplorerCommands.cpp`, `AnyPathIsSupportedArchive`-gated so
        tar-family archives show it too — deliberately not `AnyPathIsZip` like `TestCommand`),
        ordered after "Test archive", localized across all 37 `Archiver.ShellExtension` locales
        (`Localization.cpp`).
  - [x] Archive Browser entry point implemented (`MainViewModel.ScanArchiveFromBrowserCommand`,
        one combined button — scans the current selection if any is checked, else the whole open
        archive), scoped via `AntivirusScanOptions.SelectedEntryPaths`, localized across all 37
        `Archiver.App` locale `.resw` files. **Real bug found 2026-08-08** (user screenshot: the
        button was permanently disabled while browsing any ZIP): `CanScanArchiveFromBrowser()`
        reads `IsBusy`/`BrowsedArchivePath`, but neither `[ObservableProperty]` field carried
        `[NotifyCanExecuteChangedFor(nameof(ScanArchiveFromBrowserCommand))]` — the command's
        `CanExecute` was never re-evaluated after either changed, so it stayed at its
        construction-time (both-default, i.e. disabled) state forever. Fixed by adding the missing
        attribute to `_isBusy`, `_isBrowsingArchive`, and `_browsedArchivePath` — same pattern
        `ExtractAllFromBrowserCommand`/`ExtractSelectedFromBrowserCommand` already had, which is
        why only the newer Scan command was affected. User confirmed on-device 2026-08-08 the
        button now enables and the Row 0 relocation reads correctly — the EICAR-detection and
        `Inconclusive`-path criteria below still need their own dedicated check.
  - [x] Three-state result dialog implemented — `Archiver.Shell`'s `RunScanAsync`/
        `ShowScanResults` (pattern: `RunHashAsync`/`ShowHashResults`) and `Archiver.App`'s
        `IDialogService.ShowThreatScanResultAsync` — never collapsing `Inconclusive` into `Clean`;
        clean copy is "No threats found in this archive," never "safe" (Pakko doesn't recurse
        into nested archives).
  - [x] A detected threat aborts the operation cleanly — the tar-family path's quarantine
        directory is always deleted via `TarSandboxScope.Dispose()` (`using`), and the ZIP path
        never writes to disk at all; nothing is ever extracted to a real destination by a scan.
  - [x] `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` green repo-wide (Core service
        tests, tar-family integration tests, `ShellArgumentParser` `--scan` tests), plus the C++
        `Archiver.ShellExtension.Tests.exe` suite (`BuildScanArgs` cases) — all green.
  - [ ] On-device verification per this project's standing rule (shell-triggered/UI behavior is
        not graduated on `dotnet test` alone) — including a real EICAR-file detection case
        through both entry points, not just a clean-archive happy path, and a check of the
        `Inconclusive` path (e.g. with Defender's real-time protection temporarily disabled/no
        AMSI provider active). The tar-family EICAR case needs a temporary Defender exclusion
        folder added/removed by the user themselves — see `docs/DECISIONS.md`'s Phase 0 finding
        for why this can't be scripted by the agent.
- **Reported by:** user, 2026-08-06 (original ask, as T-F145) — "Додай таску щоб можна було
  викликати перевірку віндовс дефендера для файлів архіву. Один для випадаючого списку в
  експлорері. Інший у в'ювері архівів." (Add a task so a Windows Defender check can be invoked
  for archive files — one for the Explorer dropdown, another in the archive viewer.) Scope
  refined 2026-08-07 after the user asked for a full design pass ("Продумай задачу з додаванням
  перевірки антивірусом. Порадся з адвізором та дизайнером...") — see `docs/DECISIONS.md`'s
  T-F146 entry for the full options considered and why each was accepted/rejected.

### T-F152 — VirusTotal Hash Lookup Link in Archive Browser (deferred — rejected as-scoped)

- [~] **Status:** deferred 2026-08-10, user-directed — **not implemented, kept as a real backlog
  entry rather than silently dropped.**
- **Context:** user proposed replacing (or adding alongside) the Archive Browser's per-entry CRC32
  column with a SHA256/MD5 value, shown as a clickable link that opens
  `virustotal.com/gui/file/<hash>` in the user's default browser — hash-only lookup, no file
  upload, nothing sent from Pakko's own code (`Launcher.LaunchUriAsync`, the same mechanism
  already used for the About dialog's GitHub/Ko-fi/Privacy links). Later refined during scoping:
  keep CRC32 as-is (it's free — already read from the ZIP header, zero extra compute), and only
  compute SHA256/MD5 on an explicit double-click (opens the link), with a single click copying the
  hash to the clipboard instead — so the cost only exists at the moment a user asks for it, not on
  every browse.
- **Why deferred, not designed further this round:** the real blocker isn't the UI or the compute
  cost — both are solvable (the double-click/single-click split, and SHA256 needing a real
  recompute since it isn't already available like CRC32 is, especially costly for tar-family
  entries which would need the same quarantine/sandbox path a real Extract uses). The blocker is
  that Pakko's published Privacy Policy (`docs/privacy.html`), `SECURITY.md`, and `README.md` all
  currently make an unqualified claim: "Pakko does not make any network requests... does not
  integrate with any third-party services." A VirusTotal link is not a network call *from Pakko's
  own code* — it opens the user's own browser, the user's own click, the user's own hash — but
  Pakko's specific audience (Ukrainian government/defense, per `CLAUDE.md`'s stated positioning)
  chose this app partly *because of* that unqualified "zero network" claim, and a third-party-
  service link risks reading as a quiet walk-back of that promise even though it's technically
  accurate that the app itself stays offline. Asked the user whether to draft an explicit, honest
  carve-out for the Privacy Policy/SECURITY.md/README ("Pakko does not make network requests,
  except when you explicitly click a VirusTotal link, which opens your own browser") before
  writing any UI code — **user's answer: leave the policy text as-is; don't implement this
  feature.** Recorded here as the actual decision, not as an open question, so this isn't
  re-litigated from scratch in a future session without this context.
- **If ever revisited:** this is not a "come back once you've done more design work" deferral —
  it's a "the user weighed the tradeoff and chose the current unqualified policy text over this
  feature" deferral. Revisiting it means asking the user again whether that tradeoff has changed
  (e.g. if a herметичний, hash-only VirusTotal *file-scan* extension of the existing sandboxed
  AMSI scanning is what's wanted instead — see the CI-side "upload release artifacts to VirusTotal"
  idea floated in the same conversation, which is a different, also-deferred idea about signing
  releases, not this per-entry Archive Browser feature).
- **Reported by:** user, 2026-08-10 — floated the idea, then explicitly declined once the Privacy
  Policy conflict was surfaced: "ні нехай буде як є просто додай беклог як і рішення по цьому."
- **Depends on:** none

---

### Fix batch (next, after the current batch closes) — index

User decision 2026-09-24: the current batch is discovery (T-F202, T-F226);
**every fix below is a separate, next batch** (T-F197 was the one planned exception — folded in
here with T-F227/T-F228 by user decision 2026-09-25). This index is the batch's scope and order; the
task entries themselves hold the detail. Rules for every item: tests first (write the reproducing
test, confirm it fails, then fix — `CLAUDE.md`'s revert-and-confirm rule), the four test
categories, deploy and verify on device before marking done.

**0. Decisions — taken 2026-09-25** (the user delegated them: "your choice, based on our niche
and best practice for similar apps like NanaZip"). Criteria: the government/defense audience
(trust, auditability, no silent data loss), then 7-Zip/NanaZip parity where users carry habits.
- T-F233 — **P0.** Permanent, silent, affects other people's access on shared folders. No
  automatic remediation (rewriting users' ACLs is itself risky): a release note/security advisory
  with a read-only detection script (files carrying a Pakko sandbox SID ACE) and a manual fix that
  removes **only** those ACEs and re-propagates inheritance (not `icacls /reset`, which would also
  wipe explicit ACEs the user set on purpose), with `icacls /save` first. Validate both on the one
  real affected file in Downloads. The advisory touches `SECURITY.md` — separate explicit user
  permission. Fix: never touch the original's security descriptor (stage by copy).
- T-F234 — **P0.** Silent loss of files inside one archive. Fix direction: 7-Zip's actual rule
  (read in NanaZip's vendored `Archive/Zip/ZipItem.cpp:405-461` and `ZipItem.h:338-351`): bit 11
  -> UTF-8; else a valid Info-ZIP Unicode Path extra field (0x7075); else by the central header's
  host OS — Unix -> UTF-8, FAT/NTFS -> OEM code page, other -> ANSI code page. Plus a collision
  check so two entries never collapse into one without an error. One decoder shared by extract,
  list/browse, test and scan. Tests use explicit code pages (866/437/1252), never the machine's;
  `CodePagesEncodingProvider` is registered once at a documented startup point, not in a
  service's static state.
- T-F201 — **keep T-F88's multi-instance design** (7-Zip File Manager and NanaZip open one window
  per archive too); fix only the stacking: offset the new window and bring it to the foreground.
- T-F205 — **keep the archive's root folder** in SingleFolder mode ("extract with full paths",
  7-Zip/NanaZip "Extract here" and `7z x`). Update `docs/DECISIONS.md` (T-F156's note) and the
  tests that encode the strip.
- T-F206 — **`pakko x` without `-o` extracts into the current directory**, like `7z x` (the CLI
  is 7z-familiar by design). User-visible change: CHANGELOG + `--help` + `docs/CLI.md`.
- T-F210 — **keep T-F107's "Up" navigation, add an explicit way back** to create mode (a "Close
  archive" command, also on Esc), as 7-Zip/NanaZip's file manager lets you leave an archive
  without closing the window.
- T-F241 — **add "Test archive" to the App** (7-Zip/NanaZip file managers have it on the
  toolbar); **no threat scan in the CLI for now** — `7z` has no such command. Stated limitation:
  `MpCmdRun` scans ordinary archives but cannot see inside password-protected ZIPs (T-F194's
  point), so a scripted scan of those needs the App or Explorer; record this in `docs/CLI.md`/
  `docs/DECISIONS.md`, revisit on request.
- T-F207 — deletion after an operation goes to the **Recycle Bin**, and only after the T-F260
  classifier says the source was fully processed. Where the OS cannot recycle (network or
  removable drive, item too large for the bin) an explicit "this deletes permanently" confirmation
  naming the items is shown first; declining keeps the source.
- T-F199 — direction: options before the action, primary action last, password inline; the
  mockup is still shown to the user before any XAML changes (visual design is not delegable blind).
- T-F260 — **reverse DECISIONS T-F68/T-F87**: `ArchiveResult` gets an explicit outcome and a
  per-source result. **Cancellation rule: both engines throw `OperationCanceledException`** after
  cleanup (ZIP already does; tar changes) — the .NET idiom, and every frontend already catches it;
  documented in `IArchiveService`/`ITarService` as the one exception to "never throws".
- T-F261 — **gate listing by Group Policy** (T-F250 as filed): an administrator's "never spawns
  tar.exe" must hold; correct `docs/ARCHITECTURE.md:1485`.
- T-F263 — **per-process preview/nested cache roots** plus a startup sweep of folders owned by
  dead processes (the App is multi-process by design, T-F88).

**Roots (architecture review 2026-09-25):** T-F260 (leaves T-F229, T-F245, T-F242, T-F207),
T-F261 (T-F250, T-F216, T-F241, T-F262), T-F263 (T-F227, T-F228, T-F197, T-F248, T-F252, T-F244;
related T-F233), T-F264 (T-F213, T-F159). Only the slice of a root its early leaves need goes
with them (T-F263: a unique staging name + the shared committer for the P0 trio; T-F260: the
outcome + the "may this source be deleted" classifier); the rest is placed by the phase plan. Grouping
roots with no number: Core message codes (T-F209, T-F208, T-F215, T-F221, T-F254), one directory
walker (T-F236, T-F237, T-F251), boundary encoding (T-F204, T-F234, T-F238).

**Phase 4b (added 2026-09-26, user; in progress, after the v1.5.0 release):** T-F268 (Explorer
windows through one `IOperationUi`, then a modern window — step 1 done, step 2 spike next),
T-F269 (Cancel stops the whole multi-archive selection — decide with T-F268 step 3), T-F216
(first half done via T-F268). Runs before phase 5.

**Phase 4c (added 2026-09-26, user):** T-F271 (many-small-files ZIP archiving ~50% slower on
.NET 10 — investigate, then fix or record), T-F272 (VeryLarge `ExtractAsync_OneLargeFile` fails in
its own suite run, pre-existing on .NET 8 too). After T-F270 lands; runs before phase 5.

**Plan update (2026-09-28, user requests; grouped by shared code and shared check, one commit
and one test-first cycle per task):** G0 CI/Sonar for the last pushes. G1 App: T-F276 first
(wording in the operation window, Core texts and CLI stderr — CHANGELOG line), T-F278, T-F277,
T-F219, T-F198 item 5, new keys in 37 locales; one Deploy and one full App pass that also closes
wave 4 (T-F199), the stale statuses (T-F210, T-F212, T-F224, T-F267), T-F213's check and a re-check
of T-F268 (f) with the display confirmed on. G2 tar/sandbox: T-F273 (first, P1), T-F214, T-F171
(short design approved by the user first), T-F148 sandbox/tar only, then T-F287 (every other
P/Invoke, added 2026-09-29 by the user — `SHFileOperationW` guards the Recycle Bin); one
integration + Slow run and one packaged-identity device check. G3 only if T-F268 (f) reproduces. G4 on main between groups:
T-F240, T-F259, T-F203 (after G2). T-F289 (ZIP extraction keeps entry dates; now T-F298) goes after v1.6.0 (user, 2026-09-29). G5 docs: T-F257 (SECURITY.md needs permission), T-F258, T-F223,
T-F165. G6 one device campaign on CI artifacts: T-F202 (coverage table rebuilt from current
source), every `[~]` check (delegated), T-F221 terminal, T-F260 mapping, light theme, keyboard,
narrow window; Windows display language last (sign-out ends the agent session; restore after).
G7 release v1.6.0 (watch the tag run's `release` job: its `download-artifact@v8` `pattern`/`merge-multiple` step runs only on a tag, since Dependabot #4-#8, 2026-09-29) — **user decision 2026-09-28: T-F202's P0/P1 findings are fixed before the
release, P2/P3 go to the next batch.** User: real drag done (T-F242 closed, T-F278 filed),
permission to switch theme and language given. After the release, separate batches: T-F275,
T-F132, T-F121, T-F111, T-F112, remaining P/Invoke conversion, distribution tasks (T-F119, T-F10,
T-F127, T-F144, T-F152).

**1. P0 — data loss or a broken core flow:** T-F227, T-F228, T-F229, T-F204, T-F233,
T-F234 (both P0, decision 2026-09-25), T-F245 (with T-F229), T-F246 — both P0 by user decision 2026-09-25. Suggested order: T-F227 + T-F228 + T-F197 together (same staging/commit code), then
T-F229 with T-F207, then T-F233, T-F234, T-F204.

**2. P1 — broken or misleading feature:** T-F230, T-F231, T-F232, T-F235, T-F236, T-F237,
T-F200, T-F205, T-F206, T-F207, T-F208, T-F210, T-F211, T-F212, T-F213, T-F224, T-F225, T-F247,
T-F248 (with T-F233), T-F250 (P1 kept, user decision 2026-09-25), T-F251 (with T-F236), T-F260,
T-F261, T-F263 (roots — with their leaves).

**3. P2 — polish, consistency, hardening, debt:** T-F198, T-F199, T-F201, T-F203 (SonarCloud),
T-F209, T-F214, T-F215, T-F216, T-F217, T-F218, T-F219, T-F220, T-F221, T-F222, T-F223 (+ T-F165,
diagrams — redo the per-arrow ground-truth ritual T-F226 deferred, after the P0 fixes land),
T-F238, T-F239, T-F240, T-F241, T-F242, T-F243, T-F244, T-F249, T-F252, T-F253, T-F254, T-F255,
T-F256, T-F257, T-F258 (with T-F223/T-F165), T-F259, T-F262, T-F264.

**Not in this batch:** T-F202's user-only checks (light theme, en-US, keyboard-only, tray, CLI in a
real console) and T-F226's deferred per-arrow diagram ritual — carried as open items on those tasks.

### T-F202 — Full UI smoke test: every feature, every menu and submenu (batch gate)

- [~] **Progress 2026-09-24 (discovery pass run against CI build 1.4.12.9):** covered — Explorer
  menu in all 9 selection contexts plus the classic menu, every Explorer command via
  `Archiver.Shell.exe` (incl. conflict, password, bomb dialogs), both App modes control by control,
  `.7z` cold/warm activation, every CLI command and most switches, and heavy scenarios on
  multi-GB real data (3.4 GB ZIP test/extract, 5.1 GB ZIP and TAR create + round trip, all
  byte-identical to the source). Findings filed as T-F204-T-F225 (plus additions to T-F197,
  T-F198, T-F199, T-F201). **Still open:** light theme and en-US (need a system setting change),
  keyboard-only pass (focus not reliably visible to automation), tray icon, the CLI in a real
  console (masked `-p`, Y/N/A/S/U/Q, Ctrl+C, PowerShell 5.1). Caveat: the test machine's ANSI code
  page is 65001, so code-page bugs 1251/1252 users would hit are invisible here. (Correction
  2026-09-25: `GetACP()`/registry now report ACP 1251, OEMCP 866 — see T-F204.) Coverage table,
  results and the N-number-to-task mapping: batch plan section 5.
- [ ] **Status:** open — a required gate for closing this batch (user instruction 2026-09-24: the
  app is live on the Microsoft Store and earlier self-testing missed real defects). Agent-driven via
  `windows` MCP against the freshly deployed MSIX, with a written checklist and a pass/fail per item:
  every button and option of the main window in both modes; every Explorer context-menu command
  and submenu (Pakko root, Extract Here/to folder/Open, Add to X.zip/.tar, Test, Scan for threats,
  Hash) on files, folders, multi-selection and a drive root; every dialog (conflict,
  password decrypt/encrypt, summary, bomb warning, About); the CLI commands including the real-
  console prompts; ZIP and tar-family formats; Ukrainian and English UI. **Discovery only (user
  decision 2026-09-24):** every defect found, however small, becomes its own task with repro steps
  and priority; fixes go to a separate follow-up batch (with T-F198-T-F201), tests first. **Method (user instruction 2026-09-24):** before the first click,
  build a coverage table — one row per action or menu item, including every Explorer context-menu
  dropdown submenu and the classic "Show more options" menu: surface, menu path, selection context,
  expected behavior, visual check (icon, text, locale, order, separators, enabled state,
  light/dark theme), result, UX notes. The tester acts as QA + UI/UX reviewer: judge the app's
  overall behavior while testing (feedback, consistency, dangerous actions, stray windows, focus,
  accessibility), not just pass/fail. **CLI:** a smoke test of every command/switch plus a deep
  usability review of `pakko.exe` against real `7z` and CLI best practice (ripgrep/fd/bat/gh/tar/
  curl: help and error text, switch consistency, TTY/color/`NO_COLOR`, progress, quiet/verbose,
  scripting stability, exit codes, Unicode/long paths); every divergence from 7z is either
  documented as deliberate in `docs/CLI.md` or filed as a defect. Also carries the diagram gap from DECISIONS' T-F193 entry (no
  diagram models `ArchiveAsync` routing; diagram 3 has no encrypted-entry branch).
- **Reported by:** user instruction, 2026-09-24.

### T-F202 findings (T-F204 onward) — discovery pass 2026-09-24

All found against the CI build of commit 8952c12 (MSIX 1.4.12.9 x64, CI run 36020977383, and that
run's `pakko.exe` win-x64), agent-driven via the `windows` MCP plus direct `Archiver.Shell.exe`
launches with the exact arguments each `IExplorerCommand::Invoke` builds. Paths are shown relative
(`<scratch>\...`). Priority: **P0** = data loss/corruption or a broken core flow, **P1** = broken
or misleading feature, **P2** = polish/consistency. Fixes belong to the follow-up fix batch, tests
first (user decision 2026-09-24). Items marked **decision** reverse or touch an earlier documented
choice — ask the user before implementing, like T-F118/T-F156 were.

### T-F206 — `pakko x` without `-o` extracts next to the archive, not into the current directory (P1, decision)

- **Progress (2026-09-27, fix phase 8):** fixed in fabe976 — `x` (and `-si`) without `-o` extracts
  into the current directory; `--help` and `docs/CLI.md` updated. Subprocess test red before the
  fix. Stays `[~]` until a real-console check.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open. `Archiver.CLI/Program.cs` defaults the destination to
  `Path.GetDirectoryName(archive)`; 7-Zip extracts into the current directory. Repro: from an empty
  folder, `pakko x ..\out.zip` -> nothing in the current folder; files land beside `out.zip`
  (and, with T-F205, without their root folder). Undocumented in `docs/CLI.md`. Either adopt cwd
  (7z habit) or document the divergence prominently in `--help` and CLI.md.
- **Reported by:** T-F202, 2026-09-24.
- **Decision (2026-09-25):** extract into the current directory like `7z x`; CHANGELOG, `--help`, `docs/CLI.md`.

### T-F211 — A successful App operation shows no visible outcome (P1)

- **Progress (2026-09-28, T-F199 step 7):** fixed — the result line stays in the footer until the
  next action, with "Show in folder" and "Details..." (see T-F199's step 7 entry). Renamed files
  are still not listed (Core does not report renames; mockup board 7 deferred), so this task's own
  repro (a second Extract all with Rename) still reads as a clean "Видобуто" — that part stays open.
  Device 1.5.0.31.
- **Device (G6 pass 3, 1.5.0.13):** clean runs show "Стиснуто/Видобуто за N с — архівів: 1 · Показати в папці" (opens the folder); a run with a problem shows "Завершено з проблемами: N · Деталі...", which reopens the summary. The rename case still reads clean (Rename + apply to all on a second Extract gave three `(1)` copies and "Видобуто за 13 с — архівів: 1"), so this stays `[~]`.
- [~] **Status:** fixed, see progress. Original: On success with no errors/skips, `MainViewModel` sets "Розпаковано за N с
  — файлів: M" and then, a few lines later, unconditionally resets `StatusMessage` to
  "Готово" (`MainViewModel.cs` ~line 702, and the matching reset in `ArchiveAsync` ~line 603);
  `ShowOperationSummaryAsync` returns early when there is nothing to report. Net effect: the user
  never sees what happened. Repro: browse `enc.zip`, Extract all, password -> status "Готово";
  files actually landed as `a (1).txt`/`b (1).txt` (Rename into an existing folder) with no
  mention anywhere. T-F70 made the reset deliberate for busy-state reasons; keep the outcome text
  visible (or use the Row 4 outcome subtitle) without breaking T-F70.
- **Reported by:** T-F202, 2026-09-24.

### T-F220 — UI wording and small UX inconsistencies (P2)

- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4). Earlier status: Each sub-item is small; split when fixing if preferred.
  1. Terminology: Explorer says "Видобути…"/"Стиснути…"/"Тестувати архів"; the App says
     "Розпакувати"/"Архів". Pick one vocabulary.
  2. Conflict dialogs (Shell and App): "Застосувати до всіх решти конфліктів" is ungrammatical
     ("до решти конфліктів"/"до всіх інших конфліктів"); neither shows sizes/dates of the two
     files; the App dialog has no "cancel the whole operation" button.
  3. The row context menu's "Видалити" only removes the row from the list, while "Видалити після
     операції" deletes files — use "Прибрати зі списку".
  4. Column sorting works but shows no direction indicator.
  5. The status line for a nested-archive scan shows the internal temp path
     (`%TEMP%\PakkoNestedArchive\<guid>\l4.zip`) instead of `outer.zip > ... > l4.zip`.
- [~] **Progress (2026-10-03, v1.7.0 wave 4):** items 1 and 3 and the grammar of item 2 were done
  by T-F199 (App says "Видобути"/"Стиснути в {0}"/"Тестувати архів" like Explorer; the row menu
  says "Прибрати зі списку"; "Застосувати до всіх наступних конфліктів"). Now: item 2 — the App's
  conflict dialog shows both files' size and date (App.Core `ConflictText`, words copied from the
  Explorer window's 37 locales) and a "Скасувати все" link that cancels the operation; item 4 — the
  sorted header shows an arrow (`SortIndicator`; the list keeps its order until the first click);
  item 5 — the scan status line names the chain (`NestedDisplayPath`); the dialogs already showed
  only the file name. Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03): App extract of `conf.zip` with "Запитувати": the dialog shows "Наявний файл: 10 Б · змінено 03.10.2026 6:34" and "З архіву: 600 Б · змінено 03.10.2026 6:31"; "Скасувати все" closes it and the footer reads "Скасовано" (the existing file untouched, no staging left; a Core test pins a cancelled prompt on the last entry); the size header shows a down arrow after the second click and the list is in descending size order; scanning `outer2.zip > l3.zip` reads "Перевірити на загрози — outer2.zip > l3.zip". Light theme not exercised (a registry switch did not reach the running App).
- **Reported by:** T-F202, 2026-09-24.

### T-F221 — CLI error and help messages (P2)

- [~] **Status:** partial — every item fixed in code (see Progress). Left: the console-only branches (progress on stderr, the `-so` refusal, `l` on an encrypted ZIP) in a real terminal — v1.7.0 wave 3 (CLI).
- [~] **Progress (2026-09-28, fix phase 7):** fixed in d7bf2e8 — items 1-6, 9, 10 (not found, -p hint and one wrong-password line, -aoa/-aou hint, empty stdin and "(stdin)", `a -so` to a terminal refused, explicit name written as typed — 7-Zip's rule, user decision; progress percentage only on a console stderr; `h <folder>` relative names). Item 8 was already fixed by T-F261. Item 7 moves to wave 4 with T-F199's encryption flag. Console-only branches (progress, -so refusal) need a real terminal check.
- [~] **Progress (2026-09-28, wave 4, T-F199 step 1):** item 7 fixed in aa9cf8f/13bd543 — `l` has a new `Encrypted` column (`-`, `ZipCrypto`, `AES-128/192/256`, `+` unnamed method, `?` format cannot say) inserted before `Path`. **Output contract change:** `Path` moves from column 6 to column 7 (still last) — for CHANGELOG v1.6.0 and DECISIONS. Include `l` on an encrypted ZIP in the carried real-terminal check.

- **Earlier status:** open.
  1. Missing input: `t missing.tar.gz`/`x nosuch.zip` -> "File is not a recognized archive
     format"; `l`/`h` -> raw .NET "Could not find file '<full path>'". Say "not found".
  2. `t enc.zip` (piped, no `-p`) -> "password-protected and cannot be tested" with no hint to
     use `-p`; with a wrong `-p`, two lines, the second contradicting the first.
  3. `x` onto existing files when piped -> "every entry was skipped", exit 1, with no reason and
     no hint (`-aoa`/`-y`).
  4. Empty stdin: `l -si` -> "Central Directory corrupt" plus the internal staging path
     `%TEMP%\Archiver.CLI.Stdin\<guid>\stdin.bin`; `t/x -si` -> "stdin.bin: not a recognized
     archive". Say "stdin was empty / not an archive".
  5. `a -so` with stdout on a console writes binary into the terminal; 7-Zip/gzip/zstd refuse.
  6. `a -t<type> name.out` silently changes the name (`name.tar`, `x.gz` -> `x.gz.tar.gz`);
     7-Zip writes exactly the given name. Fix or document.
  7. `l` has no encrypted marker (7-Zip shows `+`/an Encrypted column).
  8. `pakko i` columns are misaligned ("(always)" vs "(supported)").
  9. No progress output at all on long operations (`a`/`x` on multi-GB input stay silent for
     tens of seconds); 7-Zip prints a percentage, curl/gh a TTY progress bar. Show progress on
     stderr only when it is a console.
  10. `h <folder>` prints absolute paths; 7-Zip prints paths relative to the given folder.
- **Reported by:** T-F202, 2026-09-24.
- **Root (grouping, architecture review 2026-09-25):** Core reports errors and skips as English text with no code (`ArchiveError`/`SkippedFile` hold only strings), and the frontends localize through four separate mechanisms (App `.resw`, Shell `.resx`, `Localization.cpp`, none in the CLI). Fix T-F209/T-F208/T-F215/T-F221/T-F254 together: a code in the Core model, rendered per frontend.

### T-F225 — Folder hash "data and names" never matches 7-Zip/NanaZip (P1)

- **Progress (2026-09-27, fix phase 6, wave 1 track B):** fixed in b8c1557 — 7-Zip resets the
  item digest to zero before every item, so a directory item is deterministic (T-F128 misread
  this); NamesSum now has one item per directory including the selected folder, paths use the
  on-disk folder name as prefix, and `.`/`..`/drive roots hash contents only. `FolderHashParityTests`
  compares live against the vendored 7za (12 cases, CRC32 and SHA256); `docs/CLI.md`'s
  "verified against 7za" claim is now true. Remaining differences: links/junctions skipped
  (7-Zip follows them), unreadable subfolders not counted as items, 7-Zip hides the names line for
  a contents-only single file. Stays `[~]` until the device check.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open. DataSum matches the vendored `7za.exe h` exactly, but NamesSum differs for
  every folder tried — including a folder holding a single ASCII file with no subfolders:
  `one\a.txt` -> Pakko `CRC32 for data and names: 680B36C2`, 7za `FBAAC368-00000000`; two files
  -> `A2D28CEB-00000000` vs `44372226-00000002`; a real 27-file CD folder (SHA-256) ->
  `6cfbbed9...-0000000E` vs `88737e7e...-0000000C`. `docs/DECISIONS.md`'s T-F128 entry documents a
  divergence only for *subfolder* objects, but 7-Zip also counts the root folder itself as an item,
  so no folder ever matches; the item-count suffix differs too, and a one-item result drops the
  `-00000000` suffix 7-Zip always prints. `docs/CLI.md` still claims "NanaZip-compatible, verified
  against the vendored 7za.exe". Either reproduce 7-Zip exactly (including directory items) or
  correct the docs and label the value as Pakko-specific. Affects `pakko h`, Explorer Hash, and
  the App.
- **Tests first:** a parity test against the vendored `7za.exe h` for a one-file folder, a flat
  folder, and a nested folder.
- **Reported by:** T-F202 heavy scenario S5, 2026-09-24.

### T-F226 — Architecture review of the whole implementation against our rules (next research step)

- [~] **Status:** 2026-09-24 — findings filed as T-F227..T-F244 (plus additions to T-F201 and
  T-F232); batch 2 (2026-09-25, the sandbox files, AMSI and format detection the first pass did
  not reach) filed T-F245..T-F249 plus additions to T-F233 and T-F244. Batch 3 (2026-09-25:
  `GroupPolicyService`, preview/nested caches, `FileHashService`, Shell password and conflict
  dialogs, `Localization.cpp`, `SECURITY.md` against code, check K, check L spot-check) filed
  T-F250..T-F259; `scripts/*.ps1` were pattern-searched only (non-ASCII literals, BOMs, recursive
  deletes). Check K ran: 7 mutants over the security gates, 6 killed by the right tests, 1
  survivor (T-F256). **Not closed:** check L was a spot-check of diagrams 1, 2, 4, 7 (T-F258);
  diagram 6 is unchecked and the per-arrow ground-truth ritual over `docs/DIAGRAMS.md` is still
  deferred until the fixes for T-F227/T-F228/T-F233/T-F236 rewrite those diagrams (with T-F165/
  T-F223) — the review's exit criterion is not met for that cell. **2026-09-29 (G5):** the ritual
  ran for diagrams 1, 2, 3 (password gate), 4 and 7 (T-F258) and the new diagram 9 (T-F223);
  T-F165 was already done 2026-08-12. Diagram 6 is still unchecked — now T-F292.
  Checked with no finding: the fire-and-forget calls and `async void` (event handlers only),
  `static` mutable state (`FileHashService._threadPoolWarmed` is a process-wide one-shot latch around
  a process-wide setting), `ArchiveTreeIndex` recursion (iterative; memory issue is T-F237),
  `QuotePath` quoting, Authenticode verification, Job Object/attribute-list lifetimes.
  Performance (one run of `Category=Slow`, all 10 pass within the 3x tolerance): Archive/
  ManySmallFiles 1.61, Archive/Hybrid 2.47, Extract/ManySmallFiles 2.08, Extract/Hybrid 2.33,
  Hash 0.98 (Pakko/7za). The archive ratios are well above T-F35's recorded ~1.0/~1.3 — rerun
  on an idle machine before calling it a regression.
  Original plan text follows. Discovery only, like T-F202. A senior-architect
  review of all projects against the written rules (global `CLAUDE.md` Code Behavior,
  `~/.claude/dev-practices.md` sections 2-5, 7, 8, `cross-language-style.md`, this repo's Hard
  Constraints/Do Not, `docs/CONVENTIONS.md`, `SECURITY.md`, `docs/ARCHITECTURE.md`,
  `docs/DIAGRAMS.md`). Checks: resources, bounds provable from the line, immutability/static state,
  recursion over untrusted input, the "never throw" error contract, concurrency and UI marshaling,
  the three safety legs, architecture boundaries and duplicated decision logic (a four-frontend
  parity matrix), test categories and isolation level, docs/diagrams ground truth, CI tier (fuzzing,
  sanitizers, dependency audit), the `pakko://`/file-association activation surface as untrusted
  input, and code-page handling at every process/console boundary (the test machine runs ANSI
  65001, which hides code-page bugs). First pass: sweep for siblings of the bug classes T-F202 found
  (T-F204, T-F205, T-F207, T-F211). Done = every component x check cell is checked with
  `file:line`, filed as a finding (T-F227 onward), or N/A with a reason. Full method: the batch plan's
  section 6.
- **Reported by:** user request, 2026-09-24.

### T-F231 — Decrypted ZIP entries are not capped at their declared size (P1)

- **Progress (2026-09-25, fix phase 2):** `LocatedZipEntry.UncompressedSize` (central directory,
  Zip64-resolved); `EncryptedZipEntryReader` wraps every decrypted entry in `VerifyingReadStream`
  (replacing `TrailerCrcCheckStream`), so AE-2 is capped and ZipCrypto/AE-1 get cap + CRC — in
  extract, Test and Scan alike. Confirmed .NET does not cap the unencrypted stored path either (it
  returns all stored bytes); T-F246's wrapper covers that. Agent-verified on device 2026-09-25 (Deploy 1.4.12.11): covered by unit/service tests (AE-2 larger than declared); no separate device scenario.

- [~] **Status:** fixed in fix phase 2, stays `[~]` until the user's own check. Original: open — code-confirmed by the T-F226 reviewer agent, exploit not yet reproduced.
  The compression-bomb and free-space gate uses declared sizes; the decrypting path wraps the
  plaintext in an unbounded `DeflateStream` (`EncryptedZipEntryReader.cs:~229`), so a
  password-protected archive can declare tiny sizes and expand far beyond them (a password shared
  out of band is the normal case). Cap the output at the entry's declared length and fail beyond
  it; confirm whether .NET caps the unencrypted path as assumed.
- **Tests first:** an AE-2 entry whose inflated output exceeds its declared length is rejected and
  the staging folder is cleaned.
- **Reported by:** T-F226 review, 2026-09-24.

### T-F235 — A large Explorer selection makes every Pakko command silently do nothing (P1)

- [~] **Progress (2026-09-28, fix phase 5, wave 2 track B):** fixed (fc1229d, fd08232). The DLL
  always runs `Archiver.Shell.exe <command> --paths-stdin` and writes the paths to the child's stdin
  (anonymous pipe, only the read end inherited via `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, UTF-16LE
  NUL-separated list with an end marker); nothing on disk, no size-dependent path. A launch/write
  failure and a selection with a non-filesystem item show a message. **Device-checked 2026-09-28
  (Deploy 1.5.0.15, real Explorer menu):** 300 files with ~95-char names (73,199-char quoted list)
  -> "Add to many.zip" creates it (7za: 300 files, OK), Hash CRC-32 lists 300, "Compress..." shows
  the too-many-files message. "Add to many.tar" now fails visibly on tar.exe's own command line
  (T-F273). Still open: Extract.../Compress.../Open hand the list to the App through
  `LaunchArguments` (32,000-char cap, too-many message shown); the non-filesystem refusal not yet
  device-checked.
- **Non-filesystem refusal, device note (2026-09-28):** not reachable from Explorer's built-in ZIP
  view — an item inside a compressed folder gets only Explorer's own classic menu
  (Open/Cut/Copy/Delete/Properties), no third-party verbs, so Pakko is never offered there.
  Remaining real triggers (MTP phone, other shell namespaces) need that hardware/namespace; covered
  by the C++ `GetSelectionPaths` test with a Control Panel item.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — symptom confirmed on device 2026-09-24, cause likely (not isolated). The
  extension passes the whole selection as one `CreateProcessW` command line
  (`ShellExtUtils.cpp:228-253`, `Build*Args`), whose documented limit is 32,767 characters. When it
  fails, `Invoke` returns the HRESULT (`ExplorerCommands.cpp:125` and siblings), which Explorer
  ignores. Repro: 300 files with ~95-character names in `<scratch>\many\` (~71,400 characters
  quoted), Ctrl+A, context menu, Pakko -> "Додати до "many.zip"" -> no process, no archive, no
  message. Control: the same command on one file works. Not separated from a pure count limit
  (a 300-file short-name run would confirm the cause).
  Related: `GetPathsFromShellItemArray` silently skips items without `SIGDN_FILESYSPATH`
  (library/virtual items), so part of a selection can vanish without notice.
  Fix direction: hand the list to `Archiver.Shell` another way (a temp response file, or the
  existing `pakko://`-style JSON), and never fail silently.
- **Tests first:** a `Build*Args` over the limit is detected; the chosen transport carries 10,000
  paths.
- **Reported by:** T-F226 review, 2026-09-24.

### T-F236 — One unreadable subfolder aborts creating the whole archive (P1)

- [~] **Status:** partial — Core part fixed (see Progress). Left: the App's own folder walks (`FileItem.LoadFolderSizeAsync`, the size pre-count) — v1.7.0 wave 6.
- **Progress (2026-09-27, fix phase 6, wave 1 track B):** Core part fixed in 6fdac82 — new
  iterative `DirectoryWalker` (`Archiver.Core/IO`) used by both ZIP creation walks; an unreadable
  subfolder is one `ArchiveError`, every readable file is archived, the source is not `Completed`
  (Delete after operation keeps it). **Remaining (phase 9, App):** `FileItem.LoadFolderSizeAsync`
  and `MainViewModel`'s size pre-count still walk on their own (no cancel on remove/clear).

- **Added 2026-09-26 (from T-F232):** `FileItem.LoadFolderSizeAsync` walks a pending-list folder
  recursively with no cancellation — adding `C:\` walks the whole drive, and removing the row does
  not stop it. Before T-F232 a `pakko://` link could trigger this; now only the user's own
  selection can. Fold it into the shared walker (cancel on remove/clear, bounded depth).

- **Earlier status:** open — confirmed on device 2026-09-24. Parallel path (above 64 files): the
  enumeration in `Zip/WorkItemEnumerator.cs:73,83,100` throws inside the producer and fails the
  whole operation — 71 files, one subfolder with `deny RD` -> `pakko a` "Access denied creating
  archive: Access to the path '...\src\locked' is denied.", exit 2, no archive. Sequential path:
  the whole top-level source is dropped even though its other files are readable. 7za on the same
  tree warns ("Access is denied", "Scan WARNINGS: 1"), exits 1 and still archives the readable
  files. Violates the per-item error rule.
- **Tests first (Error path):** an unreadable subfolder becomes one `SkippedFile`/`ArchiveError`,
  every readable file is archived, on both the sequential and parallel paths.
- **Reported by:** T-F226 review, 2026-09-24.
- **Root (grouping, architecture review 2026-09-25):** one of seven separate walks over user-supplied folder trees (`WorkItemEnumerator`, `ZipArchiveService` `AddDirectoryToArchiveAsync`/`ComputeDirectoryTotals`, `TarSandboxedService.CountRecursiveEntriesAndBytes`, `FileHashService`, `FileItem`, `MainViewModel`'s size pre-count), each with its own access-denied, junction and depth behavior. A reparse-safe iterative walker already exists (`TarSandboxedService.EnumerateFilesGuarded`) but is used only for quarantine — fix T-F236/T-F237/T-F251 through one shared walker.

### T-F241 — Frontend feature gaps with no recorded decision (P2, decision)

- **Progress (2026-09-27, fix phase 8):** CLI half recorded — no `pakko` scan, reasons in
  `docs/CLI.md`'s command table and DECISIONS "Fix phase 8". App "Test archive" stays for phase 9.

- [ ] **Status:** open. `pakko` has no threat scan (T-F146 added it to the App, Shell and
  Explorer only); the App has no "Test archive" (Explorer, Shell and CLI have it). Neither is
  recorded in `docs/DECISIONS.md` or `docs/CLI.md`. Decide: add, or document why not.
- **Reported by:** T-F226 review, 2026-09-24.
- **Root:** T-F261 (single routing and Group Policy owner) — the part this leaf needs goes with it.
- **Progress (2026-09-28, T-F199 step 6):** App half done — "Test archive" in the browser toolbar,
  ZIP only (same as Explorer's menu), through `IExtractionRouter.TestAsync`; "No errors found" only
  for `Outcome == Completed` (T-F274), otherwise the summary. Device-checked on 1.5.0.28.
- **Decision (2026-09-25):** add Test to the App; no CLI scan for now (no `7z` equivalent) — record why, including that `MpCmdRun` cannot scan inside password-protected ZIPs (T-F194), in `docs/CLI.md`/`docs/DECISIONS.md`.

### T-F243 — ZIP reader hardening (P2)

- **Progress (2026-09-25, fix phase 3):** item 1 fixed with T-F234 (d78c816, `\` normalized
  on decoded names); item 2 fixed (f317397 — check byte from the file time with a data descriptor,
  as 7-Zip does); item 3 fixed (0cb5c0e — a small ZipCrypto probe entry is fully verified at
  prompt time, so a colliding wrong password is asked again); item 4 fixed (9ae0a24 — every segment,
  part before the first dot, full Microsoft list); item 5 **not reproduced** (1110af2 —
  characterization test: Test and Extract share names and readers); item 6 fixed (1110af2 —
  over-long names are a per-entry error on both write paths). Covered by T-F234's device check
  (same read path); items 2/3/6 are test-only (no realistic on-device fixture needed beyond it).
- [~] **Status:** fixed except item 5 (not reproduced), awaiting user check. Original report: open, from the T-F226 review (reviewer agent + own reading); items marked
  hypothesis need a repro first.
  1. `ZipArchiveService.cs:1229-1236`: `\` in an entry name is not normalized when classifying
     the root shape (hypothesis).
  2. `EncryptedZipEntryReader.cs:119,198`: the ZipCrypto check byte with data-descriptor bit 3
     (hypothesis — compatibility).
  3. `ZipArchiveService.cs:799,858-865`: a wrong password that passes the 1-in-256 ZipCrypto check
     reports "corrupted" instead of asking again.
  4. `ArchiveEntrySecurity.cs:29-37`: reserved names `CON.a.b`, `NUL/x`, `CONIN$`, superscript
     `COM¹` (hypothesis).
  5. `RawZipEntryLocator` pairs local and central records by position only; Test and Extract can
     disagree on a mismatch (`ZipArchiveService.cs:1037-1062`).
  6. `Zip/ZipEntryWriter.cs:201,267`: `(ushort)nameBytes.Length` is unchecked — a name over 65,535
     UTF-8 bytes writes a truncated length field and a corrupt archive (hypothesis).
- **Reported by:** T-F226 review, 2026-09-24.

### T-F244 — Sandbox launcher, crypto and CLI staging hygiene (P2)

- [~] **Status:** partial — items 1, 2, 3, 5 fixed; item 4 fixed in code. Left: a real-console Ctrl+C and kill check of the `-si` staging — v1.7.0 wave 3 (CLI).
- **Progress (2026-09-27, fix phase 8):** item 4 fixed in 7a6f236 + cd593d5 — `CliStagingFolder`
  (`<pid>-<guid>`, owned from creation), Ctrl+C for `x`/`t`/`l`/`a` (exit 255, second press ends
  the process), startup sweep of dead runs' folders with PID-reuse detection. Real-console Ctrl+C
  and kill checks pending.

- **Progress (2026-09-25, fix phase 4):** item 1 fixed in a33da37 (pipe ends owned from creation; any failure/cancel terminates and waits for the child; the wait keeps its handle reference) and 8ac316a (NEW: concurrent `CreateAppContainerProfile` on the existing profile failed — the real cause of the CI flake; `EnsureExists` checks the Mappings key first). Item 5 fixed in a33da37 (`PROC_THREAD_ATTRIBUTE_HANDLE_LIST`; creation and the `--version` probe moved onto the same launcher). Item 2 fixed in acca0de. Item 4 stays for fix phase 8.

- **Progress (2026-09-25, fix phase 3):** item 3 fixed — bc29e20 (staging root itself checked
  for a reparse point) and b02f473 (password bytes ANSI/UTF-8 in NanaZip's order; key material
  zeroed). Item 1 evidence (for fix phase 4): Build 36133755270 failed once on
  `TarSandboxedServiceExtractTests.ExtractAsync_CancelMidExtraction_NoUnhandledExceptionNoOrphanedQuarantineDir`
  (a `%TEMP%\PakkoTarSandbox\<guid>` left behind), green on rerun and 10/10 locally; full local
  runs this phase also failed `CliSubprocessTests.Extract_TarGzHappyPath_ExtractsFilesAndExitsZero`,
  `Extract_SevenZipFixtureWithSo_StreamsSingleFileToStdout` and
  `NestedArchiveDrillDownSecurityTests.ExtractAsync_NestedBombArchive_RejectedIndependentlyAtSecondLevel`
  once each, all passing alone. Likely cause read in the code: on cancel `SandboxedProcessLauncher`
  calls `TerminateProcess` without waiting for exit and `TarSandboxScope.Dispose` deletes the
  quarantine best-effort at once; the other failures point at cross-project sandbox contention.
  Item 3's password part verified on device with T-F234 (cp1251 Cyrillic ZipCrypto password).
- **Earlier status:** open (items 1, 2, 4, 5 — fix phase 4/8), from the T-F226 review.
  1. `SandboxedProcessLauncher.cs:116-133`: pipe handles leak if setup fails midway;
     `:157-195`: `DangerousAddRef` does not span the wait.
  2. `SandboxedProcessLauncher.cs:132-133`: tar.exe stdout is read as UTF-8, so the pre-scan may
     see different names than tar writes (hypothesis; family of T-F204).
  3. WinZip AES/ZipCrypto key material is never zeroed; the ZipCrypto password is UTF-8 only;
     `PathContainsReparsePoint` does not check the staging root itself.
  4. `CliStreamStaging.cs:13-26` + `Program.cs:84,284,445`: the `-si` staging path is recorded
     only after the copy completes, so a failure mid-copy (disk full) leaks the folder; staging
     uses `CancellationToken.None`, so Ctrl+C does not interrupt it; `x -so` leaves decrypted
     plaintext in `%TEMP%` if the process dies.
  5. (T-F226 batch 2) `SandboxedProcessLauncher.cs:30-38`, `:81`: the stdout/stderr pipes are
     created inheritable and `CreateProcessW` runs with `bInheritHandles: true` but no
     `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` and without the lock .NET's own `Process.Start` takes
     around the same window — two concurrent launches (two scopes, as T-F175's test does, or an
     unsandboxed `Process.Start` of tar for creation) can each inherit the other's pipe write ends,
     including into the AppContainer child, and a reader then waits for EOF until the other child
     exits (hypothesis, not reproduced).
- **Reported by:** T-F226 review, 2026-09-24.
- **Root:** T-F263 (staging/commit owner) — the CLI staging item (A19) only; R14 belongs to the boundary-encoding grouping below.

### T-F245 — Cancelling a tar-family extraction reports success, so "Delete after operation" deletes the archive (P0)

- **Progress (2026-09-25, fix phase 1):** Core fixed — both engines throw
  `OperationCanceledException` on cancel, including between archives/sources (T-F260 entry in
  `docs/DECISIONS.md`); App consumer + device check still to come in the same phase.
- [~] **Status:** fixed in fix phase 1 (2026-09-25), agent-verified on device (App: cancelled 335 MB `.tar.gz` -> archive kept, no partial output, no tar.exe, status "Скасовано"; Shell `--extract-here` of a 200 MB `.tar.bz2` cancelled -> exits 0 in ~1 s, no output, no tar.exe); stays `[~]` until the user's own check. Details: `docs/DECISIONS.md` T-F260 entry.
- **Original report:** open — Core behavior confirmed 2026-09-25 with a scratch probe calling
  `TarSandboxedService.ExtractAsync` directly on a 600 MB `.tar.bz2` (bomb prompt answered yes):
  no cancel -> `Success=True created=1` after 10.7 s; cancel at 1.5 s / 4 s / 8 s -> returns
  promptly (tar.exe is killed, no process left) with **`Success=True errors=0 skipped=0
  created=0`**. The same probe on a 400 MB ZIP cancelled at 0.3 s throws
  `TaskCanceledException` instead. Cause: `TarSandboxedService.ExtractOneArchiveAsync` turns an
  `OperationCanceledException` into `return true` (`TarSandboxedService.cs:223-225`), the loop
  breaks (`:107-116`) and the result is built as `Success = errors.Count == 0` (`:118-120`).
  The same probe through `ExtractionRouter.ExtractAsync` (the path the App uses) returns the same
  `Success=True created=0` at 1.56 s — the router merges results without checking cancellation
  (`ExtractionRouter.cs:40-66`); in a mixed zip+tar selection the finished ZIP part is then
  deletable as well. Consequence in the App (only the last step, the `:653` condition, is not
  exercised on device — it deletes a file): `RunExtractAsync` only catches `OperationCanceledException` (`MainViewModel.cs:677`), so a
  cancelled tar extraction reaches `if (result.Success && DeleteAfterOperation)`
  (`MainViewModel.cs:653`) and `RunCleanupAsync` permanently deletes the archive although nothing
  was extracted; with several archives selected, the ones never started are deleted too
  (`GetDeletableSources`, `:1237-1241`, only excludes skipped paths — same gap as T-F229). The
  status line then says "done, 0 files" instead of "Cancelled". The CLI is not affected (it checks
  its own token, `Archiver.CLI/Program.cs:102`). Sibling: tar `CompressAsync` in
  `SeparateArchives` mode breaks out of its loop on a cancel observed between two sources
  (`TarSandboxedService.cs:1036-1037`) and also returns `Success=true`, so `MainViewModel.cs:550`
  would delete sources that were never archived (narrow window: the in-flight tar call rethrows,
  `:1133-1136`).
  Fix direction: one cancellation contract for both engines (throw, as ZIP does), and delete only
  sources that appear in `CreatedFiles`.
- **Tests first:** cancel an in-flight tar extraction and a multi-archive tar extraction -> the
  call throws (or reports cancellation) and no source is deletable; the existing T-F169 tests only
  assert "no unhandled exception" and pass today.
- **Reported by:** T-F226 batch 2, 2026-09-25.
- **Root:** T-F260 (`ArchiveResult` outcome contract) — the part this leaf needs goes with it.

### T-F246 — ZIP extraction never checks CRC-32: a corrupted entry is written and reported as success (P0)

- **Progress (2026-09-25, fix phase 2):** every unencrypted entry is read through the new shared
  `IO/VerifyingReadStream` (CRC-32 at end of stream + declared-size cap); a mismatch is a per-entry
  `ArchiveError`, the file is deleted before commit, the archive is `Partial`. Stored entries with
  an understated size hit the cap; deflated ones hit the CRC (.NET truncates them). Agent-verified
  on device 2026-09-25 (Deploy 1.4.12.11): a stored entry with one flipped byte — Shell
  --extract-here, `pakko x`, the App's Extract All and the App's preview (double-click) each report
  the CRC-32 error for `doc.txt`; only `ok.txt` is written and no viewer opens. `TestAsync` now uses
  the same check (closing review).

- [~] **Status:** fixed in fix phase 2, stays `[~]` until the user's own check. Original: open — confirmed 2026-09-25. `pakko a c.zip doc.txt -mx=0`, flip one byte
  inside the stored data: `pakko t bad.zip` -> `Entry 'doc.txt' failed CRC-32 check (expected
  FFF2F885, got E6BC4A3F)`, exit 2; `pakko x bad.zip` -> exit 0, `doc.txt` written and differs from
  the original; `7za x` -> `ERROR: CRC Failed : doc.txt`. `TestAsync` computes CRC-32 itself
  (`ZipArchiveService.cs:1026-1075`), but extraction only copies the stream
  (`ZipArchiveService.cs:1610`, `:1623`) and .NET 8's `ZipArchiveEntry.Open()` does not validate
  CRC on read. Same root, second shape: an entry whose declared uncompressed size is smaller than
  its data is silently truncated to the declared size (`under10.zip`: 10 bytes written, exit 0).
  Every frontend is affected (App, Shell, CLI, preview, nested drill-in); with "Delete after
  operation" the archive is then deleted after a corrupt extraction. Silent data corruption, for
  an audience that relies on the archive's integrity.
  Fix direction: verify CRC-32 while copying (the pipeline already streams every byte through
  `ProgressStream`), record a mismatch as an `ArchiveError` for that entry and do not commit it —
  as `TestAsync` already does. AE-2 entries have no header CRC (HMAC is the authority) — keep that
  exception. ZipCrypto entries are already CRC-checked on extraction (`SECURITY.md:338-339`), so
  today only the unencrypted majority is unchecked.
  **Design premise that turned out false:** the SHA-256 integrity manifest was removed as
  "redundant with ZIP built-in CRC-32" (`docs/TASKS_DONE.md:204-205`, repeated in `CLAUDE.md`'s
  Current State) — that is only true if extraction checks the CRC, which it does not.
- **Tests first:** Error path — a one-byte-corrupted stored entry and a deflated entry with a
  wrong CRC both fail extraction with a clear per-entry error, and the file is not left in the
  destination; Security & Boundary — understated uncompressed size.
- **Reported by:** T-F226 batch 2, 2026-09-25.

### T-F247 — "Scan for threats" crashes on any archive that contains an empty file (P1)

- **Progress (2026-09-27, fix phase 6, wave 1 track B):** fixed in 1b0f824/c4fe0d4 — an entry
  with declared length 0 is Clean without an AMSI call; a failing AMSI call is `Inconclusive` for
  that entry and the rest are still scanned. Tests: zero-byte ZIP entry (fake and real AMSI),
  zero-byte tar entry, one failing entry. Known gap (older): a short read is still scanned. Stays
  `[~]` until the device check.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — confirmed 2026-09-25. A plain ZIP made by `pakko a` from `a.txt` +
  a zero-byte `empty.txt`, and a plain `tar -cf` of the same two files: `AntivirusScanService.
  ScanAsync` throws `System.InvalidOperationException: AmsiScanBuffer failed (HRESULT 0x80070057)`
  for both. `AmsiScanBuffer` rejects a zero-length buffer with `E_INVALIDARG`; `AmsiScanner`
  turns every failing HRESULT into an exception (`Antivirus/AmsiScanner.cs:63-64`), and neither
  per-entry catch filter includes `InvalidOperationException` (`AntivirusScanService.cs:291`,
  `:570`). **On device** (installed CI MSIX 1.4.12.9): `Archiver.Shell.exe --scan withempty.zip`
  (the Explorer "Scan for threats" command) exits `0xE0434352` after the progress dialog, with no
  message to the user — only an `Application Error` / `.NET Runtime` event naming
  `AmsiScanBuffer failed`. The App path lands in the generic `catch (Exception)` and shows the raw
  message in an "Error" dialog (`MainViewModel.cs:1146-1149`). Empty files are common in real
  archives, so the feature fails on ordinary input. Violates the "methods never throw" contract;
  T-F177/T-F186 tested the size cap and a real EICAR but not the zero-length boundary.
  Fix direction: a zero-length entry is trivially clean (nothing to scan) or skipped before the
  AMSI call; a per-entry AMSI failure becomes an `Inconclusive` finding, never an exception.
- **Tests first:** Boundary — a zero-byte entry in ZIP and tar yields a normal result; Error path
  — a fake scanner that fails one entry yields `Inconclusive` for that entry and the rest scanned.
- **Reported by:** T-F226 batch 2, 2026-09-25.

### T-F251 — Hashing a folder crashes on an unreadable subfolder or a junction loop (P1)

- **Progress (2026-09-27, fix phase 6, wave 1 track B):** fixed in f95c566 — `ComputeFolderAsync`
  lists files through `DirectoryWalker`: an unreadable folder is one error entry, junctions/symlinks
  are skipped with their own entry (no loops, no foreign files); the parallel CRC-32 path fails a
  file that shrank while hashed. Stays `[~]` until the device check.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — confirmed 2026-09-25. `FileHashService.ComputeFolderAsync` enumerates
  with `new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).ToList()`
  (`FileHashService.cs:128`) outside any try, with the default options: inaccessible folders
  throw and reparse points are followed.
  Repro 1: folder `h1\` with `sub\locked\` denied `RD` for the user -> `pakko h h1` dies with
  `Unhandled exception. System.UnauthorizedAccessException ... FileHashService.cs:line 128`, exit
  0xE0434352. The installed Store-shaped package (CI 1.4.12.9), `Archiver.Shell.exe --hash
  --algorithm sha256 h1` (Explorer "Хеш-суми" -> SHA-256): exit 0xE0434352, no message to the user,
  only `Application Error`/`.NET Runtime` events naming the same exception.
  Repro 2: folder `h2\` with a junction `h2\loop -> h2` -> `pakko h h2` walks `loop\loop\...` and
  dies after ~1.3 s with `System.IO.PathTooLongException`. A junction pointing elsewhere silently
  adds files outside the folder to DataSum/NamesSum (archive creation skips reparse points, T-F23;
  hashing does not).
  The App's Hash dialog picks files only, so it is not affected.
  Also code-confirmed: the parallel CRC-32 path (`:311-320`) stops on `read <= 0` for a file that
  shrinks while hashed, yet `Crc32.Combine` still uses the chunk's declared length (`:303`, `:331`)
  — a wrong CRC reported as a success, no error.
  Sibling of T-F236 (same "one unreadable subfolder aborts everything" class) and T-F237.
- **Tests first:** Error path — an unreadable subfolder becomes one per-item error and every
  readable file is hashed; Security & Boundary — a junction loop and a junction to an outside
  folder are skipped (NanaZip parity to be checked); a short read fails the file instead of
  returning a CRC.
- **Reported by:** T-F226 batch 3, 2026-09-25.
- **Root (grouping, architecture review 2026-09-25):** one of seven separate walks over user-supplied folder trees (`WorkItemEnumerator`, `ZipArchiveService` `AddDirectoryToArchiveAsync`/`ComputeDirectoryTotals`, `TarSandboxedService.CountRecursiveEntriesAndBytes`, `FileHashService`, `FileItem`, `MainViewModel`'s size pre-count), each with its own access-denied, junction and depth behavior. A reparse-safe iterative walker already exists (`TarSandboxedService.EnumerateFilesGuarded`) but is used only for quarantine — fix T-F236/T-F237/T-F251 through one shared walker.

### T-F254 — Explorer menu stays English for Chinese and regional-variant Windows languages (P2)

- [~] **Status:** partial — fixed in code (see Progress). Left: a device with a regional language (e.g. de-AT) — the E7 language check with the user.
- [~] **Progress (2026-09-28, fix phase 7):** fixed in 4795bfb (C++ menu) and eb5876e (Shell `.resx`): exact tag (case-insensitive), zh-CN/zh-SG/zh-Hans-* -> zh-Hans, Traditional stays English, else the same language's row. Same rule in `Archiver.Messages.UiCulture`; the App picks the first of `ApplicationLanguages` Pakko translates. Needs a device with a regional language (e.g. de-AT) to confirm.

- **Earlier status:** open — code-confirmed 2026-09-25. `Localization.cpp` looks the UI language up by
  exact tag (`GetLocalizedString`, `:112-121`) from `GetThreadPreferredUILanguages` (`:94-110`).
  Windows reports Simplified Chinese as `zh-CN` (Microsoft's language-pack table, fetched
  2026-09-25), but the table's key is `zh-Hans` — it can never match, so Chinese users always get
  the English menu. Regional variants with their own Windows language pack (`pt-BR`, `es-MX`,
  `fr-CA`) and any user whose tag differs from the table's one region (`de-AT`, `de-CH`, ...) also
  fall to English. The other frontends resolve differently: `Archiver.Shell`'s `.resx` use .NET's
  parent chain (`zh-CN` -> `zh-Hans` works, `de-AT` -> `de` -> invariant does not), the App uses
  MRT language matching (its `de-AT` behavior not verified) — so the menu, Shell dialogs and window
  can disagree, localized in one and English in another.
  `LocalizationTests.cpp:62` feeds only the table's own keys, so it cannot catch this.
  `docs/DECISIONS.md`'s T-F115 entry (`:4816`) already noted `zh-Hans` does not map to a
  classic tag, but not the lookup consequence.
- **Tests first:** `GetLocalizedString(id, L"zh-CN")` returns the Chinese row; `pt-BR`/`de-AT`
  fall back to their language, not en-US.
- **Reported by:** T-F226 batch 3, 2026-09-25.
- **Root (grouping, architecture review 2026-09-25):** Core reports errors and skips as English text with no code (`ArchiveError`/`SkippedFile` hold only strings), and the frontends localize through four separate mechanisms (App `.resw`, Shell `.resx`, `Localization.cpp`, none in the CLI). Fix T-F209/T-F208/T-F215/T-F221/T-F254 together: a code in the Core model, rendered per frontend.

### Architecture review findings (T-F260 onward) — 2026-09-25

One level above T-F226's rule-per-component pass: layer boundaries, duplicated decisions, data and
state flow, extensibility. Done by the main session plus one independent reviewer agent that was
not given the defect list (it reached 7 of the same 10 roots on its own). **Rule for these
entries:** a root gets its own task only when the root fix is work no leaf task covers (a public
Core contract change, or reversing a recorded decision). Each covered leaf carries a
`**Root:**` line; the slice of the root a leaf needs goes with that leaf, the rest is placed
by the phase plan. Roots that are only a grouping
(Core message codes, directory walking, text encoding across process boundaries) have no number
here — see the `**Root:**` notes on T-F209, T-F236/T-F237/T-F251 and T-F204/T-F234/T-F238.

### T-F260 — `ArchiveResult` has no defined outcome: every frontend decides success, partial and cancelled for itself (P1, root, decision)

- [~] **Progress (2026-09-28, fix phase 7):** remainder done in 6e0575d — `ArchiveResult.Outcome` (Completed / CompletedWithSkips / NothingDone / Failed) is the one classification; `Success` is derived (no errors) instead of set at ~15 sites; Shell, CLI exit codes and the App map `Outcome` only. CLI exit codes unchanged.

- **Progress (2026-09-25, fix phase 1):** slice done in Core — `ArchiveResult.Sources` /
  `FullyProcessedSources` (fail-closed) and the cancellation rule; the general outcome and the
  frontend mapping stay for phase 7 (`docs/DECISIONS.md`, T-F260 entry).
- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — code-confirmed 2026-09-25. `ArchiveResult` is `Success` plus three string
  lists (`ArchiveResult.cs`); it cannot say "cancelled" or "partly done", `SkippedFile.Path` does
  not say whether a source or an entry was skipped, and `CreatedFiles` holds output folders for
  extraction but archives for creation (the App status line still calls them "file(s)").
  `Success` is computed separately at ~12 sites (`ZipArchiveService.cs:92,714,950`;
  `TarSandboxedService.cs:120,1014`; `ExtractionRouter.cs:56` merges without checking for a
  cancel; Shell invents `Success=false` at `Archiver.Shell/Program.cs:374-376`). Cancellation
  differs per engine: ZIP extraction lets it propagate (`ZipArchiveService.cs:1328-1335` cleans
  up and rethrows; creation does the same at `:238-241`, recorded for creation in DECISIONS
  T-F12/T-F193), tar extraction turns it into a success (`TarSandboxedService.cs:223-225`, not
  recorded). Each
  frontend then classifies on its own: CLI `Program.cs:559-569`, `ShellResultPresenter.cs:26-34`,
  App `MainViewModel.cs:550,554,653,657` + `DialogService.cs:365`; "the whole source was
  skipped" is found by comparing path strings (`MainViewModel.cs:1237-1241`). Post-operation
  actions are split across layers: "Open destination folder" runs in Core at five sites
  (`ExtractionRouter.cs:62`, `ZipArchiveService.cs:98,720`, `TarSandboxedService.cs:126,1020`),
  "Delete after operation" only in the App VM, and Core's `DeleteSourceFiles`/
  `DeleteArchiveAfterExtraction` are never read.
  DECISIONS T-F68 and T-F87 chose "don't change `ArchiveResult`, patch the consumer"; T-F229 and
  T-F245 (data loss through "Delete after operation") show the contract cannot express the states
  that decision needs.
- **Fix seam:** `ArchiveResult` — an explicit outcome (completed / partial / cancelled / failed),
  a per-source outcome, and one Core classifier every frontend uses (including "may this source
  be deleted"). Cascade: `docs/ARCHITECTURE.md`, tests in every frontend project.
- **Decisions for the user (fix-batch index item 0):** reverse T-F68/T-F87; the cancellation rule
  for both engines (rethrow, or a `Cancelled` outcome) — written into `IArchiveService`/
  `ITarService` either way.
- **Leaves:** T-F229, T-F245, T-F242 (dead options, item 2), T-F207 (the delete decision).
  (T-F211 is not a leaf — its status line is reset in `MainViewModel`, not a result-contract gap.)
- **Tests first:** one test per outcome per engine through `ExtractionRouter`, including a mixed
  zip+tar selection cancelled midway; each frontend's mapping of each outcome (exit code, dialog,
  delete/no delete).
- **Reported by:** architecture review, 2026-09-25.
- **Decision (2026-09-25):** reverse T-F68/T-F87; cancellation = `OperationCanceledException` from both engines after cleanup.

### T-F261 — Routing and Group Policy have no single owner: Test/Scan bypass the routers, the policy is optional and fail-open (P1, root, decision)

- [~] **Progress (2026-09-28, fix phase 5, wave 2 track A):** code complete (6d5ee6a).
  `ArchiveFormatPolicy` is the one public classifier (policy checked before tar capability);
  `IExtractionRouter.TestAsync` (Shell `--test` and CLI `t` use it, tar.exe never starts for Test);
  `GroupPolicyOptions` is required on every engine and router (reflection test);
  `TarSandboxedService` refuses Extract/List/Compress and skips the version probe under
  `DisableTarExtraction`; `PakkoServices.Create(policy)` builds Shell and CLI services (CLI `i`/`l`
  had no policy at all before). The App keeps DI; the engine gates cover it. Device check with real
  policy values pending.
- **Device check (2026-09-28, Deploy 1.5.0.15, policy set through an elevated helper, tar.exe
  processes polled every 100 ms):** `DisableTarExtraction=1`: `pakko i` shows every tar format
  "blocked by Group Policy" and "tar.exe ... (disabled by Group Policy)"; `pakko l`/`t`/`x` on
  `.tar.gz` and `l` on `.7z` refuse with the policy reason (zip still lists); App start + browse
  `.7z` shows the policy error; Shell extract/test/scan on `.tar.gz` refuse; no tar.exe seen in any
  of ~10 operations (control run with the policy removed: the poll caught 2 of 5 version probes plus
  a listing). `BlockedFormats=sevenzip`: `pakko l x.7z` refuses, `.tar.gz` still lists.
  `BlockedFormats=zip`: Shell test/extract/scan on a zip refuse. The Explorer menu part is under
  T-F262. Found: T-F274 (Test says "no errors" when nothing was tested); the App's error box is
  English ("Error" + Core reason) in the uk-UA UI, T-F209.

- [~] **Status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — code-confirmed 2026-09-25. Extract, Create and List have routers; Test
  has none (`IExtractionRouter.cs:10-17` has only `ExtractAsync`, although
  `docs/ARCHITECTURE.md:73` says it routes `TestAsync`): Shell sends every path to the ZIP engine
  (`Archiver.Shell/Program.cs:292-299`, the root of T-F216), the CLI re-runs format detection
  with its own reason text (`Archiver.CLI/Program.cs:288-298`), the App has no Test (T-F241).
  Every router and engine takes `GroupPolicyOptions?` defaulting to "allow everything"
  (`ExtractionRouter.cs:11-13`, `ArchiveCreationRouter.cs:9-12`), so a call site that forgets it
  fails open — the CLI still does at `Program.cs:324,438-440`. `ArchiveListingRouter` takes no
  policy and keeps its own copy of `IsSupported`/`BuildUnsupportedReason`
  (`ArchiveListingRouter.cs:33-56`), justified by "two call sites", which stopped being true when
  T-F146 added `ArchiveFormatPolicy`. The CLI `i` command has a third, hand-written format table
  (`Program.cs:327-337`). "Never spawns tar.exe" (`docs/POLICIES.md:44`) is enforced by callers,
  not where tar.exe is launched. Composition roots are hand-built per command in Shell
  (`Program.cs:211-216,265,292,576-581`) and CLI (`Program.cs:77-79,301,324,367,438-440`) and
  each decides again whether to pass the policy — the same drift T-F51's plan hit when it missed
  the CLI.
  (Checked and refuted: Scan does not bypass the policy — `AntivirusScanService.cs:80` runs
  `ArchiveFormatPolicy.Classify` before it opens a `TarSandboxScope` directly.)
- **Fix seam:** `ArchiveFormatPolicy` as the single, public classifier for every operation;
  `TestAsync` on the router; `GroupPolicyOptions` required, not optional; the
  `DisableTarExtraction` gate also at the tar launch point (`TarSandboxedService`/
  `TarSandboxScope`) and before the capability probe; one plain Core factory function (e.g.
  `PakkoServices.CreateAsync(GroupPolicyOptions)`, not a DI container) that Shell and CLI use —
  justified by the two real misses above, not speculative.
- **Decision for the user (fix-batch index item 0):** `docs/ARCHITECTURE.md:1485` records that
  listing is *deliberately* not policy-gated, citing `ITarService.ListEntriesAsync`'s doc comment
  — but that comment (`ITarService.cs:35-41`) is about the entry-safety pre-scan, not Group
  Policy, while `docs/POLICIES.md:44` promises tar.exe is never spawned. Pick one: gate listing
  (T-F250 as filed) or narrow POLICIES.md's promise.
- **Leaves:** T-F250, T-F216, T-F241, T-F262.
- **Tests first:** a fake `ITarService` that fails the test when called, for each operation
  (extract, create, list, test, scan) under `BlockedFormats` and `DisableTarExtraction=1`; the
  factory passes the loaded policy to every service it builds.
- **Reported by:** architecture review, 2026-09-25.
- **Decision (2026-09-25):** gate listing by Group Policy; correct `docs/ARCHITECTURE.md:1485`.

### T-F263 — Staging and temporary folders have no single owner; the two extraction commit paths are synced by hand (P1, root, decision)

- **Progress (2026-09-27, fix phase 8):** CLI slice done with T-F244 item 4 (per-run staging
  folders named by PID, sweep of dead runs). App preview/nested cache roots remain (phase 9).

- **Device check (2026-09-26, Deploy 1.4.12.13, title build 2026-09-25 23:58:24, agent via Shell/`windows` MCP):** a 150 MB `.tar.bz2` cancelled from the Shell dialog: the process exits at once, no files in the destination, no tar.exe, no staging folder. (Two earlier attempts did not really press Cancel: a UIA invoke on the `IProgressDialog` button does not set its cancel flag, and a mouse click hit the always-on-top terminal.)

- **Progress (2026-09-25, fix phase 4):** tar extraction now stages and commits through `ExtractionStaging` like ZIP (03a65e3): a cancel during the move phase leaves no files; renamed conflicts check claimed paths. Still open: CLI staging (phase 8), per-process caches + startup sweep (phase 9).

- **Progress (2026-09-25, fix phase 2 — the P0 slice only):** `ExtractionStaging` (unique owned
  `.pakko-x-<pid>-<guid>`, shared commit) is in use for ZIP extraction. Still open: tar on the same
  committer (phase 4), CLI staging (phase 8), per-process caches + the startup sweep of dead-process
  folders (phase 9) — a killed process still leaves its hidden staging folder behind until then
  (seen on device when a run was killed at its conflict dialog), and owner-only ACLs.

- [ ] **Status:** open — code-confirmed 2026-09-25. Seven staging mechanisms, each with its own
  naming, ACL and cleanup, and none with a startup sweep of leftovers: ZIP extraction's fixed
  `<dest>_tmp` (`ZipArchiveService.cs:1290` — the only fixed name, the root of T-F227/T-F228),
  tar's `PakkoTarStage_<guid>` (`TarSandboxedService.cs:1221`), the sandbox quarantine
  (`TarSandboxScope.cs:53`), the parallel writer's `.pakko-tmp-<guid>`
  (`ParallelSingleArchiveWriter.cs:162`), CLI stdin/stdout staging (`CliStreamStaging.cs:15,44`),
  and the App's preview and nested caches (`PreviewCache.cs:11`, `NestedArchiveCache.cs:14`).
  The two extraction commits use different algorithms — ZIP stages then commits
  (`ZipArchiveService.cs:1393-1425`), tar moves file by file from quarantine resolving conflicts
  on the way (`TarSandboxedService.cs:540-580`) — and fixes are copied between them by hand
  (T-F170 was fixed twice; the comment at `TarSandboxedService.cs:565-571` says so). The shared
  cache roots (DECISIONS T-F97/T-F98) conflict with the deliberately multi-process App (T-F88) —
  the root of T-F252.
- **Fix seam:** one Core staging primitive (unique name, owner-only ACL, `IDisposable`, a sweep
  of folders left by dead processes, per-process roots for the caches) and one shared committer
  for both engines (the T-F157/T-F158 pattern).
- **Decision for the user (fix-batch index item 0):** per-process cache roots (changes T-F97/
  T-F98's shared design) or keep them shared and only fix T-F252's window-close cleanup.
- **Leaves:** T-F227, T-F228, T-F197, T-F248, T-F252, T-F244 (CLI staging, A19). Related, not a
  leaf: T-F233 (same tar-staging layer; its hardlink-vs-copy fix is its own decision).
- **Tests first:** per mechanism — a pre-existing folder with the staging name is never reused or
  deleted; cleanup on cancel and on failure; two processes never share or delete each other's
  folders; the sweep removes only folders of dead processes.
- **Reported by:** architecture review, 2026-09-25.
- **Decision (2026-09-25):** per-process cache roots plus a startup sweep of dead-process folders.

### T-F266 — tar.exe command-line option injection through best-fit mapping ("WorstFit") (P0)

- **Device check (2026-09-26, Deploy 1.4.12.13, title build 2026-09-25 23:58:24, agent via Shell/`windows` MCP):** Explorer "Add to X.tar" on `r＂ --version ＂.txt` shows the refusal (code page 1251, use ZIP); no .tar created. Stays `[~]`.

- [~] **Status:** fixed in fix phase 4 (2026-09-25), device check at phase end. Found during the
  phase-4 T-F204 spike: tar.exe (bsdtar) reads its command line through the ANSI code page, and
  the C runtime converts it with best-fit mapping. A fullwidth quote (U+FF02) becomes `"` and
  splits a quoted argument: `tar -tf "x＂ --version ＂"` ran `--version` (exit 0, ACP 1251).
  Archive creation runs tar.exe unsandboxed, so a selected file or folder named this way could
  add tar options (e.g. `--use-compress-program=...`, command execution as the user) through
  Explorer "Add to X.tar", the App (Format = TAR) or `pakko a -ttar`; through Pakko's `-C parent
  name` shape the same file made tar.exe print environment fragments (`PATHEXT`) — out-of-bounds
  reads. Names outside the code page (U+2713) crashed tar.exe (0xC0000005, T-F204). Extraction
  runs sandboxed; its argv holds Pakko-owned paths and member names from tar's own listing.
  tar.exe is the only executable Pakko starts with ANSI argv (explorer.exe and ShellExecute are
  Unicode — checked 2026-09-25).
- **Fix:** `TarCommandLineEncoding` — a string may reach tar.exe only if
  `WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS)` converts it with no default character and
  `MultiByteToWideChar` gives it back unchanged (the OS tables the CRT uses). Checked for every
  argument in `SandboxedProcessLauncher` (every tar.exe launch goes through it since T-F244
  item 5), and for every name under a source before creation starts (tar.exe walks folders
  itself); a refused source is one `ArchiveError`, the rest are archived. Nothing changes where
  the ANSI code page is UTF-8.
- **Tests:** code-page unit tests (1251/1252/65001, best-fit fullwidth characters, unpaired
  surrogate), launcher refuses before creating a process, creation with an injecting name and
  with U+2713 deep in a folder (mutant "always representable" fails all three).
- **Reported by:** fix phase 4 spike, 2026-09-25.

### T-F268 — Explorer operations: one UI interface, then a WinUI 3 operation window (P2, design + spike)

- [~] **Status:** partial — steps 1-5 done and shipped in v1.6.0 (the tag smoke used the window and the Win32 fallback). Left: step 6, polish — v1.7.0 wave 5.
- **Device (G6 pass 5, 1.5.0.13, light theme, temporary until G7 - `OperationWindow` is G2-changed):** the conflict, password, progress and result states read correctly in the light theme. Keyboard: the conflict prompt opens with focus on "Пропустити" and cycles checkbox -> Перезаписати -> Перейменувати -> Пропустити; Space ticks "apply to all", Enter answers, Esc skips the one file; the password prompt opens in the field, Esc = "Пропустити архів"; Esc on a running extraction cancels it, Esc on the result closes the window. The window sizes itself per state (522 wide; 394/288/232 tall) and has no sizing frame, so the narrow-window check (E5) does not apply. Found T-F309 (a cancelled "Extract to X\" leaves its empty folder).

- [~] **Progress:** step 1 done 2026-09-26 — `IOperationUi`/`IOperationSession` + `Win32OperationUi`,
  `ShellCommands`, `OperationMessages`, `ShellServices`; `Program.cs` only parses and dispatches.
  Same windows as before, except T-F216's double box is now one, and results show even without a
  progress window. Also fixed: a cancelled `TestAsync` reported success (Core). Tests first,
  6/6 mutants killed, device-checked on 1.5.0.0 — see `docs/DECISIONS.md`, "T-F268 step 1".
  Next: step 2 spike (OS XAML Islands first). User's own Explorer click-through still pending.
- **Step 3 plan (user-approved 2026-09-26, `async-seeking-dove.md`):** a separate code-only WinUI 3
  helper exe (`Archiver.OperationUi`) driven by Shell over anonymous pipes, with Win32 fallback when
  it cannot start and Win32 failover if it crashes (window close = cancel, not failover); one window
  per Explorer command (closes T-F269); hidden start, shown after ~1 s / a prompt / a non-clean
  result; window logic in a plain `Archiver.OperationUi.Core`; HTML mockup approved before XAML.
  Order: Gate 0 (finish the spike in the real installed package) -> mockup -> one-window refactor on
  Win32 -> protocol -> helper progress/cancel/result -> prompts -> polish/37 locales.
- **Progress (2026-09-26):** Gate 0 passed (`docs/DECISIONS.md`); HTML mockup approved by the user
  (https://claude.ai/artifact/Kga8s2ntDYMT4dgon12UhX); step 2 done — `IOperationSession.BeginItem`,
  one session per extract command with one combined result (`ShellCommands.RunExtractSelectionAsync`),
  Win32 title names each archive of a selection; closes T-F269.
  Step 3 (protocol) done — `Archiver.OperationUi.Protocol` + `tests/Archiver.OperationUi.Tests`.
  **Decision (agent, user-delegated 2026-09-26):** step 5 extends Core's `ConflictInfo` with optional
  incoming-entry size and modified time (additive, null when unknown) so the conflict prompt can
  compare both files as the approved mockup shows; the App's T-F06 dialog keeps working unchanged.
  Step 4 (helper progress/cancel/result) done in three commits: `Archiver.OperationUi.Core`
  (`OperationWindowModel`, 23 tests), Shell `HelperOperationUi` + `HelperProcessLauncher` (fallback
  on no start / no ready in 5 s / another protocol version, failover on EOF without
  `WindowClosed`, close = cancel; 17 tests on real in-process pipes; `FallbackOperationUi` from the
  plan is the `fallback` parameter, not a class), and the code-only WinUI exe
  `Archiver.OperationUi` + packaging (App.csproj, Deploy.ps1, CI-Build-Msix.ps1) + DIAGRAMS
  diagram 8. Agent-checked on the installed 1.5.0.2 via UIA: a fast clean extract shows no window;
  a 5-archive extract shows the window at ~1.8 s with "Archive 2 of 5", progress, status and
  "Cancel all" and closes itself at the end; Cancel all stops the selection with no partial
  folder and no Win32 window; killing the helper hands the operation to `IProgressDialog`
  (4/5 -> 5/5, all extracted); Test shows its result in the window until Close. The window's
  look (light/dark, Mica, DPI) needs the user's eye (agent captures come out black); UIA reports
  the title bar's Maximize button enabled despite `IsMaximizable = false` - check on screen.
  Found on device in the closing review: a Win32 password or conflict prompt lost the foreground
  to the helper window when it showed 1 s in (typing went to the window; the conflict dialog went
  behind it). Until step 5, a prompt hands the rest of the operation to the Win32 windows (tests
  first, 2 red before the fix; re-checked on 1.5.0.4: the prompt keeps the foreground and
  `IProgressDialog` takes over). Also checked on 1.5.0.4: Archive (live progress), Hash (multi-line
  result), Scan (result), Extract to folder (fast, no window). The plan's "prompt open at crash -> re-asked via Win32" test moves
  to step 5 with the prompts. Next: step 5 (conflict and password prompts in the window).
  Step 5 (2026-09-27): conflict and password prompts inside the window, step 4's prompt
  hand-over removed; a prompt open at a helper crash is asked again via Win32, one open at a
  cancel gets Skip / no password without asking. `ConflictInfo` carries the incoming file's size
  and time. Tests first; Shell prompt paths mutation-checked. See `docs/DECISIONS.md`.
  Step 6, localization (2026-09-27): every label Shell sends the window, every operation title
  and the size units come from a new `OperationText.resx` in 37 locales (translated by the agent,
  plural-safe wording for counts); polish (b) done (`ScanNoThreatsFoundMany`); polish (c)
  done (`BeginItem` starts a fresh speed sampler in both UIs; test first); polish (a) done (hash
  results `Preformatted`: monospace, no wrap, horizontal scroll - see `docs/DECISIONS.md`).
  Polish (e) done (user decision: Minimize and Maximize both off, the caption shows only X).
  Agent-checked on 1.5.0.12 under uk-UA (UIA, not pixels): a folder's 73-character SHA-256
  DataSum stays on one line (4 lines, 74 px tall; ScrollViewer horizontally scrollable, 55% in
  view), caption buttons Minimize/Maximize hidden, 3-archive Extract here clean. (g)'s
  tooltip could not be raised by a synthetic hover - check on screen. A review of the
  existing Shell `.resx` strings and the Explorer menu table fixed 14 translation defects and a
  Latvian menu typo; the App's own `.resw` was not reviewed (only its copies of the same strings
  were fixed). Not checked on screen: RTL (he/ar/ur) and any language but Ukrainian. Checked on
  1.5.0.10 under uk-UA: the conflict window and its result are Ukrainian except Core's own reason
  text (T-F209). T-F208's Shell part is done; its grouped root (Core message codes) stays open.
  (g) Under uk-UA the caption buttons' UIA names are English (Minimize/Maximize/Close; drawn by
  WinAppSDK) - check their tooltips on screen; the main App window uses the system title bar.
- **Explorer smoke (agent, 2026-09-26, build 1.5.0.5, real context menu clicked via UIA, window
  captured by screen region - a per-window capture of WinUI comes out black):** Test (1 archive,
  corrupted -> red error icon), Extract here smart (3 archives), Extract each to its folder (3,
  cancelled with the title bar X: the running archive left no folder), Extract here on the
  encrypted ZIP (Win32 password prompt kept the foreground, Win32 progress took over, extracted),
  Add to "<folder>.zip" (3 folders, live progress), SHA-256 (3 files), Scan (3 archives), Open
  (Archiver.App browse). Light and dark theme both render per the mockup; Enter, Esc and the title
  bar X close a result. No crash events. **Polish for step 6:** (a) a SHA-256 value wraps mid-hash
  in the result - monospace and/or no wrap; (b) Scan's clean text is singular ("this archive") for
  several archives (`OperationMessages.ForScan`, pre-existing); (c) at the start of the next archive
  the status still shows the previous archive's speed - reset the speed sampler in `BeginItem`;
  (d) titles, buttons and Core messages are English (T-F208, step 6 localization); (e) the caption
  shows a greyed Maximize button - consider hiding it as a dialog does. (f) **Open, found
  2026-09-27 on 1.5.0.9, seen by the user too:** the window sometimes shows all black (caption
  buttons only) until clicked; the UIA tree is complete, so only rendering is missing. Seen on
  about ten launches in a row (from Shell started outside Explorer), then never again in six
  launches after one click on a black window, including after a package reinstall and with
  the window left inactive. Trigger not identified - reproduce before fixing; step 4's Explorer
  smoke never looked at pixels after the first frame.
  **Reproduced 2026-09-27 on 1.5.0.12/1.5.0.13, 16 of 16 launches:** `Archiver.Shell.exe --test
  <corrupt.zip>` started by `Start-Process` from a background shell (probe: pixel sampling of the
  window over time). The window never gets the foreground (Shell has no foreground right to pass
  on). It fades in already black. One real activation fixes it for good: after it the window
  renders even when inactive again. Temporary diagnostics (reverted) showed XAML itself works: the
  `Activated` (CodeActivated) and `VisibilityChanged` events fire, `CompositionTarget.Rendering`
  ticks at ~30 fps, and `XamlRoot` has the right size. The child HWNDs (`DesktopChildSiteBridge`
  520x230, visible) are identical before and after the click. Tried without effect (three
  attempts, stopped by the three-attempts rule): no Mica; `ShowWindow(SW_SHOWNOACTIVATE)`,
  `AppWindow.Show(false)` or a fake `WM_ACTIVATE` before or after `Activate()`; showing without
  `Activate()` (`AppWindow.Show(false)` alone, `SW_SHOWNA` alone); an external `RedrawWindow`,
  `SWP_FRAMECHANGED` and a 1 px resize. No matching WinAppSDK 1.8.x fix in its release notes.
  **Not reproduced 2026-09-29 on 1.5.0.34, 4 of 4 launches:** the same `Start-Process` `--test <corrupt.zip>` from a background shell. Once behind the App (rendered when moved into view, no activation), then three times with every App window minimized: the window stayed inactive and pixel sampling in place showed the full content each time. G3 is dropped per plan 8.8 unless it recurs.
  **Not verified:** the real Explorer flow. There Shell holds the click's foreground right and
  passes it to the helper (`AllowSetForegroundWindow`), so the black window is expected only when
  that right is gone by the time the window shows. Example: the user switches to another app
  during an operation longer than the ~1 s show delay. Next ideas, not yet tried:
  - create the window only when it is first shown, instead of hidden at start;
  - a minimal WinUI repro to report upstream;
  - a WinAppSDK update.
  (g) closed as an upstream limitation: the caption X is drawn and named by WinAppSDK
  (`ReunionWindowingCaptionControls`). A synthetic hover shows its hover state but raises no
  tooltip even after 3.5 s, so its tooltip cannot be checked by automation. Pakko code sets
  neither the name nor the tooltip.
- **Status (original):** open — user request 2026-09-26: Explorer-triggered dialogs look out of place on
  Windows 10 and 11. User chose a separate lightweight WinUI 3 window (not the main App window)
  for progress, conflict, password and result, and asked for one UI entry point instead of the
  current mix. Today `Archiver.Shell` uses five native mechanisms: `MessageBoxW` (results, errors,
  `Program.cs`), `TaskDialogIndirect` (`ShellConflictDialog`), a custom `DLGTEMPLATEEX` dialog
  (`PasswordDialog`), `IProgressDialog` (`NativeProgressDialog`), and Core callbacks wired per
  command through `StickyCallback` (conflict, password; the compression-bomb confirm, T-F217).
  No public Win32 API gives these a Windows 11 look; dark mode for Win32 dialogs exists only
  through undocumented `uxtheme` exports — rejected for this audience.
- **Step 1 — one interface, no visual change:** an `IOperationUi` (progress + cancel, conflict,
  password, compression-bomb confirm, result/error summary, "too large" message) that every
  Shell command uses; the current Win32 code becomes its first implementation. Pure refactor,
  tests first (a fake `IOperationUi` drives each command's prompt/cancel/result paths).
- **Step 2 — spike before any WinUI code (hard gate):** the deleted `Archiver.ProgressWindow`
  (a second WinUI 3 exe started with `Process.Start`) crashed in WinUI init (`0xc000027b`, see
  `docs/DECISIONS.md`, "Progress UI: IProgressDialog replaces Archiver.ProgressWindow"); the
  suspected cause was starting a WinUI process without activation. T-F232 now gives a real
  activation path (`ActivateApplication`). Measure, on the installed MSIX: (a) whether a WinUI
  window can be shown this way — as a second `<Application>` in the package, or as an
  "operation mode" of `Archiver.App.exe` with its own small window; (b) cold-start time vs the
  current dialogs; (c) z-order/foreground when started from Explorer (T-F253); (d) behavior with
  the main App window already open (single-instance redirection, T-F201). Record results in
  `docs/DECISIONS.md` and choose the host with the user before step 3.
- **Step 3 — WinUI implementation of `IOperationUi`** in the chosen host; Fluent/Mica, light and
  dark, 37 locales, keyboard and Narrator (T-F267 lessons). Win32 implementation stays as the
  fallback if the WinUI host cannot start (fail visible, never silent). Designed together with
  T-F199's mockup so the look changes once.
- **Must not break:** Explorer COM DLL -> `Archiver.Shell` command line (T-F235 limit); Shell ->
  App `LaunchArguments` hand-off (T-F232); Core callbacks run off the UI thread and must marshal
  (CLAUDE.md UI-thread rule); cancel -> `CancellationToken` -> no partial files (T-F263);
  "apply to all" across a multi-select (`StickyCallback`); password never logged or passed on a
  command line; every Shell command still works when the App is not running or is busy.
- **Related:** T-F199 (redesign), T-F208 (English titles/units), T-F216 (two modals), T-F217
  (bomb dialog dead end), T-F253 (dialog behind other windows), T-F255 (255-char password cut).
- **Reported by:** user, 2026-09-26.

### T-F284 — tar extract-selected matches entry names as patterns: "a[1].txt" extracts "a1.txt" (P2)

- [x] **Status:** done 2026-10-01 (22148af): `TarSandboxScope.EscapeMemberPattern` puts `[`, `*`, `?` into a one-character class at both member sites. Device (dev 1.6.0.1, App browse of `glob.tar` holding `a[1].txt` and `a1.txt`): Extract Selected on `a[1].txt` wrote only `a[1].txt` ("bracket"). See `docs/DECISIONS.md`'s wave 2 entry.
  Report as filed: open — confirmed 2026-09-29 with `tar.exe` directly (bsdtar 3.8.8): a tar holding
  `a[1].txt` and `a1.txt`, `tar -xf g.tar -C out -- "a[1].txt"` exits 0 and writes only `a1.txt`.
  bsdtar reads extract member arguments as wildcard patterns, so the Archive Browser's Extract
  Selected / preview / nested drill-in on a tar-family entry whose name holds `[`, `*` or `?` can
  extract a different entry, or several, and miss the selected one. Inside the sandbox and after
  the pre-scan, so not a security issue; a wrong-result bug. Predates T-F283 (`--` only ends
  options, it does not make members literal).
- **Fix direction:** check whether this bsdtar build honours backslash-escaping in member patterns
  on Windows, or select by extracting through a `-T` list with an exact-match mode, or filter
  after extraction in quarantine; tests first with a bracket-named entry next to its glob match.
- **Reported by:** T-F283 closing review, 2026-09-29.

### T-F285 — tar creation from a `subst` drive root fails inside tar.exe (P3)

- [ ] **Status:** open — found 2026-09-29 in the T-F283 device pass. Shell `--archive --format tar
  P:\` (P: = `subst` of a small folder) shows "tar.exe не зміг створити архів: ... Couldn't visit
  directory" with a garbled path. `tar.exe` alone fails the same way with `P:\` as an argument
  (the pre-T-F283 form), as a `-T -` list line, as `P:/`, and as `-C P:\` + `.`; `-C P:\` +
  `r.txt` fails with "GetVolumePathName failed: 123". A bsdtar limitation, not a Pakko regression.
  A real volume root was not tried (needs a small real drive or an elevated VHD).
- **Fix direction:** check a real volume root first; if only `subst`/mapped drives fail, resolve
  the drive to its target path (`QueryDosDevice`) before building the list, or refuse with a clear
  message. The archive name for a drive root is T-F281.
- **Reported by:** T-F283 device pass, 2026-09-29.

### T-F288 — `[ComImport]` interfaces to `[GeneratedComInterface]` (SYSLIB1096, P3)

- [ ] **Status:** open — split out of T-F287, 2026-09-29 (that task was about `DllImport`; this is
  a different mechanism, info-level in Sonar, never a build error). Not scheduled in G2.
- **Scope:** `Archiver.Shell/AppLauncher.cs` (`IApplicationActivationManager`, coclass `new`) and
  `Archiver.Shell/NativeProgressDialog.cs` (`IProgressDialog`) to `[GeneratedComInterface]` +
  `StrategyBasedComWrappers`, created with a `CoCreateInstance` `[LibraryImport]` instead of `new`
  on a `[ComImport]` coclass.
- **First step, before any code:** a generated wrapper is not apartment-bound like a classic RCW
  (no cross-apartment marshalling of calls). Check where `NativeProgressDialog` is created, which
  threads call it (`Win32OperationUi.Session._dialogLock` suggests several) and whether Shell's
  `Main` is `[STAThread]`; if creation and calls are on different threads, the design needs
  explicit marshalling or one owning thread.
- **Keep:** `HasUserCancelled`'s `BOOL` return — `[PreserveSig]` semantics (CLAUDE.md hard
  constraint; Cancel did nothing without it). `NativeProgressDialog` is a documented known test
  gap, so the device check (progress, Cancel) is the acceptance gate; `AppLauncher` runs only
  under package identity.
- **Reported by:** T-F287 advisor review, 2026-09-29.

### T-F286 — a crash during tar creation can leave a junction to the user's folder in `%TEMP%` (P3)

- [x] **Status:** done 2026-10-01 (338f9dd): staging folders are named `PakkoTarStage_<pid>_<guid>` and each tar creation first sweeps those whose process no longer runs (links first, then the folder). Device (dev 1.6.0.1): a planted `PakkoTarStage_999999_*` with a junction to a user folder was gone after Explorer-style `--archive --format tar`, the user file intact; one named after a running process stayed.
  Report as filed: open — found in T-F171's closing review, 2026-09-29. A colliding folder source is
  staged as a junction in `%TEMP%\PakkoTarStage_<guid>\`, removed in `CompressToArchiveAsync`'s
  `finally`. If the process is killed or crashes while tar.exe runs, `finally` never runs and the
  junction stays. A tool that deletes `%TEMP%` recursively and follows junctions (Windows
  PowerShell 5.1 `Remove-Item -Recurse`, some cleaners) would then delete the user's files.
- **Fix direction:** at the next `CompressAsync`, sweep stale `PakkoTarStage_*` folders: remove
  every reparse point inside non-recursively first, then the folder.
- **Reported by:** T-F171 closing advisor, 2026-09-29.

### T-F275 — Recovery data for archives: PAR2 files next to the archive (P3, future)

- [ ] **Status:** open, future — not scheduled; needs a `docs/SPEC.md` scope decision before work.
  Option to write PAR2 (Reed-Solomon) recovery files next to a created archive, with a chosen
  redundancy (e.g. 5%), and to verify/repair an archive from them. Use: archives kept on flash
  drives or optical media or carried offline, where bad sectors or a truncated copy are the
  realistic damage.
- **Decision (user, 2026-09-28):** PAR2 files next to the archive, not a recovery entry inside the
  ZIP. Reasons: third-party tools (par2cmdline, MultiPar) can check and repair them, which fits the
  auditability goal; no Pakko-only format to maintain; a recovery entry inside the ZIP would be
  lost with a truncated tail (the central directory is at the end) unless written first, and
  other archivers would extract it as an unexplained file.
- **Constraints:**
  - Recovery is computed over the finished archive's bytes — for an encrypted ZIP that is
    ciphertext, so the PAR2 files reveal nothing beyond it and are not encrypted themselves
    (repair must not need the password). Never compute it over plaintext.
  - Output must be standard PAR2 that par2cmdline accepts for verify and repair (tested against
    it, like the 7za cross-checks). Reed-Solomon is Pakko's own code: no NuGet packages in Core.
  - Reading PAR2 files is new untrusted input: bounded memory and sizes, every field validated,
    repair writes only to a new file next to the archive, never over the original.
  - Tests: flip bytes, zero whole blocks, truncate the tail, damage the PAR2 files themselves;
    the repaired archive must match byte for byte, and damage beyond the redundancy must fail
    cleanly.
- **Scope (user, 2026-09-28):** every archive format Pakko creates (ZIP and the tar family — the
  scheme works on the archive's bytes, so the format does not matter). Offered wherever the user
  sets archive options: the App's "New archive" card, `pakko a`, and Explorer's "Compress..."
  options dialog; not on Explorer's one-click verbs ("Add to X.zip"/"Add to X.tar"), which have no
  options.
- **Open questions:** where verify/repair lives (App, `pakko`, an Explorer verb on a `.par2` or
  the archive); Group Policy control.
- **Reported by:** user question, 2026-09-28.

### T-F280 — ZIP Test ignores local-header mismatches that 7-Zip reports (P2)

- **Decision (user, 2026-09-30):** Test reports a local/central header mismatch as an error
  ("Headers Error", as `7za t`); extraction carries on from the central directory and adds a
  warning to the summary.
- [~] **Status:** Test half done 2026-09-30 (v1.7.0 wave 1; Device (Deploy 1.6.0.0 from 480cb74, App title build 2026-09-30 22:47:15, uk-UA, 2026-09-30): a ZIP with one local CRC
  flipped -> `pakko t` exit 2 with the English message, Explorer Test window in Ukrainian). Left: the extraction
  warning, which needs a warning channel in `ArchiveResult` — v1.7.0 wave 6 (see
  `docs/DECISIONS.md`'s T-F280 entry). **CHANGELOG v1.7.0:** Test reports a ZIP whose local file
  headers disagree with its central directory, as 7-Zip does.
- **Earlier status:** open. A ZIP whose local file headers disagree with the central directory
  (an entry's local CRC field and another entry's local name byte flipped; data and central
  directory intact) passes Pakko's Test ("не виявлено помилок", Explorer Test, 1.5.0.34), while
  `7za t` reports "Headers Error" and "CRC Failed : dir\a.txt". Pakko reads everything through
  the central directory, so its own extraction is consistent, but local/central name mismatch is
  the classic ZIP-ambiguity vector (different tools extract different names). Decide: warn in Test
  (and maybe extraction) when the local header's name, sizes or CRC disagree with the central
  directory. Tests first; check the cost on large archives (one extra header read per entry).
- **Reported by:** G1 device pass (T-F268 (f) re-check fixture), 2026-09-29.

### T-F281 — Archive auto-name for a drive-root source is "archive" (P2)

- **Decision (user, 2026-09-30):** the drive letter — `C.zip` for `C:\`; a UNC share root keeps
  the share name.
- [x] **Status:** done 2026-09-30 (v1.7.0 wave 1). Device (Deploy 1.6.0.0 from 480cb74, App title build 2026-09-30 22:47:15, uk-UA, 2026-09-30): on a `subst` drive P:, Explorer's menu reads
  "Додати до \"P.zip\"" and Shell `--archive P:\` creates `P.zip`.
- **Progress (2026-09-30):** `ArchiveNaming.GetDefaultArchiveName` and C++ `BuildAddToArchiveTitle`
  name a drive root, and several files at one, after the upper-cased letter (`C.zip`, `Z.tar`).
  Tests first on both sides (C# 6 red, C++ 4 red with the old code), full suites green. See
  `docs/DECISIONS.md`'s T-F281 entry. **CHANGELOG v1.7.0:** compressing a whole drive names the
  archive after its letter (`C.zip`) instead of `archive.zip`.
- **Earlier status:** open. Split out of T-F213 (closed 2026-09-29). Compressing a drive root (App or
  Explorer) names the archive `archive.zip` by design (T-F99/T-F100); a name from the drive letter
  or volume label (`C.zip`, `Data (D).zip`) would be friendlier. Naming lives in the shared
  `ArchiveNaming` rule (T-F264) — change it once for all frontends. Tests first.
- **Reported by:** T-F202, 2026-09-24 (as part of T-F213).

### T-F282 — Main window: option cards clip below ~600 px height (P3)

- [x] **Status:** closed 2026-10-03 (v1.7.0 wave 4) — fixed by T-F224's `OptionsScroll`. Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03): at 900x520 (window rect 100,100-1000,620, 96 DPI, screenshots) the options pane shows the card down to Format; keyboard Tab from Compression goes Encrypt -> destination Up -> destination box -> "..." and the pane scrolls with it (the "..." button on screen with its focus ring, scroll bar shown). A UIA SetFocus alone does not scroll it into view. Earlier status: Split out of T-F224 (closed 2026-09-29). `PreferredMinimumHeight` is 520,
  and below about 600 px the create-mode cards ("Новий архів", "Куди і що після") are cut off with
  no scrolling — Name, Format and the destination become unreachable (886x513 on 1.5.0.34); the
  footer and primary actions stay visible. Either scroll the options area or raise the minimum to
  the height where everything fits (it fits at 665). Realistic screens (1366x768, 1920x1080 at
  150%) are fine.
- **Reported by:** G1 device pass, 2026-09-29.

### T-F290 — tar.exe on Windows ARM64 crashes on a non-ASCII name argument (P3)

- [ ] **Status:** open. On the `windows-11-arm` runner, `C:\Windows\System32\tar.exe -czf
  out.tar.gz <name>` with a Cyrillic file name exits with 0xC0000005 (access violation), both
  attempts of the T-F240 canary run 36616898216. The x64 runners pass the same fixture. The failing
  call is the test fixture (`ExternalTarFixtureBuilder`), not Pakko. Open question, which sets the
  priority: does it crash only on a name the ANSI code page cannot represent (Pakko already refuses
  those before tar.exe runs, `TarCommandLineEncoding.IsRepresentable`), or on any non-ASCII name
  (then TAR creation on an ARM64 machine whose code page does cover it, e.g. Cyrillic under 1251,
  would hit it)? `canary-arm64`'s "tar.exe name probe" step logs the tar version, code pages and
  exit codes per name and form; read it, then decide. Until then
  `ExtractAsync_TarGzWithUnicodeFilenameAndContent_ExtractsCorrectly` is skipped on an ARM64 OS
  (`IntegrationNotOnArm64Attribute`).
- **Probe result (run 36620565447, bsdtar 3.8.8, ACP 1252, OEM 437):** `plain.txt` and `café.txt`
  exit 0 with `-cf` and `-czf`; a Cyrillic name exits 0xC0000005 with both. So it crashes on a name
  the ANSI code page cannot represent, not on any non-ASCII name — Pakko refuses such names before
  tar.exe runs, so production is not exposed; P3. Left open: the same check on a machine whose ACP
  covers Cyrillic (1251), and whether to report it upstream. (The probe's list-file runs are not
  evidence: pwsh wrote the list in UTF-8, which tar.exe reads in the ACP.)
- **Reported by:** T-F240 canary, 2026-09-29.

### T-F291 — Explorer's folder hash hides which entries failed (P2)

- [ ] **Status:** open. Found redrawing diagram 1 (T-F258). For a single-folder selection
  `OperationMessages.ForHash` shows only the Files/Size/DataSum/NamesSum lines; the error entries
  `FileHashService` adds since T-F251 (an unreadable subfolder, a skipped junction or symlink, a file
  that shrank) only turn the icon into a warning. The user cannot tell what the sums leave out.
  `pakko h` prints each failed entry to stderr (`Program.cs` `PrintHashEntriesAsync`), so the two
  frontends differ. Fix: list the failed entries (capped like the file list) under the summary;
  test in `OperationMessagesTests` first.
- **Reported by:** T-F258 diagram redraw, 2026-09-29.

### T-F292 — Diagram 6 (MainWindow UI mode) predates the T-F199 redesign (P2, docs)

- [ ] **Status:** open. Diagram 6's per-row visibility table still lists the pre-redesign rows
  (Row 0 buttons, Row 6 conflict combo and checkboxes). Since wave 4 (T-F199) the window is
  `AppTitleBar` + `ContentGrid` with mode-gated rows, the option cards (`NewArchiveCard`,
  `DestinationCard`) in `OptionsScroll`, and a footer. No `docs/DIAGRAMS.md` commit touched diagram
  6 in that wave, although its DoD row fires on any row or visibility change. Redraw from
  `MainWindow.xaml` and `docs/XAML.md` per the Ground Truth Rule; mermaid-cli.
- **Reported by:** G5 closing review, 2026-09-29.

### T-F293 — `pakko x` suggests `-aoa` after a CRC failure (P2)

- [x] **Status:** done 2026-10-03 (b841f86, v1.7.0 wave 3), through T-F296's `CliHints`. Device (Release `pakko.exe` from 6ce6574/acd212a, 2026-10-03), cmd
  `chcp 866`: a second `pakko x -oout c1.tar.gz` onto the first one's files prints the `-aoa` hint;
  `Extract_CrcFailure_NoOverwriteHint` (red before the fix) pins the CRC case.
  Report as filed: open. Found in G6 (CI build af43cae, `pakko.exe` 0.0.0-dev+af43cae, 2026-09-30).
  `bad.zip` = one stored entry with one flipped data byte; `pakko x bad.zip -obad` prints the CRC
  error, then "skipped: No entries were extracted from this archive — every entry was skipped" and
  "hint: existing files were kept; -aoa overwrites them, -aou renames the extracted ones" (exit 2).
  No file existed at the destination; `-aoa` cannot help. `PrintHints` (`Program.cs` ~line 628)
  fires on `MessageCode.AllEntriesSkipped` whenever `KeptExistingByDefault`, without checking that
  a conflict caused the skips. Fix: show the hint only when a `FileExistsAtDestination` skip exists;
  consider whether `AllEntriesSkipped` belongs on a result whose entries all failed. Test first in
  `Archiver.CLI.Tests` (a CRC-failure extraction prints no `-aoa` hint).
- **Folded into T-F296** (user decision 2026-09-30): the fix is T-F296's cause-keyed hint table.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F294 — PowerShell splits `-ttar.gz`; pakko then writes a plain tar silently (P2)

- [x] **Status:** done 2026-10-03 (6ce6574, v1.7.0 wave 3), user decision: an error plus dot-free
  aliases. Device (Release `pakko.exe` from 6ce6574/acd212a, 2026-10-03), pwsh 7: the three commands below each exit 7 naming `'-ttar'` and
  `'.gz'`; `'-ttar.gz'` quoted and `-ttgz` write a real gzip (`1F 8B`); `-pSecret.1` is exit 7 without
  printing the password; `a t6.tar.gz src` (no `-t`) is exit 7; `-ttar .\.gz` writes `.gz`. In cmd
  the same `-pSecret.1` passes whole and `t` opens the archive with it. See `docs/DECISIONS.md`'s wave 3 entry.
  After the closing review: `a backup.tgz src` is exit 7 too, `-oout.d` (pwsh) is exit 7, and
  `h -scrcSHA256 .gitignore` is accepted. **CHANGELOG v1.7.0 (changes script behaviour):**
  `pakko a out.tar.gz src` without `-t` used to write a ZIP under that name and now stops with exit 7
  asking for `-ttar.gz`; a `-p`/`-o`/`-t` value that PowerShell split at a dot (`-ttar.gz`,
  `-pSecret.1`, `-oout.d`) is exit 7 instead of a wrong type, a shorter password or another folder;
  new dot-free `-ttgz`/`-ttbz2`/`-ttxz`/`-ttzst`; an archive named `.gz` keeps that name.
  Report as filed: open. Found in G6, 2026-09-30, pwsh 7. PowerShell passes `-ttar.gz` to a native
  exe as two arguments, `-ttar` and `.gz` (a known PowerShell parser rule for `-name.suffix`
  tokens). Results: `pakko a -ttar.gz t.tar.gz src` -> "Source path does not exist: t.tar.gz"
  (`.gz` became the archive name); `pakko a t2.tar.gz -ttar.gz src` -> exit 0 and an
  **uncompressed** tar named `t2.tar.gz`; `pakko a -ttar.gz t3.tar.gz a.txt` -> writes `.gz.tar`.
  The same command in cmd works (`t4.tar.gz`, 383 bytes, gzip). Real 7-Zip has the same exposure,
  but `-tgzip` has no dot. Options: document quoting (`'-ttar.gz'` or `--%`) in `docs/CLI.md` and
  `--help`; accept dot-free aliases (`-ttgz`, `-ttbz2`, `-ttxz`, `-ttzst`); warn when the `-t` type
  and the archive name's compound extension disagree. Separately, an explicit name `.gz` becomes
  `.gz.tar` while `7za a -ttar .gz x` writes `.gz` as typed (T-F221 made "explicit name as typed"
  the rule). Decide the fix with the user; tests first in `Archiver.CLI.Tests`.
- **Folded into T-F296** (user decision 2026-09-30): the `-t`/name mismatch check is T-F296's
  validation phase; the PowerShell quoting note in `docs/CLI.md` and `--help` stays here.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F295 — CLI console polish from the real-terminal pass (P3)

- [x] **Status:** done 2026-10-03 (acd212a, v1.7.0 wave 3). Device (Release `pakko.exe` from 6ce6574/acd212a, 2026-10-03), a real conhost
  console driven through `WriteConsoleInput`/`GenerateConsoleCtrlEvent`, screen read back: Ctrl+C at
  13% of `a` and during `t` of a 3 GB archive leave `pakko: operation stopped by user` on a clean
  line; `a -p` shows `Enter password (input is masked): ***`; the overwrite prompt shows `Size:` and
  `Modified:` of both files.
  Report as filed: open. Found in G6, 2026-09-30, Windows Terminal + cmd, code page 866:
  1. Ctrl+C during `a big.zip big.bin` prints `" 57%pakko: operation stopped by user"` on the
     progress line — clear the progress line (or print a newline) before the message.
  2. The masked prompt says "Enter password (will not be echoed):" but echoes `*` per character
     (7-Zip's wording, but 7-Zip prints nothing). Say "(input is masked)" or drop the remark.
  3. The overwrite prompt shows only the existing path; 7-Zip's `AskOverwrite` also shows size and
     modified time of both files, which is what the user needs to answer Y/N.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F296 — CLI: a semantic validation phase and a cause-keyed hint table (P2)

- [x] **Status:** done 2026-10-03 (b841f86, 6ce6574, v1.7.0 wave 3): `CliCommandValidator` and
  `CliHints` with its exhaustiveness test; evidence under T-F293 and T-F294.
  Report as filed: open. Filed 2026-09-30 from G6 pass 1 (user decision: one task, next batch).
  `Archiver.CLI` today has two phases that matter here: `CliArgumentParser` checks each switch on
  its own, and `ReportResult`/`PrintHints` (`Program.cs` ~line 623) guesses hints from symptom codes.
  Two findings came from that gap: T-F293 (the `-aoa` hint after a CRC failure, because
  `AllEntriesSkipped` does not say why) and T-F294 (`-ttar` plus a name ending `.gz` accepted without
  a cross-check). Add:
  1. **Validation phase** between parsing and execution: `ParsedCliCommand` -> a resolved plan
     (format, archive name, destination, conflict mode) with cross-switch rules in one table; a
     contradiction is a usage error (exit 7) or, where 7-Zip stays silent, a warning. First rules:
     `-t` vs the archive name's compound extension; `-p` with a non-ZIP format (already exists,
     move it into the table).
  2. **Hint table** keyed on the cause code (`MessageCode`) of each error/skip, not on
     `AllEntriesSkipped`. An exhaustiveness test lists every code as "has hint" or "no hint", like
     `AppResourceKeysTests`, so a new code cannot slip through unnoticed.
  No parsing library (System.CommandLine does not fit 7-Zip's glued `-ttar`/`-o{dir}`/`-p{pwd}`
  syntax and would be a new dependency). Tests first in `Archiver.CLI.Tests`: the T-F293 and
  T-F294 repros, plus the exhaustiveness test.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F297 — Error details from the OS or Core stay English in localized windows (P3)

- **Decision (user, 2026-10-03):** code + English text everywhere, and for Ukrainian only a
  Ukrainian translation added in front ("Додай до англійської українську і тільки її"). Mapped:
  invalid name, access denied, sharing violation, disk full, and Core's CRC-32 mismatch; any other
  Windows error shows its English text and code. The other 35 locales get no translation.
- [~] **Progress (2026-10-03, v1.7.0 wave 4):** done in code, tests first (`CoreMessages.Detail`,
  five codes, uk-only entries; see `docs/DECISIONS.md`'s wave 4 entry). Device evidence below.
- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4). Earlier status: Found in G6 pass 2, 2026-09-30, uk-UA, Explorer operation window:
  Test of `bad.zip` shows "Елемент «doc.txt»: Content failed CRC-32 check (expected 73DCA397, got
  A43E23CF)."; `--extract-folder qmark.zip` shows "Не вдалося видобути «What?.txt»: The filename,
  directory name, or volume label syntax is incorrect. : '...'". The frame is localized (T-F209);
  the detail is an exception message: `IO/VerifyingReadStream.cs:54` (Core's own English text) and a
  .NET `IOException` message. Fix direction: give the CRC mismatch its own `MessageCode` with
  expected/actual as arguments; for OS I/O errors map the common HResults (invalid name, access
  denied, sharing violation, disk full) to codes and keep the raw text only as a fallback.
  Tests first (`Archiver.Messages.Tests` parity + a Core test that the CRC error carries the code).
- **Reported by:** G6 device campaign, 2026-09-30.
- Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03), Explorer operation window: `--extract-folder qmark.zip` shows "Не вдалося видобути «What?.txt»: Неприпустимий синтаксис імені файлу, папки або мітки тому — The filename, directory name, or volume label syntax is incorrect. : '...\What?.txt'. (0x8007007B)"; `--test badcrc.zip` shows "Вміст не пройшов перевірку CRC-32 (очікувалося 73DCA397, отримано A43E23CF) — Content failed CRC-32 check (...)".
### T-F298 — ZIP extraction does not restore the entries' modification times (P2)

- **Decision (user, 2026-09-30):** files and folders, as 7-Zip: the time from the NTFS extra field
  (0x000A) or the Unix extended timestamp (0x5455) when present, else the DOS time; folders get
  their time after their contents are written; ZIP and tar behave the same.
- [x] **Status:** done 2026-09-30 (v1.7.0 wave 1). Device (Deploy 1.6.0.0 from 480cb74, App title build 2026-09-30 22:47:15, uk-UA, 2026-09-30), a 7-Zip `.zip` (NTFS times) and a tar.exe
  `.tar.gz`, both marked with `Zone.Identifier`: Explorer "Extract Here" and `pakko x` give every
  file and folder its archive time (ZIP to the millisecond), with MOTW on the files; the folder the
  single root collapses into takes the root's time; App browse of the `.tar.gz`: preview opens,
  Extract Selected of `docs` keeps `docs` and `note.txt` times.
- **Progress (2026-09-30):** ZIP files and folder entries get the entry time (NTFS > Unix > DOS,
  hostile fields fall back to DOS); tar folder entries get theirs (only implicitly needed
  directories are pre-created now); MOTW no longer resets a file's time — that lost every
  downloaded archive's times, tar included. Folder times only on folders the commit created.
  Tests first (15 ZIP + 3 tar red), 7 mutants killed, suites green. See `docs/DECISIONS.md`'s
  T-F298 entry. **CHANGELOG v1.7.0:** extracted files and folders keep the dates stored in the
  archive (ZIP, and tar-family archives downloaded from the internet), as in 7-Zip.
- **Earlier status:** open. Found in G6 pass 2, 2026-09-30. Every extracted ZIP file gets the time of
  extraction: `qmark.zip` entries dated 2026-09-30 02:07:36 came out as 02:45:56; same for
  `plain.zip` through Explorer and `pakko x`. The tar path keeps them (`valid.7z` -> `seven.txt`
  dated 2026-07-07). Not a regression: the released v1.5.0 and v1.4.12 `pakko x plain.zip` do the
  same (checked 2026-09-30). 7-Zip restores the time; for this audience the dates are part of what
  a recipient checks. `ZipArchiveService` sets `LastWriteTime`
  only when creating (`:1765`); the staging/commit path never copies the entry time. Same on
  `sec-t-f283`. Fix: set the file's last-write time from `ZipArchiveEntry.LastWriteTime` (and the
  NTFS/Unix extra fields when present, as 7-Zip does) after writing and before commit (check
  what 7-Zip reads from the NTFS/Unix extra fields before copying it); folders after their contents. Tests first: extract and compare times, incl. an encrypted entry and a
  merged commit.
- **Reported by:** G6 device campaign, 2026-09-30. Also filed earlier as T-F289 (G2 device pass, 2026-09-29: `plain.zip`/`aes.zip` `a.txt` dated 2019-03-04 came out with the current time in Explorer, the App and `pakko x`); **user decision 2026-09-29: fix after the v1.6.0 release.** T-F289's fix direction: best-effort per file like MOTW, covering the hand-rolled/decrypting paths (DOS date or the NTFS extra field); tests for plain, AES and ZipCrypto entries, then a check against `7za l -slt`. Folder dates are a separate question.

### T-F299 — `-mx=1` (Fastest) makes incompressible data 5% larger (P3)

- [x] **Status:** done 2026-09-30 (v1.7.0 wave 1). Device (Deploy 1.6.0.0 from 480cb74, App title build 2026-09-30 22:47:15, uk-UA, 2026-09-30): App compress of 32 MiB of random data at the
  default level -> 33,554,548 bytes for 33,554,432 (was 35.39 MB).
- **Progress (2026-09-30):** an entry Deflate did not shrink is written as Stored (7-Zip's rule):
  `ZipEntryCompressor` compares in memory; the temp-file compressor rewrites the chunk as Stored
  with a second read (no second progress report). `ZipArchive` cannot do this, so
  `CompressionSettings.RequiresHandRolledWriter` (password OR Fastest) now routes Fastest through
  `ParallelSingleArchiveWriter` in both SingleArchive and SeparateArchives modes. zlib level 2
  instead of 1 was measured and rejected: 1.7x slower on text at Fastest. Tests first
  (`ZipArchiveServiceIncompressibleTests`, 7 red before the fix), mutants killed (in-memory
  fallback, temp-file fallback, Fastest routing); `7za t`/`l -slt` show `Store`/`AES-256 Store`
  for random data and `Deflate` for text. **Acceptance, as measured:** Fastest and every
  hand-rolled path (password, over 64 files) store incompressible data exactly; the sequential
  `ZipArchive` path at Optimal/SmallestSize stays +0.03% (e.g. +10 KB on 32 MiB), as in v1.5.0.
  **CHANGELOG v1.7.0:** Fastest no longer makes already-compressed files ~5% larger (removes the
  v1.6.0 known issue).
- **Earlier status:** open. Found in G6 pass 2, 2026-09-30. 32 MiB of random data: `pakko a -mx=1`
  -> 35,388,889 bytes (+5.5%); `-mx=0` 33,554,544; `-mx=5` 33,564,789; `7za a -mx=1` 33,554,580.
  512 MiB at `-mx=1` gave a 566 MB archive. **Regression from T-F270 (.NET 8 -> 10):** the released
  v1.5.0 and v1.4.12 CLIs (.NET 8) give 33,564,789 at `-mx=1` on the same file; v1.6.0 would ship
  it. The App's **default** level is Fastest (`MainViewModel.cs:365`), so an App-made ZIP of
  already-compressed files (photos, video, ZIPs) grows by ~5% by default; Explorer's "Add to"
  uses Optimal (`ArchiveOptions` default) and is not affected. Priority is the user's call before G7.
  .NET 10's fastest Deflate does not fall back to stored blocks for incompressible input. Fix: when an entry's deflated size is not smaller than its
  original, write it as Stored (the parallel writer already compresses to a buffer or temp file
  before splicing, so the choice is cheap there); check the single-file path too. Tests first:
  random input at every level stays within a few bytes of the original.
- **Decision (2026-09-30, agent, delegated by the user):** stays P3, next batch, not a v1.6.0 blocker: +5.5% size on incompressible data only, no data or compatibility risk. G7 adds a known-issue line for it to the v1.6.0 CHANGELOG section.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F300 — `CliConflictPromptTests` leaves its temp folders behind (P3)

- [x] **Status:** done 2026-10-03 (bfc5d43, v1.7.0 wave 3): `TestTempFolder` retries the delete; a
  run of the class leaves 0 folders (52 old ones removed by hand).
  Report as filed: open. Found in G6 pass 2, 2026-09-30: 133 `%TEMP%\pakko-cli-conflict-*` folders
  (2026-09-25 to 2026-09-29), each still holding `two.tar`. `Dispose` catches `IOException` and gives
  up; the tar leg (`CreateResolver_AlwaysAcrossZipAndTar_PromptsOnceAndOverwritesBoth`) runs through
  the sandbox, and the file is still in use when the test ends. By the time of the check they
  deleted normally and their DACL was inherited only (no leftover AppContainer grant). Fix: retry the
  delete briefly, or reuse the shared test temp helper; a test that asserts no leftover folder after
  the class runs. Test hygiene only, not a product defect.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F301 — A remembered password that does not fit reads as "no password given" (P3)

- **App (G6 pass 3):** the same case in the App reads "Не вдалося розшифрувати елемент «a.txt»: неправильний пароль." — only Shell's wording is affected.
- [x] **Status:** done 2026-10-03 (198386e, v1.7.0 wave 3), user decision: the text, no re-prompt.
  Device (Deploy 1.6.0.2, uk-UA): `--extract-folder enc2.zip enc.zip` in the operation window, the
  prompt answered `Passw0rd` with "Застосувати до решти архівів" ticked: one prompt, `enc2\a.txt`
  extracted, then "enc.zip: Пароль, застосований до решти архівів, не підходить до цього архіву."
  and no `enc\` folder. **CHANGELOG v1.7.0:** Explorer says when the password applied to the
  remaining archives does not fit one of them, instead of calling it password-protected.
  Report as filed: open. Found in G6 pass 2, 2026-09-30, Explorer "Extract each to its own folder" on
  `enc2.zip` (Passw0rd) + `enc.zip` (secret1): the prompt for `enc2.zip` with "Apply to remaining
  archives" ticked -> `enc2\` extracted; `enc.zip` then fails with "Цей архів захищено паролем, тому
  його не можна видобути." No re-prompt is by design (`docs/DECISIONS.md`, T-F192's
  `StickyPasswordResolver` entry: a differently-keyed later archive surfaces as an error), but the
  text is the no-password message: the user did give a password, it just did not fit this archive.
  Fix: when the attempt came from a remembered password, say so ("the password applied to the
  remaining archives does not fit X") in all three frontends' wording; consider a re-prompt for that
  archive only (a decision for the user). Tests first at the message-code level.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F302 — Items moved to the Recycle Bin stay in the App's list (P3)

- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4). Earlier status: Found in G6 pass 3, 2026-09-30 (1.5.0.13). App compress `victim-p3.txt` with "Після
  успішного стиснення перемістити вихідні файли в Кошик": the file goes to the Recycle Bin but its row stays
  in the list; a second "Стиснути" first asks about the existing `victim-p3.zip`, then ends with "Шлях
  джерела не існує". Same after Extract All of a browsed archive with the box ticked: browse closes
  (`MainViewModel` "nothing left to browse") but the archive row stays in the pending list. Fix: drop the
  rows whose paths were recycled (`SourceRecycler.DeleteAsync` returns the ones not deleted). Tests first at
  the ViewModel level. The delete path is G2's (`Win32SourceDeleteOperations`): do it after the squash.
  Related, same area: the Recycle Bin tick survives a finished run and "Очистити" (it resets only on
  entering or leaving browse), so a tick given for "sources after compression" carries into a later
  list of archives, where the label becomes "archives after extraction". Decide whether a run or a clear
  should reset it (T-F207 history argues for yes).
- [~] **Progress (2026-10-03, v1.7.0 wave 4):** `SourceRecycler.DeleteAsync` returns
  `RecycleResult` (Deleted + NotDeleted, tests first, mutation-checked); the App drops the rows of
  the deleted sources (a declined or failed one keeps its row). Decided (agent): the tick resets
  after any run that cleaned up and on "Очистити". Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03): App compress of `victim.txt` with the Recycle Bin tick: the file went to the Recycle Bin, its row left the list, the tick was off afterwards, footer "Стиснуто за 1 с — архівів: 1".
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F303 — App speed readout units are English (P3)

- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4). Earlier status: Found in G6 pass 3, 2026-09-30 (1.5.0.13): under uk-UA the footer reads
  "Стиснення... (файлів: 1, 512,0 МБ) · 54,5 MB/s". `MainViewModel.cs` (the speed `switch`, ~line 1744)
  hardcodes `GB/s`/`MB/s`/`KB/s`/`B/s` while sizes go through the localized units. Use the localized size
  unit plus a per-second format from `.resw` (37 locales); check the operation window's speed text too.
- **G6 pass 4:** the App's ETA is English too ("· 140 KB/s · ~40:37 remaining" during a tar.bz2 extraction). The Explorer operation window is already localized ("155,1 МБ/с").
- [~] **Progress (2026-10-03, v1.7.0 wave 4):** App.Core `ProgressText` (speed in `DisplayText`'s
  units + per second, time left), tests first; new keys `SpeedPerSecond` (copied per locale from
  Shell's `UnitPerSecond`), `RemainingSeconds`, `RemainingMinutes` in 37 locales. Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03): App compress of a 768 MB file reads "Стиснення... (файлів: 1, 768,0 МБ)  ·  79,1 МБ/с  ·  Залишилось ~7 с".
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F304 — Two App controls have no UIA name (P3)

- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4). Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03), PowerShell UIA: the card toggle is "Новий архів", the name box "Назва:" (not its placeholder), the destination box "Зберегти в:" / "Видобути в:", the `...` button "Вибрати папку" (new key `DestinationBrowseButtonName`, 37 locales); every control in both modes has a name. Earlier status: Found in G6 pass 3, 2026-09-30 (1.5.0.13), `windows` MCP `ui_find`: the read-only
  destination path TextBox ("Зберегти в" / "Видобути в") and, in extract mode, the collapsed "Новий
  архів" card header Button both expose an empty name (the pass 2 carry-over "Button with empty UIA name"
  is the second one). Narrator reads nothing useful. Fix: `AutomationProperties.LabeledBy` to the row's
  label, and a name for the card toggle via `x:Uid` (37 locales); extend the App UIA-name test if one
  covers `MainWindow.xaml`.
- **G6 pass 5:** the card toggle (`NewArchiveCard`) has no name in create mode too, not only when collapsed; the archive-name TextBox is announced by its placeholder ("src.zip (авто)") rather than its "Назва:" label.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F306 — App: a mixed ZIP + tar extraction shows 100% for the whole tar part (P2)

- [x] **Status:** done 2026-10-01 (7da1517): `ExtractionRouter` gives each engine a slice of one climb, tar's bytes continue after ZIP's. Device (dev 1.6.0.1, App, `big.zip` 384 MB + `big.tar.bz2` 193 MB): 66% after the ZIP, then 66 -> 97% with a live speed through tar's extraction, "Видобуто за 43 с". The 29 s the bar waits at 66% are tar's listing passes (T-F307).
  Report as filed: open. Found in G6 pass 4, 2026-09-30 (1.5.0.13, App). Extract of `big.zip` (540 MB) +
  `big.tar.bz2` (200 MB) from the list: the bar reaches 100% at 3.2 s after the ZIP and stays at 100%
  with the speed frozen ("178,5 MB/s") until "Видобуто за 43 с" — about 93% of the run looks finished.
  `ExtractionRouter.ExtractAsync` passes `progress: null` to tar whenever ZIP also ran (T-F142's guard
  against a second 0->100 climb); its comment says this "keeps the pre-T-F142 percent-only
  per-archive-slice shape" — the device shows no tar reports at all. Explorer is not affected: Shell
  runs one archive at a time ("Архів 2 з 2 · big.tar.bz2", bar restarts from 0 with bytes and speed).
  Fix: one combined byte total (ZIP bytes + tar's polled bytes), or per-archive slices with the archive
  number shown, as the operation window does. T-F142 itself stays done (the mixed case was its
  documented compromise).
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F307 — tar-family extraction sits at 0% with no status through the listing passes (P3)

- [ ] **Status:** open, moved to wave 5 (2026-10-01): needs a phase in `ProgressReport` and a localized status in the App, the operation window and the Win32 dialog, so it goes with T-F268 step 6. Seen again in T-F306's device check (29 s at 66%).
  Report as filed: open. Found in G6 pass 4, 2026-09-30 (1.5.0.13). A 200 MB `.tar.bz2`: the App shows
  "Видобування... (архівів: 1)" at 0% with no speed for 24.7 s of 38 s, the Explorer window "Архів 2 з 2 ·
  big.tar.bz2" at 0% for 26 s; a 316 MB `.tar.bz2` Extract Selected: 39 s at 0% of 58 s. That time is the
  `-t`/`-tv` listing passes (each decompresses the whole archive; T-F239 kept the pass count). Browse
  of the 316 MB archive showed an empty list for about 30 s (whether a busy indicator was visible was not
  checked). Show a "Перевірка архіву..." phase (indeterminate or polled by the archive bytes read), or
  cut the passes to one `-tv`.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F308 — App: keyboard-only gaps in the lists and the footer (P2)

- [x] **Status:** done 2026-10-03 (v1.7.0 wave 4); the one focus case left is T-F314 (user, 2026-10-03). Device (Deploy 1.6.0.3-1.6.0.5, App title build 2026-10-03 06:30-06:39, uk-UA, dark, 2026-10-03), keys sent with SendKeys: create list Delete removes the focused row and focus moves to the next one; Shift+F10 opens "Прибрати зі списку"; extract-mode footer Tab goes Очистити -> Стиснути в ZIP -> Видобути (screen order); About (Enter, then Esc) gives focus back to "Про програму"; browse Enter opens a folder (focus on its first row) and a nested archive, Backspace and Alt+Up go up when the list has focus. **Left:** after Enter drills into a nested archive, focus lands on the Up button, so the next Backspace does nothing until the list is focused again (three fixes tried: a deferred focus, bounded retries, a live-container check — none held; stopped per the three-attempts rule). Item 5 (faint check mark) not changed. Earlier status: Found in G6 pass 5, 2026-09-30 (1.5.0.13, E6, keyboard only):
  1. Archive browser: Enter on a folder (`src` of `plain.zip`) only toggles its selection ("Вибрано 1 з 1")
     and does not open it; Backspace and Alt+Up leave the list as it is (the Up button works). Files and
     nested archives were not pressed, but only `DoubleTapped` is wired (no `KeyDown`/`ItemClick` on
     `ArchiveBrowserListView`), so they behave the same. A keyboard user cannot get below the archive root.
  2. Create list: Delete does not remove the focused row, and Shift+F10 / the menu key open nothing — the
     "Прибрати зі списку" `ContextFlyout` sits on the item template's inner `Grid`, which a keyboard context
     request on the `ListViewItem` never reaches. Only "Очистити" (everything) is left.
  3. Extract mode footer: Tab goes Очистити -> Видобути -> Стиснути в ZIP while the screen shows
     Очистити, Стиснути в ZIP, Видобути — document order, while `ExtractButtonColumn`/`ArchiveButtonColumn`
     swap the columns. Found by pressing Enter on what should have been Видобути: it compressed `enc.zip`.
  4. After About closes (Esc works), focus lands on "Додати файли", not back on "Про програму".
  5. Seen, not a bug of ours so far: the multi-select check of a selected browse row is faint in both themes
     (no accent fill); check against the WinUI default `ListViewItem` style before changing anything.
  Fix tests-first where a ViewModel command can carry it (open-entry / remove-entry commands bound to keys);
  XAML: `KeyboardAccelerator`s or a `KeyDown` handler, `ContextFlyout` on the `ListViewItem` style, `TabIndex`
  bound with the column.
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F309 — A cancelled or refused extraction leaves the empty destination folder Pakko created (P3)

- [x] **Status:** done 2026-10-01 (338f9dd): tar extraction follows ZIP's T-F230 rule. Device (dev 1.6.0.1): `--extract-folder` of a tar the pre-scan refuses shows the refusal and leaves no `evil\` folder. The filed case too: `--extract-folder` of `big.tar.bz2` next to an existing `big\`, cancelled in the operation window during the listing phase: `big (1)\` was there 5 s in and gone after the cancel, `big\` stayed.
  Report as filed: open. Found in G6 pass 5, 2026-09-30 (1.5.0.13). Explorer "Extract to big\" (`--extract-folder`)
  of a 200 MB `big.tar.bz2` with `big\` already present, cancelled during the listing phase (Esc or
  "Скасувати"): no files, quarantine empty, but an empty `big (1)\` stays (it exists 3 s into the run). Same
  shape seen in G6 pass 4: `pakko x -o<new dir>` of an archive the pre-scan refuses leaves the empty `-o` folder.
  T-F245's "no partial output" holds for files; the folder is created before the engine runs. Fix: create the
  folder lazily, or remove it on cancel/refusal when Pakko created it and it is still empty (never a folder
  that existed before).
- **Reported by:** G6 device campaign, 2026-09-30.

### T-F311 — `Archiver.App.Core.Tests` fail once in a while under a full-suite run (P3)

- [ ] **Status:** open. Seen 2026-09-30 in wave 1, in different full `dotnet test` runs:
  `SourceRecyclerTests.Win32_MoveToRecycleBin_FileLandsInRecycleBinNotDeleted` and
  `FileItemTests.TryCreate_ExistingFile_ReturnsItemWithSizeAndCrc`, each once; both pass alone and
  in three reruns of the project. Code the wave did not touch. Find the shared resource or timing
  (the real Recycle Bin; an async CRC load) and make the tests deterministic, as T-F162 did for
  `Progress<T>`.
- **Reported by:** v1.7.0 wave 1 (agent), 2026-09-30.

### T-F312 — A leftover or locked `<name>.zip.tmp` makes the next archive creation fail (P2)

- [ ] **Status:** open. Found in the v1.7.0 wave 3 device check, 2026-10-03: a `pakko a big3.zip
  big.bin` killed mid-run (`taskkill /F`) left `big3.zip.tmp` next to the destination; the next
  `pakko a big3.zip big.bin` failed at once with "Cannot create archive: The file '...big3.zip.tmp'
  already exists." `ZipArchiveService` uses one fixed temp name, `destPath + ".tmp"` (`:191`, and
  `:509` for separate archives). The user points out the same file can stay or be held without a
  kill: a sync client (Google Drive) or a backup program may pick up the `.tmp` while it is being
  written, keep it open (locked), or bring it back after Pakko removed it. Then the next run fails,
  and the final rename onto the archive can fail with a sharing violation, like T-F141's chunk files.
  Fix direction: a temp name unique to the run (as the parallel writer's chunk files already are),
  hidden like T-F35's chunk folder; remove an old `.tmp` of the same archive only when nothing holds
  it; retry the final rename briefly on a sharing violation. Check tar creation's temp naming the
  same way. Tests first: a stale `.tmp` and an open handle on it, each followed by a successful run.
- **Reported by:** v1.7.0 wave 3 device check (agent) and the user, 2026-10-03.

### T-F313 — `pakko x` onto partly existing files exits 1 with no line and no hint (P3)

- [ ] **Status:** open. Found in the v1.7.0 wave 3 closing review, 2026-10-03. ZIP keeps its
  per-file conflict skips out of `SkippedFiles` (`ZipArchiveService`, the `conflictSkipped` list: they
  only make the archive partial, so the App's summary stays as it was). When some entries of an
  archive already exist and others are extracted, `pakko x` without `-ao`/`-y` exits 1 and prints
  nothing — neither the kept files nor the `-aoa` hint. Not a regression (the pre-T-F296 hints were
  silent too); same class as T-F293. Tar lists `FileExistsAtDestination` per file and is fine. Fix
  direction: expose the count (or the list) of conflict skips on the result so the CLI can print the
  hint, without changing the App's summary. Tests first in `Archiver.CLI.Tests`.
- **Reported by:** v1.7.0 wave 3 closing review (agent), 2026-10-03.

### T-F314 — App: focus lands on the Up button after Enter opens a nested archive (P3)

- [ ] **Status:** open. Split out of T-F308 (user, 2026-10-03). In the Archive Browser, Enter on a
  nested archive opens it, but keyboard focus ends on the browse Up button instead of the first row,
  so the next Backspace/Alt+Up does nothing until the list is focused again (Enter on a folder keeps
  focus on a row). Three fixes in `MainWindow.xaml.cs` did not hold on the device (a deferred `Focus`
  at Low priority, bounded retries, a live-container check) — see `docs/DECISIONS.md`'s T-F308 entry.
  Next idea, not tried: handle Backspace/Alt+Up for the whole browse area (the Up button included),
  or find what moves focus to the Up button while the nested level loads (the list is disabled while
  busy).
- **Reported by:** v1.7.0 wave 4 device pass, 2026-10-03.

### T-F310 — A compressed GNU-format tar with OEM (cp866) names is still refused (P2)

- [x] **Status:** done 2026-10-01 (29895a2), user decision: gzip only, in-process. Device (dev 1.6.0.1): `--extract-here` of a GNU-magic cp866 `.tar.gz` wrote `Док.txt`. bzip2/xz/zstd keep the refusal by design. See `SECURITY.md` and `docs/DECISIONS.md`'s wave 2 entry.
  Report as filed: open. Found fixing T-F305, 2026-09-30. `C:\g6\p4\cp866.tar` gzipped (`cp866.tar.gz`):
  `pakko l` refuses it with the "cannot represent on this system (code page 1251)" message. T-F305's
  header check reads only an uncompressed tar, so for `.tar.gz`/`.bz2`/`.xz`/`.zst` libarchive's wording
  still decides and cannot tell invalid UTF-8 under GNU magic from a valid name the code page cannot show;
  refusing is the fail-closed answer (the OEM reading could extract mojibake). v1.4.12 read it as OEM.
  Options: decompress gzip in-process for the header check only (`GZipStream`; bz2/xz/zst have no BCL
  decoder), or a second sandboxed tar.exe run that exposes the raw name bytes. Needs a decision on
  in-process decompression of untrusted input before any code.
- **Reported by:** T-F305 fix, 2026-09-30.

## Test-Coverage Audit Follow-Ups (T-F174–T-F186)

Sourced from a full three-stage QA/AppSec coverage audit (requirements extraction -> matrix vs.
existing tests across Happy/Error/Misuse/Security/Boundary vectors -> gap analysis), 2026-08-30.
Two gaps the audit surfaced were already tracked (T-F160 CLI conflict-dialog parity, T-F171
Tar-family duplicate-entry-name parity) — not duplicated here, only cross-referenced. Priority
tiers below (P0/P1/P2) reflect the audit's own ranking for a standalone security-sensitive desktop
app, not strict execution order — `docs/DECISIONS.md` gets an entry once each task's real findings
land, per this project's normal workflow.

## ZIP Password Support (T-F188–T-F194)

Sourced from a dedicated Plan Mode + `advisor` design session, 2026-09-17 (user-directed:
"Подготуйся до впровадження читання зіп з паролями" — prepare password-protected ZIP reading,
covering Explorer/WinUI App/Archive Browser/CLI password-prompt UX and a test-first strategy,
before any code). **This reverses `SPEC.md`/`SECURITY.md`'s "Encrypted archives — Out of scope"
line** — explicitly confirmed with the user as a deliberate scope change, not an oversight. Those
documents (`SPEC.md`, `SECURITY.md`, `docs/CLI.md`, `README.md`, `docs/index.html`+`uk/`) are
updated when each task below actually ships, not in advance — this project's own established
timing convention (e.g. T-F105 only flipped `SPEC.md`'s TAR row once TAR-creation actually worked).

**Confirmed scope (user-directed):**
- **ZIP only.** `tar.exe` (bsdtar 3.8.8/libarchive 3.8.8, `C:\Windows\System32\tar.exe`) has zero
  passphrase support — confirmed empirically via `tar --help` (no password/passphrase mention at
  all) this same session. RAR/7z stay diagnostics-only, unchanged (T-F113).
- **Reading only, this phase.** Decrypts both legacy ZipCrypto (PKWARE traditional) and WinZip
  AE-1/AE-2 (AES-128/256), for maximum compatibility with real-world files. Writing (creating
  password-protected archives) is a separate future phase — AES-only, never ZipCrypto — tracked as
  T-F193 below, not implemented alongside T-F188–T-F192.
- **One shared resolver for both directions from day one** (T-F157→T-F158 precedent — that pair
  had to retroactively unify a decision that was first built extraction-only; this is deliberately
  avoided here): `PasswordResolver` and the `ResolvePasswordAsync` callback shape on
  `ExtractOptions`/`ArchiveOptions` are designed for both `Purpose: Decrypt|Encrypt` from T-F189
  onward, even though only `Decrypt` has a real caller until T-F193.

Full design rationale — the AE-1/AE-2 CRC-zeroing pitfall, the no-fork-extraction-pipeline
invariant, the raw-entry-locator-vs-reflection choice, and the empirical `tar.exe`/`7za.exe`
findings — gets its own `docs/DECISIONS.md` entry once T-F188 actually lands; the full design plan
(reviewed twice by `advisor`) is preserved in this session's plan file until then.

### T-F192 — `Archiver.Shell`: native password prompt for Explorer extract commands

- [~] **Status:** implementation complete, 2026-09-18 — stays `[~]` until the user's own on-device
  Explorer click-through, per this project's UI-graduation convention (same as T-F190).
- **Context:** `ShellConflictDialog.cs`'s `TaskDialogIndirect` has no text-input capability, so it
  cannot be reused as-is for a masked password field.
- **Design decision (resolved via real research, per this project's hard constraint):** fetched
  NanaZip's actual `PasswordDialog.rc`/`.cpp` — confirmed 7-Zip/NanaZip use a **custom `DIALOGEX`**
  (`EDITTEXT` with `ES_PASSWORD | ES_AUTOHSCROLL` + a "Show password" checkbox toggling
  `EM_SETPASSWORDCHAR`), never `CredUIPromptForCredentialsW`. Implemented as an in-memory
  `DLGTEMPLATEEX` byte buffer + `DialogBoxIndirectParamW` (no `.rc`/resource-compile step needed —
  `Archiver.Shell` is a plain C# project) rather than a compiled resource. Full rationale, the
  NanaZip source excerpts, and a Phase 0 spike's findings (a `SetForegroundWindow`-alone trap and
  its `HWND_TOPMOST` fix) are in `docs/DECISIONS.md`'s T-F192 entry.
- **Acceptance criteria:**
  - [x] Decision recorded in `docs/DECISIONS.md` with the NanaZip research findings.
  - [x] New `StickyPasswordResolver` (mirroring `StickyApplyToAllConflictResolver`) so "apply to
    remaining" spans a whole Explorer multi-select, not just one archive.
  - [x] Localized across all 37 locales (6 keys reused from `Archiver.App`'s T-F190 strings + 1
    new `PasswordDialogShowPasswordCheck`, translated fresh).
  - [x] `Deploy.ps1` build+sign+install + agent-driven on-device verification (`windows` MCP)
    against the real installed MSIX via all 3 extract commands (`--extract-here`, `--extract-
    folder`, `--extract-flat`) against real encrypted fixtures, under real Ukrainian OS UI culture,
    including a genuine occlusion test (dialog rendering on top of a restored, foreground terminal
    window, not just one that happened to be minimized) — confirmed masked entry,
    wrong-password retry, correct-password extraction, multi-archive "apply to remaining" with
    zero re-prompt (and correct coexistence with T-F155's own conflict dialog), and Cancel
    producing the exact unchanged pre-existing rejection message. Stays pending the user's own
    personal Explorer click-through.
  - [x] **Once this ships, update the trust documents in one pass** (done 2026-09-24, user-approved,
    together with T-F194's SECURITY.md paragraph — new "Password-Protected ZIP" section) (user-confirmed 2026-09-18,
    after T-F190's on-device pass raised the question): `SECURITY.md` (needs explicit permission
    per `CLAUDE.md`'s hard Do-Not), `docs/SPEC.md`, `README.md`, `docs/index.html` +
    `docs/uk/index.html` all still say "Encrypted archives — out of scope," stale since before
    T-F188. **This checkbox is now unblocked** — T-F192 was the last of the three gating tasks
    (App/T-F190, CLI/T-F191, Shell/T-F192), all three now shipped with verified password UI. Still
    not started automatically — raise it explicitly with the user rather than starting it as part
    of closing T-F192 out, since it touches `SECURITY.md`.
- **Reported by:** user request, 2026-09-17 (design session).
- **Depends on:** T-F189 (done).

---

### T-F194 — AMSI scan (T-F146) currently can't see inside a password-protected ZIP entry at all

- [~] **Status:** implementation complete, 2026-09-24 — agent-driven on-device verification via
  `windows` MCP passed (Shell `--scan` — the Explorer command's target — and the App's Archive
  Browser scan, both against real Defender and the real encrypted EICAR fixture, plus Shell `--test`); stays
  `[~]` until the user's own click-through (`SECURITY.md` updated 2026-09-24 with permission). Full design + four advisor-caught defects in `docs/DECISIONS.md`'s T-F194 entry.
  Same batch closed T-F192's Shell `--test` password gap.
- **Context:** `AntivirusScanService.ScanZipArchiveAsync` opens each entry via plain
  `ZipArchiveEntry.Open()`/`DeflateStream` — for an encrypted entry this throws
  `InvalidDataException` (encrypted bytes aren't valid deflate), already caught by the existing
  `catch (... InvalidDataException)` and reported as `ThreatVerdict.Inconclusive` ("Could not
  read entry"). This fails safe (never silently reports "clean"), but the practical effect today
  is that **a password-protected ZIP entry is never actually scanned** — a well-known real-world
  malware-delivery technique is specifically to password-protect the payload to evade automated
  AV scanning, which is exactly this project's own threat model concern for its government/
  defense audience (see `SECURITY.md`).
- **Acceptance criteria (draft):**
  - [x] `AntivirusScanService.ScanZipArchiveAsync` gains the same
    `Func<PasswordPromptInfo, Task<PasswordDecision>>? ResolvePasswordAsync` hook T-F189 adds to
    `ExtractOptions`/`ArchiveOptions` (third call site for the same shared `PasswordResolver` —
    reinforces, doesn't reopen, the "one shared mechanism" decision from T-F188/T-F189).
  - [x] When an entry's general-purpose encrypted bit is set and a password is available, the
    entry is decrypted via `EncryptedZipEntryReader` before being handed to AMSI, instead of
    going straight to the existing `Inconclusive` fallback.
  - [x] Without a resolved password (Shell/CLI scan commands not yet wired, or the user declines
    the prompt), behavior is unchanged — `Inconclusive`, not a silent "clean" — this is a strict
    improvement, never a regression on the fail-safe default.
  - [x] New Explorer/Archive Browser entry points for "Scan for threats" wired to prompt for a
    password the same way Extract does.
  - [x] `SECURITY.md`'s "Encrypted-Archive Diagnostics"/AMSI sections updated to state this
    explicitly (both the gap that existed before this task and the fix), not left implicit.
  - [x] New tests: an EICAR-in-encrypted-ZIP fixture, scanned with and without the correct
    password, asserting `Inconclusive` (no password) vs. a real detection (correct password) —
    mirroring T-F146's own existing manual EICAR verification requirement.
- **Reported by:** user, 2026-09-17 ("І в нас же є тест архів на віруси, треба не забути цю
  функціональність") — flagged mid-review of T-F188, before any AV-related code was touched.
- **Depends on:** T-F188 (done), T-F189.

---
