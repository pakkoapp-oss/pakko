# TASKS_DONE.md — Completed Tasks Archive

> ⚠ **DO NOT RE-IMPLEMENT anything in this file.**
> All tasks here are complete and committed. Re-implementing will break existing behavior.
>
> For active and future tasks see [`TASKS.md`](TASKS.md).

---

## ⚠ Agent Rules — Read Before Every Task

These rules apply to ALL tasks. Violating them = task is NOT complete.

**Completion rules:**
- NEVER mark `[x]` unless every single acceptance criterion is checked `[x]`
- `[~]` means partially complete — UI done but logic missing, or logic done but untested
- A task with ANY `[ ]` criterion must stay `[ ]` or `[~]` — never `[x]`

**Testing rules:**
- ALWAYS run `dotnet test` after any change to `Archiver.Core`
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

## Phase 1 — Project Setup

### T-01 — Create Solution and Projects
- [x] **Status:** complete

### T-02 — Create Folder Structure
- [x] **Status:** complete

---

## Phase 2 — Core Models

### T-03 — Implement ArchiveOptions
- [x] **Status:** complete

### T-04 — Implement ExtractOptions
- [x] **Status:** complete

### T-05 — Implement ArchiveResult and ArchiveError
- [x] **Status:** complete

---

## Phase 3 — Core Interface and Service

### T-06 — Implement IArchiveService
- [x] **Status:** complete

### T-07 — Implement ZipArchiveService
- [x] **Status:** complete

---

## Phase 3b — Tests

### T-12 — Implement Test Project
- [x] **Status:** complete

---

## Phase 4 — UI Layer

### T-08 — Implement MainViewModel
- [x] **Status:** complete

### T-09 — Implement MainWindow
- [x] **Status:** complete

---

## Phase 5 — Error Handling UI

### T-10 — DialogService for Error Display
- [x] **Status:** complete

---

## Phase 5b — UX Improvements

### T-13 — ZIP Detection (extension-based)
- [x] **Status:** complete — superseded by T-13.1

### T-13.1 — Upgrade ZIP Detection to Magic Bytes
- [x] **Status:** complete

**File:** `src/Archiver.Core/Services/ZipArchiveService.cs`

`IsZipFile()` uses magic bytes `50 4B 03 04`. Extension check removed entirely.

### T-13.2 — Inform User About Skipped Non-ZIP Files
- [x] **Status:** complete

`ArchiveResult.SkippedFiles` added. Known formats detected by magic bytes:
RAR `52 61 72 21`, 7-Zip `37 7A BC AF 27 1C`, GZip `1F 8B`, BZip2 `42 5A 68`, XZ `FD 37 7A 58 5A 00`, LZ4 `04 22 4D 18`.

### T-14 — Smart Extract Folder Logic
- [x] **Status:** complete

Single root folder → no double-nesting. Multiple root items → subfolder. Single root file → direct. ZIP slip protection on every entry.

### T-15 — Add Files and Add Folder Buttons
- [x] **Status:** complete

### T-16 — Destination Path Row
- [x] **Status:** complete

Default = folder of first item. Falls back to Desktop.

### T-17 — Remove Item from List (Right-click)
- [x] **Status:** complete

### T-18 — Post-Action Options — UI and Service Logic
- [x] **Status:** complete

`OpenDestinationFolder`, `DeleteSourceFiles`, `DeleteArchiveAfterExtraction` — all wired to service.

### T-19 — Operation Summary Dialog
- [x] **Status:** complete

`ShowOperationSummaryAsync` in `IDialogService`. Shows Errors / Skipped / Warnings sections conditionally.

### T-20 — Archive Name Field
- [x] **Status:** complete

### T-21 — File List Table with Columns
- [x] **Status:** complete

`FileItem` model with Name, Type, Size, SizeBytes, Modified, FullPath. Sorting by any column.

### T-22 — Archive Mode Toggle (Single / Separate)
- [x] **Status:** complete

### T-23 — Conflict Behavior Dropdown
- [x] **Status:** complete

`ConflictBehavior`: Overwrite, Skip, Rename. `Ask` cut from v1.0 scope — default is `Skip`.
(Reintroduced later as a real interactive per-conflict dialog — see T-F06 in `TASKS.md`.)

### T-24 — Compression Level Selector
- [x] **Status:** complete

`CompressionLevel` in `ArchiveOptions`. Fast / Normal / Best / None.

### T-25 — Detect and Report Password-Protected ZIP Archives
- [x] **Status:** complete

Reads bit 0 of general purpose bit flag. Returns `ArchiveError` with message "This archive is password-protected and cannot be extracted."

### T-26 — Windows Compatibility Target
- [x] **Status:** complete

`TargetFramework` → `net8.0-windows10.0.17763.0`. Supports Windows 10 1809+ and Windows Server 2019+.

### T-27 — Replace ZipFile.CreateFromDirectory with Lazy Enumeration
- [x] **Status:** complete

`Directory.EnumerateFiles` via `AddDirectoryToArchive` — lazy, no upfront collection.

### T-28 — Internationalization Foundation (ResW)
- [x] **Status:** complete

`Strings/en-US/Resources.resw`. All UI strings via `x:Uid`. Dynamic strings via `ResourceLoader`. `Archiver.Core` has zero UI references.

### T-29 — Drag & Drop on File List Area
- [x] **Status:** complete

`AllowDrop="True"` on `ListView`. Empty-state hint overlay with `IsHitTestVisible="False"`. Drop works anywhere in list area.

### T-30 — App Title and Simple File Log
- [x] **Status:** complete

`AppWindow.Title = "Pakko"`. Log at `%LocalAppData%\Pakko\logs\pakko.log`. Rotation at 1 MB, max 3 rotated files. `ILogService` / `LogService` in DI.

### T-31 — App Icon and System Tray
- [x] **Status:** complete

`H.NotifyIcon.WinUI 2.1.0`. `TaskbarIcon` with `ContextFlyout`. "Open Pakko" / "Exit". `AppWindow.SetIcon()` with generated ICO. `TrayIcon.Dispose()` on close.

### T-32 — File List Minimum Height and Layout Fix
- [x] **Status:** complete

File list row `Height="*"`. Window size `800×700`.

### T-33 — Real-Time Progress During Archive and Extract
- [x] **Status:** complete

Both `ArchiveAsync` (SingleArchive) and `ExtractAsync` wrapped in `Task.Run`. Per-file `progress?.Report()`. `IsIndeterminate` for single large file/archive >10 MB. `IsOperationRunning` disables all buttons.

### T-34 — SHA-256 Integrity Manifest in ZIP Comment
- [x] **Status:** complete → Removed in post-v1.0 — redundant with ZIP built-in CRC-32

`PAKKO-INTEGRITY-V1` header in ZIP comment. SHA-256 per entry after archive creation. Verification on extract — mismatch → `ArchiveResult.Warnings`, not errors. `ArchiveResult.Warnings` field added.

### T-35 — Test Fixtures — Real Archive Files
- [x] **Status:** complete

`FixtureHelper` with `Archive()`, `ArchiveOptional()`, `PlainFile()` — `Assert.Inconclusive` for missing fixtures. 18 new fixture-based tests. 45/45 tests pass total.

---

## Phase 6 — Packaging

### T-11 — MSIX Packaging Setup
- [x] **Status:** complete

`win-x64-msix.pubxml` publish profile. `GenerateAppxPackageOnBuild=false` by default. `AppxPackageSigningEnabled=false`. Output: `AppPackages/Archiver.App_1.0.0.0_x64_Test/`. Requires self-signed cert or Developer Mode for local install.

### T-36 — Pakko Branding Icons
- [x] **Status:** complete

Steel blue `#1D5FA8`, slab bold П lettermark. All `Assets/` placeholders replaced: `Square44x44Logo.scale-200.png`, `Square44x44Logo.targetsize-24_altform-unplated.png`, `Square150x150Logo.scale-200.png`, `Wide310x150Logo.scale-200.png`, `StoreLogo.png`, `Square44x44Logo.ico`, `SplashScreen.scale-200.png`. Source `pakko_*.png` / `pakko.ico` files removed after copy. Build: 0 warnings, 0 errors.

---

## v1.1 Sprint — Completed March 2026

### T-F17 — Tray Left-Click Toggle
- [x] **Status:** complete — Completed in v1.1 sprint, March 2026

Left-click on tray icon toggles window visibility. `TrayLeftClickCommand` in `MainWindow.xaml.cs`. `LeftClickCommand="{x:Bind TrayLeftClickCommand}"` wired in XAML. Works in Release and Debug.

### T-F18 — Operation Spinner on Action Buttons
- [x] **Status:** complete — Completed in v1.1 sprint, March 2026

Indeterminate `ProgressRing` inline on Archive/Extract buttons while `IsOperationRunning = true`. No layout shift. Buttons remain disabled during operation. Button text changes to "Archiving..." / "Extracting..." during operations.

### T-F26 — Temporary File Pattern for Safe Archive Creation
- [x] **Status:** complete — Completed in v1.1 sprint, March 2026

Archive written to `destPath + ".tmp"` during creation. On success: renamed to final path. On failure or cancellation: `.tmp` deleted — no partial archive left on disk. New test: `ArchiveAsync_Cancelled_LeavesNoTempFile`.

### T-F27 — Temporary Directory Pattern for Safe Extraction
- [x] **Status:** complete — Completed in v1.1 sprint, March 2026

Extraction target is `destPath + "_tmp"` during operation. On success: `_tmp` directory moved to final destination. On failure or cancellation: `_tmp` deleted cleanly. New test: `ExtractAsync_Cancelled_LeavesNoTempDirectory`.

### T-F28 — Archive Bomb Protection
- [x] **Status:** complete (Variant B — ratio-based skip) — Completed in v1.1 sprint, March 2026

`MaxCompressionRatio = 1000` constant in `ZipArchiveService`. Entries with ratio >1000:1 added to `SkippedFiles` and skipped — archive continues for remaining entries. Post-v1.1: configurable limits if needed for large map/imagery files. New test: `ExtractAsync_SuspiciousCompressionRatio_SkipsEntry`.

### T-F29 — UTF-8 Filename Encoding Verification
- [x] **Status:** complete — Completed in v1.1 sprint, March 2026

.NET 8 `ZipArchive` sets UTF-8 EFS flag automatically for non-ASCII entry names — no code change required. New tests: `CyrillicFilename_PreservedAfterRoundTrip`, `EmojiFilename_PreservedAfterRoundTrip`. 48/48 tests pass.

---

## v1.2 Security Hardening

### T-F14 — About Dialog
- [x] **Status:** complete — Completed March 2026

`DialogService.ShowAboutAsync()` shows version (from `Package.Current.Id.Version`, fallback "dev"), license line, GitHub and Privacy Policy `HyperlinkButton`s (URLs from `Resources.resw` `AboutGitHubUrl` / `AboutPrivacyUrl`). `TrayAboutCommand` in `MainWindow.xaml.cs` calls `ShowAboutAsync()`. About button added to Row 0 of `MainWindow.xaml` (right-aligned via `Grid ColumnDefinitions="Auto,Auto,*,Auto"`). Tray menu item "About Pakko" wired to the same command.

---

## v1.2 Security Hardening

### T-F37 — Reparse Point Protection During Extraction
- [x] **Status:** complete — Completed March 2026

`PathContainsReparsePoint(destFilePath, rootPath)` walks directory components of the resolved destination path and checks `FileAttributes.ReparsePoint`. Called after `Directory.CreateDirectory` in the per-entry loop. Entries whose path traverses a symlink or junction are added to `SkippedFiles` and skipped. No automated unit test — `System.IO.Compression` cannot create reparse point entries in test fixtures; manual testing required.

### T-F38 — Alternate Data Streams Protection
- [x] **Status:** complete — Completed March 2026

`EntryHasAlternateDataStream(entryFullName)` checks for `:` in the raw ZIP entry name. Check runs before `Path.GetFullPath` to prevent OS-level device path resolution. Rejected entries added to `SkippedFiles` with reason `"Alternate Data Stream entry rejected for security."`. New test: `ExtractAsync_EntryWithColonInName_IsSkipped`. 57/57 tests pass.

### T-F39 — Reserved Windows Filename and Control Character Filtering
- [x] **Status:** complete — Completed March 2026

`EntryHasReservedName(entryFullName)` checks the last segment of the raw ZIP entry name (before `Path.GetFullPath`) against a `HashSet` of 22 reserved Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`), case-insensitive, stripping extension. `EntryHasControlCharacters(entryFullName)` rejects any entry with a character `< 0x20`. Both checks run before `Path.GetFullPath` — critical because Windows resolves bare reserved names (e.g. `NUL`) to device paths. New tests: `ExtractAsync_ReservedWindowsName_IsSkipped` (7 theory cases), `ExtractAsync_EntryWithControlCharacters_IsSkipped`. 57/57 tests pass.

---

### T-F16 — Byte-accurate Progress Reporting
- [x] **Status:** complete — Completed March 2026

New `src/Archiver.Core/IO/ProgressStream.cs`: a read/write `Stream` wrapper that counts bytes transferred and reports percentage via `IProgress<int>`. Constructor overload accepts `startOffset` so multiple files share a single 0–100 progress range. `ZipArchiveService` changes: `AddEntryFromFileAsync` wraps `entryStream` (write side) with `ProgressStream` when `progress != null`; `AddDirectoryToArchiveAsync` accumulates `startOffset` per file; `ArchiveAsync` (both `SingleArchive` and `SeparateArchives` modes) calls `ComputeTotalBytes` upfront and tracks a running `byteOffset` across the loop. `ExtractAsync`: `isSingleLargeArchive` removed — for a single archive, byte-based progress is passed into `ExtractWithSmartFolderingAsync`; for multiple archives, file-count progress is kept. `ExtractWithSmartFolderingAsync` wraps `entry.Open()` (read side) with `ProgressStream(entryStream, totalCompressedBytes, bytesRead, progress)`; all skipped entries advance `bytesRead` for accurate reporting. `IsIndeterminate` removed from `MainViewModel` and `MainWindow.xaml` — progress is always 0–100. New tests: `ArchiveAsync_SingleFile_ReportsMonotonicByteProgress`, `ExtractAsync_SingleArchive_ReportsMonotonicByteProgress`. 59/59 tests pass.

### T-F44 — File Type Association
- [x] **Status:** complete — Completed March 2026

`Package.appxmanifest`: added `uap3` namespace + `IgnorableNamespaces` entry; added `<Extensions>` block inside `<Application>` with `uap:FileTypeAssociation Name="zipfile"` declaring `.zip` support, display name "Pakko ZIP Archive", and `Square44x44Logo.png` as the file type logo. MSIX install/uninstall automatically registers/removes the association via the Windows app model.

`App.xaml.cs`: added `_window` field; subscribe to `AppInstance.GetCurrent().Activated` in constructor; `OnActivated` handler checks `ExtendedActivationKind.File`, casts `args.Data` to `IFileActivatedEventArgs`, extracts `StorageFile.Path` values, and calls `_window.ViewModel.AddPaths(paths)`. Handles both cases: window already created (normal launch → file open) and window not yet created (app started cold via file double-click but `Activated` fires before `OnLaunched` completes). `OnLaunched` now stores window in `_window` field. `dotnet build src/Archiver.App` — 0 errors.

### T-F45 — Mark of the Web (MOTW) Propagation
- [x] **Status:** complete — Completed March 2026

`ZipArchiveService.ExtractWithSmartFolderingAsync`: after `CopyToAsync` completes and the file stream is closed for each extracted file, calls `TryPropagateMotw(archivePath, destFilePath)`. `TryPropagateMotw` opens `archivePath + ":Zone.Identifier"` for read and `destFilePath + ":Zone.Identifier"` for write, copies bytes verbatim, swallows all exceptions (MOTW is best-effort, never fatal). If the archive has no Zone.Identifier ADS, the open throws `FileNotFoundException` which is silently caught. MOTW is always on — no `ExtractOptions` flag. No P/Invoke — standard `FileStream` with ADS path syntax. New test: `ExtractAsync_ZipWithMotw_PropagatesZoneIdentifierToExtractedFiles` — creates a ZIP, writes `[ZoneTransfer]\r\nZoneId=3\r\n` to the archive's ADS, extracts, verifies each extracted file has identical ADS content; skips gracefully on non-NTFS volumes via early return. 60/60 tests pass.

---

## v1.1 Hardening & Correctness

### T-F19 — Streaming Safety Audit
- [x] **Status:** complete

All file transfers already used `CopyToAsync` with an explicit 80 KB buffer; audit found no `File.ReadAllBytes`/`ReadAllText` in `ZipArchiveService`. Confirmed via 1 GB file memory profiling (no spike beyond 2x buffer size). No code changes required.

### T-F20 — Zip64 Verification
- [x] **Status:** complete

Verified Zip64 support for >65535-file archives and >4 GiB round trips (via an NTFS sparse file, `FSCTL_SET_SPARSE` P/Invoke, test-only, falls back to skipping on volumes without sparse-file support). These three tests cost real wall-clock time (~30s each, ~1m25s total) and are tagged `[Trait("Category", "Slow")]`, excluded from the routine `dotnet test --filter "Category!=Slow"` run — the first use of this convention in the repo (see `DECISIONS.md`'s "T-F20 — Slow Test Convention").

### T-F21 — Race Condition Handling During Traversal
- [x] **Status:** complete

`FileNotFoundException` during directory traversal (file deleted mid-scan) now becomes an `ArchiveError` and the operation continues rather than throwing. New test: file deleted mid-archive → `ArchiveError`, no unhandled exception reaches the caller.

### T-F22 — Windows Long Path Support
- [x] **Status:** complete

`app.manifest` declares `longPathAware`. Verified archive/extract handle paths >260 characters without silent truncation or failure; new tests cover this.

### T-F23 — Symlink and Junction Handling
- [x] **Status:** complete

Decision: skip symlinks/junctions during traversal rather than follow them — added to `SkippedFiles` with a clear reason, no recursive loop on circular symlinks, NTFS junctions handled identically. New test: directory containing a symlink → `SkippedFile`.

### T-F24 — Property-Based Archive Integrity Testing
- [x] **Status:** complete

New `ZipArchiveServicePropertyTests.cs`: generates random directory trees (`GenerateLevel`/`RandomTreeShape`, configurable depth/file count/size, plus `ForceMaxDepth` for guaranteed deep nesting), archives, extracts, and compares SHA-256 hashes per file. Four `[Fact]`s (all-small, all-large, mixed, deep-nesting) plus a `[Theory]` over seeds 1–12. Deliberately implemented after T-F75's recursion fix landed, confirming it now catches the exact class of bug T-F75 fixed by hand. 99/99 tests pass (16 new).

### T-F25 — README Security Positioning Review
- [x] **Status:** done

Replaced unverifiable "more secure than X" claims with "different trust model, not superior security" framing; kept factual CVE references and the supply-chain risk section; added a caveat that the .NET runtime itself is a trust dependency. `SECURITY.md` left unchanged (already appropriately nuanced).

### T-F30 — Duplicate Filename Detection Inside Archive
- [x] **Status:** complete

Archive side: `GetUniqueEntryName` tracks top-level entry names already claimed and renames a colliding second `SourcePath` ("name (1).ext"), same convention as `GetUniqueFilePath`. Extract side: new `claimedFinalPaths` tracks every final path already claimed within the current run, closing a gap where the prior conflict check only looked at the destination's pre-existing state (nothing committed there until the whole extraction loop finishes) — a genuine duplicate entry inside one ZIP no longer silently overwrites itself in the temp extraction directory. `GetUniqueFilePath` gained an optional `claimedPaths` exclusion set. 83/83 tests pass (was 79/79).

### T-F31 — Deterministic Archive Output
### T-F32 — Directory Traversal Ordering
- [x] **Status:** complete (implemented together — overlapping scope)

Files sorted by full path (ordinal, case-insensitive) before archiving, so identical inputs produce byte-identical ZIPs and `Directory.EnumerateFiles`'s non-deterministic order no longer leaks into archive entry order. New test: same input archived twice → identical ZIP bytes.

---

## v1.2 — Shell Extension

### T-F53 — Archiver.Shell Project
- [x] **Status:** complete

New `net8.0-windows` `WinExe` project, references `Archiver.Core` only, no console window. Parses `--extract-here`, `--extract-folder`, `--archive`, `--open-ui --extract`, `--open-ui --archive` and dispatches correctly (`--extract-here` reuses T-14's smart folder logic). `--open-ui` launches `Archiver.App` via `pakko://` (T-F56) and exits immediately.

### T-F54 — Archiver.ProgressWindow Project
- [x] **Status:** complete, later superseded

Minimal WinUI 3 project showing live progress for silent shell operations, fed via a named pipe from `Archiver.Shell`. **Deleted entirely by T-F65** — it crashed at WinUI/WindowsAppRuntime init regardless of XAML content; progress UI now runs in-process via `IProgressDialog` instead. Recorded here for history only — do not re-create this project.

### T-F56 — Protocol Activation (pakko://)
- [x] **Status:** complete

`pakko://` URI scheme registered in `Package.appxmanifest`; `Archiver.App` parses `pakko://extract?files=...` / `pakko://archive?files=...` (base64-encoded JSON array of paths) and pre-loads the UI. Both cold-start and warm activation were believed handled via `AppInstance.Activated` at the time — **the cold-start path actually had a latent bug, found and fixed later by T-F83.**

### T-F57 — Shell Integration Tests
- [x] **Status:** complete

`Archiver.Shell`'s argument parsing extracted into a testable `ShellArgumentParser` class; covers all CLI argument combinations, missing arguments (graceful error, no crash), and multi-file arguments.

### T-F61 — IExplorerCommand Implementation
- [x] **Status:** complete

In-process COM DLL (`Archiver.ShellExtension.dll`, C++/WRL, x64+ARM64, static CRT) implementing `IExplorerCommand` for `PakkoRootCommand`/`ExtractHereCommand`/`ExtractFolderCommand`/`ArchiveCommand`, registered as `com:SurrogateServer` (runs in an isolated `dllhost.exe`; see `DECISIONS.md`'s "Correction — SurrogateServer"). Dynamic submenu by selection type; selected paths read via `GetPathsFromShellItemArray`. Key fixes found along the way: `GetIcon`/`GetToolTip` return `E_NOTIMPL` not `S_FALSE` (fixed a real `explorer.exe` crash); `Archiver.Shell.exe`/`Archiver.ProgressWindow.exe` declared as their own `<Application>` entries (`AppListEntry="none"`) and built self-contained — an undeclared or framework-dependent EXE fails silently under `CreateProcess` from inside an MSIX package. `Archiver.ShellExtension.Tests` (Google Test) passes. Manually smoke-tested end-to-end 2026-07-04/05 — Explorer doesn't hang, "Pakko ▶" submenu appears with icon, Extract here/to folder/Add to archive all work.

### T-F62 — Context Menu: Test Archive (Integrity Check)
- [x] **Status:** complete

New `TestAsync` on `IArchiveService` — reads every entry, computes CRC-32, compares against the entry's declared value, never writes to disk. Appears whenever selection contains ≥1 `.zip` (`AnyPathIsZip`, shows even on mixed selections), deliberately ordered *after* Extract/Archive per this project's primary-before-diagnostic menu-ordering rule (`CLAUDE.md`), a deviation from NanaZip's Test-first order. `Archiver.Core.Tests` 77/77, `Archiver.Shell.Tests` 28/28, C++ Google Test 33/33. Manually smoke-tested 2026-07-06 — valid archive → "No errors detected"; corrupted-CRC fixture → the exact CRC mismatch reported.

### T-F63 — Context Menu: "Extract…" and "Compress…" with Dialog
- [x] **Status:** complete

Two new leaf commands (`ExtractDialogCommand`, `CompressDialogCommand`) invoke `Archiver.Shell.exe --open-ui --extract`/`--archive`, opening the full Pakko UI with files pre-loaded via the existing `pakko://` flow, instead of running silently. Menu order verified against NanaZip's real shipped source (fetched via the GitHub trees API): the dialog command precedes its one-click sibling in both groups. This task's manual test surfaced T-F83 (cold-start activation silently dropping the file payload), fixed the same day. `Archiver.ShellExtension.Tests` 43/43. Manually smoke-tested end-to-end 2026-07-06, after the T-F83 fix.

### T-F64 — Context Menu: Fix "Add to archive…" Label vs One-Click Behavior
- [x] **Status:** complete

`ArchiveCommand::GetTitle` now renders `Add to "<name>.zip"` (`BuildAddToArchiveTitle`, computed from the first selected item, long names truncated middle) instead of the misleading "Add to archive…" — an ellipsis conventionally implies a dialog that this command never showed. No behavior change. Found and fixed a real mojibake bug along the way: the ellipsis glyph, saved as literal UTF-8 without a BOM, was misdecoded by MSVC's active-code-page fallback (cp1251) on this Cyrillic/Ukrainian-locale machine — fixed via a `\uXXXX` escape (see `CONVENTIONS.md`'s non-ASCII string literal rule; this exact bug recurred twice more later, T-F76/T-F63). `Archiver.ShellExtension.Tests` 30/30.

### T-F65 — Progress UI Redesign: IProgressDialog Replaces Archiver.ProgressWindow
- [x] **Status:** complete — not fixed as originally titled; the underlying design was removed

The suspected "App.xbf resource collision" was disproven (a no-XAML rewrite crashed identically). `Archiver.ProgressWindow` (separate WinUI 3 `.exe` + named-pipe IPC, T-F54) was deleted entirely; `Archiver.Shell` now shows progress via the Windows Shell's built-in `IProgressDialog` COM object, in-process — confirmed via NanaZip's own shipped source that it never spawns a second WinUI process either. Along the way: fixed a real Cancel-does-nothing bug (`IProgressDialog.HasUserCancelled` returns a plain `BOOL`, not `HRESULT`, needed `[PreserveSig]` — see `CLAUDE.md`'s COM interop hard constraint); added current-filename display to the dialog. `dotnet test` 95/95. Manually smoke-tested 2026-07-05.

### T-F58 — Archive Finalization Phase UX
- [x] **Status:** complete

When byte-progress reaches 100% but `ZipArchive` is still writing its central directory/flushing/closing/renaming, the UI now switches to an indeterminate "Finalizing..." state instead of appearing frozen. `IsProgressIndeterminate` added to `MainViewModel`, reset in `finally`. `dotnet test` 67/67.

### T-F59 — Extraction Progress Overshoot Fix
- [x] **Status:** complete

Extraction progress total is now computed from `entry.Length` (uncompressed) instead of `entry.CompressedLength`, matching the unit `ProgressStream` actually counts as bytes flow out of `entry.Open()` — fixes progress bar overshoot/reset on well-compressed archives. ZIP bomb ratio check still uses `CompressedLength`. `dotnet test` 67/67.

### T-F60 — Cleanup Bug: Leftover .tmp on All-Failures Archive
- [x] **Status:** complete

New `HasTempEntries` helper (opens the temp ZIP read-only, checks `Entries.Count > 0`) gates the `File.Move` temp → dest commit in both SingleArchive and SeparateArchives paths — an empty temp ZIP (every source file failed) is now deleted instead of committed as a bogus empty `.zip`. Partial success (some files ok, some missing) still commits with `Success = false` and errors reported. `dotnet test` 69/69.

### T-F68 — Shell Extract Silently Ignores SkippedFiles
- [x] **Status:** complete — verified 2026-07-06

Decision (`DECISIONS.md`): widen only the shell path's dialog trigger, not `ArchiveResult.Success` itself (`Success` stays `errors.Count == 0`, unchanged for the GUI). `RunWithProgressWindowAsync` now shows a dialog (via new `ShellResultPresenter.Classify`) whenever `SkippedFiles.Count > 0`, distinguishing "N entries skipped" (`MB_ICONWARNING`) from "operation failed" (`MB_ICONERROR`, unchanged). `Archiver.Shell.Tests` 36/36 (new `ShellResultPresenterTests`, 8 cases). Manually smoke-tested 2026-07-06 — an ADS-only ZIP now shows the skip warning instead of completing silently.

### T-F69 — Fix ARCHITECTURE.md Doc Drift: com:InProcessServer → com:SurrogateServer
- [x] **Status:** complete (trivial)

`ARCHITECTURE.md` still said `com:InProcessServer`, contradicting the actual manifest and `DECISIONS.md`'s own "Correction — SurrogateServer" entry from T-F61. Updated, along with a stale sub-command list/selection-logic description that had drifted the same way (missing `TestCommand`/T-F63's dialog commands).

### T-F70 — IsBusy vs. Status-Text Timing After Cancel vs. Success/Error
- [x] **Status:** complete — decided and fixed 2026-07-06

Decision: align, not document the asymmetry. `IsBusy = false` moved from the `finally` block to immediately before the final `StatusMessage = "Ready"` line in both `ArchiveAsync`/`ExtractAsync` (after the cancel-only `Task.Delay(2000)`), so the UI stays busy for exactly as long as something transient (dialog or "Cancelled" text) is still on screen, for all four outcomes rather than three. `DIAGRAMS.md` diagram 2 updated to match.

---

## Documentation Consolidation (2026-07-06)

### T-F71 — Consolidate Security/Supply-Chain Rationale (SECURITY.md as sole owner)
- [x] **Status:** complete

`SPEC.md`'s "Security Rationale" and `README.md`'s "Why Not 7-Zip or WinRAR?" sections trimmed to a 2–3 line teaser + link; `SECURITY.md` is now the sole full copy of the CVE/supply-chain tables and MOTW rationale.

### T-F72 — Consolidate Version Roadmap Table (SPEC.md as sole owner)
- [x] **Status:** complete

`SPEC.md`'s roadmap table (v1.1–v1.5) is now the sole copy; `CLAUDE.md`'s "Roadmap Summary" and `README.md`'s "Roadmap" sections replaced with a link to it. Version-specific status detail beyond scope stays in `CLAUDE.md`'s "Current State" prose.

### T-F73 — Fix Stale IProgress<int> Signature in ARCHITECTURE.md and CONVENTIONS.md
- [x] **Status:** complete (trivial)

`ARCHITECTURE.md`'s `IArchiveService` snippet and `CONVENTIONS.md`'s XML-doc example both updated from `IProgress<int>` to the real `IProgress<ProgressReport>?` signature (added back in T-F16, never synced to the docs).

### T-F74 — Consolidate Duplicate Testing/Build Rules Between CLAUDE.md and TASKS.md
- [x] **Status:** complete (low priority)

`TASKS.md`'s testing-rule bullets that duplicated `CLAUDE.md`'s Hard Constraints replaced with a link; task-specific rules with no `CLAUDE.md` equivalent (completion marking, UI-vs-logic, scope rules) kept in `TASKS.md`.

---

## UI/UX Design Pass (2026-07-06)

Design direction was checked against Pakko's gov/defense audience (trust, auditability, minimal
attack surface, `CLAUDE.md`'s Project section) before implementation — this audience reads native
OS fidelity as a trust signal and a bespoke brand skin as the opposite, so all decisions below
favor WinUI-native idioms and restraint over a custom visual identity.

### T-F76 — Bug: "Extract to folder…" Ellipsis Implies a Dialog That Never Appears
- [x] **Status:** complete — same bug class as T-F64

`ExtractFolderCommand::GetTitle` now computes `Extract to "<name>\"` from the first selected item (`BuildExtractFolderTitle`, reusing `TruncateMiddle`); multi-selection shows "Extract each to its own folder" instead of a single, untruthful name. No behavior change. `Archiver.ShellExtension.Tests` 39/39.

### T-F77 — Archive/Extract Options Don't Adapt to the Active Action
- [x] **Status:** complete

Decision (`DECISIONS.md`, "T-F77/T-F81 — Contextual Option Visibility and the Outcome Subtitle"): collapse Archive-only fields (Mode/Name/Compression) via a plain `Visibility` binding to `ArchiveOptionsVisibility` when the selection is 100% `.zip` (extract-only); any non-`.zip` item keeps archive-mode fields visible. Shared fields (Destination, If file exists, Open destination folder) stay always visible. No custom animation, per this niche's preference for predictability over motion. `dotnet test` 127/127.

### T-F78 — No Typographic Hierarchy in the Options Panel
- [x] **Status:** complete

Field labels use `CaptionTextBlockStyle` (existing WinUI resource); inputs stay at body weight, so contrast comes from the label. No new custom style, no layout shift, verified in both light and dark theme.

### T-F79 — Record the Decision Not to Add a Custom Brand Palette
- [x] **Status:** complete — documentation-only

Recorded in `DECISIONS.md`: Pakko deliberately has no custom brand palette — native WinUI theme plus the user's system accent color only, since a bespoke skin would read as "skinned" and undercut trust for the gov/defense audience. Restraint (T-F78) and contextual clarity (T-F77) are the actual signature instead of a color identity.

### T-F80 — Empty State Doesn't Teach the Two Modes
- [x] **Status:** complete

Added one localized second line under the empty-state drop zone (`DropZoneModeHint.Text`, `Resources.resw`): "Archives offer to extract; anything else gets archived." No icons, illustration, or animation — text addition only.

### T-F81 — Archive/Extract Button Pair Reads as a Toggle, Not Two Distinct Actions
- [x] **Status:** complete — decided alongside T-F77

Decision: keep the inactive button greyed out (not hidden), and add a one-line outcome subtitle under the button row (`OperationOutcomeText`/`OperationOutcomeVisibility`) stating the resolved action in plain language, e.g. "Will extract 1 archive(s) to the folder above." Presentation only — no change to `CanArchive`/`CanExtract`/command logic. `dotnet test` 127/127.

### T-F82 — Archive Button Keeps Accent Color Even When the Outcome Subtitle Says "Extract"
- [x] **Status:** complete — found during a follow-up design-review pass comparing rendered screens rather than mockups

`AccentButtonStyle` now follows `IsExtractOnlySelection` — `Archive` is accented except when the selection is extract-only, in which case `Extract` is instead, so visual weight always matches whichever action the T-F81 outcome subtitle describes. Presentation only.

### T-F83 — Bug: Cold-Start Protocol/File Activation Never Reached OnActivated
- [x] **Status:** complete

`AppInstance.Activated` (Windows App SDK) only fires for activations *redirected* to an already-running instance, never for a process's own cold start — `OnLaunched` built a blank window and silently dropped the File/Protocol payload on every cold `pakko://`/file-association launch (a bug pre-dating T-F63, in already-shipped T-F44/T-F56 code; found while manually testing T-F63). Fixed via a shared `HandleActivation` helper called from both `OnActivated` (warm) and `OnLaunched` (cold, via `AppInstance.GetCurrent().GetActivatedEventArgs()`). Confirmed end-to-end 2026-07-06 for protocol activation, and again once Pakko was set as the default `.zip` handler for T-F44's file-activation cold-start path — `pakko.log` showed `"Pakko started via file activation"` and the file list was visibly populated with no Pakko process running beforehand.
