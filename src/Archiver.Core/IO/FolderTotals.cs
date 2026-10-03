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
        foreach (WalkEntry entry in DirectoryWalker.Walk(folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Kind != WalkEntryKind.File)
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
}
