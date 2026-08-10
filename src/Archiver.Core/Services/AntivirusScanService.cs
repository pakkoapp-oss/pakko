using System.Buffers;
using System.IO.Compression;
using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IAntivirusScanService"/>
/// <remarks>
/// T-F146. Two independent scan paths, dispatched by ArchiveFormatPolicy.Classify (shared with
/// ExtractionRouter so policy gating can never drift between "extract" and "scan"):
/// <list type="bullet">
/// <item>ZIP: in-process, no disk writes — reads each entry's bytes directly from the trusted
/// System.IO.Compression reader into a rented buffer.</item>
/// <item>tar-family: reuses TarSandboxScope/T-F49's whole-archive pre-scan and extracts into the
/// quarantine "out\" directory exactly as a real Extract would, but STOPS there — no
/// move-to-destination phase ever runs, and the quarantine is always deleted (scope.Dispose()).</item>
/// </list>
/// Never throws to callers — every failure (unsupported/blocked format, no AMSI provider
/// registered, an unreadable/oversized entry, a rejected/unsandboxable tar archive) becomes an
/// Inconclusive finding, never silently dropped and never rendered as Clean.
/// </remarks>
public sealed class AntivirusScanService : IAntivirusScanService
{
    // 256 MiB. T-F151's Phase 0 spike (real IAmsiStream/IAntimalware::Scan COM streaming vs. the
    // existing AmsiScanBuffer path, both against the real registered Defender provider) found
    // the streaming path actually fails above 16-20 MiB on this machine, while the existing
    // AmsiScanBuffer path scanned real on-disk content up to 256 MiB correctly with no error —
    // there is no documented AMSI buffer ceiling, and empirically the simpler existing call is
    // also the one with more headroom. Raised from the original 64 MiB (T-F146) on that basis;
    // see docs/DECISIONS.md's T-F151 entry for the full spike account.
    internal const long MaxScannableEntryBytes = 256L * 1024 * 1024;

    private readonly TarCapabilities _tarCapabilities;
    private readonly GroupPolicyOptions _policy;
    private readonly Func<IAmsiScanner> _scannerFactory;
    private readonly Func<bool> _isProviderRegistered;

    // [SupportedOSPlatform("windows")] here (not on the whole class) — this is the only member
    // that directly references AmsiProviderCheck.IsAnyProviderRegistered, which the BCL's own
    // Microsoft.Win32.Registry annotation makes Windows-only (same reasoning as
    // GroupPolicyService.Load()). The internal test constructor below takes that dependency as a
    // parameter instead, so it and the rest of this class stay unannotated and callable from
    // Archiver.Core.Tests' plain net8.0 TFM without needing its own annotation.
    [SupportedOSPlatform("windows")]
    public AntivirusScanService(TarCapabilities tarCapabilities, GroupPolicyOptions? groupPolicyOptions = null)
        : this(tarCapabilities, groupPolicyOptions, () => new AmsiScanner("Pakko"), AmsiProviderCheck.IsAnyProviderRegistered)
    {
    }

    // T-F146: test-only seam — lets Archiver.Core.Tests exercise the orchestration logic (subset
    // filtering, size-cap skip, provider-empty gate, tar per-file-failure handling) via a
    // hand-rolled FakeAmsiScanner and a deterministic provider-registered flag, without depending
    // on this machine's actual registered AV. No mocking library is used anywhere in this repo
    // (CLAUDE.md) — this mirrors that convention.
    internal AntivirusScanService(
        TarCapabilities tarCapabilities,
        GroupPolicyOptions? groupPolicyOptions,
        Func<IAmsiScanner> scannerFactory,
        Func<bool> isProviderRegistered)
    {
        _tarCapabilities = tarCapabilities;
        _policy = groupPolicyOptions ?? new GroupPolicyOptions();
        _scannerFactory = scannerFactory;
        _isProviderRegistered = isProviderRegistered;
    }

    /// <inheritdoc/>
    public async Task<ThreatScanResult> ScanAsync(
        AntivirusScanOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<ThreatFinding>();
        var classification = ArchiveFormatPolicy.Classify(options.ArchivePaths, _tarCapabilities, _policy);

        foreach (SkippedFile skipped in classification.Unsupported)
        {
            findings.Add(new ThreatFinding
            {
                ArchivePath = skipped.Path,
                Verdict = ThreatVerdict.Inconclusive,
                Reason = skipped.Reason,
            });
        }

        // T-F146 / docs/DECISIONS.md: AmsiScanBuffer alone can't tell "no AV is listening" apart
        // from "AV says clean" — both plausibly return AMSI_RESULT_NOT_DETECTED. This gate is
        // checked once per whole operation (not per archive) and, when it fails, every remaining
        // archive is reported Inconclusive without ever calling AmsiScanBuffer at all.
        int totalArchives = classification.ZipPaths.Count + classification.TarPaths.Count;
        if (totalArchives == 0)
            return BuildResult(findings);

        if (!_isProviderRegistered())
        {
            foreach (string path in classification.ZipPaths.Concat(classification.TarPaths))
            {
                findings.Add(new ThreatFinding
                {
                    ArchivePath = path,
                    Verdict = ThreatVerdict.Inconclusive,
                    Reason = "No antivirus is registered to scan with.",
                });
            }
            return BuildResult(findings);
        }

        // One AMSI session for the whole operation (matches AMSI's own "a session groups related
        // scans" semantics) — never scanned concurrently (advisor: AMSI session thread-safety is
        // undocumented), so archives and their entries are scanned strictly sequentially below.
        // AmsiInitialize/AmsiOpenSession can themselves fail (e.g. the provider unregisters mid-
        // operation, or a policy blocks session creation even though a provider key exists) — this
        // must become Inconclusive for every archive, matching the no-provider-registered branch
        // above, never an unhandled throw out of ScanAsync.
        IAmsiScanner scanner;
        try
        {
            scanner = _scannerFactory();
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            foreach (string path in classification.ZipPaths.Concat(classification.TarPaths))
            {
                findings.Add(new ThreatFinding
                {
                    ArchivePath = path,
                    Verdict = ThreatVerdict.Inconclusive,
                    Reason = $"Could not start an antivirus scan session: {ex.Message}",
                });
            }
            return BuildResult(findings);
        }

        using (scanner)
        {
            // Real per-entry progress, not just per-archive — a single-archive scan (the common
            // case: Explorer single selection, Archive Browser's button) used to report nothing
            // at all until the whole thing finished (archivesCompleted*100/totalArchives with
            // totalArchives==1 is one report, at the very end). Each archive gets its own
            // 1/totalArchives-sized slice of the bar, filled in smoothly as ITS OWN entries are
            // scanned (entriesDone/entriesTotal within that archive) — archivesCompleted (prior
            // fully-finished archives) supplies the base offset so a multi-archive selection's
            // progress never jumps backward between archives.
            int archivesCompleted = 0;
            // T-F151: CurrentFile now reports the entry actually being scanned, not just the
            // archive path — with the size cap raised to 256 MiB, a single entry's own
            // AmsiScanBuffer call can run for several seconds, and the old archive-path-only
            // display looked frozen for that whole span even though ReportProgress WAS being
            // called (at each entry boundary, just always with the same archivePath text).
            void ReportProgress(string archivePath, string? entryPath, int entriesDone, int entriesTotal)
            {
                double archiveFraction = entriesTotal > 0 ? Math.Min(1.0, (double)entriesDone / entriesTotal) : 1.0;
                int percent = (int)((archivesCompleted + archiveFraction) * 100.0 / totalArchives);
                progress?.Report(new ProgressReport
                {
                    Percent = Math.Clamp(percent, 0, 100),
                    CurrentFile = entryPath ?? archivePath,
                });
            }

            foreach (string archivePath in classification.ZipPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ScanZipArchiveAsync(
                    archivePath, options.SelectedEntryPaths, scanner, findings,
                    (entry, done, total) => ReportProgress(archivePath, entry, done, total), cancellationToken)
                    .ConfigureAwait(false);
                archivesCompleted++;
                // Explicit end-of-archive report (not just relying on the last per-entry
                // callback) — locks each archive's slice to its exact boundary regardless of
                // rounding, and covers a zero-entry archive, which never calls the per-entry
                // callback at all.
                progress?.Report(new ProgressReport { Percent = archivesCompleted * 100 / totalArchives, CurrentFile = archivePath });
            }

            foreach (string archivePath in classification.TarPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ScanTarArchiveAsync(
                    archivePath, options.SelectedEntryPaths, scanner, findings,
                    (entry, done, total) => ReportProgress(archivePath, entry, done, total), cancellationToken)
                    .ConfigureAwait(false);
                archivesCompleted++;
                progress?.Report(new ProgressReport { Percent = archivesCompleted * 100 / totalArchives, CurrentFile = archivePath });
            }

            return BuildResult(findings);
        }
    }

    private static ThreatScanResult BuildResult(List<ThreatFinding> findings)
    {
        ThreatVerdict overall = findings switch
        {
            _ when findings.Any(f => f.Verdict == ThreatVerdict.ThreatDetected) => ThreatVerdict.ThreatDetected,
            _ when findings.Any(f => f.Verdict == ThreatVerdict.Inconclusive) => ThreatVerdict.Inconclusive,
            _ => ThreatVerdict.Clean,
        };

        return new ThreatScanResult { OverallVerdict = overall, Findings = findings };
    }

    // ZIP: in-process, no disk writes at all — reads straight from the trusted
    // System.IO.Compression reader, the same one ZipArchiveService itself uses for extraction.
    private static async Task ScanZipArchiveAsync(
        string archivePath,
        IReadOnlyList<string>? selectedEntryPaths,
        IAmsiScanner scanner,
        List<ThreatFinding> findings,
        Action<string?, int, int> reportProgress,
        CancellationToken cancellationToken)
    {
        List<ZipArchiveEntry> fileEntries;
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            findings.Add(new ThreatFinding
            {
                ArchivePath = archivePath,
                Verdict = ThreatVerdict.Inconclusive,
                Reason = $"Could not read archive: {ex.Message}",
            });
            return;
        }

        using (archive)
        {
            var allFileEntries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();

            // Same subset-membership logic ZipArchiveService.ExtractWithSmartFolderingAsync
            // already has — small enough to duplicate rather than share (CLAUDE.md: three similar
            // lines beats a premature abstraction for a genuinely different concern, reading for
            // scan vs. reading for extraction).
            fileEntries = allFileEntries;
            if (selectedEntryPaths is { Count: > 0 })
            {
                var selectedSet = new HashSet<string>(selectedEntryPaths, StringComparer.Ordinal);
                fileEntries = allFileEntries
                    .Where(e => selectedSet.Contains(e.FullName)
                             || selectedSet.Any(s => e.FullName.StartsWith(s + "/", StringComparison.Ordinal)))
                    .ToList();
            }

            int entriesDone = 0;
            foreach (ZipArchiveEntry entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // T-F151: reported BEFORE scanning starts, not just after — AmsiScanBuffer is one
                // atomic call with no mid-call progress callback, so a large entry (up to the new
                // 256 MiB cap) would otherwise leave the bar sitting on the PRIOR entry's
                // percentage for the whole duration of its own scan. This at least advances the
                // bar (and CurrentFile) to this entry's own starting position immediately, instead
                // of only at the end of a potentially multi-second single scan.
                reportProgress(entry.FullName, entriesDone, fileEntries.Count);
                try
                {
                    ThreatFinding finding = await ScanOneEntryAsync(
                        archivePath, entry.FullName, entry.Length, entry.Open, scanner, cancellationToken)
                        .ConfigureAwait(false);
                    findings.Add(finding);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    findings.Add(new ThreatFinding
                    {
                        ArchivePath = archivePath,
                        EntryPath = entry.FullName,
                        Verdict = ThreatVerdict.Inconclusive,
                        Reason = $"Could not read entry: {ex.Message}",
                    });
                }
                finally
                {
                    reportProgress(entry.FullName, ++entriesDone, fileEntries.Count);
                }
            }
        }
    }

    // Shared by both scan paths — reads up to `length` bytes from a freshly-opened stream into a
    // rented buffer and hands the whole thing to AMSI in one call. Extracted out of
    // ScanZipArchiveAsync/ScanTarArchiveAsync (previously duplicated inline in both) to keep each
    // caller's own per-entry loop to a single try/catch/finally — exception handling stays in the
    // callers since each catches a different set of exception types with a different message.
    private static async Task<ThreatFinding> ScanOneEntryAsync(
        string archivePath, string entryPath, long length, Func<Stream> openStream,
        IAmsiScanner scanner, CancellationToken cancellationToken)
    {
        if (length > MaxScannableEntryBytes)
        {
            return new ThreatFinding
            {
                ArchivePath = archivePath,
                EntryPath = entryPath,
                Verdict = ThreatVerdict.Inconclusive,
                Reason = $"Entry is larger than {MaxScannableEntryBytes / (1024 * 1024)} MiB and was not scanned.",
            };
        }

        int intLength = (int)length;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(intLength, 1));
        try
        {
            using Stream stream = openStream();
            int totalRead = 0;
            while (totalRead < intLength)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead, intLength - totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }

            (ThreatVerdict verdict, string? threatName) = scanner.ScanBuffer(buffer, totalRead, entryPath);
            return new ThreatFinding
            {
                ArchivePath = archivePath,
                EntryPath = entryPath,
                Verdict = verdict,
                ThreatName = threatName,
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Tar-family: reuses T-F49/T-F52's exact quarantine machinery. Extracts into
    // scope.OutputDirectory exactly as a real Extract would, then stops — no move-to-destination
    // phase runs, and using(scope) guarantees the quarantine is deleted whether the scan finds a
    // threat, comes back clean, or fails partway through.
    private static async Task ScanTarArchiveAsync(
        string archivePath,
        IReadOnlyList<string>? selectedEntryPaths,
        IAmsiScanner scanner,
        List<ThreatFinding> findings,
        Action<string?, int, int> reportProgress,
        CancellationToken cancellationToken)
    {
        TarSandboxScope? scope = null;
        try
        {
            scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, cancellationToken)
                .ConfigureAwait(false);

            (_, string[] allNames, _) = await TarSandboxedService.ScanForUnsafeEntriesAsync(scope, cancellationToken)
                .ConfigureAwait(false);

            bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
            List<string>? expandedSelection = isSelectedSubset
                ? TarSandboxedService.ExpandSelection(allNames, selectedEntryPaths!)
                : null;

            // Same T-F142 "expandedSelection-vs-whole-archive" total-entry computation
            // ExtractSingleArchiveAsync already derives for its own move-phase progress — reused
            // here, not re-listed via a second tar.exe call, so real per-entry progress costs
            // nothing extra beyond what the pre-scan above already fetched.
            int totalEntries = expandedSelection != null
                ? expandedSelection.Count(n => !n.EndsWith('/'))
                : allNames.Count(n => !n.EndsWith('/'));

            PreCreateOutputDirectories(allNames, scope.OutputDirectory!);

            var tarArgs = new List<string> { "-xf", scope.StagedArchivePath, "-C", scope.OutputDirectory! };
            if (expandedSelection != null)
                tarArgs.AddRange(expandedSelection);

            (int exitCode, _, string stdErr) = await scope.RunAsync(tarArgs, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                findings.Add(new ThreatFinding
                {
                    ArchivePath = archivePath,
                    Verdict = ThreatVerdict.Inconclusive,
                    Reason = $"Could not extract archive for scanning: {stdErr.Trim()}",
                });
                return;
            }

            await ScanExtractedFilesAsync(
                scope.OutputDirectory!, archivePath, totalEntries, scanner, findings, reportProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is TarSandboxedService.TarArchiveRejectedException
                                        or TarSignatureVerificationException
                                        or SandboxSetupException
                                        or IOException)
        {
            findings.Add(new ThreatFinding
            {
                ArchivePath = archivePath,
                Verdict = ThreatVerdict.Inconclusive,
                Reason = ex.Message,
            });
        }
        finally
        {
            scope?.Dispose();
        }
    }

    // T-F52: pre-create every directory the archive implies, at Pakko's own (unsandboxed)
    // identity, before tar.exe ever runs — same libarchive-under-AppContainer workaround
    // ExtractSingleArchiveAsync already relies on.
    private static void PreCreateOutputDirectories(string[] allNames, string outputDirectory)
    {
        foreach (string name in allNames)
        {
            string? relativeDir = name.EndsWith('/') ? name.TrimEnd('/') : Path.GetDirectoryName(name);
            if (!string.IsNullOrEmpty(relativeDir))
                Directory.CreateDirectory(Path.Combine(outputDirectory, relativeDir));
        }
    }

    private static async Task ScanExtractedFilesAsync(
        string outputDirectory, string archivePath, int totalEntries, IAmsiScanner scanner,
        List<ThreatFinding> findings, Action<string?, int, int> reportProgress, CancellationToken cancellationToken)
    {
        int entriesDone = 0;
        foreach (string file in TarSandboxedService.EnumerateFilesGuarded(outputDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(outputDirectory, file).Replace('\\', '/');
            // T-F151: see the identical call in ScanZipArchiveAsync — advances the bar (and
            // CurrentFile) to this entry's own starting position before a potentially multi-second
            // single AmsiScanBuffer call, rather than leaving it frozen on the previous entry's
            // percentage throughout.
            reportProgress(relativePath, entriesDone, totalEntries);
            try
            {
                long length = new FileInfo(file).Length;
                ThreatFinding finding = await ScanOneEntryAsync(
                    archivePath, relativePath, length,
                    () => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read),
                    scanner, cancellationToken)
                    .ConfigureAwait(false);
                findings.Add(finding);
            }
            catch (OperationCanceledException) { throw; }
            // Phase 0 spike (docs/DECISIONS.md's T-F146 entry): Defender's real-time on-access
            // scanner can independently remove/lock a file inside the quarantine "out\" directory
            // between EnumerateFilesGuarded listing it and this code reading it — that is a real,
            // observed race, not a hypothetical one, and must never crash the whole scan or
            // silently drop the entry.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                findings.Add(new ThreatFinding
                {
                    ArchivePath = archivePath,
                    EntryPath = relativePath,
                    Verdict = ThreatVerdict.Inconclusive,
                    Reason = "Removed or blocked before Pakko could scan it directly.",
                });
            }
            finally
            {
                // Fires exactly once per file regardless of which branch above ran (oversized
                // skip inside ScanOneEntryAsync, a clean scan, or the vanished/blocked catch
                // above) — a single point to advance real per-entry progress.
                reportProgress(relativePath, ++entriesDone, totalEntries);
            }
        }
    }
}
