using System.IO.Compression;
using Archiver.CLI;
using Archiver.Core.Models;
using Archiver.Core.Services;

// T-F51: loaded once per invocation and threaded into every inline service-construction call
// site below — Archiver.CLI has no DI container, same pattern as Archiver.Shell/Program.cs.
// pakko.exe only ships for Windows (tar.exe/AppContainer are already Windows-only throughout
// this file) despite this project's plain net8.0 (not net8.0-windows) TargetFramework.
#pragma warning disable CA1416
GroupPolicyOptions policy = GroupPolicyService.Load();
#pragma warning restore CA1416

var command = CliArgumentParser.Parse(args);

return command.Type switch
{
    CliCommandType.Help => RunHelp(),
    CliCommandType.Invalid => RunInvalid(command),
    CliCommandType.Extract => await RunExtractAsync(command, policy).ConfigureAwait(false),
    CliCommandType.Test => await RunTestAsync(command, policy).ConfigureAwait(false),
    CliCommandType.Info => await RunInfoAsync().ConfigureAwait(false),
    CliCommandType.Archive => await RunArchiveAsync(command, policy).ConfigureAwait(false),
    CliCommandType.List => await RunListAsync(command).ConfigureAwait(false),
    _ => 2,
};

static int RunHelp()
{
    Console.Out.WriteLine(CliHelpText.Text);
    return 0;
}

static int RunInvalid(ParsedCliCommand command)
{
    Console.Error.WriteLine($"pakko: {command.ErrorMessage}");
    return 7;
}

// -------------------------------------------------------------------------
// x: extract with full paths. SingleFolder mode (not SeparateFolders — that's
// Archiver.Shell's --extract-folder behavior) matches real 7z 'x': every named
// archive's own internal structure goes straight into the destination, no
// synthetic per-archive wrapper folder.
// -------------------------------------------------------------------------
static async Task<int> RunExtractAsync(ParsedCliCommand command, GroupPolicyOptions policy)
{
    string? stagedStdinPath = null;
    string? stdoutStagingDir = null;
    try
    {
        var tarService = new TarSandboxedService(policy);
        var capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
        var router = new ExtractionRouter(new ZipArchiveService(policy), tarService, capabilities, policy);

        IReadOnlyList<string> archivePaths = command.ArchivePaths;
        if (command.ReadFromStdin)
        {
            stagedStdinPath = await CliStreamStaging.StageStdinAsync(CancellationToken.None).ConfigureAwait(false);
            archivePaths = [stagedStdinPath];
        }

        string destination;
        if (command.WriteToStdout)
        {
            stdoutStagingDir = CliStreamStaging.CreateOutputStagingDirectory();
            destination = stdoutStagingDir;
        }
        else
        {
            destination = command.OutputDirectory
                ?? (command.ReadFromStdin ? "." : Path.GetDirectoryName(Path.GetFullPath(archivePaths[0])) ?? ".");
        }

        var options = new ExtractOptions
        {
            ArchivePaths = archivePaths,
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            // -ao wins over -y when both are given; without either, Core's own null-callback
            // defaults (Skip / auto-decline) are already the safe, non-interactive behavior a CLI
            // needs, so -y only needs to override them, never set them.
            OnConflict = command.OverwriteMode ?? (command.AssumeYes ? ConflictBehavior.Overwrite : ConflictBehavior.Skip),
            ConfirmCompressionBombExtraction = command.AssumeYes ? (_ => Task.FromResult(true)) : null,
        };

        ArchiveResult result = await router.ExtractAsync(options, progress: null, CancellationToken.None).ConfigureAwait(false);
        int code = ReportResult(result);
        if (!command.WriteToStdout || code == 2)
            return code;

        string? streamError = await CliStreamStaging.StreamSingleFileToStdoutAsync(stdoutStagingDir!, CancellationToken.None).ConfigureAwait(false);
        if (streamError is not null)
        {
            Console.Error.WriteLine($"pakko: error: {streamError}");
            return 2;
        }
        return code;
    }
    finally
    {
        if (stagedStdinPath is not null)
            CliStreamStaging.CleanupStagedStdin(stagedStdinPath);
        if (stdoutStagingDir is not null)
            CliStreamStaging.CleanupOutputStagingDirectory(stdoutStagingDir);
    }
}

// -------------------------------------------------------------------------
// t: test integrity — ZIP only (ITarService has no Test method at all). tar-family
// archive paths are reported as skipped with the specific reason, never silently
// dropped, matching CLI.md's three-way rule even though 't' itself is a supported command.
// -------------------------------------------------------------------------
static async Task<int> RunTestAsync(ParsedCliCommand command, GroupPolicyOptions policy)
{
    string? stagedStdinPath = null;
    try
    {
        IReadOnlyList<string> archivePaths = command.ArchivePaths;
        if (command.ReadFromStdin)
        {
            stagedStdinPath = await CliStreamStaging.StageStdinAsync(CancellationToken.None).ConfigureAwait(false);
            archivePaths = [stagedStdinPath];
        }

        var zipPaths = new List<string>();
        var skippedNonZip = new List<SkippedFile>();

        foreach (string path in archivePaths)
        {
            ArchiveFormat format = ArchiveFormatDetector.Detect(path);
            if (format is ArchiveFormat.Zip or ArchiveFormat.Unknown)
                zipPaths.Add(path);
            else
                skippedNonZip.Add(new SkippedFile { Path = path, Reason = "tar-family archives have no test capability" });
        }

        ArchiveResult result = zipPaths.Count > 0
            ? await new ZipArchiveService(policy).TestAsync(zipPaths, progress: null, CancellationToken.None).ConfigureAwait(false)
            : new ArchiveResult { Success = true };

        result = result with { SkippedFiles = [.. result.SkippedFiles, .. skippedNonZip] };
        return ReportResult(result);
    }
    finally
    {
        if (stagedStdinPath is not null)
            CliStreamStaging.CleanupStagedStdin(stagedStdinPath);
    }
}

// -------------------------------------------------------------------------
// i: report supported formats/codecs on this system. Tar/GZip are unconditionally supported
// (matches ExtractionRouter.IsSupported); the rest depend on the live TarCapabilities probe.
// -------------------------------------------------------------------------
static async Task<int> RunInfoAsync()
{
    var tarService = new TarSandboxedService();
    TarCapabilities capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);

    Console.Out.WriteLine("Pakko CLI — supported formats on this system:");
    Console.Out.WriteLine("  zip       create, extract, test, list   (always)");
    Console.Out.WriteLine("  tar       create, extract, list         (always)");
    Console.Out.WriteLine("  tar.gz    create, extract, list         (always)");
    PrintFormatLine("tar.bz2", "create, extract, list", capabilities.SupportsBz2);
    PrintFormatLine("tar.xz", "create, extract, list", capabilities.SupportsXz);
    PrintFormatLine("tar.zst", "create, extract, list", capabilities.SupportsZstd);
    PrintFormatLine("tar.lzma", "create, extract, list", capabilities.SupportsLzma);
    PrintFormatLine("7z", "extract, list", capabilities.Supports7z);
    PrintFormatLine("rar", "extract, list", capabilities.SupportsRar);
    Console.Out.WriteLine();
    Console.Out.WriteLine($"tar.exe: C:\\Windows\\System32\\tar.exe (version {capabilities.Version})");

    return 0;

    static void PrintFormatLine(string format, string capabilitiesText, bool supported) =>
        Console.Out.WriteLine($"  {format,-9} {capabilitiesText,-22}  ({(supported ? "supported" : "not supported")})");
}

// -------------------------------------------------------------------------
// a: create a new archive. Always SingleArchive (7z 'a' packs every named source into one
// archive) — SeparateArchives has no 7z-'a'-shaped equivalent and stays out of scope.
// -------------------------------------------------------------------------
static async Task<int> RunArchiveAsync(ParsedCliCommand command, GroupPolicyOptions policy)
{
    string? stdoutStagingDir = null;
    try
    {
        var router = new ArchiveCreationRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), policy);

        string archivePathArg = command.ArchivePathArg!;
        string destFolder;
        if (command.WriteToStdout)
        {
            stdoutStagingDir = CliStreamStaging.CreateOutputStagingDirectory();
            destFolder = stdoutStagingDir;
        }
        else
        {
            string? dir = Path.GetDirectoryName(archivePathArg);
            destFolder = string.IsNullOrEmpty(dir) ? "." : dir;
        }

        var options = new ArchiveOptions
        {
            SourcePaths = command.SourcePaths,
            DestinationFolder = destFolder,
            ArchiveName = ArchiveNaming.GetBaseName(archivePathArg),
            Mode = ArchiveMode.SingleArchive,
            OnConflict = command.AssumeYes ? ConflictBehavior.Overwrite : ConflictBehavior.Skip,
            CompressionLevel = command.CompressionLevel ?? CompressionLevel.Optimal,
            Format = command.ArchiveFormat,
        };

        ArchiveResult result = await router.ArchiveAsync(options, progress: null, CancellationToken.None).ConfigureAwait(false);
        int code = ReportResult(result);
        if (!command.WriteToStdout || code == 2)
            return code;

        string? streamError = await CliStreamStaging.StreamSingleFileToStdoutAsync(stdoutStagingDir!, CancellationToken.None).ConfigureAwait(false);
        if (streamError is not null)
        {
            Console.Error.WriteLine($"pakko: error: {streamError}");
            return 2;
        }
        return code;
    }
    finally
    {
        if (stdoutStagingDir is not null)
            CliStreamStaging.CleanupOutputStagingDirectory(stdoutStagingDir);
    }
}

// -------------------------------------------------------------------------
// l: list contents. IArchiveListingRouter takes one archive at a time; looped here for
// multiple archive paths, matching real 7z's own multi-archive 'l' behavior.
// -------------------------------------------------------------------------
static async Task<int> RunListAsync(ParsedCliCommand command)
{
    string? stagedStdinPath = null;
    try
    {
        var tarService = new TarSandboxedService();
        TarCapabilities capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
        var router = new ArchiveListingRouter(new ZipArchiveService(), tarService, capabilities);

        IReadOnlyList<string> archivePaths = command.ArchivePaths;
        if (command.ReadFromStdin)
        {
            stagedStdinPath = await CliStreamStaging.StageStdinAsync(CancellationToken.None).ConfigureAwait(false);
            archivePaths = [stagedStdinPath];
        }

        bool multiple = archivePaths.Count > 1;
        bool anyFailed = false;

        foreach (string archivePath in archivePaths)
        {
            if (multiple)
                Console.Out.WriteLine($"# archive: {archivePath}");

            ArchiveListResult listResult = await router.ListEntriesAsync(archivePath, CancellationToken.None).ConfigureAwait(false);
            if (!listResult.Success)
            {
                Console.Error.WriteLine($"pakko: error: {archivePath}: {listResult.ErrorMessage}");
                anyFailed = true;
                continue;
            }

            Console.Out.WriteLine(CliEntryFormatter.Header);
            foreach (ArchiveEntryInfo entry in listResult.Entries)
                Console.Out.WriteLine(CliEntryFormatter.FormatRow(entry));

            if (multiple)
                Console.Out.WriteLine($"# total: {listResult.Entries.Count} entries");
        }

        return anyFailed ? 2 : 0;
    }
    finally
    {
        if (stagedStdinPath is not null)
            CliStreamStaging.CleanupStagedStdin(stagedStdinPath);
    }
}

// -------------------------------------------------------------------------
// Shared: prints errors/skipped files to stderr, maps ArchiveResult onto the exit-code table
// (0 clean success, 1 success with warnings, 2 operation failed).
// -------------------------------------------------------------------------
static int ReportResult(ArchiveResult result)
{
    foreach (ArchiveError error in result.Errors)
        Console.Error.WriteLine($"pakko: error: {Path.GetFileName(error.SourcePath)}: {error.Message}");
    foreach (SkippedFile skipped in result.SkippedFiles)
        Console.Error.WriteLine($"pakko: skipped: {skipped.Path}: {skipped.Reason}");

    if (!result.Success)
        return 2;
    return result.SkippedFiles.Count > 0 ? 1 : 0;
}
