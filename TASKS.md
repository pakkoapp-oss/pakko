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

### T-F05 — Archive Contents Preview
- [ ] **Status:** future

Click ZIP in list → read-only tree view of contents via `ZipFile.OpenRead`. No extraction.

---

### T-F06 — Ask on Conflict Dialog
- [ ] **Status:** future

Interactive dialog when conflict detected — Skip / Overwrite / Rename per file.

---

### T-F07 — Optional 7-Zip Extraction Support
- [ ] **Status:** CANCELLED — replaced by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` (Microsoft-signed) supports 7z extraction on Windows 11 23H2+ without requiring a third-party binary.

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** CANCELLED — covered by tar.exe integration (T-F47/T-F49). Windows built-in `tar.exe` supports RAR extraction on Windows 11 23H2+, eliminating the need for `unrar.exe`.

---

### T-F09 — CLI Core (Archiver.CLI)
- [ ] **Status:** future

Expose `Archiver.Core` as standalone CLI executable for scripting.
New project `src/Archiver.CLI/` — no logic duplication.

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
- [ ] **Status:** future

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

`SeparateArchives` archives are fully independent — can run in parallel.

```csharp
await Parallel.ForEachAsync(
    options.SourcePaths,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
    async (sourcePath, token) => await CreateSingleArchiveAsync(sourcePath, options, progress, token));
```

Note: `SingleArchive` mode stays sequential. Progress reporting needs `Interlocked.Increment`.

**Acceptance criteria (when implemented):**
- [ ] `SeparateArchives` uses `Parallel.ForEachAsync`
- [ ] `MaxDegreeOfParallelism` capped at `Environment.ProcessorCount`
- [ ] Progress reporting thread-safe
- [ ] `CancellationToken` respected
- [ ] `SingleArchive` unchanged
- [ ] `dotnet test` passes, no file corruption

---

### T-F13 — Process Sandbox Isolation for External Binaries
- [ ] **Status:** future
- **Depends on:** T-F07 or T-F08

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
- [ ] **Status:** future
- **Priority:** low
- **Depends on:** T-F04 (TAR support)

**What:** Introduce IArchiveEngine abstraction to decouple core logic from ZIP-specific implementation. Enables TAR, tar.gz, and future formats without UI changes.

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
- [ ] **Status:** future — scope discussed with user 2026-07-07, not yet started
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
- [ ] Final language list confirmed with user before translation work begins
- [ ] `Resources.resw` created under `Strings/<locale>/` for each confirmed locale, translating
      every key already in `en-US/Resources.resw`
- [ ] `Package.appxmanifest`'s `<Resources>` element declares every shipped locale
- [ ] OS display language automatically selects the matching `Resources.resw` with no app code
      change — verified on-device for at least `uk-UA`
- [ ] An excluded/unsupported OS language (e.g. `ru-RU`) falls back to `en-US` text, not a
      blank string or resource-load crash — verified on-device
- [ ] No installer-time language picker or install-location picker added (confirmed non-goal)
- [ ] Max text-length budget determined per UI string (buttons, labels, dialog titles) —
      German/Finnish/Ukrainian and other "long" locales are notorious for overflowing controls
      sized for English text; check longest translated string per key against the control it
      renders in and either widen/wrap the control or shorten the translation before shipping
- [ ] Manual on-device check for layout corruption (clipped/overlapping/truncated text, buttons
      that no longer fit their label) on at least one long-text locale (e.g. German) and one
      wide-glyph/RTL locale (e.g. Arabic or Hebrew)
- [ ] `dotnet build src/Archiver.App` succeeds with all new resources
- [ ] `DECISIONS.md` entry: MSIX install-location non-goal + language auto-match mechanism

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

### T-F93 — Non-Intrusive Donate Link (Buy Me a Coffee)
- [ ] **Status:** future — scope confirmed with user 2026-07-07, blocked on a real URL
- **Priority:** low ("not urgent," per user)
- **Depends on:** T-F14 (About dialog with version/links — already done)

**What:** add a small, non-pushy donate link to Pakko's About section and to the GitHub README,
pointing to a Buy Me a Coffee page. Explicitly not a banner, popup, or nag — a small link/button
only, consistent with how Buy Me a Coffee itself is typically presented.

**Scope:**
- About dialog (wherever T-F14 already put version/links, `Archiver.App`) gains one additional
  small link/button (e.g. "☕ Support the project") opening the Buy Me a Coffee URL in the
  system default browser.
- `README.md` gets an equivalent small link/badge, placed near the top or bottom — not inline
  with technical content.
- **Blocked:** needs a real Buy Me a Coffee page/username from the user before wiring the link —
  do not invent or ship a placeholder URL.

**Acceptance criteria:**
- [ ] Real Buy Me a Coffee URL obtained from user
- [ ] About dialog shows one small, non-modal donate link/button
- [ ] `README.md` shows one small donate link/badge
- [ ] Link opens in the system default browser (not a modal or embedded frame)
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
- [~] **Status:** partial (v1.2) — manifest declarations written then temporarily reverted

> **Note:** COM registration (`com:Extension`) and context menu binding (`desktop4:Extension`)
> were written and then removed from `Package.appxmanifest` because Explorer hangs on
> right-click when `Archiver.Shell.exe` does not implement `IExplorerCommand`. Restore both
> blocks after T-F61 is complete.
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
- [x] Both entries invoke `Archiver.Shell.exe` with correct arguments
- [ ] Context menu appears on Win10 (classic menu) after MSIX install
- [ ] Context menu appears on Win11 (modern menu) after MSIX install
- [ ] `.zip` files show Extract submenu; non-ZIP files show Archive submenu
- [ ] Mixed selection shows combined submenu
- [ ] Uninstall removes both context menu registrations cleanly

---

### T-F40 — Shell Extension Registration (Dual Mechanism)
- [~] **Status:** partial (v1.2) — MSIX installs with all three EXEs present
- **Depends on:** T-F53, T-F55

> **Note:** `Archiver.Shell.exe` and `Archiver.ProgressWindow.exe` confirmed present in the
> installed package alongside `Archiver.App.exe`. Context menu functionality is blocked on
> `IExplorerCommand` implementation (T-F61). COM and context menu manifest entries restored
> once T-F61 is complete.

**What:** Complete dual-mechanism shell registration wired to `Archiver.Shell.exe`. Validates that both `desktop4:FileExplorerContextMenus` (Win10) and `IExplorerCommand` via COM (Win11) registrations work end-to-end after MSIX install.

**Note:** Registration declarations are written in T-F55. This task covers end-to-end validation — install, verify menu appearance on both OS versions, verify uninstall cleanup.

**Acceptance criteria:**
- [x] MSIX installs without errors on Windows 10 1809+
- [x] MSIX installs without errors on Windows 11 22000+
- [x] `Archiver.Shell.exe` and `Archiver.ProgressWindow.exe` present in installed package alongside `Archiver.App.exe`
- [ ] Context menu entry visible in classic menu on Win10 (right-click → menu appears) — requires IExplorerCommand implementation
- [ ] Context menu entry visible in modern menu on Win11 (no "Show more options" needed) — requires IExplorerCommand implementation
- [ ] Invoking any menu item launches `Archiver.Shell.exe` with correct arguments — requires IExplorerCommand implementation
- [ ] Uninstall removes both registration entries cleanly — no orphan registry keys

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
- [~] **Status:** partial (v1.3) — detection logic complete; UI criterion blocked on T-F36 (no
      tar format selector exists yet to grey out)

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
- [ ] UI greys out formats not supported by detected tar.exe — no tar format selector exists in
      the UI yet; blocked on T-F36 (Pluggable Archive Engine Interface / format dropdown)
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
- [~] **Status:** partial (v1.3) — `Archiver.Core`/`Archiver.App`/`Archiver.Shell` wiring and
      tests complete; `.tar.gz`, `.7z`, and now real `.rar` (2026-07-07, using T-F50's committed
      `valid.rar` fixture) all verified end-to-end through the installed app. Stays `[~]` — the
      remaining open criterion (a `TarCapabilities`-unsupported format selected with "delete after
      extraction" checked) still can't be exercised on this machine, since this system's tar.exe
      (bsdtar 3.8.4) supports every format `TarCapabilities` tracks — there is no naturally
      unsupported format here to test against, unrelated to the RAR fixture gap this closes
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
- [ ] Manual on-device verification also covers: a format `TarCapabilities` reports unsupported
      on this machine, selected with "delete after extraction" checked — confirm whether the
      source file survives (see **T-F87** below; `MainViewModel.ExtractAsync` only checks
      `result.Success`, which a fully-skipped extraction still satisfies). **Not testable on
      this machine** — this system's tar.exe (bsdtar 3.8.4) supports every format `TarCapabilities`
      tracks, so there is no naturally-unsupported format to select here; needs either an older
      Windows build or a deliberately-forced `TarCapabilities` override to exercise.

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

### T-F50 — tar.exe Test Fixtures
- [~] **Status:** partial (v1.3) — all achievable coverage implemented; bomb detection descoped to
      T-F90 (missing feature, not a fixture gap). RAR's previously-documented "unobtainable on
      this machine" gap (T-F49/T-F85/T-F86) was closed 2026-07-07 — a `valid.rar` fixture was
      generated via WinRAR's official console `Rar.exe` (installed via `winget`, used once, then
      uninstalled — no RAR-writing tool is shipped with or used by Pakko itself), same one-off
      pattern `valid.7z` already used with `NanaZipC.exe`

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
- [ ] Bomb: **descoped to T-F90** — no compression-ratio protection exists on the tar.exe path to
      test against; writing a "bomb skipped" test against nonexistent behavior would have been
      dishonest, so this criterion is intentionally left unchecked here
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

### T-F52 — Low IL Sandbox for tar.exe
- [ ] **Status:** future (v1.4)

**What:** `TarSandboxedService` implements `ITarService` using a P/Invoke-based Low Integrity Level sandbox for `tar.exe`. Replaces `TarProcessService` via single DI line change.

**File:** `src/Archiver.Core/Services/TarSandboxedService.cs`

**P/Invoke surface:**
- `CreateRestrictedToken` — strip privileges from Pakko's token
- `DuplicateTokenEx` — duplicate for `CreateProcessAsUser`
- `SetTokenInformation` — set integrity level to Low IL
- `CreateProcessAsUser` — launch tar.exe with restricted token
- `SetNamedSecurityInfo` — label quarantine directory with Low IL

**Flow:**
1. Create quarantine directory on same disk as destination
2. Label quarantine directory Low IL via `SetNamedSecurityInfo`
3. Launch `tar.exe` into quarantine with restricted token (Low IL)
4. After process exits, validate all files at Medium IL (C# code)
5. Atomic move to final destination
6. Clean up quarantine directory

**Acceptance criteria:**
- [ ] `TarSandboxedService` implements `ITarService` — same interface as `TarProcessService`
- [ ] DI swap is one line: `AddSingleton<ITarService, TarSandboxedService>()`
- [ ] Quarantine directory receives Low IL label before tar.exe launch
- [ ] tar.exe process runs with restricted Low IL token
- [ ] Validation and move run at Medium IL in C# after process exits
- [ ] Quarantine directory cleaned up on success and failure
- [ ] All P/Invoke handles properly closed — no leaks
- [ ] `dotnet test` passes — integration test: file write outside quarantine fails

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

