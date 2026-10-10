using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Recovery;

/// <summary>
/// Writes a PAR2 set next to every archive an engine created (T-F275 step 2). A failure for one
/// archive is an <see cref="ArchiveError"/> on that archive, the archive stays, and every source
/// is downgraded to <see cref="SourceOutcome.Partial"/> — a result does not say which source went
/// into which archive, and "Delete after" must not take the sources of an unprotected archive.
/// Never throws except <see cref="OperationCanceledException"/>.
/// </summary>
internal static partial class RecoveryDataWriter
{
    internal static ArchiveResult AddTo(ArchiveResult result, int percent, Action<double> progress, CancellationToken cancellationToken)
    {
        var errors = new List<ArchiveError>(result.Errors);
        var warnings = new List<ArchiveWarning>(result.Warnings);
        var recoveryFiles = new List<string>();
        long[] lengths = [.. result.CreatedFiles.Select(LengthOrZero)];
        double total = Math.Max(1, lengths.Sum());
        double before = 0;

        for (int i = 0; i < result.CreatedFiles.Count; i++)
        {
            string archive = result.CreatedFiles[i];
            double offset = before;
            long weight = lengths[i];
            before += weight;
            (Par2CreateResult? created, ArchiveError? error) = CreateSet(archive, percent, f => progress((offset + f * weight) / total), cancellationToken);
            if (error is not null)
            {
                errors.Add(error);
                continue;
            }
            recoveryFiles.Add(created!.IndexPath);
            recoveryFiles.Add(created.VolumePath);
            warnings.AddRange(RemoveStaleVolumes(archive, created, cancellationToken));
        }

        bool failed = errors.Count > result.Errors.Count;
        return result with
        {
            Errors = errors,
            Warnings = warnings,
            RecoveryFiles = recoveryFiles,
            Sources = failed
                ? [.. result.Sources.Select(s => s.Outcome == SourceOutcome.Completed ? s with { Outcome = SourceOutcome.Partial } : s)]
                : result.Sources,
        };
    }

    private static (Par2CreateResult? Created, ArchiveError? Error) CreateSet(string archive, int percent, Action<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            long length = new FileInfo(archive).Length;
            return Par2Creator.ChooseParameters(length, percent) is { } parameters
                ? (Par2Creator.Create(archive, parameters, progress, cancellationToken), null)
                : (null, CoreMessages.Error(archive, MessageCode.RecoveryDataFileTooLarge));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, CoreMessages.Error(archive, CoreMessages.Wrap(MessageCode.RecoveryDataNotCreated, ex), ex));
        }
    }

    private static long LengthOrZero(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0; // only a progress weight; the loop reports the failure
        }
    }

    // A volume of an earlier set for the same archive name: the archive was rewritten, so that set
    // protects bytes that no longer exist. Only files the reader parses as a set with another Set
    // ID are removed; anything unreadable or not PAR2 is left alone. Best-effort, after the new set
    // is written: a folder that cannot be listed leaves nothing to remove, as in TempOwner.SweepStale.
    private static List<ArchiveWarning> RemoveStaleVolumes(string archive, Par2CreateResult created, CancellationToken cancellationToken)
    {
        Par2ReadResult current = Par2PacketReader.Read([created.IndexPath], cancellationToken);
        if (current.Sets.Count != 1)
            return [];
        UInt128 setId = current.Sets[0].SetId;
        string name = Path.GetFileName(archive);
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(Path.GetDirectoryName(archive)!, name + ".vol*.par2");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // best-effort: nothing to remove in a folder that cannot be listed
        }

        return Delete(candidates.Where(p => IsStaleVolume(p, name, created.VolumePath, setId, cancellationToken)));
    }

    /// <summary>
    /// An archive written without recovery data keeps the set of its earlier bytes, and a test
    /// would then call the new archive damaged. A set found by the test's own rule, naming this
    /// archive, whose length and first-16-KiB MD5 no longer match it, is removed: each of its files
    /// that holds packets of that set alone. A set matching by content, another file's set and
    /// anything unreadable stay. Best-effort: a file that cannot be deleted is a warning; with no
    /// warning, <paramref name="result"/> itself comes back.
    /// </summary>
    internal static ArchiveResult RemoveEarlierSets(ArchiveResult result, CancellationToken cancellationToken)
    {
        var warnings = new List<ArchiveWarning>();
        var folderHasPar2 = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string archive in result.CreatedFiles)
        {
            string? folder = Path.GetDirectoryName(archive);
            if (folder is null)
                continue;
            if (!folderHasPar2.TryGetValue(folder, out bool hasPar2))
                folderHasPar2[folder] = hasPar2 = HasPar2File(folder);
            if (hasPar2)
                warnings.AddRange(RemoveEarlierSet(archive, cancellationToken));
        }
        return warnings.Count == 0 ? result : result with { Warnings = [.. result.Warnings, .. warnings] };
    }

    // Most folders hold no PAR2 file at all; one filtered listing spares each archive the full
    // listings of SetFilesForTarget (a thousand separate archives into a big folder).
    private static bool HasPar2File(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.par2").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false; // best-effort: nothing to remove in a folder that cannot be listed
        }
    }

    private static List<ArchiveWarning> RemoveEarlierSet(string archive, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> setFiles;
        try
        {
            setFiles = Par2SetLocator.SetFilesForTarget(archive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return []; // best-effort: nothing to remove in a folder that cannot be listed
        }
        if (setFiles.Count == 0)
            return [];

        Par2ReadResult read = Par2PacketReader.Read(setFiles, cancellationToken);
        if (Par2SetLocator.Select(read.Sets, archive) is not { NameMatches: true } match
            || Par2SetLocator.ReadContentKey(archive) is not { } key
            || (key.Length == match.Set.FileLength && key.Md5First16k == match.Set.Md5First16k))
            return [];
        UInt128 setId = match.Set.SetId;
        return Delete(setFiles.Where(p => HoldsOnlySet(p, setId, cancellationToken)));
    }

    private static bool HoldsOnlySet(string path, UInt128 setId, CancellationToken cancellationToken)
    {
        Par2ReadResult read = Par2PacketReader.Read([path], cancellationToken);
        return read.UnreadableFiles == 0
            && (read.Sets.Count > 0 || read.Rejected.Count > 0)
            && read.Sets.All(s => s.SetId == setId)
            && read.Rejected.All(r => r.SetId == setId);
    }

    private static List<ArchiveWarning> Delete(IEnumerable<string> paths)
    {
        var warnings = new List<ArchiveWarning>();
        foreach (string path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add(CoreMessages.Warning(path, CoreMessages.Wrap(MessageCode.RecoveryOldVolumeNotDeleted, ex)));
            }
        }
        return warnings;
    }

    private static bool IsStaleVolume(string path, string archiveName, string newVolume, UInt128 setId, CancellationToken cancellationToken)
    {
        string candidate = Path.GetFileName(path);
        if (string.Equals(path, newVolume, StringComparison.OrdinalIgnoreCase)
            || !candidate.StartsWith(archiveName, StringComparison.OrdinalIgnoreCase)
            || !VolumeSuffix().IsMatch(candidate[archiveName.Length..]))
            return false;
        Par2ReadResult read = Par2PacketReader.Read([path], cancellationToken);
        return read.UnreadableFiles == 0
            && (read.Sets.Count > 0 || read.Rejected.Count > 0)
            && read.Sets.All(s => s.SetId != setId)
            && read.Rejected.All(r => r.SetId != setId);
    }

    [GeneratedRegex(@"^\.vol\d+[+-]\d+\.par2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeSuffix();
}
