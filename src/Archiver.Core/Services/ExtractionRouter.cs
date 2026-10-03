using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IExtractionRouter"/>
public sealed class ExtractionRouter(
    IArchiveService archiveService,
    ITarService tarService,
    TarCapabilities tarCapabilities,
    GroupPolicyOptions groupPolicyOptions) : IExtractionRouter
{
    private readonly GroupPolicyOptions _policy = groupPolicyOptions ?? throw new ArgumentNullException(nameof(groupPolicyOptions));

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F146: classification (zip/tar/unsupported split + Group Policy gating) is now shared
        // with AntivirusScanService via ArchiveFormatPolicy, so a scan can never silently drift
        // from what real extraction would allow/refuse. Behavior here is unchanged.
        ArchiveFormatPolicy.Classification classification = ArchiveFormatPolicy.Classify(options.ArchivePaths, tarCapabilities, _policy);
        IReadOnlyList<string> zipPaths = classification.ZipPaths;
        IReadOnlyList<string> tarPaths = classification.TarPaths;
        IReadOnlyList<SkippedFile> unsupported = classification.Unsupported;

        // T-F306: with both kinds selected, ZIP runs first and tar second, each in its own slice of
        // one climb. T-F142 had given tar no progress at all here (a second 0->100 climb would have
        // dropped the bar), so the bar sat at 100% through the whole tar part.
        bool mixed = progress is not null && zipPaths.Count > 0 && tarPaths.Count > 0;
        int zipSliceEnd = mixed ? ZipSharePercent(zipPaths, tarPaths) : 100;
        SliceProgress? zipSlice = mixed ? new SliceProgress(progress!, 0, zipSliceEnd, bytesBefore: 0) : null;

        ArchiveResult zipResult = zipPaths.Count > 0
            ? await archiveService.ExtractAsync(
                options with { ArchivePaths = zipPaths, OpenDestinationFolder = false },
                zipSlice ?? progress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        IProgress<ProgressReport>? tarProgress = mixed
            ? new SliceProgress(progress!, zipSliceEnd, 100, bytesBefore: zipSlice!.LastTotalBytes)
            : progress;
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false },
                tarProgress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            CreatedFiles = [.. zipResult.CreatedFiles, .. tarResult.CreatedFiles],
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. unsupported],
            // T-F260: unsupported/policy-blocked paths get no SourceResult, so they are never
            // deletable; a cancel in either engine throws before this merge is reached.
            Sources = [.. zipResult.Sources, .. tarResult.Sources],
        };

        if (merged.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return merged;
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null,
        CancellationToken cancellationToken = default)
    {
        // T-F261: same classifier as extraction. tar.exe has no test mode, so a tar-family path
        // that policy and capabilities would allow is still only reported, never opened.
        ArchiveFormatPolicy.Classification classification = ArchiveFormatPolicy.Classify(archivePaths, tarCapabilities, _policy);

        ArchiveResult zipResult = classification.ZipPaths.Count > 0
            ? await archiveService.TestAsync(classification.ZipPaths, progress, resolvePasswordAsync, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        IEnumerable<SkippedFile> untestable = classification.TarPaths
            .Select(path => CoreMessages.Skip(path, MessageCode.NoTestCapability));
        return zipResult with { SkippedFiles = [.. zipResult.SkippedFiles, .. untestable, .. classification.Unsupported] };
    }

    private static ArchiveResult EmptyResult() => new();

    // The ZIP part's share of the climb, by the archives' sizes on disk — the only measure both
    // kinds have before either runs. By count when no size can be read.
    private static int ZipSharePercent(IReadOnlyList<string> zipPaths, IReadOnlyList<string> tarPaths)
    {
        long zipBytes = TotalLength(zipPaths);
        long tarBytes = TotalLength(tarPaths);
        return zipBytes > 0 && tarBytes > 0
            ? (int)(zipBytes * 100.0 / ((double)zipBytes + tarBytes))
            : zipPaths.Count * 100 / (zipPaths.Count + tarPaths.Count);
    }

    private static long TotalLength(IReadOnlyList<string> paths)
    {
        long total = 0;
        foreach (string path in paths)
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort: an unreadable size only changes how the bar is divided
            }
        }
        return total;
    }

    // Maps one engine's 0-100 onto its slice. Bytes continue after the engine that ran before:
    // a byte count that went back down would be ignored by ProgressSpeedSampler. A report with
    // no byte total (several archives of one kind) stays without one.
    private sealed class SliceProgress(IProgress<ProgressReport> inner, int startPercent, int endPercent, long bytesBefore)
        : IProgress<ProgressReport>
    {
        public long LastTotalBytes { get; private set; }

        public void Report(ProgressReport value)
        {
            bool hasBytes = value.TotalBytes > 0;
            if (hasBytes)
                LastTotalBytes = value.TotalBytes;
            inner.Report(new ProgressReport
            {
                Percent = startPercent + Math.Clamp(value.Percent, 0, 100) * (endPercent - startPercent) / 100,
                BytesTransferred = hasBytes ? bytesBefore + value.BytesTransferred : 0,
                TotalBytes = hasBytes ? bytesBefore + value.TotalBytes : 0,
                CurrentFile = value.CurrentFile,
                Phase = value.Phase,
            });
        }
    }
}
