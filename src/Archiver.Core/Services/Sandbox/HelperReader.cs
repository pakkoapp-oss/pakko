using System.Globalization;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Text;
using static Archiver.Core.Services.TarSandboxedService;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F470: the archive helper's reader - Archiver.ArchiveHelper.exe over archiveint.dll, in the
/// scope's sandbox. Entries are addressed by their index in the archive and written as
/// out\&lt;index&gt;, so a name never travels back to the parser: names are the helper's exact UTF-16
/// ones, checked here, and placed by the move phase. The helper is treated as compromised: every
/// run's output is checked against the protocol (<see cref="ArchiveHelperOutput"/>), every
/// extraction against the scan's listing, the selection and the files in out\.
/// Same-named copies: each is extracted by its index and meets the conflict rule in archive order,
/// as duplicate ZIP entries do (T-F30); there is no first-copy pass.
/// </summary>
internal sealed class HelperReader : ISandboxedArchiveReader
{
    private const int RegularFile = 0x8000;
    private const int FolderType = 0x4000;
    private const string ScanFirst = "ScanAsync comes first.";

    private readonly TarSandboxScope _scope;
    private readonly IReadOnlyList<string> _formatArguments;
    private string? _charset;
    private Listing? _listing;
    private int[]? _selected;

    // One listed entry: its index, its name without "./" (a folder ends in '/'), its declared size.
    private readonly record struct Entry(long Index, string Name, bool IsFolder, long Size, bool Sparse, bool Encrypted, DateTime? ModifiedUtc);

    // T-F542: what the scan keeps of the helper's listing - one compact entry each, the archive's root
    // left out - and the listing's digest and length, which every later run's own listing must match.
    // Steps after the scan hold positions in Entries, never copies of them.
    private sealed record Listing(Entry[] Entries, byte[] Digest, long Count);

    private HelperReader(TarSandboxScope scope, IReadOnlyList<string> formatArguments)
    {
        _scope = scope;
        _formatArguments = formatArguments;
    }

    /// <summary>
    /// Opens a scope that may run the helper, then picks the reader by the archive's format - read
    /// while the scope holds the archive open against writes, so the bytes it names are the ones
    /// read. A format the helper does not read goes to tar.exe, before anything is read.
    /// </summary>
    internal static async Task<ISandboxedArchiveReader> OpenAsync(
        string archivePath, bool needsOutputDir, Encoding? outputEncoding, HelperSetup helper, CancellationToken cancellationToken,
        string? nearFolder = null)
    {
        TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir, cancellationToken, outputEncoding, helper.HelperPath, nearFolder)
            .ConfigureAwait(false);
        ArchiveFormat format = ArchiveFormatDetector.Detect(archivePath);
        IReadOnlyList<string>? arguments = format == ArchiveFormat.GZip && !BareGzip.HoldsTarOrCpio(archivePath)
            ? null
            : HelperFormats.ArgumentsFor(format, helper.Capabilities);
        return arguments is null ? TarExeReader.On(scope) : new HelperReader(scope, arguments);
    }

    public void Dispose() => _scope.Dispose();

    public async Task<ArchiveListSummary> ListAsync(Action<ArchiveEntryInfo> onEntry, CancellationToken cancellationToken)
    {
        long passed = 0;
        void Pass(ArchiveHelperEntry e)
        {
            string path = EntryDiskName.StripLeadingDotSlash(e.Name!).TrimEnd('/');
            if (path.Length == 0)
                return;
            onEntry(new ArchiveEntryInfo
            {
                Path = path,
                Size = e.FileType == RegularFile ? e.Size ?? 0 : 0,
                Modified = ModifiedTimeOf(e)?.ToLocalTime(),
                IsDirectory = e.FileType == FolderType && e.Link is null,
            });
            passed++;
        }

        ArchiveListSummary unreadable = CoreMessages.Summary(CoreMessages.ListFailure(CoreMessages.Text(MessageCode.ListingInconsistent)));
        try
        {
            // T-F540: an entry passed on cannot be taken back, so the page names are read in is
            // settled first by listings that pass nothing on; then one listing in that page passes
            // each entry on as it comes (two passes, as tar.exe's "-t" and "-tv").
            string? charset = null;
            if (HelperFormats.TakesHeaderCharset(_formatArguments))
            {
                if (await ListWithCharsetAsync(Discard, NothingToTakeBack, cancellationToken).ConfigureAwait(false) is null)
                    return unreadable;
                charset = _charset;
            }
            (_, bool anyNameless, _) = await ListOnceAsync(charset, Pass, cancellationToken).ConfigureAwait(false);
            if (anyNameless)
                return unreadable;
        }
        catch (CoreTextIOException failed)
        {
            return CoreMessages.Summary(CoreMessages.ListFailure(failed.Text));
        }
        return new ArchiveListSummary { Success = true, Count = passed };
    }

    private static void NothingToTakeBack()
    {
        // the settling listings passed nothing on
    }

    // The helper's selection goes by index, never back to tar.exe as names.
    public async Task<ReaderCatalog> ScanAsync(bool sendsNamesBack, CancellationToken cancellationToken)
    {
        Entry[] entries = await ScanEntriesAsync(refuseEncrypted: true, cancellationToken).ConfigureAwait(false);
        return new ReaderCatalog(
            entries.Where(e => !e.IsFolder).Sum(e => e.Size),
            entries.Select(e => e.Name),
            entries.Where(e => !e.IsFolder).Select(e => e.Name));
    }

    public Task<SelectionOutcome> SelectForExtractAsync(
        IReadOnlyList<string>? selectedEntryPaths, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        Entry[] entries = Listed();
        int[] candidates = selectedEntryPaths is { Count: > 0 } ? Expand(entries, selectedEntryPaths) : All(entries);
        if (selectedEntryPaths is { Count: > 0 } && candidates.Length == 0)
            return Task.FromResult(new SelectionOutcome(SelectionKind.EmptySelection, 0, 0));
        if (entryFilter is not null)
        {
            candidates = [.. candidates.Where(i => entryFilter(entries[i].Name, entries[i].IsFolder))];
            if (candidates.Length == 0)
                return Task.FromResult(new SelectionOutcome(SelectionKind.NothingPassesFilter, 0, 0));
        }
        _selected = candidates;
        int[] files = FilesAmong(entries, candidates);
        return Task.FromResult(new SelectionOutcome(SelectionKind.Entries, SizeOf(entries, files), files.Length));
    }

    public async Task<IQuarantinedEntries> ExtractAsync(
        ExtractionPoll? poll, List<SkippedFile> skippedFiles, CancellationToken cancellationToken)
    {
        Entry[] entries = Listed();
        int[] selected = _selected ?? throw new InvalidOperationException("SelectForExtractAsync comes first.");
        int[] files = FilesAmong(entries, selected);
        string outDir = _scope.OutputDirectory!;
        if (files.Length > 0)
        {
            Task<ArchiveHelperOutput> run = ExtractFilesAsync(entries, files, cancellationToken);
            if (poll is { } p)
            {
                await PollExtractionProgressAsync(outDir, p.TotalBytes, new NamedProgress(p.Progress, entries, files), run, cancellationToken, p.ExtractionEnd)
                    .ConfigureAwait(false);
            }
            ArchiveHelperOutput output = await run.ConfigureAwait(false);
            EnsureOutDirHoldsExactly(entries, files, output);
        }
        return new HelperQuarantine(outDir, entries, files, [.. selected.Where(i => entries[i].IsFolder)]);
    }

    public async Task<ScanQuarantine> ExtractForScanAsync(IReadOnlyList<string>? selectedEntryPaths, CancellationToken cancellationToken)
    {
        Entry[] entries = Listed();
        int[] files = FilesAmong(entries, selectedEntryPaths is { Count: > 0 } ? Expand(entries, selectedEntryPaths) : All(entries));
        if (files.Length == 0)
            return new ScanQuarantine(null, [], 0, 0, false);
        ArchiveHelperOutput output;
        try
        {
            output = await ExtractFilesAsync(entries, files, cancellationToken).ConfigureAwait(false);
        }
        catch (CoreTextIOException failed)
        {
            return new ScanQuarantine(failed.Text, [], files.Length, files.Length, false);
        }
        EnsureOutDirHoldsExactly(entries, files, output);
        string outDir = _scope.OutputDirectory!;
        return new ScanQuarantine(null, files.Select(i => new ScanFile(Path.Combine(outDir, IndexName(entries[i].Index)), entries[i].Name)),
            files.Length, files.Length, false);
    }

    public Task<FirstCopiesForScan> ExtractFirstCopiesForScanAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The archive helper extracts every copy in its one pass.");

    public async Task<TestOutcome> TestAsync(
        IProgress<ProgressReport>? progress, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        Entry[] entries = await ScanEntriesAsync(refuseEncrypted: false, cancellationToken).ConfigureAwait(false);
        int[] files = [.. Enumerable.Range(0, entries.Length).Where(i => !entries[i].IsFolder && (entryFilter is null || entryFilter(entries[i].Name, false)))];
        if (entryFilter is not null)
        {
            // T-F410: every name is asked, folders too, as on extraction.
            foreach (Entry folder in entries.Where(e => e.IsFolder))
                entryFilter(folder.Name, true);
        }
        if (files.Length == 0)
            return new TestOutcome(null, 0, NothingRead: true);
        long declared = SizeOf(entries, files);
        if (files.Any(i => entries[i].Encrypted))
            return new TestOutcome(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported), declared, false);

        Action<int> onBytes = ContentProgress(progress, declared);
        // T-F479: "P" says how far the entry under way got, its "K" where it ended; the bar takes the growth.
        long counted = 0;
        void Grow(long bytes)
        {
            onBytes((int)Math.Min(int.MaxValue, bytes - counted));
            counted = bytes;
        }
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Test,
            onRead: (_, bytes) => { Grow(bytes); counted = 0; },
            onReading: (_, bytes) => Grow(bytes),
            onEntry: Discard);
        try
        {
            await RunAsync(ArchiveHelperMode.Test, _charset, IndicesOf(entries, files), output, cancellationToken).ConfigureAwait(false);
        }
        catch (CoreTextIOException failed)
        {
            return new TestOutcome(failed.Text, declared, false);
        }
        EnsureListingAndReadsAgree(entries, files, output);
        return new TestOutcome(null, declared, false);
    }

    private Entry[] Listed() => _listing?.Entries ?? throw new InvalidOperationException(ScanFirst);

    private static int[] All(Entry[] entries) => [.. Enumerable.Range(0, entries.Length)];

    private static int[] FilesAmong(Entry[] entries, int[] positions) => [.. positions.Where(i => !entries[i].IsFolder)];

    private static long SizeOf(Entry[] entries, int[] positions) => positions.Sum(i => entries[i].Size);

    private static long[] IndicesOf(Entry[] entries, int[] positions) => [.. positions.Select(i => entries[i].Index)];

    // A later run's entries: checked by the listing's digest, not kept.
    private static void Discard(ArchiveHelperEntry entry)
    {
        // nothing kept: ArchiveHelperOutput.ListingDigest stands for the entries
    }

    // --- Listing ---

    // T-F204's order: UTF-8 first; names stored in no charset that are not UTF-8 come back NULL
    // with a warning, and the archive is listed again in the page tar.exe reads raw names in.
    // Null: a name no charset reads, so the listing cannot name the entries.
    // T-F542: each entry goes to onEntry as it is read; restart is called before a second listing.
    private async Task<ArchiveHelperOutput?> ListWithCharsetAsync(
        Action<ArchiveHelperEntry> onEntry, Action restart, CancellationToken cancellationToken)
    {
        bool charsetApplies = HelperFormats.TakesHeaderCharset(_formatArguments);
        string? charset = charsetApplies ? TextBoundaries.ArchiveHelperUtf8Names : null;
        (ArchiveHelperOutput output, bool anyNameless, _) = await ListOnceAsync(charset, onEntry, cancellationToken).ConfigureAwait(false);
        if (charsetApplies && anyNameless)
        {
            if (TextBoundaries.ArchiveHelperRawNames(MachinePages.Current) is not { } raw)
                return null;
            charset = raw.Charset;
            restart();
            bool anyReplacement;
            (output, anyNameless, anyReplacement) = await ListOnceAsync(charset, onEntry, cancellationToken).ConfigureAwait(false);
            // libarchive 3.5.2 decodes a sequence a multi-byte page lacks as U+30FB without a
            // warning (3.8.9 warns): such a name cannot be told from a true one (docs/DECISIONS.md).
            bool silentReplacement = !raw.Boundary.Strict.IsSingleByte
                && output.Version?.StartsWith("libarchive 3.5.", StringComparison.Ordinal) == true
                && anyReplacement;
            if (silentReplacement)
                return null;
        }
        if (anyNameless)
            return null;
        _charset = charset;
        return output;
    }

    // One listing; once an entry comes without a name (no charset read it) the rest are not passed on.
    private async Task<(ArchiveHelperOutput Output, bool AnyNameless, bool AnyReplacement)> ListOnceAsync(
        string? charset, Action<ArchiveHelperEntry> onEntry, CancellationToken cancellationToken)
    {
        bool anyNameless = false;
        bool anyReplacement = false;
        void Take(ArchiveHelperEntry e)
        {
            anyNameless |= e.Name is null;
            if (anyNameless)
                return;
            anyReplacement |= e.Name!.Contains('\u30FB', StringComparison.Ordinal);
            onEntry(e);
        }
        var output = new ArchiveHelperOutput(ArchiveHelperMode.List, onEntry: Take);
        await RunAsync(ArchiveHelperMode.List, charset, null, output, cancellationToken).ConfigureAwait(false);
        return (output, anyNameless, anyReplacement);
    }

    // The whole archive, checked as tar.exe's scan checks it (T-F49, T-F420): a link or a special
    // entry, an unsafe name, an 8.3 alias of another name refuse it.
    private async Task<Entry[]> ScanEntriesAsync(bool refuseEncrypted, CancellationToken cancellationToken)
    {
        var scan = new ScanChecks(refuseEncrypted);
        ArchiveHelperOutput output = await ListWithCharsetAsync(scan.Take, scan.Restart, cancellationToken).ConfigureAwait(false)
            ?? throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));
        Entry[] entries = scan.Finish();
        _listing = new Listing(entries, output.ListingDigest.ToArray(), output.EntryCount);
        return entries;
    }

    // T-F542: the scan's checks as the entries arrive, refused in the order a whole listing was: an
    // unsafe name among all of them first, an alias of another name next, then the first entry the
    // scan does not take. The listing's own names are held only until Finish, for the alias check.
    private sealed class ScanChecks(bool refuseEncrypted)
    {
        private List<Entry> _entries = [];
        private List<string> _rawNames = [];
        private string? _firstUnsafe;
        private Exception? _firstRefusal;

        public void Take(ArchiveHelperEntry e)
        {
            string raw = e.Name!;
            _rawNames.Add(raw);
            if (_firstUnsafe is null && IsUnsafeName(raw))
                _firstUnsafe = raw;
            if (_firstRefusal is null && Checked(e, refuseEncrypted, out _firstRefusal) is { } entry)
                _entries.Add(entry);
        }

        public void Restart()
        {
            _entries.Clear();
            _rawNames.Clear();
            _firstUnsafe = null;
            _firstRefusal = null;
        }

        public Entry[] Finish()
        {
            string? unsafeName = _firstUnsafe ?? EntryDiskName.ShortNameAliasOfAnother(_rawNames);
            if (unsafeName != null)
                throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarUnsafeEntryPath, unsafeName));
            if (_firstRefusal is { } refusal)
                throw refusal;
            Entry[] entries = [.. _entries];
            // Let go of the growing lists at once, not when whatever still holds these checks does.
            _entries = [];
            _rawNames = [];
            return entries;
        }

        // One listed entry as the scan takes it; null for the archive's root itself, or with the refusal
        // the entry earns (not thrown: the scan's other checks come first).
        private static Entry? Checked(ArchiveHelperEntry e, bool refuseEncrypted, out Exception? refusal)
        {
            refusal = null;
            if (e.Link is not null || e.FileType is not (RegularFile or FolderType))
            {
                refusal = new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarSpecialEntry));
                return null;
            }
            if (refuseEncrypted && e.Encrypted == true)
            {
                refusal = new CoreTextIOException(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported));
                return null;
            }
            bool isFolder = e.FileType == FolderType;
            string name = EntryDiskName.StripLeadingDotSlash(e.Name!);
            // T-F455: a folder stored without its '/' is still a folder.
            if (isFolder && !name.EndsWith('/'))
                name += "/";
            if (name.TrimEnd('/').Length == 0)
                return null;
            if (!isFolder && e.Size is null)
            {
                refusal = new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));
                return null;
            }
            return new Entry(e.Index, name, isFolder, isFolder ? 0 : e.Size!.Value, e.SparseCount > 0, e.Encrypted == true, ModifiedTimeOf(e));
        }
    }

    // tar.exe's scan rules (T-F49, T-F420), and a name with an unpaired surrogate: no file name a
    // reader of it can rely on (tar.exe's listing could never carry one).
    internal static bool IsUnsafeName(string rawName) => IsDangerousEntryName(rawName) || HasLoneSurrogate(rawName);

    private static bool HasLoneSurrogate(string name)
    {
        ReadOnlySpan<char> rest = name;
        while (!rest.IsEmpty)
        {
            if (System.Text.Rune.DecodeFromUtf16(rest, out _, out int used) != System.Buffers.OperationStatus.Done)
                return true;
            rest = rest[used..];
        }
        return false;
    }

    // ExpandSelection's rule (T-F05) by name: an entry named as requested, and everything below it.
    private static int[] Expand(Entry[] entries, IReadOnlyList<string> requested)
    {
        bool[] picked = new bool[entries.Length];
        foreach (string path in requested)
        {
            string exact = path.TrimEnd('/');
            string folder = exact + "/";
            for (int i = 0; i < entries.Length; i++)
                picked[i] |= entries[i].Name.TrimEnd('/') == exact || entries[i].Name.StartsWith(folder, StringComparison.Ordinal);
        }
        return [.. Enumerable.Range(0, entries.Length).Where(i => picked[i])];
    }

    // --- Runs and their checks ---

    private async Task<ArchiveHelperOutput> ExtractFilesAsync(Entry[] entries, int[] files, CancellationToken cancellationToken)
    {
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Extract, onEntry: Discard);
        long maxTotal = SizeOf(entries, files);
        await RunAsync(ArchiveHelperMode.Extract, _charset, IndicesOf(entries, files), output, cancellationToken,
            ["--max-total", maxTotal.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        return output;
    }

    private async Task RunAsync(
        ArchiveHelperMode mode, string? charset, IReadOnlyList<long>? selection, ArchiveHelperOutput output,
        CancellationToken cancellationToken, IReadOnlyList<string>? extra = null)
    {
        List<string> arguments = [.. _formatArguments];
        if (charset is not null)
            arguments.AddRange(["--hdrcharset", charset]);
        if (extra is not null)
            arguments.AddRange(extra);
        (ArchiveHelperExit exit, string limitHit) = await _scope.RunHelperAsync(mode, arguments, selection, output, cancellationToken)
            .ConfigureAwait(false);
        if (exit == ArchiveHelperExit.Done)
            return;
        string detail = limitHit.Length > 0 ? limitHit : output.Failure?.Message ?? "exit " + ((int)exit).ToString(CultureInfo.InvariantCulture);
        if (IsLikelyEncryptionFailure(detail))
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported));
        throw new CoreTextIOException(mode == ArchiveHelperMode.Extract
            ? CoreMessages.Text(MessageCode.TarExtractionFailed, CoreText.Raw(detail))
            : CoreMessages.Text(MessageCode.CannotReadArchive, detail));
    }

    private void EnsureListingAndReadsAgree(Entry[] entries, int[] files, ArchiveHelperOutput output)
    {
        Listing listing = _listing ?? throw new InvalidOperationException(ScanFirst);
        EnsureListingAndReadsAgree(listing.Digest, listing.Count,
            [.. files.Select(i => (entries[i].Index, entries[i].Size, entries[i].Sparse))], output);
    }

    private void EnsureOutDirHoldsExactly(Entry[] entries, int[] files, ArchiveHelperOutput output)
    {
        EnsureListingAndReadsAgree(entries, files, output);
        EnsureOutDirHoldsExactly(_scope.OutputDirectory!, output);
    }

    /// <summary>
    /// A test or an extraction lists the archive again as it reads: the same entries as the scan,
    /// field for field (<paramref name="digest"/> and <paramref name="count"/> are the scan's
    /// <see cref="ArchiveHelperOutput.ListingDigest"/> and <see cref="ArchiveHelperOutput.EntryCount"/>);
    /// then K lines for exactly the selection, each a non-sparse entry's declared size.
    /// </summary>
    internal static void EnsureListingAndReadsAgree(
        byte[] digest, long count, IReadOnlyList<(long Index, long Size, bool Sparse)> files, ArchiveHelperOutput output)
    {
        if (output.EntryCount != count || !output.ListingDigest.SequenceEqual(digest))
            throw new ArchiveHelperProtocolException("a second listing differs from the first");
        if (!output.Read.Select(r => r.Index).SequenceEqual(files.Select(e => e.Index)))
            throw new ArchiveHelperProtocolException("the entries read are not the selection");
        foreach (((long _, long size, bool sparse), (long _, long bytes)) in files.Zip(output.Read))
        {
            if (!sparse && bytes != size)
                throw new ArchiveHelperProtocolException("an entry read other than its declared size");
        }
    }

    /// <summary>out\ holds exactly the files the run read, each named by its index, a plain file of the bytes read.</summary>
    internal static void EnsureOutDirHoldsExactly(string outDir, ArchiveHelperOutput output)
    {
        IReadOnlyList<(long Index, long Bytes)> read = output.Read;
        int found = 0;
        foreach ((string path, bool isFolder) in QuarantineFiles.Walk(outDir))
        {
            int at = isFolder ? -1 : PositionOf(read, QuarantineFiles.Relative(outDir, path));
            FileInfo info = QuarantineFiles.Info(path);
            if (at < 0 || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length != read[at].Bytes)
                throw new ArchiveHelperProtocolException("out\\ holds a file the run did not account for");
            found++;
        }
        if (found != read.Count)
            throw new ArchiveHelperProtocolException("a file the run read is missing from out\\");
    }

    // T-F542: where out\'s file of this name is among the reads (the parser keeps them in increasing
    // index order), by its index - no table of every name; -1 for a name that is no index.
    private static int PositionOf(IReadOnlyList<(long Index, long Bytes)> read, string name) =>
        Find(read.Count, at => read[at].Index, name);

    // The position among count increasing indices (indexAt) of the one an out\ file name spells; -1 if none.
    private static int Find(int count, Func<int, long> indexAt, string indexName)
    {
        if (!long.TryParse(indexName, NumberStyles.None, CultureInfo.InvariantCulture, out long index) || IndexName(index) != indexName)
            return -1;
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            long found = indexAt(middle);
            if (found == index)
                return middle;
            if (found < index)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return -1;
    }

    private static string IndexName(long index) => index.ToString(CultureInfo.InvariantCulture);

    private static DateTime? ModifiedTimeOf(ArchiveHelperEntry e) =>
        e.MtimeSeconds is { } seconds && seconds >= -62135596800L && seconds <= 253402300799L
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.AddTicks(e.MtimeNanoseconds / 100)
            : null;

    // The byte poll names the newest file in out\, an index; the user sees the entry's name, found
    // among the files by that index (T-F542: no table of names).
    private sealed class NamedProgress(IProgress<ProgressReport> inner, Entry[] entries, int[] files) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) =>
            inner.Report(new ProgressReport
            {
                Percent = value.Percent,
                BytesTransferred = value.BytesTransferred,
                TotalBytes = value.TotalBytes,
                Phase = value.Phase,
                CurrentFile = value.CurrentFile is { } f ? NameOf(f) : null,
            });

        private string? NameOf(string indexName) =>
            Find(files.Length, at => entries[files[at]].Index, indexName) is int at and >= 0 ? entries[files[at]].Name : null;
    }

    // What an extraction left in out\: the files by index, placed under their entries' names.
    private sealed class HelperQuarantine(string outDir, Entry[] entries, int[] files, int[] folders) : IQuarantinedEntries
    {
        public int ExpectedMoves => files.Length;

        public IEnumerable<QuarantinedFile> Files() =>
            files.Select(i => entries[i]).Select(e => new QuarantinedFile(
                Path.Combine(outDir, IndexName(e.Index)),
                EntryDiskName.AsPakkoWrites(e.Name).Replace('/', Path.DirectorySeparatorChar),
                EntryDiskName.Renames(e.Name),
                EntryDiskName.Trims(e.Name)));

        // Every file was checked present in out\ before this quarantine was made.
        public void EnsureNoneMissing()
        {
            // nothing left to count: EnsureOutDirHoldsExactly accounted for every file
        }

        public void RecordFolderTimesInto(FolderPlacement placement)
        {
            foreach (Entry folder in folders.Select(i => entries[i]))
            {
                if (folder.ModifiedUtc is { } utc && Placed(folder.Name, placement.StripRootPrefix) is { } relative)
                    placement.Staging.RecordFolderTime(relative, utc);
            }
        }

        public void CreateFolderEntriesIn(FolderPlacement placement)
        {
            foreach (Entry folder in folders.Select(i => entries[i]))
            {
                if (Placed(folder.Name, placement.StripRootPrefix) is { Length: > 0 } relative)
                    Directory.CreateDirectory(Path.Combine(placement.Staging.Path, relative));
            }
        }

        // Where a folder entry lands below the destination; "" is the stripped root.
        private static string? Placed(string name, bool stripRootPrefix)
        {
            string relative = EntryDiskName.AsPakkoWrites(name).Replace('/', Path.DirectorySeparatorChar);
            if (!stripRootPrefix)
                return relative;
            int sep = relative.IndexOf(Path.DirectorySeparatorChar);
            return sep < 0 ? string.Empty : relative[(sep + 1)..];
        }
    }
}

/// <summary>
/// T-F470: which of the helper's formats and filter read an archive of a detected format, given
/// the probe's <c>support_*</c> results; null when the helper cannot read it (tar.exe then does).
/// </summary>
internal static class HelperFormats
{
    private const int ArchiveOk = 0;
    private static readonly string[] Containers = ["tar", "cpio", "7zip", "rar", "rar5", "cab"];
    private static readonly string[] CharsetFormats = ["tar", "cpio", "rar", "cab"];

    /// <summary>The "--format" and "--filter" arguments for <paramref name="format"/>, or null.</summary>
    internal static IReadOnlyList<string>? ArgumentsFor(ArchiveFormat format, IReadOnlyDictionary<string, int?> capabilities)
    {
        (string[] formats, string? filter) = format switch
        {
            ArchiveFormat.Tar => (["tar"], null),
            ArchiveFormat.Cpio => (["cpio"], null),
            ArchiveFormat.SevenZip => (["7zip"], null),
            ArchiveFormat.Rar => (["rar", "rar5"], null),
            ArchiveFormat.Cab => (["cab"], null),
            // A gzip's content is checked first (OpenAsync); another stream's is not known before it
            // is read, so every container the helper has.
            ArchiveFormat.GZip => (["tar", "cpio"], "gzip"),
            ArchiveFormat.Bz2 => (Containers, "bzip2"),
            ArchiveFormat.Xz => (Containers, "xz"),
            ArchiveFormat.Zstd => (Containers, "zstd"),
            ArchiveFormat.Lzma => (Containers, "lzma"),
            ArchiveFormat.Compress => (Containers, "compress"),
            ArchiveFormat.Lzip => (Containers, "lzip"),
            _ => (Array.Empty<string>(), (string?)null),
        };
        if (formats.Length == 0 || (filter is not null && !Ok(capabilities, filter)) || !formats.All(f => Ok(capabilities, f)))
            return null;
        return filter is null
            ? ["--format", string.Join(',', formats)]
            : ["--format", string.Join(',', formats), "--filter", filter];
    }

    /// <summary>Whether a run with these arguments takes "--hdrcharset": some format of it stores raw names.</summary>
    internal static bool TakesHeaderCharset(IReadOnlyList<string> formatArguments)
    {
        int at = formatArguments.ToList().IndexOf("--format");
        return at >= 0 && at + 1 < formatArguments.Count && formatArguments[at + 1].Split(',').Any(CharsetFormats.Contains);
    }

    private static bool Ok(IReadOnlyDictionary<string, int?> capabilities, string name) =>
        capabilities.TryGetValue(name, out int? rc) && rc == ArchiveOk;
}
