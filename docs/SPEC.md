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
| Runtime | .NET 10 LTS (supported to 2028-11-14) |
| Compression | `System.IO.Compression` (built-in) |
| Optional | `tar.exe` (Windows built-in, future) |

---

## Supported Formats

| Format | Status | Method |
|--------|--------|--------|
| ZIP | ✅ v1.0 (read+write) | `System.IO.Compression.ZipFile` |
| TAR/GZ/BZ2/XZ/ZST/LZMA | ✅ v1.3 (read), ✅ v1.4 (write, T-F105) | `tar.exe` (Windows built-in) |
| RAR | ✅ v1.3 (read only — no libarchive writer exists) | `tar.exe` (Windows built-in) |
| 7z | ✅ v1.3 (read only — no libarchive writer exists) | `tar.exe` (Windows built-in) |
| Password-protected ZIP | ✅ v1.5 (read: ZipCrypto + WinZip AES; create: WinZip AES-256 only, T-F193 — file names stay unencrypted) | `System.IO.Compression` + .NET `System.Security.Cryptography` |
| Encrypted 7z/RAR | ❌ Detected and refused with a clear error (T-F113) | — |
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

- Registered via packaged COM (`com:SurrogateServer` in `Package.appxmanifest`) — appears in the
  modern context menu (no "Show more options" click required)
- **Commands available on a supported archive:** Extract… (dialog) · Extract here (flat) · Extract
  to current folder (intelligently) · Extract to `<folder_name>\` · Test archive
- **Commands available on any files/folders:** Compress… (dialog) · Add to `<name>.zip` · Add to
  `<name>.tar`

Implementation: `Archiver.ShellExtension` project, COM-based `IExplorerCommand`, registered in `Package.appxmanifest`. See `DIAGRAMS.md`'s diagram 1 for the full sequence and every command's exact enable/visibility condition.

---

## Mark of the Web (MOTW)

### Why It Matters

MOTW (Zone.Identifier ADS) signals to Windows and Office that a file originated from an untrusted zone. Without MOTW:

- Office opens documents in **edit mode** instead of Protected View — macro execution is not blocked
- Windows does not prompt before executing downloaded scripts

### What Other Extractors Do

Windows Explorer's own ZIP folder propagates MOTW (see `SECURITY.md`). 7-Zip (default settings) does not propagate MOTW. NanaZip 6.0 (Feb 2026) added MOTW propagation as a default.

### Pakko's Behavior (v1.2+)

- On extraction: reads `Zone.Identifier` ADS from the source archive
- Writes `Zone.Identifier` ADS to **every** extracted file
- On by default; the user may leave it off for one extraction (T-F360: a checkbox in the App's
  extract options, `pakko x -snz0`) — previews and nested archives always get it
- The `EnforceMOTW` Group Policy (v1.4) overrides the user's choice either way

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
- v1.4: AppContainer sandbox (T-F52) — `CreateAppContainerProfile` +
  `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`, own Job Object, ACL'd quarantine directory, no
  network capability. Chosen over a Low-IL restricted token — see `SECURITY.md`'s canonical
  "Process Isolation Levels" rationale for why; don't restate the comparison here.

### Quarantine Pattern

Same as ZIP extraction (T-F26/T-F27): extract to staging directory on same disk, validate all output files, then atomic move to final destination.

---

## Group Policy Support (v1.4)

Registry path: `HKLM\Software\Policies\Pakko\`

| Key | Type | Effect |
|-----|------|--------|
| `EnforceMOTW` | DWORD | Controls MOTW propagation (`0`=disabled, `1`=all files, `2`=unsafe extensions only) |
| `AllowedFormats` | multi-string | Whitelist of allowed formats |
| `BlockedFormats` | multi-string | Blocklist — takes precedence over `AllowedFormats` |
| `DisableTarExtraction` | DWORD | Block all tar.exe extraction and creation, hide those formats in the UI |

ADMX/ADML template provided for enterprise Group Policy deployment. `POLICIES.md` is the canonical
spec for this table (full value vocabulary, defaults, interaction rules) — this section is a
teaser only; don't let it drift from `POLICIES.md` again.

---

## Recovery Data — PAR2 (v1.8, T-F275, in progress)

For archives kept on flash drives or optical media or carried offline, where bad sectors or a
truncated copy are the realistic damage.

- **Create:** standard PAR 2.0 files next to a created archive, over the finished archive's bytes
  (an encrypted ZIP's ciphertext — repair needs no password). Every format Pakko creates. Offered
  where archive options are set: the App's "New archive" card (also opened by Explorer's
  "Compress...") with 5/10/20 % (default 5) and `pakko a -rr[N]` (1–100); not on the one-click
  verbs.
- **Verify and repair:** `pakko t` reports the set's state, `pakko r` repairs; Explorer offers both
  on a `.par2` file and on an archive with its set beside it; the App shows the state and repairs.
  The repaired copy is a new file next to the archive (another folder when that one is not
  writable); the original is never written.
- **Interoperable:** sets made by par2cmdline, MultiPar or QuickPar that protect one file are read;
  Pakko's sets are checked against par2cmdline, par2cmdline-turbo and MultiPar.
- **Not in scope:** sets covering several files; locating shifted data after inserted or deleted
  bytes (verification is positional); PAR 3.0; recovery data inside the archive.
- **Group Policy:** `DisableRecoveryData` turns all of it off (`POLICIES.md` once implemented).
- Design and research: `DECISIONS.md` "T-F275 — PAR2 recovery data".

---

## Future Roadmap

| Version | Focus |
|---------|-------|
| v1.1 | Store release — ZIP only |
| v1.2 | Shell extension + MOTW + file associations + hash viewer |
| v1.3 | tar.exe integration — RAR/7z/tar extraction + capability detection — **complete** |
| v1.4 | GPO/ADMX + AppContainer sandbox (P/Invoke, T-F52) + strict mode policy + Archive Browser (T-F05) + TAR creation via tar.exe (T-F105, pulled forward from v1.5 2026-07-16) — **complete, including GPO/ADMX (T-F51, done 2026-07-18)** |
| v1.5 | Password-protected ZIP (read ZipCrypto + WinZip AES, create AES-256; T-F188–T-F194) + `pakko://` scheme removed (T-F232) + extraction/sandbox correctness and security fixes (fix phases 1–4a) — **released as v1.5.0** |
| v1.6 | Remaining fix-batch phases (5–10, `docs/TASKS.md`'s fix-batch index) + additional format fixtures |
| v1.8 | PAR2 recovery data next to created archives: create, verify, repair (T-F275) |
| v1.9 | 7z archive creation through tar.exe (T-F342) |
| v2.0 | `pakko` measured as a stand-in for 7-Zip's console program, gaps closed where they fit Pakko's rules (T-F343) |

Package version (`Package.appxmanifest`'s `Identity Version`) tracks MSIX packaging, not this
table 1:1 — see `CLAUDE.md`'s Deployment section.
