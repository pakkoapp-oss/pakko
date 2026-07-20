# CHANGELOG.md — Release History

Human-readable summary of what shipped in each tagged release. One section per version tag,
newest first. This file starts at `v1.4.1` — earlier releases (`v1.0.0`, `v1.1.0`, `v1.4.0`) are
not backfilled; see `TASKS_DONE.md` and `git log` if you need that history.

Each entry lists the `T-Fxx` tasks completed since the previous release tag, in plain language —
not a re-statement of `TASKS_DONE.md`'s full acceptance-criteria detail. See `TASKS_DONE.md` for
the technical account of any task named here.

---

## v1.4.1 — 2026-07-20

Point release covering Explorer hash commands and a `pakko.exe` CLI addition.

- **T-F128** — Explorer context-menu hash commands: CRC-32/SHA-256 submenu for files, folder
  DataSum/NamesSum, a progress-bar fix, a Size line, full 37-locale localization, and CRC-32
  performance work (intra-file parallel hashing).
- **T-F128/T-F09 follow-up** — `pakko.exe` gained an `h` (hash) command and `-si` zero-copy
  streaming support.

---
