using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Derives a base name (for a destination folder, or an auto-named archive) from an archive's
/// file name. Path.GetFileNameWithoutExtension only strips the last extension, which is wrong for
/// the compound extensions tar.exe itself produces (T-F103: "archive.tar.gz" must strip to
/// "archive", not "archive.tar"). Kept in sync with ShellExtUtils.cpp's native equivalent.
/// </summary>
public static class ArchiveNaming
{
    private static readonly string[] CompoundExtensions =
    {
        ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lzma"
    };

    public static string GetBaseName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);

        foreach (string ext in CompoundExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return fileName[..^ext.Length];
        }

        return Path.GetFileNameWithoutExtension(archivePath);
    }

    public static string GetExtension(ArchiveContainerFormat format) => format switch
    {
        ArchiveContainerFormat.Zip => ".zip",
        ArchiveContainerFormat.Tar => ".tar",
        ArchiveContainerFormat.TarGz => ".tar.gz",
        ArchiveContainerFormat.TarBz2 => ".tar.bz2",
        ArchiveContainerFormat.TarXz => ".tar.xz",
        ArchiveContainerFormat.TarZst => ".tar.zst",
        ArchiveContainerFormat.TarLzma => ".tar.lzma",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
