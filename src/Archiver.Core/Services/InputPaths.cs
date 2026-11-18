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
    public static bool NameTrimmed(string path) => TrimmedPartEnd(path) >= 0;

    // T-F557: where in path the first part Windows trims ends ("C:\x\d.\f.txt" -> after "d."), or -1.
    private static int TrimmedPartEnd(string path)
    {
        int rootLength = (Path.GetPathRoot(path) ?? "").Length;
        string[] parts = path[rootLength..].TrimEnd(Separators).Split(Separators);
        int end = rootLength;
        for (int i = 0; i < parts.Length; i++)
        {
            end += parts[i].Length;
            if (PartTrimmed(parts[i], last: i == parts.Length - 1))
                return end;
            end++;
        }
        return -1;
    }

    /// <summary>T-F531: whether any part of <paramref name="path"/> ends as Windows trims a last part
    /// ("d ", "d..", "a.txt "). Such a part in the middle opens as spelled, but an archive entry's name has
    /// every part trimmed on extraction (<c>EntryDiskName</c>), so the name would not come back.</summary>
    internal static bool AnyPartTrimmedAsLast(string path)
    {
        string[] parts = path[(Path.GetPathRoot(path) ?? "").Length..].TrimEnd(Separators).Split(Separators);
        return Array.Exists(parts, part => PartTrimmed(part, last: true));
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

    /// <summary>T-F547: the <c>\\?\</c> form of a full path, which Windows opens as spelled (no part
    /// trimmed) and at any length: a <c>\\?\</c> path as it is, <c>\\.\</c> and <c>\??\</c> (T-F552: .NET
    /// and Windows take it as <c>\\?\</c>) as <c>\\?\</c>, a share as <c>\\?\UNC\</c>. The one place Core makes it (<c>QuarantineFiles</c>, <c>WorkRoot</c>,
    /// <c>TempOwner</c>).</summary>
    internal static string Literal(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        if (fullPath.StartsWith(@"\\.\", StringComparison.Ordinal) || fullPath.StartsWith(@"\??\", StringComparison.Ordinal))
            return @"\\?\" + fullPath[4..];
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + fullPath[2..];
        return @"\\?\" + fullPath;
    }

    /// <summary>T-F531: <paramref name="path"/> without its <c>\\?\</c> prefix where the plain path names
    /// the same file - a drive or a share after the prefix, and no part Windows would trim; otherwise
    /// as it is. A final path read from a handle ("a.txt ") would otherwise come back as its twin's.</summary>
    public static string SamePlainPath(string path)
    {
        string plain;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            plain = @"\\" + path[8..];
        else if (path.Length >= 6 && path.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(path[4]) && path[5] == ':')
            plain = path[4..];
        else
            return path;
        return NameTrimmed(plain) ? path : plain;
    }

    /// <summary><see cref="Problem"/>, or "not found" for an archive path that is no file (T-F221: not
    /// "not a recognized archive"). The twin of "x.zip " is a file, so the order matters (T-F509).</summary>
    internal static CoreText? ArchiveProblem(string archivePath) =>
        Problem(archivePath) ?? (File.Exists(archivePath) ? null : CoreMessages.Text(MessageCode.SourceNotFound, archivePath));

    /// <summary>T-F531: <see cref="Problem"/>, but a name Windows trims written as <c>\\?\</c> is no problem:
    /// Windows opens it as spelled. Only where every use of the path keeps that spelling (ZIP's creation).</summary>
    internal static CoreText? SpelledProblem(string? path) =>
        Problem(path) is { } problem && !(problem.Code == MessageCode.SourceNameTrimmed && path!.StartsWith(@"\\?\", StringComparison.Ordinal))
            ? problem
            : null;

    /// <summary>Why <paramref name="path"/> cannot be opened at all, or null when it can be tried.
    /// T-F558: a path Windows opens as a device is no file ("not found"): "CON.zip" is the console on
    /// Windows 10 and Server 2022, and reading it blocked.</summary>
    internal static CoreText? Problem(string? path) =>
        ParseProblem(path) ?? (IsDeviceFullPath(Path.GetFullPath(path!)) ? CoreMessages.Text(MessageCode.SourceNotFound, path!) : null);

    /// <summary>T-F475: whether Windows opens <paramref name="path"/> as a device - the path itself (no
    /// folder above it; "CON.zip" in any folder on Windows 10 and Server 2022) or a path under a device's
    /// root (<c>\\.\NUL\o.zip</c>). <see cref="Path.GetFullPath(string)"/> applies the rules of the Windows
    /// it runs on. False for a path that cannot be parsed.</summary>
    internal static bool NamesADevice(string path) => ParseProblem(path) is null && IsDeviceFullPath(Path.GetFullPath(path));

    private static bool IsDeviceFullPath(string fullPath)
    {
        if (Path.GetDirectoryName(fullPath) is null)
            return true;
        string root = Path.GetPathRoot(fullPath)!; // has a folder above it, so a root too
        return (root.StartsWith(@"\\.\", StringComparison.Ordinal) || root.StartsWith(@"\\?\", StringComparison.Ordinal))
            && ArchiveEntrySecurity.IsDeviceName(root[4..].TrimEnd('\\'));
    }

    private static CoreText? ParseProblem(string? path)
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
        // T-F509: by this path Windows opens another file (a twin) or none. T-F557: the text names the
        // part that ends in a space or a dot, a folder above the file where that is the one.
        int trimmedEnd = TrimmedPartEnd(path);
        return trimmedEnd >= 0 ? CoreMessages.Text(MessageCode.SourceNameTrimmed, path[..trimmedEnd]) : null;
    }
}
