---
paths:
  - "scripts/Deploy.ps1"
  - "src/Archiver.App/Package.appxmanifest"
---

# On-device verification

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **A quick `dotnet build` can silently install a stale MSIX.** Its `DeployMsix` post-build
  target reports success even when MSBuild's incremental packaging step skipped repackaging a
  changed DLL into the `.msix` (confirmed via file timestamp — 55 min old after a rebuild that
  changed a XAML-bound command). Don't trust a bare `dotnet build`'s installed package when
  verifying a UI change on-device — run the full `.\scripts\Deploy.ps1` first (it wipes old
  `AppPackages` output before rebuilding).
- **Correction (recurred 2026-07-18, T-F123, worse than the original symptom):** a bare
  `dotnet build src/Archiver.App/Archiver.App.csproj /p:Platform=x64` was used to verify a
  `MainWindow.xaml.cs` event-handler fix (an `IsBusy` guard on `ArchiveBrowserList_DoubleTapped`).
  The fix appeared to fail identically across three separate rebuild-and-retest cycles — even
  after the title-bar `Pakko — build <timestamp>` freshness check (below) looked correct each
  time. A `File.AppendAllText` trace planted at the top of the handler proved the handler wasn't
  being invoked at all: the installed package was running stale event-handler code despite a
  fresh-looking title-bar timestamp and a "Build succeeded" log. Switching to the full
  `.\scripts\Deploy.ps1 -Thumbprint ...` fixed it on the very next attempt. **Always use
  `Deploy.ps1`, never a bare `dotnet build`, before any on-device verification of
  `Archiver.App` — do not treat the title-bar timestamp as sufficient proof by itself; it can be
  fresh while the packaged binary's actual logic is stale.**
- **Never trust build logs alone to prove an on-device check ran against fresh code — always
  have the running window itself prove it.** `Archiver.App`'s title bar shows
  `Pakko — build <yyyy-MM-dd HH:mm:ss>`: the running assembly's compile time, from its
  `PakkoBuildTimeUtc` metadata (T-F218; the file time was the MSIX install time), hidden in a
  Store build (T-F198 item 4) — not a manually-bumped version, not a build-log claim, but what the
  installed binary itself reports, visible in every screenshot. Before
  treating any on-device verification result as valid (especially a repeated "still broken"
  result across several fix attempts), confirm this timestamp is within the last few minutes of
  the current time. If it's stale, the deploy didn't actually pick up the latest change and the
  verification must be redone — don't reason from build-log output alone. If a UI element already
  on screen needs a freshness check and the title bar isn't convenient to read in a given
  screenshot, add a similar visible, runtime-computed marker to the relevant page instead of
  trusting logs.
- **Windows MCP (`mcp__windows__*`) synthesizing a WinUI `DoubleTapped` gesture is coordinate-
  space sensitive, not fundamentally unreliable.** `mouse_control`'s `double_click` failed across
  ~6 attempts in one session (T-F98/T-F109) when driven by `windowHandle`-relative coordinates or
  a coordinate guess. The combination that works reliably (confirmed T-F110, a full 4-level
  Archive Browser drill-down entirely via automation): call `ui_find` for the row to get its
  `click` coordinates, then pass those coordinates straight to `mouse_control`'s `double_click`
  with `target: "primary_screen"` and no `windowHandle` at all — same fix T-F107 found for plain
  single clicks (see that entry above), it turns out to also fix double-clicks. Explorer's
  right-click context menu (Shift+F10) remains unconfirmed either way. If a double-click still
  doesn't register after trying the `ui_find` + `primary_screen` combination once, then fall back
  to asking the user to reproduce manually rather than burning further attempts.
- **`winget install`/`uninstall` needing elevation fails non-interactively** with
  `0x800704c7` ("canceled by the user") — the UAC prompt has nothing to click it. Retry once
  and ask the user to approve the UAC prompt that appears; the retry succeeds.
- **Explorer's context menu is automatable via `windows` MCP (confirmed 2026-09-28, T-F235):**
  `mouse_control` `right_click` on a selected item with `target: "primary_screen"`, then `ui_click`
  the `MenuItem` "Pakko" and the leaf by `nameContains` in the Explorer window's handle (one call
  per step — a batched sequence lost the menu). Explorer always sends `--paths-stdin`; the
  installed `Archiver.Shell.exe` still accepts plain path args as a shortcut (e.g. `--open-ui
  --browse "<path>"`, T-F03), which skips the COM click and the stdin transport.
- **Distinguishing an installed dev build from a Store build (or confirming only one is present):**
  `Get-AppxPackage *Pakko* | Select-Object Name, PackageFullName, Publisher, SignatureKind,
  InstallLocation` — `Publisher: CN=Pakko Dev` + `SignatureKind: Developer` is the local sideload;
  a Store install shows a different Publisher and `SignatureKind: Store`. Combine with `Get-Item
  <InstallLocation>\Archiver.App.exe | LastWriteTime` vs. current time to confirm freshness,
  complementing (not replacing) the title-bar build-timestamp trick above.
- **Debugging via Pakko's log file:** when running as an installed MSIX, the log is NOT at the
  plain `%LOCALAPPDATA%\Pakko\logs` `LogService.cs` constructs — MSIX virtualizes
  `LocalApplicationData` per-package. Find it at
  `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Pakko\logs\pakko.log`
  (get `<PackageFamilyName>` via `Get-AppxPackage *Pakko*`).
- **To verify a shell-triggered EXE actually runs** (Explorer/COM invocation can't be scripted):
  launch it directly the same way the COM caller would (`Start-Process <path> -ArgumentList ...`)
  and check `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'}`
  for silent apphost failures — these never produce console output or a visible error otherwise.
  For a *native* crash (WinUI/WindowsAppRuntime init failure, access violation, etc.) instead
  check `ProviderName='Application Error'` — these show as event ID 1000 with the faulting
  module/offset/exception code and never appear under the `.NET Runtime` provider at all.
- **`Archiver.Shell.exe`'s CLI commands (`--archive`, `--extract-here`, `--extract-folder`,
  `--test`, `--hash`) take only source/archive paths — never an explicit destination.** The
  destination is always auto-computed (folder-of-source for extract, `<name>.zip` next to the
  source for archive — same naming Explorer's "Add to X.zip" verb produces). Passing an extra path
  as a destination gets silently treated as another source/archive path instead (T-F142).
- **Any Pakko command that shows a native modal (`IProgressDialog`, `MessageBoxW` result dialogs —
  `--archive`, `--extract-*`, `--test`, `--scan`, `--hash`) blocks forever if invoked directly
  (`& $exe args`) from the PowerShell tool** — the call never returns because the dialog waits for
  a click. Launch via `Start-Process -FilePath $exe -ArgumentList @(...)` (detached) instead, then
  poll for the dialog with `EnumWindows`/`GetWindowThreadProcessId` filtered to the child PID, read
  its result text via `EnumChildWindows`, and dismiss with `SendMessage(hWnd, 0x00F5, ...)`
  (BM_CLICK) on the OK button's handle. If a call hangs anyway, `taskkill /F /IM
  Archiver.Shell.exe` clears the stuck modal before retrying (T-F151/T-F153 smoke tests).
- **PowerShell tool's `Add-Type` classes do NOT persist across separate calls** (only cwd does) —
  a `Win32`-style helper class defined in one call is gone in the next ("Unable to find type"). If
  you need it again (e.g. for a follow-up screenshot), redefine the whole `Add-Type` block in the
  same call that uses it, not just once at the start of a multi-call sequence. Also: `Get-Item` on
  a registry path containing `{...}` (a GUID/CLSID) silently returns nothing unless you pass
  `-LiteralPath` instead of the default `-Path` — curly braces are wildcard syntax otherwise.
- **Pass `& $exe` arguments as separate array elements, never manually quoted inside a string** —
  `& $exe $path1 $path2`, not `` & $exe "`"$path1`"" ``. The latter embeds literal `"` characters
  into the argument itself once PowerShell's own tokenizer is done, corrupting the path (confirmed:
  `IOException` with a visibly quote-mangled path, T-F142 on-device check). Let PowerShell's own
  array-argument passing handle spaces — don't hand-roll quoting.
