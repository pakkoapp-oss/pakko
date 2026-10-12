# Smoke Catalogue

The one list of checks run on a built, installed Pakko: the package from `Deploy.ps1`, the files of
a release tag, the Store bundle. `dotnet test` runs under JIT against Debug builds and cannot see
what these checks see (T-F375 was found here with every test green). Unit, integration and
subprocess tests are described in `docs/TESTING.md`; this file starts where they stop.

## How to use it

A run is a filter over the rows below, never a copy of them.

| Tier | When | What runs |
|---|---|---|
| `quick` | after any change that reaches a shipped exe | rows marked `quick`: scripts only, on the dev package, ten minutes |
| `release` | before a `vX.Y.Z` tag | `quick` and `release` rows: scripts, the agent driving the real UI, one elevated batch |
| `store` | before a Store submission and after certification | all rows, on the tag's files and the Store bundle |

Columns:

- **ID** - the surface and a number. An ID is never reused or renumbered. A row that no longer
  applies keeps its ID, gets the tier `retired` and says why in **Expect**.
- **Tier** - `quick`, `release`, `store` or `retired`. A higher tier includes the lower ones.
- **By** - who can do it: `script` (unattended), `mcp` (the agent through the `windows` MCP
  server, or a person), `uac` (a script that needs one elevation prompt), `user` (a person only:
  a real drag, a sign-out), `store-machine` (a machine with the Store's install).
- **On** - what is under test: `dev` (the package `Deploy.ps1` installed), `tag` (the files of the
  GitHub Release; before the tag exists, the bundle CI built on the release commit),
  `store-bundle`, `store-install`.
- **Covers** - the entry points the row exercises, as the code names them (`cli:` a `pakko`
  command or switch, `shell:` an `Archiver.Shell` argument, `menu:` an Explorer command class,
  `app:` a `MainViewModel` command, `policy:` a Group Policy value), and the tasks it guards.

Rules:

- Results are not written here. A run's table (ID, OK / FAIL / skipped with the reason) goes into
  that release's record in `docs/DECISIONS.md`.
- A failed row of severity P0 or P1 holds the tag; P2 and P3 become a task in `docs/TASKS.md` with
  the steps to reproduce.
- A skipped row always names its reason.
- A new command, switch, menu item, App command or policy needs a row that names it:
  `SmokeCatalogueTests` fails the build otherwise, and also when a row names something the code no
  longer has.
- To make a run longer or shorter, change a row's tier or add a row.
- Fixtures are generated into a temporary folder by the run; no row depends on a folder left by an
  earlier run.
- The rows a script can run are run by `scripts\Invoke-Smoke.ps1 -Tier quick` (or `release`,
  `store`; `-Only PAR-*,CLI-004` for some rows; `-List` to see a tier). A row's step is in
  `scripts/smoke/`, registered under the row's ID; a row without a step is printed with its action
  and expectation and reported as `manual`.

Surfaces: `CLI` the console program, `SHL` `Archiver.Shell.exe` started as Explorer starts it,
`EXP` the real Explorer menu, `APP` the WinUI window, `OPW` the Explorer operation window, `PAR`
PAR2 recovery data, `POL` Group Policy, `PKG` the package and its life cycle, `PRF` speed
budgets, `REL` the release's files, `STO` the Store.

## CLI - `pakko`

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| CLI-001 | quick | script | dev | Run the `Subprocess` test layer with `PAKKO_CLI_EXE` set to the installed `pakko.exe` | Every test passes | `cli:a` `cli:x` `cli:t` `cli:l` `cli:i` `cli:h` `cli:-o` `cli:-y` `cli:-ao` `cli:-t` `cli:-m` `cli:-mx` `cli:-scrc` T-F09 |
| CLI-002 | quick | script | dev | `pakko -v`, `--version`, `-h`, `--help` through the package alias in `WindowsApps` | The version is the package's; the help lists `a x t l i h r`; exit 0 | `cli:-v` `cli:--version` `cli:-h` `cli:--help` T-F317 |
| CLI-003 | quick | script | dev | The refused commands `u`, `d`, `rn`, `b`, `e` and the refused switches `-r`, `-i`, `-x`, `-ssc`, each on a real archive | Exit 7, a line that names the command or switch as not supported, the archive and the folder unchanged | `cli:u` `cli:d` `cli:rn` `cli:b` `cli:e` `cli:-r` `cli:-i` `cli:-x` `cli:-ssc` |
| CLI-004 | quick | script | dev | Round trip of a folder with a Cyrillic name: `a` as ZIP at `-mx=0` and `-mx=9`, as `-ttar.gz`, and as AES-256 (`-p`, `-mem=AES256`); then `l`, `t`, `x -o` | Extracted files equal the sources byte for byte; `l` shows the names and the Encrypted column; `7za t` accepts each archive | `cli:a` `cli:x` `cli:l` `cli:t` `cli:-p` `cli:-mem` `cli:-mx` `cli:-t` `cli:-o` |
| CLI-005 | quick | script | dev | The error exits: a missing archive, a wrong password, a ZIP with one flipped byte | Exit 2 each time; the damaged entry is named with both CRC values and is not left on disk | `cli:t` `cli:x` T-F231 T-F246 |
| CLI-006 | release | script | dev | Extract a 7z, a RAR and a tar.gz, and a tar with an escaping name | The first three extract through the sandbox; the last is refused; no `PakkoTarStage_*` or `PakkoTarSandbox` folder remains in `%TEMP%` | `cli:x` T-F148 T-F256 |
| CLI-007 | release | script | dev | Through `cmd`: `pakko a -so` piped into `pakko x -si`; `pakko h -si`; `pakko a -so` with the output on a terminal | The piped archive extracts equal to the source; the hash equals `Get-FileHash`; `-so` to a terminal is refused with exit 7 | `cli:-si` `cli:-so` `cli:h` |
| CLI-008 | release | script | dev | Send Ctrl+C to a long `pakko a` | Exit 255; no archive and no `.tmp` file is left | `cli:a` |
| CLI-009 | release | script | dev | `-v`, `a`, `t`, a missing archive and an unknown switch from `cmd`, `pwsh` 7 and Windows PowerShell 5.1; `-ttar.gz` typed in each | The same exit codes in all three; where PowerShell splits `-ttar.gz` the refusal carries the hint | `cli:-t` T-F294 |
| CLI-010 | release | script | dev | `pakko l` of an archive with Cyrillic names under `chcp 866` and `chcp 65001`, with `-sccUTF-8`, `-sccWIN` and `-sccDOS` | The names read correctly in each pairing that can show them | `cli:-scc` `cli:l` T-F238 |
| CLI-011 | release | script | dev | `x` and `x -snz0` on a ZIP that carries the download mark | The extracted files have `Zone.Identifier` by default and none with `-snz0` | `cli:-snz` T-F360 |
| CLI-012 | release | script | dev | `pakko h` of a folder against `7za h`; the same on a folder with an unreadable subfolder and a junction loop | The sums equal 7-Zip's; the unreadable entries are listed and the loop does not hang it | `cli:h` `cli:-scrc` T-F225 T-F251 |
| CLI-013 | release | mcp | dev | In a real console window: `pakko x enc.zip -p` with the password typed at the prompt | The prompt masks the input; the right password extracts, a wrong one gives one line | `cli:-p` T-F221 |
| CLI-014 | release | script | dev | Hostile ZIPs: names that leave the destination, a bomb, a local header that disagrees with the central directory, a 20,000-segment name | Each is refused per entry or asked about; nothing is written outside the destination; the error is short | `cli:x` `cli:t` T-F243 T-F237 T-F280 |
| CLI-015 | release | script | dev | Extract into a read-only folder, into a path longer than 260 characters, and over a file another process holds open | The read-only folder: one error, exit 2, nothing left in it. The long path: extracted whole. The held file: one error that names it, exit 2, the other files replaced, no partial file | `cli:x` `cli:-ao` |
| CLI-016 | release | script | dev | Two `pakko a` at once into one folder; then kill a third mid-write and create a small archive there | The first two both succeed; after the small run the folder holds the three archives only (nothing of the killed run); neither run replaces the other's archive | `cli:a` T-F263 T-F312 T-F321 T-F379 |
| CLI-017 | store | script | tag | `pakko.exe` from the release's `pakko-win-x64.zip`: its product version, `-v`, `--version`, `-h`, `--help`, and the `Subprocess` test layer on it, less the two tests that expect the version text of a build that is not a release | The product version is the tag's version and commit; `-v` prints `pakko <version>`; the help lists `a x t l i h r`; every test passes | `cli:-v` |
| CLI-018 | release | script | dev | Extract a ZIP and a tar whose entries are dated 2001; `pakko x a.zip` with no `-o` from an empty current folder; `a -mx=1` and `-mx=9` on 32 MiB of random bytes | Files and folders carry the archive's times; the files land in the current folder; both archives are about the size of the input (stored) | `cli:x` `cli:a` `cli:-mx` T-F298 T-F206 T-F299 |
| CLI-019 | release | script | dev | tar through `pakko`: 300 files into one tar; files named `-C`, `-T`, `--exclude=x`, `@list` and a name with fullwidth quotes; a tar with two entries of one name under `-aou`, `-aos` and `-aoa`; extract a read-only tar | 300 entries; the odd names are stored as files, or refused, and never read as tar options; both duplicates, the first, the second; the archive stays read-only and nothing of it stays in `%TEMP%` | `cli:a` `cli:x` `cli:-ao` T-F273 T-F283 T-F266 T-F171 T-F248 |
| CLI-020 | release | script | dev | `pakko l` of a tar and a tar.gz with cp866 names, and of a ZIP with cp866 names and no UTF-8 flag | Cyrillic names read correctly and no entry is lost; the tar listing has dates and `-` for the packed size | `cli:l` T-F305 T-F310 T-F234 T-F214 |
| CLI-021 | release | script | dev | The messages: `x` over a ZIP with a CRC error; `x` and `t` on an encrypted 7z; a password remembered for several archives that fits only the first; a run that extracts all, some, and none | No `-aoa` hint after the CRC error; no `-p` hint for the 7z; one line for the password that does not fit; exit 0, 1 and 2 for all, some and none; counts read naturally (no "1 entries") | `cli:x` `cli:t` `cli:-p` T-F293 T-F295 T-F296 T-F322 T-F301 T-F260 T-F323 |
| CLI-022 | quick | script | dev | `a -ttar.gz` of a tree, then `x` into a folder that grants Authenticated Users read access; save each extracted file's security with `icacls /save` | Every file has the folder's inherited entry, no entry for an AppContainer SID (`S-1-15-2-`) and no integrity label | `cli:x` T-F389 |
| CLI-023 | release | user | dev | `x` of a tar.gz and of a 7z onto an exFAT or FAT32 USB drive and into a network share | Exit 0 both times; every file is there with the source's bytes; no "cannot extract" line | `cli:x` T-F389 |

## SHL - `Archiver.Shell.exe`, started as Explorer starts it

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| SHL-001 | quick | script | dev | `--extract-here` on a ZIP with one root folder, on one with several root items and on one with a single file | The first folder's content lands in a folder named after the archive, not doubled; the several items are wrapped in such a folder; the single file lands next to the archive | `shell:--extract-here` |
| SHL-002 | quick | script | dev | `--extract-flat` and `--extract-folder` on the same ZIPs | Flat puts the items straight into the folder; the other always makes `<name>\` | `shell:--extract-flat` `shell:--extract-folder` |
| SHL-003 | quick | script | dev | `--archive` on a folder, and `--archive --format tar` | `<name>.zip` and `<name>.tar` next to the source; `7za t` and `tar -tf` accept them; entries keep the folder prefix | `shell:--archive` `shell:--format` T-F75 |
| SHL-004 | quick | script | dev | `--test` on a good ZIP and on one with a flipped byte; read the window's text | "No errors" for the first; the entry and both CRC values for the second | `shell:--test` |
| SHL-005 | quick | script | dev | `--hash --algorithm sha256` and `--algorithm crc32` on a file | The values equal `Get-FileHash` and `7za h` | `shell:--hash` `shell:--algorithm` T-F128 |
| SHL-006 | quick | script | dev | `--archive --paths-stdin` with 1000 paths written to stdin | One archive with 1000 entries | `shell:--paths-stdin` T-F235 |
| SHL-007 | quick | script | dev | `--open-ui --browse <zip>`, `--open-ui --extract <zip>`, `--open-ui --archive <file>` | The App window opens in the archive view, with the archive pending, and with the file pending; a second window opens offset from the first, not on top of it | `shell:--open-ui` `shell:--browse` `shell:--extract` T-F201 |
| SHL-008 | release | script | dev | `--scan` on a ZIP that holds the EICAR test file and on a clean one | "Threat detected" and "clean"; a tar.gz is scanned through the sandbox | `shell:--scan` T-F146 |
| SHL-009 | release | script | dev | `--test` on an encrypted ZIP: answer the window's password prompt wrongly, then rightly | A second prompt with "wrong password", then "no errors" | `shell:--test` T-F191 |
| SHL-010 | release | script | dev | No file list, an unknown argument, a path that does not exist | The first two: a non-zero exit and no window. The missing path: a result window that names the file; nothing is written | `shell:--extract-here` |
| SHL-011 | release | script | dev | `--hash --algorithm sha256` on a folder with an unreadable file and a junction loop | The window lists the hashes and names the entries it skipped; it does not hang | `shell:--hash` T-F291 T-F251 |

## EXP - the real Explorer menu

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| EXP-001 | release | mcp | dev | Right-click a `.zip`, open the Pakko submenu | Open, Extract files, Extract here, Extract here (smart), Extract to folder, Compress, Test, Scan, two Hash items, in that order; the root Pakko item has the icon, the submenu items have none (T-F92) | `menu:BrowseCommand` `menu:ExtractDialogCommand` `menu:ExtractHereFlatCommand` `menu:ExtractHereCommand` `menu:ExtractFolderCommand` `menu:CompressDialogCommand` `menu:TestCommand` `menu:ScanCommand` `menu:HashCrc32Command` `menu:HashSha256Command` |
| EXP-002 | release | mcp | dev | Right-click a folder, a plain file, and a mixed selection of a ZIP and a text file | Add to X.zip and Add to X.tar on the first two; on the mixed one both Add and Test, Test after the primary action | `menu:ArchiveCommand` `menu:TarArchiveCommand` |
| EXP-003 | release | mcp | dev | Right-click an archive with PAR2 files next to it, its `.par2` file, and an archive without a set | Verify with PAR2 and Repair with PAR2 on the first two, after Test; neither on the third | `menu:RecoveryVerifyCommand` `menu:RecoveryRepairCommand` T-F275 |
| EXP-004 | release | mcp | dev | Click Extract here, Extract to folder and Extract files on a ZIP that carries the download mark | The files are where each item says; the mark is carried over; Cyrillic names are intact | `menu:ExtractHereCommand` `menu:ExtractFolderCommand` `menu:ExtractDialogCommand` |
| EXP-005 | release | mcp | dev | Click Open, then Test, then Scan, then each Hash item | The App opens on the archive; the operation window reports each result; the hashes equal `Get-FileHash` and `7za h` | `menu:BrowseCommand` `menu:TestCommand` `menu:ScanCommand` `menu:HashCrc32Command` `menu:HashSha256Command` |
| EXP-006 | release | mcp | dev | Click Compress on a folder and create an encrypted ZIP with recovery data from the dialog | The dialog's choices are in the archive: AES-256, the PAR2 files next to it | `menu:CompressDialogCommand` |
| EXP-007 | release | mcp | dev | Select 300 files and click Add to X.tar | One tar with 300 entries; the list went through stdin | `menu:TarArchiveCommand` T-F273 |
| EXP-008 | release | mcp | dev | Put de-AT first in the language list (no sign-out) and open the menu | The items are in German: the first language of the list that Pakko ships wins | T-F330 |
| EXP-009 | release | mcp | dev | Open the menu right after a package update, without restarting Explorer | The Pakko items are there and the clicked one runs the new `Archiver.Shell.exe` | `menu:TestCommand` |
| EXP-010 | release | mcp | dev | Add to X.zip on the root of a drive (a `subst` letter) | The archive is named after the drive letter, not `archive` | `menu:ArchiveCommand` T-F281 |

## APP - the WinUI window

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| APP-001 | release | mcp | dev | Read the title bar and About | A build time within minutes of the deploy; About shows the package version | T-F218 |
| APP-002 | release | mcp | dev | Add files and a folder through the pickers; create a ZIP, each tar format and an AES-256 ZIP (password `Right-1`, then run APP-018) | Every archive passes `7za t`; the encrypted one is AES-256 | `app:BrowseFiles` `app:BrowseFolder` `app:Archive` |
| APP-003 | release | mcp | dev | Extract a marked ZIP with the download-mark box on, then off | `Zone.Identifier` present, then absent; the note under the box changes | `app:Extract` T-F360 |
| APP-004 | release | mcp | dev | In the archive view: enter folders, use the breadcrumb, go Up to the folder, the drive and This PC, close the archive; in the pending list: sort by each column | Rows are never blank; Up follows diagram 6; sorting flips on a second click; hidden system folders are not listed at a drive root | `app:NavigateUp` `app:SortBy` `app:CloseArchive` T-F112 T-F292 T-F324 |
| APP-005 | release | mcp | dev | Extract selected rows, then Extract all, from a ZIP and from a tar with `a[1].txt` next to `a1.txt` | Only the selected entries, then all of them | `app:ExtractSelectedFromBrowser` `app:ExtractAllFromBrowser` T-F284 |
| APP-006 | release | mcp | dev | Test and Scan from the archive view; preview a text file and an image; open a nested archive | "No errors"; the scan verdict; the preview opens; the nested archive lists | `app:TestBrowsedArchive` `app:ScanArchiveFromBrowser` T-F111 |
| APP-007 | release | mcp | dev | Raise each dialog: a name conflict, a suspected bomb, a password (`Wrong-1`, then `Right-1`; then run APP-018), an error | Each is shown and the choice takes effect; the conflict dialog shows size and date of both files; a renamed `x.tar.xz` becomes `x (1).tar.xz` | T-F220 T-F362 |
| APP-008 | release | mcp | dev | Pick a destination folder, then use the parent-folder button | The path field follows both | `app:BrowseDestination` `app:NavigateDestinationUp` |
| APP-009 | release | mcp | dev | Start a long operation and Cancel it; add rows and Clear them; add a large folder and remove it before its size is counted | No partial output; the list is empty; counting stops | `app:Cancel` `app:Clear` T-F236 |
| APP-010 | release | mcp | dev | After an extraction use the result line: Show in folder, then the details | Explorer opens with the output selected; the summary dialog lists the result | `app:ShowOutcomeInFolder` `app:ShowOutcomeDetails` T-F211 |
| APP-011 | release | mcp | dev | Create with "move the sources to the Recycle Bin" and "open the destination folder" on | The sources are in the Recycle Bin and gone from the list; the folder opens | T-F207 T-F302 |
| APP-012 | release | mcp | dev | Keyboard only: Enter and Backspace in the archive view, Delete and Shift+F10 in the list, Tab through the footer | Every action is reachable and every control has a name for a screen reader | T-F308 T-F304 |
| APP-013 | release | mcp | dev | A window under 600 px high; the light, dark and high-contrast themes; switching the theme while the window is open | The option cards scroll and nothing is cut off; colours are right in each theme | T-F282 |
| APP-014 | release | mcp | dev | Display scale 150 % and 200 % | No clipped text or overlapping controls in the main view, the archive view and the dialogs | |
| APP-015 | release | mcp | dev | The App in ar-SA, de-DE and ja-JP (language list, no sign-out) | Right-to-left layout mirrors; the longest strings fit; no English left in the main view | T-F209 |
| APP-016 | release | mcp | dev | From Explorer: Open with, then Pakko, on an archive, with no Pakko window open | A new window opens on that archive | `app:Extract` |
| APP-017 | release | mcp | dev | Keep one window open for 15 minutes across ten operations, the archive view and a theme change | It answers every click; the event log has no entry from Pakko | T-F355 |
| APP-018 | release | script | dev | Last in a run: type a new password into the Shell's prompt and read the command lines of the Archiver processes; search `pakko.log` and the Application event log for every password the run used and for `Right-1` and `Wrong-1` | None of them appears anywhere; the note says how much of the log is from this run | T-F200 |
| APP-019 | store | user | tag | Drag files, a folder and a `.par2` file from Explorer into the window | Rows are added; the `.par2` row opens the archive it protects | T-F275 |
| APP-020 | store | user | tag | Every item of the tray icon's menu; left click | Open, About and Exit work; a left click hides and shows the window | T-F198 |
| APP-021 | store | user | tag | Change the display language with a sign-out, to en-US and to a regional variant | The App, the Shell dialogs and the Explorer menu follow it | T-F254 T-F91 |
| APP-022 | release | mcp | dev | Under uk-UA: watch the footer during a long operation; cause an error that comes from Windows (a name the file system refuses) | Speed and time left are in Ukrainian units; the error shows the Windows code and its text | T-F303 T-F297 |
| APP-023 | release | mcp | dev | Extract a mixed selection of a ZIP and a tar.gz | One progress that grows through both; the tar part shows "checking the archive" while it lists, not 0 % or 100 % | `app:Extract` T-F306 T-F307 |
| APP-024 | release | mcp | dev | With "move the archives to the Recycle Bin" on: cancel the extraction of a large tar.gz; extract an archive of which one entry is skipped | The archive stays in place both times and no partial output is left | `app:Extract` `app:Cancel` T-F245 T-F229 |

## OPW - the Explorer operation window

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| OPW-001 | quick | script | dev | `Archiver.Shell.exe --archive` on a folder of 6000 files of 16 KiB and a 200 MB file; read the window through UI Automation | The window is up within 3 seconds and shows a percentage and sizes; the archive holds 6001 files | `shell:--archive` T-F268 |
| OPW-002 | release | mcp | dev | The same, with Cancel pressed halfway | No archive and no temporary file is left | T-F268 |
| OPW-003 | release | mcp | dev | In the window: a name conflict (Rename, for all), a password prompt, a suspected bomb (Skip) | Each question is asked there and the answer takes effect | T-F217 |
| OPW-004 | release | script | dev | Kill `Archiver.OperationUi.exe` two seconds into a large operation | The Win32 progress dialog takes over and the operation finishes | T-F268 T-F288 |
| OPW-005 | release | mcp | dev | Titles, progress and result texts under uk-UA | All translated; sizes use the language's units | T-F268 |

## PAR - PAR2 recovery data

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| PAR-001 | quick | script | dev | `pakko a -rr5` on 200 MB; `t`; overwrite 8 MiB in the middle; `t`; `r` | Intact; then "damaged, can be repaired" with exit 2; the copy `name.repaired.zip` equals the original byte for byte and the damaged file is unchanged | `cli:r` `cli:-rr` `cli:t` T-F275 |
| PAR-002 | quick | script | dev | par2cmdline verifies Pakko's set and repairs with it; Pakko repairs from a set par2cmdline wrote | Both directions end with files equal to the original | `cli:r` T-F275 |
| PAR-003 | quick | script | dev | `Archiver.Shell.exe --recovery-verify` and `--recovery-repair` on an archive with a set and on its `.par2` file | The window reports the set intact; after damage, a repaired copy next to the archive | `shell:--recovery-verify` `shell:--recovery-repair` |
| PAR-004 | release | script | dev | par2cmdline-turbo and MultiPar (par2j) against Pakko's set; Pakko against a MultiPar set, with damage and a cut-off tail, into `-o<dir>` | Every tool accepts the others' sets | `cli:r` `cli:-o` |
| PAR-005 | release | script | dev | The failure paths: a damaged main file, a damaged volume, a volume missing (a half-written set), the set of another archive under this name, forged recovery blocks in packets whose hash is right, damage beyond the set | A distinct verdict for each; where repair is impossible no copy is kept; no crash and no hang | `cli:r` `cli:t` T-F275 |
| PAR-006 | release | script | dev | `pakko r` with the archive's folder closed for new files, without and with `-o<dir>`; hash the original before and after every repair in this section | Refused without `-o` with a hint at it, written with it; the original's hash never changes | `cli:r` `cli:-o` |
| PAR-007 | release | script | dev | `a -rr` for every format Pakko creates, and for an AES-256 ZIP; damage and repair each | Each gets `name.ext.par2` and one volume; the encrypted ZIP is repaired without its password | `cli:a` `cli:-rr` |
| PAR-008 | release | script | dev | Kill `pakko a -rr20` while it writes the set; cancel it with Ctrl+C; then `t` and a second `a -rr` | No file is left under a set's name and no verdict claims one; a second run with `-y` replaces the leftovers | `cli:-rr` T-F275 |
| PAR-009 | release | script | dev | Rewrite an archive that has a set with `pakko a -y` and no `-rr`, as ZIP and as tar.gz | The old set is removed, or `t` says it does not match; never "damaged beyond repair" for a good archive | `cli:a` T-F275 |
| PAR-010 | release | mcp | dev | The App: open an archive with a set, Test, damage it, Repair; add a `.par2` row and double-click it | The PAR2 line shows the files, then the verdict; Repair opens the repaired copy; the row opens the protected archive | `app:RepairBrowsedArchive` `app:TestBrowsedArchive` |
| PAR-011 | release | mcp | dev | The App's New archive card: "Add recovery data (PAR2)" at 5, 10 and 20 %, for a ZIP and a tar.gz | The set's size follows the percentage; `pakko t` reports it intact | `app:Archive` |

## POL - Group Policy

One elevated script sets each value in `HKLM\Software\Policies\Pakko`, runs the checks, and puts
the registry back in a `finally` block. The App reads policy once at startup: restart it per value.

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| POL-001 | release | uac | dev | `EnforceMOTW` at 0, 1 and 2, each with the user's own choice the other way: `pakko x` (with `-snz0` where the policy marks) and Shell extraction of a marked ZIP that holds a text file and a `.cmd` | 0: no file is marked; 1: both are; 2: the `.cmd` alone. `pakko` says that the policy decides | `policy:EnforceMOTW` T-F51 |
| POL-002 | release | uac | dev | `AllowedFormats` = `zip`: `pakko l`, `x`, `a` on tar, tar.gz, 7z and RAR and on ZIP; Shell extraction and "Add to tar" | The others are refused with "blocked by Group Policy" and nothing is written; ZIP works | `policy:AllowedFormats` |
| POL-003 | release | uac | dev | `BlockedFormats` = `zip`, and = `sevenzip` `rar`: `pakko l`, `t`, `x`, `a`; Shell extraction, test and "Add to tar" | The blocked formats are refused and nothing is written; the rest work | `policy:BlockedFormats` T-F262 |
| POL-004 | release | uac | dev | `DisableTarExtraction` = 1: `pakko l`, `x`, `a` on tar.gz, 7z and RAR; `pakko i`; Shell extraction and "Add to tar" | Every tar-family action is refused and nothing is written; `i` shows `tar.exe` disabled; the refusal names the archive, not the destination folder; ZIP works | `policy:DisableTarExtraction` T-F261 T-F326 |
| POL-005 | release | uac | dev | `DisableRecoveryData` = 1: `pakko a -rr5`, `t` on an archive with a set, `r` on a damaged one; Shell `--recovery-repair` | `a -rr` is refused and writes nothing, `t` says nothing of the set, `r` is refused; no repaired copy appears | `policy:DisableRecoveryData` T-F275 |
| POL-006 | release | uac | dev | `BlockedFormats` = `zip`: `pakko l` and `pakko r` on a damaged ZIP with a set | `l` is refused; `r` writes a copy equal to the original | `policy:BlockedFormats` `cli:r` |
| POL-007 | release | uac | dev | The Windows FIPS policy on (`FipsAlgorithmPolicy\Enabled` = 1): `pakko a -rr5`, `t`, `r`, `h`, and an AES-256 round trip | All work; MD5 for PAR2 is not refused | `cli:-rr` T-F275 |
| POL-008 | release | uac | dev | Last of the elevated rows: stop the helper and compare the exports of the two registry keys from before the first policy with those after | The exports are equal | `policy:EnforceMOTW` |
| POL-009 | release | mcp | dev | `Invoke-Smoke.ps1 -Tier release -Only POL-00* -Hold`: while each policy is held, open the App | `EnforceMOTW`: the box is locked at the policy's value; formats: the blocked ones are missing from New archive and refused on open; `DisableRecoveryData`: no option, no PAR2 line, no Repair, a `.par2` row is refused | `policy:EnforceMOTW` `policy:AllowedFormats` `policy:DisableRecoveryData` |
| POL-010 | release | mcp | dev | The same held run: open the Explorer menu on a ZIP, a tar.gz, a folder and a ZIP with a set and its `.par2` | Items of a blocked format are hidden; under `DisableTarExtraction` Add to X.tar is hidden; under `DisableRecoveryData` neither PAR2 item shows; under `BlockedFormats` = `zip` the ZIP has no PAR2 item and its `.par2` has | `policy:BlockedFormats` `policy:DisableTarExtraction` `menu:RecoveryRepairCommand` `menu:TarArchiveCommand` |

## PKG - the package and its life cycle

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| PKG-001 | quick | script | dev | For the four exes in the install folder: `AssemblyName.GetAssemblyName`; list the package's files | Each exe is native (the call throws); no `coreclr.dll`; file count and size within a tenth of the last record | T-F355 |
| PKG-002 | quick | script | dev | Read the Application event log (`Application Error`, `.NET Runtime`) from the run's start to its end | No entry names a Pakko exe | T-F355 |
| PKG-003 | quick | script | dev | `pakko -v` from the install folder; the file version of each of the four exes | `pakko -v` names the installed package's full name; every exe has a file version | T-F317 |
| PKG-004 | quick | script | dev | After the run, look in `%TEMP%` for `PakkoTarStage_*`, `PakkoTarSandbox` contents and `*.zip.tmp` | Nothing is left; no junction, hardlink or staging file of this run either | T-F309 T-F286 T-F244 T-F248 |
| PKG-005 | release | uac | dev | `appcert.exe test -apptype packagedwin32` on the `.msix` of the installed package; compare the tests that did not pass with `scripts/smoke/wack-known.txt` | No finding that the file does not list | T-F377 |
| PKG-006 | release | script | tag | With `-PackageLifecycle`: remove the package, install the previous release's bundle, create an archive with its `pakko`, write a marker into the package data and start the App once; then install the new bundle over it | The new version, the same package family; the marker and `pakko.log` are kept; the App starts; `pakko t` accepts the old archive; no crash in the event log | T-F363 |
| PKG-007 | release | script | tag | The same update with the App running: first without `-ForceApplicationShutdown`, then with it | Without it the update is refused as in use and nothing changes; with it the App is closed, the new version is installed and the App starts | T-F363 |
| PKG-008 | release | script | tag | Install the previous release's bundle over the new one | Refused as a lower version; the package is the same, `pakko -v` names it, and `pakko a` and `t` work | T-F368 |
| PKG-009 | release | script | dev | With `-PackageLifecycle`: start the App, remove the package, then install the dev package again | After the removal: no package, no `pakko` alias, no process, no package data. After the install: the package the run began with, and `pakko -v` names it | T-F361 |
| PKG-010 | release | mcp | dev | `Invoke-Smoke.ps1 -Tier release -Only PKG-009 -PackageLifecycle -Hold`: while the package is removed, open the Explorer menu on a file, a folder and a ZIP | No Pakko item in the menu or under "Show more options" | T-F361 |

## PRF - speed budgets on the installed build

Budgets are ratios to a yardstick run on the same machine in the same run, never bare seconds:
par2cmdline for PAR2, the vendored `7za` for ZIP. A run that takes under a second passes whatever
the ratio, since at that size both times are mostly process start. Where no other program does
the same work the bound is a loose absolute one (PRF-004) or the last release record (the package
size in PKG-001). The factors were set from a measured run (`docs/DECISIONS.md`, T-F378).

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| PRF-001 | quick | script | dev | `pakko a -rr5 -mx=0` on 200 MB against `par2 create -r5` on the same file | Under twice par2cmdline's time | `cli:-rr` T-F375 |
| PRF-002 | quick | script | dev | `pakko r` of 8 MiB of damage in 200 MB against `par2 repair` | Under three times par2cmdline's time (it patches in place; Pakko writes and checks a second file) | `cli:r` T-F375 |
| PRF-003 | quick | script | dev | `pakko a` and `pakko x` on 6000 files of 16 KiB against the vendored `7za` | Under three times 7-Zip's time for each | `cli:a` `cli:x` T-F114 |
| PRF-004 | quick | script | dev | `scripts/Measure-Startup.ps1 -Scenario App` on the installed App | The window is visible within a second of the start (median of three) | T-F347 |
| PRF-005 | quick | script | dev | `pakko l` and `pakko t` on a ZIP of 6000 entries against `7za l` and `7za t` | Under twice 7-Zip's time | `cli:l` `cli:t` T-F237 |

## REL - the release's files

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| REL-001 | release | script | dev | `dotnet test --filter "Category=Slow"` on the checked-out commit | Green; the note names the commit | T-F114 |
| REL-002 | release | script | dev | Read the jobs of the newest finished canary run on the checked-out commit; when there is none (the row is skipped), start one with `gh workflow run canary.yml --ref main` and run the row again | `canary-failed-day` skipped, `canary-fuzz` success; the ARM64 subprocess step passed | T-F240 |
| REL-003 | store | script | tag | `SHA256SUMS` against every file it lists; `gh attestation verify` for the bundles and the CLI zips | All match; the attestations are valid | T-F364 |
| REL-004 | store | script | tag | Signature, version and architecture of both `.msixbundle` files, named as the tag names them | Valid, signed by the dev certificate, the tag's version, one application package of the bundle's architecture | T-F129 |
| REL-005 | store | script | tag | The GitHub Release's notes | They begin with the tag's own section of `CHANGELOG.md` as it is at the tag | T-F240 |
| REL-006 | store | script | tag | The four `.cdx.json` files | Present, valid CycloneDX, the tag's version | T-F364 |
| REL-007 | store | script | tag | `scripts/New-WingetManifest.ps1` from the release's `SHA256SUMS`, `winget validate`, `winget install --manifest`, the installed `pakko -v`, `winget uninstall --manifest` | Valid; installs; the version is the tag's; the user's PATH holds the install folder; the uninstall removes the files. Until T-F361 it leaves its PATH entry: the note says so and the step puts the PATH back; nothing else in the PATH may change | T-F361 |

## STO - the Store

| ID | Tier | By | On | Action | Expect | Covers |
|---|---|---|---|---|---|---|
| STO-001 | store | script | store-bundle | Identity, publisher, signature, architectures and language count of the bundle of the newest successful dispatch of `build.yml` on the tag (the row reads the run; without one it is skipped: `gh workflow run build.yml --ref <tag>`) | The tag's version with revision 0, the Store publisher of `Setup-StoreCert.ps1`, signed by it, x64 and arm64, as many languages as `Strings/` has folders | T-F129 |
| STO-002 | store | script | store-bundle | Compare both inner `AppxManifest.xml` files with those of the Store bundle built for the previous tag | They differ in the version only. Any other difference fails the row: say why it is meant in the release record | T-F129 |
| STO-003 | store | script | store-bundle | `docs/store-listing/en-US.txt` at the tag against the previous tag; `StoreListingTests` | "What's new in this version" is not the previous release's text; the tests are green | T-F331 |
| STO-004 | store | user | store-bundle | Upload the bundle and the listing in Partner Center | Package validation passes; the submission is accepted | T-F129 |
| STO-005 | store | store-machine | store-install | After certification: update from the previous Store version | The new version, `SignatureKind: Store`, package data kept, the App starts | T-F129 |
| STO-006 | store | store-machine | store-install | CLI-002, SHL-001 and PAR-001 on the Store install | As those rows | T-F317 |
| STO-007 | store | mcp | store-bundle | Read the feature lines and "What's new" of `docs/store-listing/en-US.txt` against `CHANGELOG.md` since the version the Store serves | Every user-visible change since that version is named | T-F331 |
