namespace Archiver.Core.IO;

/// <summary>
/// T-F236: the size of a folder tree, measured with the engines' own walk (<see cref="DirectoryWalker"/>):
/// links are not followed and an unreadable subfolder adds nothing instead of failing the whole count.
/// </summary>
/// <param name="Bytes">Total size of the files.</param>
/// <param name="Files">Number of files.</param>
public readonly record struct FolderTotals(long Bytes, int Files)
{
    /// <summary>Walks <paramref name="folder"/>; throws <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled.</summary>
    public static FolderTotals Measure(string folder, CancellationToken cancellationToken = default)
    {
        long bytes = 0;
        int files = 0;
        // T-F531: as ZIP walks it - a name Windows trims is archived, so it counts.
        foreach (WalkEntry entry in DirectoryWalker.Walk(folder, spelledBelow: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCountedFile(entry.Kind))
                continue;
            try
            {
                bytes += ((FileInfo)entry.Info).Length;
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort: a file gone since its folder was listed adds nothing
            }
        }
        return new FolderTotals(bytes, files);
    }

    /// <summary>T-F572: whether a walked entry counts as a file this total holds: what ZIP archives.</summary>
    internal static bool IsCountedFile(WalkEntryKind kind)
    {
        switch (kind)
        {
            case WalkEntryKind.File:
                return true;
            // Not archived: ZIP refuses a device name (T-F559) and skips a link; no filter is given.
            case WalkEntryKind.Directory:
            case WalkEntryKind.ReparsePoint:
            case WalkEntryKind.UnreadableDirectory:
            case WalkEntryKind.LeftOut:
            case WalkEntryKind.TrimmedName:
            case WalkEntryKind.DeviceName:
                return false;
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }
}
