using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F406: the paths a public operation is given are checked at its entry. An empty path or one
/// Windows cannot parse threw <see cref="ArgumentException"/> deep inside an engine, past catch
/// blocks that rightly take only I/O failures; here it becomes the "does not exist" error the
/// missing path already gives. <c>ArgumentException</c> is caught only around
/// <see cref="Path.GetFullPath(string)"/>, where the path is its one possible cause.
/// </summary>
internal static class InputPaths
{
    /// <summary>T-F509: whether a part of <paramref name="path"/> after its root ends in a space or a
    /// dot ("a.txt ", "d."). Windows drops it when it opens the plain path, which then names another
    /// file (a twin "a.txt" beside it) or none. Judged on the string as given: <see
    /// cref="Path.GetFullPath(string)"/> would already have trimmed it.</summary>
    internal static bool NameTrimmed(string path)
    {
        string root = Path.GetPathRoot(path) ?? "";
        return EntryDiskName.Trims(path[root.Length..]);
    }

    /// <summary>T-F509: <see cref="NameTrimmed"/> for a file met while walking <paramref name="fullRoot"/>,
    /// judged on the part below the root (the root itself was judged as a source). A path not under
    /// the root counts as trimmed: the walk would otherwise pass it unjudged.</summary>
    internal static bool NameTrimmedBelow(string fullRoot, string fullName) =>
        !fullName.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
        || EntryDiskName.Trims(fullName[fullRoot.Length..]);

    /// <summary>Why <paramref name="path"/> cannot be opened at all, or null when it can be tried.</summary>
    internal static CoreText? Problem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CoreMessages.Text(MessageCode.SourceNotFound, path ?? "");
        // T-F509: by this path Windows opens another file (a twin) or none.
        if (NameTrimmed(path))
            return CoreMessages.Text(MessageCode.SourceNameTrimmed, path);
        try
        {
            _ = Path.GetFullPath(path);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CoreMessages.Text(MessageCode.SourceNotFound, path);
        }
    }
}
