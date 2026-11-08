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
    private IReadOnlyList<ArchiveHelperEntry>? _listed;
    private List<Entry>? _entries;
    private List<Entry>? _selected;

    // One listed entry: its index, its name without "./" (a folder ends in '/'), its declared size.
    private sealed record Entry(long Index, string Name, bool IsFolder, long Size, bool Sparse, DateTime? ModifiedUtc);

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
        string archivePath, bool needsOutputDir, Encoding? outputEncoding, HelperSetup helper, CancellationToken cancellationToken)
    {
        TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir, cancellationToken, outputEncoding, helper.HelperPath)
            .ConfigureAwait(false);
        ArchiveFormat format = ArchiveFormatDetector.Detect(archivePath);
        IReadOnlyList<string>? arguments = format == ArchiveFormat.GZip && !BareGzip.HoldsTarOrCpio(archivePath)
            ? null
            : HelperFormats.ArgumentsFor(format, helper.Capabilities);
        return arguments is null ? TarExeReader.On(scope) : new HelperReader(scope, arguments);
    }

    public void Dispose() => _scope.Dispose();

    public async Task<ArchiveListResult> ListAsync(CancellationToken cancellationToken)
    {
        ArchiveHelperOutput? output;
        try
        {
            output = await ListWithCharsetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CoreTextIOException failed)
        {
            return CoreMessages.ListFailure(failed.Text);
        }
        if (output is null)
            return CoreMessages.ListFailure(CoreMessages.Text(MessageCode.ListingInconsistent));

        var entries = new List<ArchiveEntryInfo>(output.Entries.Count);
        foreach (ArchiveHelperEntry e in output.Entries)
        {
            string path = EntryDiskName.StripLeadingDotSlash(e.Name!).TrimEnd('/');
            if (path.Length == 0)
                continue;
            entries.Add(new ArchiveEntryInfo
            {
                Path = path,
                Size = e.FileType == RegularFile ? e.Size ?? 0 : 0,
                Modified = ModifiedTimeOf(e)?.ToLocalTime(),
                IsDirectory = e.FileType == FolderType && e.Link is null,
            });
        }
        return new ArchiveListResult { Success = true, Entries = entries };
    }

    public async Task<ReaderCatalog> ScanAsync(CancellationToken cancellationToken)
    {
        List<Entry> entries = await ScanEntriesAsync(refuseEncrypted: true, cancellationToken).ConfigureAwait(false);
        List<Entry> files = [.. entries.Where(e => !e.IsFolder)];
        return new ReaderCatalog(
            files.Sum(e => e.Size),
            [.. entries.Select(e => e.Name)],
            [.. files.Select(e => e.Name)]);
    }

    public Task<SelectionOutcome> SelectForExtractAsync(
        IReadOnlyList<string>? selectedEntryPaths, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        List<Entry> entries = _entries ?? throw new InvalidOperationException(ScanFirst);
        List<Entry> candidates = selectedEntryPaths is { Count: > 0 } ? Expand(entries, selectedEntryPaths) : entries;
        if (selectedEntryPaths is { Count: > 0 } && candidates.Count == 0)
            return Task.FromResult(new SelectionOutcome(SelectionKind.EmptySelection, 0, 0));
        if (entryFilter is not null)
        {
            candidates = [.. candidates.Where(e => entryFilter(e.Name, e.IsFolder))];
            if (candidates.Count == 0)
                return Task.FromResult(new SelectionOutcome(SelectionKind.NothingPassesFilter, 0, 0));
        }
        _selected = candidates;
        List<Entry> files = [.. candidates.Where(e => !e.IsFolder)];
        return Task.FromResult(new SelectionOutcome(SelectionKind.Entries, files.Sum(e => e.Size), files.Count));
    }

    public async Task<IQuarantinedEntries> ExtractAsync(
        ExtractionPoll? poll, List<SkippedFile> skippedFiles, CancellationToken cancellationToken)
    {
        List<Entry> selected = _selected ?? throw new InvalidOperationException("SelectForExtractAsync comes first.");
        List<Entry> files = [.. selected.Where(e => !e.IsFolder)];
        string outDir = _scope.OutputDirectory!;
        if (files.Count > 0)
        {
            Task<ArchiveHelperOutput> run = ExtractFilesAsync(files, cancellationToken);
            if (poll is { } p)
            {
                var names = files.ToDictionary(e => IndexName(e.Index), e => e.Name, StringComparer.Ordinal);
                await PollExtractionProgressAsync(outDir, p.TotalBytes, new NamedProgress(p.Progress, names), run, cancellationToken, p.ExtractionEnd)
                    .ConfigureAwait(false);
            }
            ArchiveHelperOutput output = await run.ConfigureAwait(false);
            EnsureOutDirHoldsExactly(files, output);
        }
        return new HelperQuarantine(outDir, files, [.. selected.Where(e => e.IsFolder)]);
    }

    public async Task<ScanQuarantine> ExtractForScanAsync(IReadOnlyList<string>? selectedEntryPaths, CancellationToken cancellationToken)
    {
        List<Entry> entries = _entries ?? throw new InvalidOperationException(ScanFirst);
        List<Entry> files = [.. (selectedEntryPaths is { Count: > 0 } ? Expand(entries, selectedEntryPaths) : entries).Where(e => !e.IsFolder)];
        if (files.Count == 0)
            return new ScanQuarantine(null, [], 0, 0, false);
        ArchiveHelperOutput output;
        try
        {
            output = await ExtractFilesAsync(files, cancellationToken).ConfigureAwait(false);
        }
        catch (CoreTextIOException failed)
        {
            return new ScanQuarantine(failed.Text, [], files.Count, files.Count, false);
        }
        EnsureOutDirHoldsExactly(files, output);
        string outDir = _scope.OutputDirectory!;
        return new ScanQuarantine(null, [.. files.Select(e => new ScanFile(Path.Combine(outDir, IndexName(e.Index)), e.Name))],
            files.Count, files.Count, false);
    }

    public Task<FirstCopiesForScan> ExtractFirstCopiesForScanAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The archive helper extracts every copy in its one pass.");

    public async Task<TestOutcome> TestAsync(
        IProgress<ProgressReport>? progress, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken)
    {
        List<Entry> entries = await ScanEntriesAsync(refuseEncrypted: false, cancellationToken).ConfigureAwait(false);
        List<Entry> files = [.. entries.Where(e => !e.IsFolder && (entryFilter is null || entryFilter(e.Name, false)))];
        if (entryFilter is not null)
        {
            // T-F410: every name is asked, folders too, as on extraction.
            foreach (Entry folder in entries.Where(e => e.IsFolder))
                entryFilter(folder.Name, true);
        }
        if (files.Count == 0)
            return new TestOutcome(null, 0, NothingRead: true);
        long declared = files.Sum(e => e.Size);
        if (files.Any(e => _listed![(int)e.Index].Encrypted == true))
            return new TestOutcome(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported), declared, false);

        Action<int> onBytes = ContentProgress(progress, declared);
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Test, (_, bytes) => onBytes((int)Math.Min(int.MaxValue, bytes)));
        try
        {
            await RunAsync(ArchiveHelperMode.Test, _charset, [.. files.Select(e => e.Index)], output, cancellationToken).ConfigureAwait(false);
        }
        catch (CoreTextIOException failed)
        {
            return new TestOutcome(failed.Text, declared, false);
        }
        EnsureListingAndReadsAgree(files, output);
        return new TestOutcome(null, declared, false);
    }

    // --- Listing ---

    // T-F204's order: UTF-8 first; names stored in no charset that are not UTF-8 come back NULL
    // with a warning, and the archive is listed again in the page tar.exe reads raw names in.
    // Null: a name no charset reads, so the listing cannot name the entries.
    private async Task<ArchiveHelperOutput?> ListWithCharsetAsync(CancellationToken cancellationToken)
    {
        bool charsetApplies = HelperFormats.TakesHeaderCharset(_formatArguments);
        string? charset = charsetApplies ? TextBoundaries.ArchiveHelperUtf8Names : null;
        ArchiveHelperOutput output = await ListOnceAsync(charset, cancellationToken).ConfigureAwait(false);
        if (charsetApplies && output.Entries.Any(e => e.Name is null))
        {
            if (TextBoundaries.ArchiveHelperRawNames(MachinePages.Current) is not { } raw)
                return null;
            charset = raw.Charset;
            output = await ListOnceAsync(charset, cancellationToken).ConfigureAwait(false);
            // libarchive 3.5.2 decodes a sequence a multi-byte page lacks as U+30FB without a
            // warning (3.8.9 warns): such a name cannot be told from a true one (docs/DECISIONS.md).
            bool silentReplacement = !raw.Boundary.Strict.IsSingleByte
                && output.Version?.StartsWith("libarchive 3.5.", StringComparison.Ordinal) == true
                && output.Entries.Any(e => e.Name?.Contains('\u30FB', StringComparison.Ordinal) == true);
            if (silentReplacement)
                return null;
        }
        if (output.Entries.Any(e => e.Name is null))
            return null;
        _charset = charset;
        _listed = output.Entries;
        return output;
    }

    private async Task<ArchiveHelperOutput> ListOnceAsync(string? charset, CancellationToken cancellationToken)
    {
        var output = new ArchiveHelperOutput(ArchiveHelperMode.List);
        await RunAsync(ArchiveHelperMode.List, charset, null, output, cancellationToken).ConfigureAwait(false);
        return output;
    }

    // The whole archive, checked as tar.exe's scan checks it (T-F49, T-F420): a link or a special
    // entry, an unsafe name, an 8.3 alias of another name refuse it.
    private async Task<List<Entry>> ScanEntriesAsync(bool refuseEncrypted, CancellationToken cancellationToken)
    {
        ArchiveHelperOutput output = await ListWithCharsetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));

        string? unsafeName = FirstUnsafeName([.. output.Entries.Select(e => e.Name!)]);
        if (unsafeName != null)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarUnsafeEntryPath, unsafeName));

        var entries = new List<Entry>(output.Entries.Count);
        foreach (ArchiveHelperEntry e in output.Entries)
        {
            if (Checked(e, refuseEncrypted) is { } entry)
                entries.Add(entry);
        }
        _entries = entries;
        return entries;
    }

    // tar.exe's scan rules (T-F49, T-F420), and a name with an unpaired surrogate: no file name a
    // reader of it can rely on (tar.exe's listing could never carry one).
    internal static string? FirstUnsafeName(string[] rawNames) =>
        rawNames.FirstOrDefault(n => IsDangerousEntryName(n) || HasLoneSurrogate(n)) ?? EntryDiskName.ShortNameAliasOfAnother(rawNames);

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

    // One listed entry as the scan takes it; null for the archive's root itself.
    private static Entry? Checked(ArchiveHelperEntry e, bool refuseEncrypted)
    {
        if (e.Link is not null || e.FileType is not (RegularFile or FolderType))
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarSpecialEntry));
        if (refuseEncrypted && e.Encrypted == true)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported));
        bool isFolder = e.FileType == FolderType;
        string name = EntryDiskName.StripLeadingDotSlash(e.Name!);
        // T-F455: a folder stored without its '/' is still a folder.
        if (isFolder && !name.EndsWith('/'))
            name += "/";
        if (name.TrimEnd('/').Length == 0)
            return null;
        if (!isFolder && e.Size is null)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));
        return new Entry(e.Index, name, isFolder, isFolder ? 0 : e.Size!.Value, e.SparseCount > 0, ModifiedTimeOf(e));
    }

    // ExpandSelection's rule (T-F05) by name: an entry named as requested, and everything below it.
    private static List<Entry> Expand(List<Entry> entries, IReadOnlyList<string> requested)
    {
        var picked = new HashSet<long>();
        foreach (string path in requested)
        {
            string folder = path.TrimEnd('/') + "/";
            foreach (Entry e in entries.Where(e => e.Name.TrimEnd('/') == path.TrimEnd('/') || e.Name.StartsWith(folder, StringComparison.Ordinal)))
                picked.Add(e.Index);
        }
        return [.. entries.Where(e => picked.Contains(e.Index))];
    }

    // --- Runs and their checks ---

    private async Task<ArchiveHelperOutput> ExtractFilesAsync(List<Entry> files, CancellationToken cancellationToken)
    {
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Extract);
        long maxTotal = files.Sum(e => e.Size);
        await RunAsync(ArchiveHelperMode.Extract, _charset, [.. files.Select(e => e.Index)], output, cancellationToken,
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

    private void EnsureListingAndReadsAgree(List<Entry> files, ArchiveHelperOutput output) =>
        EnsureListingAndReadsAgree(_listed ?? throw new InvalidOperationException(ScanFirst),
            [.. files.Select(e => (e.Index, e.Size, e.Sparse))], output);

    private void EnsureOutDirHoldsExactly(List<Entry> files, ArchiveHelperOutput output)
    {
        EnsureListingAndReadsAgree(files, output);
        EnsureOutDirHoldsExactly(_scope.OutputDirectory!, output);
    }

    /// <summary>
    /// A test or an extraction lists the archive again as it reads: the same entries as the scan,
    /// field for field; then K lines for exactly the selection, each a non-sparse entry's declared size.
    /// </summary>
    internal static void EnsureListingAndReadsAgree(
        IReadOnlyList<ArchiveHelperEntry> listed, IReadOnlyList<(long Index, long Size, bool Sparse)> files, ArchiveHelperOutput output)
    {
        if (!output.Entries.SequenceEqual(listed))
            throw new ArchiveHelperProtocolException("a second listing differs from the first");
        if (!output.Read.Select(r => r.Index).SequenceEqual(files.Select(e => e.Index)))
            throw new ArchiveHelperProtocolException("the entries read are not the selection");
        foreach (((long _, long size, bool sparse), (long _, long bytes)) in files.Zip(output.Read))
        {
            if (!sparse && bytes != size)
                throw new ArchiveHelperProtocolException("an entry read other than its declared size");
        }
    }

    /// <summary>out\ holds exactly the files the run read, each named by its index, a plain file of the bytes read with one name and no named stream.</summary>
    internal static void EnsureOutDirHoldsExactly(string outDir, ArchiveHelperOutput output)
    {
        var expected = output.Read.ToDictionary(r => IndexName(r.Index), r => r.Bytes, StringComparer.OrdinalIgnoreCase);
        int found = 0;
        foreach ((string path, bool isFolder) in QuarantineFiles.Walk(outDir))
        {
            string name = QuarantineFiles.Relative(outDir, path);
            FileInfo info = QuarantineFiles.Info(path);
            if (isFolder || !expected.TryGetValue(name, out long bytes) || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length != bytes
                || !QuarantineFiles.IsPlainFile(path))
                throw new ArchiveHelperProtocolException("out\\ holds a file the run did not account for");
            found++;
        }
        if (found != expected.Count)
            throw new ArchiveHelperProtocolException("a file the run read is missing from out\\");
    }

    private static string IndexName(long index) => index.ToString(CultureInfo.InvariantCulture);

    private static DateTime? ModifiedTimeOf(ArchiveHelperEntry e) =>
        e.MtimeSeconds is { } seconds && seconds >= -62135596800L && seconds <= 253402300799L
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.AddTicks(e.MtimeNanoseconds / 100)
            : null;

    // The byte poll names the newest file in out\, an index; the user sees the entry's name.
    private sealed class NamedProgress(IProgress<ProgressReport> inner, Dictionary<string, string> names) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) =>
            inner.Report(new ProgressReport
            {
                Percent = value.Percent,
                BytesTransferred = value.BytesTransferred,
                TotalBytes = value.TotalBytes,
                Phase = value.Phase,
                CurrentFile = value.CurrentFile is { } f && names.TryGetValue(f, out string? n) ? n : null,
            });
    }

    // What an extraction left in out\: the files by index, placed under their entries' names.
    private sealed class HelperQuarantine(string outDir, List<Entry> files, List<Entry> folders) : IQuarantinedEntries
    {
        public int ExpectedMoves => files.Count;

        public IEnumerable<QuarantinedFile> Files() =>
            files.Select(e => new QuarantinedFile(
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
            foreach (Entry folder in folders)
            {
                if (folder.ModifiedUtc is { } utc && Placed(folder.Name, placement.StripRootPrefix) is { } relative)
                    placement.Staging.RecordFolderTime(relative, utc);
            }
        }

        public void CreateFolderEntriesIn(FolderPlacement placement)
        {
            foreach (Entry folder in folders)
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
