using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F413: extraction, test and listing of a gzip of one file (<see cref="BareGzip"/>), for the
/// routers, which hand it the gzip paths <see cref="BareGzip.IsBareGzip"/> picks from the tar
/// route after Group Policy and tar.exe's capabilities have allowed the format. The one file is
/// named by <see cref="BareGzip.OutputName"/> and lands in the destination folder itself, as a
/// single-file archive does (T-F154). The rules every engine keeps: the shared conflict resolver
/// and name registry (an automatic keep is <see cref="MessageCode.FileExistsAtDestination"/>, or
/// <see cref="MessageCode.FileWrittenEarlierInThisRun"/> when this run wrote the path), the whole
/// stream read once to check it and measure it before anything is written (the compression-ratio
/// check), a temporary file committed only when complete, the download mark, the name filter,
/// one SourceResult per archive looked at, and no exception but a cancel.
/// </summary>
internal sealed class BareGzipEngine(GroupPolicyOptions policy)
{
    public async Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<ArchiveError>();
        var created = new List<string>();
        var skipped = new List<SkippedFile>();
        var sources = new List<SourceResult>();
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);
        ExtractionNameRegistry names = options.SharedNames ?? new ExtractionNameRegistry();

        bool destinationExisted = Directory.Exists(options.DestinationFolder);
        if (DestinationFolder.TryCreate(options.DestinationFolder) is { } folderFailure)
            return DestinationFolder.ExtractionRefused(options.ArchivePaths, folderFailure);
        try
        {
            int total = options.ArchivePaths.Count;
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string archivePath = options.ArchivePaths[i];
                names.CurrentArchive = archivePath;
                int errorsBefore = errors.Count, skippedBefore = skipped.Count;
                string? landed = await ExtractOneAsync(archivePath, options, conflictResolver, names, errors, skipped, cancellationToken).ConfigureAwait(false);
                if (landed is not null)
                {
                    created.Add(options.DestinationFolder);
                    options.OnItemExtracted?.Invoke(landed);
                }
                // T-F265: a subset (the App's selection, a name filter) never makes the archive deletable.
                sources.Add(SourceOutcomeRules.Classify(archivePath, produced: landed is not null,
                    clean: errors.Count == errorsBefore && skipped.Count == skippedBefore
                        && options.SelectedEntryPaths is null && options.EntryFilter is null));
                progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
            }
        }
        finally
        {
            // T-F309: a run that produced nothing does not leave the folder it created.
            if (!destinationExisted && created.Count == 0)
                ZipArchiveService.TryDeleteEmptyDirectory(options.DestinationFolder);
        }

        return new ArchiveResult
        {
            CreatedFiles = [.. created.Distinct(StringComparer.OrdinalIgnoreCase)],
            Errors = errors,
            SkippedFiles = skipped,
            UserSkippedEntryCount = conflictResolver.UserSkipCount,
            Sources = SourceOutcomeRules.DowngradeNotWhollyOnDisk(sources, names),
        };
    }

    // The path the one file landed at, or null when it was not written (the reason is in errors or skipped).
    private async Task<string?> ExtractOneAsync(
        string archivePath, ExtractOptions options, ConflictResolver conflictResolver, ExtractionNameRegistry names,
        List<ArchiveError> errors, List<SkippedFile> skipped, CancellationToken cancellationToken)
    {
        if (InputPaths.Problem(archivePath) is { } pathProblem)
        {
            errors.Add(CoreMessages.Error(archivePath, pathProblem));
            return null;
        }
        string name = BareGzip.OutputName(archivePath);
        if ((options.SelectedEntryPaths is { } selected && !selected.Contains(name, StringComparer.OrdinalIgnoreCase))
            || (options.EntryFilter is { } filter && !filter(name, false)))
        {
            return null;
        }
        if (ArchiveEntrySecurity.IsDeviceName(name))
        {
            skipped.Add(CoreMessages.Skip(name, CoreMessages.Text(MessageCode.EntryReservedName)));
            return null;
        }

        string? tempPath = null;
        try
        {
            BareGzip.ReadResult measured = await Task.Run(() => BareGzip.Read(archivePath, null, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (measured.Damaged)
            {
                errors.Add(CoreMessages.Error(archivePath, Damaged(MessageCode.CannotExtractArchive)));
                return null;
            }
            if (!await PassesRatioCheckAsync(archivePath, measured.Length, options, skipped).ConfigureAwait(false))
                return null;

            // The file lands in the destination itself, never beside or above it.
            string target = Path.GetFullPath(Path.Combine(options.DestinationFolder, name));
            if (!string.Equals(Path.GetDirectoryName(target), Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.DestinationFolder)), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(CoreMessages.Error(archivePath, CoreMessages.Text(MessageCode.UnsafeEntryPath, name)));
                return null;
            }
            if (await PlanAsync(archivePath, target, name, measured, conflictResolver, names, errors, skipped)
                .ConfigureAwait(false) is not { } plan)
            {
                return null;
            }

            tempPath = ArchiveTempFile.Create(plan.FinalPath);
            BareGzip.ReadResult written = await Task.Run(() => WriteTemp(archivePath, tempPath, measured.Length, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (written.Damaged || written.Length != measured.Length)
            {
                errors.Add(CoreMessages.Error(archivePath, Damaged(MessageCode.CannotExtractArchive)));
                return null;
            }
            if (written.ModifiedUtc is { } modified)
                FileTimes.TrySetFile(tempPath, modified);
            ArchiveEntrySecurity.TryPropagateMotw(archivePath, tempPath, policy.EffectiveMotwMode(options.ApplyDownloadMark));

            if (plan.RenameExistingTo is { } renamed)
                MoveAside(plan.FinalPath, renamed);
            string landed = await ArchiveTempFile.CommitAsync(tempPath, plan.FinalPath, plan.Replace, cancellationToken).ConfigureAwait(false);
            tempPath = null;
            return landed;
        }
        catch (IOException ex)
        {
            errors.Add(CoreMessages.Error(archivePath, CoreMessages.Wrap(MessageCode.CannotExtractArchive, ex), ex));
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(CoreMessages.Error(archivePath, CoreMessages.Text(MessageCode.AccessDeniedExtractingArchive, CoreMessages.Detail(ex)), ex));
            return null;
        }
        finally
        {
            if (tempPath is not null)
                TryDelete(tempPath);
        }
    }

    // The second read may not give more than the first measured: a file swapped in between stops.
    private static BareGzip.ReadResult WriteTemp(string archivePath, string tempPath, long measuredLength, CancellationToken cancellationToken)
    {
        using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return BareGzip.Read(archivePath, output, cancellationToken, measuredLength);
    }

    // T-F94: the whole stream's real size against the file's, as for a tar.
    private static async Task<bool> PassesRatioCheckAsync(string archivePath, long length, ExtractOptions options, List<SkippedFile> skipped)
    {
        long compressed = new FileInfo(archivePath).Length;
        long free = ArchiveEntrySecurity.GetAvailableFreeSpace(options.DestinationFolder);
        CompressionBombOutcome outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            archivePath, length, compressed, free, options.ConfirmCompressionBombExtraction).ConfigureAwait(false);
        if (outcome == CompressionBombOutcome.InsufficientDiskSpace)
        {
            skipped.Add(CoreMessages.Skip(archivePath, MessageCode.InsufficientDiskSpace,
                length.ToString("N0", CultureInfo.CurrentCulture), free.ToString("N0", CultureInfo.CurrentCulture)));
            return false;
        }
        if (outcome == CompressionBombOutcome.UserDeclined)
        {
            long ratio = compressed > 0 ? length / compressed : 0;
            skipped.Add(CoreMessages.Skip(archivePath, MessageCode.TarBombDeclined,
                ratio.ToString(CultureInfo.CurrentCulture), length.ToString("N0", CultureInfo.CurrentCulture)));
            return false;
        }
        return true;
    }

    // Where the file goes and what happens to what is there: the conflict decision, as in the other engines.
    private sealed record Plan(string FinalPath, bool Replace, string? RenameExistingTo);

    private static async Task<Plan?> PlanAsync(
        string archivePath, string finalPath, string name, BareGzip.ReadResult measured, ConflictResolver conflictResolver, ExtractionNameRegistry names,
        List<ArchiveError> errors, List<SkippedFile> skipped)
    {
        bool inRun = names.Claimed.Contains(finalPath);
        bool fileThere = File.Exists(finalPath);
        bool folderThere = Directory.Exists(finalPath);
        if (!fileThere && !inRun && !(folderThere && conflictResolver.RenamesExisting))
        {
            if (folderThere)
            {
                errors.Add(CoreMessages.Error(archivePath, MessageCode.DestinationIsAFolder, name));
                return null;
            }
            names.Claim(finalPath, namedDifferently: false);
            return new Plan(finalPath, Replace: false, RenameExistingTo: null);
        }

        int userSkipsSoFar = conflictResolver.UserSkipCount;
        DateTimeOffset? modified = measured.ModifiedUtc is { } utc ? new DateTimeOffset(utc).ToLocalTime() : null;
        ConflictBehavior decision = await conflictResolver.ResolveAsync(finalPath, measured.Length, modified).ConfigureAwait(false);
        switch (decision)
        {
            case ConflictBehavior.Skip:
                // T-F216: the user's own Skip answer is not listed; T-F415: this run's path is not one that was there.
                if (conflictResolver.UserSkipCount == userSkipsSoFar)
                    skipped.Add(CoreMessages.Skip(name, inRun ? MessageCode.FileWrittenEarlierInThisRun : MessageCode.FileExistsAtDestination));
                return null;
            case ConflictBehavior.Rename:
                string unique = ArchiveNaming.GetUniqueFilePath(finalPath, names.Claimed);
                names.Claim(unique, namedDifferently: false);
                return new Plan(unique, Replace: false, RenameExistingTo: null);
            case ConflictBehavior.RenameExisting:
                string aside = ArchiveNaming.GetUniqueFilePath(finalPath, names.Claimed);
                names.Claim(aside, namedDifferently: false);
                names.Claim(finalPath, namedDifferently: false, earlierRenamedAway: true);
                return new Plan(finalPath, Replace: false, RenameExistingTo: aside);
            default:
                if (folderThere)
                {
                    errors.Add(CoreMessages.Error(archivePath, MessageCode.DestinationIsAFolder, name));
                    return null;
                }
                names.Claim(finalPath, namedDifferently: false);
                return new Plan(finalPath, Replace: true, RenameExistingTo: null);
        }
    }

    // -aot: what is there - a file or a folder - keeps its content under the new name (T-F411, T-F419).
    private static void MoveAside(string path, string newPath)
    {
        if (Directory.Exists(path))
            Directory.Move(path, newPath);
        else if (File.Exists(path))
            File.Move(path, newPath);
    }

    public static async Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        var errors = new List<ArchiveError>();
        var sources = new List<SourceResult>();
        foreach (string archivePath in archivePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (InputPaths.Problem(archivePath) is { } pathProblem)
            {
                errors.Add(CoreMessages.Error(archivePath, pathProblem));
                continue;
            }
            // T-F410: nothing passed the filter, so nothing is read.
            if (entryFilter is not null && !entryFilter(BareGzip.OutputName(archivePath), false))
                continue;
            try
            {
                BareGzip.ReadResult read = await Task.Run(() => BareGzip.Read(archivePath, null, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (read.Damaged)
                    errors.Add(CoreMessages.Error(archivePath, Damaged(MessageCode.CannotReadArchive)));
                sources.Add(SourceOutcomeRules.Classify(archivePath, produced: true, clean: !read.Damaged));
            }
            catch (IOException ex)
            {
                errors.Add(CoreMessages.Error(archivePath, CoreMessages.Wrap(MessageCode.CannotReadArchive, ex), ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(CoreMessages.Error(archivePath, CoreMessages.Text(MessageCode.CannotReadArchive, CoreMessages.Detail(ex)), ex));
            }
        }
        return new ArchiveResult { Errors = errors, Sources = sources };
    }

    // The size is the whole stream decompressed: gzip's own size field holds only the last
    // member's, modulo 4 GiB.
    public static async Task<ArchiveListResult> ListAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (InputPaths.Problem(archivePath) is { } pathProblem)
            return CoreMessages.ListFailure(pathProblem);
        try
        {
            BareGzip.ReadResult read = await Task.Run(() => BareGzip.Read(archivePath, null, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (read.Damaged)
                return CoreMessages.ListFailure(Damaged(MessageCode.CannotReadArchive));
            return new ArchiveListResult
            {
                Success = true,
                Entries =
                [
                    new ArchiveEntryInfo
                    {
                        Path = BareGzip.OutputName(archivePath),
                        Size = read.Length,
                        CompressedSize = new FileInfo(archivePath).Length,
                        Modified = read.ModifiedUtc?.ToLocalTime(),
                    },
                ],
            };
        }
        catch (IOException ex)
        {
            return CoreMessages.ListFailure(CoreMessages.Wrap(MessageCode.CannotReadArchive, ex));
        }
        catch (UnauthorizedAccessException ex)
        {
            return CoreMessages.ListFailure(CoreMessages.Text(MessageCode.CannotReadArchive, CoreMessages.Detail(ex)));
        }
    }

    private static CoreText Damaged(MessageCode wrapper) =>
        CoreMessages.Text(wrapper, CoreMessages.Text(MessageCode.ContentDamaged));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort: the next run's sweep removes it */ }
    }
}
