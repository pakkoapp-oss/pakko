using System.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F391: the tar.exe exclude pattern that matches one entry and what lies under it, and nothing
/// else. A plain path is matched anywhere in an entry's name ("src/sub/link" also takes
/// "src/x/src/sub/link"), and "[", "]" and a trailing "$" are pattern syntax, so the pattern is
/// anchored with "^" and every such character is escaped (measured on bsdtar 3.8.8,
/// docs/DECISIONS.md).
/// </summary>
internal static class TarExcludePattern
{
    private const string Syntax = @"\[]*?$^";
    // The separator inside a tar entry name, on every system.
    private const char EntrySeparator = '/';

    /// <summary>The pattern for <paramref name="relativePath"/> under the entry tar.exe names <paramref name="entryRoot"/>.</summary>
    public static string For(string entryRoot, string relativePath)
    {
        string path = entryRoot + EntrySeparator + relativePath.Replace(Path.DirectorySeparatorChar, EntrySeparator);
        var pattern = new StringBuilder("^", path.Length + 8);
        foreach (char c in path)
        {
            if (Syntax.Contains(c))
                pattern.Append('\\');
            pattern.Append(c);
        }
        return pattern.ToString();
    }
}
