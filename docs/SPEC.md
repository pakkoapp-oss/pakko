# SPEC.md — Project Specification

**Status:** released through v1.9.0; v1.10.0 in work (`CHANGELOG.md` per release)  
**License:** Apache 2.0  
**Distribution:** GitHub source + Microsoft Store (MSIX)

---

## Goal

A lightweight archiver over what .NET and Windows already ship: ZIP through `System.IO.Compression`,
the other formats through Windows' own `tar.exe`.  
Fill the usability gap in Windows Explorer — not replace advanced archivers like 7-Zip.

**Target:** covers ~90% of everyday archive tasks with zero third-party dependencies.

---

## Security Rationale

Pakko uses only `System.IO.Compression` (.NET BCL) and Windows' own `tar.exe` instead of 7-Zip/WinRAR — a deliberate
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
| Language | C# 14 |
| Runtime | .NET 10 LTS (supported to 2028-11-14) |
| Compression | `System.IO.Compression` (built-in) |
| Other formats | `tar.exe` (Windows built-in), in an AppContainer sandbox |

---

## Supported Formats

The table of what Pakko reads and creates, and through which engine, is `README.md`'s "Formats"
(the one owner; link to it, don't copy it). Which tar-family formats work depends on the installed
`tar.exe` build (`SECURITY.md`); Pakko offers only what that build reads. Encrypted 7z/RAR are
detected and refused with a clear error (T-F113); UDF disc images are refused with the reason (T-F442).

**Not supported, with the reason (T-F442, measured on both tar.exe builds):** xar (tar.exe 3.8 reads it,
3.5.2 does not, and its version string does not say which, so it would need a probe of its own; `.pkg` is
ambiguous anyway); WARC, uuencode and base64 (little value, weak signatures); mtree (it names files on
the machine, not in the archive); LHA, RPM, lz4, lzop and the rest of libarchive's external-program
filters (not in Windows' tar.exe); disk images and file systems (VHD/VHDX, VMDK, QCOW2, VDI, DMG, WIM,
NTFS/FAT/ext/HFS/APFS, SquashFS) and NSIS, ARJ, split volumes: outside tar.exe. Windows mounts ISO,
VHD and VHDX itself.

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

Only a source processed in full and unchanged since is deleted (to the Recycle Bin where the volume
has one); every other source stays and is named. Each source is judged on its own: a skip inside
one folder keeps that folder, not the others archived with it (T-F434). An error, or a filter that
leaves something out, keeps every source of a single archive; an output that is no longer on disk
keeps them all.

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

## Non-Goals

- Background or scheduled archiving
- Per-method tuning (dictionary size, solid blocks, threads): a compression level only
- Creating RAR archives (no free RAR writer exists) or password-protected 7z archives
- Split or multi-volume archives
- Changing an archive in place (7-Zip's `u`, `d`, `rn`): Pakko writes a new archive
- Flat extraction without folders (7-Zip's `e`)
- Mounting disk images: Windows mounts ISO, VHD and VHDX itself

---

## Windows Explorer Gap Analysis

### What Explorer Provides
- Read many formats (ZIP natively; RAR, 7z and the tar family in Explorer itself since Windows 11 23H2)
- Basic extract (right-click → Extract All)

### What Explorer Lacks
- No "Extract Here" (extracts into a subfolder, no way to extract in-place)
- No "Extract to `<folder_name>`\" shortcut
- No batch extraction to separate folders
- No compression level selection
- No conflict handling (overwrites silently)
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

Since v1.2 Pakko has an Explorer context menu through `IExplorerCommand`, in the Windows 11 menu and
under "Show more options":

- Registered via packaged COM (`com:SurrogateServer` in `Package.appxmanifest`) — appears in the
  modern context menu (no "Show more options" click required)
- **On a supported archive:** Open · Extract… (dialog) · Extract here · Extract to current folder
  (intelligently) · Extract to `<folder_name>\` · Extract each to its own folder (several archives) ·
  Test archive · Scan for threats
- **On a `.par2` file, or an archive with its set beside it:** Verify with PAR2 · Repair with PAR2
- **On any files/folders:** Compress… (dialog) · Add to `<name>.zip` / `<name>.7z` / `<name>.tar` ·
  Hash (CRC-32, SHA-256)
- Each command runs in the Pakko operation window (`Archiver.OperationUi`, T-F268)

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
- Capability detection — Pakko asks the installed `tar.exe` which formats it reads
  (`TarCapabilitySnapshot`, also read by the Explorer menu); only those are offered
- Opening a format that `tar.exe` cannot read gets a message naming its version
- Pakko builds every argument list itself; source names reach `tar.exe` on a `-T -` list (T-F283),
  never as arguments

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

Machine policies under `HKLM\Software\Policies\Pakko\`, with an ADMX/ADML template: Mark of the
Web, allowed and blocked formats, tar.exe off, recovery data off and more. `POLICIES.md` is the one
owner of the list, the values and how they interact; this section only points there.

---

## Recovery Data — PAR2 (v1.8, T-F275)

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
- **Interoperable:** any standard PAR 2.0 set that protects one file is read;
  Pakko's sets are checked against par2cmdline, par2cmdline-turbo and MultiPar.
- **Not in scope:** sets covering several files; locating shifted data after inserted or deleted
  bytes (verification is positional); PAR 3.0; recovery data inside the archive.
- **Group Policy:** `DisableRecoveryData` turns all of it off (`POLICIES.md`).
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
| v1.6 | Remaining fix-batch phases (5–10) + additional format fixtures — **released as v1.6.0** |
| v1.7 | `pakko` in any terminal after a Store install (T-F317), extracted files keep their dates (T-F298), the v1.6.0 device-campaign fixes, the first Native AOT Store build — **released as v1.7.0–v1.7.2** |
| v1.8 | PAR2 recovery data next to created archives: create, verify, repair (T-F275) — **released as v1.8.0** |
| v1.8.1 | 7z archive creation through tar.exe (T-F342) + Test for every format tar.exe reads (T-F397) + fixes — **released** |
| v1.9 | Store release: `pakko` 7-Zip switches (T-F411 waves A-E, T-F410), one path pipeline (T-F420), a red-team suite (T-F419), and the v1.9.0 wave — **released as v1.9.0** |
| v1.10 | Bug fixes after v1.9.0, one owner per name/path encoding boundary (T-F441), progress on the taskbar button and tray icon (T-F432), and the read-only formats tar.exe opens: ISO, CAB, cpio, ar, `.Z`, lzip (T-F442) — **released as v1.10.0** |
| v1.11 | .NET 11 and C# 15 (T-F492), the deflate encoder reused per ZIP worker (T-F318), ZIP encryption through the BCL where backward compatible (T-F493), the bugs left after v1.10.0: the v1.11.0 wave of `docs/TASKS.md` — **in work, not released** |
| v2.0 | `pakko` measured as a stand-in for 7-Zip's console program, gaps closed where they fit Pakko's rules (T-F343; T-F412, T-F413 and T-F415 moved to v1.9) |

Package version (`Package.appxmanifest`'s `Identity Version`) tracks MSIX packaging, not this
table 1:1 — see `CLAUDE.md`'s Deployment section.
