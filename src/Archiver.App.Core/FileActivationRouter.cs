using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

public enum FileActivationMode
{
    AddToList,
    Browse,
}

public sealed record FileActivationDecision(FileActivationMode Mode, string? BrowsePath);

/// <summary>
/// Decides whether a File-kind activation should enter the Archive Browser (T-F05) or fall back
/// to the existing "add these paths to the pending archive-creation list" behavior. Lives in
/// Archiver.App.Core (not Archiver.App) so the decision is unit-testable without a WinUI test
/// host — mirrors ArchiveTreeIndex's split for the same reason.
/// </summary>
public static class FileActivationRouter
{
    public static FileActivationDecision Decide(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1 && ArchiveFormatDetector.Detect(paths[0]) != ArchiveFormat.Unknown)
            return new FileActivationDecision(FileActivationMode.Browse, paths[0]);

        return new FileActivationDecision(FileActivationMode.AddToList, null);
    }
}
