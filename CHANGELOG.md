# CHANGELOG.md — Release History

Human-readable summary of what shipped in each tagged release. One section per version tag,
newest first. This file starts at `v1.4.1` — earlier releases (`v1.0.0`, `v1.1.0`, `v1.4.0`) are
not backfilled; see `docs/TASKS_DONE.md` and `git log` if you need that history.

Each entry lists the `T-Fxx` tasks completed since the previous release tag, in plain language —
not a re-statement of `docs/TASKS_DONE.md`'s full acceptance-criteria detail. See `docs/TASKS_DONE.md` for
the technical account of any task named here.

---

## v1.4.3 — 2026-07-26

Point release adding a version flag to the CLI.

- **T-F134** — `pakko -v`/`--version` prints the CLI's own version and exits 0. A released
  `pakko.exe` now always reports the exact release tag it shipped under.

---

## v1.4.2 — 2026-07-25

Point release widening ZIP-format recognition to two more container-file families, plus
Microsoft Store submission preparation.

- **T-F131** — Explorer/file-association recognition of `.jar`/`.war`/`.ear` (Java) and `.apk`
  (Android) as ZIP-format archives, so the full Pakko context menu (Extract/Test/etc.) works on
  them directly.
- **T-F133** — Same recognition extended to `.asice`/`.asics` (ASiC-E/ASiC-S signed containers)
  and `.bdoc` (Estonia's ASiC-E profile), at a user's request.
- **T-F129** — Microsoft Store submission preparation: WACK (Windows App Certification Kit)
  fixes, vector-accurate brand assets regenerated from the canonical SVG source, and an
  `Package.appxmanifest` revision-segment fix required for submission. The Store listing itself
  is not live yet.
- **T-F130** — Fixed intermittent CI test failures in `Archiver.Core.IntegrationTests` caused by
  sandboxed tests racing each other under parallel execution.

---

## v1.4.1 — 2026-07-20

Point release covering Explorer hash commands and a `pakko.exe` CLI addition.

- **T-F128** — Explorer context-menu hash commands: CRC-32/SHA-256 submenu for files, folder
  DataSum/NamesSum, a progress-bar fix, a Size line, full 37-locale localization, and CRC-32
  performance work (intra-file parallel hashing).
- **T-F128/T-F09 follow-up** — `pakko.exe` gained an `h` (hash) command and `-si` zero-copy
  streaming support.

---
