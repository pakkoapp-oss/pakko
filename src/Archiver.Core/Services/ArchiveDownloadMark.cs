using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Whether an archive carries the "downloaded from the internet" mark (its Zone.Identifier
/// stream, T-F45) — what the App asks before it offers to leave the mark off (T-F360).
/// </summary>
public static class ArchiveDownloadMark
{
    /// <summary>True if the archive has a readable Zone.Identifier stream. Never throws.</summary>
    public static bool IsPresent(string archivePath) =>
        ArchiveEntrySecurity.ReadMotw(archivePath, MotwMode.AllFiles) is not null;
}
