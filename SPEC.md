# SPEC.md — Project Specification

**Version:** 1.0  
**License:** Apache 2.0  
**Distribution:** GitHub source + Microsoft Store (MSIX)

---

## Goal

Create a lightweight GUI wrapper over Windows built-in ZIP functionality.  
Fill the usability gap in Windows Explorer — not replace advanced archivers like 7-Zip.

**Target:** covers ~90% of everyday archive tasks with zero third-party dependencies.

---

## Security Rationale

This section is part of the specification. It defines *why* certain dependencies are excluded — not just *what* is excluded.

### The Problem with Dominant Archive Tools

The two most widely deployed archive tools on Windows — 7-Zip and WinRAR — share a common risk profile:

| Tool | Developer Origin | Source | Reproducible Builds | Known CVEs |
|------|-----------------|--------|--------------------|----|
| 7-Zip | Russian Federation | Open | ❌ No | Multiple critical |
| WinRAR | Russian Federation | Closed | ❌ No | CVE-2018-20250 and others |

The absence of reproducible builds means: even if source code is published, the distributed binary cannot be independently verified to match it. This is not a theoretical concern — the XZ Utils backdoor (2024) demonstrated exactly this attack pattern: clean source, compromised build artifact.

For organizations in Ukraine and allied countries, software with opaque ownership in adversarial jurisdictions and unverifiable binaries is a supply chain risk that should be avoided on workstations handling sensitive documents.

### This Project's Answer

Use only `System.IO.Compression` — part of the .NET Base Class Library:
- open source at `dotnet/runtime` on GitHub
- maintained by Microsoft with a public CVE response process (MSRC)
- reproducible builds available for the .NET runtime itself
- no single developer as a trust anchor

**This is a deliberate architectural constraint, not a technical limitation.**

### Scope of the Security Claim

This project claims:
- ✅ No compression code from developers in adversarial jurisdictions
- ✅ Full source auditability
- ✅ Minimal attack surface (no network, no background services, no shell extensions)
- ❌ Does NOT claim FIPS compliance
- ❌ Does NOT implement encryption
- ❌ Does NOT protect against a compromised .NET runtime or Windows OS

For a full threat model see `SECURITY.md`.

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | WinUI 3 + Windows App SDK |
| Language | C# 12 |
| Runtime | .NET 8 LTS |
| Compression | `System.IO.Compression` (built-in) |
| Optional | `tar.exe` (Windows built-in, future) |

---

## Supported Formats — v1.0

| Format | Status | Method |
|--------|--------|--------|
| ZIP | ✅ Supported | `System.IO.Compression.ZipFile` |
| TAR | 🔜 Future | `tar.exe` via `Process` |
| RAR | ❌ Not supported | — |
| 7z | ❌ Not supported | — |
| Encrypted archives | ❌ Not supported | — |
| Multi-volume | ❌ Not supported | — |

---

## Features — MVP

### Archive

- Select multiple files and/or folders
- Two modes:
  - **Single archive** — all selected items into one `.zip`
  - **Separate archives** — one `.zip` per selected item
- Choose destination folder
- Auto-naming based on source name
- Conflict handling (ask / overwrite / skip)

### Extract

- Select one or more `.zip` archives
- Default: each archive extracted into its own subfolder
- Option: extract all into a single folder
- Conflict handling (ask / overwrite / skip)

### Post-Action Options

These apply after a successful operation:

| Option | Archive | Extract |
|--------|---------|---------|
| Open destination folder | ✅ | ✅ |
| Delete source files | ✅ | — |
| Delete archive after extraction | — | ✅ |

---

## Error Handling Requirements

The app must handle and display friendly messages for:

- File locked by another process (`IOException`)
- Access denied (`UnauthorizedAccessException`)
- Destination path conflict
- Corrupted or invalid ZIP
- Disk full / out of space

Errors must **never crash the app**. Each failed item should be reported individually; other items in the batch continue processing.

---

## Non-Goals (v1.0)

- Advanced compression level tuning
- Explorer shell/context menu integration
- Background or scheduled archiving
- Archive encryption
- Formats other than ZIP
- Preview of archive contents

---

## Future Roadmap (post v1.0)

- Explorer context menu ("Archive with Archiver", "Extract here")
- TAR support via `tar.exe`
- Drag & drop to external apps
- Microsoft Store listing
