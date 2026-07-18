using System.IO.Compression;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>Command type dispatched from parsed CLI arguments.</summary>
public enum CliCommandType
{
    Extract,
    Test,
    Info,
    Archive,
    List,
    Help,
    Invalid,
}

/// <summary>Result of parsing CLI arguments passed to Archiver.CLI.</summary>
public sealed record ParsedCliCommand
{
    public CliCommandType Type { get; init; }
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];   // x, t, l
    public IReadOnlyList<string> SourcePaths { get; init; } = [];    // a
    public string? ArchivePathArg { get; init; }                     // a: raw positional[0] (name/path)
    public string? OutputDirectory { get; init; }                    // -o{dir}, x only
    public bool AssumeYes { get; init; }                              // -y
    public ConflictBehavior? OverwriteMode { get; init; }             // -ao{a|s|u}, x only
    public ArchiveContainerFormat ArchiveFormat { get; init; } = ArchiveContainerFormat.Zip; // -t{type}, a only
    public CompressionLevel? CompressionLevel { get; init; }          // -mx=N, a only (null = default Optimal)
    public bool ReadFromStdin { get; init; }                          // -si, x/t/l only (T-F116)
    public bool WriteToStdout { get; init; }                          // -so, x/a only (T-F116)
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Parses 7z-familiar CLI arguments for Archiver.CLI. Extracted into a separate class so every
/// command/switch combination — including the three-way unknown-input rule from CLI.md — is
/// unit-testable without launching a process. Never throws — invalid or unsupported input always
/// produces <see cref="CliCommandType.Invalid"/> with a specific <see cref="ParsedCliCommand.ErrorMessage"/>,
/// never a silent no-op (see CLI.md's "Unknown/unsupported input" section).
/// </summary>
public static class CliArgumentParser
{
    public static ParsedCliCommand Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
            return new ParsedCliCommand { Type = CliCommandType.Help };

        var rest = args[1..];
        return args[0] switch
        {
            "x" => ParseExtract(rest),
            "t" => ParseTest(rest),
            "i" => ParseInfo(rest),
            "a" => ParseArchive(rest),
            "l" => ParseList(rest),
            "u" or "d" or "rn" or "b" or "e" => Invalid(NotSupportedCommandReason(args[0])),
            var other => Invalid($"Incorrect command line: unknown command '{other}'"),
        };
    }

    // --- x (Extract) ---

    private static ParsedCliCommand ParseExtract(string[] rest)
    {
        var archivePaths = new List<string>();
        string? outputDirectory = null;
        bool assumeYes = false;
        ConflictBehavior? overwriteMode = null;
        bool readFromStdin = false;
        bool writeToStdout = false;

        foreach (string token in rest)
        {
            if (!IsSwitchToken(token))
            {
                archivePaths.Add(token);
                continue;
            }

            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }

            if (token == "-so")
            {
                writeToStdout = true;
                continue;
            }

            if (token == "-y")
            {
                assumeYes = true;
                continue;
            }

            if (token.StartsWith("-o", StringComparison.Ordinal))
            {
                if (token.Length == 2)
                    return Invalid("-o requires a directory, e.g. -oC:\\dest");
                outputDirectory = token[2..];
                continue;
            }

            if (token.StartsWith("-ao", StringComparison.Ordinal))
            {
                if (token.Length != 4)
                    return Invalid("-ao requires exactly one mode letter: a, s, u, or t (e.g. -aoa)");

                char mode = token[3];
                if (mode == 't')
                    return Invalid("not supported by Pakko: -aot (rename existing file instead of new) has no equivalent");

                overwriteMode = mode switch
                {
                    'a' => ConflictBehavior.Overwrite,
                    's' => ConflictBehavior.Skip,
                    'u' => ConflictBehavior.Rename,
                    _ => (ConflictBehavior?)null,
                };
                if (overwriteMode is null)
                    return Invalid($"unknown -ao mode: '{mode}' (expected a, s, u, or t)");
                continue;
            }

            return Invalid(UnsupportedSwitchReason(token));
        }

        if (readFromStdin && archivePaths.Count > 0)
            return Invalid("'-si' cannot be combined with an explicit archive path");
        if (writeToStdout && outputDirectory is not null)
            return Invalid("'-so' cannot be combined with '-o' (mutually exclusive destinations)");
        if (!readFromStdin && archivePaths.Count == 0)
            return Invalid("'x' requires at least one archive path");

        return new ParsedCliCommand
        {
            Type = CliCommandType.Extract,
            ArchivePaths = archivePaths,
            OutputDirectory = outputDirectory,
            AssumeYes = assumeYes,
            OverwriteMode = overwriteMode,
            ReadFromStdin = readFromStdin,
            WriteToStdout = writeToStdout,
        };
    }

    // --- t (Test) ---

    private static ParsedCliCommand ParseTest(string[] rest)
    {
        var archivePaths = new List<string>();
        bool readFromStdin = false;
        foreach (string token in rest)
        {
            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }
            if (IsSwitchToken(token))
                return Invalid(UnsupportedSwitchReason(token));
            archivePaths.Add(token);
        }

        if (readFromStdin && archivePaths.Count > 0)
            return Invalid("'-si' cannot be combined with an explicit archive path");
        if (!readFromStdin && archivePaths.Count == 0)
            return Invalid("'t' requires at least one archive path");

        return new ParsedCliCommand { Type = CliCommandType.Test, ArchivePaths = archivePaths, ReadFromStdin = readFromStdin };
    }

    // --- i (Info) ---

    private static ParsedCliCommand ParseInfo(string[] rest)
    {
        if (rest.Length == 0)
            return new ParsedCliCommand { Type = CliCommandType.Info };

        string token = rest[0];
        return Invalid(IsSwitchToken(token)
            ? UnsupportedSwitchReason(token)
            : "'i' does not accept any arguments");
    }

    // --- a (Archive) ---

    private static ParsedCliCommand ParseArchive(string[] rest)
    {
        string? archivePathArg = null;
        var sourcePaths = new List<string>();
        bool assumeYes = false;
        var archiveFormat = ArchiveContainerFormat.Zip;
        CompressionLevel? compressionLevel = null;
        bool writeToStdout = false;

        foreach (string token in rest)
        {
            if (!IsSwitchToken(token))
            {
                if (archivePathArg is null)
                    archivePathArg = token;
                else
                    sourcePaths.Add(token);
                continue;
            }

            if (token == "-so")
            {
                writeToStdout = true;
                continue;
            }

            if (token == "-y")
            {
                assumeYes = true;
                continue;
            }

            if (token.StartsWith("-mx", StringComparison.Ordinal))
            {
                if (token.Length < 5 || token[3] != '=')
                    return Invalid("-mx requires a value in the form -mx=<0-9>, e.g. -mx=9");

                string valueText = token[4..];
                if (!int.TryParse(valueText, out int mx))
                    return Invalid($"-mx=<n> must be a number 0-9, got '{valueText}'");

                CompressionLevel? level = CliCompressionLevelMapper.TryMap(mx);
                if (level is null)
                    return Invalid($"-mx=<n> must be 0-9, got '{mx}'");

                compressionLevel = level;
                continue;
            }

            if (token.StartsWith("-t", StringComparison.Ordinal))
            {
                if (token.Length <= 2)
                    return Invalid("-t requires a type, e.g. -tzip or -ttar.gz");

                string typeValue = token[2..];
                if (typeValue is "7z" or "rar")
                    return Invalid($"not supported by Pakko: -t{typeValue} — Pakko can only create ZIP/tar-family archives, {typeValue} is extract-only");

                ArchiveContainerFormat? format = typeValue switch
                {
                    "zip" => ArchiveContainerFormat.Zip,
                    "tar" => ArchiveContainerFormat.Tar,
                    "tar.gz" => ArchiveContainerFormat.TarGz,
                    "tar.bz2" => ArchiveContainerFormat.TarBz2,
                    "tar.xz" => ArchiveContainerFormat.TarXz,
                    "tar.zst" => ArchiveContainerFormat.TarZst,
                    "tar.lzma" => ArchiveContainerFormat.TarLzma,
                    _ => null,
                };
                if (format is null)
                    return Invalid($"unknown -t value: '{typeValue}' (expected zip, tar, tar.gz, tar.bz2, tar.xz, tar.zst, or tar.lzma)");

                archiveFormat = format.Value;
                continue;
            }

            return Invalid(UnsupportedSwitchReason(token));
        }

        if (archivePathArg is null || sourcePaths.Count == 0)
            return Invalid("'a' requires an archive name and at least one source file");

        return new ParsedCliCommand
        {
            Type = CliCommandType.Archive,
            ArchivePathArg = archivePathArg,
            SourcePaths = sourcePaths,
            AssumeYes = assumeYes,
            ArchiveFormat = archiveFormat,
            CompressionLevel = compressionLevel,
            WriteToStdout = writeToStdout,
        };
    }

    // --- l (List) ---

    private static ParsedCliCommand ParseList(string[] rest)
    {
        var archivePaths = new List<string>();
        bool readFromStdin = false;
        foreach (string token in rest)
        {
            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }
            if (IsSwitchToken(token))
                return Invalid(UnsupportedSwitchReason(token));
            archivePaths.Add(token);
        }

        if (readFromStdin && archivePaths.Count > 0)
            return Invalid("'-si' cannot be combined with an explicit archive path");
        if (!readFromStdin && archivePaths.Count == 0)
            return Invalid("'l' requires at least one archive path");

        return new ParsedCliCommand { Type = CliCommandType.List, ArchivePaths = archivePaths, ReadFromStdin = readFromStdin };
    }

    // --- shared helpers ---

    private static bool IsSwitchToken(string token) => token.Length > 0 && token[0] == '-';

    // Case 2 of the three-way rule: a real 7z command Pakko deliberately doesn't implement.
    private static string NotSupportedCommandReason(string command) => command switch
    {
        "u" => "not supported by Pakko: no diff-against-existing-archive logic exists — Pakko is not an archive manager",
        "d" => "not supported by Pakko: in-place archive mutation is out of scope — see CLAUDE.md",
        "rn" => "not supported by Pakko: in-place archive mutation is out of scope — see CLAUDE.md",
        "b" => "not supported by Pakko: benchmarking is out of scope",
        "e" => "not supported by Pakko: extraction always preserves folder structure — use 'x'",
        _ => "not supported by Pakko",
    };

    // Case 3 (a real, known 7z switch not allowed on this command) vs. case 1 (not a real 7z
    // switch at all — a typo) of the three-way rule. Matched against CLI.md's switch table.
    private static string UnsupportedSwitchReason(string token)
    {
        if (token == "-si")
            return "not supported on this command";
        if (token == "-so")
            return "not supported on this command";
        if (token == "-y")
            return "not supported on this command";
        if (token.StartsWith("-ao", StringComparison.Ordinal))
            return "not supported on this command";
        if (token.StartsWith("-mx", StringComparison.Ordinal) || token.StartsWith("-m", StringComparison.Ordinal))
            return "not supported on this command: -m{params} is only meaningful for 'a' (archive creation)";
        if (token.StartsWith("-t", StringComparison.Ordinal))
            return "not supported on this command: -t{type} is only meaningful for 'a' (archive creation)";
        if (token.StartsWith("-o", StringComparison.Ordinal))
            return "not supported on this command";
        if (token.StartsWith("-p", StringComparison.Ordinal))
            return "not supported: System.IO.Compression has no ZIP encryption support";
        if (token.StartsWith("-r", StringComparison.Ordinal))
            return "not supported: recurse-subdirectories toggle has no Pakko equivalent (archiving already recurses by default)";
        if (token.StartsWith("-i", StringComparison.Ordinal))
            return "not supported: no wildcard include-pattern filtering exists in Pakko";
        if (token.StartsWith("-x", StringComparison.Ordinal))
            return "not supported: no wildcard exclude-pattern filtering exists in Pakko";
        if (token.StartsWith("-v", StringComparison.Ordinal))
            return "not supported: no multi-part/split-archive logic exists in Pakko";
        if (token.StartsWith("-scc", StringComparison.Ordinal))
            return "not supported: .NET is Unicode-native, console charset switching has no effect";
        if (token.StartsWith("-ssc", StringComparison.Ordinal))
            return "not supported: case-sensitive matching is not implemented";

        return $"Incorrect command line: unknown switch '{token}'";
    }

    private static ParsedCliCommand Invalid(string message) =>
        new() { Type = CliCommandType.Invalid, ErrorMessage = message };
}
