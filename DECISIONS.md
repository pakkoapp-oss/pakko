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
