using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F406: the paths a public operation is given are checked at its entry. An empty path or one
/// Windows cannot parse threw <see cref="ArgumentException"/> deep inside an engine, past catch
/// blocks that rightly take only I/O failures; here it becomes the "does not exist" error the
/// missing path already gives. <c>ArgumentException</c> is caught only around
/// <see cref="Path.GetFullPath(string)"/>, where the path is its one possible cause.
/// </summary>
public static class InputPaths
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>T-F509: whether Windows changes a part of <paramref name="path"/> below its root when it
    /// opens the plain path, which then names another file (a twin "a.txt" for "a.txt ") or none. It
    /// drops every trailing dot and space of the last part, but only one trailing dot of a part in the
    /// middle ("d." is "d"; "d ", "d.." stay). Asked of <see cref="Path.GetFullPath(string)"/> one part
    /// at a time, so the rule is the running Windows' own; the whole path through it would already be
    /// trimmed. A <c>\\?\</c> path is judged the same way (T-F510 is where such names get archived).
    /// Trailing separators do not make the last name a middle one: Core normalizes them away
    /// (<c>TrimEndingDirectorySeparator</c>), and "d \" then opens "d".</summary>
    public static bool NameTrimmed(string path)
    {
        string[] parts = path[(Path.GetPathRoot(path) ?? "").Length..].TrimEnd(Separators).Split(Separators);
        for (int i = 0; i < parts.Length; i++)
        {
            if (PartTrimmed(parts[i], last: i == parts.Length - 1))
                return true;
        }
        return false;
    }

    /// <summary>T-F509: <see cref="NameTrimmed"/> for one name met inside a folder, judged as the last part
    /// of a path: the consumers of a walk open and delete each entry, a folder included, by its own path.</summary>
    internal static bool OwnNameTrimmed(string name) => PartTrimmed(name, last: true);

    private static bool PartTrimmed(string part, bool last)
    {
        // A NUL makes GetFullPath throw; such a path is refused by Problem as unparsable.
        if (part.Length == 0 || part is "." or ".." || (part[^1] != ' ' && part[^1] != '.') || part.Contains('\0'))
            return false;
        string probe = @"C:\x\" + part + (last ? "" : @"\f");
        return !string.Equals(Path.GetFullPath(probe), probe, StringComparison.Ordinal);
    }

    /// <summary><see cref="Problem"/>, or "not found" for an archive path that is no file (T-F221: not
    /// "not a recognized archive"). The twin of "x.zip " is a file, so the order matters (T-F509).</summary>
    internal static CoreText? ArchiveProblem(string archivePath) =>
        Problem(archivePath) ?? (File.Exists(archivePath) ? null : CoreMessages.Text(MessageCode.SourceNotFound, archivePath));

    /// <summary>Why <paramref name="path"/> cannot be opened at all, or null when it can be tried.</summary>
    internal static CoreText? Problem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CoreMessages.Text(MessageCode.SourceNotFound, path ?? "");
        try
        {
            _ = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CoreMessages.Text(MessageCode.SourceNotFound, path);
        }
        // T-F509: by this path Windows opens another file (a twin) or none.
        return NameTrimmed(path) ? CoreMessages.Text(MessageCode.SourceNameTrimmed, path) : null;
    }
}
