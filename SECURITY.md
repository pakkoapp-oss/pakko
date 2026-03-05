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

## Supply Chain Risk: 7-Zip and WinRAR

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

### Risk Classification for Regulated Environments

For organizations operating under security requirements (government, defense, critical infrastructure, financial sector):

- Using software from developers in adversarial jurisdictions without source auditability is a supply chain risk
- Unverifiable binaries processing sensitive files fail basic security hygiene standards
- Neither tool meets reproducible build requirements

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
| ZIP path traversal (e.g., `../../etc/passwd` style entries) | High | `System.IO.Compression` with .NET 8 validates entry paths — verify behavior in `ZipArchiveService` tests |
| ZIP bomb (deeply nested or highly compressed) | Medium | No size limit enforced in v1.0 — add extraction size limit in v2.0 |
| Symlink attacks in ZIP entries | Medium | `ZipFile.ExtractToDirectory` behavior on Windows — document and test |
| Microsoft as trust anchor | Low-Medium | Accepted tradeoff for the target audience; .NET is open source and auditable |

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
