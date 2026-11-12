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
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // T-F419: the folder's name is the only argument here; said in Core's words, not .NET's.
            return new CoreTextIOException(CoreMessages.Text(MessageCode.DestinationFolderNameInvalid, folder), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
    /// cannot remove. T-F475: or any single archive whose path <see cref="IsDevice"/>. Null when the
    /// name can be used; nothing is written otherwise.</summary>
    internal static ArchiveResult? ReservedArchiveName(ArchiveOptions options)
    {
        if (options.Mode != ArchiveMode.SingleArchive)
            return null;
        string name = ArchiveNaming.SingleArchiveFileName(options);
        bool exactDeviceName = options.ExactFileName is not null && ArchiveEntrySecurity.IsDeviceName(Path.GetFileName(name));
        // A folder that is a device fails TryCreate with its own error; the name is not the problem.
        if (!exactDeviceName && (IsDevice(options.DestinationFolder) || !IsDevice(Path.Combine(options.DestinationFolder, name))))
            return null;
        return new()
        {
            Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.ArchiveNameReserved, name)],
            Sources = [.. options.SourcePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed })],
        };
    }

    /// <summary>T-F475: whether Windows opens <paramref name="archivePath"/> as a device - the path
    /// itself (no folder above it; "CON.zip" in any folder on Windows 10 and Server 2022) or a path
    /// under a device's root (<c>\\.\NUL\o.zip</c>). <see cref="Path.GetFullPath(string)"/> applies
    /// the rules of the Windows it runs on. False for a path that cannot be parsed: the folder's
    /// own check reports that one.</summary>
    internal static bool IsDevice(string archivePath)
    {
        if (InputPaths.Problem(archivePath) is not null)
            return false;
        string fullPath = Path.GetFullPath(archivePath);
        if (Path.GetDirectoryName(fullPath) is null)
            return true;
        string root = Path.GetPathRoot(fullPath)!; // has a folder above it, so a root too
        return (root.StartsWith(@"\\.\", StringComparison.Ordinal) || root.StartsWith(@"\\?\", StringComparison.Ordinal))
            && ArchiveEntrySecurity.IsDeviceName(root[4..].TrimEnd('\\'));
    }

    /// <summary>The result of an archive creation whose folder could not be made.</summary>
    internal static ArchiveResult CreationRefused(ArchiveOptions options, Exception failure) => new()
    {
        Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), CoreMessages.Wrap(MessageCode.CannotCreateArchive, failure), failure)],
        Sources = [.. options.SourcePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed })],
    };
}
