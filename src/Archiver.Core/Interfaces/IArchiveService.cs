using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// ZIP archive/extract/test/list operations via <c>System.IO.Compression</c>. See
/// <see cref="Archiver.Core.Services.ZipArchiveService"/> for the real implementation.
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Creates one or more ZIP archives from the provided options.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// The one exception (T-F260): cancellation throws OperationCanceledException after cleanup,
    /// whether it lands inside one source or between two.
    /// </summary>
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts one or more ZIP archives.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// The one exception (T-F260): cancellation throws OperationCanceledException after cleanup,
    /// whether it lands inside one archive or between two.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies every entry's CRC-32 against its declared value without writing any files
    /// to disk. Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    /// <param name="archivePaths">The ZIP archives to verify.</param>
    /// <param name="progress">Optional overall-progress reporter.</param>
    /// <param name="resolvePasswordAsync">
    /// T-F189: invoked once per encrypted archive, mirroring
    /// <see cref="Models.ExtractOptions.ResolvePasswordAsync"/>. Null (the default — every
    /// existing caller until T-F191/T-F192 wire this) preserves the pre-T-F189 behavior exactly:
    /// "password-protected and cannot be tested." Not folded into an options record since
    /// TestAsync takes a flat path list rather than an Options type; placed before
    /// <paramref name="cancellationToken"/> (CA1068 — CancellationToken must be last).
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists an archive's entries as a flat list, without extracting. Never throws — a failure
    /// (corrupted archive, IO error) is reported via ArchiveListResult.Success/ErrorMessage.
    /// </summary>
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
