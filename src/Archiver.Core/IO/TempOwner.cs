using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.IO;

/// <summary>Where a sweep runs, which decides what it may conclude from a process id.</summary>
/// <param name="LocalOnly">The folder belongs to this machine alone (%TEMP%). Next to a destination
/// it may not: a sync client or a share shows it to other machines, whose process ids mean nothing
/// here.</param>
/// <param name="UnownedMaxAge">How old an entry must be before it is removed when its owner cannot be
/// checked.</param>
/// <param name="TakeBack">T-F527: entries are sandbox quarantines, private to the user from their birth:
/// one whose DACL lets the user nothing is taken back (<see cref="TempOwner.TryRemoveQuarantine"/>).</param>
internal readonly record struct TempScope(bool LocalOnly, TimeSpan UnownedMaxAge, bool TakeBack = false)
{
    /// <summary>A folder next to a destination the user chose.</summary>
    public static TempScope Destination => new(false, TimeSpan.FromDays(1));

    /// <summary>A Pakko folder under %TEMP%.</summary>
    public static TempScope LocalTemp(TimeSpan unownedMaxAge) => new(true, unownedMaxAge);
}

/// <summary>What a sweep needs from the machine — replaced in tests.</summary>
internal sealed record TempOwnerProbe(string MachineHash, Func<int, long?, bool> IsAlive, DateTime UtcNow);

/// <summary>
/// T-F263/T-F312: the one owner of the names of Pakko's temporary files and folders. A name holds
/// its owner as "m&lt;machine&gt;-&lt;pid&gt;-&lt;start ticks&gt;" (the start time tells a reused
/// process id apart). A run creates its names fresh and removes them itself; a sweep at the start of
/// the next operation in the same folder removes what a killed run left.
/// </summary>
public static class TempOwner
{
    private static readonly string MachineHash = HashMachineName(Environment.MachineName);

    private static readonly char[] NameSeparators = ['-', '_'];

    /// <summary>"&lt;pid&gt;-&lt;start ticks&gt;" — v1.6.0's owner name, still used under %TEMP%, where
    /// the machine adds nothing and a v1.6.0 window would take a longer name for a dead owner's.</summary>
    public static string ProcessTag { get; } = string.Create(CultureInfo.InvariantCulture,
        $"{Environment.ProcessId}-{CurrentStartTicks()}");

    /// <summary>This process's owner tag.</summary>
    public static string CurrentTag { get; } = "m" + MachineHash + "-" + ProcessTag;

    /// <summary>A new name owned by this process: prefix, tag, a unique part, suffix.</summary>
    public static string NewName(string prefix, string suffix = "") =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{CurrentTag}-{Guid.NewGuid():N}{suffix}");

    /// <summary>
    /// T-F316: true when <paramref name="fullPath"/>, below <paramref name="fullRoot"/>, is or lies
    /// inside something this process named with <see cref="NewName"/>. A walk of a folder that holds
    /// the run's destination meets the run's own temporary file and chunk folder; they are not the
    /// user's content.
    /// </summary>
    internal static bool IsOwnBelow(string fullRoot, string fullPath)
    {
        // T-F531: a walk gives a name Windows trims, and what is below it, by its literal path.
        if (IsLiteral(fullPath) && !IsLiteral(fullRoot))
            fullRoot = InputPaths.Literal(fullRoot);
        return fullPath.Length > fullRoot.Length && fullPath.AsSpan(fullRoot.Length).Contains(CurrentTag, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the process a bare tag names runs on this machine. Also reads v1.6.0's
    /// "&lt;pid&gt;-&lt;start ticks&gt;". A name that does not parse is a leftover and counts as gone;
    /// a process whose start time cannot be read counts as running, so nothing is deleted on a guess.
    /// </summary>
    public static bool IsRunningHere(string tag) =>
        Parse(tag, requireUnique: false) is { Pid: { } pid } owner && IsProcessAlive(pid, owner.StartTicks);

    /// <summary>
    /// T-F511: sweeps a Pakko folder under %TEMP% of the entries named with <paramref name="prefix"/>
    /// whose owner no longer runs here; an entry whose owner cannot be told goes once older than
    /// <paramref name="unownedMaxAge"/>. Best-effort. A folder outside %TEMP% is left alone: there an
    /// old entry may be the user's.
    /// </summary>
    public static void SweepStaleInTemp(string directory, string prefix, TimeSpan unownedMaxAge)
    {
        // A name Windows trims would list, and sweep, its twin; a link would list, and sweep, the
        // folder it reaches (the T-F511 review: %TEMP%\Archiver.CLI.Stdin planted as a junction).
        if (InputPaths.Problem(directory) is null && IsBelowTemp(directory) && !AnyLinkBelowTemp(directory))
            SweepStale(directory, prefix, "", TempScope.LocalTemp(unownedMaxAge));
    }

    // The folder itself or one between it and %TEMP% is a link; one that cannot be read counts as
    // one. %TEMP% itself is the user's to place.
    private static bool AnyLinkBelowTemp(string directory)
    {
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        try
        {
            for (string? folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                 folder is not null && folder.Length > temp.Length;
                 folder = Path.GetDirectoryName(folder))
            {
                var info = new DirectoryInfo(folder);
                if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // fail closed: nothing is swept
        }
    }

    private static bool IsBelowTemp(string directory)
    {
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// T-F511: removes a temporary file or folder Pakko named: each link on its own, never its target,
    /// and a read-only file too. Best-effort: false when something in it is held, for a later sweep.
    /// A path Windows cannot open as spelled is refused: a name it trims would remove the twin.
    /// T-F533: a literal path is opened as spelled, so no part of it is trimmed. T-F547: the folder is
    /// removed by literal paths throughout, so a name Windows trims inside it is reached as itself.
    /// </summary>
    public static bool TryRemove(string path) => TryRemoveEntry(path, takeBack: false);

    /// <summary>
    /// T-F527: <see cref="TryRemove(string)"/> for a sandbox quarantine root, which only the user and the
    /// sandbox could ever write to: an entry the sandbox left with a DACL that lets the user nothing is
    /// taken back by its owner and removed. Never for a folder another principal can write to, where a
    /// planted entry could steer the walk.
    /// </summary>
    internal static bool TryRemoveQuarantine(string path) => TryRemoveEntry(path, takeBack: true);

    private static bool TryRemoveEntry(string path, bool takeBack)
    {
        if (IsLiteral(path) ? path.Contains('\0') : InputPaths.Problem(path) is not null)
            return false;
        try
        {
            // After the check: a plain path Windows would trim is the twin's by GetFullPath.
            string literal = InputPaths.Literal(Path.GetFullPath(path));
            FileSystemInfo entry = Directory.Exists(literal) ? new DirectoryInfo(literal) : new FileInfo(literal);
            if (entry.Exists)
                Remove(entry, takeBack);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLiteral(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);

    internal static void SweepStale(string directory, string prefix, string suffix, TempScope scope) =>
        SweepStale(directory, [(prefix, suffix)], scope);

    /// <summary>T-F528: one listing of <paramref name="directory"/> for several names: a folder of
    /// thousands of entries is listed once per sweep, not once per name.</summary>
    internal static void SweepStale(string directory, IReadOnlyList<(string Prefix, string Suffix)> names, TempScope scope) =>
        SweepStale(directory, names, scope, new TempOwnerProbe(MachineHash, IsProcessAlive, DateTime.UtcNow));

    internal static void SweepStale(string directory, string prefix, string suffix, TempScope scope, TempOwnerProbe probe) =>
        SweepStale(directory, [(prefix, suffix)], scope, probe);

    /// <summary>Best-effort: an entry that cannot be listed, checked or removed stays for the next sweep.</summary>
    internal static void SweepStale(string directory, IReadOnlyList<(string Prefix, string Suffix)> names, TempScope scope, TempOwnerProbe probe)
    {
        List<(FileSystemInfo Entry, string Prefix, string Suffix)> entries;
        try
        {
            entries = [.. new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .Select(e => (Entry: e, Name: names.FirstOrDefault(n => e.Name.StartsWith(n.Prefix, StringComparison.OrdinalIgnoreCase))))
                .Where(m => m.Name.Prefix is not null)
                .Select(m => (m.Entry, m.Name.Prefix, m.Name.Suffix))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // best-effort: nothing to sweep in a folder that cannot be listed
        }

        foreach ((FileSystemInfo entry, string prefix, string suffix) in entries)
        {
            try
            {
                if (IsStale(entry, prefix, suffix, scope, probe))
                    Remove(entry, scope.TakeBack);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort: a held entry is swept by a later operation
            }
        }
    }

    private static bool IsStale(FileSystemInfo entry, string prefix, string suffix, TempScope scope, TempOwnerProbe probe)
    {
        string rest = entry.Name[prefix.Length..];
        if (suffix.Length > 0 && rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            rest = rest[..^suffix.Length];

        Owner? owner = Parse(rest, requireUnique: true);
        bool checkable = owner is { Pid: not null }
            && (scope.LocalOnly || string.Equals(owner.Value.Machine, probe.MachineHash, StringComparison.Ordinal));
        if (checkable)
            return !probe.IsAlive(owner!.Value.Pid!.Value, owner.Value.StartTicks);

        return probe.UtcNow - entry.CreationTimeUtc > scope.UnownedMaxAge;
    }

    // Links first, each on its own (deleting a link never touches its target); the tree goes only
    // once none is left, so the recursive delete cannot reach the user's files. T-F547: a folder is
    // walked by its literal path (TryRemove's, or the sweep's listing below a checked %TEMP% or
    // destination folder), one folder listed at a time as it is walked.
    // T-F527: a compromised sandboxed run can leave a folder or file whose DACL lets the user nothing
    // (the sandbox's own SID only); the token's default owner owns it (measured), and an owner may always
    // write the DACL. Taken back then, only in a quarantine, and removed again. An OWNER RIGHTS entry takes
    // that right away too: then it stays, and only an administrator can remove it (measured).
    private static void Remove(FileSystemInfo entry, bool takeBack)
    {
        try
        {
            RemoveOnce(entry);
        }
        catch (UnauthorizedAccessException) when (takeBack && OperatingSystem.IsWindows())
        {
            TakeBack(entry);
            RemoveOnce(entry);
        }
    }

    // Top-down, each folder given back before it is listed; a link is never entered (it goes as a link),
    // and an entry the user does not own is neither given back nor entered.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TakeBack(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            return;
        string literal = InputPaths.Literal(entry.FullName);
        if (!QuarantineAcl.GiveBackIfOwned(literal) || entry is not DirectoryInfo)
            return;
        var pending = new Stack<string>();
        pending.Push(literal);
        while (pending.TryPop(out string? folder))
        {
            foreach (FileSystemInfo inner in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if ((inner.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (QuarantineAcl.GiveBackIfOwned(inner.FullName) && inner is DirectoryInfo)
                    pending.Push(inner.FullName);
            }
        }
    }

    private static void RemoveOnce(FileSystemInfo entry)
    {
        string literal = InputPaths.Literal(entry.FullName);
        if (entry is FileInfo)
        {
            File.SetAttributes(literal, entry.Attributes & ~FileAttributes.ReadOnly);
            File.Delete(literal);
            return;
        }
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(literal, recursive: false);
            return;
        }

        foreach (WalkEntry walked in DirectoryWalker.WalkAsSpelled(literal))
        {
            switch (walked.Kind)
            {
                case WalkEntryKind.ReparsePoint:
                    if (walked.Info is DirectoryInfo)
                        Directory.Delete(walked.Info.FullName, recursive: false);
                    else
                        File.Delete(walked.Info.FullName);
                    break;
                case WalkEntryKind.File when (walked.Info.Attributes & FileAttributes.ReadOnly) != 0:
                    walked.Info.Attributes &= ~FileAttributes.ReadOnly;
                    break;
                // Walked as spelled, so no name is trimmed or a device; the recursive delete below
                // removes the rest, and reports what it cannot.
                case WalkEntryKind.File:
                case WalkEntryKind.Directory:
                case WalkEntryKind.UnreadableDirectory:
                case WalkEntryKind.LeftOut:
                case WalkEntryKind.TrimmedName:
                case WalkEntryKind.DeviceName:
                    break;
            }
        }
        Directory.Delete(literal, recursive: true);
    }

    private readonly record struct Owner(string? Machine, int? Pid, long? StartTicks);

    // Forms, '-' or '_' between parts: "m<8 hex>-<pid>-<ticks>[-<unique>]" (this version);
    // "<pid>-<ticks>" (v1.6.0 preview/nested folders); "<pid>-<hex>" (v1.6.0 extraction and tar
    // staging); "<32 hex>" (no owner). Anything else is unparsable.
    private static Owner? Parse(string text, bool requireUnique)
    {
        string[] parts = text.Split(NameSeparators);
        if (parts.Length >= 3 && IsMachine(parts[0]))
            return ParseTagged(parts, requireUnique);
        if (parts.Length == 2)
            return ParseOlder(parts, requireUnique);
        if (parts.Length == 1 && parts[0].Length == 32 && IsHex(parts[0]))
            return new Owner(null, null, null);
        return null;
    }

    private static Owner? ParseTagged(string[] parts, bool requireUnique)
    {
        bool shapeOk = requireUnique ? parts.Length == 4 && IsHex(parts[3]) : parts.Length == 3;
        return shapeOk && TryInt(parts[1], out int pid) && TryLong(parts[2], out long ticks)
            ? new Owner(parts[0][1..], pid, ticks)
            : null;
    }

    private static Owner? ParseOlder(string[] parts, bool requireUnique)
    {
        if (!TryInt(parts[0], out int pid))
            return null;
        if (parts[1].Length != 32 && TryLong(parts[1], out long ticks))
            return new Owner(null, pid, ticks);
        return requireUnique && IsHex(parts[1]) ? new Owner(null, pid, null) : null;
    }

    private static bool IsMachine(string part) => part.Length == 9 && part[0] == 'm' && IsHex(part[1..]);

    private static bool IsHex(string part) => part.Length > 0 && part.All(char.IsAsciiHexDigitLower);

    private static bool TryInt(string part, out int value) =>
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryLong(string part, out long value) =>
        long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool IsProcessAlive(int pid, long? startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return startTicks is null || process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (ArgumentException)
        {
            return false; // no process has this id
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true; // its start time cannot be read: never delete on a guess
        }
    }

    private static string HashMachineName(string machineName) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(machineName.ToUpperInvariant())), 0, 4);

    private static long CurrentStartTicks()
    {
        using var current = Process.GetCurrentProcess();
        return current.StartTime.ToUniversalTime().Ticks;
    }
}
