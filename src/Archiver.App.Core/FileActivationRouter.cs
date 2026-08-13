using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>What a File-kind activation should do, per <see cref="FileActivationRouter.Decide"/>.</summary>
public enum FileActivationMode
{
    /// <summary>Add the activated path(s) to the pending archive-creation list.</summary>
    AddToList,

    /// <summary>Open the Archive Browser directly on the activated archive.</summary>
    Browse,
}

/// <summary>Result of <see cref="FileActivationRouter.Decide"/>.</summary>
/// <param name="Mode">What the activation should do.</param>
/// <param name="BrowsePath">The archive to open — set only when <paramref name="Mode"/> is <see cref="FileActivationMode.Browse"/>.</param>
public sealed record FileActivationDecision(FileActivationMode Mode, string? BrowsePath);

/// <summary>
/// Decides whether a File-kind activation should enter the Archive Browser (T-F05) or fall back
/// to the existing "add these paths to the pending archive-creation list" behavior. Lives in
/// Archiver.App.Core (not Archiver.App) so the decision is unit-testable without a WinUI test
/// host — mirrors ArchiveTreeIndex's split for the same reason.
/// </summary>
public static class FileActivationRouter
{
    /// <summary>Routes a File-kind activation to Browse (single recognized archive) or AddToList (anything else).</summary>
    public static FileActivationDecision Decide(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1 && ArchiveFormatDetector.Detect(paths[0]) != ArchiveFormat.Unknown)
            return new FileActivationDecision(FileActivationMode.Browse, paths[0]);

        return new FileActivationDecision(FileActivationMode.AddToList, null);
    }
}
