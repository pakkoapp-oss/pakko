namespace Archiver.App.Core;

/// <summary>
/// T-F107: what the Archive Browser's current folder path means. Archive: a '/'-separated path
/// inside the open archive. RealFileSystem: an absolute Windows folder. ThisPc: the drives list;
/// the path is unused.
/// </summary>
public enum ArchiveBrowseScope
{
    /// <summary>Inside the open archive.</summary>
    Archive,

    /// <summary>A real Windows folder.</summary>
    RealFileSystem,

    /// <summary>The "This PC" drives list.</summary>
    ThisPc,
}

/// <summary>Where Up goes from the current location.</summary>
public enum BrowseUpStep
{
    /// <summary>Nothing: "This PC" is the top.</summary>
    None,

    /// <summary>The parent folder inside the archive.</summary>
    ArchiveParentFolder,

    /// <summary>Back to the archive that holds this nested one (T-F98).</summary>
    PopNestedLevel,

    /// <summary>The real folder that holds the archive (T-F107).</summary>
    ContainingFolder,

    /// <summary>The parent of a real folder.</summary>
    RealParentFolder,

    /// <summary>The "This PC" drives list.</summary>
    ThisPc,
}

/// <summary>T-F112: the Archive Browser's Up decision, out of the WinUI view model so it is testable.</summary>
public static class BrowseNavigation
{
    /// <summary>
    /// Where Up goes. A nested level's root pops to its parent before the outermost archive's root
    /// climbs into real folders (T-F98 before T-F107).
    /// </summary>
    public static BrowseUpStep DecideUp(ArchiveBrowseScope scope, string currentFolderPath, int nestedDepth, string? archivePath) =>
        scope switch
        {
            ArchiveBrowseScope.Archive when currentFolderPath.Length > 0 => BrowseUpStep.ArchiveParentFolder,
            ArchiveBrowseScope.Archive when nestedDepth > 0 => BrowseUpStep.PopNestedLevel,
            ArchiveBrowseScope.Archive => Path.GetDirectoryName(archivePath) is null ? BrowseUpStep.ThisPc : BrowseUpStep.ContainingFolder,
            ArchiveBrowseScope.RealFileSystem => Path.GetDirectoryName(currentFolderPath) is null ? BrowseUpStep.ThisPc : BrowseUpStep.RealParentFolder,
            _ => BrowseUpStep.None,
        };
}
