using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

public interface IArchiveService
{
    /// <summary>
    /// Creates one or more ZIP archives from the provided options.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts one or more ZIP archives.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies every entry's CRC-32 against its declared value without writing any files
    /// to disk. Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists an archive's entries as a flat list, without extracting. Never throws — a failure
    /// (corrupted archive, IO error) is reported via ArchiveListResult.Success/ErrorMessage.
    /// </summary>
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
