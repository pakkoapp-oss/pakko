# CHANGELOG.md — Release History

Human-readable summary of what shipped in each tagged release. One section per version tag,
newest first. This file starts at `v1.4.1` — earlier releases (`v1.0.0`, `v1.1.0`, `v1.4.0`) are
not backfilled; see `docs/TASKS_DONE.md` and `git log` if you need that history.

Each entry lists the `T-Fxx` tasks completed since the previous release tag, in plain language —
not a re-statement of `docs/TASKS_DONE.md`'s full acceptance-criteria detail. See `docs/TASKS_DONE.md` for
the technical account of any task named here.

---

## v1.7.2 — 2026-10-09

Every Pakko program is now compiled ahead of time to native code: faster to start, a much smaller
download, and no .NET runtime inside the package. Archives made from a drive root are fixed, large
files are compressed on several cores, and Extract can leave the download mark off for one archive.

### Changed

- **T-F355** - the App, the Explorer commands, the operation window and `pakko` are Native AOT
  builds. Measured on x64: `pakko x` of a ZIP 133 -> 34 ms, Explorer "Extract here" ~170 -> 32 ms,
  App window ~590 -> ~430 ms; the MSIX shrinks from 63 to 15 MB and the CLI zip from 38 to 3 MB.
- **T-F363** - Windows App SDK 2.5.1 and the Windows SDK build tools 10.0.28000 in both WinUI
  programs.
- **T-F352, T-F357, T-F359** - creating a ZIP of a few large files uses several cores; a file that
  does not compress is copied once, not three times; on a hard disk the large files take turns
  instead of seeking against each other.
- **T-F358** - extracting many small files from a ZIP opens each file once instead of three times.
- **T-F347, T-F348, T-F350, T-F351, T-F356** - shorter start-up: the tar.exe check runs off the UI
  thread and only for a tar-family archive, and an Explorer command opens the operation window
  only when the operation is not over at once.

### Added

- **T-F360** - Extract has an "apply the download mark" checkbox for an archive that carries the
  mark, on by default; `pakko x -snz0` does the same. A Group Policy setting still wins.
- **T-F365** - each MSIX and CLI zip on the GitHub release comes with a CycloneDX SBOM and a build
  provenance attestation (`SECURITY.md` says how to verify them).

### Fixed

- **T-F344, T-F345** - a ZIP created from a drive root (`pakko a out.zip D:\`) has entry names
  Pakko and other tools can extract, and leaves out the drive's own system entries.
- **T-F285** - creating a tar archive from a `subst` drive root works.
- **T-F362** - a renamed archive of a compound extension is numbered before `.tar.gz`
  (`a (1).tar.gz`), not between `.tar` and `.gz`.
- **T-F314, T-F319, T-F324, T-F327, T-F341** - App: focus stays on the list after Enter opens a
  nested archive; an archive that fails to list keeps the folder you browsed from and says why in
  your language; hidden system folders at a drive root are not listed; the window background
  follows a theme change.
- **T-F339, T-F340** - operation window: the result text is read once by screen readers, and its
  colors follow a theme change.
- **T-F329** - byte sizes use the right plural form in languages where the unit changes with the
  number.

### Known issues

- The ARM64 build was checked by CI on an ARM64 runner, not on an ARM64 device.

---

## v1.7.1 — 2026-10-06

A data-integrity fix for password-protected ZIPs, and the command line says what it did: files it
kept, an archive that landed under another name, an encrypted 7z or RAR it cannot open.

### Fixed

- **T-F333** — a password-protected ZIP entry that decrypts to fewer bytes than its declared size
  is an error in Test and Extract. In v1.7.0 such a tampered or truncated entry tested as intact.
  The size and truncated-entry errors show in Ukrainian in Explorer and the App.
- **T-F316** — creating an archive inside the folder being archived (`pakko a out.zip .`) no
  longer reports an error about Pakko's own temporary file (ZIP) or packs it as an entry (tar).
- **T-F338** — `pakko a out.zip .` stores the folder under its own name (`proj/a.txt`), as 7-Zip
  does, not as `./a.txt`.
- **T-F322** — an encrypted 7z or RAR says that passwords are supported for ZIP archives only, in
  every language; `pakko` no longer suggests `-p` for it.
- **T-F335** — `pakko l` and the App's Modified column show an older entry of a tar, 7z or RAR
  archive as a date alone (`2020-02-03`): tar.exe gives no time for it, and midnight was shown.
- **T-F326** — a Group Policy or password refusal to create an archive names the archive, not the
  output folder.

### Changed behavior

- **T-F280** — extracting a ZIP whose local file headers disagree with its central directory
  warns: the files are extracted by the central directory, Explorer and the App show the warning,
  and `pakko x` prints `pakko: warning:` and exits **1** (was 0). Test already reported such an
  archive as an error.
- **T-F313** — `pakko x` of a ZIP over files that already exist names each file it kept, offers
  `-aoa`/`-aou` and exits **1**, as it already did for tar archives (was exit 0 and no output).
- **T-F325** — `pakko a` whose archive name was taken by another run during the write says where
  the archive landed (`pakko: warning: created 'name (1).zip': ...`) and exits **1** (was exit 0
  and no output).

### Tests

- **T-F337** — the encrypted ZIPs Pakko writes are checked byte by byte by a reader that shares no
  code with Pakko's own.

### Known issues

- **T-F326** — the corrected Group Policy refusal text was checked by tests only, not on a device
  with the policy set.

---

## v1.7.0 — 2026-10-05

`pakko` in the terminal right after a Store install (and through winget), extracted files keep
their dates, and a long list of fixes from the v1.6.0 device campaign: keyboard use of the App,
the command line's error messages, tar archives with Cyrillic names, and temporary files that a
sync client or a crash used to leave in the way. The translations were read through in all 36
non-English languages.

### New

- **T-F317** — `pakko` works in any terminal (cmd, PowerShell, Windows Terminal) as soon as Pakko
  is installed from the Microsoft Store: the package now carries the command-line program and a
  `pakko` command alias, sharing the app's .NET runtime (about 300 KB more). `pakko -v` names the
  installed package. The standalone command-line zip can also be installed with
  `winget install pakko-cli` (once the winget catalog accepts the package) — no manual PATH edit.
- **T-F298** — extracting a ZIP or tar-family archive restores every file's and folder's
  modified date from the archive, as 7-Zip does (NTFS or Unix time when the ZIP has one, else the
  DOS time). Earlier versions set the time of extraction.
- **T-F280** — Test reports a ZIP whose local headers disagree with its central directory (other
  programs may extract different names or data from such a file), as `7z t` does.
- **T-F307** — a tar, 7z or RAR extraction shows "Checking the archive's contents..." while Pakko lists it
  before extracting, instead of a progress bar that seems stuck at 0%.
- **T-F291** — Explorer's folder hash lists the entries it could not read.

### Changed behavior

- **T-F281** — compressing a whole drive names the archive after its letter (`D.zip`), not
  `archive.zip`.
- **T-F296 / T-F294 / T-F293** — `pakko` checks the meaning of its switches before it starts:
  `-ttar` with a `.zip` name, or a switch PowerShell split in two (`-ttar.gz` unquoted becomes
  `-ttar` `.gz`), is refused with the fix spelled out (exit 7); dot-free aliases such as `-ttgz`
  avoid the quoting. Hints are keyed on the cause, so a CRC failure no longer suggests `-aoa`.
- **T-F297** — an error that comes from Windows keeps its English text and code
  (`... (0x80070005)`) in every language, so it can be searched for; the Ukrainian interface adds
  a translation in front of it.
- **T-F301** — a password applied to the remaining archives that does not fit the next one says
  so, instead of reading as "no password given".

- **T-F330** — Explorer's menu and the operation window take the first language of your Windows
  language list that Pakko has, as the main window already did. Before, they followed the
  Windows display language, so a machine with an English display language and Ukrainian first
  in the list showed a Ukrainian app and an English menu. `pakko` stays English.
- **T-F323** — two messages give the count after a colon instead of before a noun ("Entries
  whose local header does not match the central directory: 1 (first: ...)", "More copies with
  this name were not extracted (2): ..."), so that every language can say them correctly. This
  changes `pakko`'s output in these two cases; a script that matches the old text needs
  updating.

### Fixed

- **T-F328 / T-F329** — about 600 translated strings corrected across the 36 non-English
  languages: ten that meant something else (Spanish and Greek said "one file" for "one
  archive"), and one word per action (extract, compress, test, scan) in the main window,
  Explorer's menu and the messages, where the three used to differ.
- **T-F299** — "Fastest" compression (the App's default, `pakko a -mx=1`) no longer makes
  already-compressed files about 5% larger: an entry that compression would grow is stored as is.
  This was a known issue of v1.6.0.
- **T-F310** — a gzip-compressed GNU tar with Cyrillic names in code page 866 lists and extracts
  with the right names.
- **T-F284** — extracting selected entries of a tar archive no longer treats names with `[`, `*`
  or `?` as patterns (`a[1].txt` used to extract `a1.txt`).
- **T-F312 / T-F263** — a temporary file left by a killed run, or held by Google Drive or a
  backup program, no longer makes the next archive creation fail; each run writes its own
  temporary file, and leftovers of dead runs are swept. Overwrite keeps the old archive until the
  new one is complete.
- **T-F286 / T-F309** — a crashed tar creation no longer leaves a link to your folder in `%TEMP%`;
  a refused or cancelled extraction leaves no empty destination folder behind.
- **T-F306** — extracting a mix of ZIP and tar-family archives in the App shows one progress bar
  across both.
- **T-F236** — removing a large folder from the App's list stops measuring its size at once.
- **T-F308 / T-F304** — the App works from the keyboard: Enter and Backspace in the archive view,
  Delete and Shift+F10 in the list, Tab through the footer in screen order, focus back on "About"
  after its dialog; two controls gained screen-reader names.
- **T-F220 / T-F303 / T-F302 / T-F282** — smaller App fixes: the conflict dialog shows both
  files' size and date and can cancel the whole operation; the sorted column shows its direction;
  speed and time left follow the interface language; files moved to the Recycle Bin leave the
  list; the option cards scroll instead of being cut off in a low window.
- **T-F295** — console polish: Ctrl+C prints its message on a clean line, the password prompt
  says the input is masked, the overwrite prompt shows both files' size and date.

### Known issues

- **T-F320** — a command run from the Store alias that creates a *new folder directly under*
  `%LOCALAPPDATA%` or `%APPDATA%` (e.g. `pakko x a.zip -o%LOCALAPPDATA%\New`) writes into the
  package's private copy of that folder, which other programs do not see. Existing folders,
  their subfolders, `%TEMP%` and every other location are not affected; the standalone zip is not
  affected.
- **T-F322** — for a password-protected 7z or RAR archive, `pakko` suggests `-p`, but Pakko cannot
  decrypt 7z or RAR archives at all (only ZIP).
- **T-F315** — Explorer's operation window sometimes shows all black until it is clicked.
- **T-F254** — the Explorer menu and Explorer's dialogs for a regional Windows language
  (e.g. German (Austria), Chinese (China)) are fixed in code and covered by tests, but were not
  checked on such a Windows; the main window was checked.
- **T-F330** — the language a user sets for Pakko alone in Windows 11 Settings is followed by the
  main window, but not by Explorer's menu or the operation window.

### Under the hood

- **T-F329** — a glossary of ten terms per language, checked by tests against every translated
  string.
- **T-F331 / T-F332** — the Microsoft Store listing text is kept in the repository
  (`docs/store-listing`, 37 languages) and checked by tests; the listing gains Croatian, Serbian
  (Latin), Slovenian, Urdu and Vietnamese.
- **T-F288** — the last two COM interfaces moved to source-generated COM (`GeneratedComInterface`).
- **T-F292 / T-F111 / T-F112** — the main window's mode diagram is redrawn and checked by tests,
  and the Archive Browser's "Up" decision moved into a tested class.

---

## v1.6.0 — 2026-09-30

A redesigned main window, a Pakko window for Explorer's commands, messages in all 37 languages, a
security fix for tar archive creation, and a move to .NET 10. Please update: the security issue
below affects every earlier version.

### Security — please read

- **T-F283** — creating a `.tar` archive (Explorer "Add to X.tar", the App's TAR format,
  `pakko a -ttar`) passed every selected name to tar.exe on its command line, and tar.exe runs
  outside the sandbox when it creates an archive. A file or folder whose name looked like a
  tar.exe option was read as one: a crafted name could change what tar.exe did, up to running
  another program as you. tar.exe now receives the names as a list on its standard input, where
  a name is only ever a name; extracting selected entries of a tar-family archive puts `--`
  before the entry names for the same reason. The fix also removes the limit on how many files
  one tar archive can be created from (**T-F273**: a large selection used to fail).
- **T-F237** — an archive entry nested deeper than 256 folders is now refused as an unsafe path
  (for ZIP that entry, for tar-family the whole archive, before tar.exe runs). A crafted archive
  with thousands of levels used to keep Pakko busy for minutes in a step that could not be
  cancelled, and deep names cost gigabytes of memory in the App's archive view (now linear in
  the name's length).

### New

- **T-F199** — the main window is redesigned: its own title bar, option cards, and a footer that
  holds the main action (**"Compress to ZIP"** / "Compress to TAR" — the button used to say
  "Archive"), the encryption password (the separate Encrypt dialog is gone), and the result of
  the last operation with "Show in folder" and "Details" (**T-F211**). Browsing an archive gets
  Test, Close archive (Esc), an encryption badge and lock icons on encrypted entries. The window
  fits smaller screens (**T-F224**); Extract is offered only when the list holds archives
  (**T-F212**).
- **T-F268 / T-F269** — Explorer's commands (extract, compress, test, scan, hash) show a Pakko
  window with progress, the conflict and password questions, and the result, instead of the old
  Windows progress dialog and message boxes; the old dialogs remain as a fallback. Cancel stops
  the whole selection, not only the current archive.
- **T-F209 / T-F254** — error and skip messages from the archive engine are shown in the App's
  and Explorer's language (37 languages; the command line stays English). Explorer's menu now
  follows Chinese (zh-CN) and regional variants of Windows' language instead of falling back to
  English.
- **T-F217** — Explorer's extract commands ask before extracting a suspected compression bomb
  (No is the default) instead of refusing with no way forward.
- **T-F214** — tar, 7z and RAR archives show each entry's modified date in the App and in
  `pakko l`.
- **T-F219** — the App's Hash button hashes the files in the list and can copy the result
  (SHA-256).
- **T-F200** — browsing a password-protected ZIP asks for the password once per archive, not
  for every previewed file.
- **T-F238** — `pakko -scc{UTF-8|WIN|DOS}` sets the character set of its output, so redirected
  output can keep every file name exactly (7-Zip's switch).
- **T-F221** — `pakko l` has an `Encrypted` column; `pakko` shows progress on a console and
  says what went wrong and how to go on (missing file, wrong password, existing files, empty
  input).

### Changed behavior

- **T-F206** — `pakko x` without `-o` extracts into the current directory, like `7z x` (it
  used to extract next to the archive).
- **T-F221** — `pakko a -t<type> name.ext` writes exactly the name given (7-Zip's rule); only a
  name with no extension gets one added. `pakko l`'s `Path` column moves from column 6 to 7,
  after the new `Encrypted` column, and **T-F214** prints `-` instead of `0` in the Compressed
  column for tar, 7z and RAR. Scripts that parse `pakko l` need updating.
- **T-F276** — Explorer's window title and two command-line messages say "compress" instead of
  "archiving" ("...are not followed during compression.", "Unknown error while compressing.").
- **T-F171** — a tar, 7z or RAR archive holding two entries with the same name used to extract
  only the last one, silently. Now the "if file exists" setting decides, as for ZIP: Rename keeps
  both (`f.txt` is the first, `f (1).txt` the last), Skip keeps the first, Overwrite the last, Ask
  asks; a third copy or later is reported, not extracted. Creating a tar from two folders with
  the same name stores the second as `x (1)`.
- **T-F250 / T-F261 / T-F262** — Group Policy now also applies to listing and browsing an
  archive and to Test and Scan, and Explorer's menu hides the commands a policy blocks.
- **T-F222** — a `pakko` build that is not a release reports `0.0.0-dev+<commit>` as its
  version.

### Fixed

- **T-F235** — a very large Explorer selection no longer makes Pakko's commands silently do
  nothing (the known limit in v1.5.0's notes).
- **T-F305** — a GNU-format tar with Cyrillic names in code page 866 was refused as a whole
  since v1.5.0; it lists and extracts again.
- **T-F279** — a cancelled ZIP Test no longer reports "no errors found"; **T-F274** — Explorer's
  Test no longer says "no errors detected" when nothing was tested.
- **T-F236** — one unreadable subfolder no longer aborts creating the whole archive.
- **T-F251 / T-F225** — folder hashes survive unreadable subfolders and junction loops, and the
  "data and names" checksum now matches 7-Zip's.
- **T-F247** — "Scan for threats" no longer fails on an archive that contains an empty file.
- **T-F201 / T-F252** — a second Pakko window opens offset from the first, and closing one window
  no longer deletes another window's preview files.
- **T-F253 / T-F255** — Explorer's conflict dialog opens in front and names the file in full;
  its password dialog no longer cuts passwords at 255 characters.
- **T-F216** — no "every entry was skipped" warning after you chose Skip yourself.
- **T-F244** — `pakko -si`/`-so` clean up their temporary files on Ctrl+C and after a crashed
  run.
- **T-F218** — the title bar's build time is the time the app was built (not installed); a Store
  build shows none.
- **T-F277 / T-F278 / T-F198** — smaller App fixes: a cancelled browse-mode Test says
  "Cancelled", adding a file already in the list says so, no English words left in Ukrainian and
  other localized lists and buttons.

### Known issues

- **T-F299** — at the "Fastest" level (the App's default, `pakko a -mx=1`), already-compressed
  files (photos, video, ZIPs) come out about 5% larger than the originals — a change in .NET 10's
  fastest compression. No data or compatibility risk; other levels are not affected. Choose
  another level for such files until this is fixed.
- **T-F298** — ZIP extraction sets each file's date to the time of extraction, not the date
  stored in the archive (every earlier version does the same).

### Under the hood

- **T-F270 / T-F271** — every project moved to .NET 10 LTS (.NET 8 support ends 2026-11-10);
  most of the slowdown it caused when compressing many small files is recovered, the rest is in
  the runtime and reported upstream (dotnet/runtime#134700).
- **T-F148 / T-F287** — every Windows API call uses source-generated marshalling
  (`LibraryImport`).
- **T-F240** — a dependency-free ZIP fuzzer, nightly Slow, ARM64 and AddressSanitizer runs,
  NuGet vulnerability audit and Dependabot.
- **T-F257 / T-F258 / T-F223** — security, convention and diagram documents re-checked against
  the code.

---

## v1.5.0 — 2026-09-26

Password-protected ZIP support (open, test, scan and create), three security fixes, and a large
correctness pass over extraction found by a full UI smoke test and an architecture review. Please
update: two of the security issues below affect every earlier version.

### Security — please read

- **T-F232** — the `pakko://` link scheme is removed. In earlier versions a web page, e-mail or
  document could use a crafted `pakko://` link to make Pakko open an arbitrary path, including a
  network (UNC) path on a remote host — which makes Windows send your NTLM credentials to that
  host. Browsers ask before opening such a link, but offer "always allow". Explorer's commands
  that open the Pakko window (Open, Extract…, Compress…) work exactly as before; they now reach
  the app through a hand-off only a program already on your computer can start. Updating removes
  the registration; nothing needs cleaning up. Known limit, still open (T-F235): a very large
  Explorer selection (tens of thousands of characters of paths) can still make a Pakko command
  do nothing.
- **T-F233 / T-F248** — opening a tar-family archive (.tar, .gz, .7z, .rar, ...) in earlier
  versions changed that file's permissions: it added an entry for Pakko's sandbox and replaced the
  permissions the file inherited from its folder — on a shared folder, other people could lose
  access to it. tar.exe now reads the archive only through a handle Pakko opens itself; your file
  is never touched. Nothing is repaired automatically:
  [Find-PakkoSandboxAce.ps1](https://github.com/pakkoapp-oss/pakko/blob/v1.5.0/scripts/Find-PakkoSandboxAce.ps1)
  lists affected files and
  [Repair-PakkoSandboxAce.ps1](https://github.com/pakkoapp-oss/pakko/blob/v1.5.0/scripts/Repair-PakkoSandboxAce.ps1)
  restores them (see
  [SECURITY.md](https://github.com/pakkoapp-oss/pakko/blob/v1.5.0/SECURITY.md#advisory-permissions-changed-by-earlier-versions-t-f233),
  "Advisory: Permissions Changed by Earlier Versions").
- **T-F266** — a file name with certain look-alike Unicode characters (for example U+FF02, a
  full-width quote) could be turned into real tar.exe options when creating a `.tar` archive.
  Every name passed to tar.exe must now convert exactly to the system's ANSI code page; any other
  name is refused before tar.exe runs, with a message pointing to ZIP (see "Changed behavior").
- **T-F185** — an archive name like `..\..\name` could write the new archive outside the chosen
  folder. The name is now used as a plain file name; as a side effect, the App's Archive Name box
  no longer creates a subfolder from `sub\name`.
- **T-F228 / T-F246 / T-F231** — a crafted ZIP entry could overwrite an existing file despite the
  chosen conflict option; corrupted ZIP entries (bad CRC-32 or size) were written and reported as
  success; decrypted entries were not capped at their declared size. All three now fail per entry,
  and the rest of the archive still extracts.

### New

- **T-F188 – T-F192** — password-protected ZIP archives (ZipCrypto and WinZip AES) now open,
  extract, test and list in the App, in Explorer and in `pakko` (`-p{password}`, or a masked
  prompt). **CLI change:** a bare `-p` on `x`/`t` used to be an error (exit code 7); it now asks
  for the password interactively.
- **T-F193** — create password-protected ZIP archives, WinZip AES-256 only: an "Encrypt" option in
  the App, `pakko a -p` / `-mem` on the command line.
- **T-F194** — "Scan for threats" now looks inside password-protected ZIP entries (it asks for the
  password); without one the result is "Inconclusive", never "Clean".
- **T-F160** — `pakko x` on an interactive console asks what to do when a file already exists,
  with 7-Zip's own prompt (`(Y)es / (N)o / (A)lways / (S)kip all / A(u)to rename all / (Q)uit?`).
  Scripted and redirected runs keep the silent Skip.
- **T-F207** — "Delete after operation" now moves sources to the Recycle Bin, and asks before
  deleting permanently anything the Recycle Bin can't take (network drives and similar).

### Changed behavior

- **T-F205** — Explorer's "Extract here" and `pakko x` keep an archive's single root folder
  instead of flattening it; "Extract to name\" removes the root folder only when it has the
  archive's own name (like NanaZip). The App's own Extract is unchanged.
- **T-F197** — empty folders inside ZIP and tar archives are now extracted.
- **T-F234** — ZIP archives made by older tools with non-UTF-8 names (for example Cyrillic in
  code page 866) now show correct names instead of garbled ones, using 7-Zip's rules; before,
  two such entries could silently overwrite each other.
- **T-F204 / T-F215** — tar-family names are decoded correctly, and a name the system code page
  can't represent gives a clear error instead of garbled text.
- **T-F266** — creating a tar-family archive now refuses files whose names the system's ANSI code
  page can't represent exactly (for example Chinese names or emoji when Windows' language for
  non-Unicode programs is Ukrainian). Use ZIP for such files; ZIP stores any name.
- tar.exe's sandbox now allows half of the computer's memory (1 – 4 GB) and at least 60 minutes of
  CPU time plus one minute per 10 MB of archive, so large modern archives (big 7z/xz/zstd
  dictionaries) no longer fail; when a limit does stop it, the message says which (**T-F239**).
- A tar-family archive that another program has open for writing is refused as "in use".

### Fixed

- **T-F229 / T-F265 / T-F245** — "Delete after operation" could delete an archive whose entries were
  partly skipped, the whole archive after "Extract Selected", or an archive whose tar extraction
  was cancelled. Now only fully processed sources are deleted, after the summary is shown.
- **T-F227** — ZIP extraction reused and then deleted an existing `<destination>_tmp` folder.
- **T-F230** — one unwritable ZIP entry no longer aborts the whole extraction with a misleading
  message.
- **T-F243 / T-F244** — ZIP reader hardening: a wrong ZipCrypto password that passed the quick
  check is now asked again; Cyrillic ZipCrypto passwords from other Windows tools work; reserved
  device names
  (`CON.a.b`, `NUL`, `CONIN$`) are refused in every part of a path; over-long names fail only
  their own entry.
- **T-F263** — tar extraction no longer leaves partial files behind when cancelled or when it
  fails.
- **T-F249** — RAR encryption checks read only a small header window, and a crafted archive can no
  longer make them loop forever.
- **T-F195 / T-F196** — tar archives made with `tar -C dir .` failed to extract and browse;
  concurrent tar operations could fail to set up the sandbox.
- **T-F170** — a destination file locked by another program no longer aborts the whole
  extraction.
- **T-F168** — creating a tar archive from two sources with the same file name now stores the
  second one under a new name, as ZIP creation already did, instead of two entries with one name.
- **T-F164** — the App's Hash dialog now uses the same hashing code as Explorer and the CLI.

### Under the hood

- **T-F166 – T-F170, T-F174 – T-F186** — a QA/AppSec test-coverage audit: real junctions, AES-256 fixtures, tar
  cancellation, COM entry points, sandbox concurrency, Group Policy fuzzing, an end-to-end EICAR
  test, MAX_PATH, format spoofing, Unicode-trick file names.
- **T-F172 / T-F173** — a developer/API documentation site
  (https://pakkoapp-oss.github.io/pakko/dev/) with complete XML documentation.
- **T-F187** — a daily "canary" CI build that warns about toolchain changes before they break a
  release.

---

## v1.4.12 — 2026-08-11

Localization follow-up to v1.4.11's new conflict dialog, plus a CI stability fix.

- **T-F163** — Explorer's "Extract Here"/"Extract to"/"Extract" summary dialogs (shown after a
  Skip, or after any error) were hardcoded English regardless of the active Windows UI language —
  found by a user running under Ukrainian right after using v1.4.11's new conflict dialog. Now
  localized across all 37 supported languages, matching every other native dialog in this app.
- **T-F162** — Fixed intermittent CI test failures in two archive-extraction progress-reporting
  tests, caused by a background-thread timing race under heavy CI load (not a real product bug).

---

## v1.4.11 — 2026-08-11

Extraction-conflict release — a new interactive conflict dialog for the Explorer extraction
commands, two wrapping-behavior corrections, a shared decision-table refactor, and a crash fix
found via real use of the new dialog on the same day it shipped.

- **T-F154** — extracting an archive created from a single file used to land it inside a
  same-named wrapper folder (`<Destination>\photo\photo.png`) instead of directly
  (`<Destination>\photo.png`) when using the App's default Extract button or Explorer's "Extract
  Here." Now matches a real archiver's behavior.
- **T-F156** — "extract to one flat folder" mode still wrapped a genuinely multi-root archive
  (several files, no common folder) in a subfolder, contradicting its own "no wrapper, ever"
  contract. Fixed to never wrap in this mode; the per-archive Explorer "Extract Here" mode still
  wraps, unchanged.
- **T-F157 / T-F158** — the destination-conflict and smart-foldering decisions that
  `ZipArchiveService` and `TarSandboxedService` used to hand-duplicate (and had to keep manually
  in sync across T-F154/T-F156 in a single day) are now two shared, tested decision tables
  (`ExtractionDestinationPlanner`, `DestinationConflictResolver`) — a pure refactor, no behavior
  change.
- **T-F155** — `Archiver.Shell`'s three Explorer extract commands now show a real interactive
  Overwrite/Rename/Skip + "apply to all" conflict dialog, matching the WinUI app's own dialog.
  Previously these commands resolved conflicts silently with no prompt at all.
- **T-F161** — found via real use of T-F155 the same day it shipped: extracting an archive with
  files plus a folder, choosing Rename + "apply to all" on a conflict, could crash with an
  "Access is denied" error on a temporary staging folder — files extracted, but the archive's own
  folder and the operation as a whole did not complete. Caused by a Windows filesystem behavior
  where a whole-folder move fails outright if any single file inside is briefly locked by another
  process (antivirus, cloud sync, Search Indexer); now falls back to moving files individually
  when that happens, and no longer leaves a stray temp folder behind on any other failure either.

---

## v1.4.10 — 2026-08-11

Bug-fix release — a real archive-creation defect found during a broad pre-Store-submission
smoke test.

- **T-F153** — a source path ending in a trailing directory separator (typed or tab-completed in
  a terminal — never produced by the GUI's own folder picker or drag-and-drop) silently corrupted
  archive creation two ways: entries were written without their real parent folder name, and the
  Explorer "Add to X.zip" one-click command (and the equivalent CLI switch) placed the new archive
  **inside the folder being archived** instead of next to it, naming it generically instead of
  after the real source. Both are now fixed at the source.

---

## v1.4.9 — 2026-08-10

Small follow-up to v1.4.8's antivirus scanning — raises the per-entry size limit and improves
progress feedback for large scans.

- **T-F151** — the "Scan for threats" size cap was raised from 64 MiB to 256 MiB per entry. An
  empirical spike compared AMSI's real disk-streaming mechanism (`IAmsiStream`) against the
  simpler, already-shipped buffer-based call against the actual registered antivirus provider —
  the streaming approach turned out to fail above ~16-20 MiB in practice, while the existing
  method scanned real content up to 256 MiB with no error, so the existing mechanism was kept and
  its limit raised instead. Scan progress now also shows the name of the specific file being
  scanned (not just the containing archive), since a single large entry's scan can now take
  several seconds.

---

## v1.4.8 — 2026-08-10

Security feature + code-quality release — AMSI-based threat scanning for archives, a large
SonarCloud triage/refactor pass, and mandatory static-analysis gating for every language in the
repo.

- **T-F146** — new "Scan for threats" action for archives (Explorer context menu + Archive
  Browser), via AMSI (`amsi.dll`) — works with whatever antivirus/EDR is registered on the
  machine, no elevation required. ZIP entries scan entirely in memory; tar-family archives reuse
  the existing AppContainer sandbox up through the quarantine stage only (no extraction to
  destination). Reports `Inconclusive` rather than a false "clean" when no AMSI provider is
  registered. Localized across all 37 locales. Two same-day follow-up fixes: the Scan button's
  enabled state wasn't being re-evaluated (stayed permanently disabled), and the button was moved
  next to About instead of crowding the Extract Selected/Extract All pair — it's a diagnostic
  action, not a primary one.
- **T-F147** — triaged the SonarCloud findings backlog (134 issues) down to 44 (42 deferred as
  their own task, 2 accepted TODOs) — every real cognitive-complexity hotspot was refactored,
  including `ZipArchiveService.ArchiveAsync` (was the single highest-complexity method in the
  whole report). Also fixed a temp-folder cleanup bug found the same week: a hidden per-operation
  chunk folder was regularly left behind, empty, after archiving, due to an unretried delete
  losing a transient file-lock race (antivirus/cloud-sync/Search Indexer) — now retries.
- **T-F149** — raised automated test coverage on new code to clear SonarCloud's Quality Gate
  (86.3%, comfortably above the 80% threshold) by excluding a handful of files that are
  structurally unreachable by the test runner (real Explorer-launching side effects, a spawned
  child process's own entry point) rather than writing tests that could only exercise a mock.
- **T-F150** — static analyzers and linters now run on every build, for every language in the
  repo (C#, C++, PowerShell), each gated to fail the build/CI on any new, undocumented finding.
  Found and fixed 2 real bugs along the way: two COM entry points in the shell extension were
  missing SAL annotations the Windows SDK declares for them, and a test helper was silently
  ignoring a COM initialization failure. Also fixed 4 PowerShell deploy/build scripts that were
  missing a UTF-8 byte-order mark, the same encoding-corruption risk class an earlier release
  (T-F84) had already been bitten by once.

---

## v1.4.7 — 2026-08-04

Feature + bug-fix release — real progress reporting for archive creation and extraction, plus a
live speed readout.

- **T-F129** — Pakko's Microsoft Store listing is now live
  (https://apps.microsoft.com/detail/9p5mw010d8pr) — certification passed and the listing was
  confirmed genuinely public via a real `winget install --source msstore`, followed by a
  functional smoke test (test/extract/archive, including `.7z`/`.rar` through the AppContainer
  sandbox) against that exact Store-installed package.
- **T-F140** — fixed archive-creation progress reporting for both ZIP and TAR, found from a real
  user report of a frozen-looking dialog on a large multi-folder source. ZIP's parallel
  compression pipeline was passing no progress at all into its temp-file path; TAR's percent was
  computed from the wrong (too-small) denominator. Both now report real, live bytes and the
  current filename during compression.
- **T-F141** — hardened `ParallelSingleArchiveWriter`'s chunk-temp-file handling: the writer
  thread's read-back of a finished chunk requested needless exclusive access, so a transient
  external file lock (antivirus, cloud-sync client, Search Indexer) could abort an entire archive
  operation instead of just one file. Narrowed to a shared read.
- **T-F142** — TAR extraction now reports real byte progress instead of always showing 0 bytes
  (`ITarService.ExtractAsync` gained the same `IProgress<ProgressReport>` contract ZIP already
  had), and both the app's status line and the Explorer right-click progress dialog now show a
  live compression/decompression speed readout, backed by a new shared, tested speed-sampling
  helper.

---

## v1.4.6 — 2026-08-01

Bug-fix release — restores full localization to the shipped MSIX. Affects `v1.4.5` (and likely
earlier CI-built releases) too: only English shipped despite 37 supported locales.

- **T-F139** — the MSIX packaging pipeline's default auto-resource-package split
  (`AppxBundleAutoResourcePackageQualifiers=Language|Scale|DXFeatureLevel`) tried to carve each
  locale's resources into its own resource-only package, then silently failed to actually produce
  them — the shipped package ended up with only `en-US`, even though every locale was correctly
  detected during the build. Confirmed on a truly clean build (not just CI) via
  `docs/DECISIONS.md`'s T-F139 entry. Fixed by dropping `Language` from the auto-split qualifier
  list, so all 37 locales are embedded directly in the single package again, matching the
  pre-regression shape. If you installed `v1.4.5` (or possibly earlier), update to this release to
  restore non-English UI.

---

## v1.4.5 — 2026-08-01

CI/tooling release — no user-facing app changes. Builds Pakko's first Microsoft Store submission
pipeline (T-F129).

- **T-F129** — added `build-store-msix` and `bundle-store-msix`, two new `workflow_dispatch`-only
  CI jobs that produce a single, correctly-identified multi-architecture (`x64`+`arm64`) MSIX
  bundle signed with Pakko's reserved Partner Center Publisher identity — distinct from the
  tester-facing `CN=Pakko Dev` cert used everywhere else. Fixed four real, previously-undiscovered
  MSIX/Partner Center packaging quirks along the way (Publisher gets silently rewritten to match
  the signing cert at build time; a `.msixbundle`'s own identity carries no architecture, so
  separate single-arch bundles collide; a re-signed bundle needs its cert trusted, not just
  present, for signature verification to read `Valid`; Partner Center's package-identity
  uniqueness check is scoped to the developer account and survives submission deletion). First
  real Partner Center upload of the resulting package succeeded 2026-08-01. See
  `docs/TASKS.md`'s T-F129 entry for the full trail.

---

## v1.4.4 — 2026-08-01

Security-hardening and CI/tooling release — no user-facing feature changes.

- **T-F135** — SonarCloud static analysis wired into CI (`build.yml`'s `test` job), with code
  coverage fed into the quality gate. Real analysis confirmed against the live SonarCloud
  dashboard, not just a green Actions run.
- **T-F136** — All 269 findings from SonarCloud's first scan individually triaged: real bugs
  fixed, false positives suppressed with a documented reason, genuine refactor-scale debt tracked
  as new follow-up tasks rather than silently dropped.
- **T-F137** — Local static-analysis tooling (SonarLint-equivalent analyzer config) added so the
  same rule set SonarCloud enforces in CI is visible during local development too.
- **T-F138** — Fixed the real defect behind SonarCloud's `S3869` findings: several
  `SafeHandle.DangerousGetHandle()` calls in the AppContainer sandbox's P/Invoke layer
  (`QuarantineAcl`, `SecurityCapabilitiesAttributeList`, `SandboxedProcessLauncher`) are now
  `SafeHandle`-typed parameters instead of raw handle extraction, closing a handle-recycling race
  window during sandboxed `tar.exe` launches. Confirmed via SonarCloud: `bugs: 0`,
  `reliability_rating: A`.
- Dependency hygiene: `CommunityToolkit.Mvvm` version synced between `Archiver.App` and
  `Archiver.App.Core`; `System.Net.Http`/`System.Text.RegularExpressions` pinned to patched
  versions in test projects.
- CI fixes: explicit workflow-level permissions on `build.yml`, cross-platform NuGet restore
  enabled, `global.json` SDK version corrected for `setup-dotnet`, Dependabot reverted to
  security-only updates.

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
