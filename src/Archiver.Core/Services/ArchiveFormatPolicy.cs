using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The one classifier every operation uses to decide which engine may open an archive (T-F261):
/// Group Policy first (AllowedFormats/BlockedFormats, then DisableTarExtraction), then what the
/// system's tar.exe can read. Extraction, testing, listing and scanning all route through it, so
/// no operation can drift from what another would allow or refuse.
/// </summary>
public static class ArchiveFormatPolicy
{
    /// <summary>
    /// A selection split by engine. <see cref="ZipPaths"/> also holds unrecognized paths, so the
    /// ZIP engine's own "not a recognized archive" handling reports them.
    /// </summary>
    /// <param name="ZipPaths">Paths the ZIP engine handles.</param>
    /// <param name="TarPaths">Paths tar.exe may open.</param>
    /// <param name="Unsupported">Paths refused by Group Policy or by tar.exe's capabilities.</param>
    public sealed record Classification(
        IReadOnlyList<string> ZipPaths,
        IReadOnlyList<string> TarPaths,
        IReadOnlyList<SkippedFile> Unsupported);

    /// <summary>Splits <paramref name="paths"/> by engine, refusing what policy or tar.exe cannot allow.</summary>
    public static Classification Classify(
        IReadOnlyList<string> paths, TarCapabilities tarCapabilities, GroupPolicyOptions policy) =>
        Classify(paths, DetectFormats(paths), tarCapabilities, policy);

    // T-F350: as Classify, but tar.exe is asked what it can read only when a verdict depends on
    // it - never for a ZIP-only selection or for formats Group Policy already refuses.
    internal static async Task<Classification> ClassifyAsync(
        IReadOnlyList<string> paths,
        Func<Task<TarCapabilities>> tarCapabilities,
        GroupPolicyOptions policy,
        CancellationToken cancellationToken)
    {
        ArchiveFormat[] formats = DetectFormats(paths);
        // The capabilities are read only for a format the policy leaves open, so the empty value is never looked at.
        TarCapabilities capabilities = Array.Exists(formats, format => !TryDecideByPolicy(format, policy, out _))
            ? await tarCapabilities().WaitAsync(cancellationToken).ConfigureAwait(false)
            : new TarCapabilities();
        return Classify(paths, formats, capabilities, policy);
    }

    // Each file is read once: the verdicts and the "needs tar.exe" question use the same formats.
    private static ArchiveFormat[] DetectFormats(IReadOnlyList<string> paths)
    {
        var formats = new ArchiveFormat[paths.Count];
        for (int i = 0; i < formats.Length; i++)
            formats[i] = ArchiveFormatDetector.Detect(paths[i]);
        return formats;
    }

    private static Classification Classify(
        IReadOnlyList<string> paths, ArchiveFormat[] formats, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        var zipPaths = new List<string>();
        var tarPaths = new List<string>();
        var unsupported = new List<SkippedFile>();

        for (int i = 0; i < formats.Length; i++)
        {
            string path = paths[i];
            ArchiveFormat format = formats[i];
            if (GetRefusalReason(format, tarCapabilities, policy) is { } reason)
                unsupported.Add(CoreMessages.Skip(path, reason));
            else if (IsZipEngineFormat(format))
                zipPaths.Add(path);
            else
                tarPaths.Add(path);
        }

        return new Classification(zipPaths, tarPaths, unsupported);
    }

    /// <summary>
    /// Why an archive of <paramref name="format"/> may not be opened, or null when it may.
    /// Policy is checked before tar.exe's capabilities, so a refused format always names the
    /// policy — also when tar.exe was never probed because the policy disables it.
    /// <see cref="ArchiveFormat.Unknown"/> is never refused here: the ZIP engine reports it.
    /// </summary>
    public static CoreText? GetRefusalReason(ArchiveFormat format, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        if (TryDecideByPolicy(format, policy, out CoreText? refusal))
            return refusal;

        return IsSupportedByTar(format, tarCapabilities) ? null : BuildUnsupportedReason(format, tarCapabilities);
    }

    // True when the verdict needs no tar.exe capabilities: the ZIP engine takes the format, or
    // policy refuses it. False leaves the verdict to what tar.exe can read.
    private static bool TryDecideByPolicy(ArchiveFormat format, GroupPolicyOptions policy, out CoreText? refusal)
    {
        refusal = null;
        if (format == ArchiveFormat.Unknown)
            return true;

        if (!policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format)))
        {
            refusal = BlockedFormatReason(format);
            return true;
        }

        if (IsZipEngineFormat(format))
            return true;

        // DisableTarExtraction is a separate kill switch from BlockedFormats: tar.exe is never
        // started at all, not just refused per format.
        if (policy.DisableTarExtraction)
        {
            refusal = CoreMessages.Text(MessageCode.TarExtractionDisabled);
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="path"/>'s extension names an archive that policy and tar.exe let
    /// Pakko open — <see cref="GetRefusalReason"/> without reading the file (T-F212).
    /// </summary>
    public static bool CanOpenByExtension(string path, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        ArchiveFormat format = ArchiveFormatDetector.FormatFromExtension(path);
        return format != ArchiveFormat.Unknown && GetRefusalReason(format, tarCapabilities, policy) is null;
    }

    /// <summary>True when Group Policy refuses <paramref name="format"/> (BlockedFormats/AllowedFormats, or DisableTarExtraction for a tar-family format).</summary>
    public static bool IsBlockedByPolicy(ArchiveFormat format, GroupPolicyOptions policy)
    {
        if (format == ArchiveFormat.Unknown)
            return false;
        if (!policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format)))
            return true;
        return !IsZipEngineFormat(format) && policy.DisableTarExtraction;
    }

    internal static CoreText BlockedFormatReason(ArchiveFormat format) =>
        CoreMessages.Text(MessageCode.FormatBlocked, ArchiveFormatRegistryNames.ToRegistryName(format));

    private static bool IsZipEngineFormat(ArchiveFormat format) => format is ArchiveFormat.Zip or ArchiveFormat.Unknown;

    /// <summary>
    /// T-F403: why this system's tar.exe cannot make an archive of <paramref name="format"/> with
    /// <paramref name="sevenZipMethod"/> (used only for 7z), or null when it can. The one rule
    /// Core applies before starting tar.exe and the App's and the CLI's lists offer by. Policy is
    /// not part of it (<c>ArchiveCreationRouter</c> refuses by that first), nor whether the 7z
    /// writer is there (<see cref="Interfaces.ITarService.CanCreateSevenZipAsync"/>). ZIP never
    /// needs tar.exe.
    /// </summary>
    public static CoreText? GetTarCreationRefusalReason(
        ArchiveContainerFormat format, SevenZipMethod sevenZipMethod, TarCapabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        if (format == ArchiveContainerFormat.Zip)
            return null;

        ArchiveFormat read = ReadFormatOf(format);
        if (!IsSupportedByTar(read, caps))
            return BuildUnsupportedReason(read, caps);

        if (format == ArchiveContainerFormat.SevenZip && !IsSevenZipMethodSupported(sevenZipMethod, caps))
            return CoreMessages.Text(MessageCode.FormatNotSupportedByTar, $"7-Zip {SevenZipMethodName(sevenZipMethod)}", caps.Version);

        return null;
    }

    /// <summary>
    /// T-F403: the Group Policy names of the tar.exe formats this tar.exe reads, for the Explorer
    /// extension's snapshot (<see cref="TarCapabilitySnapshot"/>); policy is applied there apart.
    /// </summary>
    public static IReadOnlyList<string> TarReadableRegistryNames(TarCapabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ArchiveFormat[] tarFormats =
            [ArchiveFormat.Tar, ArchiveFormat.GZip, ArchiveFormat.Bz2, ArchiveFormat.Xz, ArchiveFormat.Zstd,
             ArchiveFormat.Lzma, ArchiveFormat.Rar, ArchiveFormat.SevenZip];
        return [.. tarFormats.Where(format => IsSupportedByTar(format, caps)).Select(ArchiveFormatRegistryNames.ToRegistryName)];
    }

    /// <summary>The 7z methods this tar.exe can write, in the order the lists show them.</summary>
    public static IReadOnlyList<SevenZipMethod> CreatableSevenZipMethods(TarCapabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        return [.. Enum.GetValues<SevenZipMethod>().Where(method => IsSevenZipMethodSupported(method, caps))];
    }

    private static ArchiveFormat ReadFormatOf(ArchiveContainerFormat format) => format switch
    {
        ArchiveContainerFormat.Tar => ArchiveFormat.Tar,
        ArchiveContainerFormat.TarGz => ArchiveFormat.GZip,
        ArchiveContainerFormat.TarBz2 => ArchiveFormat.Bz2,
        ArchiveContainerFormat.TarXz => ArchiveFormat.Xz,
        ArchiveContainerFormat.TarZst => ArchiveFormat.Zstd,
        ArchiveContainerFormat.TarLzma => ArchiveFormat.Lzma,
        ArchiveContainerFormat.SevenZip => ArchiveFormat.SevenZip,
        _ => ArchiveFormat.Zip,
    };

    // LZMA and LZMA2 need liblzma, which reading 7z already requires (T-F402).
    private static bool IsSevenZipMethodSupported(SevenZipMethod method, TarCapabilities caps) => method switch
    {
        SevenZipMethod.Lzma2 or SevenZipMethod.Lzma => caps.Supports7z,
        SevenZipMethod.BZip2 => caps.SupportsBz2,
        SevenZipMethod.Deflate => caps.SupportsGzip,
        _ => true,
    };

    /// <summary>The method's own name, the same in every language ("LZMA2", "BZip2", ...).</summary>
    public static string SevenZipMethodName(SevenZipMethod method) => method switch
    {
        SevenZipMethod.Lzma2 => "LZMA2",
        SevenZipMethod.Lzma => "LZMA",
        SevenZipMethod.BZip2 => "BZip2",
        SevenZipMethod.Deflate => "Deflate",
        _ => "Copy",
    };

    private static bool IsSupportedByTar(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Tar => true,
        ArchiveFormat.GZip => caps.SupportsGzip,
        ArchiveFormat.Bz2 => caps.SupportsBz2,
        ArchiveFormat.Xz => caps.SupportsXz,
        ArchiveFormat.Zstd => caps.SupportsZstd,
        ArchiveFormat.Lzma => caps.SupportsLzma,
        ArchiveFormat.Rar => caps.SupportsRar,
        ArchiveFormat.SevenZip => caps.Supports7z,
        _ => false,
    };

    // T-F403: "needs a newer tar.exe" only where the version is what is missing; a new enough
    // tar.exe built without the codec gets the codec's name.
    private static CoreText BuildUnsupportedReason(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Rar => NewerOrMissing("RAR", caps),
        ArchiveFormat.SevenZip => NewerOrMissing("7-Zip", caps),
        ArchiveFormat.Zstd => NewerOrMissing("Zstandard", caps),
        ArchiveFormat.Xz => CoreMessages.Text(MessageCode.FormatNotSupportedByTar, "XZ", caps.Version),
        ArchiveFormat.Lzma => CoreMessages.Text(MessageCode.FormatNotSupportedByTar, "LZMA", caps.Version),
        ArchiveFormat.Bz2 => CoreMessages.Text(MessageCode.FormatNotSupportedByTar, "bzip2", caps.Version),
        ArchiveFormat.GZip => CoreMessages.Text(MessageCode.FormatNotSupportedByTar, "gzip", caps.Version),
        _ => CoreMessages.Text(MessageCode.ArchiveFormatNotSupportedByTar, caps.Version),
    };

    private static CoreText NewerOrMissing(string name, TarCapabilities caps) =>
        TarVersionParser.MeetsModernVersion(caps.Version)
            ? CoreMessages.Text(MessageCode.FormatNotSupportedByTar, name, caps.Version)
            : CoreMessages.Text(MessageCode.FormatNeedsNewerTar, name, caps.Version);
}
