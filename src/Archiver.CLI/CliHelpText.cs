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

        COMMANDS:
          x   Extract archive(s) with full paths        [ZIP, tar-family]
          t   Test archive integrity                    [ZIP only]
          l   List archive contents                     [ZIP, tar-family]
          a   Add files to a new archive                [ZIP, tar-family]
          h   Hash files, or one folder recursively      [CRC-32, SHA-256]
          i   Show supported formats/codecs on this system

        SWITCHES:
          -o<dir>          Output directory                                    (x)
          -y               Assume yes: auto-overwrite conflicts, auto-confirm
                           compression-bomb warnings. Without -y, both default
                           to a safe decline (file skipped, reported at the end).
          -ao{a|s|u}       Overwrite mode: a=overwrite, s=skip, u=auto-rename   (x)
          -t<type>         Archive type: zip (default), tar, tar.gz, tar.bz2,
                           tar.xz, tar.zst, tar.lzma                            (a)
          -mx=<0-9>        Compression level — see table below                 (a)
          -scrc<method>    Hash method: CRC32 (default) or SHA256              (h)
          -si              Read the archive from stdin instead of a path       (x, t, l, h)
          -so              Write output to stdout instead of disk              (x, a)

        COMPRESSION LEVEL (-mx, command 'a' only):
          0    -> Store (no compression)   3-6 -> Optimal (default)
          1-2  -> Fastest                  7-9 -> SmallestSize

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

        NOT IMPLEMENTED (real 7z commands; run one to see the specific reason):
          u (update)   d (delete)   rn (rename)   b (benchmark)   e (extract, flat)

        EXIT CODES:  0 ok   1 ok with warnings   2 operation failed   7 command-line error

        Full specification: CLI.md in the Pakko repository.
        """;
}
