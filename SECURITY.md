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

**No shell extensions in v1.0**
Context menu integration requires elevated trust and COM registration. Excluded to minimize attack surface.

**No network access**
The application has no network capability by design. No telemetry, no update checks, no cloud storage integration.

**No background services**
The app runs only when the user explicitly opens it. No persistent processes.

**No format parsers beyond ZIP**
Each supported format adds parser attack surface. RAR and 7z are excluded permanently. TAR support (planned) uses the Windows built-in `tar.exe` process — not an in-process parser.

**Minimal MSIX capabilities**
`Package.appxmanifest` declares only `runFullTrust`. No `broadFileSystemAccess`, no `internetClient`, no device capabilities.

---

## Known Limitations and Residual Risks

| Risk | Severity | Mitigation |
|------|----------|-----------|
| ZIP path traversal (e.g., `../../etc/passwd` style entries) | High | `System.IO.Compression` with .NET 8 validates entry paths — covered in `ZipArchiveService` tests |
| ZIP bomb (highly compressed entries) | Medium | Ratio-based detection (T-F28, v1.0): entries with ratio >1000:1 skipped and reported |
| Symlink/reparse point attacks in ZIP entries | Medium | T-F37 — reparse point check after file creation, planned post-v1.1 |
| Alternate Data Stream entries (`:` in filename) | Medium | T-F38 — ADS entry rejection, planned post-v1.1 |
| Reserved Windows filenames in entries (`CON`, `NUL`, etc.) | Low-Medium | T-F39 — reserved name filtering, planned post-v1.1 |
| MOTW not propagated to extracted files | Medium | v1.1 gap — MOTW propagation implemented in v1.2 (T-F45) |
| tar.exe runs at Medium IL | Medium | v1.3 gap — Low IL sandbox via P/Invoke in v1.4 (T-F52) |
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

### Pakko's Behavior (v1.2+)

Pakko will propagate MOTW on all extracted files by default:

1. Read `Zone.Identifier` ADS from the source archive
2. Write identical `Zone.Identifier` ADS to each extracted file
3. Default: always on — users cannot disable (only GPO can override in v1.4)

Implementation: `FileStream` with ADS path `"extractedfile.txt:Zone.Identifier"`, no P/Invoke required.

---

## tar.exe Trust Model

### Why tar.exe Is Acceptable

`C:\Windows\System32\tar.exe` is:

- **Microsoft-signed** — binary integrity verified by Windows Authenticode chain
- **Open source** — based on bsdtar/libarchive, source on GitHub (`microsoft/bsdtar`)
- **Part of Windows Update** — patched through normal OS update cycle, MSRC process applies
- **No SHA-256 verification needed** — unlike third-party binaries, the Authenticode signature provides equivalent or stronger integrity guarantee

### Trust Chain

```
Windows Update → Microsoft signing infrastructure → tar.exe
```

This is the same trust chain as `System.IO.Compression` via the .NET runtime — both are Microsoft-signed, open source, and maintained by MSRC.

### Process Isolation Levels

| Version | Isolation | Method |
|---------|-----------|--------|
| v1.3 | Medium IL | tar.exe inherits Pakko process token |
| v1.4 | Low IL | P/Invoke `CreateRestrictedToken` + `SetNamedSecurityInfo` quarantine directory |

In both cases: extraction goes to a staging directory, all output files are validated (ADS, reserved names, reparse points), then atomically moved to final destination.

### Absolute Path Requirement

Always invoked as `C:\Windows\System32\tar.exe` — never as `tar` via PATH search. Prevents:
- EXE hijacking via PATH manipulation
- DLL side-loading from working directory
- User-placed `tar.exe` taking precedence over system binary

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
- Environments requiring FIPS 140-2 compliant cryptography (ZIP encryption is not implemented)

---

## Reporting Vulnerabilities

Report security issues via GitHub Security Advisories (private disclosure).  
Do not open public issues for security vulnerabilities.
