using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IExtractionRouter"/>
public sealed class ExtractionRouter(
    IArchiveService archiveService,
    ITarService tarService,
    TarCapabilities tarCapabilities,
    GroupPolicyOptions? groupPolicyOptions = null) : IExtractionRouter
{
    private readonly GroupPolicyOptions _policy = groupPolicyOptions ?? new GroupPolicyOptions();

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var zipPaths = new List<string>();
        var tarPaths = new List<string>();
        var unsupported = new List<SkippedFile>();

        foreach (string path in options.ArchivePaths)
        {
            ArchiveFormat format = ArchiveFormatDetector.Detect(path);

            // T-F51: AllowedFormats/BlockedFormats sit outside the format-specific switch below
            // since they apply uniformly to both Zip and every tar-family format. Unknown has no
            // registry name to check — leave it to the existing unrecognized-path handling.
            if (format != ArchiveFormat.Unknown)
            {
                string registryName = ArchiveFormatRegistryNames.ToRegistryName(format);
                if (!_policy.IsFormatAllowed(registryName))
                {
                    unsupported.Add(new SkippedFile
                    {
                        Path = path,
                        Reason = $"This archive format ({registryName}) is blocked by Group Policy."
                    });
                    continue;
                }
            }

            switch (format)
            {
                case ArchiveFormat.Zip:
                    zipPaths.Add(path);
                    break;
                case ArchiveFormat.Unknown:
                    // Not a recognized archive format at all — let IArchiveService's own
                    // ZipArchiveService.GetKnownArchiveReason defensive path handle messaging
                    // for whatever this turns out to be (kept in the ZIP bucket so its existing
                    // behavior for unrecognized paths is unchanged).
                    zipPaths.Add(path);
                    break;
                default:
                    // T-F51: DisableTarExtraction is a separate kill switch from BlockedFormats —
                    // it stops tar.exe from ever being spawned at all, not just a per-format block.
                    if (_policy.DisableTarExtraction)
                        unsupported.Add(new SkippedFile
                        {
                            Path = path,
                            Reason = "tar.exe-based extraction is disabled by Group Policy."
                        });
                    else if (IsSupported(format, tarCapabilities))
                        tarPaths.Add(path);
                    else
                        unsupported.Add(new SkippedFile
                        {
                            Path = path,
                            Reason = BuildUnsupportedReason(format, tarCapabilities)
                        });
                    break;
            }
        }

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
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false },
                zipPaths.Count == 0 ? progress : null, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            Success = zipResult.Success && tarResult.Success,
            CreatedFiles = [.. zipResult.CreatedFiles, .. tarResult.CreatedFiles],
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. unsupported],
        };

        if (merged.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return merged;
    }

    private static ArchiveResult EmptyResult() => new() { Success = true };

    private static bool IsSupported(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Tar or ArchiveFormat.GZip => true,
        ArchiveFormat.Bz2 => caps.SupportsBz2,
        ArchiveFormat.Xz => caps.SupportsXz,
        ArchiveFormat.Zstd => caps.SupportsZstd,
        ArchiveFormat.Lzma => caps.SupportsLzma,
        ArchiveFormat.Rar => caps.SupportsRar,
        ArchiveFormat.SevenZip => caps.Supports7z,
        _ => false,
    };

    private static string BuildUnsupportedReason(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Rar => $"RAR requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.SevenZip => $"7-Zip requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.Zstd => $"Zstandard requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.Xz => $"XZ is not supported by this system's tar.exe (version {caps.Version}).",
        ArchiveFormat.Lzma => $"LZMA is not supported by this system's tar.exe (version {caps.Version}).",
        _ => $"This archive format is not supported by this system's tar.exe (version {caps.Version}).",
    };
}
