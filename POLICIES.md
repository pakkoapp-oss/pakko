# POLICIES.md — Group Policy Reference for System Administrators

> **Status: shipped 2026-07-18.** Every policy below is implemented, covered by `dotnet test`, and
> confirmed on real hardware — a `gpedit.msc` import and each key's real effect against the
> installed app — matching this document exactly. See `DECISIONS.md`'s T-F51 entry and
> [`T-F51`](TASKS.md) in `TASKS.md` for the full trail.

---

## Purpose

Pakko is built for environments (government, defense, regulated enterprise) where an
administrator needs to enforce baseline behavior regardless of what an individual user configures
— e.g. guaranteeing Mark-of-the-Web propagation, or blocking specific archive formats fleet-wide.
This document is the canonical reference for that policy surface. `SECURITY.md` covers *why* each
policy matters from a threat-model perspective; this file covers *what to set and how*.

---

## Registry path

All policies live under:

```
HKLM\Software\Policies\Pakko\
```

This follows the same convention used by comparable Windows software (e.g. NanaZip's
`HKLM\Software\Policies\M2Team\NanaZip`) — machine-wide, admin-only (standard `HKLM\...\Policies`
ACLs already restrict write access to administrators), read once at Pakko startup.

**A key that is absent or not configured never changes Pakko's default behavior and never causes
an error.** Policies only take effect when explicitly set.

---

## Policy keys

| Value name | Type | Data | Effect |
|---|---|---|---|
| `EnforceMOTW` | `REG_DWORD` | `0` = disabled, `1` = all files, `2` = unsafe extensions only | Controls Mark-of-the-Web (`Zone.Identifier`) propagation to extracted files. Absent = Pakko's shipped default (`1`, all files) is unchanged. |
| `AllowedFormats` | `REG_MULTI_SZ` | one format name per line — `zip`, `tar`, `gzip`, `bz2`, `xz`, `zstd`, `lzma`, `rar`, `sevenzip` | Whitelist. If set, only listed formats can be extracted or created. Absent = no restriction. |
| `BlockedFormats` | `REG_MULTI_SZ` | same format name vocabulary as `AllowedFormats` | Blocklist. **Takes precedence over `AllowedFormats`** — a format listed in both is blocked. |
| `DisableTarExtraction` | `REG_DWORD` | `0`/`1` | `1` = Pakko never spawns `tar.exe` at all (blocks RAR/7z/tar/tar.gz/tar.bz2/tar.xz/tar.zst/tar.lzma extraction and creation outright, and hides the corresponding format options in the UI). |

### `EnforceMOTW` in detail

Mark-of-the-Web (the `Zone.Identifier` NTFS Alternate Data Stream) tells Windows and Office that a
file originated from the internet, gating Protected View, SmartScreen, and script execution
warnings. Pakko always propagates MOTW from an archive to its extracted files by default; this
policy controls *how much* of that propagation happens, not whether the underlying feature exists:

- `0` — MOTW propagation is fully disabled. Use only if a downstream tool cannot handle the
  `Zone.Identifier` stream correctly (rare) — this trades away a real security control.
- `1` — MOTW is written to **every** extracted file. This is Pakko's shipped default even with no
  policy configured.
- `2` — MOTW is written **only** to files whose extension is commonly associated with code
  execution (executables, scripts, shortcuts, registry files — the same category Windows'
  Attachment Manager / SmartScreen already treat as higher-risk), leaving ordinary data files
  (documents, images, text) unmarked. Useful if downstream MOTW-aware tooling generates excessive
  friction on plain data files in your environment.

### `AllowedFormats` / `BlockedFormats` in detail

Format names are case-insensitive and match Pakko's internal format detection, not file
extensions directly:

| Name | Matches |
|---|---|
| `zip` | `.zip` |
| `tar` | plain `.tar` |
| `gzip` | `.gz`, `.tar.gz`/`.tgz` |
| `bz2` | `.bz2`, `.tar.bz2`/`.tbz2` |
| `xz` | `.xz`, `.tar.xz`/`.txz` |
| `zstd` | `.zst`, `.tar.zst` |
| `lzma` | `.lzma`, `.tar.lzma` |
| `rar` | `.rar` (read-only in Pakko — no RAR creation) |
| `sevenzip` | `.7z` (read-only in Pakko — no 7z creation) |

If both lists mention a format, it is blocked. Leaving both absent means every format Pakko
supports is available, same as today's shipped behavior. A typical restrictive deployment sets
only `BlockedFormats` (e.g. block `rar`,`sevenzip` to reduce third-party-parser attack surface,
since those two formats route through `tar.exe`, a more complex parser than the built-in
`System.IO.Compression` ZIP path — see `SECURITY.md`'s tar.exe trust model) rather than an
exhaustive `AllowedFormats` list that has to be kept in sync with every future format Pakko adds.

### `DisableTarExtraction` in detail

This is a coarser lever than `BlockedFormats`: instead of naming individual formats, it stops
Pakko from ever launching the `tar.exe` subprocess at all (the AppContainer-sandboxed process that
handles RAR/7z/tar/tar.gz/tar.bz2/tar.xz/tar.zst/tar.lzma — see `SECURITY.md`'s tar.exe trust
model for why that subprocess is sandboxed in the first place). Set this if your environment wants
to eliminate that entire subprocess-spawn surface regardless of format, not just restrict which
archive types are readable. It overlaps with setting `BlockedFormats` to every non-ZIP format, but
is the more direct and future-proof lever if Pakko ever adds another tar.exe-routed format.

**Note:** an earlier draft of this policy set also included a `StrictZipBombMode` key (a
compression-ratio-threshold override for zip-bomb detection). It was dropped before implementation
— no comparable archiver (7-Zip, WinRAR, NanaZip) exposes that threshold as an admin-configurable
value, and it didn't correspond to a documented sysadmin need, only a hypothetical one. Pakko's
existing fixed 1000:1 compression-ratio bomb detection (see `SECURITY.md`) is unaffected by Group
Policy and applies unconditionally, regardless of any of the four keys above.

---

## Deployment (ADMX/ADML)

Once implemented, Pakko will ship `deploy/Pakko.admx` and `deploy/Pakko.adml` (English) alongside
a short `deploy/README.md` covering both usage paths:

- **Local Group Policy** (single machine, testing): `gpedit.msc` after copying the two files into
  `%SystemRoot%\PolicyDefinitions` (`.admx`) and `%SystemRoot%\PolicyDefinitions\en-US` (`.adml`).
- **Domain deployment**: copy the same two files into the domain's Central Store
  (`\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions`) so they appear automatically in Group
  Policy Management Console on every domain controller — no per-machine copy needed.

Until `deploy/Pakko.admx`/`deploy/Pakko.adml` ship, the same effect can be achieved manually via
`reg add`/`New-ItemProperty` directly against `HKLM\Software\Policies\Pakko\`, or any existing GPO
"Registry" preference item pointed at the same path/value names in the table above.

---

## See also

- [`SECURITY.md`](SECURITY.md) — threat model and rationale behind MOTW propagation, the tar.exe
  sandbox, and format-related risk classification.
- [`TASKS.md`](TASKS.md) — `T-F51` entry: full implementation design, current status, and
  real-world grounding research behind this policy set.
