# CLI.md — Archiver.CLI Command Specification (T-F09)

> **Status: implementation complete, on-device verification pending.** `Archiver.CLI` exists at
> `src/Archiver.CLI/` — T-F09 in `TASKS.md` is `[~]`. This document is the command/switch
> specification it's built against, kept separate from `TASKS.md` so it can be read as reference
> documentation (and shipped as the CLI's own `--help` content) without wading through
> task-tracking prose. `TASKS.md`'s T-F09 entry owns acceptance criteria and status; this file
> owns the command/switch tables — don't duplicate one into the other.

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

## Distribution

`Archiver.CLI` ships as a separate, standalone downloadable artifact — not bundled inside the
MSIX, and does not require Pakko's GUI to be installed. Published self-contained per architecture
(`win-x64`/`win-arm64`, matching the solution's existing platform set) via GitHub Releases — the
same channel already used for v1.1+ — each build accompanied by a `SHA256SUMS` file so a script or
a user can verify the download before running it. This is a packaging/release-engineering concern
layered on top of the Architecture section above, not a change to it: `Archiver.CLI` itself stays
exactly what's described there — a third thin frontend consuming `Archiver.Core` in-process,
built self-contained (`dotnet publish --self-contained`, the same satellite-EXE pattern
`Archiver.Shell` already uses) so it runs on a machine with no separate .NET install. The built exe
is named `pakko.exe` (`Archiver.CLI.csproj`'s `AssemblyName`, distinct from the project/folder
name) — short, matches the product name, and matches the `pakko:` prefix already used in every
stderr message and in `--help`'s own `USAGE:` line.

**Not added to `PATH` automatically.** Same as ripgrep/fd/bat's own zip distributions — the user
extracts the zip and either adds that folder to `PATH` themselves (System Properties → Environment
Variables, or `$env:PATH` in a PowerShell profile) or invokes it by full path. No installer/MSI is
shipped for the CLI (only the GUI is MSIX-packaged), so there is no automatic PATH step today; a
future package-manager listing (`winget`/`scoop`) would be the natural way to get real "install
once, available everywhere" behavior without hand-writing a PATH-mutating installer — tracked as a
possible follow-up, not yet scheduled.

**Naming note (why not something that could collide with the GUI):** Windows adds
`%LOCALAPPDATA%\Microsoft\WindowsApps` to every user's `PATH` automatically, and any MSIX
`AppExecutionAlias` registered there resolves before most user-added `PATH` entries — this is the
same mechanism that makes a Microsoft Store Python stub silently shadow a real `python.exe`
installed elsewhere. Pakko's own `Package.appxmanifest` registers no `AppExecutionAlias` today, so
there is no live collision — but if the GUI is ever given a terminal alias, it must not reuse
`pakko` while this CLI also claims that name, or resolution order (not either binary's own code)
would decide which one actually runs from a bare `pakko` invocation.

**`tar.exe` is not bundled.** `Archiver.CLI` calls the OS-provided
`C:\Windows\System32\tar.exe` via the existing `TarSandboxedService`, exactly like every other
frontend — the CLI only runs on Windows to begin with (tar/RAR/7z support depends on
`Archiver.Core`'s Windows-only sandbox subsystem), and the OS's own tar.exe is always present
(Win10 1803+/Win11), so there is nothing to gain from shipping a redistributed copy. Doing so
would only add a new supply-chain surface — a second binary to hash-pin and license-audit, on top
of the vendored `7za.exe` T-F114 already introduced for tests — for zero functional benefit.
Rejected 2026-07-18; see `DECISIONS.md`'s T-F09 "Distribution" entry.

---

## Command table — 7z command → Pakko support

| 7z | Meaning | Pakko support |
|----|---------|----------------|
| `a` | Add (create/add to archive) | Supported — ZIP and all 6 tar-family creation formats (`-ttar`/`-ttar.gz`/`-ttar.bz2`/`-ttar.xz`/`-ttar.zst`/`-ttar.lzma`) via `IArchiveCreationRouter` (T-F105, shipped 2026-07-16, after this doc's original 2026-07-13 draft); `-t7z`/`-trar` remain unsupported — Pakko can only *create* ZIP/tar-family, never 7z/RAR |
| `u` | Update (add newer/changed files to an *existing* archive) | Not supported — no "diff against existing archive contents" logic exists anywhere in `Archiver.Core` |
| `d` | Delete (remove entries from an archive) | Not supported, deliberately — no in-place archive mutation, matches T-F05's "not an archive manager" positioning |
| `t` | Test (verify integrity) | Partial — ZIP via existing `TestAsync` (T-F62); tar-family has no test capability (`ITarService` has no Test method, per T-F86's finding) |
| `e` | Extract, flattened (no directory structure) | Not supported — Pakko's extraction always preserves the archive's folder structure; no flatten mode exists |
| `x` | Extract with full paths | Supported — matches `ExtractAsync`'s existing default behavior |
| `l` | List contents | Supported — consumes `IArchiveListingRouter` (T-F05, shipped), looped once per archive path given |
| `b` | Benchmark | Not supported, deliberately out of scope (same reasoning as T-F05's NanaZip-toolbar scope cuts) |
| `i` | Info (list supported archive formats/codecs) | Not implemented, but trivial — would report ZIP (always) + live `TarCapabilities` (detected formats) |
| `h` | Hash | **Supported (added 2026-07-20, T-F128/T-F09 follow-up).** Real 7z `h` hashes files on disk, not archive entries — the original row here predated T-F128 and described the wrong thing. Maps onto `FileHashService.ComputeAsync` (same engine the Explorer context-menu "Хеш-суми" submenu uses): one or more files hashed independently, or exactly one folder recursed with a combined DataSum/NamesSum printed (NanaZip-compatible, verified against the vendored `7za.exe`) |
| `rn` | Rename entries in an archive | Not supported, deliberately — in-place mutation, same reasoning as `d` |

## Switch fidelity — per-switch, not full coverage

| 7z switch | Meaning | Pakko mapping |
|-----------|---------|----------------|
| `-o{dir}` | Output directory | Maps directly to `ExtractOptions.Destination` |
| `-p{pwd}` | Password / encryption | Not supported — `System.IO.Compression` has no ZIP encryption support; a real capability gap, not a scope choice |
| `-r[-\|0]` | Recurse subdirectories | Archiving already recurses folders by default; the 7z on/off nuance needs its own check against current `ArchiveOptions` behavior |
| `-i{pattern}` / `-x{pattern}` | Include/exclude filename patterns | Not supported — no wildcard include/exclude filtering exists in `ArchiveOptions`/`ExtractOptions` today |
| `-y` | Assume yes, suppress prompts | Needs a CLI-side default for Pakko's interactive callbacks (conflict resolution, compression-bomb confirm, T-F94) since there's no UI to prompt |
| `-t{type}` | Archive type override | Only meaningful for `a` (creation) — extraction/list/test formats are always auto-detected via `ArchiveFormatDetector`, `-t` has no effect there. For `a`, 7 real spellings map 1:1 onto `ArchiveContainerFormat`: `-tzip` (default), `-ttar`, `-ttar.gz`, `-ttar.bz2`, `-ttar.xz`, `-ttar.zst`, `-ttar.lzma`. `-t7z`/`-trar` are recognized but rejected — extract-only formats, Pakko can't create them |
| `-v{size}` | Split into volumes | Not supported — no multi-part/split-archive logic exists anywhere in `Archiver.Core` |
| `-m{params}` (e.g. `-mx=9`) | Compression method/level | Partial — 7z's 0–9 scale doesn't map 1:1 onto `System.IO.Compression.CompressionLevel`'s four discrete values (`NoCompression`/`Fastest`/`Optimal`/`SmallestSize`); needs an explicit, documented bucketing, not a naive `/9*4` — resolved: `0`->`NoCompression`, `1-2`->`Fastest`, `3-6`->`Optimal` (7z's own default `-mx5` lands here), `7-9`->`SmallestSize` |
| `-ao{a\|s\|u\|t}` | Overwrite mode | Mostly supported — maps to `ExtractOptions.OnConflict` (Overwrite/Skip/Rename); 7z's 4th variant (`t`, rename existing instead of new) has no Pakko equivalent |
| `-scc`/`-ssc` | Console charset / case-sensitivity | `-scc` not applicable (.NET is Unicode-native); case-sensitive matching is an open question worth a decision, not an assumption |
| `-si` | Read the archive from stdin | Supported on `x`/`t`/`l` (T-F116, buffered — stages stdin to a temp file first, see below) and on `h` (T-F128/T-F09 follow-up, **genuinely zero-copy** — CRC-32/SHA-256 need no seeking, so `ComputeStreamDigestAsync` hashes stdin directly, no temp file at all; the only `-si` on any command that's a real single-pass stream) |
| `-so` | Write output to stdout | Supported on `x` (only when extraction resolves to exactly one file) and `a` (T-F116). Not applicable to `h` — its report already prints to stdout by default, there's no separate result file to stream |
| `-scrc{method}` | Hash method | Only meaningful for `h` (added T-F128/T-F09 follow-up). Two real spellings map onto `HashAlgorithmKind`: `-scrcCRC32` (default when `-scrc` is omitted, matching real 7z's own default) and `-scrcSHA256`, case-insensitive. Every other real 7z method (`CRC64`, `SHA1`, `SHA3-256`, `XXH64`, `BLAKE2SP`, etc.) is recognized but rejected — Pakko only implements the two `Archiver.Core.IO`/`System.Security.Cryptography` already provided elsewhere in the app |

---

## Stdin/stdout streaming (`-si`/`-so`, T-F116)

Buffered, not zero-copy, **for `x`/`t`/`l`/`a`**: `-si` stages the full stdin stream to a private
temp file before the operation starts; `-so` runs the operation to a private temp location, then
streams the single resulting file to stdout once it's complete. A failed operation never emits
partial output. `-so` on `x` requires the extraction to resolve to exactly one file (multiple
files → a named error, exit 2). `Archiver.Core`'s public API is unchanged — see `ARCHITECTURE.md`'s
T-F116 entry for why true zero-copy streaming was rejected for these commands: `ZipArchive` needs a
seekable file to read its central directory, and `TarSandboxedService`'s whole-archive pre-scan
(T-F49) needs a real file to scan before extraction runs — neither can operate on a raw pipe
mid-stream.

**`h -si` is the one genuine exception (T-F128/T-F09 follow-up).** CRC-32/SHA-256 are single-pass,
no-seek algorithms, so nothing forces staging to disk first — `FileHashService.
ComputeStreamDigestAsync` reads directly from `Console.OpenStandardInput()` and hashes as it goes,
with no intermediate temp file at all. Confirmed via a real subprocess test piping raw bytes to the
built `pakko.exe`'s stdin (`CliSubprocessTests.Hash_StdinCrc32_MatchesKnownValue`/
`Hash_StdinSha256_MatchesKnownValue`).

**Shell compatibility — verified empirically, not assumed.** A raw `<` input-redirection operator
does not exist in PowerShell (any version — both 5.1 and 7 reject it as a reserved token). Native
`|`/`>` piping between two executables is byte-perfect in **PowerShell 7+** (a true OS-pipe fast
path), but **silently corrupts binary data in Windows PowerShell 5.1** (it always mediates
native-to-native pipes as line-based text, never a raw byte pipe) — no error, just wrong bytes.
The one pattern confirmed byte-perfect on every PowerShell version, including 5.1, is wrapping in
`cmd /c "..."`:

```
cmd /c "pakko a -so out.zip file1 file2 | pakko x -si -o dest > log"
```

If you know your script only ever runs on PowerShell 7+, native `|`/`>` works directly. If it
might run under Windows PowerShell 5.1 (still the default `powershell.exe` on many systems), use
the `cmd /c "..."` form above — this is not a Pakko-specific limitation, it is a property of the
receiving shell.

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
