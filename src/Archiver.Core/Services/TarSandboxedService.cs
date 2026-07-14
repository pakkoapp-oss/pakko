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

            try
            {
                var (actualDest, anyExtracted) = await ExtractSingleArchiveAsync(
                    archivePath, destDir, conflictResolver, skippedFiles,
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
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Cannot extract archive: {ex.Message}",
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

        Directory.CreateDirectory(destDir);

        int totalFiles = 0;
        int extractedCount = 0;

        foreach (string file in EnumerateFilesGuarded(scope.OutputDirectory!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalFiles++;

            string relativePath = Path.GetRelativePath(scope.OutputDirectory!, file);
            string finalFilePath = Path.GetFullPath(Path.Combine(destDir, relativePath));

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
            return (destDir, false);
        }

        return (destDir, true);
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
        try
        {
            using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, cancellationToken)
                .ConfigureAwait(false);

            var (nameExitCode, nameStdOut, nameStdErr) = await scope.RunAsync(
                ["-tf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
            if (nameExitCode != 0)
                return new ArchiveListResult { Success = false, ErrorMessage = nameStdErr.Trim() };

            string[] names = SplitLines(nameStdOut);

            var (typeExitCode, typeStdOut, typeStdErr) = await scope.RunAsync(
                ["-tvf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
            if (typeExitCode != 0)
                return new ArchiveListResult { Success = false, ErrorMessage = typeStdErr.Trim() };

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
