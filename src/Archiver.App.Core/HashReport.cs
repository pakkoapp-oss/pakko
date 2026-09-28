using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>
/// T-F219: what the App's "Hash..." hashes and how its result is named and copied. SHA-256 only
/// (T-F164, user decision, confirmed again for T-F219).
/// </summary>
public static class HashReport
{
    /// <summary>The pending list's paths, or null when the list is empty and the user picks files.</summary>
    public static IReadOnlyList<string>? SourcesFor(IReadOnlyList<string> listed) => listed.Count > 0 ? listed : null;

    /// <summary>
    /// The name a hashed file is shown and copied under: relative to the parent of the listed
    /// folder it was found in, else its file name.
    /// </summary>
    public static string DisplayName(string sourcePath, IReadOnlyList<string> requested)
    {
        foreach (string folder in requested)
        {
            // A drive root ("C:\") keeps its separator after trimming.
            string trimmed = Path.TrimEndingDirectorySeparator(folder);
            string prefix = Path.EndsInDirectorySeparator(trimmed) ? trimmed : trimmed + Path.DirectorySeparatorChar;
            if (sourcePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(Path.GetDirectoryName(trimmed) ?? trimmed, sourcePath);
        }
        return Path.GetFileName(sourcePath);
    }

    /// <summary>
    /// The copied text: one "hash  name" line per hashed file, the format <c>sha256sum -c</c>
    /// reads back. Files that failed are left out.
    /// </summary>
    public static string CopyText(HashResult result, IReadOnlyList<string> requested) =>
        string.Join(Environment.NewLine, result.Entries
            .Where(e => e.Hash is not null)
            .Select(e => $"{e.Hash}  {DisplayName(e.SourcePath, requested)}"));
}
