using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// Routes a single archive's ListEntriesAsync call to IArchiveService (ZIP) or ITarService
/// (tar-family), based on ArchiveFormatDetector — same dispatch IExtractionRouter uses for
/// extraction. A separate interface from IExtractionRouter because listing and extracting return
/// different result shapes; one archive path in, one ArchiveListResult out (not a batch, unlike
/// ExtractAsync — the archive browser always lists exactly one archive at a time). T-F250: Group
/// Policy applies exactly as for extraction (ArchiveFormatPolicy) — a blocked format, or a
/// tar-family format under DisableTarExtraction, is a failed result and no engine is called.
/// </summary>
public interface IArchiveListingRouter
{
    /// <summary>Lists a single archive's entries without extracting.</summary>
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// T-F540: lists an archive's entries as they are read: each goes to <paramref name="onEntry"/>
    /// in archive order and is not kept; the summary says how the listing ended. When it failed, the
    /// caller discards every entry it was given. Never throws, except what <paramref name="onEntry"/>
    /// throws - it reaches the caller as it was thrown, and the listing stops - and
    /// <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="onEntry"/> is null: a caller's bug, not an
    /// archive's (T-F545).</exception>
    Task<ArchiveListSummary> ListEntriesAsync(
        string archivePath,
        Action<ArchiveEntryInfo> onEntry,
        CancellationToken cancellationToken = default);
}
