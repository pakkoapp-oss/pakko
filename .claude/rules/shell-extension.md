---
paths:
  - "src/Archiver.ShellExtension/**"
  - "tests/Archiver.ShellExtension.Tests/**"
---

# Archiver.ShellExtension (C++ COM) rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Context-menu ordering:** primary action commands (Extract/Archive) always precede
  diagnostic/verification ones (Test archive) in `PakkoRootCommand::EnumSubCommands` —
  deliberate deviation from NanaZip's Test-first order. See `DECISIONS.md`'s
  "Test Archive (T-F62)" entry before copying NanaZip's menu order for a new command.
- **COM HRESULTs:** never return `S_FALSE` alongside a null/unset out-parameter — `S_FALSE` is a
  *success* code (`SUCCEEDED()` is true), so callers checking only `SUCCEEDED()` will dereference
  the null. Use `E_NOTIMPL` instead (verified against Microsoft's own `IExplorerCommand` sample).
- **Shell-extension icons referencing another exe** (e.g. `PakkoRootCommand::GetIcon` →
  `Archiver.App.exe,0`): the target exe needs `<ApplicationIcon>` set in its `.csproj` — a
  `Content Include` of an `.ico` (used for the MSIX tile logo) does NOT embed a Win32 icon
  resource in the exe. Verify with `ExtractIconEx(path, -1, $null, $null, 0)`'s total count, not
  `[System.Drawing.Icon]::ExtractAssociatedIcon()` — the latter can return a non-null fallback
  icon even for a file with zero real icon resources (T-F95).
- **Context-menu flicker on first open of a new Explorer window** (e.g. showing a stale/other
  entry before repainting to Pakko's) is a known Explorer verb/icon-cache artifact, not a
  Pakko code bug — Explorer caches top-level shell-extension verbs across COM DLL
  (re)registrations until it requeries `GetTitle`/`GetIcon`. Don't chase this with code changes
  without first confirming the cache-artifact explanation is wrong.
- **MSIX Packaged COM registration lives entirely under
  `HKLM\SOFTWARE\Classes\PackagedCom\Package\<PackageFullName>\...` and
  `PackagedCom\ClassIndex\<CLSID>`** — namespaced by the full versioned package identity, with
  zero classic `HKCR\CLSID\{...}` entry ever written. Confirmed empirically (T-F55/T-F40): a full
  `HKEY_CLASSES_ROOT` search for the verb ID string returned 0 matches even while installed, and
  `Remove-AppxPackage`/`Add-AppxPackage` cleanly removes/restores both `PackagedCom` subtrees.
  There is no orphan-registry-key risk to chase for this app's shell extension — it never used
  classic `regsvr32`-style registration to begin with.
- **A new leaf `IExplorerCommand` class needs zero `Package.appxmanifest` entry.** Only
  `PakkoRootCommand`'s own CLSID is ever registered there (`com:Class`/`desktopN:Verb`); every
  leaf command (`ArchiveCommand`, `TarArchiveCommand`, etc.) is instantiated internally via
  `Make<T>()` inside `PakkoRootCommand::EnumSubCommands` — confirmed T-F105 by grepping the
  manifest for every existing leaf CLSID and finding none.
- **A COM surrogate (`dllhost.exe`) hosting `Archiver.ShellExtension.dll` can lock the DLL/PDB**
  after testing the context menu, causing `C1041`/file-in-use errors on the next rebuild. Run
  `taskkill /F /IM dllhost.exe` (or find the specific PID) before rebuilding if this happens.
  The same surrogate can also lock unrelated scratch files/folders touched during that
  right-click (e.g. a smoke-test directory) — same fix if cleanup fails with "in use".
```
# Archiver.ShellExtension (C++ COM DLL) — not built or tested by dotnet build/test
# Build via Visual Studio / MSBuild (x64 or ARM64 platform).
# Archiver.ShellExtension.Tests.vcxproj only compiles the two COM-free files (ShellExtUtils.cpp,
# Localization.cpp) directly — it does NOT compile ExplorerCommands.cpp/dllmain.cpp. To validate
# a change to an IExplorerCommand class actually compiles, build the real DLL project too:
# MSBuild src\Archiver.ShellExtension\Archiver.ShellExtension.vcxproj /p:Configuration=Debug /p:Platform=x64
# Any dotnet build/publish/test command with /p:Key=Value flags must run via the PowerShell
# tool, not Bash — Bash (Git Bash/MSYS) mangles "/p:" into a path-like token, failing with
# "MSB1008: Only one project can be specified."
# First-time test project setup:
nuget restore tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj -SolutionDirectory .
# Build directly (NOT via .sln — .sln + /t:<ProjectName> applies that target to every project).
# $(SolutionDir) is only auto-set when building through the .sln, so pass it explicitly:
MSBuild tests\Archiver.ShellExtension.Tests\Archiver.ShellExtension.Tests.vcxproj /p:SolutionDir=<repo-root>\ /p:Configuration=Debug /p:Platform=x64
# If MSBuild.exe isn't on PATH, locate it with vswhere — use the PowerShell tool for this,
# not Bash: Bash strips backslashes from patterns like "MSBuild\**\Bin\MSBuild.exe".
# Then run: tests\Archiver.ShellExtension.Tests\bin\x64\Debug\Archiver.ShellExtension.Tests.exe
```
