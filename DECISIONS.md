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

---

## T-F95 — Root "Pakko" Context-Menu Icon Missing: Archiver.App.exe Had No Icon Resource

**Symptom:** user screenshot showed the top-level "Pakko" entry in a real Windows 11 right-click
menu with no icon, while "NanaZip" directly above it showed its icon correctly in the same menu.
User reported this had been flaky during earlier AI-driven smoke testing too, not a one-off.

**False leads ruled out before the real cause, in order:**
1. *"It's the already-documented Explorer icon-cache/first-open flicker artifact"* — `CLAUDE.md`
   already has a hard constraint describing exactly this class of Pakko submenu artifact. Correct
   instinct to check first (don't chase cache artifacts with code changes), but the user reported
   the absence persisted across sessions and multiple right-clicks, not just first paint — worth
   a decisive test rather than accepting the cache theory on pattern-match alone.
2. *"The exe has no icon at all"* — checked with `[System.Drawing.Icon]::ExtractAssociatedIcon()`
   against the installed `Archiver.App.exe`, which returned non-null. **This was a false
   negative-for-the-negative**: `ExtractAssociatedIcon` can return a generic system fallback icon
   for a file with zero embedded icon resources — a non-null result does not prove a real icon
   resource exists at any index. Caught by the advisor before building further conclusions on it.
3. *"NanaZip's real shipped `GetIcon` must do something different"* — fetched NanaZip's actual
   source (`NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`, `ExplorerCommandRoot::GetIcon`, via the
   GitHub trees API per `CLAUDE.md`'s pre-implementation-research rule) and found it uses the
   *identical* pattern Pakko already had: external exe path + icon index
   (`GetNanaZipPath() + ",-1"` vs Pakko's `GetAppIconPath() + ",0"`). Ruled out "the whole
   external-exe-icon approach is wrong" — the approach matches a real, working implementation.

**Decisive test (the one that actually settled it):** `ExtractIconEx(exePath, -1, null, null, 0)`
— the real Win32 API Explorer itself uses — against the installed `Archiver.App.exe`, returning
the *actual* icon count in the file. Result: `total=0`. Confirmed at the file level, independent of
any shell/COM/cache layer.

**Root cause:** `src/Archiver.App/Archiver.App.csproj` never set `<ApplicationIcon>`. The only
icon-related item in the project was `<Content Include="Assets\Square44x44Logo.ico" />` — that
asset is consumed by the MSIX manifest's `Square44x44Logo`/tile logo mechanism (a completely
different code path: the OS renders package tiles/Start-menu entries from manifest-declared PNG
assets, not from a classic Win32 `RT_GROUP_ICON` resource inside the exe). With no
`<ApplicationIcon>`, the .NET SDK's apphost had zero classic icon resources, so
`PakkoRootCommand::GetIcon`'s `Archiver.App.exe,0` was always requesting an icon that never
existed — deterministic absence, not intermittent. (The "то було то ні" during earlier smoke
testing was very likely the separate, already-documented Explorer cache-flicker behavior
compounding on top of a target that could never actually resolve — two different things, only one
of which this task fixed.)

**Fix:** one line — `<ApplicationIcon>Assets\Square44x44Logo.ico</ApplicationIcon>` added to
`Archiver.App.csproj`, reusing the same brand `.ico` already shipped for the tile logo. No change
to `ExplorerCommands.cpp` — the approach there was already correct.

**Verification chain (each step re-ran the same `ExtractIconEx` count, not a visual check, until
the very last step):** raw `dotnet build` output apphost: 0 → 1. Reinstalled package's apphost
(after working around the unrelated `MSB3231` packaging bug, see T-F96 below): 0 → 1. Only then:
user personally opened a fresh Explorer window and confirmed the "Pakko" icon now renders in the
real right-click menu (2026-07-07).

**Why this needed an advisor consult mid-investigation:** the `ExtractAssociatedIcon` false
negative (item 2 above) would have derailed the investigation into "it must be a cache/ACL/async
loading problem" without ever suspecting the icon simply didn't exist — the kind of wrong-turn a
second opinion catches cheaply before hours are sunk chasing shell-cache theories for a build
config gap.

**Files:** `src/Archiver.App/Archiver.App.csproj`.

---

## T-F96 — Found, Not Fixed: MSB3231 Packaging-Cleanup Failure After a Valid .msix Is Already Written

**Symptom:** while redeploying for T-F95's verification, `dotnet publish`/`Deploy.ps1` reliably
failed with `MSB3231: Unable to remove directory "..."` (`ACCESS_DENIED`) on a directory under
`AppPackages\..._Test\` or `obj\...\PackageLayout\` — but only *after* the `.msix` itself had
already been written successfully into that same directory tree. Reproduced identically across
three different clean-state attempts in one session, and independently by the user running
`Deploy.ps1` in their own terminal.

**Distinguishes this from the earlier, superficially similar "Deploy.ps1 Failed After T-F91" entry
above:** that earlier case was a genuinely *wedged, empty* directory that `Get-ChildItem` showed as
empty yet still failed to delete — fixed by a version bump to get a fresh path. This time, a full
clean of both `obj` and `AppPackages` *plus* a version bump (guaranteeing a brand-new, never-before-
touched folder) hit the identical error shape again, on a different sub-path each run (`cs-CZ`
resources one run, `Assets` another) — ruling out "stale wedged state" as the explanation this time.

**Ruled out:** Windows Defender real-time scanning — user already has a project-wide exclusion
path configured, and the error is `ACCESS_DENIED` on a delete, not the sharing-violation shape
typical of AV scanning a file mid-write.

**Leading hypothesis, untested:** a parallel-MSBuild-node race between the packaging pipeline's own
`RemoveDir` cleanup step and other build work still writing into the same tree — most plausibly
locale-resource generation, since T-F91 raised Pakko from 1 to 25 resource-qualified sub-packages,
substantially increasing parallel work in exactly this build stage. Not confirmed — see T-F96 in
`TASKS.md` for the next diagnostic step (serialize with `/m:1`, or Process Monitor if that doesn't
resolve it).

**Workaround used to unblock T-F95's verification (not a fix, not applied permanently):** since the
`.msix` was already valid and complete by the time the cleanup step failed, uninstalled the old
package and ran `Add-AppxPackage` directly against the freshly-built `.msix`, bypassing
`Deploy.ps1`'s own install step for that one deployment.

**Follow-up (same day, next session): corrected the wedged-folder theory, added a tolerance
guard.** The user reported this recurring a third time across separate sessions and asked for a
proper diagnosis via a second opinion (advisor, Opus 4.8). Key correction: the earlier
"Deploy.ps1 Failed After T-F91" entry's lesson — `ACCESS_DENIED` on delete implies a wedged/stale
directory, not a live handle, because `Get-ChildItem` showed it as empty — **does not transfer to
this failure**. Re-examined this session's own transcript: every manual `rm -rf` against the
"locked" path succeeded immediately (`exit=0`), moments after MSBuild's own `RemoveDir` failed on
that identical path. A truly wedged directory or DACL problem would block a manual delete too — a
handle that's gone by retry time means a **transient live handle held during the build**, then
released right after, i.e. a race, not stale state. Separately, `RemoveDirectory` returns
`ACCESS_DENIED` (not `ERROR_SHARING_VIOLATION`) when a *child file inside the directory* still has
an open handle — the earlier heuristic ("ACCESS_DENIED ⇒ wedged, SHARING_VIOLATION ⇒ live handle")
was reasoning about opening one specific file, which doesn't hold for removing that file's parent.

**Root-cause scenarios produced (ranked, not yet individually tested against a live recurrence —
see T-F96's `TASKS.md` entry for the full list):** Windows Search Indexer racing the cleanup
(top suspect — the two failing subpaths seen so far, `cs-CZ` text resources and `Assets` images,
both fall under content types the indexer touches); a third-party EDR/AV agent beyond Defender
(plausible given this project's government/defense target audience, and "I have an exclusion"
covering Defender specifically was never verified to actually include this exact path); an
MSBuild-node-level race (cheap `/m:1` test, inconclusive if negative since MakeAppx/PRI-gen may
parallelize internally regardless); or removing the trigger entirely by suppressing the
`_Test\Add-AppDevPackage.resources\<locale>` sideload-package generation, which `Deploy.ps1` never
actually consumes.

**Mitigation implemented (not a root-cause fix):** `Deploy.ps1` now captures `dotnet publish`'s
output and, only if the process exits non-zero, checks whether the output narrowly matches
`MSB3231.*Unable to remove directory.*(AppPackages|PackageLayout)` **and** a `.msix`/`.msixbundle`
newer than the publish start time exists on disk. Only when both hold does it warn and continue to
the existing install steps instead of aborting a build that actually succeeded — any other
`dotnet publish` failure (real compile/sign errors) still fails hard, exactly as before. The regex
was verified against the real historical error text captured earlier this session (both the
`AppPackages\..._Test\` and `obj\...\PackageLayout\` variants) and confirmed to *not* match an
unrelated real C# compile error, as a negative control. The race itself did not recur during two
clean-state `Deploy.ps1` runs used to test this change, so the "continue" branch is verified by
isolated regex testing, not yet by a live end-to-end trigger.

**Observed recurrence (2026-07-13, T-F99/T-F100/T-F101 session, on hold per user decision — no
new investigation or code change, purely logged):** the MSB3231 race recurred on **4 consecutive**
`Deploy.ps1` runs in a row during this session (each rebuilding after a drive-root/file-activation
fix), every one correctly tolerated by T-F102's artifact-based gate. This is far more frequent
than "intermittent" — worth the next person touching T-F96 knowing it reproduces close to every
run on this machine right now, not rarely.

---

## T-F102 — Deploy.ps1 Exit Code: Artifact-Based Gate Replaces Regex-Only Gate; Explicit Exit Codes Throughout

**What broke:** `Deploy.ps1` reported process exit code 1 even on a fully successful deployment
(found 2026-07-13, running the script for T-F05's manual verification). Pakko installed
correctly, the version bumped correctly — the script's own reported *outcome* was simply wrong.

**Root cause:** PowerShell's `$LASTEXITCODE` is set only by external/native process invocations,
never by built-in cmdlets. `dotnet publish` hitting the tolerated T-F96 MSB3231 race left
`$LASTEXITCODE` nonzero; every cmdlet the script ran afterward (`Get-AppxPackage`,
`Remove-AppxPackage`, `Add-AppxPackage`) left it untouched, and the script had no explicit `exit`
at the end. The stale nonzero value from a *tolerated, non-fatal* condition silently became the
script's final reported exit code — indistinguishable from a real failure to anything checking it
(CI, a wrapper script, or a human running `echo $LASTEXITCODE` afterward).

**Fix, designed via a second-opinion review (relayed by the user from another model) and applied
as-is after checking it against the real script:**

1. **Every native call's exit code is captured into its own local variable immediately after the
   call** (`$shellBuildExitCode`, `$shellExtBuildExitCode`, `$publishExitCode`) — every decision
   reads that variable, never `$LASTEXITCODE` at a later point where an intervening cmdlet could
   have left it stale. Every failure branch `exit`s with its own captured code; the script now
   ends with an explicit `exit 0` so reaching that line can only mean every prior step already
   succeeded (or exited on its own).

2. **The T-F96 tolerance gate no longer decides on the MSB3231 regex match — it decides on
   artifact evidence.** Old gate: `$publishOutput` text matches
   `MSB3231.*Unable to remove directory.*(AppPackages|PackageLayout)` **and** a fresh package
   exists. New gate: a fresh package exists (`LastWriteTime -ge $publishStartTime`) **and**
   `Get-AuthenticodeSignature` on it reports `Status -eq 'Valid'`. The regex is kept, but only to
   label which known failure text was seen inside the `Write-Warning` message — it no longer gates
   anything.
   **Why this is strictly better, not just different:** a real compile error happens before
   packaging ever starts, so no fresh package can exist — the freshness check alone already rules
   that failure mode out. A real signing error produces a package with an invalid or absent
   signature — the signature check rules that out too. So "fresh package + valid signature +
   nonzero exit" can only be a failure *after* both compiling and signing completed, i.e. exactly
   this post-packaging cleanup race, regardless of what the stdout text happened to say. The old
   regex gate's real weakness: MSBuild's error text is locale-dependent (a non-`en-US` toolchain
   or Windows display language emits the message in a different language), so a regex anchored to
   English words could silently stop matching on another machine while the underlying condition
   (successful build, failed cleanup) is identical — the artifact-based gate has no such
   dependency.

3. **`Add-AppxPackage` now runs inside `try { ... -ErrorAction Stop } catch { ...; exit 1 }`** — it
   previously had no error handling at all, so a corrupt or otherwise bad package would have
   silently reported success all the way to the end of the script.

**Verification status: both of the reviewing model's suggested checks are now done, one of them by
real accident.** A live `Deploy.ps1` run right after this fix hit the actual MSB3231 cleanup race
for real — not staged — while building for this same verification pass. The new gate correctly
identified `Archiver.App_1.2.0.11_x64.msix` as fresh and validly signed, tolerated the nonzero
`dotnet publish` exit, installed the package, and the script exited 0: the exact tolerate-and-
continue path the fix was written for, exercised live rather than synthetically. The race recurred
on every single `Deploy.ps1` run made during this fix's verification (three runs total) — strong
supporting evidence for T-F96's leading Search-Indexer-race hypothesis (see the indexing-exclusion
note below), though still not proof by itself. Separately, a deliberate C# syntax error was
introduced in `App.xaml.cs`, `Deploy.ps1` run against it, and confirmed to fail correctly —
`dotnet publish` never reached packaging, no fresh package existed, the gate did not tolerate it,
and the script exited 1 (the error was reverted immediately after via `git checkout`). Both checks
are now closed on T-F102 in `TASKS.md`.

**Follow-up, same day: freshness + a valid signature aren't enough — added a package-content
check.** The user raised a sharp, specific concern: could this tolerance gate let through a
package that's missing content or holds a stale copy of something, and we'd spend time later
hunting a "bug" that was actually just an incomplete package? This is a real risk in this exact
codebase, not a hypothetical — `Archiver.App.csproj` packages `Archiver.Shell.exe` and
`Archiver.ShellExtension.dll` via `Content Include ... CopyToOutputDirectory=PreserveNewest`,
which silently skips the copy if MSBuild's up-to-date check for that one file decides the
destination is already current. `CLAUDE.md`'s own "A quick `dotnet build` can silently install a
stale MSIX" note already documents this exact failure class recurring once before (a 55-minute-old
apphost surviving a rebuild that changed a XAML-bound command) — a valid outer signature doesn't
protect against it, since signing happens on whatever bytes already ended up in the package
layout, complete or not.

**First approach tried, and why it was rejected:** compare each packaged entry's `LastWriteTime`
(read via `System.IO.Compression.ZipFile` without full extraction) against the timestamp captured
right after building `Archiver.Shell.exe`/`Archiver.ShellExtension.dll` this run. Rejected after
inspecting a real known-good package: both entries — built at different times (12:58 and 13:08:53
UTC) — showed an *identical* packaged timestamp (13:09:12 UTC), meaning the zip entry's timestamp
reflects when the packaging step touched the file, not the file's own original mtime. Under that
model, a stale cached copy re-touched during packaging would carry the same "fresh" timestamp as a
genuinely up-to-date one — the check would have been unable to actually detect the failure mode it
was written for.

**What shipped instead: a byte-for-byte SHA256 comparison.** For each of the two satellite files,
compute a SHA256 hash of the packaged zip entry's stream and compare it against
`Get-FileHash` on the corresponding file in `bin\` (the exact file the Content Include item
references). Verified the load-bearing assumption *before* wiring it into the gate, against the
already-built, known-good `Archiver.App_1.2.0.12_x64.msix` sitting on disk from the prior test run
— both files' packaged hash matched their source `bin\` hash exactly. Only then edited
`Deploy.ps1` to use this comparison inside the tolerance branch, and re-ran the full script twice
more (both hit the real MSB3231 race again) to confirm the hash check still passes on a genuinely
good package and doesn't introduce a false rejection.

**Scope of what this does and doesn't prove, stated explicitly to avoid overclaiming:** the hash
check proves the packaged bytes are identical to whatever is in `bin\` *right now*, at the moment
`Deploy.ps1` runs. It does **not** prove `bin\` itself is up to date with source — that is
`dotnet build`'s own incremental-compilation correctness, a separate concern this check cannot
see. Only a flat `.msix` is checked; once 25+ locales force a `.msixbundle` (T-F91), these two
files live one zip level deeper inside a nested inner `.msix`, which this check doesn't unpack —
that combination silently falls back to freshness+signature only, rather than guessing at a
nested path that was never verified against a real bundle.

**Separately, took the user's suggestion to reduce the T-F96 race itself:** set the
`NotContentIndexed` file attribute recursively on the repo root and every subdirectory (no
elevation needed, no security tradeoff, unlike a Defender exclusion). Caveat noted, not yet acted
on: `AppPackages\` is deleted and recreated by every `Deploy.ps1` run, so this specific attribute
does not survive onto that folder's next incarnation — a persistent fix would need Windows
Search's own path-based "excluded locations" list (Indexing Options, or its COM API), which does
require elevation and wasn't attempted this session. Worth watching whether the race stops
recurring now that the stable parts of the tree are excluded; it recurred on all three `Deploy.ps1`
runs made *before* this indexing change, so a clean run afterward would be suggestive (not
conclusive, since it's already documented as intermittent) evidence for the Search Indexer theory.

**Separate, deliberately not done here:** adding the project directory to Windows Defender/Search
exclusions was suggested (by the same reviewing model) as cheap prevention for the underlying
T-F96 race itself. This is a machine-configuration change requiring elevation, not a code change,
and was left for the user to apply directly rather than run from an unattended shell — see T-F96's
existing note that a project-wide Defender exclusion was already reported in place once before;
whether it's still configured on this machine wasn't re-verified as part of this fix.

**Files:** `scripts/Deploy.ps1`.

---

## T-F05 — Archive Browser: tar.exe Selective-Extraction Spike + Subset Compression-Bomb Scope

**Spike, run before writing any subset-extraction code (per this project's "confirmed
empirically" standard for tar.exe behavior):** built a real `.tar` (`root.txt` + `sub/nested.txt`
+ `sub/nested2.txt`) with the real Windows-shipped `C:\Windows\System32\tar.exe` and tested
`-xf archive.tar -C <dest> <members...>` directly.

**Findings:**
- `-tf`'s plain-name listing and `-tvf`'s per-line paths use the exact same path form, including a
  trailing `/` on directory entries (`sub/`, not `sub`). Any selective-extraction argument list
  must match that form exactly — passing `sub` (no trailing slash) for a directory member was not
  tested but should not be assumed equivalent to `sub/`; always build member arguments from the
  literal strings `ListEntriesAsync`/`-tf` already produced, never re-derive them.
- Passing explicit leaf members (e.g. `root.txt sub/nested.txt`) extracts **only** those files —
  `sub/nested2.txt` is correctly left out, and no error occurs even though the containing `sub/`
  directory entry itself was never named as an argument (tar creates the intermediate directory
  implicitly).
- **Passing a directory member (`sub/`) auto-recurses** — it extracts every file nested under it
  (`nested.txt` and `nested2.txt` both appeared), not just an empty directory placeholder. This
  means `ExpandSelection` (T-F05's plan, `TASKS.md:148-264`) does not strictly need to expand a
  selected folder into its full descendant leaf-name list before calling `tar -xf` — passing the
  folder's own path (with trailing `/`, matching `-tf`'s form) is sufficient and tar itself does
  the recursion. Kept the plan's original "expand to explicit descendant names" approach anyway
  (see decision below) rather than relying on this auto-recursion — it is simply a validated
  superset-safe fallback if the plan's expansion logic has a bug, not a load-bearing behavior.
- An unmatched/misspelled member name (`does_not_exist.txt`) makes `tar.exe` exit non-zero
  (`tar.exe: <name>: Not found in archive` / `Error exit delayed from previous errors.`) rather
  than silently skipping it. `TarProcessService`'s subset-extraction code must therefore build its
  member-argument list only from names it already confirmed exist (from the same `-tf`/`-tvf`
  pass `ScanForUnsafeEntriesAsync` already ran), never from unvalidated UI-supplied strings — a
  stale selection (an entry the UI thinks exists but doesn't, e.g. a race with the archive
  changing on disk between listing and extraction) would otherwise fail the *entire* `-xf` call,
  not just the missing member.

**Decision — subset compression-bomb check stays whole-archive-based, not narrowed to the
selection:** `EvaluateCompressionBombAsync` continues to run against the full archive's
`declaredUncompressedSize`/`compressedSize` even when `ExtractOptions.SelectedEntryPaths` narrows
what's actually written to disk. Accepted trade-off, same shape as T-F94's own "aggregate ratio
can miss a small hidden bomb" trade-off above: this can **over-warn** (a huge, otherwise-safe
archive with only a small selected subset may trip a bomb prompt that doesn't reflect what's
actually being extracted) but never **under-warns** (a real bomb entry within the selection is
never let through uncomputed). Under-warning would be the actual regression to avoid; over-warning
is just an extra confirmation dialog. Narrowing the check to sum only the selected entries' sizes
is a valid fast-follow if this proves annoying in practice, but is out of scope for this task —
it would require the check to run after the entry list (and their individual sizes) is already
known, whereas today's whole-archive check runs as a single pass over `-tvf` output before any
per-entry logic exists.

**Files:** `scripts/Deploy.ps1`.

---

## T-F99 — Drive-Root Selection: Three Independent "Assumes a Real Path" Bugs Found While Verifying the Manifest Fix

**What shipped first:** the manifest fix itself — `xmlns:desktop10` declared,
`<desktop10:ItemType Type="Drive">` added alongside the existing `*`/`Directory` entries in
`Package.appxmanifest`, verb wired to the same `PakkoRootCommand` CLSID. This alone made Pakko's
entry appear on a drive-root right-click, matching NanaZip's real shipped manifest.

**Then, verifying it end-to-end on-device (not just checking the menu appears) surfaced three
more bugs, all the same underlying shape — code written assuming a source path always has a
filename component, which a drive root (e.g. `Z:\`) doesn't:**

1. **`QuotePath` (`ShellExtUtils.cpp`) corrupts the command line for a drive-root path.**
   `QuotePath` wrapped every path in `"..."` unconditionally. For `"Z:\"`, the result is `"Z:\"` —
   a trailing backslash immediately before the closing quote. Win32/CRT command-line parsing
   (`CommandLineToArgvW` and equivalents, including .NET's own argv handling) treats a backslash
   run immediately before a quote specially: an *odd* count means the last backslash escapes the
   quote character itself rather than closing the quoted argument. One trailing backslash is odd,
   so the quote never closes — everything after it in the command line gets swallowed into a
   single corrupted argument. Reproduced live: clicking `Compress…` on a `subst`-mapped drive
   root opened Pakko with a completely empty pending list (no error, no crash — just silently
   wrong). Fixed by doubling a trailing backslash before quoting (the standard Win32 quoting rule
   for this exact case) — a two-line change in `QuotePath`, single fix point since every
   `Build*Args` helper routes through it.
2. **`ZipArchiveService.ArchiveAsync`'s null-`ArchiveName` auto-name fallback produces a bare
   `.zip`.** `Path.GetFileNameWithoutExtension("Z:\\")` returns `""` (.NET's real behavior,
   confirmed empirically — this is *not* the same empty-string case dotfiles hit, which the
   existing comment already covered). The fallback only checked `options.ArchiveName ?? ...`,
   which doesn't help when the derived name is `""` rather than `null`. Fixed with an explicit
   length check before falling back to `"archive"`.
3. **`Archiver.Shell/Program.cs`'s `RunArchiveAsync` (the one-click "Add to X.zip" command,
   *not* the `Compress…` dialog — an entirely separate code path that computes its own name and
   destination) had the same empty-name bug independently, plus a second bug:**
   `Path.GetDirectoryName("Z:\\")` returns `null` (a root has no parent), which fell back to `"."`
   — the shell-launched process's own working directory, unpredictable for a COM-surrogate-spawned
   process and never what the user would expect. Fixed the name the same way as (2), and changed
   the destination fallback to `Environment.GetFolderPath(Environment.SpecialFolder.Desktop)` —
   reusing `MainViewModel.cs`'s own existing default-destination convention rather than inventing
   a new one.
4. **`BuildAddToArchiveTitle`'s existing drive-root fallback didn't catch this shape either.**
   The function already had a fallback comment mentioning "a bare drive letter like `C:`" for the
   *multi-file-at-drive-root* case (`GetParentFolderName` returning `"C:"`, caught by
   `name.back() == L':'`). But a *single* drive-root source goes through
   `GetFileNameWithoutExtension` instead, and `PathFindFileNameW(L"Z:\\")` — verified with a small
   standalone probe program compiled and run against the real Windows API, not assumed from
   documentation — returns the **whole string unchanged**, not an empty tail. So `name` ends up
   `"Z:\"`, whose last character is `\`, not `:` — the existing check missed it. Added a
   `name.back() == L'\\'` branch alongside the existing colon check.

**Why this matters beyond just this task:** none of these four bugs were caught by static
reasoning about `GetState`/`EnumSubCommands` (which was the original plan — see `TASKS.md`'s
original T-F99 acceptance criterion wording). They only surfaced by actually invoking the
commands end-to-end on a real (if scratch, `subst`-mapped) drive root and checking the resulting
file on disk, confirming this project's standing practice of not marking shell/UI-touching work
done on manual-verification alone. All four fixed and covered by new tests (C++:
`BuildArchiveArgs.DriveRootTrailingBackslashIsEscaped`,
`BuildAddToArchiveTitle.SingleDriveRootFallsBackToArchive`; C#:
`ArchiveAsync_NullArchiveName_SingleSourceEndingInSeparator_FallsBackToArchive` in
`Archiver.Core.Tests`). `RunArchiveAsync`'s two fixes are *not* independently unit-tested — it's a
top-level-statement local function in `Archiver.Shell/Program.cs`, the same "no test project spawns
the built exe" gap `CLAUDE.md` already documents for this project; verified instead via a real
on-device invocation of the one-click command against the `subst` drive, confirming both the
correct filename (`archive.zip`) and correct destination (Desktop) on disk.

**Files:** `src/Archiver.App/Package.appxmanifest`, `src/Archiver.ShellExtension/ShellExtUtils.cpp`,
`src/Archiver.Core/Services/ZipArchiveService.cs`, `src/Archiver.Shell/Program.cs`,
`tests/Archiver.ShellExtension.Tests/ShellExtUtilsTests.cpp`,
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`.

**User-directed on-device confirmation (2026-07-14), via Windows MCP automation instead of the
user's own hands:** the user explicitly asked the agent to drive the on-device smoke test itself
through the Windows automation MCP tools rather than clicking through it personally. A real
`subst`-mapped `Z:` drive was created, right-clicked in "Цей ПК", and the one-click `Add to
"archive.zip"` command invoked. Confirmed: Pakko's entry appears on the drive-root context menu;
the resulting archive contains the drive's real content (`file1.txt`, correct bytes) — the core
QuotePath/empty-pending-list bug stays fixed. **Two new, minor, unplanned observations from this
pass, not yet acted on:**
1. The archive landed at `Desktop\archive.zip` — matches the documented `RunArchiveAsync` fallback
   (`Environment.SpecialFolder.Desktop`) exactly, so this is expected behavior given the existing
   design, not a regression — but it means a drive-root one-click archive never lands "next to" the
   source the way a normal file/folder selection does, since a drive root has no meaningful parent
   folder. Worth a user-facing affordance someday (e.g. prompting for a destination when the source
   has no parent) but not scoped as a bug fix here.
2. The zip entry itself is stored as `/file1.txt` (leading slash) rather than the conventional
   `file1.txt`. Not yet root-caused — plausibly `ZipArchiveService`'s relative-path computation
   producing an empty prefix for a drive-root source and a downstream `Path.Combine`-style join
   leaving a leading separator. Not fixed this round; flagging for a future look if it ever proves
   to matter (most zip readers tolerate a leading slash, but it's not standard-conforming).
Given this task's acceptance criteria were already satisfied by the 2026-07-13 AI-driven pass, and
this pass adds a second independent on-device confirmation (this time explicitly requested by the
user rather than autonomous), **T-F99 graduates to done** — see `TASKS.md`.

---

## T-F100 — File Activation Routing + FileTypeAssociation Extension List

**Routing fix:** new `FileActivationRouter` static class in `src/Archiver.App.Core/`
(`Decide(IReadOnlyList<string> paths) -> FileActivationDecision`) — WinUI-free and unit-testable,
mirroring `ArchiveTreeIndex`'s existing split for the same reason. A single path whose
`ArchiveFormatDetector.Detect` result isn't `Unknown` routes to Browse (T-F05's
`EnterBrowseModeAsync`); everything else (multi-file, or a single unrecognized file) keeps the
existing `AddPaths` behavior. Wired into `App.xaml.cs`'s `HandleActivation` File case, which is now
`async void` (was `void`) to `await` the browse call deliberately rather than fire-and-forget it —
already on the UI thread from `OnLaunched`, so no dispatcher-marshaling concern.

**Extension-list decision: reuse `ShellExtUtils.cpp`'s existing `kSupportedNonZipArchiveExtensions`
list, not a new one.** That C++ list (`.rar .7z .tar .gz .tgz .bz2 .tbz2 .xz .txz .zst .tzst
.lzma`) already exists as this project's single source of truth for "non-ZIP formats
`Archiver.Core` routes to `ITarService`" — its own comment says it's kept in sync with
`MainViewModel.cs`'s `_extractableTypes`. Extending `Package.appxmanifest`'s
`windows.fileTypeAssociation` with a second `archivefile` extension using this exact list (rather
than NanaZip's much larger ~60-extension list, or independently re-deriving a list from
`TarCapabilities`) means one fewer copy of the same decision to keep in sync — a manifest can't be
conditioned on runtime `TarCapabilities` anyway (it's build-time static), so gating by "what
`tar.exe` can format-detect at all" rather than "what this specific Windows build's `tar.exe`
happens to support today" is the only coherent static choice; the existing capability-gap error
path (`ArchiveListingRouter`/`ExtractionRouter`'s `IsSupported`/`BuildUnsupportedReason`) already
handles the runtime gap for an older Windows without a capable `tar.exe`, unchanged by this task.

**Real, unplanned observation while verifying on-device:** `.zip`'s association had reverted to
Windows' built-in `CompressedFolder` handler (`HKCU:\...\FileExts\.zip\UserChoice`'s `ProgId`) —
Pakko was set as the default per T-F83's confirmation, but that apparently doesn't survive an MSIX
reinstall/reassociation cycle (this session redeployed the package 5 times). Not a bug in this
task's code — Windows' per-user default-app choice is a system setting outside the app's control —
but verification used "Open with → Pakko" (a one-time picker choice, doesn't touch the system
default) instead of relying on double-click, which exercises the exact same `HandleActivation`
code path regardless of which app the OS considers default.

**Files:** `src/Archiver.App.Core/FileActivationRouter.cs`, `src/Archiver.App/App.xaml.cs`,
`src/Archiver.App/Package.appxmanifest`,
`tests/Archiver.App.Core.Tests/FileActivationRouterTests.cs`.

**User-directed on-device confirmation (2026-07-14), via Windows MCP automation:** all four formats
re-verified via "Відкрити за допомогою → Pakko" against the real `PakkoSmokeTest` fixtures —
`.zip`, `.7z`, `.rar`, `.tar.gz` each opened directly into the T-F05 browser (not the archive-
creation UI). Also exercised T-F05's Extract Selected/Extract All from this same entry point across
all four formats (see `TASKS.md`'s T-F05 entry for the full extraction results) — this is the
deepest single-session verification pass this feature has had. **T-F100 graduates to done.**

---

## T-F101 — Classic "Show More Options" Menu Missing Pakko: Fixed (Side Effect, Cause Unconfirmed)

**Per user decision, this round is diagnosis-only** — the symptom is logged and two candidate
explanations are ruled out below, but no code changed.

**Confirmed real** on a fresh Explorer window (new process, to rule out any menu-mode state
carried over from a prior right-click in the same session): the modern top-level menu shows both
NanaZip and Pakko directly; clicking "Показати додаткові параметри" transitions to the classic
Win32 popup menu, which lists every expected classic verb (`Відкрити`, `Відкрити за допомогою`, …,
`Властивості`, `NanaZip`) but **not** Pakko.

**Ruled out — stale installed build.** `Get-AppxPackage` plus a direct read of the installed
`AppxManifest.xml` confirmed the running package matched current source on the relevant
`FileExplorerContextMenus`/`ItemType` block exactly (trivially true this round, since T-F99/T-F100
had just redeployed moments before) — the original bug report's leading theory doesn't explain
this specific repro.

**Ruled out — a crash during classic-menu enumeration.** `Get-WinEvent` against `.NET Runtime` and
`Application Error` providers, filtered to the whole repro window, returned zero events. One
`Application Hang` (ID 1002, `dllhost.exe`, `HangType: Quiesce`,
`PackageFullName: ...Pakko_1.2.0.16...`) did turn up, but its timing and `Quiesce` hang type match
this session's own repeated `Deploy.ps1` uninstall/reinstall cycling (Windows asking the *previous*
version's COM surrogate to quiesce during an MSIX package replace) — not Explorer invoking the
classic menu. Flagged explicitly so a future session doesn't mistake it for evidence.

**Not yet tried (at diagnosis time):** a real Process Monitor/ETW trace of `explorer.exe` actually
calling `IExplorerCommand::EnumSubCommands`/`GetState` during classic-menu population specifically,
to confirm whether Explorer even reaches `Archiver.ShellExtension.dll` for that code path at all.
UI-automation-driven repro turned out to be an unreliable way to *hold open* the classic Win32
popup menu long enough to inspect its state mid-flight (it collapses faster than tool round-trips
in this environment) at diagnosis time.

**Resolution found later (2026-07-14), via Windows MCP automation, user-directed:** re-running the
exact same repro (right-click a file in `PakkoSmokeTest`, click "Показати додаткові параметри")
now shows Pakko present in the classic menu, right after NanaZip — confirmed twice, reproducibly,
via screenshots saved outside the UI-automation tree-walk (see below for why that distinction
mattered). **Root cause still not confirmed** — no code change was made between the 2026-07-13
diagnosis and this pass; the leading hypothesis is that T-F100's `Package.appxmanifest`
`FileTypeAssociation` extension (landed the same day, after this diagnosis) also happened to
invalidate whatever Explorer verb/icon cache was suppressing Pakko from the classic menu, but this
is speculative, not verified against a trace. If this ever regresses, re-check whether it's tied to
a `Package.appxmanifest` change or purely an Explorer-cache artifact (see this project's existing
documented "context-menu flicker on first open of a new Explorer window" cache-artifact precedent
in `CLAUDE.md`).

**Automation note for future sessions attempting to script Explorer context menus:** a UI-Automation
tree-walk (this project's `ui_find`/annotated-`screenshot_control` tooling) issued *between* opening
a Win32 popup menu and clicking an item reliably closed the menu without registering the click —
observed 3 times in a row before switching to a plain (non-`annotate`) screen capture plus manual
pixel-coordinate clicks, which worked every time after. Treat any transient popup menu (context
menus, unpinned flyouts) as allergic to UIA tree enumeration while open; capture pixels only, or
drive it via keyboard navigation instead.

**Files:** none changed — still diagnosis + a later, better-evidenced observation. No code fix
exists to attribute this to.

---

## T-F103 — Extraction Destination Folder Misnamed for Compound Extensions

**Root cause, confirmed exactly as suspected at discovery time:** `Path.GetFileNameWithoutExtension`
(and the C++ `ShellExtUtils.cpp` equivalent of the same name) strip only the last dot segment —
`"browse_test.tar.gz"` became `"browse_test.tar"`. Five call sites shared this bug across three
files: `ZipArchiveService.cs`'s `SeparateFolders`-mode destination and its smart-foldering
wrapper-folder case, `TarProcessService.cs`'s `SeparateFolders`-mode destination, and
`Archiver.Shell/Program.cs`'s `RunExtractHereAsync`/`RunExtractFolderAsync`. A sixth, cosmetic-only
site (`ShellExtUtils.cpp`'s `BuildExtractFolderTitle`, the context-menu title text) had the same bug
independently.

**Fix: one shared helper, not five separate patches.** New `Archiver.Core.Services.ArchiveNaming
.GetBaseName(string archivePath)` strips the five compound extensions `tar.exe` itself can produce
(`.tar.gz`, `.tar.bz2`, `.tar.xz`, `.tar.zst`, `.tar.lzma`) as a unit before falling back to
`Path.GetFileNameWithoutExtension` for everything else — mirrors the existing
`_extractableTypes`/`kSupportedNonZipArchiveExtensions` "one canonical list, kept in sync across
C#/C++" precedent from T-F86/T-F100. All three C# call sites (already in or referencing
`Archiver.Core`) now call this one method instead of duplicating the logic. The native
`GetFileNameWithoutExtension` in `ShellExtUtils.cpp` got the equivalent fix (a `kCompoundArchiveExtensions`
array checked before the single-dot fallback), kept in sync via a cross-reference comment since the
two codebases can't literally share code.

**Bonus, not scope creep — fixing the shared C++ helper also fixed the archiving-direction case for
free.** `GetFileNameWithoutExtension` is used by *both* `BuildExtractFolderTitle` (extraction,
T-F103's actual scope) and `BuildAddToArchiveTitle` (archiving a source file, a different direction
this task never targeted). Since both title builders route through the one helper, fixing it once
also fixes an out-of-scope inverse bug (archiving a file literally named `backup.tar.gz` would have
produced `backup.tar.zip`) as a side effect, at zero extra cost — this is presented as a fix that
happened to occur, not a deliberate scope expansion.

**Testing:** new `ArchiveNamingTests` (12 theory cases covering every compound extension plus the
single-extension formats, case-insensitivity), a `ZipArchiveServiceExtractTests` case (a zip
literally named `browse_test.tar.gz`, since the bug is about the file-name string, not the actual
archive format), a real `TarProcessService` integration test exercising `SeparateFolders` mode
against a genuine `tar.exe`-built `.tar.gz` fixture (this mode was never previously covered by
`TarProcessServiceCompressedFormatsTests.cs` — every existing test there used `SingleFolder` with an
explicit `destDir`, which is exactly why this bug shipped uncaught until real on-device use), and
two new C++ Google Test cases. `dotnet test --filter "Category!=Slow"` green (235/235, +14) and
`Archiver.ShellExtension.Tests.exe` green (59/59, +3).

**On-device verification (2026-07-14), user-directed via Windows MCP automation:** all three
previously-buggy paths re-tested against the real `browse_test.tar.gz` fixture after a full
`Deploy.ps1` build+sign+install — the shell's own context-menu title now reads
`Extract to "browse_test\"` (was `browse_test.tar\`); "Extract to folder" and "Extract here" both
produced correctly-named folders (`browse_test\` / a numbered `browse_test (1)\` where a
same-named folder already existed from an earlier test); the Archive Browser's Extract All routed
its content into the correctly-named folder too (confirmed via a `root (1).txt` rename-on-conflict
landing in the right place). A stale `browse_test.tar\` folder from the original 2026-07-13 bug
repro was left untouched on disk during this pass — its own mtime confirmed nothing wrote to it,
distinguishing "still there from before" from "the bug is still creating it."

**Files:** `src/Archiver.Core/Services/ArchiveNaming.cs` (new),
`src/Archiver.Core/Services/ZipArchiveService.cs`, `src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.Shell/Program.cs`, `src/Archiver.ShellExtension/ShellExtUtils.cpp`,
`tests/Archiver.Core.Tests/Services/ArchiveNamingTests.cs` (new),
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceExtractTests.cs`,
`tests/Archiver.Core.IntegrationTests/TarProcessServiceCompressedFormatsTests.cs`,
`tests/Archiver.ShellExtension.Tests/ShellExtUtilsTests.cpp`.

---

## T-F05 — UI Design-Review Pass: Row 0 Visibility Bug, Top Command Bar, Window Proportions

**Trigger:** the user, looking at a real on-device screenshot of the Archive Browser, asked
whether Row 0's Add Files/Add Folder/Hash buttons belonged there, and asked for a comparison
against NanaZip's real archive-viewing UI (NanaZip is installed on the dev machine — opened the
same `.7z` fixture in both apps for a direct side-by-side).

**Diagram gap closed first.** Before touching any code, the user asked to catalog what diagrams
should exist for "all the menu elements in Pakko" — confirmed none of `DIAGRAMS.md`'s 5 existing
categories covered a WinUI window's own row-visibility state machine (diagram 2 is scoped
specifically to `IsBusy`/operation lifecycle, not to `IsBrowsingArchive`-driven layout). Added
diagram 6 (`DIAGRAMS.md`) with a per-row visibility table built directly from `MainWindow.xaml`'s
8 rows and `MainViewModel.cs`'s visibility properties — drawing it surfaced the Row 0 gap formally
(it had already been spotted visually, but the diagram is what pinned down that it was the *only*
row of 8 with no `Visibility` binding at all, and that `MainViewModel.cs:209-212`'s own comment
listing "intentionally shared" elements never mentioned it).

**Finding 1 — Row 0 never hid in browse mode; real bug, not a documented choice.** Fixed by
splitting Row 0 into two mode-gated sibling `Grid`s (same pattern Rows 1/3 already used):
pending-list variant unchanged (Add Files/Add Folder/Hash/About, now behind
`IsPendingListVisibility`), new browse variant behind `IsBrowsingArchiveVisibility`.

**Finding 2 — where should Info/Close live, given the new browse-mode Row 0 exists anyway?**
The user asked (referencing WinRAR/7-Zip/NanaZip and modern Windows 11 Explorer) whether *all*
Archive Browser actions should move above the table, text-labeled rather than icon-only. Consulted
the `frontend-design` skill on this specifically before touching XAML. The answer was not a
blanket "yes, move everything": WinRAR/7-Zip/NanaZip's own top-toolbar buttons work because they
each open a **self-contained dialog** that carries its own destination/options — clicking the
button and configuring the action happen in the same place. Pakko's model is different and
deliberate (T-F05's original design): destination path, conflict behavior, and the two checkboxes
stay **inline and persistent** below the list, not in a per-click dialog. Moving `Extract Selected`/
`Extract All` to the top while their configuration stays at the bottom would create a "configure
below, commit above" flow — worse than today's shape, not better, since today at least the buttons
sit immediately after their own inputs. `Info` and `Close`, by contrast, are non-committing/
navigational (view metadata; leave the browser) and don't consume anything below them — those two
moved into the new browse-mode Row 0, text-labeled, matching modern Explorer's command-bar-above-
the-list convention; `Extract Selected`/`Extract All` stayed in Row 3, now alone.

**Finding 3 — window proportions.** `MainWindow.xaml.cs` hardcoded `AppWindow.Resize(800, 700)` —
near-square. Every reference file manager compared (Explorer, NanaZip's real 1440×753 window,
7-Zip, WinRAR) defaults to wide-not-square, because a file/archive listing is inherently tabular —
the Name column wants width, rows don't want height. Confirmed concretely from the same `.7z`
comparison screenshots: Pakko's narrower Name column would truncate long/nested archive entry
names (this project explicitly has Unicode/emoji-filename and deep-nesting test coverage) more
aggressively than necessary. Changed to `1100x650` — wider than the old size but well short of
NanaZip's own 1440px, since Pakko's simpler 3-column list (Name/Size/Modified vs. NanaZip's 11
columns) doesn't need that much width; the window is still user-resizable beyond this, this only
changes the initial size.

**Explicitly not done, and why:** did not adopt NanaZip's broader toolbar (Add/Copy/Cut/Delete-
in-archive/Test icons) or its column set (CRC/Method/Attributes/Block/Folders/Files) — both
conflict with T-F05's own already-decided "not an archive manager" scope. NanaZip was used here
purely as a reference for layout/information-density conventions (address-bar-style path display,
column choices, top-vs-bottom action placement), never as a feature target to match.

**Verification:** AI-driven on-device, same session — redeployed, confirmed via screenshot that
Row 0 correctly shows the browse-mode variant (Info greyed until a selection exists, Close, About)
with the pending-list variant's buttons gone; confirmed `Info` opens the metadata dialog and
`Close` correctly exits back to pending-list mode (Row 0/Row 3/Row 5 all correctly flip back) from
their new position; confirmed the wider window via `AppWindow` bounds. No new automated tests —
this is a WinUI layout/visibility change with no non-UI logic to unit-test; the existing
`IsPendingListVisibility`/`IsBrowsingArchiveVisibility` properties themselves are unchanged, only
which `Grid` rows bind to them.

---

## T-F05 — Follow-up: Info Button Removed, Compressed/Full Size Added as Table Columns

**Trigger:** user feedback on the Row 0 change directly above — the `Info`+`Close` pair sitting
together in the new browse-mode command bar read as a confusing combination, not an improvement.
Separately, the user asked for compressed size and full (uncompressed) size to be visible per
entry in the browse table.

**Resolution: delete the Info dialog, fold its fields into the table.** Read `DialogService.cs`'s
`ShowEntryInfoAsync` to see exactly what it showed: Name, Path, Type (Folder/File), Size,
Compressed size (guarded `> 0`), Modified. Cross-checked against what the browse-mode entry table
(`MainWindow.xaml` Row 1) already carries: Name and Modified were already columns, Path is already
the row's tooltip (`ToolTipService.ToolTip="{x:Bind FullPath}"`), and Type is already conveyed by
the folder/file `FontIcon`. Only `Size` (uncompressed) and `Packed` (compressed) were missing as
columns — added both, which makes the entire Info dialog redundant rather than partially
redundant. Deleted outright rather than leaving it reachable some other way (e.g. a right-click
"Properties" item): `IDialogService.ShowEntryInfoAsync`, `DialogService.ShowEntryInfoAsync`,
`MainViewModel.ShowSelectedEntryInfoCommand`/`CanShowSelectedEntryInfo`, the `EntryInfoButton` XAML
button and its `Resources.resw` string. This also mechanically resolves the "combination" — Row 0
(browse) is now just `Close` + `About`, no pairing to be confused by.

**`CompressedSizeDisplay` guards `<= 0`, not just folders.** Read `TarProcessService.cs`'s listing
path (`ScanForUnsafeEntriesAsync`'s sibling) before writing this: it hardcodes
`CompressedSize = 0` for every entry, because tar.exe/libarchive's gzip/xz/bzip2/zstd streams are
whole-archive, not per-entry, so there's no real per-entry compressed size to report for any
tar-routed format (RAR/7z/tar/tar.gz/tar.bz2/tar.xz/tar.zst). `DialogService`'s old Info dialog
already worked around this with an `if (entry.CompressedSize > 0)` guard that hid the row
entirely for these formats; a column can't hide itself the same way, so `CompressedSizeDisplay`
carries the same `<= 0` guard and renders empty instead of a misleading `0 bytes`. Net effect:
the `Packed` column is ZIP-only in practice — real information for `.zip`, blank for everything
else Pakko extracts. This is disclosed here and in `ARCHITECTURE.md` rather than silently shipped;
no attempt was made to backfill a synthetic per-entry compressed size for tar-routed formats since
none exists to report honestly.

**Column alignment fixed while touching the same `Grid`s.** The browse-mode header `Grid`
(`ColumnDefinitions="*,100,140"`, no icon column) never matched the row template's
`ColumnDefinitions="Auto,*,100,140"` (icon + 3 columns) — the header's `Name` column was
consequently narrower than the row's actual name cell by one `Auto` icon-width. Not a
pre-existing bug worth a separate task: both `Grid`s were already being edited to add the
`Packed` column, so aligning them (`Auto,*,100,100,140` on both) was folded into this change.

**Verification:** `dotnet test --filter "Category!=Slow"` green (added
`ArchiveEntryViewModelTests` covering `CompressedSizeDisplay`'s folder/zero/positive cases).
On-device verification pending — same standing rule as every other UI change this session: not
marked done until confirmed via a redeploy + screenshot pass.

**Files:** `src/Archiver.App/MainWindow.xaml`, `src/Archiver.App/MainWindow.xaml.cs`,
`DIAGRAMS.md` (new diagram 6 + DoD table row + diagram 1 update for the same-day T-F99 finding).

---

## T-F05 — Second Follow-up: Close Removed, CRC-32 Column, Destination Up-Button, Localization Pass

Four separate user requests, batched into one round because all four touch `MainWindow.xaml` and
would otherwise force a second Deploy.ps1 cycle for no benefit (this session's own notes already
document Deploy.ps1's build+sign+install cost — batching is the standing practice for that reason
alone, not new to this round).

**1. Close button removed; replaced by a single up-arrow that also exits the browser.** The user
confirmed (via direct question) that the standalone Close button — kept after Info's removal in
the prior round — should also go, but flagged unprompted that this leaves *no* way back to the
pending list (the window's own "X" closes the whole app, not just the browser). Two options were
put to the user before writing any code (advisor flagged this as a genuine blocker, not a call to
make solo): (a) an up-arrow in front of the breadcrumb that navigates up a folder level, and at
the archive's own root falls through to exiting the browser; (b) a small icon-only close button
in the same spot Close occupied. The user picked (a) and asked for the same up-arrow pattern to
also apply to archive creation (i.e. the Destination Path row, which is already shared by both
modes — see point 3 below).
`MainViewModel.NavigateUpOrExitBrowser` (new `[RelayCommand]`) implements this:
`CurrentFolderPath.Length == 0` → call the now-private `ExitBrowseMode()` (no longer its own
command); otherwise → `NavigateToBreadcrumbSegment(BreadcrumbSegments.Count - 2)`, which is
exactly the target the *second-to-last* breadcrumb segment already resolves to. Row 0 (browse)
now holds only `About`. `EntryInfoButton`/`CloseArchiveBrowserButton` were already gone from
`en-US`'s `Resources.resw` (Info) or removed this round (Close).

**2. CRC-32 column added — nullable, not a `<= 0` sentinel like `CompressedSizeDisplay`.** Unlike
compressed size, `0` is a legitimate CRC-32 (an empty file legitimately hashes to `0x00000000`),
so reusing the `<= 0` "not available" pattern from the prior round's `CompressedSizeDisplay` would
have silently mislabeled a real all-zero CRC as missing — caught by the advisor before writing any
code, not found in testing. `ArchiveEntryInfo.Crc32` (`Archiver.Core`) and
`ArchiveEntryViewModel.Crc32` (`Archiver.App.Core`) are both `uint?`; `null` means unavailable
(every tar-routed format — same "no per-entry concept" reason as `CompressedSize`), any value
including `0` means a real, verified CRC. `ZipArchiveService.ListEntriesAsync` populates it from
`ZipArchiveEntry.Crc32` (already used elsewhere in the same file for `TestArchiveEntries`'s
integrity check); `TarProcessService`'s listing path leaves it unset. `CrcDisplay` renders as
uppercase 8-digit hex (`X8` format) — this is the field a government/defense/auditability
audience is most likely to read directly, so "verified zero" vs. "unknown" needed to stay
distinguishable, not collapse into the same blank cell.

**3. Destination Path gets its own up-button — a separate feature from #1, despite the visual
symmetry.** User request, independent of the archive browser: a small icon button to the left of
the Destination Path `TextBox` (Row 2, shared by both Archive and Extract flows) that goes up one
real filesystem folder level, disabled at a drive root. Implemented via the framework rather than
string parsing — `Path.GetDirectoryName(DestinationPath) is null` is the drive-root/unrooted-path
signal (confirmed this returns `null` at `"C:\"`, not an empty string or an exception), used both
as the click handler's action and as `CanNavigateDestinationUp`'s `CanExecute` gate (also checks
`!IsBusy`, matching the existing "..." browse button's disable-while-busy behavior). Deliberately
**not** wired to the archive browser's up/exit command — same icon, same visual language, but two
functionally unrelated targets (real filesystem vs. archive-internal path); worth flagging since a
future reader could otherwise assume one implementation covers both.

**4. Localization completion pass — `en-US` + `uk-UA` only, matching T-F91's existing design.**
User reported (correctly) that not all menu elements were translated. Audit of `MainWindow.xaml`
found two distinct gaps: (a) `ExtractSelectedButton`/`ExtractAllButton` already had `x:Uid`s and
`en-US` entries from T-F05's original implementation, but were never given `uk-UA` translations —
a straightforward oversight, fixed by adding the two missing `uk-UA` entries; (b) a much larger set
of controls were built with hardcoded `Content`/`Text`/`PlaceholderText` string literals that never
routed through the resource system at all, so they displayed in English regardless of locale —
the tray context menu (`Open Pakko`/`About Pakko`/`Exit`), `Hash...`/`About` buttons, both tables'
column headers (`Name`/`Type`/`Size`/`Modified`, plus the new `Packed`/`CRC-32`), the pending
list's `Remove` context-menu item, `Mode:`/`One archive`/`Separate archives`, the archive-name
placeholder text, the compression level items (`Fast`/`Normal`/`Best`/`None`), the conflict-
behavior items (`Overwrite`/`Skip`/`Rename (add number)`), and the `✕ Cancel` button. All were
converted to `x:Uid` and given both `en-US` (canonical) and `uk-UA` (translated) `Resources.resw`
entries; the other 22 locale folders were left as-is, relying on the existing documented
fallback-to-`en-US` behavior for any key a locale doesn't define (T-F91) — not creating 22 new
files for keys that were never localized to begin with. The `NameColumnHeader`/`SizeColumnHeader`/
`ModifiedColumnHeader` keys are shared between a `Button` (pending-list sort header, resolves the
`.Content` suffix) and a `TextBlock` (browse-mode header, resolves `.Text`) — both suffixes were
added per shared key rather than duplicating the string under two different key names.
`ComboBoxItem`/`RadioButton` selections all bind via `SelectedIndex`/`GroupName`+`IsChecked`, not
by matching text, so translating their display text is logic-safe (confirmed by reading
`MainViewModel.cs`'s `CompressionLevelIndex`/`OnConflictIndex` before touching any of them).

**Non-obvious tooling note, unrelated to the feature but hit while writing diagram 6's update:**
typing a raw Segoe MDL2 Assets icon glyph (a Private-Use-Area Unicode character, e.g. U+E74A)
directly into an Edit/Write tool call's content is unreliable — it can render as invisible/empty
between two backticks and then fail to match on a later `Edit` even when `Read` shows what looks
like the same text. Byte-level (`xxd`) inspection confirmed the character was written correctly
the first time; retyping it doesn't reproduce identical bytes. Fixed via a small Python script
(codepoint expressed as a `\uXXXX` escape in the *script's own source*, not as a raw character) —
see `feedback_pua_glyph_corruption` in the assistant's memory. Also noted: this machine's `python3`
is a Microsoft Store stub (exits 49, prints nothing); the `py` launcher is the real interpreter.

**Correction — on-device crash found by the first real launch, root-caused and fixed same round.**
`dotnet build`/`dotnet test` passing (and even a `dotnet build`-triggered MSIX install reporting
"installed successfully") never actually launches the app — this session's own earlier notes
already say a quick `dotnet build` can silently install a stale MSIX, but the deeper point proven
here is broader: **compiling and installing a WinUI package proves nothing about whether it can
run.** The very first real launch of this round's build (via `Start-Process` on the installed
`Archiver.App.exe`, not Explorer/shell activation) hard-crashed immediately after
`OnLaunched`/`MainWindow.InitializeComponent()` — confirmed via `pakko.log` showing only
`Pakko started` (no further lines) and a `ProviderName='Application Error'`, event ID 1000, exit
code `0xc000027b`, faulting module `Microsoft.UI.Xaml.dll`. This is a native fail-fast that bypasses
any managed `try/catch` or `App.UnhandledException` handler, so nothing else in the app's own
logging could have caught it. Root cause (found via advisor before spending a second deploy cycle
on a guess, per this repo's 3-attempt rule): two invented-this-round `x:Uid` patterns, both wrong.
(1) `NameColumnHeader`/`SizeColumnHeader`/`ModifiedColumnHeader` were given *both* a `.Content` and
a `.Text` `Resources.resw` entry under the same `x:Uid`, on the assumption each element (a `Button`
with `.Content`, or a `TextBlock` with `.Text`) would only pick up the suffix matching its own
property. It doesn't — WinUI's x:Uid resource applier applies every key found under that `Uid`
to the element regardless of which properties the element actually has, and setting `.Text` on a
`Button` (no such DP) or `.Content` on a `TextBlock` (no such DP) is fatal at `InitializeComponent`
time, not just a no-op. Fixed by giving every header its own fully separate key — the pending-list
`Button` headers kept `NameColumnHeader`/`TypeColumnHeader`/`SizeColumnHeader`/`ModifiedColumnHeader`
(`.Content` only), the browse-mode `TextBlock` headers became distinct
`BrowseNameColumnHeader`/`BrowseSizeColumnHeader`/`BrowseModifiedColumnHeader` (`.Text` only) even
though the displayed string is identical in both. (2) The two up-arrow buttons' tooltips used
`Uid.[ToolTipService.ToolTip]` — no other `.resw` in this repo had ever used the bracket syntax for
an attached property, and it turned out to be the wrong form (or at least unverified against a
working reference, which the project's own pre-implementation-research rule requires and this
change skipped). Fixed by dropping the `x:Uid` on both buttons entirely and hardcoding
`ToolTipService.ToolTip="Up"` inline — tooltip text on an icon-only "Up" button is the least
valuable thing to localize in this whole round, so it was cut rather than risked a second time.
Confirmed fixed by relaunching the freshly redeployed package (1.2.0.21) directly — no crash,
`pakko.log` shows a normal steady-state session, and all four features (up-arrow-only Row 0,
CRC-32 column populated for ZIP/blank for tar-routed fixtures, destination up-button, Ukrainian
text throughout) verified on-device via screenshots. **Lesson for future `x:Uid` work in this
repo:** never share one `Uid` across elements of different types/property sets, and don't invent
a resource-key syntax without checking a working WinUI/UWP reference first — the same
pre-implementation-research discipline `CLAUDE.md` already mandates for COM/shell/packaging work
applies just as much to XAML resource wiring, which this round treated as low-risk and shouldn't
have.

**Verification:** `dotnet test --filter "Category!=Slow"` green (221/221 — added `CrcDisplay`
folder/null/zero/positive cases to `ArchiveEntryViewModelTests`, alongside the prior round's
`CompressedSizeDisplay` tests). On-device verification now actually completed (not just deployed) —
confirmed via direct launch + screenshots as described above.

**Files:** `src/Archiver.Core/Models/ArchiveEntryInfo.cs`, `src/Archiver.Core/Services/ZipArchiveService.cs`,
`src/Archiver.App.Core/ArchiveEntryViewModel.cs`, `src/Archiver.App.Core/ArchiveTreeIndex.cs`,
`src/Archiver.App/ViewModels/MainViewModel.cs`, `src/Archiver.App/MainWindow.xaml`,
`src/Archiver.App/Strings/en-US/Resources.resw`, `src/Archiver.App/Strings/uk-UA/Resources.resw`,
`tests/Archiver.App.Core.Tests/ArchiveEntryViewModelTests.cs`, `ARCHITECTURE.md`, `DIAGRAMS.md`.

## T-F05 — Third Follow-up: Pending-List CRC-32, a Real Blank-Row Regression Found and Fixed, Large-Entry-Count Review

User asked three things in one message: (1) add CRC-32 to the pending (archive-creation) list too,
not just the archive-browser table; (2) make the hash computation itself async/lazy so it never
blocks the UI and updates progressively; (3) whether Pakko has display problems when a folder
contains "too many files to display" — a bomb-like concern, but for entry *count* rather than
compression ratio.

**1+2. Pending-list CRC-32.** `Archiver.Core.IO.Crc32` (T-F94-era ZIP-bomb/integrity code, already
internal) was changed from `internal` to `public` — reused as-is by `Archiver.App`'s `FileItem`
rather than adding a second implementation or a hashing NuGet package (`Archiver.Core` takes none).
`FileItem` gained `Crc32`/`Crc32Display` (`ObservableProperty`, same null-vs-`<=0` reasoning as the
browse-mode column — 0 is a legitimate CRC, so the sentinel is `null`, not `<= 0`) and a
`LoadCrc32Async` method matching the existing `LoadFolderSizeAsync` fire-and-forget-from-constructor
pattern: starts immediately for every file (not deferred/"lazy" in the sense of waiting for
scroll-into-view — matching the existing size-loading precedent), but throttled through a new
`static readonly SemaphoreSlim _crc32Throttle = new(4)` shared across all `FileItem` instances, so
adding many/large files at once can't turn into an unbounded concurrent-disk-read storm. A
`_crc32Throttle` static field holds no data (only a synchronization primitive), so it isn't the kind
of mutable service state `CLAUDE.md`'s "no static mutable fields" rule targets.

**3. Investigated `ArchiveTreeIndex.Build`/`CurrentFolderEntries`** (the browse-mode path a huge
archive would stress) before assuming a fix was needed. Confirmed: `Build` is a single O(n) pass
over the flat entry list (already had to be, for T-F20's 65,000+-entry Zip64 fixtures); sorting is
scoped to one folder's own children, not the whole archive, so one folder holding many entries
doesn't turn into a whole-archive sort; per-navigation lookup is an O(1) dictionary read, no re-scan.
`CurrentFolderEntries` renders through the browse-mode `ListView`, which already had an explicit
`VirtualizingStackPanel` (`VirtualizationMode="Recycling"`) predating this session. `ArchiveEntryViewModel`
does no per-item async work — its `Crc32`/sizes come straight from `ArchiveEntryInfo`, already
computed during `ListEntriesAsync`, unlike `FileItem`'s new per-file disk read. **No entry-count
"bomb" problem was found for archive browsing; no guard was built, since none was warranted.**

**A real regression WAS found while testing part 1, though — this is the concrete answer to part
3.** Adding a large file (a 20 MB test file) together with a second, small file in the same
"Add Files" batch left the second row visually and UIA-blank (no Name/Type/Size/CRC/Modified text
in either the rendered UI or the accessibility tree) until something forced a re-layout (e.g.
resizing the window), even though the underlying `FileItems` collection and count were always
correct throughout (confirmed via the on-screen "will be archived: N items" summary, which reads
straight from the collection, not from rendered rows) — a container-realization/binding race, not
data loss. Root cause: the pending-list `ListView` had *also* been given an explicit
`<VirtualizingStackPanel VirtualizationMode="Recycling"/>` `ItemsPanel` this session (added
alongside the CRC column work) where none existed before — `ListView` already virtualizes by
default via its own `ItemsStackPanel`, so the explicit panel added no capability, only introduced a
new interaction between container recycling and a `PropertyChanged` notification landing mid-
realization (the large file's ~100 ms `Task.Run` CRC read widens the timing window enough for a
second item added in the same synchronous loop to hit it — small-file-only batches don't reliably
reproduce it). Fixed by reverting the pending-list `ListView` to its previous implicit/default
panel (`MainWindow.xaml`) — one attempt, confirmed on-device: re-added a fresh small file
(`browse_test.zip`) as the second item in a batch, and its full row text was present in the
accessibility tree immediately, with no resize needed (previously this same scenario left the row
blank). The browse-mode `ListView`'s own explicit `VirtualizingStackPanel` predates this session and
was left as-is — it was never implicated (that view has no per-item async binding at all).

**Verification:** `dotnet test --filter "Category!=Slow"` green (221/221, no test changes needed —
this was a XAML-only revert). Deployed as 1.2.0.23 (the T-F96 MSB3231 publish-exit-code race
recurred and was tolerated as designed, per T-F102 — no new action taken). Confirmed via direct
`Start-Process` launch (not just a green build, per this session's own earlier lesson): app opens
without crashing, CRC-32 populates correctly for real ZIP/RAR/tar.gz/large-binary fixtures, and the
blank-row repro no longer reproduces for a freshly-added batch.

**Files:** `src/Archiver.Core/IO/Crc32.cs`, `src/Archiver.App/Models/FileItem.cs`,
`src/Archiver.App/ViewModels/MainViewModel.cs` (sort-by-CRC), `src/Archiver.App/MainWindow.xaml`
(CRC column + `ItemsPanel` revert), `ARCHITECTURE.md`.

---

## T-F06 — Ask on Conflict Dialog

**Designed via Plan Mode (2026-07-14), approved plan saved at
`C:\Users\Pa\.claude\plans\floofy-swimming-sifakis.md`.** Adds a 4th `ConflictBehavior` value,
`Ask`, alongside the existing `Overwrite`/`Skip`/`Rename` — resolved per-conflict via an
interactive dialog instead of one blanket rule chosen before the operation starts.

**Reused the existing T-F94 Core→UI callback precedent rather than inventing a new pattern.**
`ConfirmCompressionBombExtraction` (`Func<CompressionBombWarning, Task<bool>>?`) already
established the shape: a nullable delegate on the options record, invoked from a background
thread inside `ZipArchiveService`/`TarProcessService`, implemented in `DialogService` via
`_window.DispatcherQueue.TryEnqueue` wrapping a `TaskCompletionSource` (since `ContentDialog
.ShowAsync()` requires the calling thread to own the DispatcherQueue). The new
`ResolveConflictAsync` (`Func<ConflictInfo, Task<ConflictDecision>>?`) on both `ArchiveOptions`
and `ExtractOptions` follows the identical shape.

**One shared `ConflictBehavior` enum, not a parallel toggle.** `ConflictBehavior` is a
**shared** option between archive-creation and extraction per this project's own scope-rules
table, bound to a single `ComboBox` in `MainWindow.xaml`. Research before implementing (see the
approved plan) found all four existing conflict-resolution call sites are on sequential (never
parallel) code paths — including `ZipArchiveService.ArchiveAsync`'s `SeparateArchives` mode,
whose conflict pre-pass runs as a plain `foreach` *before* `Parallel.ForEachAsync` starts, not
inside it — so `Ask` could be wired everywhere the enum is already used, not just extraction,
with no half-finished direction.

**New shared internal `Archiver.Core.Services.ConflictResolver`, one instance per
`ArchiveAsync`/`ExtractAsync` call, constructed before any loop:**
```csharp
internal sealed class ConflictResolver(
    ConflictBehavior configured,
    Func<ConflictInfo, Task<ConflictDecision>>? resolveConflictAsync)
{
    private ConflictResolution? _sticky;

    public async Task<ConflictBehavior> ResolveAsync(string existingPath)
    {
        if (configured != ConflictBehavior.Ask) return configured;
        if (_sticky is { } sticky) return Map(sticky);
        if (resolveConflictAsync is null) return ConflictBehavior.Skip;

        var decision = await resolveConflictAsync(new ConflictInfo { ExistingPath = existingPath })
            .ConfigureAwait(false);
        if (decision.ApplyToAll) _sticky = decision.Resolution;
        return Map(decision.Resolution);
    }
    // Map: Overwrite→Overwrite, Rename→Rename, else→Skip
}
```
Returning `ConflictBehavior` (not `ConflictResolution`) from `ResolveAsync` is the key trick:
every existing `switch (options.OnConflict)` at the four call sites becomes
`switch (await conflictResolver.ResolveAsync(existingPath).ConfigureAwait(false))` — the
Skip/Overwrite/Rename branches themselves are completely unchanged, and `Ask` never reaches them
(the existing switches have no `default`, so this compiles cleanly without one).

**"Apply to all" scope is deliberately the whole operation, not just the current archive** —
a direct consequence of constructing one `ConflictResolver` before the outer loop rather than
inside it. Extracting 3 archives at once: choosing "apply to all" on archive 1's conflict
suppresses the dialog for archives 2 and 3 too, not just further entries within archive 1. Zip
and tar-family archives are still two independent `ExtractionRouter` calls, each with its own
resolver instance — a mixed zip+tar-family selection does not share "apply to all" across the
format boundary. Accepted as a documented scope cut, not engineered around, since the same
per-format-independence already exists elsewhere in this codebase (e.g. capability detection).

**`ContentDialogResult.None` (Escape, or the Close-button click that Skip itself uses — the two
are indistinguishable) maps to `Skip`.** `ContentDialogResult` only has three values
(`None`/`Primary`/`Secondary`), so `Primary⇒Overwrite, Secondary⇒Rename, _⇒Skip` is the only
consistent mapping — matches the existing `_ ⇒ negative answer` convention already used in
`ShowConfirmAsync`. Also set `DefaultButton = ContentDialogButton.Close` on the new dialog so
pressing Enter resolves to Skip, not the more destructive Overwrite — deliberate, since Overwrite
being the *easiest* accidental keystroke would be the wrong default for a conflict prompt.

**`SeparateArchives` mode's existing T-F12 same-run-collision rule still applies under `Ask`.**
If two sources in one archive-creation batch share a basename, T-F12 already deliberately renames
rather than overwrites even when the configured behavior is `Overwrite`, to avoid a parallel-write
race between two workers. This still fires when the user explicitly picks Overwrite through the
Ask dialog for that specific collision — surprising in isolation, but it is the same accepted
deviation T-F12 already introduced for the non-Ask path, just now user-triggerable. Not treated
as a new bug.

**Testing:** `ConflictResolverTests` unit-tests the resolver in isolation (non-`Ask` passthrough,
null-callback default, per-resolution mapping, `ApplyToAll` suppressing further callback
invocations — asserted by invocation *count*, not just final state). Ask-mode cases were added to
`ZipArchiveServiceArchiveTests` (both `SingleArchive` and `SeparateArchives`, the latter with an
`ApplyToAll` case across two conflicting sources), `ZipArchiveServiceExtractTests` (per-entry, a
zero-conflicts case proving the callback never fires spuriously, and a multi-archive `ApplyToAll`
case that specifically exercises the "resolver constructed once outside the outer loop" decision
above), and `TarProcessServiceExtractTests` (same shape, against real `tar.exe`). `dotnet test
--filter "Category!=Slow"` green (254/254, +19 new). App-layer wiring
(`IDialogService.ShowConflictDialogAsync`, `MainViewModel`, `MainWindow.xaml`,
`en-US`/`uk-UA` `Resources.resw`) has no automated test coverage, per this repo's existing
"no ViewModel test project" convention — verified manually per `TASKS.md`'s remaining acceptance
criterion.

**Files:** `src/Archiver.Core/Models/ConflictInfo.cs` (new),
`src/Archiver.Core/Models/ConflictDecision.cs` (new),
`src/Archiver.Core/Models/ArchiveOptions.cs`, `src/Archiver.Core/Models/ExtractOptions.cs`,
`src/Archiver.Core/Services/ConflictResolver.cs` (new),
`src/Archiver.Core/Services/ZipArchiveService.cs`, `src/Archiver.Core/Services/TarProcessService.cs`,
`src/Archiver.App/Services/IDialogService.cs`, `src/Archiver.App/Services/DialogService.cs`,
`src/Archiver.App/ViewModels/MainViewModel.cs`, `src/Archiver.App/MainWindow.xaml`,
`src/Archiver.App/Strings/en-US/Resources.resw`, `src/Archiver.App/Strings/uk-UA/Resources.resw`,
`tests/Archiver.Core.Tests/Services/ConflictResolverTests.cs` (new),
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceArchiveTests.cs`,
`tests/Archiver.Core.Tests/Services/ZipArchiveServiceExtractTests.cs`,
`tests/Archiver.Core.IntegrationTests/TarProcessServiceExtractTests.cs`.

---

## Correction — T-F13 Superseded by T-F52 (Sandbox Tasks Merged)

**Symptom:** user flagged, unprompted, that T-F13 ("Process Sandbox Isolation for External
Binaries") and T-F52 ("Low IL Sandbox for tar.exe") looked like they might be the same task and
asked for a plan before either was picked up.

**Root cause:** T-F13 was written while the project still planned to bundle optional third-party
binaries (`7z.exe`/`unrar.exe`, then-tasks T-F07/T-F08) and scoped its threat model around a
downloaded, SHA-256-verified binary that could be compromised between verification and execution.
That premise stopped holding 2026-07-12, when T-F07/T-F08 were cancelled outright in favor of
tar.exe integration (T-F47–T-F49) — `tar.exe` is a Microsoft-signed OS component nobody downloads
or hash-verifies, so T-F13's stated threat model doesn't fit it. T-F13's `Depends on: T-F07 or
T-F08` was never updated after that cancellation, leaving a task pointing at a dead dependency.

Comparing the two tasks line by line: T-F13's Layer 1 (restricted token), Layer 3 (filesystem
restriction via IL/DACL), and Layer 6 (staging validation, TOCTOU mitigation) are exactly what
T-F52's Flow already implements, specialized for tar.exe and already scoped into v1.4 per
`SPEC.md`'s roadmap table. T-F13's Layer 2 (Job Object: `ActiveProcessLimit`, RAM/CPU limits, UI
restrictions) and Layers 4/5 (network isolation, WFP firewall rule) are **not** covered by T-F52
as originally written — genuine additional hardening, not duplicate scope.

**Resolution:** T-F13 marked `SUPERSEDED by T-F52` rather than deleted (per this project's "never
silently deprecate" rule). T-F52's P/Invoke surface, Flow, and acceptance criteria were extended
to absorb Job Object limits and network isolation/firewall rule, making it the single task for
all tar.exe process-hardening work. No implementation exists yet for either task — this is a
task-planning correction, not a code change; `TASKS.md`'s T-F13 and T-F52 entries carry the full
updated detail.

**Files:** `TASKS.md` (T-F13, T-F52 entries only — no source changes).

---

## Correction — T-F91 Localization Parity Gap (22 of 24 Locales Fell Behind)

**Symptom:** user asked whether all T-F91 locales were "equally supported." Diffing
`<data name=` key counts across every `Strings/<locale>/Resources.resw` (rather than trusting
`TASKS.md`'s prose, which only tracked the original 2026-07-07 batch) showed `en-US` at 70 keys,
`uk-UA` at 68, and all other 22 European locales at 31.

**Root cause:** every feature that shipped after T-F91's original batch and added new `x:Uid`
strings — T-F05's second follow-up (browse-mode column headers, the up-arrow buttons, tray menu,
Mode/compression/conflict radio+combo items) and T-F06 (the entire Ask-on-Conflict dialog) — only
ever added the new keys to `en-US` and `uk-UA`. uk-UA got them because that follow-up round was
done for a Ukrainian-speaking user testing on-device; the other 22 locales were simply never
revisited. WinUI's `ResourceManager` falls back to `en-US` for any key missing from a specific
locale, so this was never a crash or a build error — it silently degraded to a part-native,
part-English UI for every non-English, non-Ukrainian locale, which is why it went unnoticed
through multiple `dotnet build`/`dotnet test` passes.

**Resolution:** translated the 37 missing keys (39 total minus the 2 intentionally-omitted URL
keys) into all 22 affected locales, matching each locale's already-established terminology (e.g.
reusing German's existing "Archivieren"/"Extrahieren"/"Ziel" rather than picking new synonyms).
All 25 `Resources.resw` files now carry 68 real keys each (`en-US` stays at 70 for the URL-key
exception the original T-F91 entry already documents). Verified every file still parses as valid
XML (`py -c "import xml.etree.ElementTree as ET; ET.parse(...)"` — `python3`'s WindowsApps alias
is unreliable per this project's own tooling notes) and that `dotnet build
src/Archiver.App/Archiver.App.csproj /p:Platform=x64` succeeds with 0 errors against the expanded
resources.

**Not addressed by this fix — stays open on T-F91:** native-speaker correctness review of any of
the 24 European locales (all translations, old and new, are AI-generated), on-device verification
that OS-language auto-match actually selects each locale, and the long-text/RTL layout-corruption
check. This fix closes the *parity* gap (same keys present everywhere), not T-F91's remaining
quality-review criteria.

**Files:** `src/Archiver.App/Strings/{bg-BG,cs-CZ,da-DK,de-DE,el-GR,es-ES,et-EE,fi-FI,fr-FR,hr-HR,
hu-HU,it-IT,lt-LT,lv-LV,nb-NO,nl-NL,pl-PL,pt-PT,ro-RO,sk-SK,sl-SI,sr-Latn-RS,sv-SE}/Resources.resw`
(22 files updated, no new files).

---

## T-F52 — AppContainer Chosen Over Low-IL Token; Two-Vector Threat-Model Reframing

**Context:** before writing any P/Invoke for T-F52 (per this project's 3-attempt-rule caution
around Windows security programming), the user asked to think through the task from first
principles: given `tar.exe` is a Microsoft-signed, built-in OS binary, what does sandboxing it
actually buy — and is a signature check alone enough? Advisor consulted before committing to a
design, per this project's own pre-implementation-research norm extended to security design.

**The reframing — two distinct threat vectors, only one of which T-F52 defends:**

1. **The binary itself is swapped/tampered with.** Writing over
   `C:\Windows\System32\tar.exe` requires defeating WRP/TrustedInstaller ACLs — SYSTEM-level
   access. At that point the whole host is already owned, and no sandbox around *Pakko's own
   invocation* changes that outcome. The load-bearing mitigation for the *realistic* version of
   this vector (PATH hijacking from a lower privilege level) is already in place: the existing
   hard constraint that `tar.exe` is always invoked by absolute path, never via PATH search.
2. **The legitimate, unmodified `tar.exe` is driven by a hostile archive into misbehaving.**
   libarchive is a native parser with a real CVE history, processing attacker-controlled bytes.
   This is the standard "sandbox the untrusted-input parser" pattern (the same reason browsers
   sandbox image/PDF decoders) — and it's what actually justifies T-F52. A signature check does
   nothing for this vector; the binary is genuinely, correctly signed and still exploitable via a
   crafted archive.

**Decision: reframe T-F52 around vector 2, not "what if tar.exe is compromised."** The previous
task description conflated the two. `SECURITY.md`'s tar.exe Trust Model section and `TASKS.md`'s
T-F52 entry now state this explicitly so a future reader doesn't re-derive (or worse,
mis-derive) the rationale.

**Decision: add an Authenticode signature check anyway, but explicitly as a cheap non-primary
defense.** Verifying `tar.exe`'s signature (Microsoft subject) before each launch is nearly free
and catches a specific tampering scenario (malware swaps the binary with a lower-privilege
write primitive that doesn't also let it forge a Microsoft signature). Documented plainly that
its value against a real, determined attacker is marginal — TOCTOU between the check and the
launch, and anyone who can swap the binary outright can typically do worse — so it must never be
treated as a substitute for the sandbox, and its presence shouldn't be used to justify shrinking
the sandbox later.

**Decision: AppContainer instead of a Low-IL restricted token (user's choice, after weighing
both).**

| | Low-IL token (original draft) | AppContainer (chosen) |
|---|---|---|
| Implementation cost | Lower — `CreateRestrictedToken`/`SetTokenInformation`, IL label on quarantine dir | Higher — AppContainer profile + SID, `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`, SID ACL on quarantine dir |
| Network isolation | Not natural to the mechanism — pushed the original draft toward a **global WFP firewall rule** blocking `C:\Windows\System32\tar.exe` outbound for *every application on the system*, requiring install-time elevation and guaranteed-clean uninstall | Free and clean — an AppContainer created with an empty capability list (no `internetClient`) simply cannot open a socket, enforced by the kernel, no firewall rule, no elevation, no system-wide side effect |
| MSIX package identity interaction | Neutral | Needs care (Pakko already runs as a packaged MSIX app) — not a blocker, just a design detail to get right during implementation |

The deciding factor was network isolation: given this project's gov/defense target audience,
blocking outbound access from a compromised parser process is a real requirement, and the
Low-IL path's only route there (a global, system-wide firewall rule) was independently flagged
as the worst cost/risk item in the original draft — elevation-gated, affects other applications'
use of the same system binary, and needs guaranteed-clean removal on uninstall. AppContainer
gets the same protection with none of that blast radius.

**Decision: implement Low-IL... no — AppContainer confinement, Job Object limits, and network
isolation as one task, not split into a follow-up.** With the WFP-rule approach dropped, network
isolation is no longer the risky, separately-scoped piece it was in the original T-F13-derived
draft — it falls out of the AppContainer capability list with no extra mechanism. Splitting it
into a second task would just add coordination overhead for no risk reduction.

**Rejected: shipping the global WFP firewall rule from the original draft.** Blocks
`C:\Windows\System32\tar.exe` outbound for every application on the machine, not just Pakko's
invocation; requires elevation at install; needs a guaranteed-clean removal on uninstall. Dropped
outright in favor of AppContainer's per-process, kernel-enforced isolation.

**Must verify empirically before relying on `ActiveProcessLimit = 1`:** confirm Windows' built-in
bsdtar keeps `.tar.xz`/`.tar.zst` compression filters statically linked and in-process (expected,
since Windows' tar.exe ships them compiled in) — if either format ever shells out to an external
filter helper, the Job Object's `ActiveProcessLimit = 1` will break that extraction. Test against
real fixtures before shipping, don't assume.

**Files (not yet changed — design-only session, no P/Invoke written):** `TASKS.md`'s T-F52 entry
rewritten to the AppContainer design; `SECURITY.md`'s tar.exe Trust Model section needs the same
two-vector reframing and isolation-method update when T-F52 implementation starts.

---

## T-F52 — Follow-up: Profile Lifecycle and Quarantine In/Out Split (User Q&A Before Implementation)

**Context:** before starting implementation, the user asked three concrete questions: is this
transparent to the user, what's the performance cost, and how do files actually cross the
sandbox boundary. Answering the third question surfaced a real gap in the design session above —
the original flow said "create (or reuse) an AppContainer profile" per operation and "delete the
AppContainer profile" as a cleanup step, which is wrong on both counts.

**Correction — the AppContainer profile is created once, never per-operation.**
`CreateAppContainerProfile` is a registry-backed, per-user-account operation with no elevation
requirement (unlike the previously-rejected WFP firewall rule) — but creating and deleting it on
every single Extract/Archive call adds avoidable overhead and, worse, a real race under T-F12's
`SeparateArchives` parallel mode (concurrent create/delete of the same named profile from
multiple threads). The profile is created lazily on first use (tolerating
`ERROR_ALREADY_EXISTS`) and kept registered for the life of the install; its SID is a fixed,
safely-shared identity across concurrent tar.exe invocations. Only the **filesystem grants** are
per-operation, not the profile itself.

**Decision — quarantine directory splits into `in\` (read-only ACE) and `out\` (write-only ACE),
not one shared folder.** An AppContainer process has zero filesystem access outside paths
explicitly ACL'd to its SID — including the source archive itself, which lives at an arbitrary
user-chosen path (Downloads, Desktop, a mapped drive) that Pakko does not control and should not
grant a sandboxed process direct access to. Rather than ACL the user's original file path, the
archive is staged into `quarantine\in\` first — via hardlink when same-volume (no I/O cost),
falling back to a real copy cross-volume — and only that Pakko-owned path gets an ACE. This keeps
every AppContainer filesystem grant scoped to directories Pakko itself created, never to a path
the user chose.

**Decision — the T-F49 whole-archive pre-scan (`-tf`/`-tvf`) runs inside the AppContainer too,
not just the extraction (`-xf`).** Both invoke the same libarchive parser against the same
untrusted bytes; the pre-scan producing no filesystem output doesn't reduce its exposure to a
parsing vulnerability. Sandboxing only the extraction step and leaving the listing step
unsandboxed would reopen exactly the vector T-F52 exists to close.

**Files:** none yet — still a design-only session; `TASKS.md`'s T-F52 entry updated with the
corrected Flow/acceptance-criteria to match before any code is written.

---

## T-F52 — Phase 0: Empirical Spike Findings (Job Object, AppContainer, ACE Masks)

**Method:** built a throwaway .NET 9 console spike (`SandboxSpike`, `dotnet new console` in the
session scratchpad, not committed — same "throwaway script, not committed" precedent T-F49 used
for its own tar.exe symlink-escape research) that P/Invokes the real Win32 APIs T-F52's design
depends on, and ran it against real `C:\Windows\System32\tar.exe` (bsdtar 3.8.4 / libarchive 3.8.4
/ liblzma 5.8.1 / libzstd 1.5.7, confirmed via `tar --version` on this machine) and real fixture
archives. All three of the design's empirically-unverified assumptions were tested and confirmed;
none forced a design change.

### 0a — Job Object `ActiveProcessLimit = 1` vs `.tar.xz`/`.tar.zst` extraction

Built real `.tar.xz` and `.tar.zst` fixtures via `tar -a -cf <name> -C <src> hello.txt` (same
auto-compress-by-suffix invocation `ExternalTarFixtureBuilder` already uses), launched
`tar -xf <archive> -C <dest>` via `Process.Start` and immediately called
`AssignProcessToJobObject` against a Job Object configured with
`JOB_OBJECT_LIMIT_ACTIVE_PROCESS` / `ActiveProcessLimit = 1`. **Both formats extracted
successfully — exit code 0, correct file contents recovered, no Job Object violation.** This
confirms Windows' built-in bsdtar keeps its xz/zstd compression filters statically linked and
in-process for extraction, exactly as hypothesized (liblzma/libzstd are linked directly into the
binary per its own `--version` output, not shelled out to separate filter executables). **Decision:
`ActiveProcessLimit = 1` is safe to ship as designed** — no exception needed for either format.

### 0b — Regular AppContainer readability

Created a real AppContainer profile (`CreateAppContainerProfile`, tolerating
`ERROR_ALREADY_EXISTS` on rerun) with an **empty capability list**, then launched
`tar.exe --version` inside it via raw `CreateProcessW` + `STARTUPINFOEX` +
`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` (`SECURITY_CAPABILITIES{AppContainerSid, Capabilities
= null, CapabilityCount = 0}`). **Succeeded on the first attempt** — exit code 0, full, correct
`--version` output (`bsdtar 3.8.4 - libarchive 3.8.4 zlib/... liblzma/... bz2lib/... libzstd/...
cng/... libb2/...`) — i.e. tar.exe successfully loaded every one of its DLL dependencies
(`zlib1.dll`-equivalent statically-linked code, `bcrypt.dll`/CNG, etc.) from `System32` while
running inside the AppContainer identity, with zero granted capabilities. **Decision: a regular
(non-LPAC) AppContainer is sufficient** — `ALL APPLICATION PACKAGES`/`ALL RESTRICTED APPLICATION
PACKAGES` already has the default read+execute Windows grants on `System32` that a regular
AppContainer needs; LPAC is not required and adds complexity (a separate, more restrictive
identity with its own compatibility surface) for no observed benefit here. Do not revisit LPAC
without a concrete failure this spike didn't hit.

### 0c — ACE mask probe for quarantine `in\`/`out\`

Built a real `in\`/`out\` quarantine pair, staged a fixture archive into `in\`, and granted the
AppContainer SID access via `icacls.exe` (used in this throwaway spike in place of hand-marshaling
`SetEntriesInAclW` — faster to iterate; production code still uses `SetEntriesInAclW`/
`SetNamedSecurityInfoW` directly as designed, this only changes how the spike itself granted
access for testing purposes) plus a **traverse-only grant on the quarantine root** (an AppContainer
identity does not bypass traverse checking on ancestor directories by default, confirmed necessary
— omitting it produces `tar.exe: could not chdir to ...`, see the negative-control result below).

Tested **least-privilege masks first, not Full Control**: `in\` = `(OI)(CI)(RX)` (Read &
Execute, inherited), `out\` = `(OI)(CI)(M)` (Modify, inherited), quarantine root = `(X)`
(traverse-only, non-inherited). **Succeeded on the first attempt** — real `-xf` run against the
staged archive extracted correctly into `out\` with zero `ERROR_ACCESS_DENIED`. Full Control on
both folders was also confirmed to work (tested in an earlier iteration of this same spike) but
**the least-privilege combination is the one to ship** — no reason to grant more than Modify/
Read&Execute once the narrower masks are confirmed sufficient.

**Negative control (the actual security proof, not just "does it work"):** the same sandboxed
tar.exe process, given the identical staged archive, was launched again targeting a destination
directory that was **never** granted any ACE for the AppContainer SID
(`0c_outside_never_acld\`, a sibling of the quarantine dir Pakko itself created but did not ACL).
Result: **`tar.exe: could not chdir to 'C:\...\0c_outside_never_acld'` — access denied, nothing
written.** This is the concrete, on-this-machine confirmation that AppContainer confinement (not
just "we didn't grant access, so we assume it's blocked") actually stops a sandboxed tar.exe from
touching a path outside the folders Pakko explicitly ACL'd — the core security property the whole
design exists to provide.

**Open item deferred to actual implementation (Phase 6 of the 13-phase plan below), not blocking
Phase 0:** the icacls letter codes above (`RX`/`M`/`X`) are confirmed-working via `icacls.exe`,
but production `TarSandboxedService` code will call `SetEntriesInAclW` directly with raw
`ACCESS_MASK` values, not shell out to `icacls.exe`. Translating `RX`/`M`/`X` to their exact
`ACCESS_MASK` hex values (rather than trusting a memorized constant) is deferred to that
implementation phase — verify via `icacls /save`+parse or by computing the mask programmatically,
per this project's own norm of not committing to an unverified Windows API detail from memory.

---

## T-F52 — Phase 1 (steps 1–5) Implementation Progress + Packaged-Identity Confirmation

**Method:** began the 13-phase implementation per TASKS.md's ordered build/verify plan, compiling
and testing after each step rather than writing all 9 files up front — advisor-recommended given
this task's unfamiliar security P/Invoke surface and this project's 3-attempt rule. New
`src/Archiver.Core/Services/Sandbox/` classes written so far: `SandboxHandles.cs` (4 SafeHandle
types — SID/Job Object/process-or-thread/attribute-list-buffer), `QuarantineStaging.cs`
(`IsSameVolume`, hardlink-or-copy staging), `AppContainerProfile.cs` (`EnsureExists`/`GetSid`/
`Delete`, the last test-only), `SandboxedProcessLauncher.cs` (raw `CreateProcessW` +
`STARTUPINFOEX` + non-inheritable pipes + `CREATE_SUSPENDED`→assign→resume, with a Win32
command-line-quoting helper), `SecurityCapabilitiesAttributeList.cs`
(`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` attribute-list builder), `SandboxJobObject.cs`
(`ActiveProcessLimit=1`, 512 MB RAM limit, CPU time limit, `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`,
full UI-restriction bitmask). Real unit/integration tests added alongside each (not deferred to
step 9) — `tests/Archiver.Core.Tests/Services/Sandbox/` and one new
`tests/Archiver.Core.IntegrationTests/SandboxJobObjectTarExtractionTests.cs` — all passing against
real Win32 calls, no mocks (per this repo's no-mocking-library convention).

**Critical environment-gap check, done before continuing to ACL/signature/service work (advisor
flagged this as the single most important checkpoint — Phase 0's spike and `dotnet test` both run
from a plain, unpackaged full-trust console host, which cannot prove AppContainer creation and a
`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` child launch also work from **package identity**,
the actual context `Archiver.Shell`/`Archiver.App` run in as an MSIX `FullTrustApplication`):
added a temporary `--sandbox-probe` command to `Archiver.Shell/Program.cs` (plus a temporary
`InternalsVisibleTo` grant for `Archiver.Shell` in `Archiver.Core.csproj`) running the exact
production pipeline — `AppContainerProfile.EnsureExists` → `GetSid` →
`SecurityCapabilitiesAttributeList.Create` → `SandboxedProcessLauncher.RunAsync("tar.exe",
["--version"], ...)` — logging its result to a temp file. Ran a full `Deploy.ps1` build+sign+
install, then launched the **installed** `Archiver.Shell.exe` from
`C:\Program Files\WindowsApps\PavloRybchenko.Pakko_1.2.0.26_x64__9hkd8feqeqbr4\` directly via
`Start-Process` (same technique this project already uses to verify shell-triggered EXEs —
see CLAUDE.md's Windows Packaging Best Practices). **Result: PASS** — exit code 0, full correct
`bsdtar 3.8.4 - libarchive 3.8.4 zlib/... liblzma/... bz2lib/... libzstd/... cng/... libb2/...`
output, from the real packaged process identity, not a simulated/unpackaged one. Both the temporary
probe command and the temporary `InternalsVisibleTo` grant were reverted immediately after
(`git checkout`/manual edit) — not part of the shipped design, same "throwaway, not committed"
treatment as the Phase 0 spike itself.

**Decision:** AppContainer + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` is confirmed safe to
build out fully under package identity — no fallback design or LPAC escape hatch is needed for
this reason. Proceeding to step 6 (`QuarantineAcl`/`TarSandboxScope`) as planned.

**Step 6 — `QuarantineAcl` resolves Phase 0's deferred icacls→`ACCESS_MASK` translation:**
implemented via the canonical `GetNamedSecurityInfoW` → `SetEntriesInAclW` (with the existing DACL
passed as `OldAcl`, so the grant is added, never a full-DACL replace that would strip the folder's
owner/SYSTEM/Administrators entries) → `SetNamedSecurityInfoW` pattern, using the standard
documented NTFS simple-permission masks: Read & Execute = `0x1200A9`, Modify = `0x1301BF`,
Traverse Folder (quarantine-root-only, non-inherited) = `0x0020`. Verified with two real
integration tests against a live AppContainer profile + real tar.exe (`QuarantineAclTests.cs`):
(1) granting Read&amp;Execute on `in\`, Modify on `out\`, and traverse-only on the quarantine root
lets a real `-xf` extraction succeed with zero `ERROR_ACCESS_DENIED`; (2) the same sandboxed
launch targeting a sibling destination folder that Pakko itself created but never granted an ACE
to fails with `tar.exe: could not chdir to ...` and writes nothing — reproducing Phase 0's own
negative-control result, now through production code instead of `icacls.exe`.

**Step 6 — real bug found while building `TarSandboxScope`, not in Phase 0's own spike:**
NTFS hard links share their security descriptor with the ORIGINAL file object, not the containing
directory — a hard link is just an additional directory entry pointing at the same file object,
and DACL inheritance only applies at file-creation time, never retroactively to an existing
object a new directory entry now also points at. This meant `QuarantineStaging.StageArchive`'s
hardlink-when-same-volume path produced a staged archive the AppContainer SID could not read,
even though the containing `in\` folder was correctly ACL'd with Read&amp;Execute for that SID —
`tar.exe: Error opening archive: Failed to open '...\in\fixture.tar'` despite `icacls` showing
`in\` itself granting the right access. Diagnosed by isolating variables one at a time (profile
name, directory nesting depth, folder-name reuse across runs — all ruled out as the cause) until
swapping `File.Copy` for `QuarantineStaging.StageArchive` in an otherwise-identical reproduction
reproduced the failure on demand, and `icacls` on the staged file itself showed only
`SYSTEM`/`Administrators`/the real user account — the original archive's inherited permissions,
not `in\`'s. **Fix:** `TarSandboxScope.CreateAsync` now calls `QuarantineAcl.GrantReadExecute`
directly on the staged file path itself immediately after staging, regardless of whether staging
hardlinked or copied (a copy would already inherit this from `in\` at creation time, but granting
explicitly is harmless and keeps both paths correct without a branch). Covered by a permanent
regression test, `TarSandboxScopeTests.CreateAsync_StagedArchiveIsHardlinkedSameVolume_
StillReadableInsideSandbox`.

**Step 6 — separate, related design correction found the same session (before the bug above):**
TASKS.md's original Flow said the quarantine directory should live "on the same disk as
the destination" (a sibling of the user's chosen destination folder). Empirically, an AppContainer
token has no bypass-traverse-checking privilege, so `FILE_TRAVERSE` is enforced on every ancestor
directory down to `in\`/`out\` — and the user's arbitrary destination folder (Desktop, Documents,
a network share, anywhere) sits under an ancestor chain Pakko does not own and should not be
granting ACEs on. `TarSandboxScope` now roots the quarantine under a fixed, Pakko-owned
`%TEMP%\PakkoTarSandbox\<guid>\` location instead, granting traverse-only on both Pakko-created
levels (the shared `PakkoTarSandbox` parent and the per-operation `<guid>` subfolder) — `%TEMP%`
itself needs no explicit grant for either of these two levels to work. This is a deliberate
deviation from TASKS.md's original text: the final atomic-seeming "move from `out\` to
destination" was already, in the real `TarProcessService` code, a **per-file `File.Move` loop**,
not a whole-directory rename — and `File.Move` already succeeds across volumes for individual
files — so rooting the quarantine under `%TEMP%` instead of next to the destination costs at most
an extra copy instead of a rename when the two are on different volumes, never a correctness
problem. Confirmed via `git log`/Explore of `TarProcessService.ExtractSingleArchiveAsync` before
committing to this — the "same disk" requirement in the original text was never actually load-
bearing for correctness, only an unrealized perf assumption.

**Step 7 — `TarSignatureVerifier`: real `CERT_FIND_SUBJECT_CERT` constant bug, and a scope
boundary (embedded vs. catalog signing):** the first working draft used
`CERT_FIND_SUBJECT_CERT = 0x00070000`, guessed from a half-remembered constant — this is
actually `CERT_COMPARE_NAME_STR_A << CERT_COMPARE_SHIFT` (a find-by-name-string mode), not
`CERT_COMPARE_SUBJECT_CERT(11) << CERT_COMPARE_SHIFT(16) = 0x000B0000`. Passing the wrong
constant made `CertFindCertificateInStore` misinterpret the `CERT_INFO` buffer from
`CryptMsgGetParam(CMSG_SIGNER_CERT_INFO_PARAM)` as a plain string pointer, silently returning
`CRYPT_E_NOT_FOUND` — confirmed via a temporary diagnostic build against the real
`C:\Windows\System32\tar.exe` (`WinVerifyTrust` returned `0x00000000` — the integrity check
itself was correct — but `CertFindCertificateInStore` failed with
`0x80092004`/`-2146885628`). Fixed to the correct `0x000B0000`; re-running against the real
tar.exe then correctly read the signer's Organization attribute as `Microsoft Corporation` (full
subject `CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US`, matching
`Get-AuthenticodeSignature`'s own output for this file).

Separately, an over-generalized test (`notepad.exe` should also pass, "confirming the check
generalizes beyond tar.exe") failed even after the fix — not a further bug, but a real scope
boundary: `notepad.exe` is genuinely Microsoft-signed but via a **Windows catalog (.cat) file**,
not an embedded PKCS#7-in-PE signature (confirmed via
`(Get-AuthenticodeSignature notepad.exe).SignatureType` = `"Catalog"` on this machine).
`TarSignatureVerifier`'s `CryptQueryObject` call only requests
`CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED`, so it correctly does not recognize catalog
signatures — and doesn't need to, since tar.exe (the only file this class ever checks) carries a
real embedded signature. The test was corrected to assert `notepad.exe` returns `false`, with a
comment explaining this is a deliberate scope boundary, not something to "fix" later.

**Step 8 — `TarSandboxedService`: real bug — implicit parent-directory creation fails under the
AppContainer, explicit directory entries don't.** Before deleting `TarProcessService.cs`, the
port was smoke-tested by temporarily pointing all 4 existing test files' `_sut` field at
`TarSandboxedService` and running the full existing suite (26 tests: the T-F49 reject cases,
compressed-format round trips, 7z/RAR reads, MOTW propagation, selective extraction,
`DetectCapabilitiesAsync`) — advisor-recommended, since a renamed-test-only verification would
only prove "extraction/listing work," not that nothing regressed. 23/26 passed immediately; 3
failed, all sharing one shape: a tar entry like `"sub/b.txt"` with **no preceding explicit `"sub/"`
directory entry** failed to extract (`tar.exe: sub/b.txt: Can't create '...\out\sub\b.txt': No
such file or directory`), even though `icacls` confirmed `out\` itself correctly carried
`(OI)(CI)(M)` for the AppContainer SID. Isolated via a throwaway diagnostic harness (leaked,
uncleaned scopes, one variable changed at a time): (1) an **explicit** `"sub/"` directory entry
alone extracts fine under the sandbox; (2) the identical `"sub/b.txt"`-only archive extracts fine
through the same raw launcher with **no** AppContainer capabilities at all (ruling out a
launcher/pipe/Job-Object bug); (3) an archive with **both** an explicit `"sub/"` entry and
`"sub/b.txt"` extracts fine under the sandbox. Conclusion: libarchive's own **implicit**
parent-directory auto-creation (when a nested file entry has no corresponding directory entry)
behaves differently under an AppContainer token than its normal explicit-directory-entry path —
plausibly a permissions/ownership fixup step libarchive performs only for auto-created
directories, though the exact internal libarchive code path wasn't traced further (not necessary
once a robust fix was found). **Fix:** `TarSandboxedService.ExtractSingleArchiveAsync` now
pre-creates every directory the archive implies — via `Directory.CreateDirectory`, at Pakko's own
trusted process identity, which correctly inherits `out\`'s ACEs — immediately after the pre-scan
and before `-xf` ever runs, so tar.exe itself never needs to create a directory under
AppContainer. All 26/26 tests passed after this fix, including both selective-extraction
variants (files-only and folder-with-descendants) the advisor specifically flagged as unverified
by the plan up to that point.

**Step 9 (`TarSandboxedServiceSandboxBehaviorTests`) — a test-design bug, not a product bug, in
the network-isolation proof.** The first draft's socket-connect test called
`TcpListener.AcceptTcpClientAsync()` once but never actually answered the accepted connection with
an HTTP response. Windows completes a real TCP handshake (and queues it in the backlog) as soon as
`Start()` has run, independent of whether .NET's own accept call has been reached yet — so the
*unsandboxed* curl call genuinely connected (confirmed via `curl -v`: "Established connection...")
but then timed out waiting for bytes that were never sent, producing the same exit code 28
(`CURLE_OPERATION_TIMEDOUT`) as the sandboxed call's real connection failure — a false negative
that had nothing to do with AppContainer. Diagnosed by temporarily capturing `curl -v` output for
both calls side by side. **Fix:** a background loop now accepts every real connection for the
test's duration and answers with a minimal 200 OK, so a genuinely reachable curl call always
succeeds — leaving "never even established a connection" as the only way the sandboxed case can
still fail, with an accepted-connection counter (expected exactly 1, from the unsandboxed call
only) as the assertion, instead of reasoning about ambiguous curl exit codes.

**Step 11 — one more flake in the same socket test, only visible under the full parallel suite:**
disposing each accepted `TcpClient` immediately after `WriteAsync` (inside the accept loop) raced
the OS's actual delivery of that data under load — running the full `dotnet test` suite (many
tests, including several spawning real tar.exe processes, running in parallel) made the dispose
land before the response bytes were flushed, some of the time, sending an abrupt RST instead of a
graceful close; curl reported `CURLE_RECV_ERROR` (56). Invisible when this one test file ran in
isolation (confirmed: passed 5/5 in isolation before the full-suite run exposed it) — a reminder
that "passes alone" isn't sufficient for a timing-sensitive test. **Fix:** accepted clients are now
collected and disposed only once, after both curl calls have fully completed, with an explicit
`FlushAsync` after the write. Two full `dotnet test --filter "Category!=Slow"` runs (282/282 both
times) confirmed the fix.
