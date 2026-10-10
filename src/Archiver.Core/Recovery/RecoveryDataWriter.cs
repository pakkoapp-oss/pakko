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
        bool failed = false;

        for (int i = 0; i < result.CreatedFiles.Count; i++)
        {
            string archive = result.CreatedFiles[i];
            double offset = before;
            long weight = lengths[i];
            try
            {
                long length = new FileInfo(archive).Length;
                if (Par2Creator.ChooseParameters(length, percent) is not { } parameters)
                {
                    errors.Add(CoreMessages.Error(archive, MessageCode.RecoveryDataFileTooLarge));
                    failed = true;
                    continue;
                }
                Par2CreateResult created = Par2Creator.Create(archive, parameters, f => progress((offset + f * weight) / total), cancellationToken);
                recoveryFiles.Add(created.IndexPath);
                recoveryFiles.Add(created.VolumePath);
                warnings.AddRange(RemoveStaleVolumes(archive, created, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(CoreMessages.Error(archive, CoreMessages.Wrap(MessageCode.RecoveryDataNotCreated, ex), ex));
                failed = true;
            }
            finally
            {
                before += weight;
            }
        }

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
    // ID are removed; anything unreadable or not PAR2 is left alone.
    private static IEnumerable<ArchiveWarning> RemoveStaleVolumes(string archive, Par2CreateResult created, CancellationToken cancellationToken)
    {
        string folder = Path.GetDirectoryName(archive)!;
        string name = Path.GetFileName(archive);
        Par2ReadResult current = Par2PacketReader.Read([created.IndexPath], cancellationToken);
        if (current.Sets.Count != 1)
            yield break;
        UInt128 setId = current.Sets[0].SetId;

        foreach (string path in Directory.EnumerateFiles(folder, name + ".vol*.par2"))
        {
            string candidate = Path.GetFileName(path);
            if (string.Equals(path, created.VolumePath, StringComparison.OrdinalIgnoreCase)
                || !candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                || !VolumeSuffix().IsMatch(candidate[name.Length..]))
                continue;
            Par2ReadResult read = Par2PacketReader.Read([path], cancellationToken);
            bool stale = read.UnreadableFiles == 0
                && (read.Sets.Count > 0 || read.Rejected.Count > 0)
                && read.Sets.All(s => s.SetId != setId)
                && read.Rejected.All(r => r.SetId != setId);
            if (!stale)
                continue;
            ArchiveWarning? warning = null;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning = CoreMessages.Warning(path, CoreMessages.Wrap(MessageCode.RecoveryOldVolumeNotDeleted, ex));
            }
            if (warning is not null)
                yield return warning;
        }
    }

    [GeneratedRegex(@"^\.vol\d+[+-]\d+\.par2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeSuffix();
}
