---
paths:
  - "scripts/**"
---

# Script and build-tooling rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Testing `scripts/*.ps1` fixes:** these scripts require Windows PowerShell 5.1
  (`#Requires -Version 5.1`). The PowerShell tool runs pwsh 7+, which defaults to UTF-8 and
  will NOT reproduce non-BOM-file ANSI-codepage bugs (see T-F84). To actually verify a fix,
  invoke `powershell.exe` explicitly rather than relying on the tool's default interpreter.
- **Running `Deploy.ps1`/any `.ps1` via the Bash tool's `powershell.exe` fails outright** —
  `cannot be loaded because running scripts is disabled on this system` (default Restricted
  execution policy for that invocation path). Use the PowerShell tool instead (its pwsh 7 session
  already runs unrestricted) — don't try `-ExecutionPolicy Bypass` workarounds from Bash.
- **Writing a new throwaway script with non-ASCII content (translations, Cyrillic, etc.):**
  the opposite applies — run it via the PowerShell tool's default pwsh 7, NOT `powershell.exe`.
  `powershell.exe` (5.1) decodes a UTF-8-no-BOM `.ps1` via the system ANSI codepage, corrupting
  every non-ASCII character before the script even runs (confirmed T-F105, a 37-locale insert
  script). Only reach for explicit `powershell.exe` when deliberately reproducing a codepage bug.
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
- **`DeployMsix`'s post-build `Add-AppxPackage` also actively fails a Release `dotnet
  publish`/`build` outright (not just silently) on any machine without the signing cert in
  `LocalMachine\TrustedPeople`** — e.g. a fresh CI runner (`0x800B0109`, "root certificate ...
  must be trusted"). Set `$env:PAKKO_DEPLOYING = '1'` before the call and clear it after (see
  `Deploy.ps1`'s own use of this exact guard) to suppress the target entirely. Found T-F122,
  2026-07-19 — the first real CI run failed on this before it was noticed.
- **A build failing with a file-lock-shaped error** (`MSB3231`/`Access to the path ... is
  denied` on something under `bin`/`obj`/`AppPackages`) — first try `dotnet build-server
  shutdown` (kills lingering MSBuild/VBCSCompiler nodes that can hold output handles open)
  before assuming a stuck folder needs an `obj` clean (see the `AppPackages` wedge note above).
- **`git stash push -u` can silently half-fail**: if cleaning untracked content hits
  `Permission Denied` on an unrelated empty directory (e.g. leftover build-artifact folders),
  the stash entry is still created correctly, but the working tree may NOT actually revert —
  `git status` can still show the same modified files. Always verify with `git status` after
  any `stash push`; if changes persist, finish the revert manually with `git checkout --
  <files>` (the stash already has a safe backup, so this is not destructive).
- **Windows App Certification Kit (`appcert.exe`) requires elevation** — a bare invocation fails
  with "requires elevation." Run it via `Start-Process -Verb RunAs -Wait` from the PowerShell
  tool. Its report is XML: `<REPORT OVERALL_RESULT="...">`, per-check `<TEST><RESULT>PASS/FAIL
  </RESULT><MESSAGES><MESSAGE TEXT="..."/></MESSAGES></TEST>` — parse for `FAIL`/`WARNING` rather
  than reading the whole report by eye.
- **Deploy shortcuts:**
  Release build in VS triggers `Deploy.ps1 -DeployOnly` automatically (post-build event).
  For manual deploy from terminal: `.\scripts\Deploy.ps1` (full build + sign + install)
  or `.\scripts\Deploy.ps1 -DeployOnly` (install only, no build).
- **`MSB3231` on `AppPackages`/`obj\...\PackageLayout\`:** if a manual `Remove-Item` of that path
  succeeds right after the failure, it is a transient live handle (Search Indexer is the top
  suspect) — rerun; `Deploy.ps1` tolerates it when a valid fresh `.msix` exists (T-F96). If the
  folder stays stuck even across a reboot, clean `src/Archiver.App/obj\`; every Deploy run already
  packages a new revision, so the output folder name is fresh (T-F368).
