using Archiver.Core.Models;

namespace Archiver.Shell;

/// <summary>Command type dispatched from parsed CLI arguments.</summary>
public enum CommandType
{
    ExtractHere,
    ExtractHereFlat,
    ExtractFolder,
    Archive,
    Test,
    OpenUiExtract,
    OpenUiArchive,
    Invalid,
}

/// <summary>Result of parsing CLI arguments passed to Archiver.Shell.</summary>
public sealed record ParsedCommand
{
    public CommandType Type { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public string? ErrorMessage { get; init; }

    // T-F105: only Archive uses this — set from an optional "--format zip|tar" pair right after
    // "--archive". Defaults to Zip, matching Archiver.ShellExtension.dll's BuildArchiveArgs,
    // which stays flag-less for the pre-existing zip one-click command.
    public ArchiveContainerFormat Format { get; init; } = ArchiveContainerFormat.Zip;
}

/// <summary>
/// Parses CLI arguments for Archiver.Shell. Extracted into a separate class so
/// T-F57 can unit-test all argument combinations without launching a process.
/// </summary>
public static class ShellArgumentParser
{
    /// <summary>
    /// Parses <paramref name="args"/> into a <see cref="ParsedCommand"/>.
    /// Never throws — invalid or missing arguments produce <see cref="CommandType.Invalid"/>.
    /// </summary>
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return Invalid("No arguments provided.");

        return args[0] switch
        {
            "--open-ui"        => ParseOpenUi(args),
            "--extract-here"   => ParseFileList(CommandType.ExtractHere, args),
            "--extract-flat"   => ParseFileList(CommandType.ExtractHereFlat, args),
            "--extract-folder" => ParseFileList(CommandType.ExtractFolder, args),
            "--archive"        => ParseArchive(args),
            "--test"           => ParseFileList(CommandType.Test, args),
            var other          => Invalid($"Unknown command: {other}"),
        };
    }

    // T-F105: "--archive" alone means ZIP (unchanged pre-existing behavior); an optional
    // "--format zip|tar" pair, consumed right after "--archive" and before the file paths,
    // selects TAR instead. Only these two values are accepted here — the one-click shell command
    // never prompts the user, so it only ever needs the two formats with nothing to configure
    // (plain zip and plain tar); the Compress dialog's full 6-variant tar selector goes through
    // MainViewModel's IArchiveCreationRouter call directly, not this CLI path.
    private static ParsedCommand ParseArchive(string[] args)
    {
        var rest = args[1..];
        var format = ArchiveContainerFormat.Zip;

        if (rest.Length > 0 && rest[0] == "--format")
        {
            if (rest.Length < 2)
                return Invalid("--format requires a value.");

            format = rest[1] switch
            {
                "zip" => ArchiveContainerFormat.Zip,
                "tar" => ArchiveContainerFormat.Tar,
                _     => (ArchiveContainerFormat)(-1),
            };
            if ((int)format == -1)
                return Invalid($"Unknown --format value: {rest[1]}");

            rest = rest[2..];
        }

        if (rest.Length == 0)
            return Invalid("--archive requires at least one file.");

        return new ParsedCommand { Type = CommandType.Archive, Files = rest, Format = format };
    }

    private static ParsedCommand ParseOpenUi(string[] args)
    {
        if (args.Length < 3)
            return Invalid("--open-ui requires a sub-command and at least one file.");

        var files = (IReadOnlyList<string>)args[2..];

        return args[1] switch
        {
            "--extract" => new ParsedCommand { Type = CommandType.OpenUiExtract, Files = files },
            "--archive" => new ParsedCommand { Type = CommandType.OpenUiArchive, Files = files },
            var other   => Invalid($"Unknown --open-ui sub-command: {other}"),
        };
    }

    private static ParsedCommand ParseFileList(CommandType type, string[] args)
    {
        var files = (IReadOnlyList<string>)args[1..];
        if (files.Count == 0)
            return Invalid($"{args[0]} requires at least one file.");

        return new ParsedCommand { Type = type, Files = files };
    }

    private static ParsedCommand Invalid(string message) =>
        new ParsedCommand { Type = CommandType.Invalid, ErrorMessage = message };
}
