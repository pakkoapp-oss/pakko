using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Maps ArchiveFormat/ArchiveContainerFormat to the registry-string vocabulary used by
/// GroupPolicyOptions.AllowedFormats/BlockedFormats (T-F51). The two enums don't line up 1:1 —
/// e.g. creating ArchiveContainerFormat.TarGz is later detected on extraction as
/// ArchiveFormat.GZip — so both map onto the same 9-name vocabulary rather than each having its
/// own registry string set.
/// </summary>
public static class ArchiveFormatRegistryNames
{
    public static string ToRegistryName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => "zip",
        ArchiveFormat.Tar => "tar",
        ArchiveFormat.GZip => "gzip",
        ArchiveFormat.Bz2 => "bz2",
        ArchiveFormat.Xz => "xz",
        ArchiveFormat.Zstd => "zstd",
        ArchiveFormat.Lzma => "lzma",
        ArchiveFormat.Rar => "rar",
        ArchiveFormat.SevenZip => "sevenzip",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string ToRegistryName(ArchiveContainerFormat format) => format switch
    {
        ArchiveContainerFormat.Zip => "zip",
        ArchiveContainerFormat.Tar => "tar",
        ArchiveContainerFormat.TarGz => "gzip",
        ArchiveContainerFormat.TarBz2 => "bz2",
        ArchiveContainerFormat.TarXz => "xz",
        ArchiveContainerFormat.TarZst => "zstd",
        ArchiveContainerFormat.TarLzma => "lzma",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
