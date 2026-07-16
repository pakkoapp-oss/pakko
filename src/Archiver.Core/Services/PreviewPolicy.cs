namespace Archiver.Core.Services;

// T-F97: safe-preview-type allowlist for the Archive Browser's double-click-to-preview feature.
// Deliberately conservative — only formats with no known code/macro/script execution path via
// their typical OS default handler. See SECURITY.md.
public static class PreviewPolicy
{
    private static readonly HashSet<string> _previewableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".txt", ".md", ".log", ".ini", ".csv", ".json", ".xml", ".yaml", ".yml",
    };

    public static bool IsPreviewable(string entryName) =>
        _previewableExtensions.Contains(Path.GetExtension(entryName));
}
