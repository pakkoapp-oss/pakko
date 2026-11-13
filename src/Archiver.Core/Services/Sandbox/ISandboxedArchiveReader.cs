using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F470: the one handler through which every list, extract, test and antivirus scan of a
/// tar-family archive reads it. One session reads one archive for one operation, inside its own
/// sandbox, in the order <see cref="ScanAsync"/>, a select, an extract. The callers - placement,
/// the compression-bomb gate, staging and commit, MOTW, the antivirus scan of the files - never
/// see how the backend names or writes entries.
/// </summary>
internal interface ISandboxedArchiveReader : IDisposable
{
    /// <summary>
    /// Opens the backend this process reads with, on the archive: the archive helper when
    /// <paramref name="helper"/> is given, or <paramref name="gate"/> chose it, and it reads the
    /// archive's format; tar.exe otherwise. The one place the gate's decision is asked for.
    /// T-F524: an extraction passes its <paramref name="destination"/>, and its quarantine goes next to
    /// it where <see cref="QuarantineLocation"/> allows.
    /// </summary>
    static async Task<ISandboxedArchiveReader> OpenAsync(
        string archivePath, bool needsOutputDir, Encoding? outputEncoding, CancellationToken cancellationToken,
        HelperSetup? helper = null, ArchiveintGate? gate = null, string? destination = null)
    {
        if (helper is null && gate is not null)
            helper = await gate.HelperAsync(cancellationToken).ConfigureAwait(false);
        string? nearFolder = destination is null || !OperatingSystem.IsWindows() || QuarantineLocation.VolumesKeptInTemp is not { } keptInTemp
            ? null
            : QuarantineLocation.NearDestination(destination, byHelper: helper is not null, new Win32RegistryReader(), keptInTemp);
        return helper is null
            ? await TarExeReader.OpenAsync(archivePath, needsOutputDir, outputEncoding, cancellationToken, nearFolder).ConfigureAwait(false)
            : await HelperReader.OpenAsync(archivePath, needsOutputDir, outputEncoding, helper, cancellationToken, nearFolder).ConfigureAwait(false);
    }

    /// <summary>The entries for browsing: an unsafe name or a link lists, as before; only extraction refuses it.</summary>
    Task<ArchiveListResult> ListAsync(CancellationToken cancellationToken);

    /// <summary>The whole archive, checked: throws <see cref="TarSandboxedService.TarArchiveRejectedException"/> on an unsafe entry.</summary>
    Task<ReaderCatalog> ScanAsync(CancellationToken cancellationToken);

    /// <summary>Resolves a selection and a filter against the scanned entries, for <see cref="ExtractAsync"/>.</summary>
    Task<SelectionOutcome> SelectForExtractAsync(
        IReadOnlyList<string>? selectedEntryPaths, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken);

    /// <summary>Extracts the selection into the quarantine; copies not extracted are reported into <paramref name="skippedFiles"/>.</summary>
    Task<IQuarantinedEntries> ExtractAsync(ExtractionPoll? poll, List<SkippedFile> skippedFiles, CancellationToken cancellationToken);

    /// <summary>Extracts a selection (or everything) into the quarantine for the antivirus scan.</summary>
    Task<ScanQuarantine> ExtractForScanAsync(IReadOnlyList<string>? selectedEntryPaths, CancellationToken cancellationToken);

    /// <summary>After the main pass is scanned: the first of several same-named copies, for the scan too (T-F171).</summary>
    Task<FirstCopiesForScan> ExtractFirstCopiesForScanAsync(CancellationToken cancellationToken);

    /// <summary>Scans, then reads every entry the filter passes without writing it.</summary>
    Task<TestOutcome> TestAsync(IProgress<ProgressReport>? progress, Func<string, bool, bool>? entryFilter, CancellationToken cancellationToken);
}

/// <summary>T-F470: the archive helper a process reads with, and its probe's <c>support_*</c> results by name.</summary>
internal sealed record HelperSetup(string HelperPath, IReadOnlyDictionary<string, int?> Capabilities);

/// <summary>What placement and the compression-bomb gate need from a scan.</summary>
/// <param name="TotalDeclaredSize">Every file entry's declared size (T-F94).</param>
/// <param name="RootNames">Entry names as they land, folders included, for the root-shape decision.</param>
/// <param name="FileEntryNames">Every file entry's name, repeats included, for the trimmed twins (T-F419).</param>
internal sealed record ReaderCatalog(long TotalDeclaredSize, IReadOnlyList<string> RootNames, IReadOnlyList<string> FileEntryNames);

internal enum SelectionKind
{
    /// <summary>Entries to extract.</summary>
    Entries,

    /// <summary>A selection naming no entry: nothing is extracted, as in ZIP.</summary>
    EmptySelection,

    /// <summary>A filter that passed no entry (T-F410).</summary>
    NothingPassesFilter,
}

/// <summary>A resolved selection: its declared bytes and file count drive progress (T-F142).</summary>
internal sealed record SelectionOutcome(SelectionKind Kind, long DeclaredBytes, int FileCount);

/// <summary>Byte progress while the backend writes the quarantine (T-F142).</summary>
internal sealed record ExtractionPoll(IProgress<ProgressReport> Progress, long TotalBytes, int ExtractionEnd);

/// <summary>One quarantined file and the path it lands at, relative to the archive root, before any root strip.</summary>
/// <param name="Path">The file in the quarantine.</param>
/// <param name="RelativePath">Its path below the archive root, OS separators.</param>
/// <param name="Renamed">Its entry's name is one Windows writes as another (EntryDiskName).</param>
/// <param name="Trimmed">Its entry's name is one Windows trims (EntryDiskName).</param>
internal sealed record QuarantinedFile(string Path, string RelativePath, bool Renamed, bool Trimmed);

/// <summary>Where folder entries go: the staging folder, the destination it stands for, and the root strip.</summary>
internal sealed record FolderPlacement(ExtractionStaging Staging, string ActualDest, bool StripRootPrefix);

/// <summary>What an extraction left in the quarantine.</summary>
internal interface IQuarantinedEntries
{
    /// <summary>How many files <see cref="Files"/> yields at most, for the move phase's progress.</summary>
    int ExpectedMoves { get; }

    /// <summary>The files to move out, in the order they claim names (first copies first, T-F171).</summary>
    IEnumerable<QuarantinedFile> Files();

    /// <summary>T-F462: throws when a file entry reached neither the quarantine nor a report.</summary>
    void EnsureNoneMissing();

    /// <summary>T-F298: records each folder entry's time before the move phase changes it.</summary>
    void RecordFolderTimesInto(FolderPlacement placement);

    /// <summary>T-F197: creates the folder entries in the staging folder after the files moved.</summary>
    void CreateFolderEntriesIn(FolderPlacement placement);
}

/// <summary>What the main pass of an extraction for the antivirus scan left in the quarantine.</summary>
/// <param name="Failure">Why the main pass failed, or null.</param>
/// <param name="Files">The main pass's files and the entry names they are scanned under.</param>
/// <param name="TotalEntries">Files to scan, first copies included, for progress.</param>
/// <param name="ExpectedFiles">Files the scan must account for (T-F462).</param>
/// <param name="HasFirstCopies">Same-named copies wait for <see cref="ISandboxedArchiveReader.ExtractFirstCopiesForScanAsync"/>.</param>
internal sealed record ScanQuarantine(CoreText? Failure, IReadOnlyList<ScanFile> Files, int TotalEntries, int ExpectedFiles, bool HasFirstCopies);

/// <summary>A quarantined file for the antivirus scan, and the entry name a finding names (T-F470: not the file's own name).</summary>
internal sealed record ScanFile(string Path, string Name);

/// <summary>The first-copy pass for the antivirus scan (T-F171).</summary>
/// <param name="Files">Its files and the entry names they are scanned under.</param>
/// <param name="Failure">Why it failed, or null.</param>
/// <param name="CreditedCopies">After the scan: the first copies that may count beside the main pass's files (T-F462).</param>
internal sealed record FirstCopiesForScan(IReadOnlyList<ScanFile> Files, CoreText? Failure, Func<int> CreditedCopies);

/// <summary>A test's result: why it failed, or null; the bytes read; whether the filter passed nothing (T-F410).</summary>
internal sealed record TestOutcome(CoreText? Failure, long DeclaredSize, bool NothingRead);
