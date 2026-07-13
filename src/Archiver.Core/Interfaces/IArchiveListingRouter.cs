using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// Routes a single archive's ListEntriesAsync call to IArchiveService (ZIP) or ITarService
/// (tar-family), based on ArchiveFormatDetector — same dispatch IExtractionRouter uses for
/// extraction. A separate interface from IExtractionRouter because listing and extracting return
/// different result shapes; one archive path in, one ArchiveListResult out (not a batch, unlike
/// ExtractAsync — the archive browser always lists exactly one archive at a time).
/// </summary>
public interface IArchiveListingRouter
{
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
