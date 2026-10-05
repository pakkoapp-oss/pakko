namespace Archiver.Core.Services;

/// <summary>
/// The form of a source path every archive-creation engine names entries from: no trailing
/// separator (T-F153), and a relative or dot-named path (".", "..", "C:") resolved to the folder
/// it stands for (T-F338) - its last segment is otherwise "." and becomes the entry root.
/// </summary>
internal static class SourcePathNormalizer
{
    public static string Normalize(string path)
    {
        // A drive root ("C:\") keeps its separator - ArchiveNaming's drive-root handling needs it (T-F99).
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        if (Path.IsPathFullyQualified(trimmed) && Path.GetFileName(trimmed) is not ("." or ".."))
            return trimmed;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a path Windows can resolve: left as typed, the engine reports it per item.
            return trimmed;
        }
    }
}
