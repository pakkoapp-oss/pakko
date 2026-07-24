# SECURITY.md — Threat Model and Security Rationale

---

## Why This Document Exists

Archive tools are a common attack vector. They process untrusted input (archive files received from external sources), run with user-level permissions, and are installed on virtually every workstation. The choice of which tool to trust is a security decision, not just a UX preference.

This document explains the threat model behind Windows Archiver Wrapper and why the design choices were made.

---

## Threat Model

### Assets Being Protected

- Files on the host filesystem (documents, credentials, configs)
- Integrity of the extraction destination
- User session and process context

### Threat Actors Considered

| Actor | Capability | Vector |
|-------|-----------|--------|
| Malicious archive file | Crafted ZIP sent to user | Extraction path traversal, ZIP bomb |
| Compromised tool binary | Supply chain attack | Backdoored `.exe` distributed via official channel |
| Compromised update | Supply chain attack | Automatic update delivers malicious version |
| Vulnerable parser | CVE exploitation | Malformed archive triggers memory corruption |

### Out of Scope (v1.0)

- Attacks requiring physical access to the machine
- OS-level privilege escalation
- Attacks against the Microsoft Store distribution infrastructure

---

## Supply Chain Risk: 7-Zip, WinRAR, and Windows tar.exe

This section documents the rationale for excluding these tools as a dependency or reference implementation.

### 7-Zip

| Property | Status |
|----------|--------|
| Developer | Igor Pavlov — Russian Federation |
| Source code | Published (LGPL) |
| Reproducible builds | ❌ No — distributed binary cannot be verified against source |
| Independent security audit | ❌ None publicly documented |
| Governance | Single developer, no independent review process |
| CVE history | Multiple: CVE-2016-9296, CVE-2017-17969, CVE-2018-10115, CVE-2022-29072, and others |

The absence of reproducible builds is a critical gap: anyone with access to the build infrastructure can distribute a modified binary while the published source remains clean. This is a known and documented attack pattern (see XZ Utils backdoor, 2024).

### WinRAR

| Property | Status |
|----------|--------|
| Developer | Eugene Roshal — Russian Federation |
| Source code | ❌ Closed source |
| Reproducible builds | ❌ Not applicable — source not available |
| Independent security audit | ❌ Impossible without source |
| CVE history | CVE-2018-20250 (critical, path traversal, 500M+ installations affected), and others |

Closed-source compression tools that process untrusted files are not appropriate for environments handling sensitive data. There is no technical means to verify the absence of intentional backdoors or unintentional vulnerabilities.

### Windows tar.exe

`C:\Windows\System32\tar.exe` is an acceptable dependency for extracting non-ZIP formats. It is Microsoft-signed, open source (bsdtar/libarchive on GitHub), and delivered through the Windows Update chain. See the **tar.exe Trust Model** section below for details.

### Risk Classification for Regulated Environments

For organizations operating under security requirements (government, defense, critical infrastructure, financial sector):

- Using software from developers in adversarial jurisdictions without source auditability is a supply chain risk
- Unverifiable binaries processing sensitive files fail basic security hygiene standards
- Neither 7-Zip nor WinRAR meets reproducible build requirements

---

## This Project's Security Properties

### What We Rely On

| Component | Trust Basis |
|-----------|-------------|
| `System.IO.Compression` | Part of .NET BCL — open source at `dotnet/runtime`, CVE process via Microsoft MSRC, reproducible builds available |
| Windows App SDK / WinUI 3 | Open source at `microsoft/WindowsAppSDK`, Microsoft security response process |
| CommunityToolkit.Mvvm | Open source at `CommunityToolkit/dotnet`, UI-only, no file processing |

### Architectural Decisions with Security Impact

**No shell extensions in v1.0 — added in v1.2 with a narrowed surface**
v1.0 excluded context menu integration to minimize attack surface, since it requires elevated
trust and COM registration. v1.2 adds a shell extension (`IExplorerCommand`, T-F61), registered
as a `com:SurrogateServer` — the COM DLL runs inside an isolated `dllhost.exe` process, not
in-process inside `explorer.exe`, so a crash in the extension cannot bring down Explorer itself.
`IContextMenu` (the legacy, in-process-only shell extension API) is not used, by hard constraint.

**No network access**
The application has no network capability by design. No telemetry, no update checks, no cloud storage integration.

**No background services**
The app runs only when the user explicitly opens it. No persistent processes.

**No format parsers beyond ZIP**
Each supported format adds parser attack surface. RAR and 7z are read-only, via the Windows
built-in `tar.exe` process — not an in-process parser — since libarchive has no writer for
either. TAR-family formats (read and create) use the same `tar.exe` process.

**Minimal MSIX capabilities**
`Package.appxmanifest` declares only `runFullTrust`. No `broadFileSystemAccess`, no `internetClient`, no device capabilities.

---

## Known Limitations and Residual Risks

| Risk | Severity | Mitigation |
|------|----------|-----------|
| ZIP path traversal (e.g., `../../etc/passwd` style entries) | High | `System.IO.Compression` with .NET 8 validates entry paths — covered in `ZipArchiveService` tests |
| ZIP bomb (highly compressed entries) | Medium | Whole-archive ratio check (T-F94, v1.3; supersedes T-F28's per-entry version) — an archive whose declared uncompressed size exceeds 1000:1 against the archive file's on-disk size is blocked unless the destination has free space for the declared size AND the user explicitly confirms extraction; see `DECISIONS.md`'s T-F94 entry |
| tar-family decompression bomb (`.tar.gz`/`.bz2`/`.xz`/`.zst`/`.lzma`) | Medium | Mitigated (T-F94, v1.3; supersedes T-F90's auto-reject-only version) — same whole-archive ratio check and confirm-if-it-fits model as ZIP, run before `-xf` ever executes; see `DECISIONS.md`'s T-F94 entry |
| Symlink/reparse point attacks in ZIP entries | Medium | Mitigated (T-F37, v1.2) — reparse point check after file creation; path traversal via reparse point rejected |
| Alternate Data Stream entries (`:` in filename) | Medium | Mitigated (T-F38, v1.2) — ADS entries rejected |
| Reserved Windows filenames in entries (`CON`, `NUL`, etc.) | Low-Medium | Mitigated (T-F39, v1.2) — reserved names and control characters filtered |
| MOTW not propagated to extracted files | — | Resolved (T-F45, v1.2) — MOTW propagated to every extracted file by default |
| tar.exe runs at Medium IL | Medium | Resolved (T-F52, v1.4) — extraction now runs inside an AppContainer via P/Invoke (empty capability list, ACL'd quarantine directory, Job Object process/resource limits); archive creation stays unsandboxed by design, since it only reads trusted local files — see the "Why Archive Creation Is NOT Sandboxed" note below |
| tar.exe symlink entries escape a naive quarantine (confirmed exploit, T-F49) | High | Mitigated (T-F49, v1.3) — whole-archive pre-scan via `tar -tf`/`-tvf` rejects any archive containing a symlink/hardlink/device entry or a traversal/ADS/reserved name before `-xf` ever runs; see `DECISIONS.md`'s T-F49 entry |
| Recursive decompression bomb via nested archives (an archive inside an archive inside an archive, multiplying expansion per level) | Medium | Mitigated (T-F98, v1.4) — Archive Browser drill-down into a nested archive is capped at 4 levels deep, and every level independently re-runs the same whole-archive pre-scan (T-F49) and compression-ratio + disk-space check (T-F90/T-F94) a normal extraction would — no shortcut or inherited "already checked" state from an outer level; see `DECISIONS.md`'s T-F98 entry |
| Native decompression 0-day in the ZIP path (a memory-corruption bug in the native zlib-derived code `System.IO.Compression`'s `DeflateStream` calls across its managed→native boundary, triggered by a maliciously malformed compression stream — e.g. corrupted Huffman tables) | Low (theoretical) | **Accepted risk, not sandboxed.** Unlike tar-family extraction (AppContainer, T-F52), ZIP handling runs unsandboxed in-process by design — see "No format parsers beyond ZIP" above. Successful exploitation would execute with the app's own user-level privileges; no isolation boundary catches it. The only mitigation is indirect: Microsoft's MSRC CVE process on `dotnet/runtime`, the same trust basis this project already extends to `System.IO.Compression` generally. Sandboxing the ZIP path the same way as tar.exe is a real, undone option — not pursued, since it would add real overhead (cross-process marshaling for the common case) against a threat class with no track record against this specific code path so far. Revisit if that changes. |
| Microsoft as trust anchor | Low-Medium | Accepted tradeoff for the target audience; .NET is open source and auditable |

---

## Mark of the Web — Security Rationale

### What Zone.Identifier Is

Windows NTFS Alternate Data Stream `Zone.Identifier` records the security zone of a file's origin (e.g., `ZoneId=3` = Internet). This is the Mark of the Web (MOTW).

### Why MOTW Propagation Prevents Attacks

When a user downloads a ZIP archive from the internet, the archive receives MOTW. If extracted files **do not** inherit MOTW:

- Microsoft Office opens the extracted `.docx` or `.xlsm` in full edit mode — macros execute without Protected View
- Windows SmartScreen does not warn before running extracted `.exe` files

This is a documented exploitation technique: deliver a macro-containing document inside a ZIP, knowing the extractor will strip MOTW on extraction.

### Explorer's Gap — and 7-Zip's Default

- Windows Explorer **does not propagate** MOTW to extracted files
- 7-Zip **does not propagate** MOTW by default (added as an option in 7-Zip 23.01, off by default)
- NanaZip 6.0 (Feb 2026) propagates MOTW by default

### Pakko's Behavior (v1.2+, implemented)

Pakko propagates MOTW on all extracted files by default:

1. Read `Zone.Identifier` ADS from the source archive
2. Write identical `Zone.Identifier` ADS to each extracted file
3. Default: always on — users cannot disable (only GPO can override in v1.4)

Implementation: `FileStream` with ADS path `"extractedfile.txt:Zone.Identifier"`, no P/Invoke required.

**For system administrators:** the planned v1.4 Group Policy surface (`EnforceMOTW`,
`AllowedFormats`/`BlockedFormats`, `DisableTarExtraction` under `HKLM\Software\Policies\Pakko\`) is
documented in full, with deployment instructions, in [`POLICIES.md`](docs/POLICIES.md) — not yet
implemented (tracked as `T-F51`), see that file's status banner.

---

## Archive Browser Preview — Safe-Type Allowlist (T-F97)

Double-clicking a file inside the Archive Browser (T-F05) extracts just that entry to a
throwaway temp cache and opens it with the OS's default handler, instead of requiring a manual
Extract first. Two constraints keep this from becoming a new attack surface:

1. **Safe-type allowlist only** — `Archiver.Core.Services.PreviewPolicy.IsPreviewable` restricts
   auto-open to images (`.jpg`/`.jpeg`/`.png`/`.gif`/`.bmp`/`.webp`), plain text
   (`.txt`/`.md`/`.log`/`.ini`/`.csv`/`.json`/`.xml`/`.yaml`/`.yml`), common video containers
   (`.mp4`/`.m4v`/`.mkv`/`.avi`/`.mov`/`.wmv`/`.webm`, added T-F109), and audio
   (`.mp3`/`.wav`/`.flac`/`.ogg`/`.m4a`/`.aac`, added T-F109) — no executable, script, `.lnk`,
   macro-capable document, or PDF (PDF is deliberately excluded despite looking "safe" — some
   readers execute embedded JavaScript, unlike every other allowlisted type here).
   `ShellExecute`-ing an arbitrary archive entry with one click, no "Extract to..." friction first,
   would itself be an attack surface (a malicious file inside an archive, opened automatically).
   **This is deliberately stricter than 7-Zip/NanaZip**, confirmed by reading NanaZip's real
   `PanelItemOpen.cpp`/`OpenItemInArchive`: neither has any type allowlist at all — a double-click
   always extracts to temp and unconditionally `ShellExecuteEx`s the result, including `.exe`
   (with special handling to extract every sibling file first, so a portable app's DLL
   dependencies resolve). The only check either performs is `IsVirus_Message` — a Unicode
   right-to-left-override filename-spoofing check, not a file-type restriction. Pakko's narrower
   allowlist is a deliberate choice for its government/defense audience, not something forced by
   archiver convention — see `DECISIONS.md`'s T-F109 entry.
2. **Anything outside the allowlist is warned, not silently extracted to the user's chosen
   Destination.** T-F109: double-clicking a non-allowlisted entry shows a confirm dialog
   (`IDialogService.ShowConfirmAsync`) explaining it can't be safely opened directly; on
   confirmation, only that one entry is extracted — into a dedicated subfolder named after the
   archive, created next to the archive itself on disk (`ArchiveNaming.GetBaseName`), never the
   general Destination field (which is for deliberate bulk Extract Selected/All operations). No
   auto-open follows extraction — the friction is the point.
3. **No shortcut around existing extraction security** — both the preview flow and the
   warn-then-extract flow reuse the real `IExtractionRouter.ExtractAsync` pipeline
   (`ExtractOptions.SelectedEntryPaths` restricted to the one entry), the same mechanism T-F05's
   "Extract Selected" already uses. This means T-F49's whole-archive pre-scan for tar-family
   formats always runs first, unconditionally, before any bytes are extracted — neither path is
   ever a "validate this one entry only" shortcut — and MOTW propagation (above) applies
   automatically, since it happens inside `ZipArchiveService`/`TarSandboxedService` as part of
   normal extraction, not something the App layer has to remember to call separately.

Preview files are staged under `%TEMP%\PakkoPreview\<random>\` (one shared cache root, a fresh
subfolder per preview) and deleted on window close, best-effort — see `DECISIONS.md`'s T-F97
entry.

---

## tar.exe Trust Model

### Why tar.exe Is Acceptable

`C:\Windows\System32\tar.exe` is:

- **Microsoft-signed** — binary integrity verified by Windows Authenticode chain
- **Open source** — based on bsdtar/libarchive, source on GitHub (`microsoft/bsdtar`)
- **Part of Windows Update** — patched through normal OS update cycle, MSRC process applies
- **No SHA-256 verification needed** — unlike third-party binaries, the Authenticode signature provides equivalent or stronger integrity guarantee
- **T-F52 (v1.4) additionally verifies the Authenticode subject is Microsoft before every launch**
  — cheap defense-in-depth, not a primary control (see "Why Sandbox tar.exe at All" below for why)

### Why Sandbox tar.exe at All, Given It's Microsoft-Signed?

Two distinct threat vectors, only the second of which the v1.4 sandbox (T-F52) defends against —
see `DECISIONS.md`'s T-F52 entry for the full design session:

1. **The binary itself is swapped/tampered with.** Reaching `C:\Windows\System32\tar.exe`'s ACLs
   requires SYSTEM-level access — the host is already fully compromised at that point, and no
   sandbox around Pakko's own invocation of it changes that. The realistic version of this vector
   (PATH hijacking from a lower privilege level) is already covered by the existing hard
   constraint that `tar.exe` is always invoked by absolute path, never via PATH search. A
   signature check (above) adds a cheap additional check but is not the reason to sandbox.
2. **The legitimate, unmodified tar.exe is driven by a hostile archive into misbehaving.**
   libarchive is a native parser with a real CVE history, processing attacker-controlled bytes —
   this is the standard "sandbox the untrusted-input parser" pattern (the same reason browsers
   sandbox image/PDF decoders). This is what T-F52's AppContainer confinement, Job Object limits,
   and network isolation actually defend against, and why the sandbox is proportionate despite
   `tar.exe` being a trusted OS component.

### Why Archive Creation (T-F105, v1.4) Is NOT Sandboxed

`TarSandboxedService.CompressAsync` deliberately runs `tar.exe` **unsandboxed** — no
`TarSandboxScope`, no AppContainer, no Job Object — even though it shares a class name and a
process (`tar.exe`) with the extraction path above. This is not an oversight; it follows directly
from the "Why Sandbox tar.exe at All" reasoning: vector 2 above (*"the legitimate, unmodified
tar.exe is driven by a hostile archive into misbehaving"*) requires an attacker-controlled
**archive** feeding libarchive's parser. Archive creation has no such input — `tar.exe` reads
`ArchiveOptions.SourcePaths`, files the user themselves selected via Pakko's own UI/shell
integration, and writes a brand-new archive. There is no untrusted-input parser in this data flow
for the sandbox to isolate; the threat model this task exists to close simply does not apply in
the reverse direction.

This is consistent with `ZipArchiveService.ArchiveAsync`, which has never been sandboxed either,
for the identical reason — ZIP creation via `System.IO.Compression.ZipFile` also only ever reads
trusted local files. The Authenticode signature check (above) still runs before every `tar.exe`
launch regardless of direction — cheap, and not specific to the extraction threat model — but
`CompressAsync` gets no `TarSandboxScope`/quarantine/AppContainer/Job-Object machinery.

If a future task ever wants defense-in-depth on the creation path anyway (e.g. against a
maliciously-named source file tripping a hypothetical libarchive *writer*-side bug), that would be
a new, separate decision with its own cost/benefit case — not an extension of T-F52's threat
model, which is specifically about parsing untrusted archive bytes.

### Trust Chain

```
Windows Update → Microsoft signing infrastructure → tar.exe
```

This is the same trust chain as `System.IO.Compression` via the .NET runtime — both are Microsoft-signed, open source, and maintained by MSRC.

### Process Isolation Levels

| Version | Isolation | Method |
|---------|-----------|--------|
| v1.3 | Medium IL | tar.exe inherits Pakko process token |
| v1.4 | AppContainer | P/Invoke `CreateAppContainerProfile` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` (empty capability list — no network); quarantine directory ACL'd to the AppContainer SID via `SetNamedSecurityInfo`; Job Object (`ActiveProcessLimit = 1`, RAM/CPU limits). Chosen over a Low-IL restricted token because network isolation falls out of the empty capability list for free — no global firewall rule needed (see T-F52 in `TASKS_DONE.md`/`DECISIONS.md`) |

In both cases: extraction goes to a staging directory, all output files are validated (ADS, reserved names, reparse points), then atomically moved to final destination. The staging-directory walk alone is **not** sufficient — a symlink entry can cause tar.exe to write outside the staging directory before any C# code inspects it, confirmed empirically in T-F49 (see `DECISIONS.md`). The primary defense is a whole-archive pre-scan (`tar -tf`/`-tvf`) that rejects any archive containing a symlink/hardlink/device entry or a traversal/rooted/ADS/reserved name before extraction ever runs. The same pre-scan also sums each entry's declared uncompressed size (from `-tvf`'s size column); if that total exceeds 1000x the compressed file's size on disk, extraction is blocked unless the destination has free space for the declared size AND the user explicitly confirms — the same shared evaluator (`ArchiveEntrySecurity.EvaluateCompressionBombAsync`) and confirm-if-it-fits model ZIP uses, computed once for the whole archive since tar-family compression wraps the entire stream rather than each entry independently (T-F94, v1.3; see `DECISIONS.md`'s T-F94 entry — supersedes T-F90's original auto-reject-only version).

### Encrypted-Archive Diagnostics (7z/RAR, T-F113)

Pakko does not decrypt anything — this is diagnostics-only, so a password-protected archive
fails with a clear message instead of raw libarchive stderr. Detection is asymmetric between the
two formats, and deliberately so:

- **RAR** is checked proactively, before tar.exe ever runs, by walking RAR5's own block/extra-area
  structure directly (`ArchiveFormatDetector.IsEncryptedRar`) — RAR headers are never compressed,
  only file *data* is, so reading a block's type/flags is real, bounded metadata parsing, not
  decryption. A block of type 4 ("Archive encryption header") as the very first block means the
  whole archive, including filenames, is unreadable without a password; otherwise, the first File
  Header block's extra area is checked for an "Encryption" record (type 1) — same first-entry-only
  fidelity `ZipArchiveService.IsEncryptedZip` already accepts for ZIP, not a weaker standard.
  Legacy RAR4 (7-byte signature, no version byte) is not parsed — an accepted scope cut, since it
  falls through to the reactive check below instead.
- **7z** cannot be checked the same way: an AES-256 coder ID would appear inside the folder/coder
  metadata, which 7z itself typically stores LZMA-compressed as an "Encoded Header" — inspecting it
  would require decompressing 7z's own header stream, i.e. writing a partial 7z reader, which is
  disproportionate hand-rolled-format-parsing effort for a diagnostics-only task. Instead, 7z (and
  RAR's own rarer header-encrypted case) is classified reactively: the same `tar -tf`/`-tvf`/`-xf`
  calls that already run unconditionally are inspected for a stderr message containing "encrypt"
  (case-insensitive) — confirmed empirically to catch every encryption-related libarchive failure
  message across both formats and both encryption modes. Exact byte offsets and stderr strings are
  recorded in `DECISIONS.md`'s T-F113 entry.

### Absolute Path Requirement

Always invoked as `C:\Windows\System32\tar.exe` — never as `tar` via PATH search. Prevents:
- EXE hijacking via PATH manipulation
- DLL side-loading from working directory
- User-placed `tar.exe` taking precedence over system binary

---

## Vendored 7-Zip: Test-Only, Sandboxed, Never Shipped (T-F114)

`tests/Archiver.Core.PerformanceTests/Tools/7-Zip/{x64,arm64}/7za.exe` is a pinned,
hash-verified, LGPL-attributed copy of 7-Zip's standalone console binary, used purely as a
speed-comparison reference in automated performance-regression tests (T-F114). It exists **only**
in the test tree and is **never** referenced by, built into, or shipped inside
`Archiver.Core`/`Archiver.App`/`Archiver.Shell` or the MSIX package — this is not an exception to
this project's "no 7-Zip, no WinRAR, no third-party compression code" rule (see `SPEC.md`'s
Security Rationale), it is entirely outside the shipped product's dependency surface. See
`tests/Archiver.Core.PerformanceTests/Tools/7-Zip/NOTICE.md` for provenance (exact version,
source URL, SHA-256, vendoring date) and `CONVENTIONS.md` for the packages-allowed note.

**Why it's sandboxed anyway, even though it's test-only:** the binary is hash-verified at
vendoring time, but a third-party executable checked into the repo is still worth containing on
the assumption that a hash check only proves the file matches what was downloaded, not that it's
safe to run unconditionally forever. Every `7za.exe` launch (`SevenZipRunner.cs`) runs under a
Job Object (`SandboxJobObject`, reused directly from the tar.exe sandbox subsystem below —
`ActiveProcessLimit = 1` so it cannot spawn further processes, plus RAM/CPU caps) via the same
`SandboxedProcessLauncher` tar.exe uses. This deliberately stops short of tar.exe's full
AppContainer + ACL'd-quarantine treatment: that layer exists to contain a hostile *archive*
feeding an untrusted-input parser (see "Why Sandbox tar.exe at All" above) — `7za.exe`'s input
here is Pakko's own freshly-generated fixture data, not attacker-controlled, so that specific
threat doesn't apply, and adding AppContainer's staging/ACL overhead would risk biasing the very
timing the tests exist to measure. The Job Object alone still meaningfully bounds the damage a
compromised copy of the binary itself could do (no process spawning, capped resource use)
without touching filesystem access or measured performance. See `DECISIONS.md`'s T-F114 entry for
the full design rationale.

---

## CI Signing Secret: New Supply-Chain Surface (T-F122)

`.github/workflows/build.yml` signs the MSIX with the same local self-signed `CN=Pakko Dev`
development certificate `Deploy.ps1` uses, exported once as a PFX and stored as two GitHub
Actions repo secrets (`PAKKO_DEV_CERT_PFX_BASE64`, `PAKKO_DEV_CERT_PASSWORD`). This is a new
supply-chain surface worth naming explicitly: a compromised repository or organization secret
could be used to sign a malicious build that carries this project's dev-cert identity.

**Why the residual risk is limited today:** the cert is the same self-signed, sideload-only
certificate already described above — it carries no elevated trust of its own. It is not in the
Windows Trusted Root chain and is not recognized by SmartScreen; a package signed with it still
cannot install on a machine that hasn't separately been given `PakkoDev.cer` and had it placed in
`Cert:\LocalMachine\TrustedPeople` (see "Self-signed" in `TASKS.md`'s T-F10 cert-options table).
In practice this means a compromised secret could produce a signed-looking package, but it could
not silently install anywhere Pakko isn't already explicitly trusted — the blast radius is
bounded by the same manual-trust step that already limits this cert's legitimate use.

**This changes once T-F10 (SignPath Foundation) lands:** a SignPath-issued certificate carries
real, broadly-trusted Authenticode reputation, so the same secret-compromise scenario would then
let an attacker produce a package that installs and runs without any of the manual-trust
friction above. At that point this section should be revisited — likely tightening the workflow
to require signing approval via SignPath's own managed CI integration rather than a bare
PFX-in-secrets model, since SignPath's whole design point is to avoid handing the raw private key
to CI at all. Tracked as a T-F10 Phase 1 follow-up, not solved by this task.

---

## Recommended Usage Context

This tool is appropriate for:

- Government and public sector organizations on Windows
- Defense and military administrative workstations
- Businesses with supply chain security policies
- Organizations that have banned or restricted 7-Zip / WinRAR on security grounds
- Any user who prefers an auditable, dependency-minimal archive tool

This tool is **not** a replacement for:

- Full-featured archivers where RAR/7z/encrypted format support is required
- Environments requiring FIPS 140-2 compliant cryptography (ZIP/7z/RAR encryption is not
  implemented — a password-protected archive in any of these three formats is detected and
  refused with a clear error, not silently mishandled; see "Encrypted-Archive Diagnostics" above)

---

## Symlink Detection — Filesystem Compatibility Notes

T-F23 added symlink and junction detection to `ZipArchiveService` using
`File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)`. This section
documents how that detection behaves on every Windows-mountable filesystem.

### How the Detection Works

`IsReparsePoint` calls `GetFileAttributesW` (via .NET's `File.GetAttributes`).
If the attribute `FILE_ATTRIBUTE_REPARSE_POINT` is set, the path is treated as
a symlink or junction and added to `SkippedFiles`. All exceptions are swallowed
and return `false` (conservative: if attributes are unreadable, let the
subsequent file-open produce an `ArchiveError` rather than silently skipping).

### Non-NTFS Filesystem Considerations

This table covers all Windows-mountable filesystems. It applies to every place
in `ZipArchiveService` that touches the filesystem: `IsReparsePoint`,
`AddDirectoryToArchiveAsync`, `ComputeDirectoryBytes`, `TryPropagateMotw`.

| Filesystem | Reparse Points | `IsReparsePoint` | Notes |
|------------|---------------|-------------------|-------|
| **NTFS** | Yes | Correctly true/false | Symlinks, junctions, cloud stubs all detected |
| **ReFS** | Yes | Correctly true/false | Behavior identical to NTFS; all reparse point types supported |
| **FAT32** | No | Always `false` | No reparse points; all files enumerated and archived normally |
| **exFAT** | No | Always `false` | Same as FAT32; `FILE_ATTRIBUTE_REPARSE_POINT` never returned |
| **SMB/UNC (Windows server, NTFS/ReFS backend)** | Possible | Usually correct | DFS junctions are followed transparently by the SMB redirector and appear as normal directories — they are **not** detected as reparse points. True NTFS symlinks may be blocked by server policy; access denial produces `ArchiveError` |
| **SMB/UNC (Linux/Samba backend)** | No NTFS equivalent | Always `false` | Linux symlinks are resolved server-side; they appear as their targets (normal files/dirs) or as inaccessible entries. Symlink cycles are handled server-side — no infinite-loop risk. No reparse-point detection applies |
| **ISO 9660 / UDF** | No | Always `false` | Read-only optical media; no reparse points; all files archived normally. UDF 2.5+ extended attributes are not exposed as `FILE_ATTRIBUTE_REPARSE_POINT` by the Windows driver |
| **MOTW (ADS, Zone.Identifier)** | NTFS/ReFS only | N/A | `TryPropagateMotw` writes `Zone.Identifier` ADS to extracted files. ADS is not supported on FAT32, exFAT, or network shares backed by non-NTFS servers — the write silently fails (best-effort, never fatal) |

### Security Assumptions That Break on Non-NTFS Volumes

| Assumption | NTFS | FAT32/exFAT | SMB/Linux | Notes |
|------------|------|-------------|-----------|-------|
| Symlinks detected and skipped | ✅ | ✅ (none exist) | ⚠️ Partial | Linux symlinks not detected; see above |
| MOTW propagated to extracted files | ✅ | ❌ | ❌ (non-NTFS) | ADS not supported; best-effort silently no-ops |
| No infinite loop on circular symlinks | ✅ | ✅ | ✅ (server-side) | Safe on all filesystems |
| All exceptions produce `ArchiveError` | ✅ | ✅ | ✅ | `IOException` propagates to caller catch blocks |

### Known Limitation: Cloud Storage Stubs

Files that are cloud-only (OneDrive, not locally downloaded) carry
`FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_OFFLINE`. `IsReparsePoint`
returns `true`, causing these files to be added to `SkippedFiles` rather than
being downloaded and archived. The user sees a skipped-file entry but no
indication that it was a cloud stub rather than a symlink.

This is safe (no data corruption or security issue) but is a usability
limitation. Distinguishing cloud stubs from true symlinks requires reading the
raw reparse tag (`IO_REPARSE_TAG_CLOUD_*` vs. `IO_REPARSE_TAG_SYMLINK` /
`IO_REPARSE_TAG_MOUNT_POINT`), which needs P/Invoke or `FileSystemInfo.LinkTarget`.

---

## Reporting Vulnerabilities

Report security issues via GitHub Security Advisories (private disclosure).
Do not open public issues for security vulnerabilities.
