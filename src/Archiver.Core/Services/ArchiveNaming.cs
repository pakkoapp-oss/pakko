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

    /// <summary>Strips an archive's extension, compound tar extensions included (see class remarks).</summary>
    public static string GetBaseName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);

        string? matchedExt = CompoundExtensions.FirstOrDefault(
            ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (matchedExt is not null)
            return fileName[..^matchedExt.Length];

        return Path.GetFileNameWithoutExtension(archivePath);
    }

    /// <summary>
    /// T-F99: shared by ZipArchiveService.ArchiveAsync and TarSandboxedService.CompressAsync — an
    /// explicit user-provided name always wins; otherwise falls back to the sole source path's own
    /// file name, or "archive" when that's empty (a drive-root selection like "Z:\" via the shell
    /// extension's Drive ItemType has no file name component to fall back to).
    /// </summary>
    public static string ResolveSingleArchiveName(string? explicitName, IReadOnlyList<string> sourcePaths)
    {
        if (explicitName is not null)
            return explicitName;

        if (sourcePaths.Count != 1)
            return "archive";

        string name = Path.GetFileNameWithoutExtension(sourcePaths[0]);
        return name.Length > 0 ? name : "archive";
    }

    /// <summary>Maps a creation-time container format to its on-disk file extension.</summary>
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
