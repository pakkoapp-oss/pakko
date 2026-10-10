using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>What double-clicking a row does (T-F242 item 3: this was decided in code-behind).</summary>
public enum RowOpenAction
{
    /// <summary>Nothing happens.</summary>
    None,

    /// <summary>Show the folder's contents.</summary>
    OpenFolder,

    /// <summary>Browse the archive on disk.</summary>
    OpenArchive,

    /// <summary>Extract the archive entry to a temp scope and browse it (T-F98).</summary>
    DrillIntoNestedArchive,

    /// <summary>Extract to a temp scope and open with the default app (T-F97).</summary>
    Preview,

    /// <summary>Warn, then extract next to the archive (T-F109).</summary>
    ExtractWithWarning,
}

/// <summary>
/// T-F242 items 3, 4 and 6: the double-click decisions of the pending list and the Archive
/// Browser, and the containment check for a file extracted into a temp scope.
/// </summary>
public static class BrowserEntryRouting
{
    /// <summary>
    /// A pending-list row: only a real archive on disk opens (magic bytes, the same ground truth
    /// ExtractionRouter uses — never the extension); nothing while an operation runs.
    /// </summary>
    public static RowOpenAction DecidePendingRow(bool isBusy, bool isFolder, Func<bool> isArchiveOnDisk) =>
        isBusy || isFolder || !isArchiveOnDisk() ? RowOpenAction.None : RowOpenAction.OpenArchive;

    /// <summary>
    /// An Archive Browser row. Outside an archive (T-F107) a file opens only when it is a real
    /// archive; inside one an archive entry drills in (T-F98), a previewable one previews (T-F97)
    /// and anything else is extracted after a warning (T-F109).
    /// </summary>
    public static RowOpenAction DecideBrowserRow(
        bool isBusy, bool insideArchive, bool isFolder, string name, Func<bool> isArchiveOnDisk)
    {
        if (isBusy)
            return RowOpenAction.None;
        if (isFolder)
            return RowOpenAction.OpenFolder;
        if (!insideArchive)
            return isArchiveOnDisk() ? RowOpenAction.OpenArchive : RowOpenAction.None;
        if (ArchiveFormatDetector.IsRecognizedArchiveExtension(name))
            return RowOpenAction.DrillIntoNestedArchive;
        return PreviewPolicy.IsPreviewable(name) ? RowOpenAction.Preview : RowOpenAction.ExtractWithWarning;
    }

    /// <summary>
    /// T-F275 step 3c: a row that <see cref="DecidePendingRow"/> or <see cref="DecideBrowserRow"/>
    /// leaves alone opens the archive it protects when it is a PAR2 file on disk (in the pending
    /// list, or in a real folder). Inside an archive a <c>.par2</c> entry is an ordinary entry.
    /// </summary>
    public static bool OpensProtectedArchive(bool isBusy, bool insideArchive, bool isFolder, string name) =>
        !isBusy && !insideArchive && !isFolder && RecoveryDataLookup.IsRecoveryFile(name);

    /// <summary>
    /// Where an entry extracted into <paramref name="scopeDir"/> landed, or null when its name
    /// would point outside the scope (an absolute or <c>..</c> entry name) — T-F242 item 6: the
    /// path is handed to ShellExecute, so it must never name a file Pakko did not write.
    /// </summary>
    public static string? ResolveInScope(string scopeDir, string entryPath)
    {
        string root = Path.GetFullPath(scopeDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string relative = entryPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
            return null;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length ? full : null;
    }
}
