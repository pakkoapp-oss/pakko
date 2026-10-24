namespace Archiver.CLI;

/// <summary>
/// T-F325: 'a' never renames on its own, so an archive that landed under another name than the
/// one asked for means another run took the name while this one was writing (T-F321) - or that
/// the name is one of the run's own sources, which is never replaced (T-F421).
/// </summary>
public static class CliCreatedName
{
    /// <summary>The stderr line for an archive that landed elsewhere than
    /// <paramref name="requestedPath"/>; null when it is where it was asked to be.
    /// <paramref name="sourcePaths"/> (full paths): T-F421, a requested name that is one of them was
    /// numbered by Core, since a source of the run is never replaced.</summary>
    public static string? TakenLine(string requestedPath, IReadOnlyList<string> createdFiles, IReadOnlyList<string>? sourcePaths = null)
    {
        if (createdFiles.Count != 1)
            return null;

        // GetFullPath drops the trailing dot or space Windows drops from the name on disk; Core
        // reports the created path as it was given, so both sides need it (T-F419).
        string requested = Path.GetFileName(Path.GetFullPath(requestedPath));
        string landed = Path.GetFileName(Path.GetFullPath(createdFiles[0]));
        if (string.Equals(landed, requested, StringComparison.OrdinalIgnoreCase))
            return null;
        string requestedFull = Path.GetFullPath(requestedPath);
        bool isASource = sourcePaths?.Any(s => string.Equals(Path.GetFullPath(s), requestedFull, StringComparison.OrdinalIgnoreCase)) == true;
        return isASource
            ? $"pakko: warning: created '{landed}': the name '{requested}' is one of the files being archived"
            : $"pakko: warning: created '{landed}': the name '{requested}' was taken while compressing";
    }
}
