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
    Hash,
    Repair,
    Help,
    Version,
    Invalid,
}

/// <summary>How 'a' names the archive: 7-Zip's -sa{s|e|a} (T-F411).</summary>
public enum CliArchiveNameMode
{
    /// <summary>-sas, the default: a name with no extension gets the type's.</summary>
    Smart,
    /// <summary>-sae: the name exactly as typed.</summary>
    Exact,
    /// <summary>-saa: the type's extension is always added.</summary>
    Always,
}

/// <summary>Result of parsing CLI arguments passed to Archiver.CLI.</summary>
public sealed record ParsedCliCommand
{
    public CliCommandType Type { get; init; }
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];   // x, t, l
    public IReadOnlyList<string> SourcePaths { get; init; } = [];    // a, h
    public string? ArchivePathArg { get; init; }                     // a: raw positional[0] (name/path)
    public string? OutputDirectory { get; init; }                    // -o{dir}, x and r
    public bool AssumeYes { get; init; }                              // -y
    public ConflictBehavior? OverwriteMode { get; init; }             // -ao{a|s|u}, x only
    public ArchiveContainerFormat ArchiveFormat { get; init; } = ArchiveContainerFormat.Zip; // -t{type}, a only
    public string? ArchiveTypeSwitch { get; init; }                   // the -t token as typed; null when no -t (T-F296)
    public CompressionLevel? CompressionLevel { get; init; }          // -mx=N, a only (null = default Optimal)
    public SevenZipMethod? SevenZipMethod { get; init; }              // -m0=<method>, a -t7z only (T-F342); null = not given
    public HashAlgorithmKind HashAlgorithm { get; init; } = HashAlgorithmKind.Crc32; // -scrc{method}, h only (T-F128/T-F09 follow-up); Crc32 matches real 7z's own default hash method
    public bool ReadFromStdin { get; init; }                          // -si, x/t/l/h only (T-F116)
    public bool WriteToStdout { get; init; }                          // -so, x/a only (T-F116)
    public string? Password { get; init; }                            // -p{pwd}, x/t (T-F191), a (T-F193)
    public bool PromptForPassword { get; init; }                      // bare -p: ask interactively, x/t/a (T-F193)
    public int? ConsoleCodePage { get; init; }                        // -scc{charset}, every command (T-F238); a CliConsoleCharset value
    public bool? ApplyDownloadMark { get; init; }                     // -snz[0|1], x only (T-F360); null = not given
    public int RecoveryPercent { get; init; }                         // -rr[N], a only (T-F275); 0 = no PAR2 files
    public CliArchiveNameMode ArchiveNameMode { get; init; }          // -sas/-sae/-saa, a only (T-F411)
    public string? WorkingFolder { get; init; }                       // -w[dir], a only (T-F411); "" = bare -w, null = not given
    public int MaxThreads { get; init; }                              // -mmt[N|on|off], a only (T-F411); 0 = the core count
    public bool SetArchiveTime { get; init; }                         // -stl, a only (T-F411)
    public bool ReadFilesOpenForWriting { get; init; }                // -ssw, a only (T-F411)
    public bool StopOnUnreadableSource { get; init; }                 // -sse, a only (T-F411)
    public bool DeleteSourcesAfter { get; init; }                     // -sdel, a only (T-F411)
    public bool DropRootNamedLikeOutput { get; init; }                // -spe[-], x only (T-F411)
    public CliOutputOptions Output { get; init; } = new();            // -ba/-bb/-bd/-bt/-bs*, every command (T-F408)
    public CliNameSwitches NameSwitches { get; init; } = new();       // -i/-x/-r/-spd/-ssc/-scs/-ai/-ax/-an, x/l/t (T-F410)
    public IReadOnlyList<CliFilterName> FilterNames { get; init; } = []; // x/l/t: the names after the archive name; a: its sources (T-F410)
    public CliNameFilter? EntryFilter { get; init; }                  // x/l: set by Program from CliNameSelection, not by the parser (T-F410)
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
        if (args.Length == 0 || AsksForHelp(args))
            return new ParsedCliCommand { Type = CliCommandType.Help };

        if (args[0] is "-v" or "--version")
            return new ParsedCliCommand { Type = CliCommandType.Version };

        // T-F407: as in 7-Zip (CommandLineParser.cpp, ParseStrings), everything after the first "--"
        // is a name, even "-name" or a second "--"; patool puts "--" before the names on every call.
        int stop = Array.IndexOf(args, "--");
        string[] switchPart = stop < 0 ? args : args[..stop];
        string[] names = stop < 0 ? [] : args[(stop + 1)..];
        if (TakeCommand(ref switchPart, ref names, out string? commandError) is not { } commandName)
            return Invalid(commandError!);
        if (!CliSwitchNames.TryCanonicalize(switchPart, out switchPart, out string? repeatError))
            return Invalid(repeatError!);
        if (ShellSplitReason(switchPart) is { } splitError)
            return Invalid(splitError);
        if (!TryTakeConsoleCharset(ref switchPart, out int? consoleCodePage, out string? charsetError))
            return Invalid(charsetError!);
        if (!CliOutputOptions.TryTake(ref switchPart, out CliOutputOptions output, out string? outputError))
            return Invalid(outputError!);
        if (!CliNameSwitches.TryTake(ref switchPart, out CliNameSwitches nameSwitches, out string? nameError))
            return Invalid(nameError!);

        CliToken[] rest = [.. switchPart.Select(CliToken.Of), .. names.Select(CliToken.Name)];
        ParsedCliCommand command = CliCommandValidator.Validate(ParseCommand(commandName, rest, nameSwitches));
        if (command.Type == CliCommandType.Invalid)
            return command;
        if (StdOutConflict(command, output) is { } conflict)
            return Invalid(conflict);
        return command with { ConsoleCodePage = consoleCodePage, Output = output };
    }

    // T-F411: as in 7-Zip (ParseStrings, then the first of NonSwitchStrings), the command is the first
    // argument that is not a switch, or the first name after "--" when every argument before it is
    // one. Its letters are compared without case, ASCII only; an unknown command keeps its spelling
    // for the error message.
    private static readonly string[] CommandNames = ["x", "t", "i", "a", "l", "h", "r", "u", "d", "rn", "b", "e"];

    // T-F411: 7-Zip shows its help for -?, -h or --help anywhere before "--", in any case
    // (ArchiveCommandLine.cpp, HelpMode); "-hx" is not one of them.
    private static bool AsksForHelp(string[] args)
    {
        foreach (string token in args)
        {
            if (token == "--")
                return false;
            if (CliSwitchNames.ToLowerAscii(token) is "-h" or "-?" or "--help")
                return true;
        }
        return false;
    }

    private static string? TakeCommand(ref string[] switchPart, ref string[] names, out string? error)
    {
        int index = Array.FindIndex(switchPart, token => token.Length == 0 || token[0] != '-');
        bool afterBarePassword = index > 0 && CliSwitchNames.ToLowerAscii(switchPart[index - 1]) == "-p";
        string typed;
        if (index >= 0)
        {
            typed = switchPart[index];
            switchPart = [.. switchPart[..index], .. switchPart[(index + 1)..]];
        }
        else if (names.Length > 0)
        {
            typed = names[0];
            names = names[1..];
            afterBarePassword = switchPart.Length > 0 && CliSwitchNames.ToLowerAscii(switchPart[^1]) == "-p";
        }
        else
        {
            error = "Incorrect command line: no command given (e.g. pakko x archive.zip)";
            return null;
        }
        error = null;
        string lower = CliSwitchNames.ToLowerAscii(typed);
        if (Array.IndexOf(CommandNames, lower) >= 0)
            return lower;
        // "-p secret x a.zip" makes "secret" the command; a word that may be a password is never printed.
        if (afterBarePassword)
        {
            error = "Incorrect command line: unknown command after a bare -p (the word is not printed; give a password as -p<password>)";
            return null;
        }
        return typed;
    }

    // T-F408: under -so stdout carries the data; 7-Zip turns its messages off there (StdOutMode).
    private static string? StdOutConflict(ParsedCliCommand command, CliOutputOptions output)
    {
        if (!command.WriteToStdout)
            return null;
        if (output.OutStream == CliOutputTarget.StdOut)
            return "'-bso1' cannot be combined with '-so' — stdout carries the data; send messages to stderr with -bso2";
        if (output.ErrStream == CliOutputTarget.StdOut)
            return "'-bse1' cannot be combined with '-so' — stdout carries the data; errors stay on stderr";
        return null;
    }

    // One command-line argument, and whether it is read as a switch: a name after "--" never is,
    // nor a list file (T-F410: "@name" is a list file only before "--", as in 7-Zip).
    private readonly record struct CliToken(string Text, bool IsSwitch)
    {
        public bool AfterStop { get; init; }
        public static CliToken Of(string text) => new(text, text.Length > 0 && text[0] == '-');
        public static CliToken Name(string text) => new(text, false) { AfterStop = true };
    }

    // T-F294: PowerShell passes "-name.rest" to a native program as two arguments, "-name" and
    // ".rest" ("-ttar.gz" became "-ttar" and ".gz"; "-pSecret.1" became "-pSecret" and ".1").
    // Refused for the switches whose value a split silently changes: -p (a shorter password), -o
    // (another folder), -t (another type). A complete switch such as -y or -scrcSHA256 followed by
    // a dotfile is what cmd passes too, so it is left alone. A path that really starts with a dot
    // can always be written .\name, which never looks like a piece.
    private static string? ShellSplitReason(string[] rest)
    {
        for (int i = 0; i + 1 < rest.Length; i++)
        {
            if (!LooksLikeSplitSwitch(rest[i]) || !LooksLikeSplitPiece(rest[i + 1]))
                continue;

            // Neither half of a password is ever printed.
            if (rest[i].StartsWith("-p", StringComparison.Ordinal))
                return "'-p<password>' was cut at its first dot: PowerShell passes '-name.rest' as two arguments. "
                    + "Quote the switch ('-p<password>')";

            string piece = rest[i + 1];
            return $"'{rest[i]}' is followed by '{piece}': PowerShell passes '-name.rest' as two arguments. "
                + $"Quote the switch ('{rest[i]}{piece}'), or write a path that starts with a dot as .\\{piece}";
        }
        return null;
    }

    private static bool LooksLikeSplitSwitch(string token) =>
        token.Length >= 2 && token[0] == '-' && token[1] is 'p' or 'o' or 't'
        && token.AsSpan(1).IndexOfAnyExcept(NameCharacters) < 0;

    private static readonly System.Buffers.SearchValues<char> NameCharacters =
        System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    private static bool LooksLikeSplitPiece(string token) =>
        token.Length >= 2 && token[0] == '.' && token[1] is not ('.' or '\\' or '/');

    private static ParsedCliCommand ParseCommand(string commandName, CliToken[] rest, CliNameSwitches nameSwitches)
    {
        // T-F410: 'a' takes -i, -x, -r, -spd, -ssc and -scs too; what picks archives, -stx, -xtd
        // and -xtf stay x, l and t's (7-Zip refuses -ai on 'a' and ignores -stx there).
        if (commandName == "a" && (nameSwitches.PicksArchives || nameSwitches.ExcludedTypes.Count > 0
            || nameSwitches.ExcludeFolders || nameSwitches.ExcludeFiles))
            return Invalid(ArchivePickingSwitchesReason);
        if (commandName is not ("x" or "l" or "t" or "a") && (nameSwitches.FiltersNames || nameSwitches.PicksArchives
            || !nameSwitches.WildcardMatching || nameSwitches.CaseSensitive || nameSwitches.ListFileCharset is not null
            || nameSwitches.ExcludedTypes.Count > 0))
            return Invalid(NameSwitchesOnlyOnExtractReason);
        ParsedCliCommand command = commandName switch
        {
            "x" => ParseExtract(rest),
            "t" => ParseTest(rest),
            "i" => ParseInfo(rest),
            "a" => ParseArchive(rest),
            "l" => ParseList(rest),
            "h" => ParseHash(rest),
            "r" => ParseRepair(rest),
            "u" or "d" or "rn" or "b" or "e" => Invalid(NotSupportedCommandReason(commandName)),
            var other => Invalid($"Incorrect command line: unknown command '{other}'"),
        };
        if (command.Type == CliCommandType.Archive)
        {
            return command.FilterNames.Count == 0 && !nameSwitches.Names.Any(n => n.Include)
                ? Invalid("'a' requires an archive name and at least one source file")
                : command with { NameSwitches = nameSwitches };
        }
        return command.Type is CliCommandType.Extract or CliCommandType.List or CliCommandType.Test
            ? SplitArchiveAndNames(command, nameSwitches)
            : command;
    }

    private const string ArchivePickingSwitchesReason =
        "not supported on 'a': -ai, -ax, -an, -stx, -xtd and -xtf work on 'x', 'l' and 't'";

    private const string NameSwitchesOnlyOnExtractReason =
        "not supported on this command: name filters (-i, -x, -r, -ai, -ax, -an, -spd, -ssc, -scs) and -stx work on 'x', 'l' and 't'";

    // T-F410: as in 7-Zip, the first name is the archive (none with -si or -an) and every other
    // name filters the entries inside it. The command parsers collect every name as FilterNames.
    private static ParsedCliCommand SplitArchiveAndNames(ParsedCliCommand command, CliNameSwitches nameSwitches)
    {
        IReadOnlyList<CliFilterName> names = command.FilterNames;
        bool archiveNamed = !command.ReadFromStdin && !nameSwitches.NoArchiveName;
        string verb = command.Type switch { CliCommandType.Extract => "x", CliCommandType.Test => "t", _ => "l" };
        if (command.ReadFromStdin && nameSwitches.PicksArchives)
            return Invalid("'-ai', '-ax' and '-an' cannot be combined with '-si' — stdin already carries the archive");
        if (nameSwitches.NoArchiveName && !nameSwitches.Archives.Any(a => a.Include))
            return Invalid("'-an' needs '-ai' to name the archives (e.g. -an -ai!*.zip)");
        if (archiveNamed && names.Count == 0)
            return Invalid($"'{verb}' requires at least one archive path");

        List<CliFilterName> filters = [.. names.Skip(archiveNamed ? 1 : 0)];
        return command with
        {
            ArchivePaths = archiveNamed ? [names[0].Text] : [],
            FilterNames = filters,
            NameSwitches = nameSwitches,
        };
    }

    // T-F410: "@name" before "--" is a list file of names, as in 7-Zip (AddToCensorFromNonSwitchesStrings).
    private static CliFilterName PositionalName(CliToken token) =>
        new(token.Text, IsListFile: !token.AfterStop && token.Text.StartsWith('@'));

    // T-F238: -scc{charset} applies to every command, so it is taken out before the per-command
    // parsers see the tokens; given twice, CliSwitchNames refuses it, as 7-Zip does. 7-Zip also takes a numeric
    // code page; Pakko supports only the three names its help documents.
    private static bool TryTakeConsoleCharset(ref string[] rest, out int? consoleCodePage, out string? error)
    {
        consoleCodePage = null;
        error = null;
        var remaining = new List<string>(rest.Length);
        foreach (string token in rest)
        {
            if (!token.StartsWith("-scc", StringComparison.Ordinal))
            {
                remaining.Add(token);
                continue;
            }

            string name = token[4..];
            consoleCodePage = CliConsoleCharset.TryParse(name);
            if (consoleCodePage is null)
            {
                error = name.Length == 0
                    ? "-scc requires a charset: UTF-8, WIN or DOS (e.g. -sccUTF-8)"
                    : $"not supported by Pakko: -scc{name} — the console charset must be UTF-8, WIN or DOS";
                return false;
            }
        }

        rest = [.. remaining];
        return true;
    }

    // --- x (Extract) ---

    // Mutable per-parse state for ParseExtract — moving the token-dispatch if-chain out of the
    // foreach loop and into ApplyExtractToken below is what actually clears S3776's threshold; // NOSONAR: prose, not commented-out code (S125 false positive)
    // extracting the -o/-ao validation alone (TryParseOutputDirectory/TryParseOverwriteMode)
    // wasn't enough, since the loop nesting itself was the bigger contributor.
    private sealed class ExtractParseState
    {
        public List<CliFilterName> Names { get; } = [];
        public string? OutputDirectory { get; set; }
        public bool AssumeYes { get; set; }
        public ConflictBehavior? OverwriteMode { get; set; }
        public bool ReadFromStdin { get; set; }
        public bool WriteToStdout { get; set; }
        public string? Password { get; set; }
        public bool PromptForPassword { get; set; }
        public bool? ApplyDownloadMark { get; set; }
        public bool DropRootNamedLikeOutput { get; set; }
    }

    private static ParsedCliCommand ParseExtract(CliToken[] rest)
    {
        var state = new ExtractParseState();

        foreach (CliToken token in rest)
        {
            string? error = ApplyExtractToken(token, state);
            if (error is not null)
                return Invalid(error);
        }

        if (state.WriteToStdout && state.OutputDirectory is not null)
            return Invalid("'-so' cannot be combined with '-o' (mutually exclusive destinations)");
        if (state.ReadFromStdin && state.PromptForPassword)
            return Invalid(BarePasswordWithStdinReason);

        return new ParsedCliCommand
        {
            Type = CliCommandType.Extract,
            FilterNames = state.Names,
            OutputDirectory = state.OutputDirectory,
            AssumeYes = state.AssumeYes,
            OverwriteMode = state.OverwriteMode,
            ReadFromStdin = state.ReadFromStdin,
            WriteToStdout = state.WriteToStdout,
            Password = state.Password,
            PromptForPassword = state.PromptForPassword,
            ApplyDownloadMark = state.ApplyDownloadMark,
            DropRootNamedLikeOutput = state.DropRootNamedLikeOutput,
        };
    }

    // Returns an error message if the token was invalid/unsupported, null on success (including
    // a plain archive-path token). Mutates state in place — matches the original inline loop's
    // per-token behavior exactly, just without the loop's own nesting contributing to complexity.
    private static string? ApplyExtractToken(CliToken argument, ExtractParseState state)
    {
        string token = argument.Text;
        if (!argument.IsSwitch)
        {
            state.Names.Add(PositionalName(argument));
            return null;
        }

        if (token == "-si") { state.ReadFromStdin = true; return null; }
        if (token == "-so") { state.WriteToStdout = true; return null; }
        if (token == "-y") { state.AssumeYes = true; return null; }

        if (token.StartsWith("-spe", StringComparison.Ordinal))
        {
            (state.DropRootNamedLikeOutput, string? error) = ParseDropRoot(token);
            return error;
        }

        if (token.StartsWith("-o", StringComparison.Ordinal))
        {
            if (!TryParseOutputDirectory(token, out string? outputDirectory, out string? error))
                return error;
            state.OutputDirectory = outputDirectory;
            return null;
        }

        if (token.StartsWith("-ao", StringComparison.Ordinal))
        {
            if (!TryParseOverwriteMode(token, out ConflictBehavior? overwriteMode, out string? error))
                return error;
            state.OverwriteMode = overwriteMode;
            return null;
        }

        if (token.StartsWith("-p", StringComparison.Ordinal))
        {
            (state.Password, state.PromptForPassword) = ParsePassword(token);
            return null;
        }

        if (token.StartsWith("-snz", StringComparison.Ordinal))
        {
            (state.ApplyDownloadMark, string? error) = ParseDownloadMark(token);
            return error;
        }

        return UnsupportedSwitchReason(token);
    }

    // T-F411: 7-Zip's -spe (SWFRM_MINUS): -spe- turns it off again.
    private static (bool Drop, string? Error) ParseDropRoot(string token) => token switch
    {
        "-spe" => (true, null),
        "-spe-" => (false, null),
        _ => (false, $"Incorrect command line: -spe takes only '-' after it, got '{token}'"),
    };

    // T-F360: 7-Zip's -snz[0|1|2] (NanaZip's ArchiveCommandLine.cpp): 0 none, 1 or bare all files,
    // 2 Office files only - which Pakko's modes do not have. Given twice: CliSwitchNames refuses it.
    private static (bool? Apply, string? Error) ParseDownloadMark(string token) => token switch
    {
        "-snz" or "-snz1" => (true, null),
        "-snz0" => (false, null),
        "-snz2" => (null, "not supported by Pakko: -snz2 (mark Office files only) has no equivalent; use -snz or -snz0"),
        _ => (null, $"unknown -snz value: '{token[4..]}' (expected -snz, -snz0 or -snz1)"),
    };

    // T-F193: a bare -p means "prompt for it", as in real 7z.
    private static (string? Password, bool Prompt) ParsePassword(string token) =>
        token.Length == 2 ? (null, true) : (token[2..], false);

    private const string BarePasswordWithStdinReason =
        "a bare '-p' cannot be combined with '-si' — stdin already carries the archive, so there is nothing to type the password into; use -p<pwd>";

    private static bool TryParseOutputDirectory(string token, out string? outputDirectory, out string? error)
    {
        if (token.Length == 2)
        {
            outputDirectory = null;
            error = "-o requires a directory, e.g. -oC:\\dest";
            return false;
        }
        outputDirectory = token[2..];
        error = null;
        return true;
    }

    private static bool TryParseOverwriteMode(string token, out ConflictBehavior? overwriteMode, out string? error)
    {
        overwriteMode = null;
        if (token.Length != 4)
        {
            error = "-ao requires exactly one mode letter: a, s, t or u (e.g. -aoa)";
            return false;
        }

        char mode = token[3];
        overwriteMode = mode switch
        {
            'a' => ConflictBehavior.Overwrite,
            's' => ConflictBehavior.Skip,
            'u' => ConflictBehavior.Rename,
            't' => ConflictBehavior.RenameExisting,
            _ => (ConflictBehavior?)null,
        };
        if (overwriteMode is null)
        {
            error = $"unknown -ao mode: '{mode}' (expected a, s, t or u)";
            return false;
        }

        error = null;
        return true;
    }

    // --- t (Test) ---

    private static ParsedCliCommand ParseTest(CliToken[] rest)
    {
        var names = new List<CliFilterName>();
        bool readFromStdin = false;
        bool assumeYes = false;
        string? password = null;
        bool promptForPassword = false;
        foreach (CliToken argument in rest)
        {
            (string token, bool isSwitch) = argument;
            if (!isSwitch)
            {
                names.Add(PositionalName(argument));
                continue;
            }
            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }
            if (token == "-y")
            {
                assumeYes = true; // T-F409: as on x, no password is asked for unless -p asks
                continue;
            }
            if (token.StartsWith("-p", StringComparison.Ordinal))
            {
                (password, promptForPassword) = ParsePassword(token);
                continue;
            }
            return Invalid(UnsupportedSwitchReason(token));
        }

        if (readFromStdin && promptForPassword)
            return Invalid(BarePasswordWithStdinReason);

        return new ParsedCliCommand
        {
            Type = CliCommandType.Test, FilterNames = names, ReadFromStdin = readFromStdin, AssumeYes = assumeYes,
            Password = password, PromptForPassword = promptForPassword,
        };
    }

    // --- r (Repair, T-F275 step 4) ---

    private static ParsedCliCommand ParseRepair(CliToken[] rest)
    {
        var paths = new List<string>();
        string? outputDirectory = null;
        bool assumeYes = false;
        foreach ((string token, bool isSwitch) in rest)
        {
            if (!isSwitch)
            {
                paths.Add(token);
                continue;
            }
            if (token == "-y")
            {
                assumeYes = true; // nothing to confirm: the copy never replaces a file
                continue;
            }
            if (token.StartsWith("-o", StringComparison.Ordinal))
            {
                if (!TryParseOutputDirectory(token, out outputDirectory, out string? error))
                    return Invalid(error!);
                continue;
            }
            return Invalid(UnsupportedSwitchReason(token));
        }

        if (paths.Count == 0)
            return Invalid("'r' requires at least one archive or .par2 path");

        return new ParsedCliCommand
        {
            Type = CliCommandType.Repair, ArchivePaths = paths, OutputDirectory = outputDirectory, AssumeYes = assumeYes,
        };
    }

    // --- i (Info) ---

    private static ParsedCliCommand ParseInfo(CliToken[] rest)
    {
        if (rest.Length == 0)
            return new ParsedCliCommand { Type = CliCommandType.Info };

        (string token, bool isSwitch) = rest[0];
        return Invalid(isSwitch
            ? UnsupportedSwitchReason(token)
            : "'i' does not accept any arguments");
    }

    // --- a (Archive) ---

    // Mirrors ExtractParseState/ApplyExtractToken's shape — see that pair's comment for why the
    // dispatch has to move out of the foreach loop, not just have its per-switch validation
    // extracted, to actually clear S3776's threshold.
    private sealed class ArchiveParseState
    {
        public string? ArchivePathArg { get; set; }
        public List<string> SourcePaths { get; } = [];
        public List<CliFilterName> SourceNames { get; } = [];
        public bool AssumeYes { get; set; }
        public ArchiveContainerFormat ArchiveFormat { get; set; } = ArchiveContainerFormat.Zip;
        public string? ArchiveTypeSwitch { get; set; }
        public CompressionLevel? CompressionLevel { get; set; }
        public SevenZipMethod? SevenZipMethod { get; set; }
        public bool WriteToStdout { get; set; }
        public string? Password { get; set; }
        public bool PromptForPassword { get; set; }
        public int RecoveryPercent { get; set; }
        public CliArchiveNameMode ArchiveNameMode { get; set; }
        public string? WorkingFolder { get; set; }
        public int MaxThreads { get; set; }
        public bool SetArchiveTime { get; set; }
        public bool ReadFilesOpenForWriting { get; set; }
        public bool StopOnUnreadableSource { get; set; }
        public bool DeleteSourcesAfter { get; set; }
    }

    private static ParsedCliCommand ParseArchive(CliToken[] rest)
    {
        var state = new ArchiveParseState();

        foreach (CliToken token in rest)
        {
            string? error = ApplyArchiveToken(token, state);
            if (error is not null)
                return Invalid(error);
        }

        // T-F410: no source is checked in ParseCommand, where -i can name the sources instead.
        if (state.ArchivePathArg is null)
            return Invalid("'a' requires an archive name and at least one source file");

        return new ParsedCliCommand
        {
            Type = CliCommandType.Archive,
            ArchivePathArg = state.ArchivePathArg,
            SourcePaths = state.SourcePaths,
            FilterNames = state.SourceNames,
            AssumeYes = state.AssumeYes,
            ArchiveFormat = state.ArchiveFormat,
            ArchiveTypeSwitch = state.ArchiveTypeSwitch,
            CompressionLevel = state.CompressionLevel,
            SevenZipMethod = state.SevenZipMethod,
            WriteToStdout = state.WriteToStdout,
            Password = state.Password,
            PromptForPassword = state.PromptForPassword,
            RecoveryPercent = state.RecoveryPercent,
            ArchiveNameMode = state.ArchiveNameMode,
            WorkingFolder = state.WorkingFolder,
            MaxThreads = state.MaxThreads,
            SetArchiveTime = state.SetArchiveTime,
            ReadFilesOpenForWriting = state.ReadFilesOpenForWriting,
            StopOnUnreadableSource = state.StopOnUnreadableSource,
            DeleteSourcesAfter = state.DeleteSourcesAfter,
        };
    }

    private static string? ApplyArchiveToken(CliToken argument, ArchiveParseState state)
    {
        string token = argument.Text;
        if (!argument.IsSwitch)
        {
            if (state.ArchivePathArg is null)
            {
                state.ArchivePathArg = token;
            }
            else
            {
                state.SourcePaths.Add(token);
                state.SourceNames.Add(PositionalName(argument));
            }
            return null;
        }

        if (ArchiveFlags.TryGetValue(token, out Action<ArchiveParseState>? setFlag))
        {
            setFlag(state);
            return null;
        }
        if (SimpleSwitchPostfixError(token) is { } postfixError)
            return postfixError;

        if (token.StartsWith("-m", StringComparison.Ordinal))
            return ApplyMethodToken(token, state);

        if (token.StartsWith("-t", StringComparison.Ordinal))
        {
            if (!TryParseArchiveFormat(token, out ArchiveContainerFormat? format, out string? error))
                return error;
            state.ArchiveFormat = format!.Value;
            state.ArchiveTypeSwitch = token;
            return null;
        }

        if (token.StartsWith("-p", StringComparison.Ordinal))
        {
            (state.Password, state.PromptForPassword) = ParsePassword(token);
            return null;
        }

        if (token.StartsWith("-rr", StringComparison.Ordinal))
        {
            (state.RecoveryPercent, string? error) = ParseRecoveryPercent(token);
            return error;
        }

        if (token.StartsWith("-sa", StringComparison.Ordinal))
        {
            (state.ArchiveNameMode, string? error) = ParseArchiveNameMode(token);
            return error;
        }

        // T-F411: 7-Zip's working folder (an empty one is the system's temp). Pakko writes its
        // temporary archive next to the destination, so the commit is a rename on one volume;
        // Program checks only that a named folder exists, as 7-Zip fails without it.
        if (token.StartsWith("-w", StringComparison.Ordinal))
        {
            state.WorkingFolder = token[2..];
            return null;
        }

        return UnsupportedSwitchReason(token);
    }

    // T-F411: 7-Zip's -sa{s|e|a} (ArchiveCommandLine.cpp, ParseArcNameMode). -sas is Pakko's own rule
    // (T-F221 item 6: a name with no extension gets the type's), -sae writes the name as typed, -saa
    // always adds the type's extension. The letter is case-sensitive, as in 7-Zip (-SAE is exit 7).
    private static (CliArchiveNameMode Mode, string? Error) ParseArchiveNameMode(string token) => token switch
    {
        "-sas" => (CliArchiveNameMode.Smart, null),
        "-sae" => (CliArchiveNameMode.Exact, null),
        "-saa" => (CliArchiveNameMode.Always, null),
        _ => (CliArchiveNameMode.Smart, $"unknown -sa value: '{token[3..]}' (expected -sas, -sae or -saa)"),
    };

    // 7z's -m{params} on 'a': -mx=<0-9>, -mem=AES256 and -m0=<method> (7z only, T-F342) are
    // implemented, every other one is refused.
    private static string? ApplyMethodToken(string typed, ArchiveParseState state)
    {
        string token = PropertyNameInLowerCase(typed);
        if (token.StartsWith("-mx", StringComparison.Ordinal))
        {
            if (!TryParseCompressionLevel(token, out CompressionLevel? compressionLevel, out string? error))
                return error;
            state.CompressionLevel = compressionLevel;
            return null;
        }

        if (token.StartsWith("-mmt", StringComparison.Ordinal))
        {
            (state.MaxThreads, string? error) = ParseThreadCount(token);
            return error;
        }

        if (token.StartsWith("-mem", StringComparison.Ordinal))
            return EncryptionMethodError(token);

        if (token.StartsWith("-m0", StringComparison.Ordinal))
        {
            (state.SevenZipMethod, string? error) = ParseSevenZipMethod(token);
            return error;
        }

        if (token.Length > 2 && char.IsAsciiDigit(token[2]))
            return $"not supported by Pakko: {token[..3]} — a 7z archive is written with one method, named by -m0=<method>";

        return "not supported by Pakko: of 7z's -m{params}, only -mx=<0-9>, -mmt=<N>, -mem=AES256 and -m0=<method> (with -t7z) are implemented";
    }

    // The switches of 'a' that only switch something on.
    private static readonly Dictionary<string, Action<ArchiveParseState>> ArchiveFlags = new(StringComparer.Ordinal)
    {
        ["-so"] = state => state.WriteToStdout = true,
        ["-y"] = state => state.AssumeYes = true,
        ["-stl"] = state => state.SetArchiveTime = true,
        ["-ssw"] = state => state.ReadFilesOpenForWriting = true,
        ["-sse"] = state => state.StopOnUnreadableSource = true,
        ["-sdel"] = state => state.DeleteSourcesAfter = true,
    };

    // T-F411: 7-Zip's switches that take no value (SWFRM_SIMPLE in ArchiveCommandLine.cpp): one
    // with anything after its name is "Incorrect switch postfix" there.
    private static readonly string[] SimpleArchiveSwitches = ["-stl", "-ssw", "-sse", "-sdel"];

    private static string? SimpleSwitchPostfixError(string token)
    {
        string? name = Array.Find(SimpleArchiveSwitches, simple => token.StartsWith(simple, StringComparison.Ordinal));
        return name is null ? null : $"Incorrect command line: {name} takes no value, got '{token}'";
    }

    // T-F411: 7-Zip's -mmt (measured on 7za 26.02): bare, "=", "=on" and "0" follow the core count,
    // "=off" is one thread, a number with or without "=" is that many; "on" without "=", a sign or
    // a number past UInt32 is refused. on/off without case, as 7-Zip reads them.
    private static (int Threads, string? Error) ParseThreadCount(string token)
    {
        string value = token[4..];
        bool hasEquals = value.StartsWith('=');
        if (hasEquals)
            value = value[1..];
        if (value.Length == 0)
            return (0, null);
        if (hasEquals && CliSwitchNames.ToLowerAscii(value) is "on" or "off")
            return (value.Length == 3 ? 1 : 0, null);
        if (value.All(char.IsAsciiDigit) && uint.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out uint threads))
            return ((int)Math.Min(threads, (uint)int.MaxValue), null);
        return (0, $"-mmt takes a thread count (-mmt, -mmt4, -mmt=4, -mmt=on or -mmt=off), got '{token[4..]}'");
    }

    // T-F411: 7-Zip reads a property's name without case (-MX=9, -mEM=AES256); its value as typed.
    private static string PropertyNameInLowerCase(string token)
    {
        int end = 2;
        while (end < token.Length && char.IsAsciiLetter(token[end]))
            end++;
        return "-m" + CliSwitchNames.ToLowerAscii(token[2..end]) + token[end..];
    }

    // T-F342: the methods tar.exe's 7z writer has and Pakko offers. A bare name only: a method's
    // own parameters (-m0=lzma2:d=64m) have no counterpart there. Whichever -m0 comes last wins.
    private const string SevenZipMethodNames = "lzma2, lzma, bzip2, deflate, copy";

    private static (SevenZipMethod? Method, string? Error) ParseSevenZipMethod(string token)
    {
        if (!token.StartsWith("-m0=", StringComparison.Ordinal) || token.Length == 4)
            return (null, $"-m0 requires a method in the form -m0=<method>: {SevenZipMethodNames}");

        string method = token[4..];
        if (method.Contains(':'))
            return (null, $"not supported by Pakko: -m0={method} — a method's parameters cannot be set; name the method alone ({SevenZipMethodNames})");

        return method.ToUpperInvariant() switch
        {
            "LZMA2" => (SevenZipMethod.Lzma2, null),
            "LZMA" => (SevenZipMethod.Lzma, null),
            "BZIP2" => (SevenZipMethod.BZip2, null),
            "DEFLATE" => (SevenZipMethod.Deflate, null),
            "COPY" => (SevenZipMethod.Copy, null),
            "PPMD" or "ZSTD" =>
                (null, $"not supported by Pakko: -m0={method} — Windows' tar.exe, which writes the archive, has no {method} for 7z ({SevenZipMethodNames})"),
            _ => (null, $"unknown -m0 value: '{method}' (expected {SevenZipMethodNames})"),
        };
    }

    // T-F275: WinRAR's -rr[N] spelling (no 7z switch adds recovery data). A bare -rr is the App's
    // default, 5 %; 0 is refused rather than read as "none", since leaving -rr out means none.
    private const int DefaultRecoveryPercent = 5;

    private static (int Percent, string? Error) ParseRecoveryPercent(string token)
    {
        string value = token[3..];
        if (value.Length == 0)
            return (DefaultRecoveryPercent, null);
        bool number = value.Length <= 3 && value.All(char.IsAsciiDigit);
        int percent = number ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return percent is >= 1 and <= 100
            ? (percent, null)
            : (0, $"-rr{value}: recovery data is -rr (5 %) or -rr<N> with N from 1 to 100 (percent)");
    }

    // T-F193: Pakko writes WinZip AES-256 only, so -mem=AES256 (what 7z itself writes for a ZIP
    // given -p) is accepted as a no-op, and every weaker method is refused rather than silently
    // upgraded.
    private static string? EncryptionMethodError(string token)
    {
        if (!token.StartsWith("-mem=", StringComparison.Ordinal))
            return "-mem requires a method, e.g. -mem=AES256";

        string method = token[5..];
        return method.ToUpperInvariant() switch
        {
            "AES256" => null,
            "ZIPCRYPTO" or "AES128" or "AES192" =>
                $"not supported by Pakko: -mem={method} — Pakko only creates AES-256 encrypted ZIP archives",
            _ => $"unknown -mem value: '{method}' (Pakko supports only -mem=AES256)",
        };
    }

    // T-F411: 7-Zip takes -mx (level 9), -mxN and -mx=N, and reads a level above 9 as 9.
    private const int MaxCompressionLevel = 9;

    private static bool TryParseCompressionLevel(string token, out CompressionLevel? level, out string? error)
    {
        level = null;
        string valueText = token.Length > 3 && token[3] == '=' ? token[4..] : token[3..];
        if (valueText.Length == 0 && token.Length <= 4) // -mx and -mx=: 7-Zip's default level for x, 9
            valueText = MaxCompressionLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);

        bool digits = valueText.Length > 0 && valueText.All(char.IsAsciiDigit);
        if (!digits)
        {
            error = $"-mx takes a level 0-9 (-mx, -mx9 or -mx=9), got '{valueText}'";
            return false;
        }

        // An all-digit value too long for an int is above 9 as well.
        int mx = int.TryParse(valueText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? Math.Min(parsed, MaxCompressionLevel)
            : MaxCompressionLevel;

        level = CliCompressionLevelMapper.TryMap(mx);
        if (level is null)
        {
            error = $"-mx=<n> must be 0-9, got '{mx}'";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseArchiveFormat(string token, out ArchiveContainerFormat? format, out string? error)
    {
        format = null;
        if (token.Length <= 2)
        {
            error = "-t requires a type, e.g. -tzip or -ttar.gz";
            return false;
        }

        string typeValue = CliSwitchNames.ToLowerAscii(token[2..]); // T-F411: -tZIP, as 7-Zip reads it
        if (typeValue is "rar")
        {
            error = "not supported by Pakko: -trar — Pakko can only create ZIP, 7z and tar-family archives, rar is extract-only";
            return false;
        }

        format = typeValue switch
        {
            "zip" => ArchiveContainerFormat.Zip,
            "tar" => ArchiveContainerFormat.Tar,
            "tar.gz" => ArchiveContainerFormat.TarGz,
            "tar.bz2" => ArchiveContainerFormat.TarBz2,
            "tar.xz" => ArchiveContainerFormat.TarXz,
            "tar.zst" => ArchiveContainerFormat.TarZst,
            "tar.lzma" => ArchiveContainerFormat.TarLzma,
            "7z" => ArchiveContainerFormat.SevenZip,
            // T-F294: dot-free spellings PowerShell cannot split.
            "tgz" => ArchiveContainerFormat.TarGz,
            "tbz2" => ArchiveContainerFormat.TarBz2,
            "txz" => ArchiveContainerFormat.TarXz,
            "tzst" => ArchiveContainerFormat.TarZst,
            _ => null,
        };
        if (format is null)
        {
            error = $"unknown -t value: '{typeValue}' (expected zip, 7z, tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma, or tgz, tbz2, txz, tzst)";
            return false;
        }

        error = null;
        return true;
    }

    // --- l (List) ---

    private static ParsedCliCommand ParseList(CliToken[] rest)
    {
        var names = new List<CliFilterName>();
        bool readFromStdin = false;
        bool assumeYes = false;
        string? password = null;
        bool promptForPassword = false;
        foreach (CliToken argument in rest)
        {
            (string token, bool isSwitch) = argument;
            if (!isSwitch)
            {
                names.Add(PositionalName(argument));
                continue;
            }
            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }
            // T-F409: 7-Zip takes -y and -p on 'l', and patool passes "-y -p-". Listing asks nothing
            // and needs no password: ZIP names are not encrypted, and a 7z with encrypted headers is
            // refused as before, since Pakko gives tar.exe no password.
            if (token == "-y")
            {
                assumeYes = true;
                continue;
            }
            if (token.StartsWith("-p", StringComparison.Ordinal))
            {
                (password, promptForPassword) = ParsePassword(token);
                continue;
            }
            return Invalid(UnsupportedSwitchReason(token));
        }

        if (readFromStdin && promptForPassword)
            return Invalid(BarePasswordWithStdinReason);

        return new ParsedCliCommand
        {
            Type = CliCommandType.List, FilterNames = names, ReadFromStdin = readFromStdin, AssumeYes = assumeYes,
            Password = password, PromptForPassword = promptForPassword,
        };
    }

    // --- h (Hash, T-F128/T-F09 follow-up) ---

    private static ParsedCliCommand ParseHash(CliToken[] rest)
    {
        if (HasSecondHashMethod(rest))
            return Invalid("not supported by Pakko: more than one -scrc — Pakko computes one hash method per run");

        var paths = new List<string>();
        HashAlgorithmKind algorithm = HashAlgorithmKind.Crc32; // matches real 7z's own default hash method
        bool readFromStdin = false;

        foreach ((string token, bool isSwitch) in rest)
        {
            if (!isSwitch)
            {
                paths.Add(token);
                continue;
            }

            if (token == "-si")
            {
                readFromStdin = true;
                continue;
            }

            if (token.StartsWith("-scrc", StringComparison.Ordinal))
            {
                if (!TryParseHashAlgorithm(token, out HashAlgorithmKind? mapped, out string? error))
                    return Invalid(error!);
                algorithm = mapped!.Value;
                continue;
            }

            return Invalid(UnsupportedSwitchReason(token));
        }

        if (readFromStdin && paths.Count > 0)
            return Invalid("'-si' cannot be combined with an explicit path");
        if (!readFromStdin && paths.Count == 0)
            return Invalid("'h' requires at least one file or folder path");

        return new ParsedCliCommand
        {
            Type = CliCommandType.Hash,
            SourcePaths = paths,
            HashAlgorithm = algorithm,
            ReadFromStdin = readFromStdin,
        };
    }

    // T-F411: 7-Zip computes every -scrc method given; Pakko computes one, so a second is refused
    // rather than silently dropped.
    private static bool HasSecondHashMethod(CliToken[] rest) =>
        rest.Count(token => token.IsSwitch && token.Text.StartsWith("-scrc", StringComparison.Ordinal)) > 1;

    private static bool TryParseHashAlgorithm(string token, out HashAlgorithmKind? algorithm, out string? error)
    {
        algorithm = null;
        if (token.Length <= 5)
        {
            error = "-scrc requires a method, e.g. -scrcCRC32 or -scrcSHA256";
            return false;
        }

        string method = token[5..];
        algorithm = method.ToUpperInvariant() switch
        {
            "CRC32" => HashAlgorithmKind.Crc32,
            "SHA256" => HashAlgorithmKind.Sha256,
            _ => null,
        };
        if (algorithm is null)
        {
            error = $"not supported by Pakko: -scrc{method} — only CRC32 and SHA256 are implemented";
            return false;
        }

        error = null;
        return true;
    }

    // --- shared helpers ---

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
    private const string NotSupportedOnThisCommand = "not supported on this command";

    // Scanned in order, first match wins. Every prefix is exclusive of the others except "-rr",
    // which starts with "-r" and so must stay above it (T-F275). "-mx" needs no separate entry: it
    // already starts with "-m".
    private static readonly (string Prefix, string Message)[] UnsupportedSwitchPrefixes =
    [
        ("-ao", NotSupportedOnThisCommand),
        ("-m", "not supported on this command: -m{params} is only meaningful for 'a' (archive creation)"),
        ("-t", "not supported on this command: -t{type} is only meaningful for 'a' (archive creation)"),
        ("-scrc", "not supported on this command: -scrc{method} is only meaningful for 'h' (hash)"),
        ("-o", NotSupportedOnThisCommand),
        ("-p", "not supported on this command: -p{pwd} is only valid with 'x' (extract), 't' (test), 'l' (list) or 'a' (create an AES-256 ZIP)"),
        ("-rr", "not supported on this command: -rr[N] (recovery data) is only meaningful for 'a' (archive creation)"),
        ("-r", "unknown -r value: expected -r, -r- or -r0 (T-F410)"),
        ("-v", "not supported: no multi-part/split-archive logic exists in Pakko"),
        ("-ssc", "unknown -ssc value: expected -ssc or -ssc- (T-F410)"),
        ("-snz", "not supported on this command: -snz is only valid with 'x' (extract)"),
        ("-sa", "not supported on this command: -sa{s|e|a} names the archive 'a' creates"),
        ("-w", "not supported on this command: -w{dir} is only meaningful for 'a' (archive creation)"),
        ("-stl", "not supported on this command: -stl (the archive takes its newest file's time) is only meaningful for 'a'"),
        ("-ssw", "not supported on this command: -ssw (read files open for writing) is only meaningful for 'a'"),
        ("-sse", "not supported on this command: -sse (stop at a file that cannot be read) is only meaningful for 'a'"),
        ("-spe", "not supported on this command: -spe (drop the root folder named like -o) is only meaningful for 'x'"),
        ("-sdel", "not supported on this command: -sdel (delete the sources once archived) is only meaningful for 'a'"),
        ("-ssp", "not supported by Pakko: -ssp (keep the sources' last access time) - tar.exe cannot keep it, and Windows mostly does not update it"),
    ];

    private static string UnsupportedSwitchReason(string token)
    {
        // T-F419: CLI.md's rule 3 names the switch; the bare reason did not.
        if (token is "-si" or "-so" or "-y")
            return $"{NotSupportedOnThisCommand}: {token}";

        foreach ((string prefix, string message) in UnsupportedSwitchPrefixes)
        {
            if (token.StartsWith(prefix, StringComparison.Ordinal))
                return message == NotSupportedOnThisCommand ? $"{message}: {token}" : message;
        }

        return $"Incorrect command line: unknown switch '{token}'";
    }

    private static ParsedCliCommand Invalid(string message) =>
        new() { Type = CliCommandType.Invalid, ErrorMessage = message };
}
