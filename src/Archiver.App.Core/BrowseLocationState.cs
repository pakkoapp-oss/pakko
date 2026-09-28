namespace Archiver.App.Core;

/// <summary>
/// T-F199 step 6 / T-F210: what the Archive Browser offers where the user is. Inside an archive:
/// the extract actions and their options; Test only for ZIP (tar.exe has no test mode, and
/// Explorer offers Test for ZIP only); "delete after" only for an archive on disk, not a nested
/// one (that is a temp copy). Outside (a real folder or "This PC" after Up, T-F107): none of it,
/// and a line saying where the user is.
/// </summary>
/// <param name="ShowsExtractActions">Extract selected / Extract all.</param>
/// <param name="ShowsOptions">The destination options.</param>
/// <param name="ShowsTest">Test archive.</param>
/// <param name="OffersDeleteAfter">"Move the archive to the Recycle Bin" applies.</param>
/// <param name="ShowsOutsideInfo">The "this is a folder, not an archive" line.</param>
public sealed record BrowseLocationState(
    bool ShowsExtractActions,
    bool ShowsOptions,
    bool ShowsTest,
    bool OffersDeleteAfter,
    bool ShowsOutsideInfo)
{
    /// <summary>The state for a location.</summary>
    public static BrowseLocationState For(bool insideArchive, bool nested, bool isZip) => new(
        ShowsExtractActions: insideArchive,
        ShowsOptions: insideArchive,
        ShowsTest: insideArchive && isZip,
        OffersDeleteAfter: insideArchive && !nested,
        ShowsOutsideInfo: !insideArchive);
}
