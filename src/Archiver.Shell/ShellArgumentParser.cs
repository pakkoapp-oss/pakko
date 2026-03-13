namespace Archiver.Shell;

/// <summary>Command type dispatched from parsed CLI arguments.</summary>
public enum CommandType
{
    ExtractHere,
    ExtractFolder,
    Archive,
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
            "--extract-folder" => ParseFileList(CommandType.ExtractFolder, args),
            "--archive"        => ParseFileList(CommandType.Archive, args),
            var other          => Invalid($"Unknown command: {other}"),
        };
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
