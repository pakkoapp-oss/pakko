namespace Archiver.App.Core;

/// <summary>
/// T-F98: bounds how many nested archives the Archive Browser will drill into, one inside another.
/// This is a browse-session/UX resource bound enforced by the App layer's recursive orchestration,
/// not a per-extraction security invariant — that backstop is T-F49 (whole-archive pre-scan) and
/// T-F90/T-F94 (compression-ratio + disk-space check), already in Archiver.Core and already
/// re-applied independently at every nesting level since each level goes through the same
/// IExtractionRouter.ExtractAsync pipeline. See DECISIONS.md's T-F98 entry for why 4 was chosen.
/// </summary>
public static class NestedArchivePolicy
{
    /// <summary>Maximum nesting depth the Archive Browser will drill into.</summary>
    public const int MaxDepth = 4;

    /// <summary>True once <paramref name="currentDepth"/> has reached <see cref="MaxDepth"/>.</summary>
    public static bool ExceedsMaxDepth(int currentDepth) => currentDepth >= MaxDepth;
}
