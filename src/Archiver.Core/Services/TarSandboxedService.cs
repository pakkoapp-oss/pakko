using System.Diagnostics;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Services;

/// <summary>
/// Extracts tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma, 7z, rar) via
/// the system's tar.exe, launched inside a Windows AppContainer (no network capability) with a
/// Job Object (ActiveProcessLimit = 1, RAM/CPU limits) — see TASKS.md's T-F52 entry for the full
/// design and DECISIONS.md for the empirical trail. Never throws to callers — all errors are
/// captured in ArchiveResult.Errors. Replaces the deleted TarProcessService.
/// </summary>
public sealed class TarSandboxedService : ITarService
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    // DetectCapabilitiesAsync runs synchronously on app startup (App.xaml.cs forces eager
    // resolution) — a hung tar.exe --version must not hang app launch indefinitely.
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public async Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        try
        {
            // Deliberately unsandboxed (no AppContainer/Job Object) — this is a one-shot,
            // eagerly-resolved startup probe, not an untrusted-archive operation. Still gated on
            // the signature check: a tampered tar.exe should fail closed here via the same
            // all-false-defaults path used for "tar.exe absent", not silently run --version.
            if (!TarSignatureVerifier.Verify(TarExecutablePath))
                return new TarCapabilities();

            var startInfo = new ProcessStartInfo
            {
                FileName = TarExecutablePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                return new TarCapabilities();

            using var timeoutCts = new CancellationTokenSource(DetectionTimeout);

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                return TarVersionParser.Parse(output);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new TarCapabilities();
            }
        }
        catch (Exception)
        {
            return new TarCapabilities();
        }
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        // T-F06: one instance for the whole call — see ZipArchiveService.ExtractAsync's identical
        // comment. Independent of ZipArchiveService's own resolver instance: Zip and tar-family
        // archives are handled by two separate ExtractionRouter calls, so "apply to all" does not
        // cross a mixed zip+tar-family selection (an accepted, documented scope cut).
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        Directory.CreateDirectory(options.DestinationFolder);

        int total = options.ArchivePaths.Count;

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string archivePath = options.ArchivePaths[i];
            string destDir = options.Mode == ExtractMode.SeparateFolders
                ? Path.Combine(options.DestinationFolder,
                    options.SeparateFolderName ?? ArchiveNaming.GetBaseName(archivePath))
                : options.DestinationFolder;

            // T-F113: cheap proactive check, no sandbox/tar.exe launch needed for a known-
            // encrypted RAR — mirrors ZipArchiveService.ExtractAsync's IsEncryptedZip placement.
            // 7z and RAR's rarer header-encrypted case aren't cheaply detectable this way (see
            // ArchiveFormatDetector.IsEncryptedRar's doc comment) — those are instead caught
            // reactively below via IsLikelyEncryptionFailure once tar.exe actually fails.
            if (ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
                && ArchiveFormatDetector.IsEncryptedRar(archivePath))
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "This archive is password-protected and cannot be extracted."
                });
                progress?.Report((i + 1) * 100 / total);
                continue;
            }

            try
            {
                bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
                var (actualDest, anyExtracted) = await ExtractSingleArchiveAsync(
                    archivePath, destDir, alreadyIsolated, conflictResolver, skippedFiles,
                    options.ConfirmCompressionBombExtraction, options.SelectedEntryPaths,
                    cancellationToken)
                    .ConfigureAwait(false);

                // T-F87: an archive whose entries were all individually skipped (e.g. every
                // entry already exists at the destination with OnConflict=Skip) must not be
                // reported as CreatedFiles — MainViewModel uses this list to decide whether
                // DeleteAfterOperation may delete the source archive.
                if (anyExtracted)
                    createdFiles.Add(actualDest);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TarArchiveRejectedException ex)
            {
                errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message });
            }
            catch (TarSignatureVerificationException ex)
            {
                errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message });
            }
            catch (SandboxSetupException ex)
            {
                errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message, Exception = ex });
            }
            catch (IOException ex)
            {
                // T-F113: covers 7z (both encryption modes) and RAR's header-encrypted case —
                // the proactive check above only catches RAR's more common data-only case
                // before staging even begins.
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = IsLikelyEncryptionFailure(ex.Message)
                        ? "This archive is password-protected and cannot be extracted."
                        : $"Cannot extract archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Access denied extracting archive: {ex.Message}",
                    Exception = ex
                });
            }

            progress?.Report((i + 1) * 100 / total);
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    // T-F49: whole-archive pre-scan (name + type) runs before any -xf call. tar.exe does not
    // abort extraction on a single bad entry (confirmed: it logs an error and keeps writing the
    // rest of the archive, only returning a delayed nonzero exit code) and a symlink entry can
    // be created and then written through to escape the quarantine directory entirely before any
    // C# code gets a chance to inspect the result — see DECISIONS.md's T-F49 entry for the
    // reproduced exploit. Post-hoc validation of quarantine contents therefore cannot be the
    // primary defense; rejecting the whole archive before -xf runs is.
    //
    // T-F52: the scope (profile + ACLs + staging + Job Object + signature check) is created
    // FIRST, before the compression-bomb decision — unlike the pre-sandbox design, the pre-scan
    // itself must now run inside the sandbox too, which needs a staged copy of the archive to
    // already exist in quarantine\in\. This means a declined/blocked bomb no longer leaves
    // "nothing to clean up" the way it used to — the `using` on scope below disposes the
    // quarantine directory on every exit path, early or not.
    private static async Task<(string ActualDest, bool AnyExtracted)> ExtractSingleArchiveAsync(
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        ConflictResolver conflictResolver,
        List<SkippedFile> skippedFiles,
        Func<CompressionBombWarning, Task<bool>>? confirmCompressionBombExtraction,
        IReadOnlyList<string>? selectedEntryPaths,
        CancellationToken cancellationToken)
    {
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, cancellationToken)
            .ConfigureAwait(false);

        // T-F05: the whole-archive pre-scan below runs unconditionally, exactly as it did before
        // SelectedEntryPaths existed — it must NEVER be skipped or narrowed just because only a
        // subset will be extracted (see T-F49's exploit finding in DECISIONS.md: a symlink entry
        // can escape quarantine before any per-entry check runs, so the whole archive must be
        // validated regardless of what subset the caller eventually asks tar.exe to extract).
        var (declaredUncompressedSize, allNames) = await ScanForUnsafeEntriesAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        // T-F118: mirrors ZipArchiveService.ExtractWithSmartFolderingAsync's identical algorithm
        // exactly — allNames already carries tar's own trailing '/' convention for directory
        // entries (see ScanForUnsafeEntriesAsync's comment), so "file entries" can be derived the
        // same way ZIP derives them from ZipArchiveEntry.FullName, with no second tar.exe call.
        // A selected subset (T-F05/T-F98 drill-down) has no single meaningful "root" to collapse,
        // same reasoning as ZIP's isSelectedSubset — always extract straight into destDir.
        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        var fileNames = allNames.Where(n => !n.EndsWith('/')).ToList();

        bool isSingleRootFolder = !isSelectedSubset
            && fileNames.Count > 0
            && fileNames.All(n => n.Contains('/'))
            && fileNames
                .Select(n => n[..n.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = !isSelectedSubset && fileNames.Count == 1 && !fileNames[0].Contains('/');

        string actualDest = (isSingleRootFolder || isSingleRootFile || alreadyIsolated || isSelectedSubset)
            ? destDir
            : Path.Combine(destDir, ArchiveNaming.GetBaseName(archivePath));

        // T-F94: whole-archive compression-ratio decision. compressedFileSize reads the
        // ORIGINAL archivePath (not the staged copy — same size either way, hardlink or copy,
        // but this is the path the caller/UI actually knows about for any error messages).
        long compressedFileSize = new FileInfo(archivePath).Length;
        var bombOutcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            archivePath, declaredUncompressedSize, compressedFileSize,
            ArchiveEntrySecurity.GetAvailableFreeSpace(destDir),
            confirmCompressionBombExtraction).ConfigureAwait(false);

        if (bombOutcome == CompressionBombOutcome.InsufficientDiskSpace)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Archive declares {declaredUncompressedSize:N0} bytes uncompressed, " +
                         $"but the destination only has {ArchiveEntrySecurity.GetAvailableFreeSpace(destDir):N0} bytes free. " +
                         "Extraction was blocked."
            });
            return (destDir, false);
        }

        if (bombOutcome == CompressionBombOutcome.UserDeclined)
        {
            long ratio = compressedFileSize > 0 ? declaredUncompressedSize / compressedFileSize : 0;
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Suspicious compression ratio ({ratio}:1, {declaredUncompressedSize:N0} bytes declared) " +
                         "across the whole archive. Extraction was declined as a precaution against decompression bombs."
            });
            return (destDir, false);
        }

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
        foreach (string name in allNames)
        {
            string? relativeDir = name.EndsWith('/') ? name.TrimEnd('/') : Path.GetDirectoryName(name);
            if (!string.IsNullOrEmpty(relativeDir))
                Directory.CreateDirectory(Path.Combine(scope.OutputDirectory!, relativeDir));
        }

        var tarArgs = new List<string> { "-xf", scope.StagedArchivePath, "-C", scope.OutputDirectory! };
        if (selectedEntryPaths is { Count: > 0 })
            tarArgs.AddRange(ExpandSelection(allNames, selectedEntryPaths));

        var (exitCode, _, stdErr) = await scope.RunAsync(tarArgs, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
            throw new IOException($"tar.exe extraction failed: {stdErr.Trim()}");

        Directory.CreateDirectory(actualDest);

        int totalFiles = 0;
        int extractedCount = 0;

        foreach (string file in EnumerateFilesGuarded(scope.OutputDirectory!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalFiles++;

            string relativePath = Path.GetRelativePath(scope.OutputDirectory!, file);

            // T-F118: matches ZipArchiveService.ExtractWithSmartFolderingAsync's identical strip —
            // when the whole archive collapses to one root folder, actualDest already stands in
            // for that folder, so its own name is dropped from the path being written.
            if (isSingleRootFolder)
            {
                int sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
                if (sep < 0)
                {
                    // Defensive only — every file walked here came from a fileNames entry that
                    // was confirmed to contain '/' for isSingleRootFolder to be true at all.
                    continue;
                }
                relativePath = relativePath[(sep + 1)..];
            }

            string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));

            if (File.Exists(finalFilePath))
            {
                ConflictBehavior resolvedConflict = await conflictResolver.ResolveAsync(finalFilePath).ConfigureAwait(false);
                if (resolvedConflict == ConflictBehavior.Skip)
                {
                    skippedFiles.Add(new SkippedFile { Path = relativePath, Reason = "File already exists at destination." });
                    continue;
                }
                if (resolvedConflict == ConflictBehavior.Rename)
                {
                    finalFilePath = GetUniqueFilePath(finalFilePath);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalFilePath)!);
            File.Move(file, finalFilePath, overwrite: true);

            // T-F45: propagate Zone.Identifier ADS from the ORIGINAL archive (never the staged
            // quarantine copy) to the extracted file — the staged copy is a Pakko-internal
            // implementation detail and may not even carry a Zone.Identifier depending on
            // hardlink-vs-copy staging; MOTW must reflect the real source the user chose.
            ArchiveEntrySecurity.TryPropagateMotw(archivePath, finalFilePath);

            extractedCount++;
        }

        // T-F87: every extracted file was individually skipped (already existed at the
        // destination) — nothing was actually written, so the caller must not count this
        // archive as CreatedFiles (that list gates whether DeleteAfterOperation may delete
        // the source archive).
        if (totalFiles > 0 && extractedCount == 0)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = "No entries were extracted from this archive — every entry was skipped."
            });
            return (actualDest, false);
        }

        return (actualDest, true);
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
    private static async Task<(long TotalDeclaredSize, string[] Names)> ScanForUnsafeEntriesAsync(
        TarSandboxScope scope, CancellationToken cancellationToken)
    {
        var (nameExitCode, nameStdOut, nameStdErr) = await scope.RunAsync(
            ["-tf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
        if (nameExitCode != 0)
            throw new IOException($"Cannot read archive: {nameStdErr.Trim()}");

        string[] names = SplitLines(nameStdOut);

        foreach (string name in names)
        {
            if (IsDangerousEntryName(name))
                throw new TarArchiveRejectedException(
                    $"Archive contains an unsafe entry path ('{name}') and cannot be safely extracted.");
        }

        var (typeExitCode, typeStdOut, typeStdErr) = await scope.RunAsync(
            ["-tvf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
        if (typeExitCode != 0)
            throw new IOException($"Cannot read archive: {typeStdErr.Trim()}");

        string[] typeLines = SplitLines(typeStdOut);
        if (typeLines.Length != names.Length)
            throw new TarArchiveRejectedException(
                "Archive listing is inconsistent and cannot be safely extracted.");

        // T-F90: column 4 (size) is accumulated alongside the existing column-0 (type) check in
        // the same pass — see DECISIONS.md's T-F90 entry for why the size column, unlike the
        // date column, is safe to parse regardless of locale.
        long totalDeclaredSize = 0;

        foreach (string line in typeLines)
        {
            char typeChar = line.Length > 0 ? line[0] : '?';
            if (typeChar != '-' && typeChar != 'd')
                throw new TarArchiveRejectedException(
                    "Archive contains a symlink, hardlink, device, or other special entry and cannot be safely extracted.");

            if (typeChar == '-')
                totalDeclaredSize += ParseTarListingSize(line);
        }

        // T-F05: the raw names (with tar's own trailing '/' on directory entries preserved) are
        // returned so ExtractSingleArchiveAsync's selected-subset extraction can build a "-xf"
        // member argument list without a second "-tf" invocation, and so the exact path form
        // tar.exe itself uses is what's ever passed back to it (see DECISIONS.md's T-F05 spike
        // entry — an unmatched/mismatched member name makes the whole "-xf" call fail non-zero).
        return (totalDeclaredSize, names);
    }

    // T-F05: expands a UI-selected set of archive-internal paths (ArchiveEntryInfo.Path's
    // convention — no trailing slash, even for folders) into the exact literal member names
    // tar.exe's "-tf" reported, for a "-xf archive member..." selective-extraction call. A
    // selected folder path is expanded to every one of its descendants explicitly, rather than
    // relying on tar.exe auto-recursing a bare directory-member argument — confirmed empirically
    // (DECISIONS.md's T-F05 entry) that tar.exe does auto-recurse, but this method doesn't depend
    // on that behavior continuing to hold.
    private static List<string> ExpandSelection(string[] allNames, IReadOnlyList<string> selectedEntryPaths)
    {
        var allNamesSet = new HashSet<string>(allNames, StringComparer.Ordinal);
        var result = new List<string>();

        foreach (string selected in selectedEntryPaths)
        {
            if (allNamesSet.Contains(selected))
                result.Add(selected);
            else if (allNamesSet.Contains(selected + "/"))
                result.Add(selected + "/");

            string descendantPrefix = selected + "/";
            foreach (string name in allNames)
            {
                if (name.StartsWith(descendantPrefix, StringComparison.Ordinal))
                    result.Add(name);
            }
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        // T-F113: cheap proactive check for the header-encrypted case only (unlike ExtractAsync's
        // IsEncryptedRar check) — a data-only-encrypted RAR's filenames are still readable, so
        // listing should still succeed there, matching ZipArchiveService.ListEntriesAsync's and
        // 7z's own parity (only extraction refuses for data-only encryption, not browsing).
        if (ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
            && ArchiveFormatDetector.IsRarHeaderEncrypted(archivePath))
        {
            return new ArchiveListResult
            {
                Success = false,
                ErrorMessage = "This archive is password-protected and cannot be browsed."
            };
        }

        try
        {
            using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, cancellationToken)
                .ConfigureAwait(false);

            var (nameExitCode, nameStdOut, nameStdErr) = await scope.RunAsync(
                ["-tf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
            if (nameExitCode != 0)
                return new ArchiveListResult
                {
                    Success = false,
                    ErrorMessage = IsLikelyEncryptionFailure(nameStdErr)
                        ? "This archive is password-protected and cannot be browsed."
                        : nameStdErr.Trim()
                };

            string[] names = SplitLines(nameStdOut);

            var (typeExitCode, typeStdOut, typeStdErr) = await scope.RunAsync(
                ["-tvf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
            if (typeExitCode != 0)
                return new ArchiveListResult
                {
                    Success = false,
                    ErrorMessage = IsLikelyEncryptionFailure(typeStdErr)
                        ? "This archive is password-protected and cannot be browsed."
                        : typeStdErr.Trim()
                };

            string[] typeLines = SplitLines(typeStdOut);
            if (typeLines.Length != names.Length)
                return new ArchiveListResult { Success = false, ErrorMessage = "Archive listing is inconsistent." };

            var entries = new List<ArchiveEntryInfo>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                char typeChar = typeLines[i].Length > 0 ? typeLines[i][0] : '?';
                entries.Add(new ArchiveEntryInfo
                {
                    Path = names[i].TrimEnd('/'),
                    Size = typeChar == '-' ? ParseTarListingSize(typeLines[i]) : 0,
                    CompressedSize = 0,
                    // Date column was observed locale-mangled (see this method's sibling
                    // ScanForUnsafeEntriesAsync's comment and DECISIONS.md's T-F84 entry) — left
                    // null rather than risk a half-correct parse; the UI shows "—" instead.
                    Modified = null,
                    IsDirectory = typeChar == 'd',
                });
            }

            return new ArchiveListResult { Success = true, Entries = entries };
        }
        catch (TarSignatureVerificationException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (SandboxSetupException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (IOException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc/>
    // T-F105: deliberately unsandboxed — SourcePaths are trusted local files the user selected,
    // not an untrusted archive being parsed, so T-F52's threat model (a hostile archive driving
    // libarchive into misbehaving) does not apply. See SECURITY.md's tar.exe Trust Model section
    // for the extraction-vs-creation distinction. Still runs the same Authenticode signature
    // check as every other tar.exe launch site (SandboxedProcessLauncher.RunAsync,
    // DetectCapabilitiesAsync above) — cheap, not a substitute for the sandbox, but no launch
    // site should skip it.
    public async Task<ArchiveResult> CompressAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        if (!TarSignatureVerifier.Verify(TarExecutablePath))
        {
            errors.Add(new ArchiveError
            {
                SourcePath = options.DestinationFolder,
                Message = "tar.exe failed Authenticode signature verification; refusing to run it."
            });
            return new ArchiveResult { Success = false, CreatedFiles = createdFiles, Errors = errors, SkippedFiles = skippedFiles };
        }

        Directory.CreateDirectory(options.DestinationFolder);
        string extension = ArchiveNaming.GetExtension(options.Format);

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            // T-F99: same drive-root/empty-name fallback ZipArchiveService.ArchiveAsync already
            // uses for a single-source drive-root selection (e.g. "Z:\" via the shell extension's
            // Drive ItemType) instead of silently naming the archive after the bare extension.
            string archiveName = options.ArchiveName ?? (options.SourcePaths.Count == 1
                ? Path.GetFileNameWithoutExtension(options.SourcePaths[0]) is { Length: > 0 } name
                    ? name
                    : "archive"
                : "archive");

            string destPath = Path.Combine(options.DestinationFolder, archiveName + extension);

            if (File.Exists(destPath))
            {
                switch (await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false))
                {
                    case ConflictBehavior.Skip:
                        return new ArchiveResult
                        {
                            Success = true,
                            CreatedFiles = [],
                            Errors = [],
                            SkippedFiles = [.. options.SourcePaths.Select(p => new SkippedFile
                            {
                                Path = p,
                                Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                            })],
                        };
                    case ConflictBehavior.Overwrite:
                        File.Delete(destPath);
                        break;
                    case ConflictBehavior.Rename:
                        destPath = GetUniqueFilePath(destPath);
                        break;
                }
            }

            await CompressToArchiveAsync(options, destPath, createdFiles, errors, skippedFiles, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else // ArchiveMode.SeparateArchives — one archive per top-level source path
        {
            var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string sourcePath in sortedSourcePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    errors.Add(new ArchiveError { SourcePath = sourcePath, Message = $"Source path does not exist: {sourcePath}" });
                    continue;
                }

                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string destPath = Path.Combine(options.DestinationFolder, baseName + extension);

                if (File.Exists(destPath))
                {
                    switch (await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false))
                    {
                        case ConflictBehavior.Skip:
                            skippedFiles.Add(new SkippedFile
                            {
                                Path = sourcePath,
                                Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                            });
                            continue;
                        case ConflictBehavior.Overwrite:
                            File.Delete(destPath);
                            break;
                        case ConflictBehavior.Rename:
                            destPath = GetUniqueFilePath(destPath);
                            break;
                    }
                }

                var singleOptions = options with { SourcePaths = [sourcePath] };
                await CompressToArchiveAsync(singleOptions, destPath, createdFiles, errors, skippedFiles, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    // Runs one tar.exe -cf invocation writing to a ".tmp" path, then atomically moves it to
    // destPath only if at least one entry was actually written — mirrors ZipArchiveService's
    // temp-then-commit pattern (no partial files on cancel or failure, CLAUDE.md hard
    // constraint). Reparse-point sources are skipped (T-F23 precedent); missing sources are
    // reported as ArchiveError, matching ZipArchiveService.ArchiveAsync's per-item handling.
    private static async Task CompressToArchiveAsync(
        ArchiveOptions options,
        string destPath,
        List<string> createdFiles,
        List<ArchiveError> errors,
        List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        string tempPath = destPath + ".tmp";

        var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var tarArgs = new List<string>();
        AppendCompressionFilterArgs(tarArgs, options.Format, options.CompressionLevel);
        tarArgs.Add("-v");
        tarArgs.Add("-cf");
        tarArgs.Add(tempPath);

        int entryCount = 0;

        foreach (string sourcePath in sortedSourcePaths)
        {
            if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
            {
                skippedFiles.Add(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = "Symbolic links and NTFS junctions are not archived."
                });
                continue;
            }

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                errors.Add(new ArchiveError { SourcePath = sourcePath, Message = $"Source path does not exist: {sourcePath}" });
                continue;
            }

            string fullSource = Path.GetFullPath(sourcePath);
            string? parent = Path.GetDirectoryName(fullSource);
            string name = Path.GetFileName(fullSource);

            if (string.IsNullOrEmpty(name))
            {
                // Drive-root source (e.g. "Z:\") — GetFileName returns "" and GetDirectoryName
                // returns null. tar.exe strips the drive letter from a rooted absolute-path
                // argument on its own (see IsDangerousEntryName's comment above) — pass it
                // through directly rather than via -C. Same edge case T-F99 already handles for
                // ZipArchiveService; needs its own on-device confirmation in Phase C/D.
                tarArgs.Add(fullSource);
            }
            else
            {
                tarArgs.Add("-C");
                tarArgs.Add(parent!);
                tarArgs.Add(name);
            }

            entryCount++;
        }

        if (entryCount == 0)
            return;

        int reportedFiles = 0;
        void OnVerboseLine(string _)
        {
            reportedFiles++;
            progress?.Report(new ProgressReport { Percent = Math.Min(99, reportedFiles * 100 / Math.Max(entryCount, 1)), BytesTransferred = 0, TotalBytes = 0 });
        }

        try
        {
            var (exitCode, _, stdErr) = await RunUnsandboxedTarAsync(tarArgs, OnVerboseLine, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0 || !File.Exists(tempPath))
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                errors.Add(new ArchiveError { SourcePath = destPath, Message = $"tar.exe failed to create archive: {stdErr.Trim()}" });
                return;
            }

            File.Move(tempPath, destPath, overwrite: true);
            createdFiles.Add(destPath);
            progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = 0, TotalBytes = 0 });
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        catch (IOException ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            errors.Add(new ArchiveError { SourcePath = destPath, Message = $"Cannot create archive: {ex.Message}", Exception = ex });
        }
        catch (UnauthorizedAccessException ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            errors.Add(new ArchiveError { SourcePath = destPath, Message = $"Access denied creating archive: {ex.Message}", Exception = ex });
        }
    }

    // Maps the selected container format + the existing ZIP CompressionLevel enum (reused as the
    // UI-facing knob rather than inventing a second one) to tar.exe's real
    // "--options <filter>:compression-level=N" mechanism — confirmed empirically during T-F105
    // planning that a bare "-9"-style flag does NOT work (exit 1), but --options does, for all
    // five write filters (gzip/bzip2/xz/zstd/lzma), and that "compression-level=0" is a real
    // store/no-compression mode (see DECISIONS.md's T-F105 entry for the raw command output).
    // Plain Tar gets no filter flag and no --options at all — passing --options without an
    // active filter fails with "Unknown module name", confirmed empirically the same round.
    private static void AppendCompressionFilterArgs(List<string> tarArgs, ArchiveContainerFormat format, System.IO.Compression.CompressionLevel level)
    {
        (string? filterFlag, string? moduleName, int max) = format switch
        {
            ArchiveContainerFormat.Tar => ((string?)null, (string?)null, 0),
            ArchiveContainerFormat.TarGz => ("-z", "gzip", 9),
            ArchiveContainerFormat.TarBz2 => ("-j", "bzip2", 9),
            ArchiveContainerFormat.TarXz => ("-J", "xz", 9),
            ArchiveContainerFormat.TarZst => ("--zstd", "zstd", 19),
            ArchiveContainerFormat.TarLzma => ("--lzma", "lzma", 9),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

        if (filterFlag is null)
            return;

        tarArgs.Add(filterFlag);

        int numericLevel = level switch
        {
            System.IO.Compression.CompressionLevel.NoCompression => 0,
            System.IO.Compression.CompressionLevel.Fastest => 1,
            System.IO.Compression.CompressionLevel.SmallestSize => max,
            _ => Math.Max(1, max / 2), // Optimal (and any future enum value) — libarchive's own
                                        // conventional mid-range default (gzip's default is 6 of 9)
        };

        tarArgs.Add("--options");
        tarArgs.Add($"{moduleName}:compression-level={numericLevel}");
    }

    // Unsandboxed tar.exe launch for archive CREATION only (see CompressAsync's own comment for
    // why this is safe to run outside the AppContainer). Mirrors TarSandboxScope.RunAsync's
    // (exitCode, stdOut, stdErr) shape for consistency, but has no quarantine/ACL/Job-Object
    // setup — just a plain redirected-IO process launch, the same shape
    // DetectCapabilitiesAsync above already uses for its own deliberately-unsandboxed probe.
    // onStdErrLine is invoked once per non-empty stderr line as it streams in — tar.exe's "-v"
    // writes each added entry's "a <name>" line to STDERR during creation (confirmed
    // empirically; NOT stdout), so this is how per-entry progress is derived.
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunUnsandboxedTarAsync(
        IReadOnlyList<string> arguments,
        Action<string>? onStdErrLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TarExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using Process process = new() { StartInfo = startInfo };

        var stdOutBuilder = new System.Text.StringBuilder();
        var stdErrBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            stdErrBuilder.AppendLine(e.Data);
            if (e.Data.Length > 0)
                onStdErrLine?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return (process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString());
    }

    // Column 4 (0-based) of "tar -tvf" output: mode, link-count, owner, group, size, month, day,
    // time, name. Locale-independent (plain ASCII decimal), unlike the date columns — see
    // DECISIONS.md's T-F90 entry.
    private static long ParseTarListingSize(string line)
    {
        string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 4 && long.TryParse(fields[4], out long size) ? size : 0;
    }

    private static string[] SplitLines(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsDangerousEntryName(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            return false;

        // T-F49: path-traversal segment check (tar.exe itself also rejects a raw ".." entry,
        // but this is rejected here first regardless — defense-in-depth, not reliance on tar's
        // own behavior).
        if (entryName.Split('/').Any(segment => segment == ".."))
            return true;

        // Rooted paths (leading '/', UNC "\\server\share", or "C:/...") — tar.exe strips the
        // drive letter and keeps these contained (confirmed empirically), but reject outright
        // rather than trust that sanitization.
        if (Path.IsPathRooted(entryName))
            return true;

        if (ArchiveEntrySecurity.HasAlternateDataStreamMarker(entryName))
            return true;

        if (ArchiveEntrySecurity.HasReservedName(entryName))
            return true;

        if (ArchiveEntrySecurity.HasControlCharacters(entryName))
            return true;

        return false;
    }

    // T-F113: reactive classification of a tar.exe/libarchive failure as encryption-related —
    // used for 7z (both data-only and header-encrypted) and RAR's rarer header-encrypted case,
    // where ArchiveFormatDetector.IsEncryptedRar's proactive byte check can't apply (RAR's
    // data-only case is caught proactively instead; see ExtractAsync/ListEntriesAsync). Unlike
    // the RAR byte check, 7z's header metadata is itself typically LZMA-compressed, so a
    // fixed-offset check isn't feasible without a partial 7z reader — see DECISIONS.md's T-F113
    // entry. Confirmed empirically against real 7-Zip/WinRAR-encrypted fixtures that libarchive's
    // own stderr always contains "encrypt" (case-insensitive) for every encryption-related
    // failure it produces: "The file content is encrypted, but currently not supported",
    // "The archive header is encrypted, but currently not supported",
    // "Reading encrypted data is not currently supported", "Encryption is not supported".
    private static bool IsLikelyEncryptionFailure(string stdErr)
        => stdErr.Contains("encrypt", StringComparison.OrdinalIgnoreCase);

    // Walks the sandbox scope's output directory without ever recursing into a reparse-point
    // subdirectory — a plain Directory.EnumerateFiles(..., AllDirectories) would follow such a
    // directory and could walk straight out of quarantine. The pre-scan already rejects any
    // archive containing a symlink entry, so this is defense-in-depth for anything the scan
    // didn't anticipate, not the primary safety mechanism.
    private static IEnumerable<string> EnumerateFilesGuarded(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            List<string> files;
            List<string> subDirs;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
                subDirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
                yield return file;

            foreach (string subDir in subDirs)
            {
                if (!ArchiveEntrySecurity.IsReparsePoint(subDir))
                    pending.Push(subDir);
            }
        }
    }

    // Same "name (1)", "name (2)", ... convention as ZipArchiveService.GetUniqueFilePath. Not
    // shared via ArchiveEntrySecurity — this is a naming convenience, not a security check, and
    // each file here is moved (not written) one at a time, so File.Exists sees every prior move
    // in this same run without needing an in-memory claimed-paths set the way ZIP's single-pass
    // write-then-commit flow does.
    private static string GetUniqueFilePath(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }

    private sealed class TarArchiveRejectedException(string message) : Exception(message);
}
