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

        ArchiveResult zipResult = zipPaths.Count > 0
            ? await archiveService.ExtractAsync(
                options with { ArchivePaths = zipPaths, OpenDestinationFolder = false },
                progress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        // T-F142: real byte-level progress for tar-family extraction is only granted when tar
        // handles the WHOLE selection alone (zipPaths is empty) — not just when tarPaths.Count == 1.
        // TarSandboxedService.ExtractAsync decides "am I extracting a single archive?" purely from
        // its OWN subset's count, with no visibility into whether ZIP also ran first in this same
        // call. In a mixed selection (e.g. one .zip + one .tar.gz), zipResult above already ran
        // its own real per-byte climb to 100% (zip always runs before tar here, unconditionally,
        // matching its pre-existing behavior) — if tar then also believed itself "alone" (its own
        // bucket count == 1) it would restart a second real 0->100 climb, visibly dropping the
        // dialog back down after it had already reached 100%. Suppressing tar's real reporting
        // whenever zip also ran keeps the existing (pre-T-F142) percent-only per-archive-slice
        // shape for that case — no worse than before this task, just not improved for a mixed
        // selection specifically.
        IProgress<ProgressReport>? tarProgress = zipPaths.Count == 0 ? progress : null;
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false },
                tarProgress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            Success = zipResult.Success && tarResult.Success,
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
            .Select(path => new SkippedFile { Path = path, Reason = ArchiveFormatPolicy.NoTestCapabilityReason });
        return zipResult with { SkippedFiles = [.. zipResult.SkippedFiles, .. untestable, .. classification.Unsupported] };
    }

    private static ArchiveResult EmptyResult() => new() { Success = true };
}
