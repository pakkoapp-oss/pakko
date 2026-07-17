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

Pakko uses only `System.IO.Compression` (.NET BCL) instead of 7-Zip/WinRAR — a deliberate
architectural constraint driven by supply-chain trust concerns (developer jurisdiction, lack of
reproducible builds, CVE history), not a technical limitation.

**For the full rationale — CVE tables, risk classification, and the scope of what this project
does and does not claim — see `SECURITY.md`** (the canonical, most current source; this section
is a teaser only, per `CLAUDE.md`'s Documentation Map).

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

## Supported Formats

| Format | Status | Method |
|--------|--------|--------|
| ZIP | ✅ v1.0 (read+write) | `System.IO.Compression.ZipFile` |
| TAR/GZ/BZ2/XZ/ZST/LZMA | ✅ v1.3 (read), 🔜 v1.4 (write, T-F105) | `tar.exe` (Windows built-in) |
| RAR | ✅ v1.3 (read only — no libarchive writer exists) | `tar.exe` (Windows built-in) |
| 7z | ✅ v1.3 (read only — no libarchive writer exists) | `tar.exe` (Windows built-in) |
| Encrypted archives | ❌ Out of scope | — |
| Multi-volume | ❌ Out of scope | — |

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
- Background or scheduled archiving
- Archive encryption
- Preview of archive contents

---

## Windows Explorer Gap Analysis

### What Explorer Provides
- Read many formats (ZIP natively; RAR/7z/tar via built-in `tar.exe` on Win 11 23H2+)
- Basic extract (right-click → Extract All)

### What Explorer Lacks
- No "Extract Here" (extracts into a subfolder, no way to extract in-place)
- No "Extract to `<folder_name>`\" shortcut
- No batch extraction to separate folders
- No compression level selection
- No conflict handling (overwrites silently)
- No MOTW propagation — extracted files lose Mark of the Web
- No per-file progress or extraction log
- No GPO policy for format restrictions

### Pakko's Unique Value
- Auditable stack — `System.IO.Compression` + Windows built-in `tar.exe`, no third-party binaries
- MOTW propagation — extracted files inherit Zone.Identifier from archive
- Shell integration — modern IExplorerCommand context menu
- Security-first defaults — reparse point protection, ADS blocking, ZIP bomb detection
- GPO policy support for enterprise deployment

---

## Shell Extension

Pakko v1.2 adds a native Windows 11 context menu via `IExplorerCommand`:

- Registered via MSIX AppExtension — appears in modern context menu (no "Show more options" click required)
- **Commands available on ZIP files:** Extract here · Extract to `<folder_name>\` · Open with Pakko
- **Commands available on any files/folders:** Archive with Pakko

Implementation: `Archiver.ShellExtension` project, COM-based `IExplorerCommand`, registered in `Package.appxmanifest`.

---

## Mark of the Web (MOTW)

### Why It Matters

MOTW (Zone.Identifier ADS) signals to Windows and Office that a file originated from an untrusted zone. Without MOTW:

- Office opens documents in **edit mode** instead of Protected View — macro execution is not blocked
- Windows does not prompt before executing downloaded scripts

### Explorer's Gap

Windows Explorer does **not** propagate MOTW to extracted files. 7-Zip (default settings) also does not propagate MOTW. NanaZip 6.0 (Feb 2026) added MOTW propagation as a default.

### Pakko's Behavior (v1.2+)

- On extraction: reads `Zone.Identifier` ADS from the source archive
- Writes `Zone.Identifier` ADS to **every** extracted file
- Always on by default — cannot be disabled by user
- Only a GPO policy can disable MOTW propagation (v1.4)

Implementation: `FileStream` opened with ADS path `file.txt:Zone.Identifier`.

---

## tar.exe Integration (v1.3)

### Design

- Always uses absolute path `C:\Windows\System32\tar.exe` — no PATH lookup, prevents EXE hijacking
- Capability detection at app startup — probes which formats the current `tar.exe` supports
- UI shows only formats the detected `tar.exe` supports
- Unsupported formats shown greyed with tooltip "Requires Windows 11 23H2+"
- Argument whitelist: only `-xf` and `-C` allowed — no arbitrary flag injection

### Process Isolation

- v1.3: `tar.exe` runs at Medium IL (inherits from Pakko process)
- v1.4: restricted token via P/Invoke (`CreateRestrictedToken`, `SetNamedSecurityInfo`) — Low IL quarantine directory

### Quarantine Pattern

Same as ZIP extraction (T-F26/T-F27): extract to staging directory on same disk, validate all output files, then atomic move to final destination.

---

## Group Policy Support (v1.4)

Registry path: `HKLM\Software\Policies\Pakko\`

| Key | Type | Effect |
|-----|------|--------|
| `EnforceMOTW` | DWORD | Force MOTW propagation even if user disables |
| `AllowedFormats` | multi-string | Whitelist of allowed formats |
| `StrictZipBombMode` | DWORD | Lower compression ratio threshold |
| `DisableTarExtraction` | DWORD | Block all tar.exe extraction |

ADMX/ADML template provided for enterprise Group Policy deployment.

---

## Future Roadmap

| Version | Focus |
|---------|-------|
| v1.1 | Store release — ZIP only |
| v1.2 | Shell extension + MOTW + file associations + hash viewer |
| v1.3 | tar.exe integration — RAR/7z/tar extraction + capability detection — **complete** |
| v1.4 | GPO/ADMX + AppContainer sandbox (P/Invoke, T-F52) + strict mode policy + Archive Browser (T-F05) + TAR creation via tar.exe (T-F105, pulled forward from v1.5 2026-07-16) — **complete except GPO/ADMX (T-F51), still open/future** |
| v1.5 | Additional format fixtures |

Package version (`Package.appxmanifest`'s `Identity Version`) tracks MSIX packaging, not this
table 1:1 — see `CLAUDE.md`'s Deployment section.
