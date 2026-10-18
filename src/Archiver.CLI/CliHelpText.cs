namespace Archiver.CLI;

/// <summary>
/// Hand-written --help text (not generated from CLI.md's markdown at runtime — parsing markdown
/// would need a new dependency, and Core/CLI both stay at zero NuGet packages). The tradeoff is
/// manual sync with CLI.md; CliHelpTextTests guards the -mx bucket boundaries against
/// CliCompressionLevelMapper's real output so those two can never silently diverge.
/// </summary>
public static class CliHelpText
{
    public const string Text = """
        Pakko CLI — 7z-familiar commands over Pakko's Archiver.Core (not a drop-in replacement)

        USAGE:
          pakko <command> [switches] <archive> [files...]
          pakko -h | --help        Show this help text
          pakko -v | --version     Show version number

        COMMANDS:
          x   Extract archive(s) with full paths        [ZIP, tar-family]
          t   Test archive integrity                    [ZIP; any archive
                                                         with PAR2 files]
          r   Repair from PAR2 recovery data: writes    [any archive with
              <name>.repaired<ext>, the archive stays    PAR2 files]
          l   List archive contents                     [ZIP, tar-family]
          a   Add files to a new archive                [ZIP, 7z, tar-family]
          h   Hash files, or one folder recursively      [CRC-32, SHA-256]
          i   Show supported formats/codecs on this system

        SWITCHES:
          -o<dir>          Output directory; default: the current directory,   (x, r)
                           as with 7z (not the archive's own folder). r: where
                           the repaired copy goes; default: next to the archive
          -p<pwd>          Password. x/t: opens a ZipCrypto/AES-encrypted ZIP.  (x, t, l, a)
                           a: encrypts the new ZIP with AES-256 (file names
                           stay readable); printable ASCII only, at most 99
                           characters, so 7-Zip can open it. A bare -p asks
                           with masked input (twice for 'a') on a real
                           interactive console. Without -p on an encrypted
                           archive, x/t prompt the same way, or fail
                           immediately when piped/scripted/-y is given.
                           l: accepted and not needed. -p- is the password "-"
          -mem=AES256      Encryption method: accepted for 7z compatibility;   (a)
                           AES-256 is the only one (ZipCrypto/AES128/AES192
                           are refused)
          -y               Assume yes: auto-overwrite conflicts, auto-confirm
                           compression-bomb warnings. Without -y, a bomb warning
                           is declined; a file conflict prompts on a real
                           interactive console (Y/N/A/S/U/Q, like 7-Zip) and is
                           skipped when piped/scripted.
          -ao{a|s|u|t}     Overwrite mode: a=overwrite, s=skip, u=auto-rename   (x)
                           the new file, t=auto-rename the existing one
          -snz[0|1]        Download mark: -snz/-snz1 (default) copies the       (x)
                           archive's "from the internet" mark to every file;
                           -snz0 leaves it off. Group Policy overrides both
          -spe             Drop the archive's root folder when it is named     (x)
                           like the last part of -o (-oout\src: src's content
                           goes straight into out\src)
          -t<type>         Archive type: zip (default), 7z, tar, tar.gz,
                           tar.bz2, tar.xz, tar.zst, tar.lzma; or tgz, tbz2,
                           txz, tzst                                            (a)
                           A name ending in one of these types, or in .tgz,
                           .tbz2, .txz, .tzst, must match it (out.tar.gz needs
                           -ttar.gz, out.7z needs -t7z); any other extension
                           is written as typed. 7z is written by Windows' own
                           tar.exe, where it can, and never with a password
          -mx[=]<0-9>      Compression level — see table below; -mx is 9,      (a)
                           a level above 9 is 9
          -m0=<method>     Method of a 7z archive (with -t7z): lzma2 (default), (a)
                           lzma, bzip2, deflate, copy
          -rr[N]           Recovery data: PAR2 files next to the archive,      (a)
                           N percent of it, 1-100; bare -rr is 5. par2cmdline
                           and MultiPar can verify and repair with them too
          -mmt[=N|on|off]  At most N files compressed at once (ZIP)            (a)
          -sse             Write no archive when a file cannot be read (ZIP)   (a)
          -ssw             Also read files open for writing (ZIP)              (a)
          -stl             The archive takes its newest file's time            (a)
          -sdel            Delete the sources once archived: files unchanged   (a)
                           since they were listed, then empty folders; never
                           a link, a volume root or the current folder
          -w[dir]          Working folder: accepted; it must exist             (a)
          -scrc<method>    Hash method: CRC32 (default) or SHA256              (h)
          -si              Read the archive from stdin instead of a path       (x, t, l, h)
          -so              Write output to stdout instead of disk              (x, a)
          -sae/-sas/-saa   -sae: the archive is named exactly as typed;        (a)
                           -sas (default): a name without an extension gets
                           the type's; -saa: the type's is always added
          -bb[N]           Log level, -bb is 1: from 1, x prints "- <name>"    (all)
                           for each folder and file it wrote, relative to -o
          -bd              No percentage                                       (all)
          -bt              Time statistics after the command, as 7-Zip's       (all)
          -ba              l prints the entry rows only, no header             (l)
          -bso{0|1|2}      Where messages (-bso), errors (-bse) and the        (all)
          -bse, -bsp       percentage (-bsp) go: 0 nowhere, 1 stdout, 2 stderr.
                           Under -so stdout carries the data, so messages go
                           nowhere unless -bso2
          --               Every argument after it is a name, even one that    (all)
                           starts with '-' (patool passes it)
          <arc> <names>    Names after the archive filter the entries:         (x, l, t)
                           as in 7-Zip: a folder takes what is in it; @<file>
                           reads the names from a list file. Nothing matches:
                           'No files to process', exit 0
          a <arc> <names>  Sources, as in 7-Zip: dir\sub is stored as typed,   (a)
                           an absolute path or one with . or .. by its last
                           part; . is the folder's contents; masks (*.txt,
                           dir\*) are looked for; a missing one: warning, exit 1
          -i[r]!<name>     Include (-i) or exclude (-x) names; r: at any       (x, l, t, a)
          -x[r]!<name>     depth, r-: from the top, r0: wildcards only; @<file>
                           instead of !<name> for a list (Scoop: -xr!*.nsis).
                           -xtd / -xtf (x, l, t): leave out every folder / file
          -r[-|0]          How masks recurse: -r any depth, -r- from the       (x, l, t, a)
                           top (default), -r0 any depth only with a wildcard
          -spd, -ssc       No wildcards; case-sensitive names                  (x, l, t, a)
          -scs<charset>    List file charset: UTF-8 (default), UTF-16LE,       (x, l, t, a)
                           UTF-16BE, WIN, DOS
          -ai, -ax, -an    More archives by mask (-ai!*.zip, -air! recurses),  (x, l, t)
                           archives left out (-ax), no archive name (-an);
                           x *.zip works too. Before, x a.zip b.zip took two
                           archives, now b.zip is a name inside a.zip (7-Zip)
          -stx<type>       Do not open archives of this type (zip, 7z, tar,    (x, l, t)
                           gzip, bzip2, xz, zstd, lzma, rar); the outer
                           format counts: -stxgzip leaves out a .tar.gz
          -scc{UTF-8|WIN|DOS}  Charset of printed text (stdout, stderr)   (all)
                           Default: the console code page, where a name it
                           cannot hold prints as '?'. Use -sccUTF-8 when
                           redirecting to a file, e.g. pakko l a.zip -sccUTF-8 > list.txt

        SWITCHES: as in 7-Zip, a switch name and the command are read without
          case (-Y X), switches may come before the command, and each switch is
          given once (twice is exit 7) except -i, -x, -ai, -ax, -m and -stx.

        COMPRESSION LEVEL (-mx, command 'a' only):
          0    -> Store (no compression)   3-6 -> Optimal (default)
          1-2  -> Fastest                  7-9 -> SmallestSize
          The same four steps for every type. For 7z, 0 stores whatever -m0 says.

        HASH (command 'h'):
          One or more files -> each hashed independently.
          Exactly one folder -> recurses fully and also prints a combined
          DataSum (all file contents) and NamesSum (all names+paths+contents),
          bit-for-bit compatible with NanaZip's own folder-hash values.
          A folder mixed into a multi-item selection is skipped, not summed.

        STDIN/STDOUT STREAMING (-si/-so):
          Buffered, not zero-copy, for x/t/l/a: -si stages the full stdin stream
          to a private temp file before the operation starts; -so runs the
          operation to a private temp location, then streams the single
          resulting file to stdout once it's complete. A failed operation never
          emits partial output. -so on 'x' requires the extraction to resolve
          to exactly one file. 'h -si' is the one exception: CRC-32/SHA-256
          need no seeking, so stdin is hashed directly with no temp file at all
          — a genuine single-pass stream. Native pipes work correctly in
          PowerShell 7+. In Windows
          PowerShell 5.1, native '|'/'>' between two executables silently
          corrupts binary data (it mediates the pipe as text) — wrap in
          cmd /c "..." instead, which uses a true OS pipe on any PowerShell
          version:
            cmd /c "pakko a -so out.zip file1 file2 | pakko x -si -o dest > log"

        POWERSHELL:
          PowerShell passes '-name.rest' as two arguments: '-ttar.gz' arrives as
          '-ttar' '.gz', '-pSecret.1' as '-pSecret' '.1'. Pakko refuses such a pair
          after -p, -o or -t (exit 7). Quote the switch ('-ttar.gz', '-p<pwd>') or
          use -ttgz; write a path that starts with a dot as .\name.

        NOT IMPLEMENTED (real 7z commands; run one to see the specific reason):
          u (update)   d (delete)   rn (rename)   b (benchmark)   e (extract, flat)

        EXIT CODES:  0 ok   1 ok with warnings   2 operation failed   7 command-line error
                     255 stopped by user (Q at a prompt, or Ctrl+C in x/t/l/a;
                         a second Ctrl+C ends pakko at once)

        Full specification: CLI.md in the Pakko repository.
        """;
}
