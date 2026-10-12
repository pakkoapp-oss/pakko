using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F342: whether this system's tar.exe writes 7z, left in a file for the Explorer extension,
/// which cannot run the probe (<see cref="Interfaces.ITarService.AskSevenZipWriterAsync"/>). The
/// file names the tar.exe it was measured on by size and write time, so a Windows update that
/// replaces tar.exe makes the verdict stale. The extension hides "Add to name.7z" only on a
/// matching "no"; anything else shows the item, and Core still refuses a creation it cannot do.
/// </summary>
public static class SevenZipWriterVerdict
{
    /// <summary>The verdict file's name inside the package's LocalCache folder.</summary>
    public const string FileName = "sevenzip-writer.txt";

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

    /// <summary>The file's one line: tar.exe's size, its write time as a FILETIME, and 1 or 0.</summary>
    public static string Format(long tarSize, long tarLastWriteFileTime, bool canCreate) =>
        string.Create(CultureInfo.InvariantCulture, $"{tarSize};{tarLastWriteFileTime};{(canCreate ? 1 : 0)}");

    /// <summary>
    /// Records what tar.exe answered (<paramref name="answer"/>) for the system tar.exe. Best
    /// effort: false when nothing was written. Nothing is written without an answer (null: the
    /// probe timed out or could not start tar.exe), nor under DisableTarExtraction, where the
    /// probe does not run: neither says anything about this tar.exe, and a recorded "no" hides
    /// the menu item until something records again.
    /// </summary>
    public static bool TryWrite(GroupPolicyOptions policy, string? verdictPath, bool? answer) =>
        TryWrite(policy, verdictPath, TarSandboxedService.TarExecutablePath, answer);

    /// <summary>
    /// <see cref="TryWrite(GroupPolicyOptions, string?, bool?)"/> for the package this process was
    /// started from; nothing is written when it was not started from an installed package.
    /// </summary>
    public static bool TryRecord(GroupPolicyOptions policy, bool? answer) => TryWrite(
        policy,
        PathFor(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        answer);

    internal static bool TryWrite(GroupPolicyOptions policy, string? verdictPath, string tarPath, bool? answer)
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
            File.WriteAllText(temp, Format(tar.Length, tar.LastWriteTimeUtc.ToFileTimeUtc(), canCreate));
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
