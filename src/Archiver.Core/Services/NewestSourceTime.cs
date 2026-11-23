using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F411: the newest modification time among what an archive of these sources holds (7-Zip's
/// -stl, measured on 7za 26.02: folders count too). Walked the way both engines walk a source:
/// its own entry, then <see cref="DirectoryWalker"/>, which never enters a link; what the filter
/// leaves out, and a folder that cannot be listed, add nothing.
/// </summary>
internal static class NewestSourceTime
{
    public static DateTime? Find(IReadOnlyList<string> sourcePaths, SourceEntryNames names)
    {
        DateTime? newest = null;
        foreach (string source in sourcePaths)
        {
            // T-F509: a refused source ("a.txt ") by its plain path is its twin's; T-F531: written as \\?\
            // it is itself, and ZIP archives it (tar refuses it, and its time then dates no archive).
            if (InputPaths.SpelledProblem(source) is not null)
                continue;
            bool isFolder = Directory.Exists(source);
            string name = names.For(source);
            if (name.Length > 0 && !names.Keeps(name, isFolder))
                continue;
            if (!isFolder)
            {
                if (File.Exists(source))
                    newest = Later(newest, File.GetLastWriteTimeUtc(source));
                continue;
            }
            foreach (WalkEntry entry in DirectoryWalker.Walk(source, names.LeaveOutBelow(source, name), spelledBelow: true)
                .Where(entry => IsArchivedContent(entry.Kind)))
            {
                newest = Later(newest, entry.Info.LastWriteTimeUtc);
            }
        }
        return newest;
    }

    private static bool IsArchivedContent(WalkEntryKind kind)
    {
        switch (kind)
        {
            case WalkEntryKind.Directory:
            case WalkEntryKind.File:
                return true;
            // Not archived as content, so its time is not the archive's.
            case WalkEntryKind.ReparsePoint:
            case WalkEntryKind.UnreadableDirectory:
            case WalkEntryKind.LeftOut:
            case WalkEntryKind.TrimmedName:
            case WalkEntryKind.DeviceName:
                return false;
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }

    private static DateTime Later(DateTime? current, DateTime candidate) =>
        current is { } known && known >= candidate ? known : candidate;
}
