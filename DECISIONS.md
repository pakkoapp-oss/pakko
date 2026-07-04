# DECISIONS.md — Architectural Decisions

Key decisions and rejected alternatives. Read before implementing anything in
packaging, COM, or shell integration.

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

**Decision:** Newline-delimited UTF-8 JSON over `NamedPipeServerStream`.
- `Archiver.Shell` = server (creates pipe, runs operation)
- `Archiver.ProgressWindow` = client (connects, renders progress)
- Pipe name passed to `Archiver.ProgressWindow` via `--pipe <name>` command-line argument

**Message types:** `progress` (percent, speed, ETA), `complete`, `error`, `cancel` (client → server).
