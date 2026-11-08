using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;

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
    // also the one with more headroom. Raised from the original 64 MiB per T-F146 on that
    // basis. See docs/DECISIONS.md's T-F151 entry for the full spike account.
    internal const long MaxScannableEntryBytes = 256L * 1024 * 1024;

    private readonly Func<Task<TarCapabilities>> _tarCapabilities;
    private readonly GroupPolicyOptions _policy;
    private readonly Func<IAmsiScanner> _scannerFactory;
    private readonly Func<AmsiProviderState> _providerState;

    // [SupportedOSPlatform("windows")] here (not on the whole class) — this is the only member
    // that directly references AmsiProviderCheck.GetState, which the BCL's own
    // Microsoft.Win32.Registry annotation makes Windows-only (same reasoning as
    // GroupPolicyService.Load()). The internal test constructor below takes that dependency as a
    // parameter instead, so it and the rest of this class stay unannotated and callable from
    // Archiver.Core.Tests' plain net8.0 TFM without needing its own annotation.
    /// <summary>Creates a scanner wired to the real AMSI provider.</summary>
    [SupportedOSPlatform("windows")]
    public AntivirusScanService(TarCapabilities tarCapabilities, GroupPolicyOptions groupPolicyOptions)
        : this(() => Task.FromResult(tarCapabilities), groupPolicyOptions)
    {
    }

    /// <summary>Creates a scanner wired to the real AMSI provider, reading through the process's <paramref name="gate"/> (T-F470).</summary>
    [SupportedOSPlatform("windows")]
    public AntivirusScanService(TarCapabilities tarCapabilities, GroupPolicyOptions groupPolicyOptions, Sandbox.ArchiveintGate gate)
        : this(() => Task.FromResult(tarCapabilities), groupPolicyOptions, gate)
    {
    }

    // T-F350: PakkoServices' entry - the one place the real AMSI provider is wired in.
    [SupportedOSPlatform("windows")]
    internal AntivirusScanService(Func<Task<TarCapabilities>> tarCapabilities, GroupPolicyOptions groupPolicyOptions, Sandbox.ArchiveintGate? gate = null)
        : this(tarCapabilities, groupPolicyOptions, () => new AmsiScanner("Pakko"), AmsiProviderCheck.GetState, gate)
    {
    }

    // T-F146: test-only seam — lets Archiver.Core.Tests exercise the orchestration logic (subset
    // filtering, size-cap skip, provider-empty gate, tar per-file-failure handling) via a
    // hand-rolled FakeAmsiScanner and a deterministic provider-registered flag, without depending
    // on this machine's actual registered AV. No mocking library is used anywhere in this repo
    // (CLAUDE.md) — this mirrors that convention.
    internal AntivirusScanService(
        TarCapabilities tarCapabilities,
        GroupPolicyOptions groupPolicyOptions,
        Func<IAmsiScanner> scannerFactory,
        Func<AmsiProviderState> providerState)
        : this(() => Task.FromResult(tarCapabilities), groupPolicyOptions, scannerFactory, providerState)
    {
    }

    // T-F350: the capabilities are asked for only when a selection holds a tar-family archive.
    internal AntivirusScanService(
        Func<Task<TarCapabilities>> tarCapabilities,
        GroupPolicyOptions groupPolicyOptions,
        Func<IAmsiScanner> scannerFactory,
        Func<AmsiProviderState> providerState,
        Sandbox.ArchiveintGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(groupPolicyOptions);
        _gate = gate;
        _tarCapabilities = tarCapabilities;
        _policy = groupPolicyOptions;
        _scannerFactory = scannerFactory;
        _providerState = providerState;
    }

    // T-F234: see ZipArchiveService.NameCodePages — scan reports the same names extraction writes.
    internal ZipNameCodePages NameCodePages { get; init; } = ZipNameCodePages.Current();

    // T-F465: see TarSandboxedService.ForcedTarOutputEncoding.
    internal System.Text.Encoding? ForcedTarOutputEncoding { get; init; }

    // T-F470: the archive helper this service reads with (tests); null leaves the choice to the gate.
    internal Sandbox.HelperSetup? Helper { get; init; }

    // T-F470: the process's one gate; null reads with tar.exe.
    private readonly Sandbox.ArchiveintGate? _gate;

    // T-F406: a path that cannot be opened at all is reported, not handed to a reader.
    private static List<string> TakeUsablePaths(IReadOnlyList<string> paths, List<ThreatFinding> findings)
    {
        var usable = new List<string>();
        foreach (string path in paths)
        {
            if (InputPaths.Problem(path) is { } pathProblem)
                findings.Add(CoreMessages.Inconclusive(path, null, pathProblem));
            else
                usable.Add(path);
        }
        return usable;
    }

    /// <inheritdoc/>
    public async Task<ThreatScanResult> ScanAsync(
        AntivirusScanOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F449: no archive given is nothing scanned, never "nothing found".
        if (options.ArchivePaths.Count == 0)
            return new ThreatScanResult { OverallVerdict = ThreatVerdict.Inconclusive };

        var findings = new List<ThreatFinding>();
        List<string> usable = TakeUsablePaths(options.ArchivePaths, findings);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(usable, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);

        // T-F250: scan reads ZIPs itself, not through ZipArchiveService, so a blocked "zip" is
        // refused here too — the ZIP bucket also holds Unknown paths, which include ZIPs the
        // magic-byte detector does not recognize (an entry-less archive, a self-extractor).
        if (ArchiveFormatPolicy.IsBlockedByPolicy(ArchiveFormat.Zip, _policy))
        {
            CoreText reason = ArchiveFormatPolicy.BlockedFormatReason(ArchiveFormat.Zip);
            classification = classification with
            {
                ZipPaths = [],
                Unsupported = [.. classification.Unsupported, .. classification.ZipPaths.Select(path => CoreMessages.Skip(path, reason))],
            };
        }

        foreach (SkippedFile skipped in classification.Unsupported)
        {
            findings.Add(CoreMessages.Inconclusive(skipped.Path, null, skipped.Text ?? CoreText.Raw(skipped.Reason)));
        }

        // T-F146 / docs/DECISIONS.md: AmsiScanBuffer alone can't tell "no AV is listening" apart
        // from "AV says clean" — both plausibly return AMSI_RESULT_NOT_DETECTED. This gate is
        // checked once per whole operation (not per archive) and, when it fails, every remaining
        // archive is reported Inconclusive without ever calling AmsiScanBuffer at all.
        int totalArchives = classification.ZipPaths.Count + classification.TarPaths.Count;
        if (totalArchives == 0)
            return BuildResult(findings);

        // T-F448: the same holds when the only providers are ones measured to answer Clean for
        // known test threats - their Clean is no verdict, so nothing is scanned.
        if (ProviderRefusal(_providerState()) is { } providerReason)
        {
            foreach (string path in classification.ZipPaths.Concat(classification.TarPaths))
            {
                findings.Add(CoreMessages.Inconclusive(path, null, CoreMessages.Text(providerReason)));
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
                findings.Add(CoreMessages.Inconclusive(path, null, CoreMessages.Text(MessageCode.ScanSessionFailed, CoreMessages.Detail(ex))));
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

            // T-F194: one instance for the whole call, same as ExtractAsync — an "apply to
            // remaining" answer spans every archive in this multi-select.
            var passwordResolver = new PasswordResolver(options.ResolvePasswordAsync, maxAttempts: 3);

            foreach (string archivePath in classification.ZipPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ScanZipArchiveAsync(
                    archivePath, options.SelectedEntryPaths, scanner, new ZipReadSettings(passwordResolver, NameCodePages), findings,
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
                    (entry, done, total) => ReportProgress(archivePath, entry, done, total), (ForcedTarOutputEncoding, Helper, _gate), cancellationToken)
                    .ConfigureAwait(false);
                archivesCompleted++;
                progress?.Report(new ProgressReport { Percent = archivesCompleted * 100 / totalArchives, CurrentFile = archivePath });
            }

            return BuildResult(findings);
        }
    }

    // Any state other than Usable is no verdict to scan with; one added later reads the same way.
    private static MessageCode? ProviderRefusal(AmsiProviderState state) => state switch
    {
        AmsiProviderState.Usable => null,
        AmsiProviderState.OnlyNonReporting => MessageCode.ScanProviderDoesNotReport,
        _ => MessageCode.NoAntivirusRegistered,
    };

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

    // How a ZIP's names and passwords are read — one value for the whole scan call.
    private sealed record ZipReadSettings(PasswordResolver PasswordResolver, ZipNameCodePages CodePages);

    // ZIP: in-process, no disk writes at all — reads straight from the trusted
    // System.IO.Compression reader, the same one ZipArchiveService itself uses for extraction.
    private static async Task ScanZipArchiveAsync(
        string archivePath,
        IReadOnlyList<string>? selectedEntryPaths,
        IAmsiScanner scanner,
        ZipReadSettings settings,
        List<ThreatFinding> findings,
        Action<string?, int, int> reportProgress,
        CancellationToken cancellationToken)
    {
        (PasswordResolver passwordResolver, ZipNameCodePages codePages) = settings;
        List<NamedZipEntry> fileEntries;
        ZipArchiveReader reader;
        try
        {
            reader = ZipArchiveReader.Open(archivePath, codePages);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            findings.Add(CoreMessages.Inconclusive(archivePath, null, CoreMessages.Wrap(MessageCode.ScanCannotReadArchive, ex)));
            return;
        }

        using (reader)
        {
            ZipArchive archive = reader.Archive;
            // T-F194: an encrypted entry used to reach entry.Open() and fail as "Could not read
            // entry" — never scanned at all, which is exactly how password-protected malware
            // delivery evades AV. Now its decrypted plaintext is scanned when a password resolves.
            using Stream? rawArchiveStream = TryMapEncryptedEntries(archivePath, archive, out Dictionary<ZipArchiveEntry, LocatedZipEntry>? encryptedEntryMap);
            ZipArchiveService.ResolvedZipPassword? password = encryptedEntryMap is null
                ? null
                : await ZipArchiveService.ResolveArchivePasswordAsync(archivePath, passwordResolver, codePages).ConfigureAwait(false);

            var allFileEntries = reader.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();

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
            foreach (NamedZipEntry named in fileEntries)
            {
                ZipArchiveEntry entry = named.Entry;
                cancellationToken.ThrowIfCancellationRequested();
                // T-F151: reported BEFORE scanning starts, not just after — AmsiScanBuffer is one
                // atomic call with no mid-call progress callback, so a large entry (up to the new
                // 256 MiB cap) would otherwise leave the bar sitting on the PRIOR entry's
                // percentage for the whole duration of its own scan. This at least advances the
                // bar (and CurrentFile) to this entry's own starting position immediately, instead
                // of only at the end of a potentially multi-second single scan.
                reportProgress(named.FullName, entriesDone, fileEntries.Count);
                try
                {
                    ThreatFinding finding = encryptedEntryMap is { } map
                                            && map.TryGetValue(entry, out LocatedZipEntry? located)
                                            && located.GeneralPurposeEncryptedBit
                        ? await ScanEncryptedEntryAsync(
                            archivePath, named, located, rawArchiveStream!, password, scanner, cancellationToken)
                            .ConfigureAwait(false)
                        : await ScanOneEntryAsync(
                            archivePath, named.FullName, entry.Length, entry.Open, scanner, cancellationToken)
                            .ConfigureAwait(false);
                    findings.Add(finding);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    findings.Add(CoreMessages.Inconclusive(archivePath, named.FullName, CoreMessages.Wrap(MessageCode.ScanCannotReadEntry, ex)));
                }
                finally
                {
                    reportProgress(named.FullName, ++entriesDone, fileEntries.Count);
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
        IAmsiScanner scanner, CancellationToken cancellationToken, bool verifyIntegrityAfterScan = false)
    {
        if (length > MaxScannableEntryBytes)
            return OversizedFinding(archivePath, entryPath);

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

            // T-F247: an empty entry holds nothing to scan, and AmsiScanBuffer rejects a
            // zero-length buffer (E_INVALIDARG). A failing AMSI call is this entry's result
            // (Inconclusive), never an exception out of the whole scan.
            ThreatVerdict verdict;
            string? threatName;
            if (intLength == 0)
            {
                (verdict, threatName) = (ThreatVerdict.Clean, null);
            }
            else
            {
                try
                {
                    (verdict, threatName) = scanner.ScanBuffer(buffer, totalRead, entryPath);
                }
                catch (InvalidOperationException ex)
                {
                    return CoreMessages.Inconclusive(archivePath, entryPath, CoreMessages.Text(MessageCode.ScanEntryFailed, CoreMessages.Detail(ex)));
                }
            }

            // T-F194 (advisor-caught): ZipCrypto's one-byte password check accepts ~1 in 256 wrong
            // passwords, decrypting to garbage AMSI would happily call Clean. The trailer CRC-32
            // only fires once the stream is read to its end — stopping at exactly `length` bytes
            // never reaches it. One more read either hits end-of-stream (running that check) or
            // finds content past the declared length; both failures mean the bytes scanned are
            // not trustworthy. A real detection stands regardless.
            // T-F446: a policy block gives way to it too - garbage a policy blocks is still garbage.
            if (verifyIntegrityAfterScan && verdict != ThreatVerdict.ThreatDetected
                && !await ReachesVerifiedEndAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                return CoreMessages.Inconclusive(archivePath, entryPath, CoreMessages.Text(MessageCode.ScanEntryIntegrityFailed));
            }

            // T-F446: the scanner's only Inconclusive (IAmsiScanner): an administrator policy blocked it.
            if (verdict == ThreatVerdict.Inconclusive)
                return CoreMessages.Inconclusive(archivePath, entryPath, CoreMessages.Text(MessageCode.ScanEntryBlockedByPolicy));

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

    private static async Task<bool> ReachesVerifiedEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            byte[] probe = new byte[1];
            return await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) == 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // T-F194: positional pairing of archive.Entries with RawZipEntryLocator.LocateAll, the same way
    // ZipArchiveService.TestArchiveEntries does it. Returns null (and no map) for a plain archive —
    // zero extra parsing — or when the raw layout can't be read (Zip64/odd structure), in which
    // case encrypted entries fall back to the pre-T-F194 "Could not read entry" Inconclusive.
    private static FileStream? TryMapEncryptedEntries(
        string archivePath, ZipArchive archive, out Dictionary<ZipArchiveEntry, LocatedZipEntry>? map)
    {
        map = null;
        if (!ZipArchiveService.IsEncryptedZip(archivePath))
            return null;

        FileStream? raw = null;
        try
        {
            raw = File.OpenRead(archivePath);
            List<LocatedZipEntry> located = RawZipEntryLocator.LocateAll(raw);
            if (located.Count != archive.Entries.Count)
            {
                raw.Dispose();
                return null;
            }

            map = new Dictionary<ZipArchiveEntry, LocatedZipEntry>(archive.Entries.Count);
            for (int i = 0; i < archive.Entries.Count; i++)
                map[archive.Entries[i]] = located[i];
            return raw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            raw?.Dispose();
            map = null;
            return null;
        }
    }

    private static async Task<ThreatFinding> ScanEncryptedEntryAsync(
        string archivePath, NamedZipEntry named, LocatedZipEntry located, Stream rawArchiveStream,
        ZipArchiveService.ResolvedZipPassword? password, IAmsiScanner scanner, CancellationToken cancellationToken)
    {
        string entryName = named.FullName;
        ZipArchiveEntry entry = named.Entry;
        if (password is null)
            return CoreMessages.Inconclusive(archivePath, entryName, CoreMessages.Text(MessageCode.ScanEntryPasswordProtected));

        // The size cap below (inside ScanOneEntryAsync) checks entry.Length from the central
        // directory, but TryOpen buffers the whole ciphertext sized by the LOCAL header — an
        // independently attacker-controlled field — so it needs its own cap before any allocation.
        if (located.CompressedSize > MaxScannableEntryBytes || entry.Length > MaxScannableEntryBytes)
            return OversizedFinding(archivePath, entryName);

        (EncryptedZipReadResult result, Stream? stream) = EncryptedZipEntryReader.TryOpen(rawArchiveStream, located, password.Text, password.Encoding);
        return result switch
        {
            EncryptedZipReadResult.Success => await ScanOneEntryAsync(
                archivePath, entryName, entry.Length, () => stream!, scanner, cancellationToken,
                verifyIntegrityAfterScan: true).ConfigureAwait(false),
            EncryptedZipReadResult.WrongPassword =>
                CoreMessages.Inconclusive(archivePath, entryName, CoreMessages.Text(MessageCode.ScanEntryWrongPassword)),
            EncryptedZipReadResult.UnsupportedCompressionMethod =>
                CoreMessages.Inconclusive(archivePath, entryName, CoreMessages.Text(MessageCode.ScanEntryUnsupportedMethod)),
            _ => CoreMessages.Inconclusive(archivePath, entryName, CoreMessages.Text(MessageCode.ScanEntryAuthenticationFailed)),
        };
    }

    private static ThreatFinding OversizedFinding(string archivePath, string entryPath) =>
        CoreMessages.Inconclusive(archivePath, entryPath,
            CoreMessages.Text(MessageCode.ScanEntryTooLarge, (MaxScannableEntryBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)));

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
        (System.Text.Encoding? Encoding, HelperSetup? Helper, ArchiveintGate? Gate) readWith,
        CancellationToken cancellationToken)
    {
        ISandboxedArchiveReader? reader = null;
        try
        {
            reader = await ISandboxedArchiveReader.OpenAsync(archivePath, needsOutputDir: true, readWith.Encoding, cancellationToken, readWith.Helper, readWith.Gate)
                .ConfigureAwait(false);
            _ = await reader.ScanAsync(cancellationToken).ConfigureAwait(false);

            ScanQuarantine quarantine = await reader.ExtractForScanAsync(selectedEntryPaths, cancellationToken).ConfigureAwait(false);
            if (quarantine.Failure is { } failure)
            {
                findings.Add(CoreMessages.Inconclusive(archivePath, null, CoreMessages.Text(MessageCode.ScanCannotExtract, failure)));
                return;
            }

            var target = new TarScanTarget(archivePath, scanner, findings, reportProgress);
            int scanned = await ScanExtractedFilesAsync(
                quarantine.Files, 0, quarantine.TotalEntries, target, cancellationToken)
                .ConfigureAwait(false);
            int credited = scanned;
            if (quarantine.HasFirstCopies)
            {
                FirstCopiesForScan firstCopies = await reader.ExtractFirstCopiesForScanAsync(cancellationToken).ConfigureAwait(false);
                if (firstCopies.Failure is { } firstFailure)
                {
                    findings.Add(CoreMessages.Inconclusive(archivePath, null, CoreMessages.Text(MessageCode.ScanCannotExtract, firstFailure)));
                    return;
                }
                _ = await ScanExtractedFilesAsync(
                    firstCopies.Files, scanned, quarantine.TotalEntries, target, cancellationToken)
                    .ConfigureAwait(false);
                credited = scanned + firstCopies.CreditedCopies();
            }

            // T-F462: Clean only with every file accounted for, whichever name or folder broke (the
            // middle of three same-named copies, a folder Windows trims, a pass that failed).
            if (credited < quarantine.ExpectedFiles)
                findings.Add(CoreMessages.Inconclusive(archivePath, null, CoreMessages.Text(MessageCode.ScanFilesMissing,
                    credited.ToString(CultureInfo.CurrentCulture), quarantine.ExpectedFiles.ToString(CultureInfo.CurrentCulture))));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is TarSandboxedService.TarArchiveRejectedException
                                        or TarSignatureVerificationException
                                        or SandboxSetupException
                                        or IOException)
        {
            findings.Add(CoreMessages.Inconclusive(archivePath, null, CoreMessages.FromException(ex)));
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private sealed record TarScanTarget(
        string ArchivePath, IAmsiScanner Scanner, List<ThreatFinding> Findings, Action<string?, int, int> ReportProgress);

    // Returns the running count of scanned files; startAt continues it across a second folder.
    private static async Task<int> ScanExtractedFilesAsync(
        IReadOnlyList<ScanFile> files, int startAt, int totalEntries, TarScanTarget target, CancellationToken cancellationToken)
    {
        (string archivePath, IAmsiScanner scanner, List<ThreatFinding> findings, Action<string?, int, int> reportProgress) = target;
        int entriesDone = startAt;
        foreach ((string file, string relativePath) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // T-F151: see the identical call in ScanZipArchiveAsync — advances the bar (and
            // CurrentFile) to this entry's own starting position before a potentially multi-second
            // single AmsiScanBuffer call, rather than leaving it frozen on the previous entry's
            // percentage throughout.
            reportProgress(relativePath, entriesDone, totalEntries);
            try
            {
                // T-F460: by its literal name - a folder "d " asked for without the prefix is "d".
                long length = QuarantineFiles.Info(file).Length;
                ThreatFinding finding = await ScanOneEntryAsync(
                    archivePath, relativePath, length,
                    () => QuarantineFiles.OpenRead(file),
                    scanner, cancellationToken)
                    .ConfigureAwait(false);
                findings.Add(finding);
            }
            catch (OperationCanceledException) { throw; }
            // Phase 0 spike (docs/DECISIONS.md's T-F146 entry): Defender's real-time on-access
            // scanner can independently remove/lock a file inside the quarantine "out\" directory
            // between QuarantineFiles.EnumerateFiles listing it and this code reading it — that is a real,
            // observed race, not a hypothetical one, and must never crash the whole scan or
            // silently drop the entry.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                findings.Add(CoreMessages.Inconclusive(archivePath, relativePath, CoreMessages.Text(MessageCode.ScanRemovedOrBlocked)));
            }
            finally
            {
                // Fires exactly once per file regardless of which branch above ran (oversized
                // skip inside ScanOneEntryAsync, a clean scan, or the vanished/blocked catch
                // above) — a single point to advance real per-entry progress.
                reportProgress(relativePath, ++entriesDone, totalEntries);
            }
        }
        return entriesDone;
    }
}
