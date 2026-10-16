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

    // A snapshot is one short line; anything longer is not ours (C++ reads at most 256 bytes).
    private const int MaxSnapshotLength = 256;

    /// <summary>
    /// The file's one line: tar.exe's size, its write time as a FILETIME, 1 or 0 for the 7z writer,
    /// and, when <paramref name="readable"/> is given, the formats it reads, comma-separated.
    /// </summary>
    public static string Format(long tarSize, long tarLastWriteFileTime, bool canCreate, IReadOnlyList<string>? readable = null) =>
        Format(tarSize, tarLastWriteFileTime, canCreate ? '1' : '0', readable);

    private static string Format(long tarSize, long tarLastWriteFileTime, char writer, IReadOnlyList<string>? readable) =>
        string.Create(CultureInfo.InvariantCulture, $"{tarSize};{tarLastWriteFileTime};{writer}")
        + (readable is null ? string.Empty : ";" + string.Join(',', readable));

    /// <summary>
    /// Records what tar.exe answered (<paramref name="answer"/>) and, with
    /// <paramref name="capabilities"/>, what it reads, for the system tar.exe. Best effort: false
    /// when nothing was written. Nothing is written under DisableTarExtraction, where tar.exe does
    /// not run, nor when there is neither an answer nor capabilities with a version: neither says
    /// anything about this tar.exe. T-F403: capabilities without an answer (the Shell or pakko did
    /// not ask about the 7z writer) keep the answer already recorded for this very tar.exe, or
    /// write "?" (not asked), which the menu treats as "shown", never as "no".
    /// </summary>
    public static bool TryWrite(GroupPolicyOptions policy, string? verdictPath, bool? answer, TarCapabilities? capabilities = null) =>
        TryWrite(policy, verdictPath, TarSandboxedService.TarExecutablePath, answer, capabilities);

    /// <summary>
    /// <see cref="TryWrite(GroupPolicyOptions, string?, bool?, TarCapabilities?)"/> for the package this
    /// process was started from; nothing is written when it was not started from an installed package.
    /// </summary>
    public static bool TryRecord(GroupPolicyOptions policy, bool? answer, TarCapabilities? capabilities = null) => TryWrite(
        policy, PackageSnapshotPath(), answer, capabilities);

    /// <summary>
    /// T-F403: for the Shell and pakko, which do not ask about the 7z writer: records what tar.exe
    /// reads only when this package's snapshot is missing, older (no list) or names another
    /// tar.exe (a Windows update replaced it), so the menu learns it without the App having run.
    /// <paramref name="capabilities"/> is asked only then: a current snapshot costs no tar.exe run.
    /// </summary>
    public static async Task<bool> RecordIfStaleAsync(GroupPolicyOptions policy, Func<Task<TarCapabilities>> capabilities)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(capabilities);
        return await RecordIfStaleAsync(policy, PackageSnapshotPath(), TarSandboxedService.TarExecutablePath, capabilities)
            .ConfigureAwait(false);
    }

    internal static async Task<bool> RecordIfStaleAsync(
        GroupPolicyOptions policy, string? path, string tarPath, Func<Task<TarCapabilities>> capabilities)
    {
        if (path is null || policy.DisableTarExtraction || IsCurrent(path, tarPath))
            return false;
        return TryWrite(policy, path, tarPath, answer: null, await capabilities().ConfigureAwait(false));
    }

    /// <summary>Whether the snapshot at <paramref name="path"/> lists what the tar.exe at <paramref name="tarPath"/> reads.</summary>
    internal static bool IsCurrent(string path, string tarPath) =>
        Read(path, tarPath) is { HasList: true };

    private static string? PackageSnapshotPath() =>
        PathFor(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    // The recorded 7z answer ('0', '1' or '?') and whether a list follows, for a snapshot of exactly
    // this tar.exe; null for none, another tar.exe or a line this class did not write.
    private static (char Writer, bool HasList)? Read(string path, string tarPath)
    {
        try
        {
            var tar = new FileInfo(tarPath);
            var file = new FileInfo(path);
            if (!tar.Exists || !file.Exists || file.Length > MaxSnapshotLength)
                return null;
            string prefix = string.Create(CultureInfo.InvariantCulture, $"{tar.Length};{tar.LastWriteTimeUtc.ToFileTimeUtc()};");
            string text = File.ReadAllText(path);
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length <= prefix.Length)
                return null;
            char writer = text[prefix.Length];
            if (writer is not ('0' or '1' or '?'))
                return null;
            return (writer, text.Length > prefix.Length + 1 && text[prefix.Length + 1] == ';');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool TryWrite(
        GroupPolicyOptions policy, string? verdictPath, string tarPath, bool? answer, TarCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        bool hasCapabilities = capabilities is { Version.Length: > 0 };
        if (verdictPath is null || policy.DisableTarExtraction || (answer is null && !hasCapabilities))
            return false;

        string temp = verdictPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var tar = new FileInfo(tarPath);
            if (!tar.Exists)
                return false;

            char writer = answer switch
            {
                true => '1',
                false => '0',
                null => Read(verdictPath, tarPath)?.Writer ?? '?',
            };
            Directory.CreateDirectory(Path.GetDirectoryName(verdictPath)!);
            File.WriteAllText(temp, Format(
                tar.Length, tar.LastWriteTimeUtc.ToFileTimeUtc(), writer,
                hasCapabilities ? ArchiveFormatPolicy.TarReadableRegistryNames(capabilities!) : null));
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
                /* best-effort: the snapshot is a hint, and a leftover temp file is only clutter */
            }
            return false;
        }
    }
}
