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
| `t` | Test (verify integrity) | Partial — ZIP via existing `TestAsync` (T-F62); tar-family has no test capability (`ITarService` has no Test method, per T-F86's finding). Since T-F261 `t` goes through `IExtractionRouter.TestAsync`: each path is classified once (Group Policy, then format), a tar-family or refused path is skipped with the router's reason, and tar.exe is never started |
| `e` | Extract, flattened (no directory structure) | Not supported — Pakko's extraction always preserves the archive's folder structure; no flatten mode exists |
| `x` | Extract with full paths | Supported — `ExtractMode.SingleFolder`; since T-F205 an archive's single root folder is kept, as `7z x` does. Without `-o`, extracts into the **current directory**, as `7z x` does (T-F206; before it, next to the archive) |
| `l` | List contents | Supported — consumes `IArchiveListingRouter` (T-F05, shipped), looped once per archive path given. Tab-separated columns `Size`, `Compressed`, `Crc32`, `Modified`, `Type`, `Encrypted`, `Path` (Path always last). `Compressed` and `Crc32` are `-` for tar-family, 7z and RAR (no per-entry value; T-F214, was `0` for `Compressed`); `Modified` is local time, for those formats read from tar.exe's listing (to the minute within half a year, else the date at 00:00), `-` when unreadable. `Encrypted` (T-F221 item 7, 2026-09-28) is `-`, `ZipCrypto`, `AES-128`/`AES-192`/`AES-256`, `+` (encrypted by a method Pakko cannot name) or `?` (the format cannot say without extracting: tar-family, 7z, RAR) |
| `b` | Benchmark | Not supported, deliberately out of scope (same reasoning as T-F05's NanaZip-toolbar scope cuts) |
| `i` | Info (list supported archive formats/codecs) | Supported — prints ZIP and tar/tar.gz (always) plus each tar.exe-backed format (tar.bz2/xz/zst/lzma, 7z, rar) with its live `TarCapabilities` result, and the tar.exe version. Since T-F261 each format's status comes from Core's `ArchiveFormatPolicy` and the loaded Group Policy: "supported", "not supported" or "blocked by Group Policy"; under `DisableTarExtraction` the tar.exe line reads "disabled by Group Policy" and no version probe runs. Takes no arguments (only `-scc`) |
| `h` | Hash | **Supported (added 2026-07-20, T-F128/T-F09 follow-up).** Real 7z `h` hashes files on disk, not archive entries — the original row here predated T-F128 and described the wrong thing. Maps onto `FileHashService.ComputeAsync` (same engine as the Explorer context menu's CRC-32/SHA-256 commands, flattened out of the old "Хеш-суми" submenu by T-F128): one or more files hashed independently, or exactly one folder recursed with a combined DataSum/NamesSum printed (NanaZip-compatible, verified against the vendored `7za.exe`) |
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

| 7z switch | Meaning | Pakko mapping |
|-----------|---------|----------------|
| `-o{dir}` | Output directory | Maps directly to `ExtractOptions.DestinationFolder`; omitted, it is the current directory (T-F206, 7z behavior), including with `-si` |
| `-p{pwd}` | Password / encryption | Supported on `x`/`t` (T-F191, ZIP only — ZipCrypto and WinZip AE-1/AE-2) and on `a` (T-F193: encrypts every file entry with WinZip AES-256 AE-2; folder entries and all file names stay unencrypted). Not applicable to `l` (listing needs no password). **Bare `-p`** (T-F193, 7z semantics) asks on the console — once on `x`/`t`, twice (enter + re-enter) on `a`; before T-F193 a bare `-p` was a command-line error. A bare `-p` with a redirected stdin or with `-si` exits **7** (nothing to type into). **On `a` only:** the password must be printable ASCII (0x20–0x7F) and at most 99 characters — 7-Zip's own creation rule (it decodes ZIP passwords through the ANSI code page and refuses longer AES passwords), so any other password would give an archive 7-Zip cannot open; `x`/`t` accept any password. A `-p<pwd>` that breaks the rule exits **7** (command-line error, nothing created); at the interactive prompt a mismatch or a refused password exits **2** with its reason and is never re-asked (7z behavior), and Esc/Ctrl+C exits **255**. `-p` with any `-t tar*` format exits **7** (tar has no encryption). Without `-p` on an encrypted archive (`x`/`t`): a real interactive console (not redirected/piped) and no `-y` prompts with a masked `Console.ReadKey`-based input, retried on a wrong password up to 3 times; a redirected/piped stdin (including `-si`, which already consumes stdin for the archive itself) or `-y` fails immediately with the same message as an unresolved password always has — deliberate: `-y` means "pick the safe default" for conflicts/compression-bomb warnings, where a safe default exists; there is no safe default for a missing password, so `-y` cannot make the operation proceed, only fail predictably instead of hanging on a prompt that can't be answered non-interactively. **`-p<pwd>` is visible in the process's own command line** (`Get-Process`/Process Explorer, or any other process on the machine enumerating command lines) for as long as `pakko.exe` runs — same exposure as any CLI tool's `-p`/`--password`-shaped flag; prefer the interactive prompt over `-p` when that matters. See `ARCHITECTURE.md`'s "`-p{pwd}` password support (T-F191)" section and `DECISIONS.md`'s T-F191/T-F193 entries |
| `-r[-\|0]` | Recurse subdirectories | Archiving already recurses folders by default; the 7z on/off nuance needs its own check against current `ArchiveOptions` behavior |
| `-i{pattern}` / `-x{pattern}` | Include/exclude filename patterns | Not supported — no wildcard include/exclude filtering exists in `ArchiveOptions`/`ExtractOptions` today |
| `-y` | Assume yes, suppress prompts | Needs a CLI-side default for Pakko's interactive callbacks (conflict resolution, compression-bomb confirm, T-F94) since there's no UI to prompt. **T-F160:** without `-y`/`-ao`, `x` on a real interactive console (stdin not redirected, not `-si`) now asks per conflict exactly like 7-Zip's console — `(Y)es / (N)o / (A)lways / (S)kip all / A(u)to rename all / (Q)uit?`, re-asked until one valid letter, end of input = quit (confirmed against NanaZip's vendored `UI/Console/UserInputUtils.cpp`). Deviation: the prompt goes to **stderr**, not 7z's stdout, since `-so` streams data on stdout. "Always"/"Skip all"/"Auto rename all" span the whole command, including a mixed zip+tar selection. Redirected/scripted runs keep the silent Skip. `Q` or Ctrl+C stops cleanly with exit code **255** (7-Zip's "user stopped"). **T-F216:** entries the user skipped at the prompt (one by one or "Skip all") are not reported as skips, so a run where the user skipped everything exits **0**, as in 7-Zip (was 1); the configured Skip (`-aos`, redirected input) still reports them |
| `-t{type}` | Archive type override | Only meaningful for `a` (creation) — extraction/list/test formats are always auto-detected via `ArchiveFormatDetector`, `-t` has no effect there. For `a`, 7 real spellings map 1:1 onto `ArchiveContainerFormat`: `-tzip` (default), `-ttar`, `-ttar.gz`, `-ttar.bz2`, `-ttar.xz`, `-ttar.zst`, `-ttar.lzma`, plus the dot-free `-ttgz`/`-ttbz2`/`-ttxz`/`-ttzst` that PowerShell cannot split (T-F294). A name ending in one of these types must match the type, `-tzip` included when no `-t` is given (`a out.tar.gz src` is exit 7, not a ZIP named `out.tar.gz`); any other extension is written as typed. `-t7z`/`-trar` are recognized but rejected — extract-only formats, Pakko can't create them |
| `-v{size}` | Split into volumes | Not supported — no multi-part/split-archive logic exists anywhere in `Archiver.Core` |
| `-mem={method}` | ZIP encryption method | T-F193, `a` only: `-mem=AES256` (case-insensitive) is accepted as a no-op — AES-256 is the only method Pakko writes. `-mem=ZipCrypto`/`AES128`/`AES192` are refused (exit 7) rather than silently upgraded; any other value is an unknown-value error. Every other `-m{param}` except `-mx` is refused on `a` as not implemented |
| `-m{params}` (e.g. `-mx=9`) | Compression method/level | Partial — 7z's 0–9 scale doesn't map 1:1 onto `System.IO.Compression.CompressionLevel`'s four discrete values (`NoCompression`/`Fastest`/`Optimal`/`SmallestSize`); needs an explicit, documented bucketing, not a naive `/9*4` — resolved: `0`->`NoCompression`, `1-2`->`Fastest`, `3-6`->`Optimal` (7z's own default `-mx5` lands here), `7-9`->`SmallestSize` |
| `-ao{a\|s\|u\|t}` | Overwrite mode | Mostly supported — maps to `ExtractOptions.OnConflict` (Overwrite/Skip/Rename); 7z's 4th variant (`t`, rename existing instead of new) has no Pakko equivalent |
| `-scc{charset}` | Console charset | **Supported on every command (T-F238).** `-sccUTF-8`, `-sccWIN` (system ANSI code page) or `-sccDOS` (system OEM code page), case-insensitive, last one wins — the three names 7-Zip's own help lists (confirmed in NanaZip's vendored `ArchiveCommandLine.cpp`/`Main.cpp`); 7-Zip's undocumented numeric form (`-scc1251`) is refused with a named error. Sets the encoding of everything pakko prints, stdout **and** stderr, with no BOM. Without it, output uses the console code page, so a name the page cannot hold is written as `?` (7-Zip writes it lossily too): under `chcp 866`, `pakko l x.zip > list.txt` turns `Звіт.txt` into `Зв?т.txt`. Use `-sccUTF-8` whenever a script needs exact names. The switch is meant for redirected output: on
an interactive console whose code page is not UTF-8, the console decodes pakko's UTF-8 bytes in
its own code page, so non-ASCII text (including a conflict prompt on stderr) shows as mojibake —
expected, and the same with 7-Zip. The console's own code page is never changed (pakko does not call `SetConsoleOutputCP`). Input is unaffected: prompts read keys as Unicode |
| `-ssc` | Case-sensitivity | Not supported; case-sensitive matching is an open question worth a decision, not an assumption |
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
- `-si`: an empty stdin is `stdin was empty, so there is no archive to read` (exit 2); the staged
  archive is always shown as `(stdin)`, never its temporary path.
- `a -so` with stdout on a terminal is refused (exit 7), like 7-Zip, gzip and zstd.
- `a`: a name with an extension is written exactly as typed (`-ttar x.gz` creates a tar named
  `x.gz`, `-ttar .\.gz` one named `.gz`); a name without one gets the format's extension (7-Zip's
  rule, user decision 2026-09-28). The automatic name (T-F264) is unchanged. A name whose
  extension is an archive type Pakko writes must agree with `-t` (T-F296, user decision
  2026-10-03).
- **PowerShell splits `-name.rest`** into two arguments before pakko sees them: `-ttar.gz` arrives
  as `-ttar` `.gz`, `-pSecret.1` as `-pSecret` `.1` (checked in pwsh 7; `-oC:\out.d` and
  `-mx=1` are not split). A switch made only of name characters followed by an argument that starts
  with a dot is refused (exit 7) for every command; the message never prints either half of a
  password. Quote the switch (`'-ttar.gz'`, `'-p<pwd>'`), use `-ttgz`, or write a path that starts
  with a dot as `.\name`. cmd passes such tokens whole.
- Hints are keyed on the cause code of each error and skip (`CliHints`, T-F296): the `-aoa` hint
  follows a conflict, never a CRC failure (T-F293).
- A percentage (`\r 42%`) is written to stderr during `x`/`t`/`a` only when stderr is a console,
  and cleared before any prompt or result line.
- `h <folder>` names each file relative to the folder's parent (`docs\sub\a.txt`) — the same names
  its "data and names" sum covers.

---

See `TASKS.md`'s T-F09 entry for acceptance criteria, test-layer requirements, and current status.
