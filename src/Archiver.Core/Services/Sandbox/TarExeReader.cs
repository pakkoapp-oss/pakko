using System.Globalization;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Text;
using static Archiver.Core.Services.TarSandboxedService;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F470: the tar.exe reader - every step that knows how tar.exe lists, names and writes entries
/// (its "-t"/"-tv" listings, "-T"/"-X" member lists, the names it writes on disk, best-fit judging,
/// duplicate first copies). Callers see only <see cref="ISandboxedArchiveReader"/>; one session
/// reads one archive for one operation, in the order Scan, Select, Extract.
/// </summary>
internal sealed class TarExeReader : ISandboxedArchiveReader
{
    private const string ScanFirst = "ScanAsync comes first.";
    private readonly TarSandboxScope _scope;
    private long _totalDeclaredSize;
    private string[]? _allNames;
    private Dictionary<string, long>? _sizeByName;
    private List<(string Name, long Size)>? _fileEntries;
    private Func<string, bool, bool>? _filter;
    private List<string>? _tarMembers;
    private List<string>? _expandedSelection;
    private HashSet<string>? _droppedFiles;
    private List<DuplicateGroup>? _scanDuplicates;

    private TarExeReader(TarSandboxScope scope) => _scope = scope;

    internal static async Task<ISandboxedArchiveReader> OpenAsync(
        string archivePath, bool needsOutputDir, Encoding? outputEncoding, CancellationToken cancellationToken) =>
        new TarExeReader(await TarSandboxScope.CreateAsync(archivePath, needsOutputDir, cancellationToken, outputEncoding)
            .ConfigureAwait(false));

    /// <summary>T-F470: a reader on a scope opened for the archive helper, for a format the helper does not read.</summary>
    internal static ISandboxedArchiveReader On(TarSandboxScope scope) => new TarExeReader(scope);

    public void Dispose() => _scope.Dispose();

    public async Task<ArchiveListResult> ListAsync(CancellationToken cancellationToken)
    {
        (string[]? names, ArchiveListResult? nameError) = await RunListingCommandAsync(_scope, verbose: false, cancellationToken).ConfigureAwait(false);
        if (nameError is not null)
            return nameError;

        (string[]? typeLines, ArchiveListResult? typeError) = await RunListingCommandAsync(_scope, verbose: true, cancellationToken).ConfigureAwait(false);
        if (typeError is not null)
            return typeError;

        if (typeLines.Length != names.Length)
            return CoreMessages.ListFailure(CoreMessages.Text(MessageCode.ListingInconsistent));

        return new ArchiveListResult { Success = true, Entries = BuildEntryList(names, typeLines) };
    }

    public async Task<ReaderCatalog> ScanAsync(CancellationToken cancellationToken)
    {
        (_totalDeclaredSize, _allNames, _sizeByName, _fileEntries) = await ScanForUnsafeEntriesAsync(_scope, cancellationToken)
            .ConfigureAwait(false);
        // T-F196: `tar -C dir .` (the most common way to make a tar) prefixes every member with
        // "./". tar.exe itself resolves that away on disk (out\a.txt, not out\.\a.txt), so the
        // root-shape decision must see the same names. T-F197: folder entries count as roots too;
        // a bare "./" member names the archive's own top level, not a root.
        return new ReaderCatalog(
            _totalDeclaredSize,
            [.. _allNames.Select(EntryDiskName.StripLeadingDotSlash).Where(n => n.Length > 0)],
            [.. _fileEntries.Select(e => e.Name)]);
    }

    public async Task<SelectionOutcome> SelectForExtractAsync(
        IReadOnlyList<string>? selectedEntryPaths, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        string[] allNames = _allNames ?? throw new InvalidOperationException(ScanFirst);
        _filter = entryFilter;
        List<string>? tarMembers = selectedEntryPaths is { Count: > 0 } ? ExpandSelection(allNames, selectedEntryPaths) : null;
        // A selection that names no entry selects nothing, as in ZIP: an empty member list is
        // tar.exe's "every entry" (a name it lists as "????.txt" on 1252 never matches the real one).
        if (tarMembers is { Count: 0 })
            return new SelectionOutcome(SelectionKind.EmptySelection, 0, 0);
        // T-F441: from here names decide - a selection's members, a filter's verdicts - so the
        // listing must name the entries they decide about. A filter alone extracts every entry, and
        // the files on disk still settle a Suspect after the extraction (below).
        if (tarMembers is not null || entryFilter is not null)
        {
            TarListingVerdict verdict = TarNames.Judge(
                allNames, await _scope.ReadHeaderNamesAsync(cancellationToken).ConfigureAwait(false), tarMembers);
            TarNames.EnsureNamesCanDecide(verdict, allowSuspect: tarMembers is null);
        }
        List<string>? expandedSelection = tarMembers;
        // T-F410: each name on its own, as the listing shows it (no "./"), a folder without its
        // content. None passes: 7-Zip's "No files to process", which creates no folder either.
        // tar.exe still extracts tarMembers (or everything); the move phase below leaves out what
        // did not pass. Listing the names for tar.exe instead cost N^2 pattern matches in
        // libarchive (measured 2026-10-17: 40,000 names 148 s against 24 s for all of them).
        // Only the files left out are dropped: a name tar.exe writes differently on disk (an entry
        // named with a backslash becomes a folder and a file) is never lost to a lookup that misses.
        HashSet<string>? droppedFiles = null;
        if (entryFilter is not null)
        {
            List<string> candidates = [.. tarMembers ?? (IEnumerable<string>)allNames];
            expandedSelection = [.. candidates.Where(name =>
                EntryDiskName.StripLeadingDotSlash(name) is { Length: > 0 } shown && entryFilter(shown, name.EndsWith('/')))];
            if (expandedSelection.Count == 0)
                return new SelectionOutcome(SelectionKind.NothingPassesFilter, 0, 0);
            var kept = new HashSet<string>(expandedSelection, StringComparer.Ordinal);
            // T-F420: by the name tar.exe wrote it ("a?b" as "a_b"); one a kept entry shares stays.
            var keptOnDisk = new HashSet<string>(kept.Select(EntryDiskName.AsTarExeWrites), StringComparer.OrdinalIgnoreCase);
            droppedFiles = new HashSet<string>(
                candidates.Where(n => !n.EndsWith('/') && !kept.Contains(n)).Select(EntryDiskName.AsTarExeWrites).Where(n => !keptOnDisk.Contains(n)),
                StringComparer.OrdinalIgnoreCase);
        }
        _tarMembers = tarMembers;
        _expandedSelection = expandedSelection;
        _droppedFiles = droppedFiles;
        long declaredBytes = tarMembers != null
            ? tarMembers.Sum(n => _sizeByName!.GetValueOrDefault(n, 0L))
            : _totalDeclaredSize;
        int fileCount = expandedSelection != null
            ? expandedSelection.Count(n => !n.EndsWith('/'))
            : allNames.Count(n => !n.EndsWith('/'));
        return new SelectionOutcome(SelectionKind.Entries, declaredBytes, fileCount);
    }

    public async Task<IQuarantinedEntries> ExtractAsync(
        ExtractionPoll? poll, List<SkippedFile> skippedFiles, CancellationToken cancellationToken)
    {
        string[] allNames = _allNames ?? throw new InvalidOperationException(ScanFirst);
        List<(string Name, long Size)> fileEntries = _fileEntries!;
        string outDir = _scope.OutputDirectory!;

        // T-F52: pre-create every directory the archive implies, at Pakko's own (unsandboxed)
        // identity, before tar.exe ever runs inside the AppContainer. Found empirically: when a
        // nested file entry (e.g. "sub/b.txt") has no preceding explicit "sub/" directory entry,
        // libarchive's own implicit parent-directory creation fails under the AppContainer even
        // though "out\" itself is correctly ACL'd with inheritable Modify — an explicit "sub/"
        // entry extracts fine, so this is specific to libarchive's own implicit-mkdir path, not a
        // general ACL problem (isolated via a throwaway diagnostic against the real tar.exe; see
        // DECISIONS.md's T-F52 entry). Pre-creating here sidesteps it entirely: Directory.
        // CreateDirectory, run by Pakko's own trusted process, correctly inherits "out\"'s ACEs
        // for every directory it creates, so tar.exe's own directory ever needs to create one.
        // T-F298: only the directories tar.exe would have to create implicitly — tar.exe does not
        // set the time of a directory that already exists, so pre-creating one with its own entry
        // lost that entry's time.
        // With a selection, only the selected names are extracted: their folders' entries may not be.
        IEnumerable<string> extractedNames = (IEnumerable<string>?)_tarMembers ?? allNames;
        HashSet<string> preCreated = PreCreateImplicitFolders(extractedNames, outDir);

        Task<(int ExitCode, string StdOut, string StdErr)> extractionTask = _scope.ExtractAsync(_tarMembers, cancellationToken);
        if (poll is { } p)
        {
            await PollExtractionProgressAsync(outDir, p.TotalBytes, p.Progress, extractionTask, cancellationToken, p.ExtractionEnd)
                .ConfigureAwait(false);
        }

        (int exitCode, _, string? stdErr) = await extractionTask.ConfigureAwait(false);

        if (exitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.TarExtractionFailed, DescribeFailure(stdErr)));

        List<DuplicateGroup> duplicateGroups = FindDuplicateGroups(fileEntries, allNames, _expandedSelection);
        List<string>? foldersFromDisk = JudgeWhatTarExeWrote(allNames, outDir, preCreated, ref duplicateGroups);
        (string? firstCopyDir, HashSet<string> firstCopies, int copiesReported) = await ExtractFirstCopiesAsync(
            _scope, duplicateGroups, skippedFiles, cancellationToken)
            .ConfigureAwait(false);

        // T-F462: the main pass's files, and the first copies that may count beside them.
        string[] mainFiles = FilesBelow(outDir);
        int filesFound = mainFiles.Length + (firstCopyDir is null ? 0
            : FirstCopiesAtTheirPath(duplicateGroups, firstCopies.Select(f => QuarantineFiles.Relative(firstCopyDir, f).Replace('\\', '/')), mainFiles));
        int expectedMoves = (_expandedSelection != null
            ? _expandedSelection.Count(n => !n.EndsWith('/'))
            : allNames.Count(n => !n.EndsWith('/'))) + firstCopies.Count;
        return new TarQuarantine(this, outDir, foldersFromDisk, (firstCopyDir, firstCopies),
            new TarQuarantineCounts(filesFound + copiesReported, FileEntriesInScope(fileEntries, _expandedSelection), expectedMoves));
    }

    private List<string>? JudgeWhatTarExeWrote(
        string[] allNames, string outDir, HashSet<string> preCreated, ref List<DuplicateGroup> duplicateGroups)
    {
        // T-F441: after a run without a member list the quarantine holds every entry under its true
        // name, so a file the listing does not account for was listed under another name. A filter's
        // verdicts were then made on the wrong names (refused); a duplicate group was two different
        // names (dropped: each file meets the conflict rule under its own name).
        // T-F443: folders too, on every such run; a folder entry then comes from the quarantine.
        if (_tarMembers is not null)
            return null;
        List<(string Path, bool IsFolder)> quarantinedItems = [.. QuarantineFiles.Walk(outDir)
            .Select(item => (QuarantineFiles.Relative(outDir, item.Path).Replace('\\', '/'), item.IsFolder))];
        string[] quarantinedFiles = [.. quarantinedItems.Where(i => !i.IsFolder).Select(i => i.Path)];
        TarListingVerdict filesOnDisk = TarNames.JudgeExtracted(allNames, quarantinedFiles, []);
        TarListingVerdict onDisk = filesOnDisk.Trust != TarNameTrust.Faithful ? filesOnDisk : TarNames.JudgeExtracted(allNames, quarantinedFiles,
            quarantinedItems.Where(i => i.IsFolder && !preCreated.Contains(i.Path)).Select(i => i.Path));
        if (onDisk.Trust != TarNameTrust.Faithful)
        {
            if (_filter is not null)
                TarNames.EnsureNamesCanDecide(onDisk);
            // Two files of one name are told apart only by the files: a folder named by best fit
            // says nothing about them (the fresh review).
            if (filesOnDisk.Trust != TarNameTrust.Faithful)
                duplicateGroups = [];
            return FoldersTarExeWrote(quarantinedItems, preCreated);
        }
        return null;
    }

    public async Task<ScanQuarantine> ExtractForScanAsync(IReadOnlyList<string>? selectedEntryPaths, CancellationToken cancellationToken)
    {
        string[] allNames = _allNames ?? throw new InvalidOperationException(ScanFirst);
        List<(string Name, long Size)> fileEntries = _fileEntries!;
        string outDir = _scope.OutputDirectory!;

        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        List<string>? expandedSelection = isSelectedSubset
            ? ExpandSelection(allNames, selectedEntryPaths!)
            : null;
        // T-F441: a selection names entries by their listed names; they must be the entries' own.
        if (expandedSelection is { Count: > 0 })
        {
            TarNames.EnsureNamesCanDecide(TarNames.Judge(
                allNames, await _scope.ReadHeaderNamesAsync(cancellationToken).ConfigureAwait(false), expandedSelection));
        }

        int totalEntries = expandedSelection != null
            ? expandedSelection.Count(n => !n.EndsWith('/'))
            : allNames.Count(n => !n.EndsWith('/'));

        // T-F52, as extraction does it (T-F460: the same names, through the long-path prefix).
        PreCreateImplicitFolders((IEnumerable<string>?)expandedSelection ?? allNames, outDir);

        (int exitCode, _, string stdErr) = await _scope.ExtractAsync(expandedSelection, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
            return new ScanQuarantine(DescribeFailure(stdErr), [], totalEntries, 0, false);

        // T-F171: extraction also takes out the first of several same-named copies, so it is
        // scanned too. Everything the first-copy pass wrote is scanned, not only the files
        // extraction would keep.
        _expandedSelection = expandedSelection;
        _scanDuplicates = FindDuplicateGroups(fileEntries, allNames, expandedSelection);
        return new ScanQuarantine(null, ScanFilesBelow(outDir), totalEntries + _scanDuplicates.Count,
            FileEntriesInScope(fileEntries, expandedSelection), _scanDuplicates.Count > 0);
    }

    public async Task<FirstCopiesForScan> ExtractFirstCopiesForScanAsync(CancellationToken cancellationToken)
    {
        string[] allNames = _allNames ?? throw new InvalidOperationException(ScanFirst);
        List<DuplicateGroup> duplicates = _scanDuplicates ?? throw new InvalidOperationException("ExtractForScanAsync comes first.");
        string outDir = _scope.OutputDirectory!;

        (string firstDir, int firstExitCode, string firstStdErr) = await _scope.ExtractFirstOccurrencesAsync(
            duplicates.Select(g => g.FirstName).ToList(), cancellationToken).ConfigureAwait(false);
        // T-F460 (fresh review): a first copy tar.exe did not write was never scanned.
        if (firstExitCode != 0)
            return new FirstCopiesForScan([], DescribeFailure(firstStdErr), () => 0);
        // T-F462: a first copy counts only when the listing names the main pass's files truly, and
        // only at its group's own path (FirstCopiesAtTheirPath). A best-fit pair lists as one name
        // twice, and its "first copy" is a file already scanned, which must not stand in for a file
        // lost elsewhere; it is still scanned. Counted once the first copies are scanned.
        return new FirstCopiesForScan(ScanFilesBelow(firstDir), null, () =>
        {
            string[] mainFiles = FilesBelow(outDir);
            bool faithful = TarNames.JudgeExtracted((IReadOnlyList<string>?)_expandedSelection ?? allNames, mainFiles, []).Trust == TarNameTrust.Faithful;
            return faithful ? FirstCopiesAtTheirPath(duplicates, FilesBelow(firstDir), mainFiles) : 0;
        });
    }

    // A pass's files for the scan, each under its name below the pass's folder, '/' between parts.
    private static ScanFile[] ScanFilesBelow(string directory) =>
        [.. QuarantineFiles.EnumerateFiles(directory).Select(f => new ScanFile(f, QuarantineFiles.Relative(directory, f).Replace('\\', '/')))];

    // A quarantine folder's files, relative to it, '/' between parts.
    private static string[] FilesBelow(string directory) =>
        [.. QuarantineFiles.EnumerateFiles(directory).Select(f => QuarantineFiles.Relative(directory, f).Replace('\\', '/'))];

    public async Task<TestOutcome> TestAsync(
        IProgress<ProgressReport>? progress, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        (int exitCode, string stdErr, long declaredSize, bool nothingRead) = await ScanAndTestAsync(_scope, progress, entryFilter, cancellationToken)
            .ConfigureAwait(false);
        if (exitCode == 0)
            return new TestOutcome(null, declaredSize, nothingRead);
        CoreText failure = IsLikelyEncryptionFailure(stdErr)
            ? CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported)
            : CoreMessages.Text(MessageCode.CannotReadArchive, DescribeFailure(stdErr));
        return new TestOutcome(failure, declaredSize, nothingRead);
    }

    // T-F462: the files a run accounted for, the files it must, and the most the move phase handles.
    private readonly record struct TarQuarantineCounts(int AccountedFor, int Expected, int Moves);

    // What one tar.exe run left in the quarantine, by tar.exe's names: Files walks the first copies,
    // then the main pass (T-F171), leaving out what the filter dropped (T-F410).
    private sealed class TarQuarantine(
        TarExeReader reader, string outDir, List<string>? foldersFromDisk,
        (string? Directory, HashSet<string> Files) firstCopies, TarQuarantineCounts counts) : IQuarantinedEntries
    {
        private readonly HashSet<string> _renamed = QuarantinedAs(reader._fileEntries!, EntryDiskName.Renames);
        private readonly HashSet<string> _trimmed = QuarantinedAs(reader._fileEntries!, EntryDiskName.Trims);
        private readonly IEnumerable<string> _folderNames = (IEnumerable<string>?)reader._expandedSelection ?? reader._allNames!;

        public int ExpectedMoves => counts.Moves;

        public IEnumerable<QuarantinedFile> Files()
        {
            // T-F171: first copies move first, so the first copy takes the name and the last one meets
            // the conflict rule, as a duplicate ZIP entry does (T-F30).
            IEnumerable<(string File, string Root)> quarantined = firstCopies.Directory is not { } firstCopyDir
                ? []
                : QuarantineFiles.EnumerateFiles(firstCopyDir).Where(firstCopies.Files.Contains).Select(f => (f, firstCopyDir));
            quarantined = quarantined.Concat(QuarantineFiles.EnumerateFiles(outDir).Select(f => (f, outDir)));
            foreach ((string file, string root) in quarantined)
            {
                string relative = QuarantineFiles.Relative(root, file);
                string named = relative.Replace(Path.DirectorySeparatorChar, '/');
                if (reader._droppedFiles is not null && reader._droppedFiles.Contains(named))
                    continue;
                yield return new QuarantinedFile(file, relative, _renamed.Contains(named), _trimmed.Contains(named));
            }
        }

        public void EnsureNoneMissing()
        {
            // T-F462: nothing is committed unless every file entry reached the quarantine or was reported
            // (a copy past the first and the last); a file lost on the way fails the archive.
            if (counts.AccountedFor < counts.Expected)
                throw new CoreTextIOException(CoreMessages.Text(MessageCode.TarFilesMissing,
                    counts.AccountedFor.ToString(CultureInfo.CurrentCulture), counts.Expected.ToString(CultureInfo.CurrentCulture)));
        }

        public void RecordFolderTimesInto(FolderPlacement placement)
        {
            if (foldersFromDisk is null)
                RecordFolderTimes(_folderNames, placement.StripRootPrefix, outDir, placement.Staging);
            else
                RecordFolderTimesFromDisk(foldersFromDisk, placement, outDir);
        }

        public void CreateFolderEntriesIn(FolderPlacement placement)
        {
            if (foldersFromDisk is null)
                CreateFolderEntries(_folderNames, placement.StripRootPrefix, placement.Staging.Path);
            else
                CreateFolderEntriesFromDisk(foldersFromDisk, placement);
        }

        private static HashSet<string> QuarantinedAs(IReadOnlyList<(string Name, long Size)> fileEntries, Func<string, bool> which) =>
            new(fileEntries.Where(e => which(e.Name)).Select(e => EntryDiskName.AsTarExeWrites(EntryDiskName.StripLeadingDotSlash(e.Name))),
                StringComparer.OrdinalIgnoreCase);

        // T-F197: the move phase walks files only, so folder entries are created here — from the
        // names the pre-scan already validated (no "..", rooted, ADS or reserved names), with the
        // same root strip the files get. Moving files first means a folder that also holds files
        // already exists; only the empty ones are new.
        private static void CreateFolderEntries(IEnumerable<string> names, bool stripRootPrefix, string actualDest)
        {
            foreach (string name in names)
            {
                if (!name.EndsWith('/'))
                    continue;
                string relative = EntryDiskName.AsPakkoWrites(name).Replace('/', Path.DirectorySeparatorChar);
                if (stripRootPrefix)
                {
                    int sep = relative.IndexOf(Path.DirectorySeparatorChar);
                    relative = sep < 0 ? string.Empty : relative[(sep + 1)..];
                }
                if (relative.Length > 0)
                    Directory.CreateDirectory(Path.Combine(actualDest, relative));
            }
        }

        // T-F298: a folder entry takes the time tar.exe gave it in the quarantine — read before the
        // move phase, since moving a folder's files out changes that time. Same names and root strip
        // as CreateFolderEntries; "" is the stripped root, which actualDest stands in for.
        private static void RecordFolderTimes(IEnumerable<string> names, bool stripRootPrefix, string quarantineDir, ExtractionStaging staging)
        {
            foreach (string name in names)
            {
                if (!name.EndsWith('/'))
                    continue;
                string entryPath = EntryDiskName.AsTarExeWrites(name).Replace('/', Path.DirectorySeparatorChar);
                string quarantined = Path.Combine(quarantineDir, entryPath);
                if (entryPath.Length == 0 || !QuarantineFiles.FolderExists(quarantined))
                    continue;
                string relative = EntryDiskName.AsPakkoWrites(name).Replace('/', Path.DirectorySeparatorChar);
                if (stripRootPrefix)
                {
                    int sep = relative.IndexOf(Path.DirectorySeparatorChar);
                    relative = sep < 0 ? string.Empty : relative[(sep + 1)..];
                }
                try
                {
                    staging.RecordFolderTime(relative, QuarantineFiles.FolderTime(quarantined));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // best-effort: the folder keeps the time the commit gives it
                }
            }
        }

        // T-F443: where a folder from the quarantine lands, relative to actualDest - where a file of the
        // same path would (TryMoveSingleEntryAsync); "" is the stripped root, which actualDest stands in for.
        private static string FolderRelativeToDest(string quarantined, FolderPlacement plan)
        {
            string relative = quarantined.Replace('/', Path.DirectorySeparatorChar);
            if (plan.StripRootPrefix)
            {
                int sep = relative.IndexOf(Path.DirectorySeparatorChar);
                if (sep < 0)
                    return string.Empty;
                relative = relative[(sep + 1)..];
            }
            relative = EntryDiskName.AsPakkoWrites(relative.Replace(Path.DirectorySeparatorChar, '/') + "/").Replace('/', Path.DirectorySeparatorChar);
            return Path.GetRelativePath(plan.ActualDest, Path.GetFullPath(Path.Combine(plan.ActualDest, relative)));
        }

        private static void CreateFolderEntriesFromDisk(IEnumerable<string> folders, FolderPlacement plan)
        {
            foreach (string folder in folders)
            {
                string relative = FolderRelativeToDest(folder, plan);
                if (relative.Length > 0 && relative != ".")
                    Directory.CreateDirectory(Path.Combine(plan.Staging.Path, relative));
            }
        }

        private static void RecordFolderTimesFromDisk(IEnumerable<string> folders, FolderPlacement plan, string quarantineDir)
        {
            foreach (string folder in folders)
            {
                string relative = FolderRelativeToDest(folder, plan);
                try
                {
                    string quarantined = Path.Combine(quarantineDir, folder.Replace('/', Path.DirectorySeparatorChar));
                    plan.Staging.RecordFolderTime(relative == "." ? string.Empty : relative, QuarantineFiles.FolderTime(quarantined));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // best-effort: the folder keeps the time the commit gives it
                }
            }
        }
    }

    // Rejects the whole archive (throws TarArchiveRejectedException) if any entry name is
    // unsafe, or if any entry is a symlink/hardlink/device/fifo/socket. Two tar.exe invocations,
    // both run through the sandboxed scope: "-tf" lists plain entry names (one per line, no
    // locale-dependent formatting — used for the name checks) and "-tvf" lists the same entries
    // with a leading ls-style type character ('-' regular, 'd' directory, 'l' symlink, 'h'
    // hardlink, etc.) that is rendered deterministically by libarchive regardless of locale —
    // unlike the rest of that line (its date column was observed locale-mangled on a
    // Cyrillic-locale machine, same bug class as T-F84) — so only character 0 of each "-tvf" line
    // is read.
    // Returns the sum of declared uncompressed sizes for every regular-file entry (T-F94) — the
    // ratio-threshold decision itself lives in ExtractSingleArchiveAsync via the shared
    // ArchiveEntrySecurity.EvaluateCompressionBombAsync evaluator, but the size sum is still
    // accumulated here, in the same single "-tvf" pass that already reads the type column, to
    // avoid a second tar.exe invocation just to re-derive it (matches T-F90's original rationale
    // for extending this one pass in the first place).
    // T-F146: internal (was private) so AntivirusScanService can reuse the exact same T-F49
    // pre-scan for its own tar-family quarantine-extraction flow, without a second implementation
    // that could silently drift from what real extraction actually rejects.
    internal static async Task<(long TotalDeclaredSize, string[] Names, Dictionary<string, long> SizeByName, List<(string Name, long Size)> FileEntries)> ScanForUnsafeEntriesAsync(
        TarSandboxScope scope, CancellationToken cancellationToken)
    {
        (int nameExitCode, string? nameStdOut, string? nameStdErr) = await scope.ListAsync(verbose: false, cancellationToken).ConfigureAwait(false);
        if (nameExitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, DescribeFailure(nameStdErr)));

        // T-F418: the real names, so a control character is seen (T-F39) and a "-T" line names it.
        string[] names = TarNames.ReadListing(nameStdOut, scope.OutputEncoding)
            ?? throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));

        // T-F420 review: an entry named as another's 8.3 alias - tar.exe would write it through that
        // short name inside the quarantine, over the other, before Pakko sees either file.
        string? unsafeName = names.FirstOrDefault(IsDangerousEntryName) ?? EntryDiskName.ShortNameAliasOfAnother(names);
        if (unsafeName != null)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarUnsafeEntryPath, unsafeName));

        (int typeExitCode, string? typeStdOut, string? typeStdErr) = await scope.ListAsync(verbose: true, cancellationToken).ConfigureAwait(false);
        if (typeExitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, DescribeFailure(typeStdErr)));

        string[] typeLines = SplitLines(typeStdOut);
        if (typeLines.Length != names.Length)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));

        // T-F90: column 4 (size) is accumulated alongside the existing column-0 (type) check in
        // the same pass — see DECISIONS.md's T-F90 entry for why the size column, unlike the
        // date column, is safe to parse regardless of locale.
        long totalDeclaredSize = 0;
        // T-F142: per-entry sizes, keyed by the same raw name form as `names` — needed so a
        // selected-subset extraction (Archive Browser -> Extract Selected) can compute its own
        // subset byte total for progress reporting, distinct from totalDeclaredSize (deliberately
        // whole-archive, for the T-F94 compression-bomb check — see that check's own comment).
        // Retained here rather than re-parsed with a second "-tvf" pass, since this loop already
        // reads every line's size column once.
        var sizeByName = new Dictionary<string, long>(names.Length, StringComparer.Ordinal);
        // T-F171: every file entry in archive order — sizeByName keeps one size per name, but
        // same-named entries each need their own.
        var fileEntries = new List<(string Name, long Size)>();

        for (int i = 0; i < typeLines.Length; i++)
        {
            string line = typeLines[i];
            char typeChar = line.Length > 0 ? line[0] : '?';
            if (typeChar != '-' && typeChar != 'd')
                throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarSpecialEntry));

            // T-F455: a type-5 header stored without its '/' ("emptydir") lists without it; every
            // consumer tells a folder by the '/', and tar.exe matches "emptydir/" to that header.
            if (typeChar == 'd' && !names[i].EndsWith('/'))
                names[i] += "/";

            if (typeChar == '-')
            {
                long size = ParseTarListingSize(line);
                totalDeclaredSize += size;
                sizeByName[names[i]] = size;
                fileEntries.Add((names[i], size));
            }
        }

        // T-F05: the raw names (with tar's own trailing '/' on directory entries preserved) are
        // returned so ExtractSingleArchiveAsync's selected-subset extraction can build a "-xf"
        // member argument list without a second "-tf" invocation, and so the exact path form
        // tar.exe itself uses is what's ever passed back to it (see DECISIONS.md's T-F05 spike
        // entry — an unmatched/mismatched member name makes the whole "-xf" call fail non-zero).
        return (totalDeclaredSize, names, sizeByName, fileEntries);
    }

    // T-F05: expands a UI-selected set of archive-internal paths (ArchiveEntryInfo.Path's
    // convention — no trailing slash, even for folders) into the exact literal member names
    // tar.exe's "-tf" reported, for a "-xf archive member..." selective-extraction call. A
    // selected folder path is expanded to every one of its descendants explicitly, rather than
    // relying on tar.exe auto-recursing a bare directory-member argument — confirmed empirically
    // (DECISIONS.md's T-F05 entry) that tar.exe does auto-recurse, but this method doesn't depend
    // on that behavior continuing to hold.
    // T-F146: internal (was private) — AntivirusScanService's own selected-subset scan reuses this
    // exact expansion rather than re-deriving it.
    internal static List<string> ExpandSelection(string[] allNames, IReadOnlyList<string> selectedEntryPaths)
    {
        var allNamesSet = new HashSet<string>(allNames, StringComparer.Ordinal);
        var result = new List<string>();

        foreach (string requested in selectedEntryPaths)
        {
            string selected = ToArchiveMemberName(allNamesSet, requested);
            if (allNamesSet.Contains(selected))
                result.Add(selected);
            else if (allNamesSet.Contains(selected + "/"))
                result.Add(selected + "/");

            string descendantPrefix = selected + "/";
            result.AddRange(allNames.Where(name => name.StartsWith(descendantPrefix, StringComparison.Ordinal)));
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    // T-F196: ListEntriesAsync strips tar's "./" member prefix (see BuildEntryList), so a path the
    // Archive Browser hands back may need it restored to name the archive's real member.
    private static string ToArchiveMemberName(HashSet<string> allNames, string requested)
    {
        if (allNames.Contains(requested) || allNames.Contains(requested + "/"))
            return requested;
        string dotted = "./" + requested;
        return allNames.Contains(dotted) || allNames.Contains(dotted + "/") ? dotted : requested;
    }

    // Runs one of ListEntriesAsync's two tar.exe listing calls ("-tf" for names, "-tvf" for
    // type/size lines) and maps a nonzero exit code to an ArchiveListResult the same way both
    // calls already did identically. Error is non-null exactly when the call failed.
    private static async Task<(string[] Lines, ArchiveListResult? Error)> RunListingCommandAsync(
        TarSandboxScope scope, bool verbose, CancellationToken cancellationToken)
    {
        (int exitCode, string? stdOut, string? stdErr) = await scope.ListAsync(verbose, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            return ([], CoreMessages.ListFailure(IsLikelyEncryptionFailure(stdErr)
                ? CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported)
                : DescribeFailure(stdErr)));
        }

        if (verbose)
            return (SplitLines(stdOut), null);
        // T-F418: the "-t" names as the entries have them; the "-tv" lines are read by column only.
        return TarNames.ReadListing(stdOut, scope.OutputEncoding) is { } names
            ? (names, null)
            : ([], CoreMessages.ListFailure(CoreMessages.Text(MessageCode.ListingInconsistent)));
    }

    private static List<ArchiveEntryInfo> BuildEntryList(string[] names, string[] typeLines)
    {
        DateTime now = DateTime.Now;
        var entries = new List<ArchiveEntryInfo>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            // T-F196: `tar -C dir .` archives prefix every member "./" and start with a bare "./"
            // — shown verbatim, the Archive Browser's root was a lone folder named ".". List the
            // same paths the plain archive would; ExpandSelection maps them back for extraction.
            string path = EntryDiskName.StripLeadingDotSlash(names[i]).TrimEnd('/');
            if (path.Length == 0)
                continue;

            char typeChar = typeLines[i].Length > 0 ? typeLines[i][0] : '?';
            TarListedDate? modified = TarListingDate.Parse(typeLines[i], TarListingDate.UserMonthNames, now);
            entries.Add(new ArchiveEntryInfo
            {
                Path = path,
                Size = typeChar == '-' ? ParseTarListingSize(typeLines[i]) : 0,
                Modified = modified?.Value,
                ModifiedHasTime = modified?.HasTime ?? true,
                IsDirectory = typeChar == 'd',
            });
        }
        return entries;
    }

    // The pre-scan extraction runs (T-F49): an archive it refuses must not pass a test, a filter or
    // not. Then tar.exe reads what the filter passes; its declared size is what tar.exe is about to
    // write to stdout. NothingRead: no file passed, and tar.exe did not run.
    private static async Task<(int ExitCode, string StdErr, long DeclaredSize, bool NothingRead)> ScanAndTestAsync(
        TarSandboxScope scope, IProgress<ProgressReport>? progress, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        (long declaredSize, string[] names, Dictionary<string, long> sizeByName, _) =
            await ScanForUnsafeEntriesAsync(scope, cancellationToken).ConfigureAwait(false);
        if (entryFilter is not null)
        {
            // T-F441: Test reads no files to disk, so a Suspect listing cannot be settled.
            TarNames.EnsureNamesCanDecide(
                TarNames.Judge(names, await scope.ReadHeaderNamesAsync(cancellationToken).ConfigureAwait(false)));
        }
        TestSubset subset = entryFilter is null ? new TestSubset(declaredSize, null, null) : TestSubset.Of(names, sizeByName, entryFilter);
        if (subset.NothingToRead)
            return (0, string.Empty, 0, true);
        (int exitCode, string stdErr) = await scope.TestAsync(
            ContentProgress(progress, subset.DeclaredSize), subset.Members, subset.ExcludedFiles, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdErr, subset.DeclaredSize, false);
    }

    // T-F410: the files a filtered test reads. Every name is asked, folders too, so a filter counts
    // what it let through as on extraction; a folder has no data to read. tar.exe gets the shorter
    // of the two lists: libarchive matches every entry against every pattern, so a long list costs
    // N^2 (docs/DECISIONS.md, "T-F410, wave A": 40,000 names 148 s against 24 s for none).
    internal sealed record TestSubset(long DeclaredSize, IReadOnlyList<string>? Members, IReadOnlyList<string>? ExcludedFiles)
    {
        public bool NothingToRead { get; private init; }

        public static TestSubset Of(string[] names, Dictionary<string, long> sizeByName, Func<string, bool, bool> entryFilter)
        {
            var passedFiles = new List<string>();
            var leftOutFiles = new List<string>();
            foreach (string name in names)
            {
                bool isFolder = name.EndsWith('/');
                bool passes = EntryDiskName.StripLeadingDotSlash(name) is { Length: > 0 } shown && entryFilter(shown, isFolder);
                if (!isFolder)
                    (passes ? passedFiles : leftOutFiles).Add(name);
            }
            long size = passedFiles.Sum(n => sizeByName.GetValueOrDefault(n, 0L));
            if (passedFiles.Count == 0)
                return new TestSubset(0, null, null) { NothingToRead = true };
            if (leftOutFiles.Count == 0)
                return new TestSubset(size, null, null);
            return passedFiles.Count <= leftOutFiles.Count
                ? new TestSubset(size, passedFiles, null)
                : new TestSubset(size, null, leftOutFiles);
        }
    }

    // T-F171: a name several file entries share. Case-insensitive, since NTFS makes "A.txt" and
    // "a.txt" one file; FirstName is the exact name of the first entry, which "-q" matches.
    // T-F419: and by folder as tar.exe creates it, trailing dots and spaces dropped ("d./x.txt"
    // lands in "d"); a file name it writes as it is ("a.txt." stays "a.txt.", measured).
    internal sealed record DuplicateGroup(string FirstName, long FirstSize, int Count);

    // Groups among the entries being extracted (the selection, or the whole archive). A name that
    // is also a folder entry is left out: the two cannot both exist on disk.
    internal static List<DuplicateGroup> FindDuplicateGroups(
        IReadOnlyList<(string Name, long Size)> fileEntries, IReadOnlyList<string> allNames, IReadOnlyCollection<string>? selection)
    {
        HashSet<string>? selected = selection is null ? null : new HashSet<string>(selection, StringComparer.Ordinal);
        HashSet<string> folders = FolderKeys(allNames);
        var groups = new Dictionary<string, DuplicateGroup>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, long size) in fileEntries)
        {
            if (selected is not null && !selected.Contains(name))
                continue;
            string key = EntryDiskName.AsTarExeWrites(name);
            groups[key] = groups.TryGetValue(key, out DuplicateGroup? group)
                ? group with { Count = group.Count + 1 }
                : new DuplicateGroup(name, size, 1);
        }
        return groups.Where(g => g.Value.Count > 1 && !folders.Contains(g.Key)).Select(g => g.Value).ToList();
    }

    // T-F462 (closing review): the first copies a run may count - a file of the first-copy pass at its
    // group's own path, one per group, and only while the main pass holds at most one file under the
    // group's key. A member also matches as a folder prefix ("y" took "y/z.txt", bsdtar 3.8.9), a file
    // the main pass already has; and .NET's case-insensitive comparer folds pairs NTFS keeps apart
    // (U+01C5/U+01C6, measured), so such a "group" is two files on disk already. Paths: '/' between
    // parts, relative to their pass's folder.
    internal static int FirstCopiesAtTheirPath(
        IEnumerable<DuplicateGroup> groups, IEnumerable<string> firstFiles, IEnumerable<string> mainFiles)
    {
        var first = new HashSet<string>(firstFiles, StringComparer.OrdinalIgnoreCase);
        var mainPerKey = mainFiles.GroupBy(f => f, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return groups.Count(g =>
        {
            string path = EntryDiskName.AsTarExeWrites(EntryDiskName.StripLeadingDotSlash(g.FirstName));
            return first.Contains(path) && mainPerKey.GetValueOrDefault(path) <= 1;
        });
    }

    /// <summary>
    /// T-F462: how many files the run must account for - every file entry being extracted (the
    /// selection, or the whole archive), repeats included. What reaches disk, plus first copies and
    /// copies reported not extracted, must reach this, or something was lost without a word.
    /// A file named as a folder entry counts too (fresh review): tar.exe replaces the file "x" with
    /// a folder "x/" after it and exits 0, and "x /" or "x./" is a folder of its own beside "x".
    /// </summary>
    internal static int FileEntriesInScope(
        IReadOnlyList<(string Name, long Size)> fileEntries, IReadOnlyCollection<string>? selection)
    {
        HashSet<string>? selected = selection is null ? null : new HashSet<string>(selection, StringComparer.Ordinal);
        return fileEntries.Count(e => selected is null || selected.Contains(e.Name));
    }

    private static HashSet<string> FolderKeys(IReadOnlyList<string> allNames) => new(
        allNames.Where(n => n.EndsWith('/')).Select(n => EntryDiskName.AsPakkoWrites(n.TrimEnd('/'))),
        StringComparer.OrdinalIgnoreCase);


    // T-F171: runs the first-copy pass and keeps only files at a group's exact path with the first
    // entry's listed size. A member is a pattern (T-F284), so "a[1].txt" can pull in "a1.txt"
    // instead; anything unexpected stays in the quarantine. A group whose first copy is not
    // recovered keeps only its last copy, and every copy not extracted is reported.
    private static async Task<(string? Directory, HashSet<string> Files, int Reported)> ExtractFirstCopiesAsync(
        TarSandboxScope scope, List<DuplicateGroup> groups, List<SkippedFile> skippedFiles, CancellationToken cancellationToken)
    {
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (groups.Count == 0)
            return (null, accepted, 0);
        int reported = 0;

        string? firstDir;
        try
        {
            (firstDir, _, _) = await scope.ExtractFirstOccurrencesAsync(
                groups.Select(g => g.FirstName).ToList(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SandboxSetupException)
        {
            firstDir = null; // the main pass already succeeded: keep its last copies, report the rest
        }

        foreach (DuplicateGroup group in groups)
        {
            string displayName = EntryDiskName.StripLeadingDotSlash(group.FirstName);
            // Not through GetFullPath: tar.exe writes "a.txt." as it is, and that would look for "a.txt".
            string? firstPath = firstDir is null ? null
                : Path.Combine(firstDir, EntryDiskName.AsTarExeWrites(displayName).Replace('/', Path.DirectorySeparatorChar));
            FileInfo? info = firstPath is null ? null : QuarantineFiles.Info(firstPath);
            bool recovered = info is { Exists: true } && info.Length == group.FirstSize
                && (info.Attributes & FileAttributes.ReparsePoint) == 0;
            if (recovered)
                accepted.Add(firstPath!);
            int notExtracted = group.Count - (recovered ? 2 : 1);
            reported += Math.Max(0, notExtracted);
            if (notExtracted > 0)
                skippedFiles.Add(CoreMessages.Skip(displayName, MessageCode.TarDuplicateCopiesNotExtracted,
                    notExtracted.ToString(CultureInfo.CurrentCulture)));
        }
        return (firstDir, accepted, reported);
    }

    // T-F52/T-F298: the directories an entry needs before its own directory entry has been seen,
    // in archive order — tar.exe cannot create those itself inside the AppContainer. A directory
    // whose entry comes before everything inside it (what GNU tar, bsdtar and 7-Zip write) is
    // left to tar.exe, which then sets its time. Pre-creating a directory also creates its
    // parents, so a rare archive with an implicit subfolder under an explicit one loses that
    // parent's time; it still extracts.
    // T-F52's folders, made where tar.exe will look for them: by the name tar.exe writes ("d?" is
    // "d_", T-F420; "d " keeps its space, T-F454) and through the long-path prefix, or Windows would
    // make "d" for "d ". Returns their names below outputDirectory, with the parents made along the
    // way: the quarantine starts empty, so those are Pakko's too (T-F443).
    internal static HashSet<string> PreCreateImplicitFolders(IEnumerable<string> namesInArchiveOrder, string outputDirectory)
    {
        var preCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativeDir in DirectoriesToPreCreate(namesInArchiveOrder))
        {
            string onDisk = EntryDiskName.AsTarExeWrites(relativeDir + "/");
            QuarantineFiles.CreateFolder(Path.Combine(outputDirectory, onDisk.Replace('/', Path.DirectorySeparatorChar)));
            preCreated.Add(onDisk);
            for (int sep = onDisk.LastIndexOf('/'); sep > 0; sep = onDisk.LastIndexOf('/', sep - 1))
                preCreated.Add(onDisk[..sep]);
        }
        return preCreated;
    }

    internal static List<string> DirectoriesToPreCreate(IEnumerable<string> namesInArchiveOrder)
    {
        var seenDirectoryEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in namesInArchiveOrder)
        {
            string path = EntryDiskName.StripLeadingDotSlash(name);
            bool isDirectory = path.EndsWith('/');
            string[] segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < segments.Length; i++)
            {
                string ancestor = string.Join('/', segments, 0, i);
                if (!seenDirectoryEntries.Contains(ancestor))
                    preCreate.Add(ancestor);
            }
            if (isDirectory && segments.Length > 0)
                seenDirectoryEntries.Add(string.Join('/', segments));
        }
        return [.. preCreate];
    }

    // T-F443: the folders tar.exe wrote, by their names on disk, when the listing named some other
    // way (best fit): every folder in the quarantine but those Pakko pre-created by a listed name.
    // One of those that holds anything arrives with its content anyway; an empty one was only the
    // listing's twin of a true folder. Accepted limit: a true empty folder whose entry follows its
    // content and whose name is another folder's best fit goes with the twin (DECISIONS, "T-F443").
    internal static List<string> FoldersTarExeWrote(IEnumerable<(string Path, bool IsFolder)> quarantined, IReadOnlySet<string> preCreated) =>
        [.. quarantined.Where(item => item.IsFolder && !preCreated.Contains(item.Path)).Select(item => item.Path)];

}
