namespace Archiver.Core.Services;

/// <summary>
/// T-F97: safe-preview-type allowlist for the Archive Browser's double-click-to-preview feature.
/// Deliberately conservative — only formats with no known code/macro/script execution path via
/// their typical OS default handler. See SECURITY.md.
/// </summary>
public static class PreviewPolicy
{
    private static readonly HashSet<string> _previewableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".txt", ".md", ".log", ".ini", ".csv", ".json", ".xml", ".yaml", ".yml",
        // T-F109: common video/audio containers — no known macro/script execution path via the OS
        // default player, same reasoning as images/text. See SECURITY.md.
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm",
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac",
    };

    /// <summary>True if a double-click on this entry should preview instead of extract.</summary>
    public static bool IsPreviewable(string entryName) =>
        _previewableExtensions.Contains(Path.GetExtension(entryName));
}
