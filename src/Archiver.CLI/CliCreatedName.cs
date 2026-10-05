namespace Archiver.CLI;

/// <summary>
/// T-F325: 'a' never renames on its own, so an archive that landed under another name than the
/// one asked for means another run took the name while this one was writing (T-F321).
/// </summary>
public static class CliCreatedName
{
    /// <summary>The stderr line for an archive that landed elsewhere than
    /// <paramref name="requestedPath"/>; null when it is where it was asked to be.</summary>
    public static string? TakenLine(string requestedPath, IReadOnlyList<string> createdFiles)
    {
        if (createdFiles.Count != 1)
            return null;

        // GetFullPath drops the trailing dot or space Windows drops from the name on disk.
        string requested = Path.GetFileName(Path.GetFullPath(requestedPath));
        string landed = Path.GetFileName(createdFiles[0]);
        return string.Equals(landed, requested, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"pakko: warning: created '{landed}': the name '{requested}' was taken while compressing";
    }
}
