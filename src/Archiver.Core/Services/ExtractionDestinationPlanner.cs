namespace Archiver.Core.Services;

// T-F157: the shape a single archive's file entries collapse to, once selection/subset status is
// known — computed identically by ZipArchiveService and TarSandboxedService from their own
// entry-name representations (ZipArchiveEntry vs. raw tar names), then fed into Resolve below so
// the actual "where do files land" decision is shared code, not two hand-synced copies (T-F118's
// own comment called that sync a documentation-enforced promise — T-F154/T-F156 both had to
// honor it manually in one day).
internal enum RootShape
{
    SingleFolder,
    SingleFile,
    MultiRoot,
    SelectedSubset
}

/// <summary>
/// Shared decision for where a single archive's extracted files actually land, and whether the
/// archive's own root folder name is stripped from entry paths — the one piece of logic
/// <see cref="ZipArchiveService.ExtractWithSmartFolderingAsync"/> and
/// <see cref="TarSandboxedService.ExtractSingleArchiveAsync"/> must otherwise keep in sync by
/// hand (T-F118). <see cref="Resolve"/>'s two invariants: <c>ActualDest == (alreadyIsolated
/// &amp;&amp; shape == RootShape.SingleFile) ? unisolatedDestDir : destDir</c> (T-F154's
/// single-file unwrap), and <c>StripRootPrefix == (shape == RootShape.SingleFolder &amp;&amp;
/// (alreadyIsolated || rootDuplicatesArchiveName))</c> — T-F205: SingleFolder mode keeps the
/// archive's root folder ("extract with full paths", 7-Zip/NanaZip), except where the caller asked
/// to eliminate a root named like the archive (NanaZip's "Extract to name\" ElimDup). Check new
/// arms against those two formulas, not just intuition.
/// </summary>
internal static class ExtractionDestinationPlanner
{
    public static RootShape Classify(bool isSelectedSubset, bool isSingleRootFolder, bool isSingleRootFile)
    {
        if (isSelectedSubset) return RootShape.SelectedSubset;
        if (isSingleRootFolder) return RootShape.SingleFolder;
        if (isSingleRootFile) return RootShape.SingleFile;
        return RootShape.MultiRoot;
    }

    // T-F157: all 8 (alreadyIsolated, RootShape) combinations spelled out explicitly rather than
    // compressed into the two boolean formulas above — deliberately readable as an actual
    // decision table for the next person to audit. A discard-less version of this switch was
    // built and confirmed to fail with CS8524 ("not exhaustive... involving an unnamed enum
    // value... (RootShape)4 is not covered") even with all 8 named combinations present — Roslyn
    // treats a plain enum's members as an open set, so this repo's TreatWarningsAsErrors (T-F150)
    // cannot get real compile-time exhaustiveness from a tuple-pattern switch this way. The real
    // "a new RootShape must be handled" guard is ExtractionDestinationPlannerTests' enumeration
    // theory, not the compiler. See DECISIONS.md's T-F157 entry.
    public static (string ActualDest, bool StripRootPrefix) Resolve(
        bool alreadyIsolated, RootShape shape, string destDir, string unisolatedDestDir,
        bool rootDuplicatesArchiveName = false) =>
        (alreadyIsolated, shape) switch
        {
            (true, RootShape.SingleFile) => (unisolatedDestDir, false),
            (true, RootShape.SingleFolder) => (destDir, true),
            (true, RootShape.MultiRoot) => (destDir, false),
            (true, RootShape.SelectedSubset) => (destDir, false),
            (false, RootShape.SingleFile) => (destDir, false),
            (false, RootShape.SingleFolder) => (destDir, rootDuplicatesArchiveName),
            (false, RootShape.MultiRoot) => (destDir, false),
            (false, RootShape.SelectedSubset) => (destDir, false),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    /// <summary>T-F205: whether an archive's single root folder is named like the archive itself
    /// (case-insensitive, compound extensions stripped) — the only root NanaZip's ElimDup removes.
    /// Compared with the archive's name, not the destination folder's, which Pakko may have
    /// numbered ("name (1)").</summary>
    public static bool RootDuplicatesArchiveName(string rootName, string archivePath) =>
        string.Equals(rootName, ArchiveNaming.GetBaseName(archivePath), StringComparison.OrdinalIgnoreCase);
}
