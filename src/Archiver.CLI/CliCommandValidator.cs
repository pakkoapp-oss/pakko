using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.CLI;

/// <summary>
/// T-F296 item 1: rules across switches, checked after each switch was parsed on its own and before
/// anything runs. A contradiction is a command-line error (exit 7), never a guess.
/// </summary>
public static class CliCommandValidator
{
    /// <summary>The command, or an <see cref="CliCommandType.Invalid"/> one naming the first broken rule.</summary>
    public static ParsedCliCommand Validate(ParsedCliCommand command)
    {
        string? error = command.Type == CliCommandType.Archive ? ArchiveRules(command) : null;
        return error is null ? command : new ParsedCliCommand { Type = CliCommandType.Invalid, ErrorMessage = error };
    }

    private static string? ArchiveRules(ParsedCliCommand command)
    {
        if ((command.Password is not null || command.PromptForPassword) && command.ArchiveFormat != ArchiveContainerFormat.Zip)
            return "not supported by Pakko: -p on a tar-family archive — only ZIP archives can be password-protected";

        return TypeAgainstName(command);
    }

    // T-F294: "-ttar" + a name ending .gz wrote an uncompressed tar named .gz. Only a name that ends
    // in an archive type Pakko writes counts; any other extension is written as typed (T-F221 item 6).
    private static string? TypeAgainstName(ParsedCliCommand command)
    {
        string name = Path.GetFileName(command.ArchivePathArg!);
        ArchiveContainerFormat? named = FormatOfName(name);
        if (named is not { } nameFormat || nameFormat == command.ArchiveFormat)
            return null;

        string extension = ArchiveNaming.GetExtension(nameFormat);
        return command.ArchiveTypeSwitch is { } typeSwitch
            ? $"the archive name '{name}' ends in {extension}, but {typeSwitch} asks for {ArchiveNaming.GetExtension(command.ArchiveFormat)}; make the name and the type agree"
            : $"the archive name '{name}' ends in {extension}, but no -t was given, so it would be a ZIP; add {SwitchFor(nameFormat)}";
    }

    private static ArchiveContainerFormat? FormatOfName(string name)
    {
        ArchiveContainerFormat? best = null;
        int bestLength = 0;
        foreach (ArchiveContainerFormat format in Enum.GetValues<ArchiveContainerFormat>())
        {
            string extension = ArchiveNaming.GetExtension(format);
            if (extension.Length > bestLength && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                best = format;
                bestLength = extension.Length;
            }
        }
        return best;
    }

    private static string SwitchFor(ArchiveContainerFormat format) => format switch
    {
        ArchiveContainerFormat.Tar => "-ttar",
        ArchiveContainerFormat.TarGz => "-ttar.gz (or -ttgz)",
        ArchiveContainerFormat.TarBz2 => "-ttar.bz2 (or -ttbz2)",
        ArchiveContainerFormat.TarXz => "-ttar.xz (or -ttxz)",
        ArchiveContainerFormat.TarZst => "-ttar.zst (or -ttzst)",
        ArchiveContainerFormat.TarLzma => "-ttar.lzma",
        _ => "-tzip",
    };
}
