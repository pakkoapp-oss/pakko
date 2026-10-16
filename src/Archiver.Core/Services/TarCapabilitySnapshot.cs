using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// What this system's tar.exe can do, left in a file for the Explorer extension, which cannot run
/// tar.exe itself. T-F342: whether it writes 7z (<see cref="Interfaces.ITarService.AskSevenZipWriterAsync"/>);
/// T-F403: which formats it reads, by their Group Policy names, from Core's one rule
/// (<see cref="ArchiveFormatPolicy.TarReadableRegistryNames"/>). The file names the tar.exe it was
/// measured on by size and write time, so a Windows update that replaces tar.exe makes it stale.
/// The extension hides "Add to name.7z" only on a matching "no", and an archive item only for a
/// format the matching file does not list; anything else shows, and Core still refuses what it
/// cannot do.
/// </summary>
public static class TarCapabilitySnapshot
{
    /// <summary>The verdict file's name inside the package's LocalCache folder.</summary>
    public const string FileName = "tar-capabilities.txt";

    /// <summary>
    /// The verdict file of the package installed in <paramref name="moduleDirectory"/> (a folder
    /// named by the package full name), or null when that is not an installed package's folder.
    /// The path is spelled out, not left to the per-package redirection of %LOCALAPPDATA%, so the
    /// extension's host reads the same file whatever identity it runs with.
    /// </summary>
    public static string? PathFor(string moduleDirectory, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(moduleDirectory);
        ArgumentNullException.ThrowIfNull(localAppData);

        // Name_Version_Architecture_ResourceId_PublisherId; the family name is the first and the last.
        string[] parts = Path.GetFileName(Path.TrimEndingDirectorySeparator(moduleDirectory)).Split('_');
        if (parts.Length != 5 || !IsNamePart(parts[0]) || !IsNamePart(parts[4]))
            return null;

        string packageData = Path.Combine(localAppData, "Packages", $"{parts[0]}_{parts[4]}");
        return Directory.Exists(packageData) ? Path.Combine(packageData, "LocalCache", FileName) : null;
    }

    private static bool IsNamePart(string part) =>
        part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && part.Any(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// The file's one line: tar.exe's size, its write time as a FILETIME, 1 or 0 for the 7z writer,
    /// and, when <paramref name="readable"/> is given, the formats it reads, comma-separated.
    /// </summary>
    public static string Format(long tarSize, long tarLastWriteFileTime, bool canCreate, IReadOnlyList<string>? readable = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{tarSize};{tarLastWriteFileTime};{(canCreate ? 1 : 0)}")
        + (readable is null ? string.Empty : ";" + string.Join(',', readable));

    /// <summary>
    /// Records what tar.exe answered (<paramref name="answer"/>) for the system tar.exe. Best
    /// effort: false when nothing was written. Nothing is written without an answer (null: the
    /// probe timed out or could not start tar.exe), nor under DisableTarExtraction, where the
    /// probe does not run: neither says anything about this tar.exe, and a recorded "no" hides
    /// the menu item until something records again.
    /// </summary>
    public static bool TryWrite(GroupPolicyOptions policy, string? verdictPath, bool? answer, TarCapabilities? capabilities = null) =>
        TryWrite(policy, verdictPath, TarSandboxedService.TarExecutablePath, answer, capabilities);

    /// <summary>
    /// <see cref="TryWrite(GroupPolicyOptions, string?, bool?, TarCapabilities?)"/> for the package this
    /// process was started from; nothing is written when it was not started from an installed package.
    /// </summary>
    public static bool TryRecord(GroupPolicyOptions policy, bool? answer, TarCapabilities? capabilities = null) => TryWrite(
        policy,
        PathFor(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        answer,
        capabilities);

    internal static bool TryWrite(
        GroupPolicyOptions policy, string? verdictPath, string tarPath, bool? answer, TarCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (verdictPath is null || answer is not { } canCreate || policy.DisableTarExtraction)
            return false;

        string temp = verdictPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var tar = new FileInfo(tarPath);
            if (!tar.Exists)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(verdictPath)!);
            File.WriteAllText(temp, Format(
                tar.Length, tar.LastWriteTimeUtc.ToFileTimeUtc(), canCreate,
                capabilities is null ? null : ArchiveFormatPolicy.TarReadableRegistryNames(capabilities)));
            File.Move(temp, verdictPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                /* best-effort: the verdict is a hint, and a leftover temp file is only clutter */
            }
            return false;
        }
    }
}
