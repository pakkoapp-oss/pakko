# CLI.md — Archiver.CLI Command Specification (Planned, T-F09)

> **Status: not yet implemented.** `Archiver.CLI` does not exist yet — T-F09 in `TASKS.md` is
> `future`. This document is the command/switch specification to build against once T-F09 starts,
> kept separate from `TASKS.md` so it can be read as reference documentation (and eventually
> shipped as the CLI's own `--help`/README content) without wading through task-tracking prose.
> `TASKS.md`'s T-F09 entry owns acceptance criteria and status; this file owns the command/switch
> tables — don't duplicate one into the other.

---

## Goal

`Archiver.CLI`'s commands/switches are spelled the same as `7z.exe`'s so a user who already knows
7-Zip doesn't have to relearn syntax — **not** a byte-for-byte compatible drop-in. A script pointed
at `7z` cannot be silently repointed at `pakko` and expect identical exit codes or output format
for every command. "Maximally close to 7z" collides with Pakko's minimalism (several real 7z
commands require archive mutation Pakko won't do), so a genuine drop-in would mean either faking
unsupported commands or silently diverging from 7z's own documented behavior. Familiar-but-distinct
avoids both.

## Architecture

`Archiver.CLI` is a third thin frontend over `Archiver.Core`, exactly like `Archiver.App` and
`Archiver.Shell` already are. It does **not** become a shared engine that `Archiver.App`/
`Archiver.Shell` shell out to — Core is already the shared engine; three frontends consume it
in-process. (Subprocess delegation is the right pattern for one specific existing case — isolating
the *untrusted external* `tar.exe`, T-F52's Low IL sandbox — that's isolation of an external
binary, not routing trusted managed Core through a subprocess, and doesn't generalize here.)

Research basis: 7-Zip's real command-line parser source (`ArchiveCommandLine.h`/`.cpp`, via
NanaZip's vendored copy — a direct 7-Zip fork). Confirmed the authoritative 11-command set
(`g_Commands = "audtexlbih"`, one character per `NCommandType` enum entry in declaration order)
plus the two-character `rn` (rename) special case, and the real switch-prefix table
(`kSwitchForms`).

---

## Command table — 7z command → Pakko support

| 7z | Meaning | Pakko support |
|----|---------|----------------|
| `a` | Add (create/add to archive) | Partial — ZIP create only; no `-t7z`/`-ttar` archive *creation* (tar-family creation is T-F36's deferred v1.5 scope) |
| `u` | Update (add newer/changed files to an *existing* archive) | Not supported — no "diff against existing archive contents" logic exists anywhere in `Archiver.Core` |
| `d` | Delete (remove entries from an archive) | Not supported, deliberately — no in-place archive mutation, matches T-F05's "not an archive manager" positioning |
| `t` | Test (verify integrity) | Partial — ZIP via existing `TestAsync` (T-F62); tar-family has no test capability (`ITarService` has no Test method, per T-F86's finding) |
| `e` | Extract, flattened (no directory structure) | Not supported — Pakko's extraction always preserves the archive's folder structure; no flatten mode exists |
| `x` | Extract with full paths | Supported — matches `ExtractAsync`'s existing default behavior |
| `l` | List contents | Not supported today — this is exactly T-F05 (Archive Browser)'s listing API; T-F09 should consume that once it exists, not duplicate it |
| `b` | Benchmark | Not supported, deliberately out of scope (same reasoning as T-F05's NanaZip-toolbar scope cuts) |
| `i` | Info (list supported archive formats/codecs) | Not implemented, but trivial — would report ZIP (always) + live `TarCapabilities` (detected formats) |
| `h` | Hash | Partial — T-F46 hashes arbitrary files (SHA-256) via the GUI, but nothing hashes *entries inside* an archive specifically |
| `rn` | Rename entries in an archive | Not supported, deliberately — in-place mutation, same reasoning as `d` |

## Switch fidelity — per-switch, not full coverage

| 7z switch | Meaning | Pakko mapping |
|-----------|---------|----------------|
| `-o{dir}` | Output directory | Maps directly to `ExtractOptions.Destination` |
| `-p{pwd}` | Password / encryption | Not supported — `System.IO.Compression` has no ZIP encryption support; a real capability gap, not a scope choice |
| `-r[-\|0]` | Recurse subdirectories | Archiving already recurses folders by default; the 7z on/off nuance needs its own check against current `ArchiveOptions` behavior |
| `-i{pattern}` / `-x{pattern}` | Include/exclude filename patterns | Not supported — no wildcard include/exclude filtering exists in `ArchiveOptions`/`ExtractOptions` today |
| `-y` | Assume yes, suppress prompts | Needs a CLI-side default for Pakko's interactive callbacks (conflict resolution, compression-bomb confirm, T-F94) since there's no UI to prompt |
| `-t{type}` | Archive type override | Partial — format is already auto-detected via `ArchiveFormatDetector`; only meaningful for `a` (creation), and only `-tzip` is real today |
| `-v{size}` | Split into volumes | Not supported — no multi-part/split-archive logic exists anywhere in `Archiver.Core` |
| `-m{params}` (e.g. `-mx=9`) | Compression method/level | Partial — 7z's 0–9 scale doesn't map 1:1 onto `System.IO.Compression.CompressionLevel`'s four discrete values (`NoCompression`/`Fastest`/`Optimal`/`SmallestFiles`); needs an explicit, documented bucketing, not a naive `/9*4` |
| `-ao{a\|s\|u\|t}` | Overwrite mode | Mostly supported — maps to `ExtractOptions.OnConflict` (Overwrite/Skip/Rename); 7z's 4th variant (`t`, rename existing instead of new) has no Pakko equivalent |
| `-scc`/`-ssc` | Console charset / case-sensitivity | `-scc` not applicable (.NET is Unicode-native); case-sensitive matching is an open question worth a decision, not an assumption |

---

## Unknown/unsupported input — three-way rule, never silent

1. **Unparseable token** (not a real 7z command/switch at all) → 7z-style "Incorrect command line"
   error, non-zero exit — a typo, not a scope gap.
2. **Real 7z command Pakko deliberately doesn't implement** (`u`, `d`, `rn`, `b`, flat `e`) →
   explicit `"not supported by Pakko: <reason>"` message naming the specific gap (e.g. "in-place
   archive mutation is out of scope — see CLAUDE.md"), not a generic parse error. This
   distinguishes "real 7z feature we chose not to build" from a typo, which matters for anyone
   debugging a script.
3. **Real, supported command with an unsupported switch** → name the specific switch in the error,
   don't fail generically.

Never silently ignore an unrecognized token or switch and proceed as if it wasn't there.

---

See `TASKS.md`'s T-F09 entry for acceptance criteria, test-layer requirements, and current status.
