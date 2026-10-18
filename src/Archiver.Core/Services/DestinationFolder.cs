using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F406: the folder an extraction or a new archive goes into. A file of that name, a folder
/// without access or a malformed path made <see cref="Directory.CreateDirectory(string)"/> throw
/// out of the service (the CLI died with an unhandled exception); now the call ends with an error
/// on what it was given and writes nothing.
/// </summary>
internal static class DestinationFolder
{
    /// <summary>Creates <paramref name="folder"/> when it is missing; returns what stopped it, or null.</summary>
    internal static Exception? TryCreate(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex;
        }
    }

    /// <summary>The result of an extraction whose folder could not be made: an error per archive.</summary>
    internal static ArchiveResult ExtractionRefused(IReadOnlyList<string> archivePaths, Exception failure) => new()
    {
        Errors = [.. archivePaths.Select(path => CoreMessages.Error(path, CoreMessages.Wrap(MessageCode.CannotExtractArchive, failure), failure))],
        Sources = [.. archivePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed })],
    };

    /// <summary>T-F419: a single archive named exactly as a device ("NUL", "CON ", only with an
    /// exact name, -sae): Windows opens the device instead, or a long path writes a file Explorer
    /// cannot remove. Null when the name can be used; nothing is written otherwise.</summary>
    internal static ArchiveResult? ReservedArchiveName(ArchiveOptions options)
    {
        if (options.Mode != ArchiveMode.SingleArchive || options.ExactFileName is not { } name
            || !ArchiveEntrySecurity.IsDeviceName(Path.GetFileName(name)))
            return null;
        return new()
        {
            Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.ArchiveNameReserved, name)],
            Sources = [.. options.SourcePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed })],
        };
    }

    /// <summary>The result of an archive creation whose folder could not be made.</summary>
    internal static ArchiveResult CreationRefused(ArchiveOptions options, Exception failure) => new()
    {
        Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), CoreMessages.Wrap(MessageCode.CannotCreateArchive, failure), failure)],
        Sources = [.. options.SourcePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed })],
    };
}
