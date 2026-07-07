# DECISIONS.md — Architectural Decisions

Key decisions and rejected alternatives. Read before implementing anything in
packaging, COM, or shell integration.

---

## Context Menu Appeared But Commands Did Nothing (three stacked packaging bugs)

**Symptom:** After fixing the `explorer.exe` crash (below), the "Pakko ▶" submenu appeared
with correct icons, but clicking "Extract here" / "Extract to folder…" / "Add to archive…"
did nothing — no error, no UI, no extracted files.

**Root causes, found by testing `Archiver.Shell.exe` directly (bypassing Explorer/COM) via
`CreateProcess`/`Start-Process` from an external process, then reading the Windows
`Application` event log for `.NET Runtime` entries** — three separate, stacked failures,
each hiding the next:

1. **`ERROR_ACCESS_DENIED` launching `Archiver.Shell.exe`.** Windows blocks `CreateProcess`
   of any EXE inside an installed package's `WindowsApps` folder unless that EXE is declared
   as its own `<Application>` in `AppxManifest.xml` — confirmed via
   [microsoft/WindowsAppSDK#4651](https://github.com/microsoft/WindowsAppSDK/issues/4651):
   "on Win11 it is sufficient to create an entry for your helper EXEs in AppxManifest.xml."
   Only `Archiver.App.exe` (`Id="App"`) was declared; `Archiver.Shell.exe` and
   `Archiver.ProgressWindow.exe` were bare `Content Include` files with no `<Application>`
   entry, so `IExplorerCommand::Invoke`'s `CreateProcessW` call always failed silently
   (the shell does not surface `Invoke` failures to the user).
   **Fix:** added `<Application Id="ShellHelper" Executable="Archiver.Shell.exe"
   EntryPoint="Windows.FullTrustApplication">` and the equivalent for
   `Archiver.ProgressWindow.exe`, both with `AppListEntry="none"` on `uap:VisualElements`
   to hide them from the Start menu / app list (`uap:VisualElements` is a required child
   element even for hidden entries — still needs `DisplayName` and both logo attributes).

2. **`Archiver.Shell.exe` apphost couldn't find its own managed assembly.** Once launchable,
   the `.NET Runtime` event log showed: `"The application to execute does not exist:
   ...Archiver.Shell.dll"`. `Archiver.App.csproj`'s `Content Include` for the satellite only
   copied the bare `.exe` — a framework-dependent/self-contained .NET apphost is a thin
   native stub that always needs its `.dll` + `.deps.json` + `.runtimeconfig.json` alongside
   it. **Fix:** added `Content Include` entries for all three files (not `Archiver.Core.dll`/
   `.pdb` — Archiver.App's own self-contained publish already places an identical copy of
   `Archiver.Core.dll` at the package root).

3. **`Archiver.Shell.exe` couldn't find the .NET runtime.** Even with the `.dll` present, the
   event log then showed `"You must install or update .NET to run this application... No
   frameworks were found"` — a real modal dialog box, which is why the process never exited
   (`HasExited: False` indefinitely; looked identical to "nothing happens" from the COM
   caller's side). Cause: `Deploy.ps1` built `Archiver.Shell`/`Archiver.ProgressWindow` with
   `--no-self-contained` (framework-dependent), but the MSIX package ships with no
   system-wide .NET runtime to fall back on — unlike `Archiver.App`, which is
   `SelfContained=true`. **Fix:** changed both `dotnet build` invocations in `Deploy.ps1` to
   `--self-contained`. This does not need extra `Content Include` entries for the native host
   files (`hostfxr.dll`, `coreclr.dll`, `hostpolicy.dll`, etc.) — `Archiver.App`'s own
   self-contained publish already deposits identical copies (same TFM/RID) at the package
   root, and a self-contained apphost probes its own directory first.

**Verification method:** since Explorer/COM surrogate invocation can't be scripted, each fix
was verified by directly invoking `Start-Process 'C:\Program Files\WindowsApps\...\
Archiver.Shell.exe' -ArgumentList '--extract-here "<test.zip>"'` from an external process
(faithfully reproducing what `IExplorerCommand::Invoke`'s `CreateProcessW` does) and
confirming the archive's contents actually appeared on disk — not just that the process
exited 0.

**Known remaining gap (not fixed, separate task — see T-F65):** `Archiver.ProgressWindow.exe`
still fails to launch after fix #2/#3 are applied to it too — its own event log entry was the
same "`.dll` does not exist" error before its `Content Include` was extended to match
`Archiver.Shell.exe`'s. `RunWithProgressWindowAsync` in `Archiver.Shell`'s `Program.cs` degrades
gracefully when `Archiver.ProgressWindow.exe` fails to connect its named pipe within 5 seconds
(falls back to running the operation silently, with no progress UI) — so extraction/archiving
still succeeds, just without visual feedback.

**Update 2026-07-05 — the `App.xbf` collision theory above is disproven.** T-F65 added the
missing `Archiver.ProgressWindow.dll`/`.deps.json`/`.runtimeconfig.json` `Content Include`
entries (the apphost fix, confirmed necessary and correct — the "`.dll` does not exist" error
is gone) and then, to rule out the theorized `App.xaml`/`App.xbf` resource-identity collision,
rewrote `Archiver.ProgressWindow`'s entire UI in C# with **zero XAML files** (no `App.xaml`,
no `ProgressWindow.xaml`, no `InitializeComponent`/`LoadComponent` call anywhere in the
project). The crash was **byte-for-byte identical** before and after this rewrite: `Application
Error` (event 1000), faulting module `Microsoft.UI.Xaml.dll` (from the
`Microsoft.WindowsAppRuntime.1.8` framework package, not the app-local copy), exception code
`0xc000027b` (`STATUS_STOWED_EXCEPTION`), same faulting offset `0x3a7515`, in both cases.
Since the second run has no XAML at all, nothing about `App.xaml`/`ms-appx:///`/`resources.pri`
can be the cause — the original hypothesis (and the T-F65 acceptance criteria as originally
written) was wrong. Reproduced identically via two paths: direct `Start-Process` of the exe,
and the real production path (`Archiver.Shell.exe --archive` spawning
`Archiver.ProgressWindow.exe` via `Process.Start`, exactly as `RunWithProgressWindowAsync`
does) — same crash both times, so this is not a test-harness artifact.

**What's actually happening (unconfirmed HRESULT):** `0xc000027b` is a WinRT exception
surfacing during WinUI/WindowsAppRuntime init, before any app code runs — it fires even for a
`Microsoft.UI.Xaml.Application`/`Window` with no XAML content. The only failing combination
among Pakko's three satellite processes is "uses WinUI 3" + "launched via raw `Process.Start`
instead of shell/user activation" (`Archiver.Shell.exe` has no WinUI dependency and works;
`Archiver.App.exe` has WinUI but is always shell-activated and works). This points at
WindowsAppRuntime/WinUI framework resolution failing for a `Windows.FullTrustApplication`
satellite spawned by `CreateProcess` rather than proper activation — not a resource-file
problem. Getting the real (stowed) HRESULT under the outer `0xc000027b` needs a crash dump
analyzed in WinDbg/`dotnet-dump` (`!analyze -v`); this was not available in the diagnosing
session (no local WER reports were archived, and `HKLM` `LocalDumps` registration requires
elevation not available in that shell). **Do not re-attempt a fix here without that HRESULT or
explicit direction** — two implementation attempts (apphost fix, then the no-XAML rewrite)
have already been made per the 3-attempt rule in `CLAUDE.md`.

---

## Archiver.ShellExtension — explorer.exe Crash on Context Menu (GetIcon/GetToolTip S_FALSE)

**Symptom:** `explorer.exe` crashes (access violation, null pointer deref) inside
`Windows.UI.FileExplorer.dll!winrt::hstring::operator=` / `ShouldLoadIconAsync()` when the
Pakko context menu is invoked, per a WinDbg trace captured against Microsoft symbol servers.

**Root cause:** `ExtractHereCommand::GetIcon`, `ExtractFolderCommand::GetIcon`,
`ArchiveCommand::GetIcon`, `PakkoRootCommand::GetIcon` (empty-path fallback), and the
corresponding `GetToolTip` methods all set `*ppszIcon`/`*ppszInfotip = nullptr` and returned
`S_FALSE`. `S_FALSE` is `0x00000001` — a **success** code under the `SUCCEEDED()` macro (top
bit clear). Shell code that does `if (SUCCEEDED(cmd->GetIcon(...))) { hstring = *ppszIcon; }`
treats `S_FALSE` the same as `S_OK` and dereferences the null pointer we returned.

**Verified against real reference implementations** (per this file's pre-implementation
research rule):
- Microsoft's own canonical `IExplorerCommand` sample,
  `Windows-classic-samples/.../ExplorerCommandVerb/ExplorerCommandVerb.cpp`:
  ```cpp
  IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR *ppszIcon)
  {
      *ppszIcon = NULL;
      return E_NOTIMPL;   // not S_FALSE
  }
  ```
  Same pattern for `GetToolTip`.
- ReactOS `zipfldr` `CExplorerCommand::GetIcon` (a real shipped `IExplorerCommand`
  implementation) never returns `S_FALSE` at all — it always returns `S_OK` with a valid,
  non-null icon string (`"zipfldr.dll,-1"`). No real-world implementation pairs a null
  out-pointer with a success HRESULT.

**Fix:** All `GetIcon`/`GetToolTip` "no value to provide" paths in
`src/Archiver.ShellExtension/ExplorerCommands.cpp` now return `E_NOTIMPL` instead of
`S_FALSE`. The one legitimate remaining `S_FALSE` in the file is
`SubCommandEnum::Next` (`IEnumXXX::Next` contract: `S_FALSE` means "fewer than `celt`
elements were returned", not "success with a null value") — left unchanged.

**Lesson:** `S_FALSE` is a *success* HRESULT. Never pair it with a null/unset out-parameter
in a COM method whose caller may only check `SUCCEEDED()`. Use `E_NOTIMPL` (or another real
failure HRESULT) whenever an out-parameter is intentionally left empty.

---

## Archiver.ShellExtension Build — Stale PCH (C1853)

**Symptom:** `Deploy.ps1` fails building `Archiver.ShellExtension.vcxproj` with
`error C1853: '...\Archiver.ShellExtension.pch' precompiled header file is from a different
version of the compiler, or the precompiled header is C++ and you are using it from C`.

**Cause:** `Deploy.ps1` deletes `AppPackages/` before rebuilding but never cleaned
`src\Archiver.ShellExtension\obj\` / `bin\`. A stale `.pch` left behind by a prior build
(e.g. built once from the Visual Studio IDE, or an interrupted headless build) gets picked
up by the next headless MSBuild invocation and fails with C1853 — reproduced on a fresh
checkout where `obj/`/`bin/` were not removed (`git clean` does not touch them by default
since they're gitignored build output, not tracked files).

**Fix:** `Deploy.ps1` now removes `src\Archiver.ShellExtension\obj\<platform>\Release` and
`bin\<platform>\Release` immediately before invoking MSBuild on it, and passes
`/nodeReuse:false` so no stale MSBuild worker node/compiler state can be reused across runs.

---

## Archiver.ShellExtension Build — Doubled Output Path / MSB3030

**Symptom:** `dotnet publish` on `Archiver.App.csproj` fails with
`error MSB3030: Could not copy the file "...\Archiver.ShellExtension\bin\x64\Release\
Archiver.ShellExtension.dll" because it was not found.` even though `Deploy.ps1` just
reported the DLL built successfully.

**Cause:** `Archiver.ShellExtension.vcxproj`'s `OutDir`/`IntDir` are defined relative to
`$(SolutionDir)`. `$(SolutionDir)` is only auto-populated by MSBuild when a build goes
through a `.sln` file. `Deploy.ps1` invokes the `.vcxproj` directly (no `.sln` in the
command line), so `$(SolutionDir)` was undefined and fell back to `$(ProjectDir)` — the
project's own folder — producing a doubled, wrong output path:
`src\Archiver.ShellExtension\src\Archiver.ShellExtension\bin\x64\Release\...` instead of
`src\Archiver.ShellExtension\bin\x64\Release\...`. The `Content Include` in
`Archiver.App.csproj` looks for the DLL at the correct (non-doubled) path, so `dotnet
publish` can't find it.

**Fix:** `Deploy.ps1` now passes `/p:SolutionDir=$repoRoot\` explicitly when invoking MSBuild
on `Archiver.ShellExtension.vcxproj`.

---

## Archiver.App — MSIX Never Generated (silent)

**Symptom:** `dotnet publish .../Archiver.App.csproj /p:GenerateAppxPackageOnBuild=true ...`
exits 0, `Archiver.App.exe` and all satellites publish fine, but no `.msix` is ever produced
anywhere in the repo — `Deploy.ps1` then fails at its own "No .msix file found" check. Tried
both the default `<PublishProfile>win-$(Platform).pubxml</PublishProfile>` (plain
file-system publish, no packaging) and the explicit `win-x64-msix.pubxml` profile (correct
`AppPackages`-shaped `PublishDir`, but still only a flat publish, no real MSIX artifacts) —
neither triggered actual Appx packaging.

**Cause:** `Archiver.App.csproj` had picked up a second, redundant NuGet package —
`Microsoft.Windows.SDK.BuildTools.MSIX` (1.7.251221100), added alongside the base
`Microsoft.Windows.SDK.BuildTools` package already providing MSIX tooling — presumably while
investigating manifest schema for the T-F61 COM registration work. Its
`Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets` shadows/duplicates the base
package's Appx packaging targets, silently no-oping the actual `.msix` generation while still
reporting build success.

**Fix:** removed the `Microsoft.Windows.SDK.BuildTools.MSIX` `PackageReference`. With only the
base `Microsoft.Windows.SDK.BuildTools` package present, `dotnet publish` with the default
publish profile correctly produces
`src\Archiver.App\AppPackages\Archiver.App_<version>_x64_Test\Archiver.App_<version>_x64.msix`,
and the full `Deploy.ps1` pipeline (build → sign → package → install) completes successfully.
If a future task needs extra MSIX/COM manifest schema support, verify against a real working
example (see the "Pre-implementation research" rule in `CLAUDE.md`) rather than adding a
second packaging-tools package — this is the second time an unverified COM/MSIX-schema
addition silently broke the pipeline (see "Correction — SurrogateServer" above).

---

## MSIX Satellite EXE Packaging

**Decision:** `Content Include` items in `Archiver.App.csproj` conditioned on
`GenerateAppxPackageOnBuild=true`.

**Rejected:**
- `BeforeTargets` MSBuild hooks — fires after MakeAppx regardless of target name; correct hook point changes across SDK versions and `dotnet publish` vs VS build contexts
- Manual `MakeAppx` calls in `Deploy.ps1` (unpack → inject → repack) — fragile, not incremental, duplicates SDK work
- `.wapproj` (Windows Application Packaging Project) — causes duplicate PRI resource entries (`Files/App.xbf`) when packaging multiple WinUI 3 apps; conflict cannot be resolved within the `.wapproj` model

**Reason:** `Content Include` is the only declarative approach that survives incremental builds and works identically in VS and `dotnet publish`.

---

## MSIX Signing

**Decision:** `dotnet publish` with `AppxPackageSigningEnabled=true` and
`PackageCertificateThumbprint` passed on the command line.

**Rejected:**
- Manual `SignTool.exe` calls after `MakeAppx` — produces `ERROR_BAD_FORMAT` on MSIX files

**Reason:** `New-SelfSignedCertificate` generates CNG keys by default on modern Windows. `SignTool` cannot use CNG keys to sign MSIX directly. `dotnet publish` uses a different internal code path that handles this correctly. Workaround if manual signing is ever needed: pass `-Provider "Microsoft Strong Cryptographic Provider"` to `New-SelfSignedCertificate` to force CryptoAPI (RSA 2048) instead of CNG.

---

## IExplorerCommand (Shell Context Menu)

> **Superseded 2026-07-04:** the original decision below (in-process `com:InProcessServer`)
> never actually registered — the menu silently failed to appear for ~4 months of real-world
> use. Root cause and correction are documented in "Correction — SurrogateServer" further down.
> Kept here for history; do not re-implement the `com:InProcessServer` schema.

**Decision (original, superseded):** In-process COM DLL (`Archiver.ShellExtension.dll`), WRL,
registered via `com:InProcessServer` in `Package.appxmanifest`.

**Rejected (original, superseded — see correction below):** Out-of-process COM EXE server
(previously described in T-F61). Shell can activate an in-process DLL directly without a
separate EXE activation path. Lower latency, simpler manifest registration, no `LocalServer32`
infrastructure needed.

**Risk acknowledged (original):** A crash in an in-process DLL can bring down the Explorer
process. Mitigated by static CRT (`/MT`), `catch (...)` on every COM method, and no mutable
global state. **This risk is why the correction below moved to `SurrogateServer` instead —
it removes the risk rather than just mitigating it.**

**Architecture (still current — only the manifest registration mechanism changed):**
- One registered CLSID: `PakkoRootCommand` (`1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A`), ThreadingModel STA.
- Sub-commands (`ExtractHereCommand`, `ExtractFolderCommand`, `ArchiveCommand`) returned at runtime
  via `IExplorerCommand::EnumSubCommands` — not separately registered in the manifest.
- Selection logic in `EnumSubCommands`:
  - All `.zip` → `[ExtractHereCommand, ExtractFolderCommand]`
  - All non-ZIP / mixed / null `psia` → `[ArchiveCommand]`
- `Invoke` launches `Archiver.Shell.exe` via `CreateProcess` with the correct argument set.

**Manifest registrations:**
- `desktop4:ItemType Type="*"` — all files, including `.zip` (no duplicate `.zip` entry)
- `desktop4:ItemType Type="Directory"` — folder selections (verify empirically)
- Both verbs reference the same `PakkoRootCommand` CLSID.

**Implementation notes:**
- WRL `RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>` for all command classes.
- Manual `DllGetClassObject` dispatch (no WRL `CoCreatableClass` macros); `Module<InProc>`
  used only for `DllCanUnloadNow` object counting.
- `ShellExtUtils.cpp` holds all COM-free logic (path extraction, argument building, CreateProcess)
  so it can be unit-tested independently.
- Google Test via `Microsoft.googletest.v143.windesktop.msvcstl.static.rt-static` NuGet package.

**Manifest registration note (superseded — see correction below):** `com:InProcessServer`
requires `com:Path` as a **child element** of the server (not a `Path` attribute on
`com:Class`). This schema was documented but never actually applied to `Package.appxmanifest`
— the shipped manifest instead had `Path` as an attribute on `com4:InProcessServer` and used
the `com4` namespace prefix for an element that belongs in `com`, on top of never declaring
`xmlns:com` at all (invalid XML — undefined namespace prefix). Also requires
`MinVersion="10.0.18362.0"` (Windows 10 1903) or higher in `TargetDeviceFamily`.

---

## Correction — SurrogateServer (2026-07-04)

**What broke:** the context menu never appeared for ~4 months. Root cause, in order of
severity:
1. `Package.appxmanifest` used the `com:` prefix (`com:Extension`, `com:ComServer`,
   `com:Class`) without ever declaring `xmlns:com` — only `xmlns:com4` was declared. Invalid
   XML namespace usage; the COM extension block never registered.
2. Even setting that aside, the element used (`com4:InProcessServer Path="..."`) matched
   neither the `InProcessServer` schema documented above (`com:Path` as child element) nor a
   valid `SurrogateServer` schema (`Path` attribute on `com:Class`) — a hybrid of both that
   is valid in neither.
3. `ThreadingModel="Both"` was used instead of the `STA` this codebase's WRL classes actually
   support (no `IMarshal`/free-threading support implemented).

**New decision:** Registered as `com:SurrogateServer` — `Path` as an attribute on `com:Class`,
inside `<com:ComServer><com:SurrogateServer>`. `xmlns:com` declared as
`http://schemas.microsoft.com/appx/manifest/com/windows10` (the v1 COM manifest namespace, not
`com4`).

```xml
<com:Extension Category="windows.comServer">
  <com:ComServer>
    <com:SurrogateServer DisplayName="Pakko Shell Extension">
      <com:Class Id="1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A"
                 Path="Archiver.ShellExtension.dll"
                 ThreadingModel="STA" />
    </com:SurrogateServer>
  </com:ComServer>
</com:Extension>
```

**Why SurrogateServer, verified against NanaZip:** fetched NanaZip's actual shipped manifest
(`github.com/M2Team/NanaZip`, `NanaZipPackage/Package.appxmanifest`, Store-published, in
production) — it registers its shell extension exactly this way: `com:SurrogateServer`,
`Path` attribute on `com:Class`, `ThreadingModel="STA"`, `xmlns:com` (not `com4`). This is the
schema this codebase's own earlier notes called "the previous failed attempt" — that
conclusion was wrong. The real 7-Zip/NanaZip-style implementations run the shell extension in
an isolated `dllhost.exe` surrogate process, which also resolves the in-process crash risk
noted above without extra mitigation code.

**GetState selection filtering:** `EnumSubCommands` does not receive `IShellItemArray` (the
COM interface has no selection parameter there), so per-command visibility must be implemented
in each leaf command's `GetState(psia, ...)` instead — return `ECS_HIDDEN` when the command
doesn't apply to the current selection, `ECS_ENABLED` otherwise. `PakkoRootCommand` continues
to unconditionally return all three leaf commands from `EnumSubCommands`; Explorer hides the
ones whose `GetState` returns `ECS_HIDDEN`. Implemented via the already-tested
`AllPathsAreZip`/`AnyPathIsZip` helpers in `ShellExtUtils.cpp`.

---

## Named Pipe Protocol (Shell ↔ ProgressWindow)

> **Superseded 2026-07-05** — see "Progress UI: IProgressDialog replaces Archiver.ProgressWindow"
> below. `Archiver.ProgressWindow` (and this named-pipe protocol) was removed entirely; kept
> here for history.

**Decision:** Newline-delimited UTF-8 JSON over `NamedPipeServerStream`.
- `Archiver.Shell` = server (creates pipe, runs operation)
- `Archiver.ProgressWindow` = client (connects, renders progress)
- Pipe name passed to `Archiver.ProgressWindow` via `--pipe <name>` command-line argument

**Message types:** `progress` (percent, speed, ETA), `complete`, `error`, `cancel` (client → server).

---

## Progress UI: IProgressDialog replaces Archiver.ProgressWindow

**Decision:** `Archiver.Shell` shows progress via the Windows Shell's built-in
`IProgressDialog` COM object (`CLSID_ProgressDialog`, `shell32`), wrapped in
`Archiver.Shell/NativeProgressDialog.cs`, called in-process. The separate
`Archiver.ProgressWindow` project (a second independent WinUI 3 `.exe` communicating over a
named pipe) was deleted along with its `Content Include` entries in `Archiver.App.csproj`,
its `<Application Id="ProgressWindow">` manifest entry, and its `Deploy.ps1` build step.

**Why:** T-F65 set out to fix a theorized `App.xbf` resource collision between
`Archiver.ProgressWindow.exe` and `Archiver.App.exe` (two independent WinUI 3 apps' compiled
XAML sharing one package). That theory was disproven empirically — the crash (`Application
Error`, `Microsoft.UI.Xaml.dll`, exception `0xc000027b`/`STATUS_STOWED_EXCEPTION`, same
faulting offset) was **byte-for-byte identical** before and after rewriting
`Archiver.ProgressWindow` to use zero XAML files (see the T-F65 entry above this one for the
full disproof). The real cause was never confirmed (would need a WinDbg/`dotnet-dump`
`!analyze -v` on a crash dump, not available in the diagnosing environment), but the evidence
pointed at WinUI3/WindowsAppRuntime framework activation failing for a
`Windows.FullTrustApplication` satellite spawned via raw `Process.Start` rather than proper
shell/user activation — `Archiver.App.exe` (WinUI, but always shell-activated) and
`Archiver.Shell.exe` (`Process.Start`'d, but no WinUI) each work individually; only "WinUI +
`Process.Start`" fails.

**Verified against a real reference (per `CLAUDE.md`'s pre-implementation-research rule):**
fetched `M2Team/NanaZip`'s actual source. `NanaZip.Modern.cpp` is **not an executable** — it's a
DLL exporting plain C functions (`K7ModernShowProgressWindow`, `K7ModernShowAboutDialog`, ...)
that the caller (the classic file-manager/shell-extension process) invokes in-process;
`ProgressPage.xaml` is a `Page` navigated within NanaZip.Modern's single `App.xaml`, never a
second `Application`/`Window` in a separately spawned process. NanaZip never hits this failure
mode because it never creates it: no second CreateProcess'd WinUI app exists in its design.

**Fix:** don't spawn a second process at all. `IProgressDialog` is a plain COM object (not
WinUI/WindowsAppRuntime) that `Archiver.Shell.exe` creates directly via
`(IProgressDialog)new ProgressDialogCoClass()` — it renders on its own internal worker thread
inside the same process, matching the in-process principle from NanaZip's design without
needing NanaZip's C++/WinRT XAML-island machinery (out of proportion for this project — see
`NativeProgressDialog.cs` for the full COM interop and `Program.cs`'s `RunWithProgressWindowAsync`
for the call site). Cancellation maps onto `IProgressDialog.HasUserCancelled()`, polled from the
existing `IProgress<ProgressReport>` callback, feeding the same `CancellationToken` the archive
services already accept — no new IPC or protocol needed.

**Rejected:**
- Fixing `Archiver.ProgressWindow` in place (subfolder + duplicated self-contained runtime, or
  merging its XAML into `Archiver.App`'s own PRI) — both require either re-litigating the
  `.wapproj` rejection (see "MSIX Satellite EXE Packaging" above) or custom MSBuild/PRI-merge
  tooling this project's conventions rule out.
- Replicating NanaZip's C++/WinRT XAML-island-in-`dllhost.exe` approach exactly — technically
  the most faithful fix, but requires native XAML-island hosting inside
  `Archiver.ShellExtension.dll`'s COM surrogate process, a disproportionate rewrite for a
  progress bar.

---

## Test Archive (T-F62)

**Decision:** Added `IArchiveService.TestAsync(IReadOnlyList<string> archivePaths, ...)`,
reusing the existing `ArchiveResult` model (CRC mismatches → `Errors`, unsupported formats →
`SkippedFiles`, `CreatedFiles` always empty). No new `TestResult` model was introduced.

**Why no new model:** `ArchiveResult` already expresses "some items failed, here's why" without
committing to disk — the exact shape Test needs. `IArchiveService` has exactly one implementation
(`ZipArchiveService`), so widening the interface carries no compatibility risk.

**Verified against a real reference (per `CLAUDE.md`'s pre-implementation-research rule):**
fetched `M2Team/NanaZip`'s actual `NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`. Its `Test`
command (`CommandID::Test`, `IDS_CONTEXT_TEST`) is gated on the same `NeedExtract` flag used by
`Extract`/`ExtractHere` — true when **any** selected item needs extraction, not all — and is
invoked with the full, unfiltered selection (`TestArchives(FileNames)`); NanaZip's underlying
engine skips non-archive entries itself rather than the shell layer pre-filtering. Pakko mirrors
both: `TestCommand::GetState` uses the already-present-but-previously-unused `AnyPathIsZip`
helper (contrast `ExtractHereCommand`/`ExtractFolderCommand`, which use `AllPathsAreZip`), and
`TestCommand::Invoke` passes the full selection to `Archiver.Shell.exe --test`, relying on
`ZipArchiveService.TestAsync`'s own `IsZipFile`/`GetKnownArchiveReason` gating (the same gating
`ExtractAsync` already uses) to skip non-archives — no new C++-side filtering helper needed.

**CRC-32 is hand-rolled, not `System.IO.Hashing.Crc32`:** that type ships in a NuGet package,
and `Archiver.Core` takes zero NuGet dependencies (`CLAUDE.md` hard constraint).
`ZipArchiveEntry.Crc32` only exposes the value declared in the entry's header — .NET's
`System.IO.Compression` never validates an entry's decompressed bytes against it on read, so
a bit-flipped-but-structurally-valid entry extracts "successfully" with silently wrong content
unless something explicitly checks. `Archiver.Core/IO/Crc32.cs` is a ~40-line table-based
implementation of the standard ZIP/PNG polynomial (`0xEDB88320`), self-contained.

**Fixture:** `corrupted_crc_stored.zip` uses a `CompressionLevel.NoCompression` (Stored) entry
with a single data byte flipped *after* the ZIP was written. The two pre-existing corrupted
fixtures (`corrupted_entry_data.zip`, `corrupted_central_directory.zip`) break the Deflate
stream or the EOCD signature respectively — both tend to throw on read rather than produce a
clean "reads fine, CRC is wrong" case, which is the specific scenario `TestAsync` exists to catch.

**Menu order — deviates from NanaZip by explicit project direction:** `TestCommand` is enumerated
**last** in `PakkoRootCommand::EnumSubCommands` (after Extract here/Extract to folder/Add to
archive), not first as NanaZip's Open → Test → Extract → Compress ordering would suggest. Per
project direction: Test is a diagnostic/verification action, not a primary one, and primary
actions (Extract/Archive) must always precede it in the menu.

---

## T-F75 — Correctness Bug: Nested Subdirectory Entries Lost Their Path Prefix

**Found while investigating T-F30** (duplicate filename detection) — writing a throwaway trace
test to check current archive-entry-naming behavior before adding dedup logic surfaced this
independently, more severe bug. **Confirmed shipped in the tagged `v1.1.0` release** — any
archive of a directory containing subdirectories nested two or more levels deep has always
produced structurally wrong entry names.

**Root cause:** `AddDirectoryToArchiveAsync` computed each file's ZIP entry name as
`Path.GetRelativePath(Path.GetDirectoryName(sourceDir)!, filePath)` — relative to the CURRENT
recursion level's own immediate parent, recomputed fresh at every level. This is correct only at
the top level. One level down, the relative-path base has already shifted to the subdirectory's
own parent, so the entry name loses the entire prefix accumulated so far. Traced concretely:
archiving `notes/` containing `notes/readme.txt` and `notes/sub/file.txt` produced ZIP entries
`notes/readme.txt` (correct) and `sub/file.txt` (**missing the `notes/` prefix entirely**) —
verified via a real `ArchiveAsync` → `ZipFile.OpenRead` round trip, not inferred from reading the
code.

**Blast radius — silent data loss, not just misplaced files:** because the relative-path base
resets at every level to "the current directory's own parent" rather than the true archived
root, two files at different nesting depths can produce the *same* entry name whenever their
paths relative to their own immediate parent happen to match — e.g. `notes/a/file.txt` and
`notes/b/a/file.txt` both reduce to entry name `a/file.txt`. `ZipArchive.CreateEntry` does not
reject duplicate names, so both are written; on extraction, the second write clobbers the first.
Confirmed via `ArchiveAsync_SiblingSubdirectoriesWithMatchingRelativeStructure_NoEntryCollision`
in `ZipArchiveServiceArchiveTests.cs` that this collision is now closed.

**Fix:** `AddDirectoryToArchiveAsync` gained a `rootDir` parameter — the original top-level
directory being archived — held **fixed** across every recursion level, alongside the existing
`entryPrefix` (the archived folder's own, possibly-deduplicated, top-level name). Every entry
name is now `entryPrefix + "/" + Path.GetRelativePath(rootDir, filePath)`, computed against the
one true root regardless of recursion depth. The empty-subdirectory special case (T-F66) had the
identical bug (`Path.GetFileName(sourceDir) + "/"` with no prefix) and is fixed the same way.

**Test that had codified the bug, now corrected:**
`ArchiveAsync_FolderWithEmptySubfolder_PreservesEmptySubfolderEntry` asserted the ZIP contained
an entry named exactly `EmptyChild/` — this was the bug's own output, asserted as if it were
correct. Updated to expect `Parent/EmptyChild/`. No other existing entry-name assertion in
`ZipArchiveServiceArchiveTests.cs` encoded the bug (the "SameDirectoryTwice" tests only assert
run-to-run consistency and ascending order, never the actual prefix content).

**Why no existing test caught this:** no test exercised `ArchiveAsync` on a directory nested two
or more levels deep and then asserted the *exact* entry names or performed a structural
round-trip comparison — exactly the gap T-F24 (property-based round-trip integrity testing) is
meant to close. Implementing T-F24 was deferred until after this fix landed, so it validates
correct behavior rather than locking in the bug.

**Action item flagged to user (not yet decided as of this writing):** whether this warrants a
v1.1 patch release notice, given early testers may have archives with silently-misplaced or
partially-overwritten nested content.

---

## T-F20 — Slow Test Convention (`[Trait("Category", "Slow")]`)

**Decision:** T-F20's Zip64 tests (`ZipArchiveServiceZip64Tests.cs`) are tagged
`[Trait("Category", "Slow")]` and excluded from the routine `dotnet test` invocation via
`--filter "Category!=Slow"`. This is the **first use of this convention in the repo** — every
other project hard constraint says "always run `dotnet test` with no path argument," full stop.

**Why:** Measured, not assumed. The >65535-file archive/extract tests took ~30s *each*
(~65s combined) — tried parallelizing the file-creation loop first (`Parallel.For`), which only
shaved ~6s off, confirming the real cost is `ZipArchiveService` processing tens of thousands of
individual ZIP entries (traversal, sorting, per-entry writes), not the file-creation loop itself.
That's not fixable without changing production code for a test's sake, and it's well past a
"routine `dotnet test`" cost. The >4 GiB round-trip test adds real multi-GB disk I/O on top.

**Sparse-file technique for the >4 GiB case:** `CreateSparseFileOver4Gb` marks a file NTFS-sparse
via `DeviceIoControl(FSCTL_SET_SPARSE)` (P/Invoke, test-only — this is not production
`Archiver.Core` code, so it doesn't touch the "P/Invoke reserved for v1.4 Low IL sandbox" hard
constraint) before calling `SetLength` past 4 GiB — the filesystem returns zeros for the
unallocated range on read without real disk I/O, so generating the *source* file is fast. The
resulting `.zip` itself is still a real ~4 GiB file on disk (transient, cleaned up by
`TempDirectory.Dispose`): archived with `CompressionLevel.NoCompression` (Stored) rather than
Deflate, because an all-zero source compresses at a ratio our own ZIP-bomb check
(`MaxCompressionRatio = 1000`) would reject on extract, and Stored also avoids spending CPU time
compressing 4 GiB of zeros just to prove Zip64's size handling works. Falls back to skipping
(not failing) if `FSCTL_SET_SPARSE` isn't supported on the volume (e.g. FAT32).

**Convention going forward:** `[Trait("Category", "Slow")]` is now the repo's mechanism for
"this test is correct and worth having, but too expensive to run on every change." Use it only
when a cheaper test design has genuinely been ruled out (as here) — it is not a shortcut around
writing a faster test. `CLAUDE.md`'s hard constraint, `TASKS.md`'s Agent Rules, and `TESTING.md`
were all updated to `dotnet test --filter "Category!=Slow"` as the routine command, with
`--filter "Category=Slow"` required before a release or when a change touches Zip64-adjacent
code.

---

## T-F79 — No Custom Brand Palette

**Decision:** Pakko will **not** adopt a custom brand color palette. The app stays on the
native WinUI 3 dark/light theme with the user's system accent color as the only accent (already
the case on the primary Archive button) — no app-specific hex values, no custom color resource
dictionary.

**Why:** Pakko's audience is Ukrainian government/defense users, where the whole value
proposition is trust and auditability over third-party/branded tooling (`CLAUDE.md`'s Project
section, `SECURITY.md`'s threat model). For that audience, native OS chrome fidelity *is* a trust
signal — the app looking exactly like "a Windows dialog" is reassuring, because it means nothing
is being visually dressed up or hidden. A bespoke palette would read as "skinned," which is the
opposite signal: it invites the question "what else did they customize that I can't see." This
was raised as a possible gap during a 2026-07-06 design review pass and considered explicitly
before being rejected — recorded here per `CLAUDE.md`'s "never silently deprecate" spirit, so a
future session doesn't "fix" the lack of branding as an oversight.

**What Pakko invests in instead:** restraint and typographic hierarchy using existing WinUI
resources (T-F78: `CaptionTextBlockStyle` for field labels) and contextual clarity — showing only
the options relevant to the current selection (T-F77) — rather than a visual identity layer.

**Rejected:** a themed accent color (e.g. a fixed blue/teal regardless of system accent) — still
rejected for the same reason; system accent is the one piece of "color" that is itself a Windows-
native, user-controlled signal, not a Pakko brand choice.

**Cascade check:** `SECURITY.md` and `SPEC.md` reviewed — neither currently makes claims about
visual trust signals beyond what's already covered by the general trust/auditability framing, so
no change needed there.

---

## T-F77 / T-F81 — Contextual Option Visibility and the Outcome Subtitle

**Decision:** Archive-only fields (`Mode`, `Name`, `Compression`) **collapse** (not grey out) via
a plain `Visibility` binding (`ArchiveOptionsVisibility`) when the current selection is
extract-only. A selection counts as extract-only **only if every selected item is a `.zip`** — a
single non-`.zip` item keeps the panel in archive-mode, because bundling a `.zip` together with
other files into one new archive is still a coherent action, while "extract" has no meaning for a
mixed set. The Archive/Extract button pair (T-F81) keeps both buttons visible and greyed out as
before, paired with a new one-line outcome subtitle (`OperationOutcomeText`, e.g. "Will extract 2
archive(s) to the folder above") shown under the button row whenever the file list is non-empty.

**Why:** Consulted the project's design-review framing (native WinUI fidelity as a trust signal
for a gov/defense audience, `CLAUDE.md`'s Project section) for three sub-decisions:
- **Collapse over grey-out:** an absent field reads as "does not apply right now"; a greyed-out
  field still asks the user to notice and dismiss it. Absence is the clearer signal for an
  audience that reads predictability as trust. No animation — the collapse is an instant reaction
  to a selection change the user just made, not decorative motion (consistent with T-F77's
  original "don't hide fields with elaborate animation" direction).
- **Strict all-`.zip` rule for "extract-only":** the alternative (any `.zip` present flips the
  whole selection to extract-mode) would hide Mode/Name/Compression for a selection where
  Archive is still the only action that makes sense — the archive-only fields must never
  disappear while Archive is the intended operation.
- **Grey-out + subtitle over hiding the inactive button:** hiding a button makes the remaining
  one jump position/width on every selection change, which is more disruptive for a "nothing
  moves unless I did something deliberate" audience than a stable disabled twin. The subtitle
  reuses the same "structure states the outcome" mechanism as the collapse decision and the
  empty-state hint (T-F80) — one mechanism applied consistently, not a new one per problem.

**Implementation:** `MainViewModel.IsExtractOnlySelection` (`FileItems.Count > 0 &&
FileItems.All(x => x.Type == "ZIP")`), `ArchiveOptionsVisibility`, `OperationOutcomeVisibility`,
and `OperationOutcomeText` (resource strings `OutcomeWillExtract`/`OutcomeWillArchive`) — all
plain computed properties following the existing `IsFileListEmptyVisibility` pattern, notified via
the `FileItems.CollectionChanged` handler. No change to `CanArchive`/`CanExtract`/`ArchiveAsync`/
`ExtractAsync` — presentation only, per both tasks' acceptance criteria.

---

## T-F68 — Shell Extract Silently Ignoring SkippedFiles

**Decision:** Widen only the shell path's dialog trigger to also fire when
`result.SkippedFiles.Count > 0`. Do **not** change `ArchiveResult.Success` (stays
`Errors.Count == 0`, unchanged everywhere it's read).

**Why:** Two options were on the table — make `Success` itself account for `SkippedFiles`, or
widen only the shell's trigger condition. `Success` already has a GUI-facing meaning ("no hard
failures") that several call sites (button state, status text) depend on; a skip-only run (e.g.
one bad entry rejected, rest extracted fine) is not a *failure*, so folding skips into `Success`
would be a semantic change with GUI-wide blast radius for a bug that's actually localized to one
`Program.cs` conditional. The GUI's `DialogService.ShowOperationSummaryAsync` already gates on
`Errors.Count == 0 && SkippedFiles.Count == 0` together — i.e. the GUI already shows skips
regardless of `Success`. The shell path just never learned that pattern when it was written
against the older `!Success || Errors.Count > 0` check. Matching the shell's trigger to the GUI's
existing gate is the minimal fix and needs no change to `ArchiveResult`.

**Message distinction:** errors keep the existing "operation failed" (`MB_ICONERROR`) dialog
unchanged. A new skip-only outcome gets its own `MB_ICONWARNING` dialog reading "N entries
skipped:" followed by the same `path: reason` line format `ShowErrorSummary` already uses. If both
errors and skips are present, the error dialog wins (unchanged behavior) — the run is already
non-silent in that case, so covering the pure-skip gap was the actual bug.

**Implementation:** new `ShellResultPresenter` class (`src/Archiver.Shell/ShellResultPresenter.cs`)
holds the classification (`Classify(ArchiveResult)`) and message-building
(`BuildSkippedMessage(IReadOnlyList<SkippedFile>)`) as pure, unit-testable static methods —
same pattern `ShellArgumentParser` established for T-F57, since `Program.cs`'s top-level-statement
local functions aren't reachable from `Archiver.Shell.Tests`.

---

## T-F83 — Cold-Start Protocol/File Activation Never Reached OnActivated

**Found while manually verifying T-F63** (`ExtractDialogCommand`/`CompressDialogCommand`): invoking
"Extract…" from a fresh Explorer right-click launched `Archiver.App` with a completely empty file
list — the `pakko://extract?files=...` payload was silently dropped. Reproduced independently of
the shell extension via a direct `Start-Process Archiver.Shell.exe --open-ui --extract "<file>"`,
so the bug is in `Archiver.App`, not in the new T-F63 code. `%LOCALAPPDATA%\Packages\...\Pakko\logs\
pakko.log` showed `"Pakko started"` (the plain `OnLaunched` log line) instead of `"Pakko started via
protocol activation"` for both repro attempts — proof `OnActivated` never ran.

**Root cause:** `App.xaml.cs`'s constructor did `AppInstance.GetCurrent().Activated += OnActivated;`,
but per the Windows App SDK's documented behavior, `AppInstance.Activated` only fires for activation
requests *redirected* to an already-running instance — it is never raised for the process's own
initial (cold-start) activation. `OnLaunched(LaunchActivatedEventArgs)` — the plain WinUI XAML
override — fired instead, which built a blank `MainWindow` and never inspected the File/Protocol
payload at all. This is why the bug went unnoticed until now: `dotnet build` and unit tests can't
catch it, and every previous manual smoke test of protocol/file activation (T-F44, T-F56) happened
to run while a Pakko window was already open (warm/redirected path, where `Activated` *does* fire),
or was verified only at the build/URI-construction level rather than by watching the file actually
appear in the UI. T-F56's and T-F44's acceptance-criteria checklists have no explicit "the file list
is visibly populated after a cold launch" smoke-test line — a real gap in how "complete" was
verified for both, not just an incidental omission.

**Fix:** pulled the `switch (args.Kind)` File/Protocol handling out of `OnActivated` into a shared
`HandleActivation(AppActivationArguments, string defaultLogMessage)`, called from both entry points:
- `OnActivated` (warm/redirected) passes the event's own args, as before.
- `OnLaunched` (cold) now calls `AppInstance.GetCurrent().GetActivatedEventArgs()` to pull the
  process's actual initial activation kind, instead of ignoring `LaunchActivatedEventArgs` and
  always building a blank window.

Both entry points are mutually exclusive per process (a given process either cold-starts once or
receives redirected activations later, never both for the same activation), so there is no
double-handling risk.

**T-F44 status — reverified 2026-07-06:** the user set Pakko as the default `.zip` handler on this
machine (`UserChoice` ProgId resolved to Pakko's AppX ProgId, `ApplicationName = Pakko`) and a
cold-start file activation was reproduced via `Start-Process` on a test ZIP (no Pakko process
running beforehand, confirmed via `Get-Process`). `pakko.log` recorded `"Pakko started via file
activation"` (previously just `"Pakko started"`), and `ui_read` over the resulting window confirmed
the file (`tf83coldstart.zip`, 194 bytes) was visibly populated in the list with "Will extract 1
archive(s)..." shown — the exact "file list is visibly populated after a cold launch" check T-F44
was missing. T-F44's cold-start claim is now confirmed working, not just unverified.

---

## T-F70 — IsBusy vs. Status-Text Timing After Cancel vs. Success/Error

**Decision:** align — `IsBusy` now stays `true` for exactly as long as *something transient is still
on screen* for all four operation outcomes, not just three of them. Fixed by moving `IsBusy = false`
out of the `finally` block in both `ArchiveAsync`/`ExtractAsync` and placing it immediately before
the final `StatusMessage = "Ready"` line — after the `if (wasCancelled) await Task.Delay(2000)`.

**Why:** the success/issues/error paths already await a modal dialog (`ShowOperationSummaryAsync`/
`ShowErrorAsync`) *inside* the `try`, before `finally` runs — so `IsBusy` blocks new operations for
exactly as long as that dialog is up. The cancelled path showed no dialog and released `IsBusy`
immediately in `finally`, then displayed "Cancelled" for a further fixed 2 seconds with the UI
already re-enabled — the only outcome where a new operation could start while the previous one's
result text was still on screen. Per the project's own established direction (T-F77/T-F81's
"structure states the outcome" principle, and the gov/defense audience's preference for
predictability over motion, `CLAUDE.md`'s Project section), the asymmetry was accidental, not
deliberate, and the more consistent behavior — never re-enable controls while a result is still
being displayed, dialog or plain text — was chosen over documenting the asymmetry or dropping the
2s delay entirely.

**Files:** `src/Archiver.App/ViewModels/MainViewModel.cs` (`ArchiveAsync`, `ExtractAsync`)

**Verification note:** the exact 2-second window was confirmed by code inspection (the line move is
mechanical and unambiguous), not by visually catching the race through remote UI automation — each
tool round-trip in this session's harness reliably exceeded 2 seconds, so by the time any
post-cancel screenshot could be taken, the window had already elapsed regardless of whether the fix
was in place. Functional behavior (Cancel still stops the operation cleanly, no crash, UI reaches
"Ready" afterward) was confirmed live.

---

## T-F84 — Bug: Deploy.ps1's Post-Build Hook Fails on Cyrillic-Locale Machines (Mojibake, 3rd Language)

**Found while:** verifying T-F47/T-F48 compiled cleanly under a real Visual Studio Release build
(requested since `dotnet build`/`dotnet test` cannot build `Archiver.App`, a WinUI 3 project).
`Archiver.App`'s post-build event auto-runs `Deploy.ps1 -DeployOnly` (documented in `CLAUDE.md`);
the build reported `MSB3073: The command "powershell.exe ... Deploy.ps1" ... exited with code 1`.

**Root cause:** `scripts/Deploy.ps1` line 204 (`Write-Warning "... Package.appxmanifest —
skipped version bump."`) contains a literal em-dash inside a double-quoted string literal, and the
file is saved as UTF-8 **without a BOM**. Windows PowerShell 5.1 (`powershell.exe`, required by this
script's `#Requires -Version 5.1`) has no BOM to detect UTF-8 from, so on this Cyrillic-locale
machine it decodes the file via the system ANSI code page (cp1251) instead — the em-dash's UTF-8
bytes (`E2 80 94`) misdecode into `вЂ”`, and one of the resulting characters breaks the string's
terminator, cascading into `TerminatorExpectedAtEndOfString` / `Missing closing '}'` parser errors
at the *reported* lines 203/186 (both downstream symptoms, not the real location). Confirmed by
running `Deploy.ps1 -DeployOnly` directly and reading the full PowerShell error text (VS's Output
window truncates/wraps this badly enough that a direct terminal repro was clearer than screen
scraping it). This is the same **mojibake bug class already documented three times in this
project's C++ code** (T-F64, T-F76, T-F63 — see `CONVENTIONS.md`'s non-ASCII string-literal rule)
— the first known occurrence in a PowerShell script rather than C++.

**Fix:** replaced the em-dash with a plain ASCII hyphen (`"... appxmanifest - skipped version
bump."`). `grep -P "[^\x00-\x7F]"` run over every `scripts/*.ps1` file (not just `Deploy.ps1`)
found one more live instance: `Setup-DevCert.ps1` line 21, `Write-Host "Not running as
Administrator — relaunching elevated..."` — arguably higher-risk than `Deploy.ps1`'s, since that
script explicitly relaunches itself via `Start-Process powershell` (Windows PowerShell, the exact
vulnerable interpreter) when not elevated. Fixed the same way. The many em-dash/box-drawing
characters used as comment dividers throughout both files (`# ── Paths ──...`) don't affect
parsing (comments are skipped verbatim regardless of how their bytes decode) and were deliberately
left alone, per "minimal diff, don't touch unrelated content."

**Considered and rejected:** re-saving the file as UTF-8-with-BOM instead of removing the
character. Rejected for the same reason `CONVENTIONS.md` prefers `\uXXXX` escapes over BOM
discipline in C++ — an encoding fix is fragile (any future save without preserving the BOM,
by any editor or tool, silently reintroduces the bug), while removing the non-ASCII character is
immune to encoding regardless of how the file is later saved. Windows PowerShell 5.1 also has no
backtick-`u{}` Unicode escape (that requires PowerShell 6.2+/pwsh core), so an escape-sequence fix
analogous to the C++ one wasn't available here.

**Why it hid until now:** the bug only manifests when the file is read by Windows PowerShell 5.1
(`powershell.exe`) on a non-UTF-8-default-locale machine. pwsh 7+ defaults to UTF-8 regardless of
system locale, so every manual `.\Deploy.ps1`/`.\Setup-DevCert.ps1` run from a pwsh 7 terminal
(this project's usual dev workflow) read the file correctly. The Release-only Visual Studio
post-build hook — which shells out via `powershell.exe` specifically — was the first path to
actually exercise the vulnerable interpreter.

**Verification:** both fixes confirmed against the *actual* vulnerable interpreter, not pwsh 7
(which would have reported "no parse errors" on the broken files too, since it doesn't hit the
bug) — `powershell.exe -NoProfile -Command "[...]::ParseFile(...)"` reports zero parse errors for
both files after the fix. `Deploy.ps1 -DeployOnly` run directly (via `powershell.exe`) completed
successfully (`Pakko installed successfully`, version 1.1.0.42); a subsequent Visual Studio
Release build of the full solution completed with 0 errors/0 warnings.

**Files:** `scripts/Deploy.ps1`, `scripts/Setup-DevCert.ps1`

---

## T-F49 — tar.exe Extraction Pipeline: Symlink Escape Confirmed, Whole-Archive Pre-Scan Chosen

**Found while:** designing `TarProcessService.ExtractAsync()`. Per `CLAUDE.md`'s
"Pre-implementation research" hard constraint, verified `C:\Windows\System32\tar.exe`'s actual
extraction behavior against hand-crafted malicious tar archives before writing any extraction
code, rather than assuming `SECURITY.md`'s existing quarantine-then-validate sketch (written for
ZIP, where entries are inspected before being written) transfers unchanged to tar.exe (an
external process that writes whatever it decides to, with no per-entry pre-write hook available
to Pakko).

**Method:** built raw tar archives byte-for-byte (512-byte USTAR headers, no real files, no
third-party tooling) via a throwaway PowerShell script, then ran the actual
`C:\Windows\System32\tar.exe` (bsdtar 3.8.4, libarchive 3.8.4, confirmed via `tar --version`)
against them with `-tf`, `-tvf`, and `-xf -C <quarantine>`.

**Findings:**

1. **Symlink entries are created and then written through, escaping the extraction root.** An
   archive containing a typeflag-`2` entry named `link` with linkname `..`, followed immediately
   by an entry named `link/escaped.txt`, caused tar.exe to create `link` as an NTFS reparse point
   *inside* the quarantine directory, then write `escaped.txt` through it — landing the file
   **one directory level above the quarantine root**, entirely outside the extraction sandbox.
   Confirmed by direct inspection: `quarantine\link` had `FileAttributes.ReparsePoint`, and
   `escaped.txt` (with the exact 20-byte payload) appeared in quarantine's parent directory.
   `ARCHIVE_EXTRACT_SECURE_SYMLINKS` (libarchive's usual guard against exactly this) is not
   effectively blocking it on this Windows build — plausibly because the check is written for
   POSIX symlink semantics and doesn't fully cover NTFS reparse points, though the exact reason
   wasn't traced further since it doesn't change the mitigation.
2. **Path-traversal (`..`) entries are already rejected by tar.exe itself** — extracting an
   entry named `../evil.txt` produces `../evil.txt: Path contains '..': Unknown error` and the
   file is never written. Safe as-is; no extra mitigation needed for this specific case.
3. **Absolute/rooted paths are sanitized, not rejected** — an entry named
   `C:/Windows/Temp/pakko_abs_test.txt` extracts as `Windows\Temp\pakko_abs_test.txt` *inside*
   the destination (`tar.exe: Removing leading drive letter from member names`), confirmed to
   stay contained (`C:\Windows\Temp\pakko_abs_test.txt` itself was not created).
4. **tar.exe does not abort extraction on a bad entry.** It logs the error and continues
   processing subsequent entries, only returning a nonzero exit code at the very end
   (`tar.exe: Error exit delayed from previous errors.`). Proven directly: in the symlink-escape
   run, `innocent.txt` (a preceding valid entry), the `link` reparse point, and the escaped file
   all landed on disk despite the process exiting with code 1. **This means exit-code/error-based
   reactive handling cannot prevent an escape** — by the time Pakko's code observes the error,
   the damaging write has already happened. Any mitigation must act *before* calling `-xf`.
5. **`tar -tvf`'s entry-type character (column 0 of each output line) is reliable across
   locales; the rest of the line is not.** The date column renders using the system locale and
   was observed mangled on this (Cyrillic-locale) machine (garbled month abbreviation), matching
   the same locale-decoding bug class as T-F84. The leading type character (`-` regular, `d`
   directory, `l` symlink, `h` hardlink) is rendered deterministically by libarchive from the
   entry's typeflag and was not affected. `tar -tf` (plain name list, one per line, no type or
   date info) is separately clean and was used for name-based checks.
6. **Hardlink entries pointing at a nonexistent target fail cleanly** (`Hard-link target
   'innocent.txt' does not exist.`) without escaping — but a hardlink to an *existing* file
   elsewhere on disk was not tested (out of scope once symlinks alone proved the whole-archive
   pre-scan was necessary regardless).

**Decision:** `TarProcessService.ExtractAsync()` performs a **whole-archive pre-scan and
reject** before ever invoking `-xf` — not the ZIP path's per-entry skip-and-continue model (see
`ARCHITECTURE.md`'s `ITarService` section for the resulting pipeline shape):
- `tar -tf` lists all entry names; any name containing `..`, rooted (leading `/`, `\`, drive
  letter, or UNC `\\`), carrying an ADS colon, matching a reserved Windows device name, or
  containing control characters → the **entire archive** is rejected with one `ArchiveError`
  (not sanitized, not skipped per-entry — defense-in-depth: reject outright rather than trust
  tar.exe's own partial sanitization from finding #2/#3 above).
- `tar -tvf` is scanned for entry types; any character-0 value other than `-`/`d` (i.e. any
  symlink, hardlink, device, fifo, or socket entry) → the entire archive is rejected.
- Only after both scans pass clean does `-xf -C <quarantine>` run. The quarantine directory
  and its post-extraction walk still exist (same same-disk/atomic-move pattern as
  `ZipArchiveService`) for defense-in-depth and consistent commit/conflict/MOTW handling, but
  they are **not** the primary safety mechanism against escape — the pre-scan is, per finding #4.

**Rejected: `System.Formats.Tar` (BCL).** .NET 8 ships an in-process, dependency-free tar reader
that would sidestep the type-detection fragility entirely (structured `TarEntryType` instead of
parsing a CLI output column). Not adopted here: `SECURITY.md`'s tar.exe Trust Model section
explicitly commits to "tar.exe process, not an in-process parser" as the threat-model boundary
for non-ZIP formats (see "No format parsers beyond ZIP" in that doc). Swapping to an in-process
parser — even a BCL one — reverses that documented boundary and is a decision for the project
owner to make deliberately, not something to slide in as a T-F49 implementation detail. Flagged
here for future consideration; not implemented.

**Rejected: parsing more of `-tvf`'s columns** (permissions, link count, size) for additional
signal. Only column 0 is used, deliberately — every other column risks the same locale/whitespace
fragility that already broke the date column, and a single reliable character is all the design
needs.

**Files:** `src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.Core/Services/ArchiveEntrySecurity.cs`, `SECURITY.md` (Trust Model cross-reference),
`tests/Archiver.Core.IntegrationTests/`

---

## T-F87 — Bug: `DeleteAfterOperation` Could Delete a Source That Was Only Skipped, Not Processed

**Found while:** advisor-reviewing T-F85, then confirmed as a pre-existing gap on both the
Archive and Extract sides, not something T-F85 introduced (T-F85 only made the Extract-side
instance far more reachable, by routing RAR/7z/tar formats through `IsExtractOnlySelection`'s
"will extract" UI framing).

**Root cause:** `MainViewModel.ArchiveAsync`/`ExtractAsync` both gate `RunCleanupAsync` (which
deletes the operation's source paths when `DeleteAfterOperation` is checked) on `result.Success`
alone. `ArchiveResult.Success` is `errors.Count == 0` and does not look at `SkippedFiles` — so an
archive/extraction that was entirely skipped, not processed at all, still reports `Success=true`.
Three concrete ways this happened, found by reading each conflict-skip branch directly rather than
assuming symmetry:
1. `ZipArchiveService.ArchiveAsync`'s `SingleArchive` mode returned an early, bare
   `new ArchiveResult { Success = true, CreatedFiles = [], Errors = [] }` when the destination
   already existed and `OnConflict == Skip` — no `SkippedFiles` entry at all.
2. `ZipArchiveService.ArchiveAsync`'s `SeparateArchives` mode hit the identical conflict but
   `continue`d silently per source path, again with no `SkippedFiles` entry.
3. `ZipArchiveService.ExtractAsync`/`TarProcessService.ExtractAsync`: when every entry inside an
   archive individually conflict-skipped (`OnConflict == Skip`, all entries already exist at the
   destination), the per-entry skip path itself records nothing (`ZipArchiveService.cs`'s
   `S6`/conflict branch, `TarProcessService`'s equivalent), and the caller unconditionally added
   the archive's destination folder to `CreatedFiles` regardless of whether anything was written.
   See `DIAGRAMS.md` diagrams 3 and 5 (nodes `N`/`Q` before this fix) — both already documented
   this exact `Success`/`SkippedFiles` asymmetry as a finding, from redraws in 2026-07-05/07-07,
   before it was fixed here.

Concretely dangerous case: a `.rar` on a pre-Windows-11-23H2 machine now routes through
`IExtractionRouter` to an `unsupported`-format `SkippedFiles` entry (T-F85) rather than being
extracted. With "delete after extraction" checked, the `.rar` would be deleted having never been
extracted — data loss, and the exact reason this bug was escalated from "someday" to "now."

**Decision — fix the cleanup gate, not `ArchiveResult.Success`'s definition.** Widening `Success`
to also check `SkippedFiles.Count == 0` was considered and rejected: `Success` currently means
"no errors" and every caller (status-message branching in `ArchiveAsync`/`ExtractAsync`, the shell
path's `ShellResultPresenter.Classify`, `ExtractionRouter`'s merge) depends on that exact meaning.
Redefining it to also mean "nothing was skipped" is a broad blast-radius change for a fix that
only needs to answer one narrower question: *was this specific source actually processed?*

**Fix — per-source whole-item `SkippedFiles` entries, filtered at the ViewModel:**
- Both `ZipArchiveService.ArchiveAsync` conflict-skip branches (`SingleArchive` and
  `SeparateArchives`) now add a `SkippedFile { Path = <sourcePath>, Reason = "..." }` for every
  source that was skipped, instead of silently continuing or returning bare.
- `ZipArchiveService.ExtractWithSmartFolderingAsync` and
  `TarProcessService.ExtractSingleArchiveAsync` now track how many entries were actually written
  (`extractedCount`). If an archive had entries but none were extracted, a whole-archive
  `SkippedFile { Path = archivePath, ... }` is added and the method reports `AnyExtracted = false`
  (return type changed from `Task<string>` to `Task<(string ActualDest, bool AnyExtracted)>` —
  both are private methods, no public-API impact). The caller (`ExtractAsync` in both services)
  only adds the archive to `CreatedFiles` when `AnyExtracted` is true.
- **Why `Path == archivePath`/`sourcePath` (the full source path) and not something else:** this
  is what lets the fix work with zero `ArchiveResult` model changes. Per-entry skips inside an
  archive already record `Path = entry.FullName` — a relative in-archive path, which by
  construction never equals a caller's full source-path string. So a single `HashSet` membership
  check at the ViewModel (`GetDeletableSources`, below) distinguishes "this whole source was
  skipped" from "some entries inside a successfully-processed source were skipped" without needing
  to know which case produced which entry.
- `MainViewModel.ArchiveAsync`/`ExtractAsync` now call a new `GetDeletableSources(sources, result)`
  helper before `RunCleanupAsync`, which filters out any source path found in
  `result.SkippedFiles` (by exact path, case-insensitive) — so a fully-skipped source is never
  handed to `RunCleanupAsync` regardless of what `result.Success` says.

**Side effect, accepted as an honesty improvement:** a ZIP archive that conflict-skips every entry
now surfaces in the operation summary dialog as having skipped files, where it previously looked
like a silent clean success. This changes 3 existing `ZipArchiveServiceExtractTests` assertions
(`ExtractAsync_EntryWithColonInName_IsSkipped`, `_ReservedWindowsName_IsSkipped`,
`_EntryWithControlCharacters_IsSkipped`) from `SkippedFiles.Should().HaveCount(1)` to `HaveCount(2)`
— each of those fixtures has exactly one entry, which was already individually skipped, so the new
whole-archive aggregate entry is a second, expected item, not a regression.

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`,
`src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.App/ViewModels/MainViewModel.cs`, `DIAGRAMS.md` (diagrams 3 and 5 updated in the
same commit per their own DoD trigger), `tests/Archiver.Core.Tests/Services/*`,
`tests/Archiver.Core.IntegrationTests/TarProcessServiceExtractTests.cs`

---

## T-F86 — Explorer Context-Menu Gating for Non-ZIP Extract/Test (Native)

**Pre-implementation research (per `CLAUDE.md`'s COM/shell constraint):** fetched the real,
currently-shipping `NanaZip.UI.Modern/NanaZip.ShellExtension.cpp` via
`raw.githubusercontent.com` (not a description from memory) to see how NanaZip's modern
`IExplorerCommand` extension decides whether a selection "needs extract." Quoted the actual code:

```cpp
static const char* const kExtractExcludeExtensions =
    " 3gp"
    " aac ans ape asc asm asp aspx avi awk"
    " bas bat bmp"
    ... // ~30 more lines of known-non-archive extensions (media, docs, code)
    " ";

static bool FindExt(const char* p, const FString& name) { /* linear scan of the space-joined list */ }

static bool DoNeedExtract(const FString& name)
{
    return !FindExt(kExtractExcludeExtensions, name);
}
```

**Finding: NanaZip's gate is extension-only, and it is an *exclusion* list, not an allowlist.**
Any extension not in `kExtractExcludeExtensions` is treated as "needs extract" — there is no
magic-byte/content sniffing anywhere in `GetState()`-equivalent code, and no attempt to positively
confirm the file is one of the ~10 formats in NanaZip's own `kArcExts`. This makes sense for
NanaZip: it wraps the full 7-Zip engine, which opens dozens of formats it can't enumerate by
extension ahead of time, so an exclusion list is the more complete filter.

**Decision — deviate from NanaZip's exclusion-list shape; use a positive extension allowlist
instead, matching the pattern this codebase already uses in `MainViewModel.cs`.** Two reasons:
1. Pakko's supported-format surface is small and fixed (ZIP directly; RAR/7z/tar/gz/bz2/xz/zst/
   lzma via `ITarService`) — unlike 7-Zip's engine, there is no long tail of formats to catch with
   a broad exclusion net. An allowlist is both more precise (won't show "Extract" on a `.txt` or
   other file NanaZip's own exclusion list happens to miss) and matches this project's "minimal
   attack surface" audience (`CLAUDE.md`).
2. `Archiver.App/ViewModels/MainViewModel.cs` already solved this exact problem for the in-app file
   list (`_extractableTypes`, added in T-F85): a fixed `HashSet<string>` of extensions
   (`ZIP, RAR, 7Z, TAR, GZ, TGZ, BZ2, TBZ2, XZ, TXZ, ZST, TZST, LZMA`), pure string comparison, "read
   on every FileItems change... must not do per-file disk I/O." `GetState()` is called by Explorer
   on every right-click, at least as often as `MainViewModel`'s property — the same no-I/O
   constraint applies, and mirroring the existing allowlist keeps the two gates from silently
   drifting apart (same the `com:InProcessServer`/`SurrogateServer` drift already documented above
   in this file). No magic-byte read (`ArchiveFormatDetector`'s approach) is used at gating time —
   it would mean opening every selected file on every right-click, which neither NanaZip nor
   Pakko's own existing UI gate does. Magic-byte detection remains authoritative only where it
   already runs today: inside `ExtractionRouter`, at the moment extraction actually happens.

**Decision — gate on "does `tar.exe` exist" only, not per-format `TarCapabilities` (RAR/7z/zstd
need libarchive ≥ 3.7.0).** The native DLL has no access to `Archiver.Core`'s `TarVersionParser`
(a different language, no cross-language sharing mechanism in this repo — `T-F86`'s own task note
in `TASKS.md` says the same). Re-implementing version-string parsing in C++ purely to decide menu
*visibility* would duplicate a canonical-owner computation (`ARCHITECTURE.md`'s `ITarService`
layer) for a non-authoritative check, risking exactly the kind of drift `CLAUDE.md`'s "Update
Cascades" section warns about. A cheap `GetFileAttributesW` check for
`C:\Windows\System32\tar.exe`'s existence (cached once per DLL load, mirroring
`ExplorerCommands.cpp`'s existing `GetAppIconPath()` `call_once` pattern) is enough to answer "is
extraction even possible in principle." If tar.exe exists but this specific Windows build's
libarchive is too old for the selected format (e.g. RAR on pre-23H2), the menu item still shows,
Invoke still launches `Archiver.Shell.exe`, and `ExtractionRouter.BuildUnsupportedReason` (already
shipped in T-F85) produces the same specific "requires tar.exe with libarchive >= 3.7.0" message
the in-app file picker path already gives for the identical case — one authoritative place decides
the precise per-format answer, the native menu only decides the coarse "is this worth offering."

**Scope:** `ExtractHereCommand`/`ExtractFolderCommand` (`AllPathsAreZip` → new
`AllPathsAreSupportedArchive`), `ExtractDialogCommand` (`AnyPathIsZip` → new
`AnyPathIsSupportedArchive`). `ArchiveCommand`'s `GetState()` is unchanged — it already shows "Add
to archive…" for any non-all-ZIP selection (including an all-RAR one), which is correct: archiving
already-archived files into a new ZIP is a legitimate action, the same reasoning
`CompressDialogCommand`'s existing comment gives for ZIP.

**Correction found while implementing — `TestCommand` deliberately stays `AnyPathIsZip`, not
`AnyPathIsSupportedArchive`.** `Archiver.Shell/Program.cs`'s `RunTestAsync` constructs a bare
`ZipArchiveService` and calls `TestAsync` directly — it was never updated by T-F85 (whose own
design note says "`RunArchiveAsync`/`RunTestAsync` are unchanged — `ITarService` has no Archive/
Test method"). `ITarService` has no test/verify method at all. Enabling the "Test archive" verb
for a `.rar`/`.7z` would launch `Archiver.Shell.exe --test`, which would run
`ZipArchiveService.TestAsync` — the same method that "skips non-zip paths internally" per
`TestCommand::GetState`'s own long-standing comment — and, finding no errors in an archive it
never actually opened, show `MessageBoxW`'s "No errors detected in the archive(s)." That is a
false integrity claim, worse than not offering Test at all. Verifying tar-family archives would
need a new `ITarService` capability (e.g. `tar -tvf` full-listing success as a proxy) — out of
scope for this native-gating task; left as a gap, not silently implemented.

**Files:** `src/Archiver.ShellExtension/ShellExtUtils.h`, `ShellExtUtils.cpp`,
`ExplorerCommands.cpp`, `tests/Archiver.ShellExtension.Tests/ShellExtUtilsTests.cpp`,
`DIAGRAMS.md` (diagram 1, per its own COM-interop DoD trigger).

---

## T-F88 — Pakko Confirmed Deliberately Multi-Instance; Dead `AppInstance.Activated` Subscription Removed

**Question:** launching Pakko twice in a row via `pakko://extract?files=...` opened two separate
windows/processes instead of the second activation redirecting into the first. Root cause:
`AppInstance.GetCurrent().FindOrRegisterForKey(...)`/`RedirectActivationTo` appear nowhere in
`src/` — without them, Windows has no mechanism to route a new activation into an already-running
process, so `App()`'s `AppInstance.GetCurrent().Activated += OnActivated;` subscription and its
`OnActivated` handler never fired in practice; `OnLaunched`'s `GetActivatedEventArgs()` path
(T-F83) handled every real launch, cold or warm, on its own.

**Decision — asked the user directly (this is a product-behavior choice, not something to infer
from code): stay multi-instance.** One process per launch matches 7-Zip File Manager/WinRAR/
NanaZip precedent — each is a one-shot "do the task" tool, not a persistent workspace a second
activation should redirect into. Implementing real single-instance redirection was rejected: it
would raise an unresolved UX question with no obviously-correct answer (what happens to a
redirected activation if the first instance has `IsBusy = true`, i.e. an operation already
running?) for a behavior change nothing was actually asking for.

**Fix:** removed the dead `AppInstance.GetCurrent().Activated += OnActivated;` subscription from
`App()`'s constructor and the now-unreachable `OnActivated` method entirely. `HandleActivation`
(the actual logic, still used by `OnLaunched`) is unchanged. `OnLaunched`'s comment updated to
state the multi-instance decision explicitly, so a future reader doesn't wonder why there's no
`Activated` subscriber.

**Files:** `src/Archiver.App/App.xaml.cs`.

---

## T-F50 — tar.exe Test Fixtures: Runtime Generation Instead of a Committed Corpus; Bomb-Detection Gap Found

**Empirical check before committing to a design (advisor-recommended):** ran
`tar.exe --version`/`--help` and tried creating each target format directly.
`tar --format {ustar|pax|cpio|shar}` are the only writer formats — no 7z or rar writer exists in
libarchive at all. But gz/bz2/xz/zstd/lzma compression *filters* on top of a tar writer all worked
empirically: `tar -czf`/`-cjf`/`-cJf`/`--zstd -cf`/`--lzma -cf` all produced valid archives
`tar -tvf` could read back. (An initial naive `-cf out.7z file` "succeeded" but only because `-cf`
alone with no format/compression flag just writes a plain ustar tar under a misleading `.7z`
filename — verified by checking the actual bytes, which were the ustar header, not
`37 7A BC AF 27 1C`.)

**Decision — generate at test-run time for every format tar.exe can create; commit a fixture only
for 7z (tar.exe can read, not write it); leave RAR undone (no encoder available anywhere on this
machine).** This extends `TarBuilder.cs`'s existing self-generation precedent (that file's own
comment already deferred "the full multi-format fixture set" to this task) rather than committing
a new binary corpus the original task text asked for — avoids repo bloat for formats that are
fully reproducible in CI. New `ExternalTarFixtureBuilder.cs` shells out to the real `tar.exe` per
test, gated inside each `[SkipIfFormatUnsupported]`-tagged test body (not shared setup) so
generating an unsupported format skips cleanly instead of throwing.

**Finding, spun out as T-F90 rather than solved here:** `TarProcessService`/`ArchiveEntrySecurity`
has no equivalent to `ZipArchiveService`'s `MaxCompressionRatio` (1000:1) ZIP-bomb check — grepped
`TarProcessService.cs` for "ratio"/"bomb"/"1000" and found nothing. Writing the "bomb skipped"
test T-F50 originally asked for would have silently asserted behavior that doesn't exist. Also,
tar's compression wraps the whole stream (unlike ZIP's per-entry compression), so a bomb check
here isn't a copy-paste of ZIP's per-entry ratio — it needs its own design (compare declared total
uncompressed size against the compressed file's on-disk size). Tracked as its own task rather than
implemented under this one's scope or silently dropped from the checklist.

**Files:** `tests/Archiver.Core.IntegrationTests/ExternalTarFixtureBuilder.cs`,
`TarProcessServiceCompressedFormatsTests.cs`, `TarProcessServiceExternalFormatsTests.cs`,
`TarProcessServiceExtractTests.cs` (added truncated-tar test), `Fixtures/valid.7z`,
`Fixtures/README.md`.

---

## T-F90 — Whole-Archive Compression-Ratio Check for the tar.exe Path

**Problem (spun out of T-F50, see above):** `ZipArchiveService` rejects individual ZIP entries
whose `Length / CompressedLength` exceeds 1000:1 as a decompression-bomb precaution. No
equivalent exists on the tar.exe path. Can't be a direct port: ZIP compresses each entry
independently and exposes `CompressedLength` per entry for free; a `.tar.gz`/`.tar.bz2`/etc.
compresses the *entire tar stream* in one pass, so there is no per-entry compressed size to read
before extraction.

**Empirical check before implementing (per `CLAUDE.md`'s pre-implementation-research
constraint, and because T-F49's own entry above explicitly warned against parsing `-tvf` columns
beyond the type character):** T-F49 rejected parsing `-tvf`'s other columns after observing the
date column render mangled on this Cyrillic-locale machine (same bug class as T-F84) and
generalized that caution to "every other column." Re-tested specifically for the size column: on
`tar -tvf` output like `-rw-rw-rw-  0 0      0          12 <mangled-month> 07 16:59 file1.txt`,
the size field (`12`) is a plain ASCII decimal number at a fixed token position (5th
whitespace-separated field), not a locale-formatted string — only the month abbreviation (6th
field) was mangled. Confirmed across empty (`0`), single-digit, and multi-digit sizes; token
position stays fixed regardless of filename spacing because the parse only reads field index 4
and never needs to locate the name field. **Correction to T-F49's blanket "every other column"
caution: the size column specifically is safe to parse — the risk T-F49 found is confined to the
date column's locale-dependent formatting, not shared by numeric fields.**

**Decision:** extend `ScanForUnsafeEntriesAsync`'s existing `-tvf` pass (already reads column 0
for entry type) to also parse column 4 (size) for every regular-file (`-`) entry and accumulate a
running total declared uncompressed size. After the scan, compare that total against the archive
file's actual on-disk (compressed) size:
- Ratio computed as `totalDeclaredSize / compressedFileSize`, guarded against division by zero
  (empty/zero-byte archive file skips the check, same guard style as `ZipArchiveService`'s
  per-entry check).
- Threshold: **1000:1, matching `ZipArchiveService.MaxCompressionRatio` exactly** — no evidence
  surfaced during this task to justify a different number, and keeping both extractors' bomb
  thresholds identical avoids an unexplained asymmetry between the two paths a future reader
  would have to puzzle over.
- On breach, the **whole archive is rejected** (`TarArchiveRejectedException`, same whole-archive
  reject-before-`-xf` model T-F49 already established) — not a per-entry skip. Tar's compression
  wraps the whole stream, so there is no way to attribute the bomb to one specific entry the way
  ZIP's per-entry check can.
- Directory entries (`d`) are excluded from the size sum — their declared size is always 0 and
  contributes nothing to genuine bomb ratios, but skipping them explicitly keeps the sum's
  meaning unambiguous.

**Rejected: comparing against libarchive's own decompression limits or shelling out a second
`tar.exe` invocation with a byte-count cap.** Adds process overhead and a second point of
locale/version fragility for no benefit over a single `-tvf` pass already being read for the
type-character check — the size column rides along in the same pass at zero extra cost.

**Files:** `src/Archiver.Core/Services/TarProcessService.cs`,
`tests/Archiver.Core.IntegrationTests/TarProcessServiceExtractTests.cs`.

---

## T-F92 — Reverted: Submenu Icons Undone After On-Device Review

**Original decision (same day, see T-F92's own entry above in `TASKS.md`):** give every Pakko
submenu command (Extract here, Extract to folder, Add to archive, Test archive, both dialogs) the
same icon as the root "Pakko" entry, via `GetAppIconPath()`.

**Correction:** implemented and on-device verified (all six subcommands showed the icon,
Explorer stable), then shown to the user as a screenshot. The user's reaction: the icons on every
individual action read as visual clutter, not as parity with the root entry the original decision
assumed. Reverted all six `GetIcon` overrides back to `E_NOTIMPL`; only `PakkoRootCommand::GetIcon`
(the top-level "Pakko" entry) keeps an icon, matching the pre-T-F92 shipped behavior.

**Why recorded rather than just reverted silently:** the original "one shared icon" decision was
made *before* seeing it rendered in Explorer — a case where the actual visual result changed the
call, not a discovered bug. Worth keeping so a future session doesn't re-propose the same fix
without knowing it was already tried and rejected on sight.

**Files:** `src/Archiver.ShellExtension/ExplorerCommands.cpp`.

---

## T-F94 — Compression-Bomb Handling: Confirm-and-Extract Instead of Auto-Reject

**Problem:** T-F90 (same session, earlier) added a whole-archive ratio check to
`TarProcessService` that always rejected an archive over 1000:1; `ZipArchiveService` already had
an older per-entry version (T-F28, v1.0) that always skipped individual suspicious entries. User
feedback: auto-rejecting is too aggressive — a legitimately huge but genuinely compressible file
(a text log, a disk image) can trip the same ratio a real bomb would. The user should see the
declared size and ratio, and be allowed to extract anyway **if the destination disk has room for
the declared size**; if it doesn't fit, block with an explanation, no override.

**Decision — unify ZIP to whole-archive ratio, matching tar's model:** `ZipArchiveService`'s
detection moves from per-entry to whole-archive granularity (sum of all entries' declared
`Length` vs. the archive file's on-disk size), enabling exactly one confirmation per archive
instead of one dialog per flagged entry. **Trade-off, accepted deliberately:** an archive with
one small bomb entry hidden among otherwise-legitimate large files may no longer trip detection
if the aggregate ratio stays under 1000:1. Not mitigated — considered an acceptable cost for
consistent, non-spammy UX.

**Shared evaluator, not duplicated per-extractor logic:** `ArchiveEntrySecurity` gains
`EvaluateCompressionBombAsync(archivePath, declaredUncompressedSize, compressedSize,
availableFreeSpaceBytes, confirmCallback)` returning a `CompressionBombOutcome` (`NotABomb`,
`InsufficientDiskSpace`, `UserDeclined`, `UserConfirmed`). `MaxCompressionRatio` (1000) moved here
too — previously duplicated separately in `ZipArchiveService` and `TarProcessService` with a
"kept identical deliberately" comment; now one constant. Disk space is checked **before** the
confirm callback runs — `InsufficientDiskSpace` always blocks regardless of what a callback would
have said, matching the user's explicit "block if it doesn't fit, no override" requirement.

**Delegate on `ExtractOptions`, not a DI-injected interface:** `ConfirmCompressionBombExtraction
{ get; init; }` is a `Func<CompressionBombWarning, Task<bool>>?`, not a constructor-injected
service. Both `ZipArchiveService`/`TarProcessService` are constructed directly (`new
ZipArchiveService()`) in multiple test files and DI composition roots — a mandatory
constructor-injected confirm-service would force a dependency (and a no-op fake) onto every one
of those call sites. `null` (the default) auto-declines, preserving pre-T-F94 behavior for
`Archiver.Shell` and any test that doesn't set it. Rides through `ExtractionRouter`'s existing
`options with { ArchivePaths = subset, OpenDestinationFolder = false }` pattern for free.

**`GetDiskFreeSpaceExW` via P/Invoke, not `DriveInfo`:** `DriveInfo`'s constructor throws on UNC
paths (`\\server\share\...`), which this app's destination folder picker can legitimately point
at — silently and permanently blocking every bomb-flagged archive extracted to a network
destination regardless of actual free space. `GetDiskFreeSpaceExW` works uniformly for local
drive letters and UNC shares. Queries the **volume/share root** (`Path.GetPathRoot`), not the
exact destination subfolder — the per-archive `SeparateFolders` subfolder may not exist yet when
the check runs, and `GetDiskFreeSpaceExW` requires an existing directory. Returns 0 on any
failure (treated as "no room," blocking extraction) — conservative default, consistent with this
codebase's security-first posture on the extraction path.

**ZIP-specific ordering fix (found while implementing, not part of the original design pass):**
the whole-archive check runs **before** `tempDest` is created (`ZipArchiveService`'s
`ExtractWithSmartFolderingAsync`) — placing it after `Directory.CreateDirectory(tempDest)` but
before the `try/finally` that cleans it up would leak an orphaned `<name>_tmp` directory on every
declined/blocked bomb.

**Tar refactor precision:** `ScanForUnsafeEntriesAsync`'s return type changed from `Task` to
`Task<long>`, returning the `totalDeclaredSize` it already accumulates during its one `-tvf`
pass — the ratio/outcome decision itself moved to `ExtractSingleArchiveAsync` (via the shared
evaluator), but the size sum stays computed in that single pass rather than spawning a second
`tar -tvf` process to re-derive it (would have silently reversed T-F90's own stated rationale for
extending that one pass in the first place).

**Tar bomb outcome changes from `ArchiveError` to `SkippedFile`:** under T-F90, a rejected bomb
set `Success = false` (an `ArchiveError`). Under T-F94, `InsufficientDiskSpace`/`UserDeclined`
produce a `SkippedFile` instead — `Success` stays `true`, consistent with T-F87's
SkippedFiles/CreatedFiles bookkeeping and with ZIP's skip-based model. This also moves which
section (`Errors` vs `Skipped`) it surfaces under in `ShowOperationSummaryAsync`'s dialog and in
`Archiver.Shell`'s `MessageBoxW` split.

**UI-thread marshaling (the difference between working and a guaranteed crash on first real
use):** both extractors run their per-archive bodies off the UI thread (`ConfigureAwait(false)`
throughout; `ZipArchiveService.ExtractAsync` explicitly wraps per-archive work in `Task.Run(...)`).
`ContentDialog.ShowAsync()` requires the calling thread to own the window's `DispatcherQueue`.
`DialogService.ShowCompressionBombConfirmAsync` marshals explicitly via a `TaskCompletionSource<bool>`
+ `DispatcherQueue.TryEnqueue(...)` rather than calling `ShowAsync()` directly from wherever the
confirm delegate happens to run — this is new ground for this codebase (no prior mid-operation UI
prompt existed here before).

**Archiver.Shell scope (confirmed with user):** `Archiver.Shell` is a `WinExe` with
`CreateNoWindow=true`, launched by Explorer via COM — no attached console/stdin/stdout in its
actual invocation path, so a console Y/N prompt isn't technically meaningful there today. It keeps
`ConfirmCompressionBombExtraction` unset (auto-decline), unchanged. The project's actual "console
analog of 7z" goal is tracked separately as **T-F09 (Archiver.CLI)**, currently `future`/unbuilt —
out of scope here. The delegate design was specifically validated against this: when T-F09 is
eventually built, it supplies its own callback (`Console.ReadLine`-based Y/N prompt by default, or
an unconditional `true`/`false` under a future `--yes`/`--silent` flag) with zero changes needed
in `Archiver.Core`.

**UX note:** a batch extraction with N flagged archives shows N sequential modal confirm dialogs —
intended, not a bug (both extractors already loop per-archive independently; nothing new needed
for one bomb archive to skip/block while the rest of the batch proceeds normally).

**Known, pre-existing gap, not fixed here:** clicking Cancel while a confirm `ContentDialog` is
showing won't dismiss it (`ShowAsync()` isn't wired to the `CancellationToken`) — the existing
`ShowOperationSummaryAsync` dialog has the identical gap today.

**Test-boundary limit, accepted:** the `InsufficientDiskSpace` branch is covered only at the pure
`EvaluateCompressionBombAsync` unit-test level (`availableFreeSpaceBytes` injected as a plain
`long`) — not attempted in integration tests, since simulating a genuinely full disk isn't
practical there.

**`InternalsVisibleTo` added** (`Archiver.Core.csproj` → `Archiver.Core.Tests`) so
`EvaluateCompressionBombAsync`/`CompressionBombOutcome` (both `internal`, matching
`ArchiveEntrySecurity`'s existing internal-only surface) can be unit-tested directly rather than
made `public` just for test access — first use of this attribute in the repo.

**Files:** `src/Archiver.Core/Models/CompressionBombWarning.cs` (new),
`src/Archiver.Core/Models/ExtractOptions.cs`, `src/Archiver.Core/Services/ArchiveEntrySecurity.cs`,
`src/Archiver.Core/Services/ZipArchiveService.cs`, `src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.Core/Archiver.Core.csproj`, `src/Archiver.App/Services/IDialogService.cs`,
`src/Archiver.App/Services/DialogService.cs`, `src/Archiver.App/ViewModels/MainViewModel.cs`,
`src/Archiver.App/Strings/en-US/Resources.resw`,
`tests/Archiver.Core.Tests/Services/ArchiveEntrySecurityCompressionBombTests.cs` (new),
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceExtractTests.cs`,
`tests/Archiver.Core.IntegrationTests/TarProcessServiceExtractTests.cs`.

---

## T-F91 — Multi-Language Localization (First Batch: 24 European Locales)

**What:** added `Resources.resw` under `Strings/<locale>/` for all 24 confirmed European
locales (uk-UA, de-DE, fr-FR, es-ES, it-IT, pl-PL, pt-PT, nl-NL, ro-RO, cs-CZ, sk-SK, hu-HU,
el-GR, sv-SE, da-DK, fi-FI, nb-NO, bg-BG, hr-HR, sr-Latn-RS, sl-SI, et-EE, lv-LV, lt-LT),
translating the 31 UI strings `en-US/Resources.resw` currently defines. Scope confirmed with
user 2026-07-07: all 24 European locales in one batch, not the "one pilot locale" incremental
approach `TASKS.md` originally floated — the string count (31, mostly single words/short
phrases) made the whole batch tractable in one pass rather than justifying per-locale staging.
Arabic/Japanese/Chinese/etc. (the non-European half of T-F91's target list) are explicitly
**not** part of this batch — a separate follow-up, since those scripts (RTL, CJK) carry their
own layout-verification risk `TASKS.md`'s T-F91 entry already calls out.

**AboutGitHubUrl/AboutPrivacyUrl intentionally omitted from every non-English `Resources.resw`:**
these two keys hold literal URLs, not translatable text. WinUI 3/MRT's resource candidate
lookup falls back to the neutral/default-language resource (`en-US`, per `Package.appxmanifest`'s
implicit default) when a specific-culture `.resw` doesn't define a key — confirmed by inspecting
the generated `AppxManifest.xml` after a `dotnet build` (see below), which lists all 25 locales
via the existing `<Resource Language="x-generate"/>` auto-expansion with no manual manifest edit
needed. Duplicating the same URL string across 24 files would only create 24 places to miss when
the URL changes.

**Locale tag choices:** used the standard specific-region BCP-47 tag matching each language's
primary Windows-supported culture (e.g. `de-DE`, `pt-PT` for European Portuguese not `pt-BR`,
`nb-NO` for Norwegian Bokmål). Serbian used `sr-Latn-RS` (Latin script) rather than
`sr-Cyrl-RS` — a deliberate simplification for this small string set, not a claim that Latin is
more "correct" for Serbian; flagged here in case a native reviewer prefers Cyrillic instead.

**Verification performed:** every new `Resources.resw` parsed successfully via PowerShell's
`[xml]` cast (24/24 files, 31 data keys each). `dotnet build src/Archiver.App/Archiver.App.csproj
/p:Platform=x64` succeeded (0 warnings, 0 errors) and its generated `AppxManifest.xml`
(`bin/x64/Release/.../AppxManifest.xml`) confirmed all 25 `<Resource Language="..."/>` entries
were auto-generated from the new locale folders. **Not yet done:** on-device verification that
the OS display language actually selects the matching resource set (`TASKS.md`'s T-F91
acceptance criteria still list this open, plus the native-speaker review pass the task's own
text requires before shipping any locale — these translations are AI-generated to a
professional-UI standard but unreviewed by a native speaker).

**Files:** `src/Archiver.App/Strings/<locale>/Resources.resw` (24 new files, one per locale
listed above).

---

## Correction — T-F36 / T-F48: "Pluggable Archive Engine" Was Two Different Tasks

**Symptom:** asked to pick up T-F36 (unblock T-F48's grey-out-unsupported-formats criterion).
Before writing code, re-read both tasks against the architecture actually shipped in
T-F47–T-F50/T-F85 and found the premise no longer holds.

**Root cause:** T-F36 was written before tar.exe integration existed and bundled two unrelated
problems under one task:
1. **Extraction of non-ZIP formats** — T-F36/T-F48 both assumed this needed a UI format
   selector to grey out unsupported entries. It doesn't: `ArchiveFormatDetector` +
   `IExtractionRouter` (T-F85) auto-detect the format from file content and route to
   `IArchiveService`/`ITarService`, producing a specific `SkippedFiles` message
   (e.g. "RAR requires tar.exe with libarchive >= 3.7.0...") for anything `TarCapabilities`
   reports unsupported. There is no selector in this flow to grey out.
2. **Creation of non-ZIP archives** (T-F36's actual "Format: ZIP/TAR/TAR.GZ" dropdown next to
   the Archive button) — genuine unbuilt work, but `SPEC.md`'s roadmap table (line 214) places
   "TAR creation via tar.exe" at **v1.5**, not the current v1.3/v1.4 window.

**Why this wasn't caught earlier:** T-F48 was written/partial-completed during the T-F47–T-F49
push and its one open criterion just carried forward the "blocked on T-F36" note without anyone
re-checking whether T-F36 itself still matched reality once T-F85 shipped a completely different
extraction-routing design.

**Decision (confirmed with user 2026-07-07):** do not build `IArchiveEngine` now. Marked T-F36
superseded/deferred to v1.5 and T-F48's grey-out criterion as not-applicable-to-extraction in
`TASKS.md`, per the "never silently deprecate" rule — neither task was deleted. When v1.5's TAR
creation work actually starts, re-scope it as "add a create/compress method to the existing
`ITarService`" rather than resurrecting a from-scratch `IArchiveEngine` interface — avoids
building an abstraction with only one real implementation (`ZipEngine`) and a stub
(`TarEngine`) today, which `CLAUDE.md`'s no-premature-abstraction rule already warns against.

**Files:** `TASKS.md` (T-F36, T-F48 status/criteria notes only — no source changes this round).

---

## T-F12 — Parallel Compression (SeparateArchives Mode)

**What:** `ArchiveAsync`'s `SeparateArchives` branch now processes all `SourcePaths` via
`Parallel.ForEachAsync` (capped at `Environment.ProcessorCount`) instead of a sequential `for`
loop. Each path produces its own independent `.zip`, so there is no shared `ZipArchive` writer
across workers — the parallelism itself is safe by construction.

**The one thing the one-line pseudocode in `TASKS.md` didn't account for:** the old sequential
loop's conflict/rename logic (`OnConflict` handling for a destination path that already exists)
implicitly relied on `File.Exists(destPath)` reflecting every *prior* iteration's completed
write — true only because execution was strictly ordered. Two different `SourcePaths` sharing a
basename (e.g. two folders both named "Photos" from different parents) would, under real
concurrency, both observe "doesn't exist yet" and race to write the identical `.tmp` path.
**Fix:** split conflict resolution out into its own sequential pre-pass before the parallel
section starts. It walks the sorted source paths once, using an in-memory `claimedDestPaths`
set alongside the on-disk `File.Exists` check, and assigns each path a final, collision-free
`destPath` (or a skip decision) before any worker touches the filesystem. Workers then just use
their precomputed destination — no conflict logic left inside the parallel body at all.

**Deliberate behavior deviation, `Overwrite` + same-run collision only:** sequentially,
`OnConflict = Overwrite` with two same-basename sources meant "last one processed clobbers the
first" (nondeterministic which one survives, purely by loop order). Two workers concurrently
overwriting the same path is a genuine data race, not just a semantic quirk — so a same-run
collision under `Overwrite` is treated as a rename instead (same as `ConflictBehavior.Rename`
would do), while a true on-disk-only conflict (no same-run collision) still overwrites exactly
as before. This only changes behavior for the narrow case of two sources sharing a basename
within one `Overwrite`-mode batch — not covered by any pre-existing test, and not a scenario
worth reproducing exactly given the old behavior was itself nondeterministic under any future
reordering.

**Progress reporting:** replaced the single running `byteOffset` (impossible to keep once
multiple workers touch it concurrently) with a `long[] completedBytesBox` box updated via
`Interlocked.Read`/`Interlocked.Add`. Each worker snapshots the current total as its own
per-entry progress baseline when it starts, and adds its path's byte count back once it
finishes. This is a reasonable, thread-safe approximation, not byte-exact — two workers mid-flight
at once will briefly double-count in the reported numerator, which is acceptable for a progress
bar. To keep the existing `ArchiveAsync_ProgressReporting...` test's `reports.Last().Percent ==
100` assertion deterministic regardless of how concurrent workers' individual reports interleave,
`ArchiveAsync` now emits one explicit, exact `Percent = 100` report immediately after the
`Parallel.ForEachAsync` await completes.

**Cancellation:** `Parallel.ForEachAsync` throws immediately if handed an *already-cancelled*
token, whereas the old `for` loop's `if (cancellationToken.IsCancellationRequested) break;` at
the top of each iteration simply skipped all work and returned a graceful (mostly empty)
`ArchiveResult`. This broke `ArchiveAsync_CancellationRequested_StopsProcessing` (pre-cancels the
token before calling `ArchiveAsync`) the first time this was implemented — caught by running the
full test suite before considering the task done. Fixed by gating the whole pre-pass + parallel
section behind `if (!cancellationToken.IsCancellationRequested)`, restoring the old graceful-noop
behavior for that specific case. Mid-flight cancellation (token cancelled after work has started)
is unchanged: it still propagates an `OperationCanceledException` out of `ArchiveAsync`, exactly
as the old sequential loop's per-path `catch (OperationCanceledException) { cleanup; throw; }`
already did — no test exercises that path for `SeparateArchives` specifically, so no further
change was needed there.

**Signature widening for delegate reuse:** `AddDirectoryToArchiveAsync` originally took
`List<SkippedFile>`/`List<ArchiveError>` parameters. The parallel path needed to pass a
`ConcurrentBag<T>` instead (thread-safe `.Add` from multiple workers) — `ConcurrentBag<T>` does
**not** implement `ICollection<T>` (only `IProducerConsumerCollection<T>`/`IReadOnlyCollection<T>`),
so widening the parameter type to `ICollection<T>` doesn't compile for both callers. Changed the
parameters to `Action<SkippedFile>`/`Action<ArchiveError>` instead — both `List<T>.Add` and
`ConcurrentBag<T>.Add` satisfy this via method-group conversion (`skippedFiles.Add`,
`errors.Add`), and the method only ever calls `.Add` on these anyway.

**Verification:** `dotnet test --filter "Category!=Slow"` — 190/190 (was 187/187). Added 3 new
tests (many-file parallel round-trip content check, two-sources-same-basename collision handling,
a batch larger than typical core counts). Reran the affected test classes 5x each after the fix —
no flakiness observed, including the pre-existing progress-reporting and pre-cancelled-token
tests that this change put genuinely at risk.

**Files:** `src/Archiver.Core/Services/ZipArchiveService.cs`,
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`.

---

## Bug: Deploy.ps1 Failed After T-F91 — `.msixbundle` vs `.msix`, and a Wedged Version Folder

**Symptom:** while verifying T-F12 on-device, `.\scripts\Deploy.ps1` failed repeatedly with
`MSB3231: Unable to remove directory "...AppPackages\Archiver.App_1.2.0.4_Test\"... Access to
the path ... is denied`, rotating across different subpaths each retry (`Add-AppDevPackage.
resources\de-DE`, then `\cs-CZ`, then the bare directory itself). This looked exactly like a
process-lock problem and was chased as one for a long time.

**What was tried and did NOT fix it (all legitimate, ruled out cleanly, not wasted — see below
for why the false trail happened):**
- `taskkill /F /IM dllhost.exe` (did find and kill a live COM surrogate — didn't help)
- `dotnet build-server shutdown` (did find and shut down a live MSBuild/VBCSCompiler node —
  didn't help)
- Manual `Remove-Item`/`[System.IO.Directory]::Delete` on the stuck folder between attempts
- Confirming `EnableControlledFolderAccess` was `0` (not Defender's ransomware-protection
  feature blocking it) and that ACLs/owner on the folder were normal (`Full Control` for the
  current user)
- **A full machine reboot** — the strongest possible test for "some process holds a handle."
  Failed identically afterward, which should have been the moment to abandon the process-lock
  theory outright
- **Reducing the locale count from 25 to 6** (`uk-UA`, `de-DE`, `fr-FR`, `es-ES`, `pl-PL` +
  `en-US`) as a controlled experiment against the "T-F91 added too many locale folders" theory —
  failed identically with only 6, cleanly disproving that specific theory. (The real T-F91
  connection turned out to be true anyway, just via a completely different mechanism — see below.)

**What actually fixed it:** bumping `Package.appxmanifest`'s `Version` from `1.2.0.4` to
`1.2.0.5` (to get a **fresh** output folder name) combined with deleting `obj\` as well as
`AppPackages\` (not just `AppPackages\`, which is all every prior cleanup attempt had touched).
Once building against a clean version/folder, the packaging step got measurably further before
hitting a *different*, real bug — proving the `1.2.0.4`-named `obj`/`AppPackages` state itself
was wedged (not a live handle at all; `Get-ChildItem` had shown the "locked" directory as empty
the whole time, which in hindsight was the tell — a live handle from a scanner/indexer produces
`ERROR_SHARING_VIOLATION` on a specific open file, not `ERROR_ACCESS_DENIED` on an
enumerated-empty directory). Root cause of the wedge itself was never fully pinned down (most
likely NTFS metadata left over from one of the many earlier failed/interrupted publish attempts
against that exact path) — not worth further investigation now that the workaround
(version bump + `obj` clean) is known and cheap.

**The real, separate bug T-F91 did cause:** once past the wedged-folder issue, `dotnet publish`
succeeded and produced `Archiver.App_1.2.0.5_x64.msix` **and then consumed it into**
`Archiver.App_1.2.0.5_x64.msixbundle` — no flat `.msix` remained on disk. This is expected,
correct MSBuild behavior: once an app has enough per-language resource packages (T-F91 took
Pakko from 1 locale to 25), a single flat `.msix` can no longer represent the app — a bundle is
required to hold multiple resource-qualified sub-packages so a device can install only the
languages it needs. `Deploy.ps1`'s "locate the final package" step
(`Get-ChildItem -Filter '*.msix'`) had no reason to expect this before T-F91 (Pakko shipped only
`en-US` until then) and simply found nothing, failing with a misleading `No .msix file found`
error that had nothing to do with the file actually being missing — `dotnet publish` had already
succeeded by that point.

**Fix:** `Deploy.ps1`'s package-locate step now searches `-Include '*.msix', '*.msixbundle'`
instead of `-Filter '*.msix'`, taking whichever is newest. `Add-AppxPackage` installs either
format directly, so no other change was needed.

**Why the long detour was still worth it:** the reboot and locale-reduction experiments were the
correct next steps given the evidence available at each point (an "Access is denied" error is a
process-lock symptom far more often than a wedged-folder one) — they weren't wasted, they were
what correctly ruled out the wrong theory and forced the actual fix (version bump) to be found.
The lesson worth keeping: `Get-ChildItem`/enumeration showing a directory as **empty** while
deletion still fails with `Access is denied` (not `in use by another process`) points at wedged
directory state, not a live handle — try a fresh path (version bump) before spending more time
on process/handle theories next time this shape of error shows up.

**Verification:** clean `.\scripts\Deploy.ps1 -Thumbprint "..."` run completed end-to-end —
`Pakko installed successfully, Version: 1.2.0.5`, confirmed via
`Get-AppxPackage *Pakko*` showing `PavloRybchenko.Pakko_1.2.0.5_x64__9hkd8feqeqbr4`. Version
auto-bumped to `1.2.0.6` by `Deploy.ps1` itself per its documented behavior.

**Files:** `scripts/Deploy.ps1`, `src/Archiver.App/Package.appxmanifest` (version bump only,
its own auto-increment behavior — not a manual scope change).
