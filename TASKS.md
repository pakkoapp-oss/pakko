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

## Current State — v1.0 Complete

- All T-01 through T-35 + T-11 complete and committed
- 45/45 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` (unsigned — see T-F10 for signing)
- Git tag: `v1.0.0`

---

## Future Tasks (post v1.0)

### T-F01 — Explorer Context Menu Integration
- [ ] **Status:** future

Right-click → "Archive with Pakko" / "Extract here" in Windows Explorer.
Requires COM registration and shell extension — significant additional complexity.

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
- [ ] **Status:** future

**What:** Optional component — not bundled by default, installable separately.

**Binary source:** NanaZip (MIT licensed fork of 7-Zip by M2Team, Japan).
Preferred over original 7-Zip due to reproducible builds and non-Russian maintainership.

**Security model:**
- SHA-256 hash of binary embedded as constant in source code
- Hash verified at runtime before every invocation
- Hash mismatch → operation refused, user notified with security error
- Binary stored in app's local data folder, not system-wide

**Process isolation:** see T-F13.

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → clear security error, no execution
- [ ] Optional install — not present in base MSIX package
- [ ] `.7z` files extracted to destination using verified binary
- [ ] Falls back to "unsupported format" if binary not installed
- [ ] `Process` always disposed after use — wrap in `using` or `finally`
- [ ] No orphaned process handles after extraction completes or fails

---

### T-F08 — Optional RAR Extraction Support
- [ ] **Status:** future

**What:** Optional component for `.rar` archives.
Official RARLAB `unrar.exe` (freeware license allows use in free software).

**Security model:** same as T-F07 — SHA-256 verification before every invocation.

**Process isolation:** see T-F13.

**Acceptance criteria (when implemented):**
- [ ] SHA-256 verification before every `Process.Start`
- [ ] Hash mismatch → security error, no execution
- [ ] Optional install only
- [ ] `.rar` files extracted using verified binary
- [ ] `Process` always disposed after use
- [ ] No orphaned process handles

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
- [ ] **Status:** future

One-line change. Windows on ARM increasingly common in government/enterprise.

```xml
<!-- Before -->
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>

<!-- After -->
<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
```

No code changes required — .NET 8 JIT handles ARM64 natively.

**Acceptance criteria (when implemented):**
- [ ] `win-arm64` added to `RuntimeIdentifiers`
- [ ] App builds for ARM64 without errors
- [ ] MSIX bundle includes both architectures
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

**Threat model:** binary passes SHA-256 but has undiscovered vulnerability, or is compromised between verification and execution, or attempts network exfiltration.

**Layer 1 — Windows Job Object (P/Invoke):**
- `ActiveProcessLimit = 1` — cannot spawn child processes
- Memory limit 512 MB — prevent resource exhaustion
- UI restrictions — no clipboard, no desktop manipulation

**Layer 2 — WFP firewall rule:**
Added at optional component install time (requires elevation once):
```powershell
New-NetFirewallRule -DisplayName "Pakko — block 7z.exe outbound" `
    -Direction Outbound -Program "$env:LOCALAPPDATA\Pakko\tools\7z.exe" -Action Block
```
Rule removed on uninstall.

**What this does NOT cover:** full filesystem isolation, AppContainer-level isolation.

**Acceptance criteria (when implemented):**
- [ ] External binary process assigned to Job Object before execution
- [ ] `ActiveProcessLimit = 1`
- [ ] Memory limit enforced
- [ ] UI restrictions applied
- [ ] Firewall rule added at install, removed at uninstall
- [ ] Job Object handle closed after process exits — no leak
- [ ] `dotnet test` passes
- [ ] Verified: spawning child process from sandboxed binary fails
