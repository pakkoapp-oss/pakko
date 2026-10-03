using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Archiver.Core.IO;

/// <summary>Where a sweep runs, which decides what it may conclude from a process id.</summary>
/// <param name="LocalOnly">The folder belongs to this machine alone (%TEMP%). Next to a destination
/// it may not: a sync client or a share shows it to other machines, whose process ids mean nothing
/// here.</param>
/// <param name="UnownedMaxAge">How old an entry must be before it is removed when its owner cannot be
/// checked.</param>
internal readonly record struct TempScope(bool LocalOnly, TimeSpan UnownedMaxAge)
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

    /// <summary>This process's owner tag.</summary>
    public static string CurrentTag { get; } = string.Create(CultureInfo.InvariantCulture,
        $"m{MachineHash}-{Environment.ProcessId}-{CurrentStartTicks()}");

    /// <summary>A new name owned by this process: prefix, tag, a unique part, suffix.</summary>
    public static string NewName(string prefix, string suffix = "") =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{CurrentTag}-{Guid.NewGuid():N}{suffix}");

    /// <summary>
    /// True when the process a bare tag names runs on this machine. Also reads v1.6.0's
    /// "&lt;pid&gt;-&lt;start ticks&gt;". A name that does not parse is a leftover and counts as gone;
    /// a process whose start time cannot be read counts as running, so nothing is deleted on a guess.
    /// </summary>
    public static bool IsRunningHere(string tag) =>
        Parse(tag, requireUnique: false) is { Pid: { } pid } owner && IsProcessAlive(pid, owner.StartTicks);

    internal static void SweepStale(string directory, string prefix, string suffix, TempScope scope) =>
        SweepStale(directory, prefix, suffix, scope, new TempOwnerProbe(MachineHash, IsProcessAlive, DateTime.UtcNow));

    /// <summary>Best-effort: an entry that cannot be listed, checked or removed stays for the next sweep.</summary>
    internal static void SweepStale(string directory, string prefix, string suffix, TempScope scope, TempOwnerProbe probe)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = [.. new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .Where(e => e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // best-effort: nothing to sweep in a folder that cannot be listed
        }

        foreach (FileSystemInfo entry in entries)
        {
            try
            {
                if (IsStale(entry, prefix, suffix, scope, probe))
                    Remove(entry);
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
    // once none is left, so the recursive delete cannot reach the user's files.
    private static void Remove(FileSystemInfo entry)
    {
        if (entry is FileInfo file)
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
            file.Delete();
            return;
        }
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(entry.FullName, recursive: false);
            return;
        }

        foreach (WalkEntry walked in DirectoryWalker.Walk(entry.FullName).ToList())
        {
            if (walked.Kind == WalkEntryKind.ReparsePoint)
            {
                if (walked.Info is DirectoryInfo)
                    Directory.Delete(walked.Info.FullName, recursive: false);
                else
                    File.Delete(walked.Info.FullName);
            }
            else if (walked.Kind == WalkEntryKind.File && (walked.Info.Attributes & FileAttributes.ReadOnly) != 0)
            {
                walked.Info.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
        Directory.Delete(entry.FullName, recursive: true);
    }

    private readonly record struct Owner(string? Machine, int? Pid, long? StartTicks);

    // Forms, '-' or '_' between parts: "m<8 hex>-<pid>-<ticks>[-<unique>]" (this version);
    // "<pid>-<ticks>" (v1.6.0 preview/nested folders); "<pid>-<hex>" (v1.6.0 extraction and tar
    // staging); "<32 hex>" (no owner). Anything else is unparsable.
    private static Owner? Parse(string text, bool requireUnique)
    {
        string[] parts = text.Split('-', '_');
        if (parts.Length >= 3 && IsMachine(parts[0]))
        {
            bool shapeOk = requireUnique ? parts.Length == 4 && IsHex(parts[3]) : parts.Length == 3;
            if (shapeOk && TryInt(parts[1], out int pid) && TryLong(parts[2], out long ticks))
                return new Owner(parts[0][1..], pid, ticks);
            return null;
        }
        if (parts.Length == 2 && TryInt(parts[0], out int olderPid))
        {
            if (parts[1].Length != 32 && TryLong(parts[1], out long olderTicks))
                return new Owner(null, olderPid, olderTicks);
            if (requireUnique && IsHex(parts[1]))
                return new Owner(null, olderPid, null);
            return null;
        }
        if (parts.Length == 1 && parts[0].Length == 32 && IsHex(parts[0]))
            return new Owner(null, null, null);
        return null;
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
