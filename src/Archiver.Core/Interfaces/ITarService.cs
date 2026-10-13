using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// tar.exe-backed archive/extract/list/compress operations for the tar family (tar, gz, bz2, xz,
/// zst, lzma) plus read-only 7z/rar support. See
/// <see cref="Archiver.Core.Services.TarSandboxedService"/> for the real implementation and its
/// AppContainer sandboxing on the extraction path.
/// </summary>
public interface ITarService
{
    /// <summary>
    /// Detects which formats the system's tar.exe supports by probing its version output.
    /// Returns sensible all-false defaults if tar.exe is absent or the probe fails.
    /// </summary>
    Task<TarCapabilities> DetectCapabilitiesAsync();

    /// <summary>
    /// T-F342: whether this system's tar.exe writes 7z archives Pakko can open again. Found by
    /// having it write an empty one, not by its version (the same libarchive version ships in
    /// different Windows releases), so it starts tar.exe: ask only when 7z creation is wanted.
    /// The answer is kept for the service's lifetime. False under DisableTarExtraction, without
    /// starting anything. Never throws, except <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// T-F342: what tar.exe answered to the question behind <see cref="CanCreateSevenZipAsync"/>:
    /// true or false, or null when it gave no answer (it timed out, as on a busy machine, or could
    /// not be started). No answer is not "no": it is not kept, and the next call asks again. Use
    /// this where the answer outlives the process; <see cref="CanCreateSevenZipAsync"/> is true
    /// only for a "yes". The default is for an engine that always answers.
    /// </summary>
    async Task<bool?> AskSevenZipWriterAsync(CancellationToken cancellationToken = default) =>
        await CanCreateSevenZipAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Extracts one or more tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma,
    /// 7z, rar) via tar.exe. Never throws — errors are captured in ArchiveResult.Errors.
    /// IProgress&lt;ProgressReport&gt; (T-F142 — was IProgress&lt;int&gt;) to match
    /// IArchiveService.ExtractAsync's contract, since callers route through the same
    /// IExtractionRouter regardless of which service handles the format. Real
    /// BytesTransferred/TotalBytes/CurrentFile are only reported for a single-archive extraction
    /// (options.ArchivePaths.Count == 1) — matching ZipArchiveService.ExtractAsync's own
    /// singleArchive convention — a multi-archive selection still reports percent-only,
    /// BytesTransferred/TotalBytes = 0.
    /// The one exception to "never throws" (T-F260): cancellation throws
    /// OperationCanceledException after cleanup, whether it lands inside one archive or between two.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// T-F397: tests each archive by having tar.exe read every entry inside the sandbox and write
    /// nothing ("-x -O"). An archive extraction would refuse is refused here with the same
    /// message. Where the format has no checksum of file contents that tar.exe verifies (plain
    /// tar, tar.gz, tar.lzma, RAR), a passed test carries a warning that only the structure was
    /// checked. Never throws, except <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a tar-family archive's entries as a flat list, without extracting. Never throws — a
    /// failure (corrupted archive, tar.exe error) is reported via
    /// ArchiveListResult.Success/ErrorMessage. Does not run the whole-archive safety pre-scan
    /// ScanForUnsafeEntriesAsync performs before extraction — listing must never be gated on a
    /// safety check that only matters once bytes are about to be written to disk. Group Policy is
    /// a different matter (T-F250): IArchiveListingRouter refuses blocked formats, and this
    /// engine refuses to list at all under DisableTarExtraction.
    /// </summary>
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tar-family archive (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma) or a 7z from
    /// options.SourcePaths via tar.exe. Unlike ExtractAsync, this never runs the sandboxed
    /// AppContainer path — the input is trusted local files the user selected, not an untrusted
    /// archive being parsed, so T-F52's threat model (a hostile archive driving libarchive into
    /// misbehaving) does not apply here. Never throws — errors are captured in
    /// ArchiveResult.Errors. The one exception (T-F260): cancellation throws
    /// OperationCanceledException after cleanup.
    /// </summary>
    Task<ArchiveResult> CompressAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
