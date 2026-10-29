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

`Archiver.CLI` ships as a separate, standalone downloadable artifact that does not require Pakko's
GUI to be installed; since T-F317 (v1.7.0) the MSIX carries the same `pakko.exe` too (see the
table below). Published as one Native AOT `pakko.exe` per architecture (T-F355)
(`win-x64`/`win-arm64`, matching the solution's existing platform set) via GitHub Releases — the
same channel already used for v1.1+ — each build accompanied by a `SHA256SUMS` file so a script or
a user can verify the download before running it. This is a packaging/release-engineering concern
layered on top of the Architecture section above, not a change to it: `Archiver.CLI` itself stays
exactly what's described there — a third thin frontend consuming `Archiver.Core` in-process,
compiled Native AOT (T-F355): one native exe with the runtime and `Archiver.Core` inside, so it
runs on a machine with no .NET install, and the zip holds `pakko.exe` alone. The built exe
is named `pakko.exe` (`Archiver.CLI.csproj`'s `AssemblyName`, distinct from the project/folder
name) — short, matches the product name, and matches the `pakko:` prefix already used in every
stderr message and in `--help`'s own `USAGE:` line.

**Three ways to get the `pakko` command (T-F317, v1.7.0):**

| Install | How `pakko` gets on `PATH` | `pakko -v` prints |
|---|---|---|
| Pakko from the Microsoft Store or an MSIX | The package's execution alias `pakko.exe` in `%LOCALAPPDATA%\Microsoft\WindowsApps` (on every user's `PATH`) | `pakko X.Y.Z (package PavloRybchenko.Pakko_...)` |
| `winget install pakko-cli` (package `PavloRybchenko.PakkoCLI`, the CLI zip; available once the winget catalog accepts the package) | winget's symlink `pakko.exe` in `%LOCALAPPDATA%\Microsoft\WinGet\Links` (on the user `PATH`, T-F361) | `pakko X.Y.Z` |
| The zip from GitHub Releases | Not added — extract it and add the folder to `PATH` yourself, or call it by full path (like ripgrep/fd/bat zips) | `pakko X.Y.Z` |

- **Inside the MSIX** `pakko.exe` is its own hidden `<Application Id="Cli">` with a `uap3`
  execution alias, the same shape as NanaZip's console exe (`NanaZipC.exe`). It is the same
  program as the zip, built from the same project — one native exe (T-F355) — and runs with the
  package identity — tar.exe still goes through the
  same AppContainer sandbox.
- **winget runs `pakko` through its `WinGet\Links` symlink (T-F361):** the AOT exe (T-F355) is one
  file and runs through the link. Before 1.9.0 the manifest put the install folder on `PATH`
  instead (the pre-AOT apphost looked for `pakko.dll` next to the link and failed), and `winget
  uninstall` left that `PATH` entry behind; a 1.8.x install upgraded to 1.9.0 may still carry it,
  pointing nowhere, harmless.
- **Both installed:** a bare `pakko` runs whichever folder comes first in `PATH`. `WindowsApps` is
  usually ahead of user entries, so the Store copy wins; `pakko -v` shows which one ran.
- **Known difference of the packaged copy (T-F320):** MSIX write virtualization. Creating a *new
  folder directly under* `%LOCALAPPDATA%` or `%APPDATA%` (e.g. `-o%LOCALAPPDATA%\NewFolder`) lands in
  the package's private `LocalCache` instead, invisible to other programs. Existing folders under
  them, their new subfolders, `%TEMP%` and every other location are written normally. Use the zip
  or winget copy for that one shape. Kept as a known difference (decided again for the v1.9.0
  Store submission, 2026-10-24: the fix needs the restricted capability `unvirtualizedResources`,
  which can hold up certification; `docs/DECISIONS.md`, "T-F320").

**Naming note:** `pakko` is the terminal program everywhere — the same convention as 7-Zip (`7z`
console, `7zFM`/`7zG` GUI) and NanaZip (`NanaZipC` console). The GUI has no execution alias; it is
started from the Start menu, Explorer or a file association. If it ever gets one, it needs its own
name (e.g. `pakkofm`), never `pakko`.

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
| `a` | Add (create/add to archive) | Supported — ZIP and all 6 tar-family creation formats (`-ttar`/`-ttar.gz`/`-ttar.bz2`/`-ttar.xz`/`-ttar.zst`/`-ttar.lzma`) via `IArchiveCreationRouter` (T-F105, shipped 2026-07-16, after this doc's original 2026-07-13 draft); `-t7z` since T-F342 (2026-10-12): a real 7z written by Windows' own tar.exe where it has a 7z writer (`pakko i` says so), never encrypted; `-trar` stays unsupported — RAR is extract-only |
| `u` | Update (add newer/changed files to an *existing* archive) | Not supported — no "diff against existing archive contents" logic exists anywhere in `Archiver.Core` |
| `d` | Delete (remove entries from an archive) | Not supported, deliberately — no in-place archive mutation, matches T-F05's "not an archive manager" positioning |
| `t` | Test (verify integrity) | Supported — ZIP via `IArchiveService.TestAsync` (T-F62), every format tar.exe reads via `ITarService.TestAsync` (T-F397: tar.exe reads each entry in the sandbox and writes nothing). `t` goes through `IExtractionRouter.TestAsync` (T-F261): each path is classified once (Group Policy, then format) and a refused path is skipped with the router's reason. tar.exe compares the checksums of 7z, tar.xz, tar.bz2 and tar.zst (an xz or zstd stream written without a check counts with the next group); for a plain tar, tar.gz and RAR it verifies no contents (and for a tar.lzma, which is recognized by its name since it has no magic number, T-F398), so a passed test there is exit 1 with the warning "Only the archive's structure was checked" and no "no errors" line. An archive extraction would refuse (an escaping name, a link entry, an encrypted 7z or RAR) fails the test with the same message. **Recovery data (T-F275, v1.8):** each archive is also checked against a PAR2 set next to it (`<archive>.par2` and `<archive>.*.par2`, or `<name without the last extension>.par2` as QuickPar/MultiPar name it), and a `.par2` path checks the archive its set protects; see "Recovery data in `t`" below |
| `e` | Extract, flattened (no directory structure) | Not supported — Pakko's extraction always preserves the archive's folder structure; no flatten mode exists |
| `x` | Extract with full paths | Supported — `ExtractMode.SingleFolder`; since T-F205 an archive's single root folder is kept, as `7z x` does. Without `-o`, extracts into the **current directory**, as `7z x` does (T-F206; before it, next to the archive). An entry name Windows refuses is written as 7-Zip and tar.exe write it: `* ? " < > |` as `_`, a part left empty as `_` (`What?.txt` is `What_.txt`); `:` and control characters are refused (T-F38/T-F39). A part's trailing dots and spaces are dropped, as Windows writes the name (7-Zip writes them as `_`: `a.txt.` is its `a.txt_`). Two different entries that land on one name that way (`a?b.txt` and `a_b.txt`, `a.txt.` and `a.txt`), in one archive or across the archives of one `x`, are both kept, the later numbered, `-y` or not (T-F419/T-F420); an entry whose name lost a trailing dot or space, or an 8.3 alias of a file (`LONGFI~1.TXT`), never replaces a file already there either - it is numbered beside it. A tar archive with an entry named as another entry's 8.3 alias is refused whole: tar.exe would write one over the other before Pakko sees either |
| `l` | List contents | Supported — consumes `IArchiveListingRouter` (T-F05, shipped), looped once per archive path given. Tab-separated columns `Size`, `Compressed`, `Crc32`, `Modified`, `Type`, `Encrypted`, `Path` (Path always last). `Compressed` and `Crc32` are `-` for tar-family, 7z and RAR (no per-entry value; T-F214, was `0` for `Compressed`); `Modified` is local time, for those formats read from tar.exe's listing (to the minute within half a year; an older entry shows the date alone, `2020-02-03`, because tar.exe prints its year in place of the time and has no option for both, T-F335), `-` when unreadable. `Encrypted` (T-F221 item 7, 2026-09-28) is `-`, `ZipCrypto`, `AES-128`/`AES-192`/`AES-256`, `+` (encrypted by a method Pakko cannot name) or `?` (the format cannot say without extracting: tar-family, 7z, RAR). With `-slt` (T-F412) it prints 7-Zip's technical listing instead: see the switch table. Names are printed as 7-Zip prints them (T-F418): redirected, a line break in a name becomes `_`; on a console every control character does. A tar name is shown as stored: `a\b.txt`, where `tar.exe -t` prints `a\\b.txt` and 7-Zip shows `a_b.txt` |
| `b` | Benchmark | Not supported, deliberately out of scope (same reasoning as T-F05's NanaZip-toolbar scope cuts) |
| `i` | Info (list supported archive formats/codecs) | Supported (the 7z line reads `create, extract, list` where this tar.exe has a 7z writer, found by running it once, and `extract, list` elsewhere; T-F342) — prints ZIP and tar/tar.gz (always) plus each tar.exe-backed format (tar.bz2/xz/zst/lzma, 7z, rar) with its live `TarCapabilities` result, and the tar.exe version. Since T-F261 each format's status comes from Core's `ArchiveFormatPolicy` and the loaded Group Policy: "supported", "not supported" or "blocked by Group Policy"; under `DisableTarExtraction` the tar.exe line reads "disabled by Group Policy" and no version probe runs. Since T-F403 the status of a tar format covers making it as well as reading it (one codec does both, decided by `ArchiveFormatPolicy` from the libraries in `tar --version`), and where 7z can be made a `methods for 7z:` line lists the methods this tar.exe can write. Takes no arguments (only `-scc`) |
| `h` | Hash | **Supported (added 2026-07-20, T-F128/T-F09 follow-up).** Real 7z `h` hashes files on disk, not archive entries — the original row here predated T-F128 and described the wrong thing. Maps onto `FileHashService.ComputeAsync` (same engine as the Explorer context menu's CRC-32/SHA-256 commands, flattened out of the old "Хеш-суми" submenu by T-F128): one or more files hashed independently, or exactly one folder recursed with a combined DataSum/NamesSum printed (NanaZip-compatible, verified against the vendored `7za.exe`) |
| `r` | (not a 7z command) Repair from PAR2 recovery data | **Pakko's own (T-F275 step 4):** writes `<name>.repaired<extension>` from the PAR2 set next to the archive; see "Repair: `r`" below |
| `rn` | Rename entries in an archive | Not supported, deliberately — in-place mutation, same reasoning as `d` |
| — | Scan for threats (Pakko's own App/Explorer command, T-F146) | Not supported, by decision (T-F241, 2026-09-25): 7z has no such command to mirror, and Windows' `MpCmdRun -Scan` cannot see inside password-protected ZIPs (T-F194), so a CLI scan would be weaker than the App's in-memory decrypt-and-scan. Scripts can `pakko x` and run their antivirus on the extracted files (e.g. `MpCmdRun -Scan -ScanType 3 -File <dir>`). The App, the Explorer menu and `Archiver.Shell` keep the command. Revisit on request |

## Version reporting — deliberately not a 7z pattern

Real 7z has no `version` subcommand — it isn't one of the 11 characters in `g_Commands`. Instead
every 7z invocation prints a startup banner (`7-Zip 19.00 (x64) : Copyright ...`) built from a
version constant compiled into the binary. Pakko's CLI diverges here on purpose: a banner on every
command's output would add noise to scripted/piped usage (`-so`/`h` output especially), so instead
`pakko -v`/`pakko --version` (added 2026-07-26) is a dedicated flag, printing just `pakko X.Y.Z`
and exiting 0 — closer to `git --version`/`rg --version` convention than 7z's own. The version
comes from `Archiver.CLI.csproj`'s `<Version>` MSBuild property, which `scripts/Publish-Cli.ps1`
overrides via `/p:Version` at release-build time (CI passes the pushed git tag, stripped of its
leading `v`) so a released `pakko.exe` always reports the exact tag it shipped under.
**Every other build** (a non-tag CI build, a local build) keeps the checked-in default
`0.0.0-dev` and prints it with the 7-character commit the SDK appends to the informational
version, e.g. `pakko 0.0.0-dev+0e379cc` (T-F222 — the old checked-in default `1.4.2` made such
builds indistinguishable from the real v1.4.2). The default is deliberately never bumped.

## Switch fidelity — per-switch, not full coverage

**How switches are read (T-F411, as 7-Zip's `CommandLineParser.cpp` at 26.02, measured on 7za):**
a switch name is the longest one in 7-Zip's table that starts the argument, compared without case
for ASCII letters only (`-Y`, `-OC:\out`, `-MX=9`, `-XR!*.tmp`; a non-ASCII letter matches no
switch). The command is read without case too (`X`, `L`) and is the first argument that is not a
switch, so switches may come before it (`pakko -y x a.zip`, `pakko -- x a.zip`). What follows the
name keeps its case where 7-Zip's does: the postfix letter of `-ao`, `-sa`, `-bso`/`-bse`/`-bsp`,
`-r` and `-ssc-` (`-AOA` and `-SAE` are exit 7, as in 7-Zip), and every password, path and mask;
type names, charsets and `-m` property names do not (`-tZIP`, `-sccutf-8`, `-mEM=aes256`).
**A switch given twice is exit 7** (`Multiple instances for switch: -o<dir>`, a password never
printed), except the ones 7-Zip takes more than once: `-i`, `-x`, `-ai`, `-ax`, `-m` (the last
value of one property wins) and `-stx`. Pakko's own `-rr` is taken once too. A change in behavior
from 1.8.1, where the last of a repeated switch won. `-?`, `-h` and `--help` show the help wherever
they stand before `--`, in any case (`pakko x a.zip -H`), as in 7-Zip; `-v`/`--version` only as the
first argument. A word that lands as the command right after a bare `-p` (`pakko -p secret x a.zip`)
is refused without being printed, where 7-Zip prints it: it may be a password typed apart.

| 7z switch | Meaning | Pakko mapping |
|-----------|---------|----------------|
| `-o{dir}` | Output directory | Maps directly to `ExtractOptions.DestinationFolder`; omitted, it is the current directory (T-F206, 7z behavior), including with `-si`. On `x` it is read as Windows reads a path: `-o"out "` is `out` (7-Zip writes into a folder named `out `, which Explorer cannot open; T-F419); a `\\?\` path stays as typed. On `r` it is where the repaired copy goes; omitted there, the copy goes next to the archive |
| `-p{pwd}` | Password / encryption | Supported on `x`/`t` (T-F191, ZIP only — ZipCrypto and WinZip AE-1/AE-2) and on `a` (T-F193: encrypts every file entry with WinZip AES-256 AE-2; folder entries and all file names stay unencrypted). On `l` (T-F409) `-p`, `-p-` and a bare `-p` are accepted and change nothing: a listing needs no password. **Bare `-p`** (T-F193, 7z semantics) asks on the console — once on `x`/`t`, twice (enter + re-enter) on `a`; before T-F193 a bare `-p` was a command-line error. A bare `-p` with a redirected stdin or with `-si` exits **7** (nothing to type into). **On `a` only:** the password must be printable ASCII (0x20–0x7F) and at most 99 characters — 7-Zip's own creation rule (it decodes ZIP passwords through the ANSI code page and refuses longer AES passwords), so any other password would give an archive 7-Zip cannot open; `x`/`t` accept any password. A `-p<pwd>` that breaks the rule exits **7** (command-line error, nothing created); at the interactive prompt a mismatch or a refused password exits **2** with its reason and is never re-asked (7z behavior), and Esc/Ctrl+C exits **255**. `-p` with any `-t tar*` format exits **7** (tar has no encryption). Without `-p` on an encrypted archive (`x`/`t`): a real interactive console (not redirected/piped) and no `-y` prompts with a masked `Console.ReadKey`-based input, retried on a wrong password up to 3 times; a redirected/piped stdin (including `-si`, which already consumes stdin for the archive itself) or `-y` fails immediately with the same message as an unresolved password always has — deliberate: `-y` means "pick the safe default" for conflicts/compression-bomb warnings, where a safe default exists; there is no safe default for a missing password, so `-y` cannot make the operation proceed, only fail predictably instead of hanging on a prompt that can't be answered non-interactively. **`-p<pwd>` is visible in the process's own command line** (`Get-Process`/Process Explorer, or any other process on the machine enumerating command lines) for as long as `pakko.exe` runs — same exposure as any CLI tool's `-p`/`--password`-shaped flag; prefer the interactive prompt over `-p` when that matters. See `ARCHITECTURE.md`'s "`-p{pwd}` password support (T-F191)" section and `DECISIONS.md`'s T-F191/T-F193 entries. **T-F417:** a ZIP `x`/`t` with names or `-i`/`-x` asks for (and checks) the password only when an encrypted entry passes, as 7-Zip does: `t e.zip x` on a plain `x` beside an encrypted `cost` exits 0 without a password. |
| `-r[-\|0]` | Recurse subdirectories | **T-F410**, `x`, `l` and `t`: how a name filter matches. `-r` matches a mask at any depth (`-r *.txt` takes `dir\c.txt`), `-r-` (the default) only from the top (`*.txt` takes only top-level files; a folder name takes everything under it either way), `-r0` recurses only a name with a wildcard. A `-i`/`-x` switch that says `r`, `r-` or `r0` itself overrides it. On `a` (T-F410 wave C) a source mask recurses the same way (`a o.zip *.txt -r` takes `dir\c.txt` as `dir/c.txt`, `a o.zip sub -r` every folder named `sub`); a folder source is archived with all it holds either way. On `h`, `r`: exit 7 |
| `-rr[N]` | Recovery data (WinRAR's spelling — 7z has no such switch) | T-F275, `a` only (exit 7 on other commands, above `-r` in the refusal table). PAR2 files next to the archive: `<archive>.par2` and `<archive>.vol0+R.par2`, R = N percent of its slices rounded up; bare `-rr` is 5, `-rr<N>` takes 1-100, anything else (`-rr0`, `-rr101`, `-rrx`, `-rr5%`) exits 7. Given twice: exit 7 (T-F411). With `-so` exits 7 (no file to put a set next to); with `-p` the set protects the ciphertext. On success nothing more is printed; a failed set is `pakko: error: <archive>: Recovery data was not created: ...` with exit 2 and the archive kept. Group Policy `DisableRecoveryData` refuses it with exit 2 before anything is written. With or without `-rr`, `a` over an archive that had a set removes that set's files once the archive is written and before a new set is (the set no longer matches its length or first 16 KiB; a stuck file is a `pakko: warning:`, exit 1), so an `a -rr` interrupted while writing the set leaves an archive with no set; under the policy an old set is left alone |
| `-i{pattern}` / `-x{pattern}` | Include/exclude filename patterns | **T-F410**, `x`, `l` and `t`. `-i` and `-x` take `r`, `r-`, `r0` (recursion), `w`/`w-` (wildcards on/off), then `!<name>` or `@<list file>`: Scoop's `-xr!*.nsis`, `-ir!dir\*`. `-xtd`/`-xtf` leave out every folder/file entry. The rules are 7-Zip's `Wildcard.cpp`, the node tree included: a mask's literal leading folders anchor it, so `-xr!dir\*.nsis` removes `dir\sub\d.nsis` but not `Other\dir\x.nsis`; a folder name takes everything under it; an exclude wins; matching ignores case unless `-ssc`. 7-Zip's `m` (strict file names) and `#` (shared memory) are refused. On `a` (wave C): `-i` names more sources and `-x` leaves entries out by their names in the archive: `a o.zip dir -xr!*.log`, `-x!dir\sub` (a folder with all it holds); `-x!c.txt` does not reach `dir\c.txt`, as in 7-Zip; an absolute `-x` name, or one with a `.` or `..` part, leaves nothing out (7-Zip's `ExtendExclude` carries only an exclude without a prefix); `-xtd`/`-xtf` exit 7 there. On `h`, `r`: exit 7. Core gets a plain per-entry predicate (`ExtractOptions.EntryFilter`, `TestAsync`'s `entryFilter`, `ArchiveOptions.EntryFilter`) |
| Names after the archive, `@listfile` | Name filters | **T-F410, a change in behavior:** `x a.zip b.zip` used to extract two archives; as in 7-Zip, `b.zip` is now a name filter inside `a.zip` (Chocolatey passes a folder there). Several archives are `x *.zip` or `-ai`. `@<file>` before `--` reads names from a list file: UTF-8 unless `-scs` says `UTF-16LE`, `UTF-16BE`, `WIN` or `DOS`, byte order marks skipped, each line trimmed and stripped of one pair of quotes, empty lines dropped; a missing or undecodable list file is exit 7. A filter that lets nothing through prints `No files to process` and exits 0, creating no `-o` folder (7-Zip's `DecompressArchive`). An absolute path or `..` in a filter matches nothing, as in 7-Zip. `-so` works when the filter leaves one file. With `-bb1` a folder Pakko creates for a file inside it is named too, even when the folder's own entry did not pass (7-Zip names only entries). On `t` (wave B) the same filters pick the entries tested: a damaged entry the filter leaves out is not reported, as in 7-Zip, and nothing passing is `No files to process`, exit 0, with no structure-only warning on a tar. Unlike 7-Zip, `tar.exe` does not report a damaged file of a solid 7z that the filter leaves out (measured). A tar whose listing shows a name with `?` (T-F416: `tar.exe` 3.5.2 cannot show a name outside the ANSI code page) refuses a filter on `x` and `t`, exit 2; without a filter it extracts and tests as before |
| `-ai`, `-ax`, `-an` | Archive names | **T-F410**, `x`, `l` and `t`. The archive name may hold a wildcard (`x arcs\*.zip`, never recursive); `-ai`/`-ax` add or remove archives with the same syntax as `-i`/`-x` (a searched name is read without `\\?\`, so `-ax!\\?\C:\d\b.zip` leaves `b.zip` out where 7-Zip does not, T-F419) (`-air!*.zip` walks the folder tree, without following links); `-an` means no archive name on the line, so every name is a filter and `-ai` must name the archives. Archives found by a wildcard are opened in name order; a wildcard that finds none is `Cannot find archive`, exit 7; a name without a wildcard is opened as typed (missing: exit 2). With `-si`: exit 7 |
| `-spd`, `-ssc[-]` | Wildcards off; case-sensitive names | **T-F410**, `x`, `l` and `t`'s archive names. `-spd` reads `*` and `?` as plain characters; `-ssc` compares names case-sensitively (`-ssc-`, the default on Windows, does not) |
| `-stx{Type}` | Exclude archive type | **T-F410 wave B**, `x`, `l` and `t`, with `-si` too. An archive whose own format, by its bytes, is excluded is `Cannot open the file as archive`; the other archives go on and the run ends with exit 2, as in 7-Zip. Only the outer format counts: `-stxtar` leaves a `.tar.gz` alone, `-stxgzip` refuses it. Types, in any case: `zip`, `7z`, `tar`, `gzip`, `bzip2`, `xz`, `zstd`, `lzma`, `rar`, `rar5`; another name is `Unsupported archive type`, exit 2 (7-Zip knows more types, which Pakko does not read). `-stx` with no type: exit 7. On other commands: exit 7 |
| `-y` | Assume yes, suppress prompts | Needs a CLI-side default for Pakko's interactive callbacks (conflict resolution, compression-bomb confirm, T-F94) since there's no UI to prompt. **T-F160:** without `-y`/`-ao`, `x` on a real interactive console (stdin not redirected, not `-si`) now asks per conflict exactly like 7-Zip's console — `(Y)es / (N)o / (A)lways / (S)kip all / A(u)to rename all / (Q)uit?`, re-asked until one valid letter, end of input = quit (confirmed against NanaZip's vendored `UI/Console/UserInputUtils.cpp`). Deviation: the prompt goes to **stderr**, not 7z's stdout, since `-so` streams data on stdout. "Always"/"Skip all"/"Auto rename all" span the whole command, including a mixed zip+tar selection. Redirected/scripted runs keep the silent Skip. `Q` or Ctrl+C stops cleanly with exit code **255** (7-Zip's "user stopped"). **T-F216:** entries the user skipped at the prompt (one by one or "Skip all") are not reported as skips, so a run where the user skipped everything exits **0**, as in 7-Zip (was 1); the configured Skip (`-aos`, redirected input) still reports them **T-F409:** also taken on `t` (no password prompt, as on `x`) and on `l` (nothing to answer) |
| `-t{type}` | Archive type override | Only meaningful for `a` (creation) — extraction/list/test formats are always auto-detected via `ArchiveFormatDetector`, `-t` has no effect there. For `a`, 7 real spellings map 1:1 onto `ArchiveContainerFormat`: `-tzip` (default), `-ttar`, `-ttar.gz`, `-ttar.bz2`, `-ttar.xz`, `-ttar.zst`, `-ttar.lzma`, plus the dot-free `-ttgz`/`-ttbz2`/`-ttxz`/`-ttzst` that PowerShell cannot split (T-F294). A name ending in one of these types (or in `.tgz`/`.tbz2`/`.txz`/`.tzst`) must match the type, `-tzip` included when no `-t` is given (`a out.tar.gz src` is exit 7, not a ZIP named `out.tar.gz`); any other extension is written as typed. `-t7z` (T-F342) creates a 7z archive; a name ending in `.7z` needs it (`a out.7z src` is exit 7), and `-p` with it is exit 7 (Pakko cannot encrypt 7z). `-trar` is recognized but rejected — RAR is extract-only. **T-F413:** `-tgzip`, `-tbzip2`, `-txz` (7-Zip's names, any case; `-tgz` and `-tbz2` are unknown, as in 7-Zip) compress **one file** without tar, through `tar.exe`'s raw format: `a -tgzip notes.txt.gz notes.txt`, patool's `a -y -txz -- <file>.xz <file>`, and 7-Zip's two-step `a -tgzip x.tar.gz x.tar`. A name without an extension gets the type's (`made` -> `made.xz`). The name may end in the type's extension, the tar type of the same codec (`.tar.gz`/`.tgz`, `.tar.bz2`/`.tbz2`, `.tar.xz`/`.txz`) or no archive type; another type's or stream's extension is exit 7 (`-tgzip out.xz`). A bare `.gz`/`.bz2`/`.xz` name under another `-t`, or with no `-t`, is still written as typed (`a out.gz src` is a ZIP named `out.gz`, where 7-Zip picks gzip). Two files or a folder: exit 2, `gzip compresses a single file: give exactly one file, not a folder or several files.`, nothing written (7-Zip: exit 2). `-p`: exit 7. Unlike 7-Zip, the gzip header holds no file name and no time (Pakko asks tar.exe for MTIME 0: its raw writer would store the time of compression), so an extracted file gets the time of extraction. Blocked by `DisableTarExtraction` and by blocking `gzip`/`bz2`/`xz`, as the tar formats of that codec; bzip2 and xz need a `tar.exe` with those codecs (T-F403). Not offered by the App or Explorer |
| `-v{size}` | Split into volumes | Not supported — no multi-part/split-archive logic exists anywhere in `Archiver.Core` |
| `-mem={method}` | ZIP encryption method | T-F193, `a` only: `-mem=AES256` (case-insensitive) is accepted as a no-op — AES-256 is the only method Pakko writes. `-mem=ZipCrypto`/`AES128`/`AES192` are refused (exit 7) rather than silently upgraded; any other value is an unknown-value error. Every other `-m{param}` except `-mx` is refused on `a` as not implemented |
| `-m0={method}` | Method of a 7z archive | T-F342, `a -t7z` only: `lzma2` (default), `lzma`, `bzip2`, `deflate`, `copy`, in any case; the last `-m0` wins (`-m` is the one switch 7-Zip takes many times). Refused with exit 7: `ppmd` and `zstd` (tar.exe's 7z writer has neither), a method with parameters (`-m0=lzma2:d=64m`), a second coder (`-m1=`), and `-m0` without `-t7z`. `-mx=0` stores whatever `-m0` names |
| `-m{params}` (e.g. `-mx=9`) | Compression method/level | Partial — 7z's 0–9 scale doesn't map 1:1 onto `System.IO.Compression.CompressionLevel`'s four discrete values (`NoCompression`/`Fastest`/`Optimal`/`SmallestSize`); needs an explicit, documented bucketing, not a naive `/9*4` — resolved: `0`->`NoCompression`, `1-2`->`Fastest`, `3-6`->`Optimal` (7z's own default `-mx5` lands here), `7-9`->`SmallestSize`. T-F411: `-mx` and `-mx=` alone are 9, `-mxN` is `-mx=N`, and a level above 9 is 9 (7za takes `-mx10` and `-mx=99`); a value that is not a number is exit 7 (7za: exit 2). The same four steps for `-t7z`: Copy, then tar.exe's levels 1, 6 and 9 |
| `-ao{a\|s\|u\|t}` | Overwrite mode | Maps to `ExtractOptions.OnConflict`: `a` Overwrite, `s` Skip, `u` Rename, `t` RenameExisting (T-F411, below) |
| `-scc{charset}` | Console charset | **Supported on every command (T-F238).** `-sccUTF-8`, `-sccWIN` (system ANSI code page) or `-sccDOS` (system OEM code page), case-insensitive, given once (twice: exit 7, as in 7-Zip; T-F411) — the three names 7-Zip's own help lists (confirmed in NanaZip's vendored `ArchiveCommandLine.cpp`/`Main.cpp`); 7-Zip's undocumented numeric form (`-scc1251`) is refused with a named error. Sets the encoding of everything pakko prints, stdout **and** stderr, with no BOM. Without it, output uses the console code page, so a name the page cannot hold is written as `?` (7-Zip writes it lossily too): under `chcp 866`, `pakko l x.zip > list.txt` turns `Звіт.txt` into `Зв?т.txt`. Use `-sccUTF-8` whenever a script needs exact names. The switch is meant for redirected output: on
an interactive console whose code page is not UTF-8, the console decodes pakko's UTF-8 bytes in
its own code page, so non-ASCII text (including a conflict prompt on stderr) shows as mojibake —
expected, and the same with 7-Zip. The console's own code page is never changed (pakko does not call `SetConsoleOutputCP`). Input is unaffected: prompts read keys as Unicode |
| `-snz[0\|1\|2]` | Propagate Zone.Identifier (the download mark) | T-F360, `x` only (exit 7 on other commands). `-snz`/`-snz1` = every file (Pakko's default anyway), `-snz0` = no mark — 7-Zip's numbering (NanaZip's vendored `ArchiveCommandLine.cpp`). `-snz2` (7-Zip: Office files only) is recognized but refused (exit 7) — Pakko has no such mode, and its own "unsafe types" list (Group Policy `EnforceMOTW=2`) means something else. Given twice: exit 7 (T-F411). A set `EnforceMOTW` Group Policy overrides it; pakko then prints `pakko: warning: -snz asked for ..., but Group Policy (EnforceMOTW) decides: ...` on stderr before extracting, and the exit code is unaffected |
| `-si` | Read the archive from stdin | Supported on `x`/`t`/`l` (T-F116, buffered — stages stdin to a temp file first, see below) and on `h` (T-F128/T-F09 follow-up, **genuinely zero-copy** — CRC-32/SHA-256 need no seeking, so `ComputeStreamDigestAsync` hashes stdin directly, no temp file at all; the only `-si` on any command that's a real single-pass stream) |
| `-so` | Write output to stdout | Supported on `x` (only when extraction resolves to exactly one file) and `a` (T-F116). Not applicable to `h` — its report already prints to stdout by default, there's no separate result file to stream |
| `-scrc{method}` | Hash method | Only meaningful for `h` (added T-F128/T-F09 follow-up). Two real spellings map onto `HashAlgorithmKind`: `-scrcCRC32` (default when `-scrc` is omitted, matching real 7z's own default) and `-scrcSHA256`, case-insensitive. 7-Zip computes every `-scrc` given; Pakko computes one, so a second `-scrc` is exit 7 rather than dropped (T-F411). Every other real 7z method (`CRC64`, `SHA1`, `SHA3-256`, `XXH64`, `BLAKE2SP`, etc.) is recognized but rejected — Pakko only implements the two `Archiver.Core.IO`/`System.Security.Cryptography` already provided elsewhere in the app |
| `--` | End of switches | **T-F407.** Every argument after the first `--` is a name, even one that starts with `-` or a second `--`, as in 7-Zip's `CommandLineParser.cpp`. `-scc` and the output switches count only before it. With no command before it, the command is the first name after it (`pakko -- x a.zip`, T-F411) |
| `-sa{s\|e\|a}` | How `a` names the archive | **T-F411 (part).** `-sas` is the default rule (a name with no extension gets the type's); `-sae` writes the name exactly as typed, extension or not (patool passes it); `-saa` always adds the type's (`a -ttar -saa n.tar` writes `n.tar.tar`, and the name-against-type check is off, so `a -t7z -saa x.zip` writes `x.zip.7z`, as in 7-Zip). Unlike 7-Zip, the default type stays ZIP, so `a -saa named.zip` writes `named.zip.zip` where 7-Zip writes `named.zip.7z` |
| `-bb[N]` | Log level | **T-F408**, every command. `-bb` is 1; any number is taken, a non-number is exit 7. From 1, `x` prints `- <name>` on stdout for each folder (with a trailing `\`) and file that arrived in the destination, relative to `-o`: the lines Chocolatey records as installed files. Unlike 7-Zip: the lines come once the archive's entries are in place, folders first, not in archive order; a file renamed by `-aou` is named as written; no `.` lines for skipped items at any level; `a`, `t`, `l`, `h`, `r` print no per-file lines |
| `-bd` | No percentage | **T-F408**, every command |
| `-bt` | Time statistics | **T-F408**, every command: after it, 7-Zip's `Kernel`, `User`, `Process` and `Global` time lines with the peak memory; no cycle-count lines (.NET has no `QueryProcessCycleTime`) |
| `-ba` | No headers | **T-F408**: `l` prints the entry rows only, without the column header and the `#` lines; no other command prints a header |
| `-slt` | Technical listing | **T-F412**, `l` only (exit 7 elsewhere; `-slt` with a suffix is exit 7). 7-Zip's blocks (List.cpp, 26.02): `Listing archive: <arc>`, then `--`, `Path = <arc>`, `Type = <7-Zip's name for the outer format>`, `Physical Size = <bytes>`, a blank line and `----------`; then per entry `Name = value` lines and a blank line. Fields, in 7-Zip's order, only those Pakko knows: ZIP `Path`, `Folder`, `Size`, `Packed Size`, `Modified`, `Encrypted`, `CRC`; 7z `Path`, `Size`, `Modified`; tar-family and RAR `Path`, `Folder`, `Size`, `Modified`. A value Pakko lacks is empty (`CRC = ` for a folder). `Modified` is local time to the second for ZIP, to the minute (or the day alone) for what `tar.exe` lists; 7-Zip prints 100 ns for ZIP and seconds for tar. Paths use `\`. A `.tar.gz` is `Type = gzip` followed by the tar's files, where 7-Zip lists one item, the inner `.tar`. With `-ba`, only the entry blocks |
| `-aot` | Rename the file already there | **T-F411 wave E**, `x`. The existing file is renamed and the extracted one takes its name, as in 7-Zip; the new name is numbered as `-aou` numbers it (`a (1).txt`, where 7-Zip writes `a_1.txt`) and is never a name the archive is still to write, nor one a folder already has. The rename happens when the extracted files are moved in, one file at a time: a run stopped before that changes nothing, and a file that cannot be moved in leaves the existing one under its own name (`Cannot write ...: destination file is locked`, exit 2). Two entries of one archive with one name: the later one is renamed itself, as with `-aou` (7-Zip renames the first). A folder where the file goes is renamed the same way, its content kept (7-Zip's rule too; T-F419); under the other switches a folder in the way fails that file with `a folder of that name is in the way`, exit 2, and `-aoa`/`-y` replace a read-only file, as 7-Zip does |
| `-spe[-]` | Drop the root folder named like `-o` | **T-F411 wave E**, `x`. When every entry of the archive lies under one folder named like the last part of `-o` as typed (without case), that folder is dropped: `x src.zip -oout\src -spe` puts `src`'s content straight into `out\src` (7-Zip's `ElimDup`, `Extract.cpp`). Decided over every entry before a name filter, so `-x`/`-i` drop it too, as in 7-Zip; a file of that name, or any entry beside the folder, keeps it; `-o.` names `.`, so nothing is dropped; without `-o` nothing is. `-spe-` turns it off. Explorer's "Extract to <name>\" compares with the archive's name instead (T-F205), unchanged |
| `-mmt[N\|=on\|=off]` | Threads | **T-F411 wave E**, `a`. The most files the ZIP writer compresses at once, up to 16: `-mmt`, `-mmt=`, `-mmt=on` and `-mmt0` follow the core count, `-mmt=off` is one, `-mmtN`/`-mmt=N` is N; the archive is the same bytes either way. `-mmtON` without `=`, a sign or a number past UInt32 is exit 7 (7za: 2). `tar.exe` writes the other formats and is not reached. On other commands: exit 7, as every `-m` |
| `-sse` | Stop at a file that cannot be read | **T-F411 wave E**, `a`. A source file the ZIP engine cannot open fails the archive: none is written, an existing one it would replace stays byte for byte, the file is named and exit is 2 with `no archive written: -sse stops at a file that cannot be read` (7za: exit 2, no archive). A missing source is a warning before anything is read and not covered, as in 7-Zip. The tar-family formats and 7z write no archive when `tar.exe` cannot read a file, `-sse` or not (measured) |
| `-ssw` | Read files open for writing | **T-F411 wave E**, `a`. The ZIP engine also opens a file another program holds open for writing (7-Zip's `OpenShared`); without it such a file is an error, as in 7-Zip (7-Zip: a warning, exit 1; Pakko: exit 2). Each entry's size and CRC come from the bytes read; a file stored as it is is read twice and fails the archive if it changed in between. `tar.exe` reads such files without being asked, so the other formats take them with or without `-ssw` |
| `-stl` | Archive time from the newest entry | **T-F411 wave E**, `a`. The archive takes the newest modification time among the files and folders it holds, read from disk (7za counts folders too, measured); what a filter leaves out and what is behind a link do not count |
| `-sdel` | Delete the sources once archived | **T-F411 wave E**, `a`. As 7-Zip's `UpdateArchive` does it (`Update.cpp`): the files listed before archiving are deleted permanently (no Recycle Bin), read-only ones too, when their size, modification and creation time are still the same; then the folders, deepest first, each only once empty. A file changed meanwhile stays (`pakko: warning: -sdel kept <path>: it changed while it was being archived`, exit 1); one added meanwhile is not on the list, so it stays with its folder, unreported, as in 7-Zip. Only a source Core reports archived in full is touched, and Core judges one archive as a whole: with a skip, an error or a `-x` filter anywhere, no source is deleted (7-Zip deletes the files it stored), and when the archive was not written in full nothing at all. Unlike 7-Zip a link is never entered or deleted (7-Zip follows a junction and deletes the files behind it), and neither is a source whose path passes a link (`j\t.txt`, `j\sub` with `j` a junction; T-F419): it is reported as a link. Refused with exit 7 before anything is written: a volume root, or a source that is or holds the current folder (`a -sdel x.zip ..`), compared where the paths really lead (`\\?\`, `\\?\Volume{...}\`, `\\.\` and `\\localhost\C$\` spellings and junctions resolved, an existing source also by file identity, T-F419; one whose target cannot be read is refused too); `.` names the current folder's items, which go, while the folder stays. A file that cannot be deleted is named, exit 2 |
| `-w[dir]` | Working folder | **T-F411 wave E**, `a`. Taken; Pakko writes its temporary archive next to the destination, so the final step is a rename on one volume. `-w<dir>` naming a folder that is not there is exit 2 before anything is written, as in 7-Zip |
| `-ssp` | Keep the sources' last access time | **Declined (T-F411 wave E)**, exit 7 on every command: `tar.exe` cannot keep it, and Windows mostly does not update it |
| `-bso{0\|1\|2}`, `-bse{0\|1\|2}`, `-bsp{0\|1\|2}` | Where messages, errors and the percentage go | **T-F408**, every command: 0 off, 1 stdout, 2 stderr. Defaults: messages stdout, errors stderr, percentage stderr (7-Zip's is stdout). Under `-so` messages are off unless `-bso2`; `-bso1` or `-bse1` with `-so` is exit 7, since the text would end up inside the data |

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

**Staging folders (T-F244 item 4 / T-F263).** Each `-si`/`-so` run stages into its own folder,
`%TEMP%\Archiver.CLI.Stdin\<pid>-<guid>` or `%TEMP%\Archiver.CLI.Stdout\<pid>-<guid>`, owned from
the moment it exists: a failed or cancelled copy (disk full, broken pipe, Ctrl+C) removes it, as
does the end of the command. Ctrl+C in `x`/`t`/`l`/`a` cancels cleanly with exit code **255**; a
read blocked on a stalled stdin pipe does not see that cancellation, so a **second** Ctrl+C ends
pakko at once — with Windows' own `0xC000013A` exit status (`STATUS_CONTROL_C_EXIT`), not 255,
and without the cleanup (the next run's sweep does it). A process killed outright (`taskkill`, power loss) cannot clean up — `x -so` may
then leave an extracted (possibly decrypted) file in its `Stdout` folder; the next `x`/`t`/`l`/`a`
run deletes every staging folder whose process is gone (PID not running, or reused by a process
started after the folder was made). `%TEMP%` is already private to the user, so the folders get
no ACL of their own.

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

## Messages and naming (T-F221, fix phase 7)

- Messages stay English (user decision — 7-Zip parity, scripts); the GUI frontends translate the
  same Core messages. A missing input says "Source path does not exist"; a file that is not an
  archive says so for `x`/`t`/`l`.
- Hints on stderr: `pakko: hint: give the password with -p<password>` after an encrypted archive
  was refused with no password given; `pakko: hint: existing files were kept; -aoa overwrites
  them, -aou renames the extracted ones` when `x` kept existing files because nothing else was
  chosen. A wrong `-p` is one line (`incorrect password (-p)`), not followed by Core's generic one.
  An encrypted 7z or RAR gets no `-p` hint (T-F322): tar.exe cannot decrypt either, and the error
  says that passwords are supported for ZIP archives only.
- `x` over files that already exist (T-F313): each file kept by the default or by `-aos` is one
  `pakko: skipped: <name>: File already exists at destination.` line, for ZIP as for tar-family
  (a ZIP used to exit 0 with no line). The exit code is **1** for the default skip and **0** with
  `-aos`, as in 7-Zip (T-F415: the user chose it; Scoop stops on any other code). An entry kept
  because an earlier entry of the same run took its name (two entries of one name, `a.txt` and
  `A.TXT` in a tar, one name in two archives of one `x`) prints the same line and stays exit 1
  under `-aos` too: no file that was there was kept, an entry of the archive was lost (7-Zip
  exits 0). Any other skip, warning or error keeps its code. A file the user skipped at the
  interactive prompt is not reported (T-F216).
- A gzip of one file (not a tar inside; T-F413): `x`, `t` and `l` read it in process, as
  `tar.exe` cannot. The file is named from the archive's name as 7-Zip's default name is
  (`notes.txt.gz` -> `notes.txt`, `x.tgz` -> `x.tar`, `noext` -> `noext~`), never from the
  name stored in the header (7-Zip uses that one when present, as a path), and lands in the
  `-o` folder; `-ao*`, `-y`, `-so`, the name filters, `-snz` and the compression-ratio check
  work as for any archive. `t` checks every byte and each member's CRC and size: a cut stream
  or bytes after it are `Cannot read archive: The compressed data is damaged.`, exit 2. `l`
  shows the whole decompressed size (7-Zip shows gzip's own size field, the last member's
  only). A gzip whose start holds a tar, cpio, zip or another format `tar.exe` opens still goes
  to `tar.exe`. A bzip2 or xz of one file cannot be read (`tar.exe` gives no way to ask for
  its raw reader, .NET has no such decoder): exit 2 with `tar.exe`'s text, as before.
- `-si`: an empty stdin is `stdin was empty, so there is no archive to read` (exit 2); the staged
  archive is always shown as `(stdin)`, never its temporary path.
- `a -so` with stdout on a terminal is refused (exit 7), like 7-Zip, gzip and zstd.
- `a`: a name with an extension is written exactly as typed (`-ttar x.gz` creates a tar named
  `x.gz`, `-ttar .\.gz` one named `.gz`); a name without one gets the format's extension (7-Zip's
  rule, user decision 2026-09-28). The automatic name (T-F264) is unchanged. A name whose
  extension is an archive type Pakko writes must agree with `-t` (T-F296, user decision
  2026-10-03).
- `a` onto a name another run creates at the same moment (T-F321): the other archive is kept and
  this one is written as `name (1).ext`; stderr says `pakko: warning: created 'name (1).ext': the
  name 'name.ext' was taken while compressing` and the exit code is **1** (T-F325; it was exit 0
  and no line), so a script that goes on to use the name it passed is stopped. A name that
  already exists when `a` starts is still skipped (exit 1) unless `-y` overwrites it.
- `a` onto a name that is one of its own sources (`pakko a -y a.txt a.txt`, T-F421): a source of
  the run is never replaced, `-y` or not. The archive is written as `name (1).ext`, stderr says
  `pakko: warning: created 'a (1).txt': the name 'a.txt' is one of the files being archived`,
  exit **1** (as T-F325). Without `-y` the name is skipped (exit 1), as any existing name.
  7za 26.02 refuses this command ("Cannot open the file as [zip] archive", exit 2).
- `a` names a source as 7-Zip does (T-F414, measured against 7za 26.02; Core's
  `ArchiveOptions.SourceNameParts`): a relative path is stored as typed (`a o.zip dir\sub` stores
  `dir/sub/...`), an absolute path, or one with a `.` or `..` part, by its last part
  (`C:\w\dir\sub` and `.\dir\sub` store `sub/...`), and a path ending in `.` or `..` is that folder's
  contents at the top (`pakko a out.zip .` in `proj` stores `a.txt`; T-F338 had stored
  `proj/a.txt`, a ZIP before it `./a.txt`). A mask (`dir\*.txt`, `d*`, `*.txt -r`) is looked for on
  disk, hidden files included (in a drive root the system's own Hidden and System entries are left
  out, as for a drive root given whole), never entering a link and never taking the archive being
  written (a source named literally can be, as in 7-Zip); an item found twice, or under a folder
  taken whole, is archived once, unless `-x` leaves it out of that folder (`a o.zip -x!dir dir
  C:\w\dir\c.txt` stores `c.txt`, as 7-Zip). `.` stays the contents under `-spd`. A name ending in a
  separator names a folder: on a file it is a warning, as in 7-Zip. When `-x` leaves out every
  source, it is `nothing to archive`, exit 2, as with no source left. A source that exists
  nowhere is `pakko: warning: <name>: The system cannot find the file specified.` and the rest is
  archived, exit **1**; with nothing left `pakko: error: nothing to archive`, exit **2**, and no
  archive (7-Zip writes an empty one). A relative and an absolute source that would land one
  inside the other (`a o.zip C:\other\dir dir\c.txt`) are refused with exit 2 before anything is
  written. A list file after the archive (`@list.txt`) lists sources.
- A refusal to create anything (Group Policy, a password with a tar format) names the archive
  that was to be written, not the output folder (T-F326).
- **PowerShell splits `-name.rest`** into two arguments before pakko sees them: `-ttar.gz` arrives
  as `-ttar` `.gz`, `-pSecret.1` as `-pSecret` `.1` (checked in pwsh 7; `-oC:\out.d` and
  `-mx=1` are not split). A `-p`, `-o` or `-t` switch made only of letters, digits and `_` followed
  by an argument that starts with a dot (not `.\`, `./`, `..`) is refused (exit 7) for every
  command — those are the switches whose value a split silently changes; the message never prints
  either half of a password. A complete switch such as `-y` or `-scrcSHA256` followed by a
  dotfile is accepted. Quote the switch (`'-ttar.gz'`, `'-p<pwd>'`), use `-ttgz`, or write a path that starts
  with a dot as `.\name`. cmd passes such tokens whole.
- Hints are keyed on the cause code of each error and skip (`CliHints`, T-F296): the `-aoa` hint
  follows a conflict, never a CRC failure (T-F293).
- A percentage (`\r 42%`) is written to stderr during `x`/`t`/`a` only when stderr is a console,
  and cleared before any prompt or result line.
- `h <folder>` names each file relative to the folder's parent (`docs\sub\a.txt`) — the same names
  its "data and names" sum covers.

---

See `TASKS.md`'s T-F09 entry for acceptance criteria, test-layer requirements, and current status.

## Recovery data in `t` (T-F275 step 3)

With a PAR2 set next to the archive, `t` also checks the archive against it, block by block at each
block's own position (bad sectors and a cut tail; bytes inserted or removed read as damage from that
point on). `-si` is not checked (a staged stdin has no neighbours); under the `DisableRecoveryData`
policy no set is looked for and a `.par2` path is an error.

| What the set says | Output | Exit |
|---|---|---|
| intact | stdout `out.zip: recovery data intact (2000 blocks, 100 recovery blocks)`; a tar-family archive is no longer "skipped" | 0 (was 1 for tar-family) |
| damaged, repairable | `pakko: error: out.zip: The archive is damaged (3 of 2000 blocks); its recovery data can repair it (100 recovery blocks).` | 2 |
| damaged beyond the set, or the repair too large | `pakko: error: ...beyond what its recovery data can repair...` / `...beyond what Pakko can do.` | 2 |
| a ZIP that tests intact and a set that disagrees | `pakko: warning: out.zip: The recovery data does not match the archive...` — taken to be a set left from an earlier version by another tool (Pakko's own `a -y` without `-rr` removes the old set) | 1 |
| PAR2 files that cannot be read, or a set for another file | `pakko: warning:` line; the archive is tested as without a set | 1 |
| matched by content under another name | `pakko: warning:` line besides the result | 1 |
| a `.par2` whose set cannot be read or whose archive is not found | `pakko: error:` line | 2 |

`t out.zip out.zip.par2` checks the set once. A "can repair it" error ends with
`pakko: hint: 'pakko r <archive>' writes a repaired copy next to it; the archive itself is not changed`.

## Repair: `r` (T-F275 step 4)

`pakko r <archive|.par2>... [-o<dir>] [-y]`. Not a 7z command (7z has no recovery data; the letter
is WinRAR's, as `-rr` is). Each damaged archive is rebuilt from the PAR2 set next to it into a new file,
`<name>.repaired<extension>` (`out.repaired.zip`, `out.repaired.tar.gz`), next to the archive or in
`-o<dir>` (created when missing). A taken name gets a number (`out.repaired (1).zip`). The archive
and the PAR2 files are only read; nothing is ever overwritten, so `-y` is accepted and changes
nothing. A `.par2` path stands for the archive its set protects, and an archive that is gone is
rebuilt when the set holds enough blocks. `r` rebuilds exactly what `t` calls damaged and
repairable. `-si`, `-so`, `-p` and every other switch exit 7.

| What `r` finds | Output | Exit |
|---|---|---|
| damaged, repairable | stdout `out.zip: repaired (3 of 2000 blocks rebuilt): C:\dir\out.repaired.zip` | 0 |
| intact | stdout `out.zip: nothing to repair, recovery data intact (2000 blocks, 100 recovery blocks)` | 0 |
| a ZIP that tests intact and a set that disagrees | the same `pakko: warning:` as `t`; nothing is written | 1 |
| damaged beyond the set, or the repair too large | the same `pakko: error:` as `t` | 2 |
| no PAR2 files, PAR2 files that cannot be read, a set for another file | `pakko: error: out.zip: No recovery data was found next to the archive.` or the reason | 2 |
| the copy does not match the set (the recovery data itself is wrong) | `pakko: error: ...The repaired copy did not match the recovery data and was not kept.` | 2 |
| the copy cannot be written | `pakko: error: ...The repaired copy could not be written: <reason>` and `pakko: hint: -o<dir> writes the repaired copy to another folder` | 2 |
| `DisableRecoveryData` policy | `pakko: error: ...Recovery data is disabled by Group Policy.` for every path | 2 |

Ctrl+C stops with exit 255 and leaves no copy. A `pakko r` that was killed leaves only a
`.pakko-a-*.tmp` file, which the next run in that folder removes. A tar-family archive, or an
encrypted ZIP, that another tool rewrote beside its old set reads as damaged (there is no test to
say otherwise), and `r` writes the earlier bytes as the `.repaired` copy; the archive itself stays.

## Warnings (T-F280)

A warning is something to know about a command that still did what was asked. It is one line on
stderr, `pakko: warning: <archive>: <text>`, and the exit code is **1** (7-Zip's code for a
warning) unless an error makes it 2. `a` has one of its own, the name taken during the run (see
"Messages and naming"). From Core there is one so far: `x` on a ZIP whose local file
headers disagree with its central directory. Pakko extracts by the central directory; another
program may extract other names or data from the same archive. `t` reports the same archive as
an error (exit 2), as `7z t` does.

## 7-Zip compatibility matrix (T-F343)

Measured 2026-10-17 by `scripts/Measure-SevenZipCompat.ps1`: the same command line run by the
vendored 7-Zip `7za.exe` 26.02 (T-F114) and by `pakko.exe`, each in a fresh folder, comparing the
exit code, the files written (path and SHA-256) and the output lines a caller reads. The switch
list is 7-Zip's own (`kSwitchForms` in `CPP/7zip/UI/Common/ArchiveCommandLine.cpp`, tag `26.02`),
not the help page, which leaves out about twenty of them. Method and sources: `docs/DECISIONS.md`,
"T-F343". The tables above say how a command or switch maps onto Pakko; this one says where each
stands.

**States.** **Yes** — works as in 7-Zip (same exit code, same result). **Can add** — missing
today, possible within Pakko's rules; the task is named, or "not planned" when nobody is known to
need it. **Declined** — possible, decided against; the reason is given. **Can't** — would break a
hard constraint or needs what Windows' `tar.exe` and .NET do not have.

### Real callers

What a program that runs `7z.exe` passes, copied from its source. Measured first by T-F343, when
none of the three ran; again after T-F407, T-F408 and T-F409 (2026-10-17): patool's and Chocolatey's
lines run, Scoop's still stopped at `-xr!`; and after T-F410's wave A (2026-10-17): Scoop's and
Chocolatey's folder lines run too.

| Caller | Command line | Today | Stops at | Task |
|---|---|---|---|---|
| Scoop, `Expand-7zipArchive` | `x <arc> -o<dir> -xr!*.nsis -y` (+ `-ir!<dir>\*`, `-aoa`/`-aos`/`-aou`) | ZIP, 7z, tar: works, `-ir!` too. `.tar.gz`, `.tar.xz`: differs, as below. `-aos` over existing files exits 0, as in 7-Zip (T-F415; the kept files are still named on stderr); `-aou` names the renamed file differently | nothing | T-F412 (decided: a known difference) |
| Scoop, inner tar of a `.tar.*` | `l <arc>`, then the regex `[^ ]*tar$` over the output | differs (by design) | 7-Zip opens a `.tar.gz` as gzip holding `x.tar` and extracts the `.tar`; Pakko reads through to the files in one step (kept, T-F412's decision), so neither its `l` nor `l -slt` names an inner `.tar` | T-F412: a known difference |
| Chocolatey, `Get-ChocolateyUnzip` | `x -aoa -bd -bb1 -o"<dir>" -y "<file>" ["<folder>"]`; reads the `- <name>` lines as the list of installed files; one message per exit code 0/1/2/7/8/255 | ZIP, 7z, tar: works, the same `- <name>` lines (7za printed a Cyrillic name in the OEM code page, `pakko` in the console's). `.tar.gz`, `.tar.xz`: differs, as for Scoop's inner tar. A folder after the archive (T-F410): works | nothing | T-F412 (decided: a known difference) |
| patool, `p7zip.py` | `x -y -p- -aou -o<dir> -- <arc>`, `l -y -p- -- <arc>`, `t -y -p- -- <arc>`, `a -y -t7z -sae -- <arc> <files>` | works, except: `t` on a tar or tar.gz is exit 1 (only the structure is checked, T-F397); `x` of a `.tar.gz` or `.tar.xz` gives the files where 7-Zip gives the inner `.tar` | nothing | T-F412 |
| patool | `e -y -p- -o<dir> -- <arc>` | exit 7 | `e` | Declined |
| patool | `a -y -txz`/`-tgzip`/`-tbzip2 -- <file>.xz <file>` | works (T-F413); extracting such a `.xz` or `.bz2` with `x` is exit 2 (bzip2 and xz of one file cannot be read), a `.gz` works | nothing | T-F413 |

### Commands

| Command | State | Note |
|---|---|---|
| `a` | Yes | ZIP, 7z (where `tar.exe` writes it) and the tar family. Without an extension 7-Zip makes `.7z`, Pakko `.zip`: Declined (ZIP is Pakko's default) |
| `x` | Yes | Name filters after the archive name: T-F410 |
| `t` | Yes | |
| `l` | Yes | Pakko prints its own tab-separated columns by default; `-slt` prints 7-Zip's technical blocks (T-F412); `-ba` (T-F408) |
| `h` | Yes | CRC-32 and SHA-256 |
| `i` | Yes | Pakko's own layout (formats with read/write state) |
| `e` | Declined | Pakko always keeps the folder structure ("no flatten mode") |
| `u`, `d`, `rn` | Declined | Changing an archive in place is out of scope |
| `b` | Declined | Benchmarking is out of scope |
| `X`, `A`, ... (capital letters) | Yes | T-F411; ASCII letters only, as in 7-Zip |
| A switch before the command (`-y x a.zip`, `-- x a.zip`) | Yes | T-F411 |

### Switches

| Switch | State | Note |
|---|---|---|
| `--` | Yes | T-F407. Every patool call starts the names with it |
| `-ai`, `-ax`, `-an` | Yes | Archive names by wildcard or list, and `x *.zip`; T-F410, on `x`, `l`, `t` |
| `-ad` | Can't | Opens 7-Zip's GUI dialog (7zG only) |
| `-aoa`, `-aos`, `-aou` | Yes | `-aos` exits 0 for files kept because they exist (T-F415) |
| `-aot` | Yes | T-F411 wave E; the renamed file is numbered as `-aou` numbers it |
| `-ba`, `-bb0`..`-bb3`, `-bd`, `-bso`/`-bse`/`-bsp{0,1,2}`, `-bt` | Yes | Output control; T-F408. `-bb1` and up print `- <name>` per item `x` wrote, which Chocolatey records; where it differs from 7-Zip: the switch table above |
| `-i`, `-x` (with `r`, `w`, `!`, `@list`), `-xtd`, `-xtf`, name filters after the archive, `@listfile`, `-r`, `-r-`, `-r0`, `-spd`, `-scs` | Yes | T-F410's waves A and B; on `a` too (wave C) except `-xtd`/`-xtf`. `m` and `#` after `-i`/`-x`: Declined |
| `-m0=` (7z) | Yes | `lzma2`, `lzma`, `bzip2`, `deflate`, `copy` |
| `-m0=ppmd`, `-m0=zstd` (7z) | Can't | Not in `tar.exe`'s 7z writer |
| `-mx=N`, `-mxN`, `-mx` | Yes | T-F411: `-mx` is 9, above 9 is 9 |
| `-mm=Deflate`/`Copy` (ZIP) | Can add | not planned |
| `-mm=Deflate64`/`BZip2`/`LZMA`/`PPMd` (ZIP) | Can't | No such codec in .NET; Pakko writes ZIP with `System.IO.Compression` only |
| `-mmt` | Yes | T-F411 wave E; the ZIP writer's threads, `tar.exe` is not reached |
| `-mtc`, `-mta`, `-mtm` | Can add | not planned |
| `-mem=AES256` | Yes | |
| `-mem=ZipCrypto`/`AES128`/`AES192` | Declined | ZipCrypto is broken; AES-256 only (T-F193) |
| `-mhe`, `-p` with `-t7z` | Can't | 7z encryption: `tar.exe`'s 7z writer has no AES |
| `-o` | Yes | |
| A switch given twice | Yes | Exit 7 except `-i`, `-x`, `-ai`, `-ax`, `-m`, `-stx`, as in 7-Zip; T-F411. Second `-scrc`: exit 7 (Pakko computes one method) |
| Switch names in capitals (`-Y`, `-MX=9`) | Yes | T-F411; a postfix letter keeps its case (`-AOA`: exit 7 in both) |
| `-p` on `x`, `t`, `a` (ZIP) | Yes | |
| `-p`, `-p-` on `l` | Yes | T-F409 |
| `-p-` on `x`, `t` | Yes | |
| `-sae`, `-sas` | Yes | How `a` names the archive; T-F411 (patool passes `-sae`) |
| `-saa` | Yes | T-F411; Pakko's default type is ZIP, 7-Zip's 7z |
| `-scc` | Yes | |
| `-scrcCRC32`, `-scrcSHA256` on `h` | Yes | Other methods, and `-scrc` on `x`: Can add, not planned |
| `-sdel` | Yes | T-F411 wave E; nothing of a source a filter or a skip touched, never through a link |
| `-seml` | Declined | Sending mail is out of scope |
| `-sfx` | Can't | Needs 7-Zip's SFX module: third-party code |
| `-si`, `-so` | Yes | Within the limits above; with a name filter too (T-F410) |
| `-slp`, `-stm` | Declined | Large pages and CPU affinity tune 7-Zip's own codecs; nothing to tune here |
| `-slt` | Yes | T-F412: the fields Pakko knows, in 7-Zip's names and order |
| `-snh`, `-snl` | Can add | not planned; how Pakko treats links is T-F391's. Without `-snh` 7-Zip stores a file's second hard-link name in a tar as a full file (measured, 26.02); `pakko a -ttar` (and the tar family) instead refuses such a pair with exit 2 and names both (T-F392): tar.exe can store only a link entry, which Pakko does not extract. ZIP and 7z store both, as 7-Zip does |
| `-sni`, `-sns` | Can't | NT security and alternate streams have no place in ZIP or in `tar.exe`'s formats as Pakko writes them |
| `-snc`, `-snr`, `-snt`, `-snoi`, `-snon`, `-snld` | Declined | Pakko's own name and link safety rules decide these (`SECURITY.md`) |
| `-snz` | Yes | |
| `-spe` | Yes | T-F411 wave E (`ExtractOptions.DuplicateRootName`) |
| `-spf` | Declined | Absolute paths in an archive are what Pakko's traversal checks refuse |
| `-ssc`, `-ssc-` | Yes | T-F410 |
| `-sse`, `-ssw`, `-stl`, `-w` | Yes | T-F411 wave E; `-sse` and `-ssw` act on the ZIP engine, `tar.exe` behaves so already |
| `-ssp` | Declined | `tar.exe` cannot keep the last access time; Windows mostly does not update it |
| `-stx` | Yes | T-F410's wave B, on `x`, `l`, `t` |
| `-t` on `a` | Yes | Case-insensitive (`-tZIP`), T-F411 |
| `-tgzip`, `-tbzip2`, `-txz` (one file, no tar) | Yes | T-F413: written by tar.exe's raw format; read back: gzip yes (in process), bzip2 and xz Can't (no reader tar.exe exposes, no .NET decoder) |
| `x`, `t`, `l` of a `.bz2` or `.xz` holding one file | Can't | tar.exe has no reader for a bare stream that can be asked for, .NET has no bzip2/xz decoder, and Pakko takes no third-party compression code |
| `-u` | Declined | Update is a change in place |
| `-v` | Can add | not planned (split volumes) |
| `-y` on `x`, `a` | Yes | |
| `-y` on `l`, `t` | Yes | T-F409 |

Not measured (7-Zip itself refused the probe's context): `-scrf`, `-shd`, `-slf`, `-slsl`,
`-slmu`, `-smemx`, `-spm`, `-spo`.

### Errors and exit codes

7-Zip's codes (`ExitCode.h`): 0 no error, 1 warning, 2 fatal error, 7 command line error, 8 out
of memory, 255 stopped by the user. 7-Zip writes errors to stderr (`ERROR: ...`, `Command Line
Error:`) and a summary line to stdout (`Everything is Ok`, `Errors: N`, `Warnings: N`).

| Case | 7-Zip | Pakko | State |
|---|---|---|---|
| Missing archive, cut archive (`x`, `t`, `l`), not an archive | 2 | 2 | Yes |
| Wrong password | 2, writes empty files | 2, writes nothing | Yes (Pakko writes no empty files) |
| Unknown command, unknown switch, no archive name, `-o` with a space | 7 | 7 | Yes |
| `-o` names a file | 2 | 2 (crashed before T-F406) | Yes |
| `a` with one missing source | 1 (warning), the rest archived under the path as given (`app\readme.txt`) | 1 (warning), the same (2 and `readme.txt` before T-F414) | Yes |
| `a` with a source file that cannot be read (locked, no read access, open for writing without `-ssw`) | 1 (warning), the rest archived | 2, the file named, the rest archived | Declined: a file the user named is not in the archive; with `-sse` no archive is written (T-F419) |
| `a` whose sources all are missing, or whose masks find nothing | 1 or 0, an empty archive | 2, `nothing to archive`, no archive | Declined: Pakko writes no empty archive |
| `a` with the same name twice (`a o.zip t.txt top\..\t.txt`) | 2, `Duplicate filename on disk` | 0, archived once (one file), or `t (1).txt` for two files | Declined: Pakko's renaming rule (T-F30) |
| `a` with `-stx` | ignored | 7 | Declined: Pakko refuses a switch it would not use |
| Encrypted archive or a conflict, no `-y`, stdin closed | 255 (the prompt reads end of input) | 2 or 1, no prompt | Declined: Pakko does not prompt when nobody can answer (T-F160, T-F191) |
| Text of the messages | `ERROR: ...` | `pakko: error: ...` | Declined: Pakko's own wording (T-F221); scripts branch on the exit code |

**Position.** "Goal" above says `pakko` is familiar but not a drop-in. The numbers keep that true
today. T-F407 to T-F410 close what the three measured callers stop at, except `e` and the inner
tar step; whether `pakko` can then be offered as a stand-in for those callers is decided when they
land, with this script run again.
