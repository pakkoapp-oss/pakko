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

    internal static IReadOnlyList<string> CompoundExtensionList => CompoundExtensions;

    private const string FallbackName = "archive";

    /// <summary>
    /// T-F264: the one default name for a new archive, used by the App (blank name box), Explorer's
    /// "Add to" commands and — mirrored in ShellExtUtils.cpp's BuildAddToArchiveTitle — the menu
    /// title. One source: its name without extension (compound tar extensions stripped as a unit, a
    /// dotfile keeps its full name). Several sources: the folder that holds the first one. A drive
    /// root is named after its letter ("C", T-F281). Falls back to "archive" where that gives no
    /// usable name (no sources, a name of only an extension).
    /// </summary>
    public static string GetDefaultArchiveName(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
            return FallbackName;

        string first = Path.TrimEndingDirectorySeparator(sourcePaths[0]);
        string name = sourcePaths.Count > 1
            ? LastSegment(Path.GetDirectoryName(first) ?? "")
            : GetBaseName(first);
        if (IsDriveSpecifier(name))
            return char.ToUpperInvariant(name[0]).ToString();
        return name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? FallbackName : name;
    }

    private static bool IsDriveSpecifier(string name) =>
        name.Length == 2 && name[1] == ':' && char.IsAsciiLetter(name[0]);

    // The text after the last separator, like the C++ side's PathFindFileNameW. Unlike
    // Path.GetFileName it gives "share" for a UNC root "\\server\share" (GetFileName gives ""), and
    // "C:" for a drive root, which GetDefaultArchiveName turns into the drive letter.
    private static string LastSegment(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed[(trimmed.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) + 1)..];
    }

    /// <summary>
    /// T-F264: the "name (1)", "name (2)", ... rule every rename-on-conflict path uses — the number
    /// goes before the extension. <paramref name="isTaken"/> decides what counts as taken (a file
    /// on disk, a name already claimed in memory). A compound tar extension counts as one (T-F362:
    /// "src (1).tar.gz", not "src.tar (1).gz").
    /// </summary>
    public static string GetUniqueName(string fileName, Func<string, bool> isTaken)
    {
        if (!isTaken(fileName))
            return fileName;

        string? compound = CompoundExtensions.FirstOrDefault(
            ext => fileName.Length > ext.Length && fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (compound is not null)
            return Number(fileName[..^compound.Length], fileName[^compound.Length..], isTaken);
        return Number(Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName), isTaken);
    }

    /// <summary>
    /// T-F264: a folder under <paramref name="parentDir"/> that does not exist yet — "name", else
    /// "name (1)", ... with the number after the whole name (a folder has no extension).
    /// </summary>
    public static string GetUniqueFolderName(string parentDir, string name)
    {
        bool Exists(string candidate) => Directory.Exists(Path.Combine(parentDir, candidate));
        return Exists(name) ? Number(name, "", Exists) : name;
    }

    private static string Number(string stem, string extension, Func<string, bool> isTaken)
    {
        int i = 1;
        string candidate;
        do { candidate = $"{stem} ({i++}){extension}"; }
        while (isTaken(candidate));
        return candidate;
    }

    /// <summary>
    /// The file name of a single archive: <see cref="ArchiveOptions.ExactFileName"/> as given, else
    /// the resolved base name plus the format's extension.
    /// </summary>
    public static string SingleArchiveFileName(ArchiveOptions options) =>
        options.ExactFileName ?? ResolveSingleArchiveName(options.ArchiveName, options.SourcePaths) + GetExtension(options.Format);

    /// <summary>
    /// The item a refusal to create anything at all is about (T-F326): the archive that was to be
    /// written, or the first source when every source would get its own archive. Never the
    /// destination folder - it reads as the thing that failed.
    /// </summary>
    internal static string RefusedArchivePath(ArchiveOptions options) =>
        options.Mode == ArchiveMode.SeparateArchives && options.SourcePaths.Count > 0
            ? options.SourcePaths[0]
            : Path.Combine(options.DestinationFolder, SingleArchiveFileName(options));

    /// <summary>Strips an archive's extension, compound tar extensions included (see class remarks).</summary>
    public static string GetBaseName(string archivePath)
    {
        string fileName = LastSegment(archivePath);

        string? matchedExt = CompoundExtensions.FirstOrDefault(
            ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (matchedExt is not null)
            return fileName[..^matchedExt.Length];

        // T-F264: a leading dot is not an extension (".gitignore", an archive named ".zip") — the
        // same rule as ShellExtUtils.cpp's GetFileNameWithoutExtension, instead of an empty name.
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    /// <summary>
    /// T-F99: shared by ZipArchiveService.ArchiveAsync and TarSandboxedService.CompressAsync — an
    /// explicit user-provided name always wins; otherwise <see cref="GetDefaultArchiveName"/> (T-F264).
    /// </summary>
    /// <remarks>
    /// T-F185: an explicit name is a bare file-name component, never a path — both callers combine
    /// the return value directly with a separate DestinationFolder via Path.Combine, which does not
    /// sanitize "..\" segments. Path.GetFileName strips any directory component (including a
    /// leading "..\..\") down to the final segment, closing a real path-traversal write confirmed
    /// via CompressAsync_DestinationNameWithParentTraversalSegments_NeverWritesOutsideIntendedTree
    /// (a "..\..\evil" ArchiveName landed the created archive outside DestinationFolder entirely
    /// before this fix). Falls back to "archive" if that strips the name down to nothing (e.g. an
    /// explicit name of just "..\" or "\").
    /// </remarks>
    public static string ResolveSingleArchiveName(string? explicitName, IReadOnlyList<string> sourcePaths)
    {
        if (explicitName is not null)
        {
            string sanitized = Path.GetFileName(explicitName);
            return sanitized.Length > 0 ? sanitized : "archive";
        }

        return GetDefaultArchiveName(sourcePaths);
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

    /// <summary>
    /// T-F159: the "name (1)", "name (2)", ... rename-on-conflict path for a file that
    /// already exists at <paramref name="path"/> — shared by ZIP and tar-family archive creation
    /// and extraction. <paramref name="claimedPaths"/> (T-F30) also excludes candidates reserved
    /// in memory this run but not yet written, which File.Exists cannot see.
    /// </summary>
    internal static string GetUniqueFilePath(string path, HashSet<string>? claimedPaths = null)
    {
        string dir = Path.GetDirectoryName(path)!;
        bool IsTaken(string candidate)
        {
            string full = Path.Combine(dir, candidate);
            return File.Exists(full) || (claimedPaths?.Contains(full) ?? false);
        }

        // Always numbered, even when the name itself is free: callers come here only after a conflict.
        string fileName = Path.GetFileName(path);
        return Path.Combine(dir, GetUniqueName(fileName, c => c == fileName || IsTaken(c)));
    }
}
