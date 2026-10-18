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
            foreach (WalkEntry entry in DirectoryWalker.Walk(source, names.LeaveOutBelow(source, name))
                .Where(entry => entry.Kind is WalkEntryKind.Directory or WalkEntryKind.File))
            {
                newest = Later(newest, entry.Info.LastWriteTimeUtc);
            }
        }
        return newest;
    }

    private static DateTime Later(DateTime? current, DateTime candidate) =>
        current is { } known && known >= candidate ? known : candidate;
}
