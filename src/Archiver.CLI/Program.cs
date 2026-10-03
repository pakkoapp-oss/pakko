using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Archiver.CLI;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;

// T-F51/T-F261: loaded once per invocation; every command gets its services from Core's one
// factory built with it — Archiver.CLI has no DI container, same as Archiver.Shell.
// pakko.exe only ships for Windows (tar.exe/AppContainer are already Windows-only throughout
// this file) despite this project's plain net10.0 (not net10.0-windows) TargetFramework.
#pragma warning disable CA1416
var services = PakkoServices.Create(GroupPolicyService.Load());
#pragma warning restore CA1416

ParsedCliCommand command = CliArgumentParser.Parse(args);
if (command.ConsoleCodePage is { } consoleCodePage)
    CliConsoleCharset.Apply(consoleCodePage);
// T-F263: -si/-so staging left by a pakko that was killed (e.g. `x -so` plaintext) goes now.
if (command.Type is CliCommandType.Extract or CliCommandType.Test or CliCommandType.List or CliCommandType.Archive)
    CliStreamStaging.SweepAbandoned();

return command.Type switch
{
    CliCommandType.Help => RunHelp(),
    CliCommandType.Version => RunVersion(),
    CliCommandType.Invalid => RunInvalid(command),
    _ when command.PromptForPassword && Console.IsInputRedirected => RejectBarePasswordWithoutConsole(),
    _ when command.Type == CliCommandType.Archive && command.WriteToStdout && !Console.IsOutputRedirected => RejectArchiveToConsole(),
    CliCommandType.Extract => await RunExtractAsync(command, services).ConfigureAwait(false),
    CliCommandType.Test => await RunTestAsync(command, services).ConfigureAwait(false),
    CliCommandType.Info => await RunInfoAsync(services).ConfigureAwait(false),
    CliCommandType.Archive => await RunArchiveAsync(command, services).ConfigureAwait(false),
    CliCommandType.List => await RunListAsync(command, services).ConfigureAwait(false),
    CliCommandType.Hash => await RunHashAsync(command).ConfigureAwait(false),
    _ => 2,
};

static int RunHelp()
{
    Console.Out.WriteLine(CliHelpText.Text);
    return 0;
}

static int RunVersion()
{
    // T-F222: the informational version keeps the -dev suffix and the SDK-appended commit that
    // the 4-segment AssemblyVersion drops.
    string? informationalVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    Console.Out.WriteLine(CliVersionText.Format(informationalVersion));
    return 0;
}

static int RunInvalid(ParsedCliCommand command)
{
    Console.Error.WriteLine($"pakko: {command.ErrorMessage}");
    return 7;
}

// T-F193: the parser can't see whether stdin is a console, so this half of the bare -p rule lives here.
static int RejectBarePasswordWithoutConsole()
{
    Console.Error.WriteLine("pakko: a bare '-p' asks for the password on the console, but stdin is redirected — use -p<pwd>");
    return 7;
}

// T-F221 item 5: like 7-Zip, gzip and zstd — archive bytes would garble the terminal.
static int RejectArchiveToConsole()
{
    Console.Error.WriteLine("pakko: -so would write the archive's binary data to the terminal; redirect stdout to a file or a pipe");
    return 7;
}

// T-F221 item 4: an empty stdin is said as such, not as a corrupt archive under its temp name.
static int? RejectEmptyStdin(CliStagingFolder? stdinFolder)
{
    if (stdinFolder is null || new FileInfo(Path.Combine(stdinFolder.Path, CliStreamStaging.StdinFileName)).Length > 0)
        return null;
    Console.Error.WriteLine("pakko: error: stdin was empty, so there is no archive to read");
    return 2;
}

static string? StdinPathFor(CliStagingFolder? stdinFolder) =>
    stdinFolder is null ? null : Path.Combine(stdinFolder.Path, CliStreamStaging.StdinFileName);

// -------------------------------------------------------------------------
// x: extract with full paths. SingleFolder mode (not SeparateFolders — that's
// Archiver.Shell's --extract-folder behavior) matches real 7z 'x': every named
// archive's own internal structure goes straight into the destination, no
// synthetic per-archive wrapper folder. Without -o the destination is the
// current directory, as with 7z 'x' (T-F206).
// -------------------------------------------------------------------------
static async Task<int> RunExtractAsync(ParsedCliCommand command, PakkoServices services)
{
    // T-F160: cancelled by the conflict prompt's (Q)uit / end of input, or by Ctrl+C — either way a
    // clean Core cancellation (temp output removed) and exit code 255, 7-Zip's "user stopped".
    using var cancellation = CliCancellation.ListenToConsole();
    var progress = CliProgress.ForConsole();
    try
    {
        IExtractionRouter router = await services.CreateExtractionRouterAsync().ConfigureAwait(false);

        using CliStagingFolder? stdinFolder = await StageStdinIfRequestedAsync(command, cancellation.Token).ConfigureAwait(false);
        if (RejectEmptyStdin(stdinFolder) is { } emptyStdin)
            return emptyStdin;
        using CliStagingFolder? stdoutFolder = command.WriteToStdout ? CliStagingFolder.Create(CliStreamStaging.StdoutRoot) : null;
        string destination = stdoutFolder?.Path ?? ResolveExtractDestination(command);

        var report = new CliReportContext
        {
            StdinPath = StdinPathFor(stdinFolder),
            PasswordGiven = command.Password is not null || command.PromptForPassword,
            KeptExistingByDefault = command.OverwriteMode is null && !command.AssumeYes,
        };
        ExtractOptions options = BuildExtractOptions(command, ArchivePathsFor(command, stdinFolder), destination, cancellation.Source, progress, report);

        ArchiveResult result = await router.ExtractAsync(options, progress, cancellation.Token).ConfigureAwait(false);
        progress?.Clear();
        if (cancellation.Token.IsCancellationRequested)
            return ReportUserStopped();
        return await ReportAndStreamAsync(result, stdoutFolder, report, cancellation.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
    {
        progress?.Clear();
        return ReportUserStopped();
    }
}

// T-F244 item 4: -si stages into a folder this run owns from the moment it exists, so a failed
// or cancelled copy leaves nothing in %TEMP%. Null when the archive is a path argument.
static async Task<CliStagingFolder?> StageStdinIfRequestedAsync(ParsedCliCommand command, CancellationToken cancellationToken)
{
    if (!command.ReadFromStdin)
        return null;
    await using Stream stdin = Console.OpenStandardInput();
    return await CliStreamStaging.StageStdinAsync(CliStreamStaging.StdinRoot, stdin, cancellationToken).ConfigureAwait(false);
}

static IReadOnlyList<string> ArchivePathsFor(ParsedCliCommand command, CliStagingFolder? stdinFolder) =>
    stdinFolder is null ? command.ArchivePaths : [Path.Combine(stdinFolder.Path, CliStreamStaging.StdinFileName)];

// T-F206: like `7z x`, no -o means the current directory, not the archive's own folder.
static string ResolveExtractDestination(ParsedCliCommand command) =>
    command.OutputDirectory ?? Directory.GetCurrentDirectory();

static ExtractOptions BuildExtractOptions(
    ParsedCliCommand command, IReadOnlyList<string> archivePaths, string destination, CancellationTokenSource quit,
    CliProgress? progress, CliReportContext report)
{
    // T-F160: like real 7-Zip, ask on a conflict only when nothing else decided it (-ao/-y) and a
    // person can actually answer: stdin is a real console, not redirected and not -si's archive
    // bytes. Otherwise the pre-T-F160 non-interactive Skip stays (pinned by T-F179's test).
    bool askInteractively = command.OverwriteMode is null && !command.AssumeYes
        && !command.ReadFromStdin && !Console.IsInputRedirected;

    return new()
    {
        ArchivePaths = archivePaths,
        DestinationFolder = destination,
        Mode = ExtractMode.SingleFolder,
        OnConflict = ChooseExtractConflictBehavior(command, askInteractively),
        ResolveConflictAsync = askInteractively
            ? ClearingProgress<ConflictInfo, ConflictDecision>(progress, CliConflictPrompt.CreateResolver(ReadConflictAnswer, Console.Error.Write, quit).ResolveAsync)
            : null,
        ConfirmCompressionBombExtraction = command.AssumeYes ? (_ => Task.FromResult(true)) : null,
        ResolvePasswordAsync = BuildPasswordResolver(command, command.AssumeYes, progress, report),
    };
}

// A prompt starts on a clean line, not after the progress percentage.
static Func<T, Task<TResult>> ClearingProgress<T, TResult>(CliProgress? progress, Func<T, Task<TResult>> prompt)
{
    return value =>
    {
        progress?.Clear();
        return prompt(value);
    };
}

// -ao wins over -y when both are given; -y only needs to override the safe defaults, never set them.
static ConflictBehavior ChooseExtractConflictBehavior(ParsedCliCommand command, bool askInteractively)
{
    if (command.OverwriteMode is { } overwriteMode)
        return overwriteMode;
    if (command.AssumeYes)
        return ConflictBehavior.Overwrite;
    return askInteractively ? ConflictBehavior.Ask : ConflictBehavior.Skip;
}

// T-F160: key by key with TreatControlCAsInput, exactly like the password prompt below — a plain
// Console.ReadLine keeps blocking after a Ctrl+C that CancelKeyPress cancelled (confirmed on
// device), so Ctrl+C at the prompt has to arrive as a key (CliLineInput maps it to quit).
static string? ReadConflictAnswer()
{
    bool previousTreatControlCAsInput = Console.TreatControlCAsInput;
    Console.TreatControlCAsInput = true;
    try
    {
        string? answer = CliLineInput.Read(() => Console.ReadKey(intercept: true), EchoTypedChar, mask: false);
        Console.Error.WriteLine();
        return answer;
    }
    finally
    {
        Console.TreatControlCAsInput = previousTreatControlCAsInput;
    }
}

static void EchoTypedChar(char c) => Console.Error.Write(c == '\b' ? "\b \b" : c.ToString());

static int ReportUserStopped()
{
    Console.Error.WriteLine("pakko: operation stopped by user");
    return 255;
}

// -------------------------------------------------------------------------
// T-F191: -p{pwd} support for x/t. Three shapes, in order of precedence:
//   1. -p<pwd> given                  -> try it once; a second call (wrong password) reports a
//                                         specific "incorrect password" line before declining, so
//                                         the CLI-known fact "a password WAS supplied" isn't lost
//                                         in Core's generic "password-protected" message (Core's
//                                         PasswordResolver collapses never-wired/cancelled/
//                                         exhausted-attempts into the same null result — see
//                                         docs/DECISIONS.md's T-F191 entry).
//   2. no -p, stdin redirected or -y  -> no resolver at all, preserving the exact pre-T-F191
//                                         "password-protected and cannot be extracted/tested"
//                                         message (also covers -si, since stdin is already
//                                         consumed by the piped archive bytes in that case).
//   3. no -p, real interactive stdin  -> masked Console.ReadKey prompt, retried by Core's own
//                                         PasswordResolver up to its maxAttempts.
// -------------------------------------------------------------------------
// T-F193: a bare -p asks even under -y — the user explicitly requested a prompt. RejectBarePasswordWithoutConsole
// has already ruled out redirected stdin by the time this runs.
static Func<PasswordPromptInfo, Task<PasswordDecision>>? BuildPasswordResolver(
    ParsedCliCommand command, bool assumeYes, CliProgress? progress, CliReportContext report)
{
    if (command.Password is { } password)
        return ClearingProgress<PasswordPromptInfo, PasswordDecision>(progress, info => Task.FromResult(ResolveFixedPassword(info, password, report)));

    if (!command.PromptForPassword && (Console.IsInputRedirected || assumeYes))
        return null;

    return ClearingProgress<PasswordPromptInfo, PasswordDecision>(progress, info => Task.FromResult(PromptForPasswordInteractively(info, report)));
}

// T-F221 item 2: the wrong -p is reported here, once; Core's generic "password-protected" line for
// the same archive is then left out (CliReportContext.IsAlreadyReported).
static PasswordDecision ResolveFixedPassword(PasswordPromptInfo info, string password, CliReportContext report)
{
    if (info.AttemptNumber > 1)
    {
        Console.Error.WriteLine($"pakko: error: {report.DisplayArchiveName(info.ArchiveName)}: incorrect password (-p)");
        report.WrongPasswordArchives.Add(info.ArchiveName);
        return new PasswordDecision { Password = null };
    }
    return new PasswordDecision { Password = password };
}

static PasswordDecision PromptForPasswordInteractively(PasswordPromptInfo info, CliReportContext report)
{
    if (info.PreviousAttemptWasWrong)
        Console.Error.WriteLine("pakko: incorrect password, try again");

    Console.Error.Write($"Password for {report.DisplayArchiveName(info.ArchiveName)}: ");

    bool previousTreatControlCAsInput = Console.TreatControlCAsInput;
    Console.TreatControlCAsInput = true;
    try
    {
        string? password = CliPasswordPrompt.Read(() => Console.ReadKey(intercept: true), EchoMaskChar);
        Console.Error.WriteLine();
        return new PasswordDecision { Password = password };
    }
    finally
    {
        Console.TreatControlCAsInput = previousTreatControlCAsInput;
    }
}

static CliPasswordPrompt.NewPasswordResult PromptForNewPasswordInteractively()
{
    bool previousTreatControlCAsInput = Console.TreatControlCAsInput;
    Console.TreatControlCAsInput = true;
    try
    {
        return CliPasswordPrompt.ReadNewPassword(() => Console.ReadKey(intercept: true), Console.Error.Write, EchoMaskChar);
    }
    finally
    {
        Console.TreatControlCAsInput = previousTreatControlCAsInput;
    }
}

static void EchoMaskChar(char c) => Console.Error.Write(c == '\b' ? "\b \b" : "*");

// Shared by RunExtractAsync and RunArchiveAsync: report the result, then, for -so, stream the
// single staged output file to stdout once the operation has succeeded.
static async Task<int> ReportAndStreamAsync(
    ArchiveResult result, CliStagingFolder? stdoutFolder, CliReportContext report, CancellationToken cancellationToken)
{
    int code = ReportResult(result, report);
    if (stdoutFolder is null || code == 2)
        return code;

    string? streamError = await CliStreamStaging.StreamSingleFileToStdoutAsync(stdoutFolder.Path, cancellationToken).ConfigureAwait(false);
    if (streamError is null)
        return code;
    await Console.Error.WriteLineAsync($"pakko: error: {streamError}").ConfigureAwait(false);
    return 2;
}

// -------------------------------------------------------------------------
// t: test integrity — ZIP only (ITarService has no Test method at all). T-F261: the router
// classifies (Group Policy included) and reports tar-family paths as skipped with the specific
// reason, never silently dropped, matching CLI.md's three-way rule.
// -------------------------------------------------------------------------
static async Task<int> RunTestAsync(ParsedCliCommand command, PakkoServices services)
{
    using var cancellation = CliCancellation.ListenToConsole();
    // T-F295 item 1: declared outside the try, so a cancel clears the percentage first.
    var progress = CliProgress.ForConsole();
    try
    {
        using CliStagingFolder? stdinFolder = await StageStdinIfRequestedAsync(command, cancellation.Token).ConfigureAwait(false);
        if (RejectEmptyStdin(stdinFolder) is { } emptyStdin)
            return emptyStdin;
        IReadOnlyList<string> archivePaths = ArchivePathsFor(command, stdinFolder);

        var report = new CliReportContext
        {
            StdinPath = StdinPathFor(stdinFolder),
            PasswordGiven = command.Password is not null || command.PromptForPassword,
        };
        IExtractionRouter router = await services.CreateExtractionRouterAsync().ConfigureAwait(false);
        ArchiveResult result = await router.TestAsync(
            archivePaths,
            progress,
            resolvePasswordAsync: BuildPasswordResolver(command, assumeYes: false, progress, report),
            cancellationToken: cancellation.Token).ConfigureAwait(false);
        progress?.Clear();
        return ReportResult(result, report);
    }
    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
    {
        progress?.Clear();
        return ReportUserStopped();
    }
}

// -------------------------------------------------------------------------
// i: report supported formats/codecs on this system. T-F261: each line's status comes from
// Core's shared classifier (Group Policy, then the live TarCapabilities probe), not a table
// kept here. Under DisableTarExtraction the probe never runs.
// -------------------------------------------------------------------------
static async Task<int> RunInfoAsync(PakkoServices services)
{
    TarCapabilities capabilities = await services.GetTarCapabilitiesAsync().ConfigureAwait(false);
    GroupPolicyOptions policy = services.Policy;

    await Console.Out.WriteLineAsync("Pakko CLI — supported formats on this system:").ConfigureAwait(false);
    const string CreateExtractList = "create, extract, list";
    await PrintFormatLineAsync("zip", "create, extract, test, list", ArchiveFormat.Zip).ConfigureAwait(false);
    await PrintFormatLineAsync("tar", CreateExtractList, ArchiveFormat.Tar).ConfigureAwait(false);
    await PrintFormatLineAsync("tar.gz", CreateExtractList, ArchiveFormat.GZip).ConfigureAwait(false);
    await PrintFormatLineAsync("tar.bz2", CreateExtractList, ArchiveFormat.Bz2).ConfigureAwait(false);
    await PrintFormatLineAsync("tar.xz", CreateExtractList, ArchiveFormat.Xz).ConfigureAwait(false);
    await PrintFormatLineAsync("tar.zst", CreateExtractList, ArchiveFormat.Zstd).ConfigureAwait(false);
    await PrintFormatLineAsync("tar.lzma", CreateExtractList, ArchiveFormat.Lzma).ConfigureAwait(false);
    await PrintFormatLineAsync("7z", "extract, list", ArchiveFormat.SevenZip).ConfigureAwait(false);
    await PrintFormatLineAsync("rar", "extract, list", ArchiveFormat.Rar).ConfigureAwait(false);
    await Console.Out.WriteLineAsync().ConfigureAwait(false);
    string tarVersion = policy.DisableTarExtraction ? "disabled by Group Policy" : $"version {capabilities.Version}";
    await Console.Out.WriteLineAsync($"tar.exe: C:\\Windows\\System32\\tar.exe ({tarVersion})").ConfigureAwait(false);

    return 0;

    Task PrintFormatLineAsync(string format, string capabilitiesText, ArchiveFormat archiveFormat)
    {
        string status;
        if (ArchiveFormatPolicy.IsBlockedByPolicy(archiveFormat, policy))
            status = "blocked by Group Policy";
        else if (ArchiveFormatPolicy.GetRefusalReason(archiveFormat, capabilities, policy) is null)
            status = "supported";
        else
            status = "not supported";
        return Console.Out.WriteLineAsync($"  {format,-9} {capabilitiesText,-27}  ({status})");
    }
}

// -------------------------------------------------------------------------
// a: create a new archive. Always SingleArchive (7z 'a' packs every named source into one
// archive) — SeparateArchives has no 7z-'a'-shaped equivalent and stays out of scope.
// -------------------------------------------------------------------------
static async Task<int> RunArchiveAsync(ParsedCliCommand command, PakkoServices services)
{
    if (RejectUnusableEncryptionPassword(command) is { } commandLineError)
        return commandLineError;

    using var cancellation = CliCancellation.ListenToConsole();
    // T-F295 item 1: declared outside the try, so a cancel clears the percentage first. The
    // encryption password is asked before any work starts, so no percentage is on screen then.
    var progress = CliProgress.ForConsole();
    try
    {
        IArchiveCreationRouter router = services.CreationRouter;

        using CliStagingFolder? stdoutFolder = command.WriteToStdout ? CliStagingFolder.Create(CliStreamStaging.StdoutRoot) : null;
        StrongBox<CliPasswordPrompt.NewPasswordResult?> prompt = new();
        ArchiveOptions options = BuildArchiveOptions(command, ResolveArchiveDestination(command, stdoutFolder), prompt);

        ArchiveResult result = await router.ArchiveAsync(options, progress, cancellation.Token).ConfigureAwait(false);
        progress?.Clear();
        if (ReportNewPasswordPromptOutcome(prompt.Value) is { } promptExitCode)
            return promptExitCode;
        return await ReportAndStreamAsync(result, stdoutFolder, new CliReportContext(), cancellation.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
    {
        progress?.Clear();
        return ReportUserStopped();
    }
}

// T-F193: a -p<pwd> Pakko cannot encrypt with is a command-line error, found before any work.
static int? RejectUnusableEncryptionPassword(ParsedCliCommand command)
{
    if (command.Password is not { } fixedPassword
        || CliPasswordPrompt.DescribeEncryptProblem(EncryptionPasswordRule.Check(fixedPassword)) is not { } problem)
        return null;
    Console.Error.WriteLine($"pakko: -p: {problem}");
    return 7;
}

static string ResolveArchiveDestination(ParsedCliCommand command, CliStagingFolder? stdoutFolder)
{
    if (stdoutFolder is not null)
        return stdoutFolder.Path;
    string? dir = Path.GetDirectoryName(command.ArchivePathArg!);
    return string.IsNullOrEmpty(dir) ? "." : dir;
}

// T-F221 item 6 (user decision 2026-09-28, 7-Zip's rule): a name with an extension is written
// exactly as typed ("-ttar x.gz" makes a tar named x.gz); a name without one gets the format's.
static ArchiveOptions BuildArchiveOptions(
    ParsedCliCommand command, string destFolder, StrongBox<CliPasswordPrompt.NewPasswordResult?> prompt)
{
    string fileName = Path.GetFileName(command.ArchivePathArg!);
    bool hasExtension = fileName.Contains('.'); // T-F294: ".gz" too, as 7-Zip writes it
    return new()
    {
        SourcePaths = command.SourcePaths,
        DestinationFolder = destFolder,
        ArchiveName = hasExtension ? null : fileName,
        ExactFileName = hasExtension ? fileName : null,
        Mode = ArchiveMode.SingleArchive,
        OnConflict = command.AssumeYes ? ConflictBehavior.Overwrite : ConflictBehavior.Skip,
        CompressionLevel = command.CompressionLevel ?? CompressionLevel.Optimal,
        Format = command.ArchiveFormat,
        ResolvePasswordAsync = BuildNewPasswordResolver(command, prompt),
    };
}

// The interactive prompt records its own outcome in `prompt`: that, never Core's English message,
// decides the report, since Core only sees a null password either way.
static Func<PasswordPromptInfo, Task<PasswordDecision>>? BuildNewPasswordResolver(
    ParsedCliCommand command, StrongBox<CliPasswordPrompt.NewPasswordResult?> prompt)
{
    if (command.Password is { } password)
        return _ => Task.FromResult(new PasswordDecision { Password = password });
    if (!command.PromptForPassword)
        return null;
    return _ =>
    {
        CliPasswordPrompt.NewPasswordResult result = PromptForNewPasswordInteractively();
        prompt.Value = result;
        return Task.FromResult(new PasswordDecision { Password = result.Password });
    };
}

static int? ReportNewPasswordPromptOutcome(CliPasswordPrompt.NewPasswordResult? promptResult)
{
    if (promptResult is { Cancelled: true })
        return ReportUserStopped();
    if (promptResult?.Error is not { } promptError)
        return null;
    Console.Error.WriteLine($"pakko: error: {promptError}");
    return 2;
}

// -------------------------------------------------------------------------
// l: list contents. IArchiveListingRouter takes one archive at a time; looped here for
// multiple archive paths, matching real 7z's own multi-archive 'l' behavior.
// -------------------------------------------------------------------------
static async Task<int> RunListAsync(ParsedCliCommand command, PakkoServices services)
{
    using var cancellation = CliCancellation.ListenToConsole();
    try
    {
        IArchiveListingRouter router = await services.CreateListingRouterAsync().ConfigureAwait(false);

        using CliStagingFolder? stdinFolder = await StageStdinIfRequestedAsync(command, cancellation.Token).ConfigureAwait(false);
        if (RejectEmptyStdin(stdinFolder) is { } emptyStdin)
            return emptyStdin;
        IReadOnlyList<string> archivePaths = ArchivePathsFor(command, stdinFolder);
        var report = new CliReportContext { StdinPath = StdinPathFor(stdinFolder) };

        bool multiple = archivePaths.Count > 1;
        bool anyFailed = false;

        foreach (string archivePath in archivePaths)
        {
            if (!await PrintArchiveListingAsync(archivePath, router, multiple, report, cancellation.Token).ConfigureAwait(false))
                anyFailed = true;
        }

        return anyFailed ? 2 : 0;
    }
    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
    {
        return ReportUserStopped();
    }
}

// Prints one archive's listing (or its error) to stdout/stderr. Returns false on failure.
static async Task<bool> PrintArchiveListingAsync(
    string archivePath, IArchiveListingRouter router, bool multiple, CliReportContext report, CancellationToken cancellationToken)
{
    if (multiple)
        await Console.Out.WriteLineAsync($"# archive: {archivePath}").ConfigureAwait(false);

    ArchiveListResult listResult = await router.ListEntriesAsync(archivePath, cancellationToken).ConfigureAwait(false);
    if (!listResult.Success)
    {
        string name = report.StdinPath is not null ? "(stdin)" : archivePath;
        await Console.Error.WriteLineAsync($"pakko: error: {name}: {listResult.ErrorMessage}").ConfigureAwait(false);
        return false;
    }

    await Console.Out.WriteLineAsync(CliEntryFormatter.Header).ConfigureAwait(false);
    foreach (ArchiveEntryInfo entry in listResult.Entries)
        await Console.Out.WriteLineAsync(CliEntryFormatter.FormatRow(entry)).ConfigureAwait(false);

    if (multiple)
        await Console.Out.WriteLineAsync($"# total: {listResult.Entries.Count} entries").ConfigureAwait(false);

    return true;
}

// -------------------------------------------------------------------------
// h: hash arbitrary files/a single folder (T-F128/T-F09 follow-up) — real 7z 'h' hashes files on
// disk, unrelated to archive entries (CLI.md's original row description of this command predated
// T-F128 and was corrected alongside this). Maps directly onto FileHashService.ComputeAsync,
// exactly the same engine the Explorer context-menu "Хеш-суми" submenu uses.
//
// -si is a genuine single-pass stream here, unlike x/t/l's -si — CRC-32/SHA-256 need no seeking,
// so stdin is hashed directly via FileHashService.ComputeStreamDigestAsync with no intermediate
// temp file (contrast CliStreamStaging.StageStdinAsync, which x/t/l genuinely need because ZIP's
// central directory / tar.exe's pre-scan require a real seekable file).
// -------------------------------------------------------------------------
static async Task<int> RunHashAsync(ParsedCliCommand command)
{
    if (command.ReadFromStdin)
        return await RunHashStdinAsync(command.HashAlgorithm).ConfigureAwait(false);

    HashResult result = await FileHashService.ComputeAsync(command.SourcePaths, command.HashAlgorithm, progress: null, CancellationToken.None).ConfigureAwait(false);

    // T-F221 item 10: a folder's files are named relative to the folder's parent ("docs\sub\a.txt"),
    // the same names its "data and names" sum covers, not as absolute paths.
    string? folderParent = result.Folder is null ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.SourcePaths[0])));
    int errorCount = await PrintHashEntriesAsync(result.Entries, folderParent).ConfigureAwait(false);

    if (result.Folder is { } folder)
    {
        string label = command.HashAlgorithm == HashAlgorithmKind.Crc32 ? "CRC32" : "SHA256";
        await PrintHashFolderSummaryAsync(folder, label).ConfigureAwait(false);
    }

    if (errorCount == result.Entries.Count)
        return 2;
    return errorCount > 0 ? 1 : 0;
}

static async Task<int> RunHashStdinAsync(HashAlgorithmKind algorithm)
{
    await using Stream stdin = Console.OpenStandardInput();
    string hash = await FileHashService.ComputeStreamDigestAsync(stdin, algorithm, CancellationToken.None).ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"{hash}  (stdin)").ConfigureAwait(false);
    return 0;
}

static async Task<int> PrintHashEntriesAsync(IReadOnlyList<HashEntry> entries, string? folderParent)
{
    int errorCount = 0;
    foreach (HashEntry entry in entries)
    {
        string name = folderParent is null ? entry.SourcePath : Path.GetRelativePath(folderParent, entry.SourcePath);
        if (entry.Error is not null)
        {
            await Console.Error.WriteLineAsync($"pakko: error: {name}: {entry.Error}").ConfigureAwait(false);
            errorCount++;
        }
        else
        {
            await Console.Out.WriteLineAsync($"{entry.Hash}  {name}").ConfigureAwait(false);
        }
    }
    return errorCount;
}

static async Task PrintHashFolderSummaryAsync(FolderHashSummary folder, string label)
{
    await Console.Out.WriteLineAsync().ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"Files: {folder.FileCount}").ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"{label} for data:           {folder.DataSum}").ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"{label} for data and names: {folder.NamesSum}").ConfigureAwait(false);
}

// -------------------------------------------------------------------------
// Shared: prints errors/skipped files to stderr, maps ArchiveResult onto the exit-code table
// (0 clean success, 1 success with warnings, 2 operation failed).
// -------------------------------------------------------------------------
// T-F221 items 2-3: the way forward, not only what went wrong. T-F296: keyed on each cause code.
static void PrintHints(ArchiveResult result, CliReportContext report)
{
    IReadOnlyList<string> hints = CliHints.For(
        [.. result.Errors.Select(e => e.Text?.Code)], [.. result.SkippedFiles.Select(s => s.Text?.Code)],
        report.PasswordGiven, report.KeptExistingByDefault);
    foreach (string hint in hints)
        Console.Error.WriteLine(hint);
}

static int ReportResult(ArchiveResult result, CliReportContext report)
{
    foreach (ArchiveError error in result.Errors.Where(e => !report.IsAlreadyReported(e)))
        Console.Error.WriteLine($"pakko: error: {report.DisplayName(error.SourcePath)}: {error.Message}");
    foreach (SkippedFile skipped in result.SkippedFiles)
    {
        string name = report.StdinPath is not null && skipped.Path == report.StdinPath ? "(stdin)" : skipped.Path;
        Console.Error.WriteLine($"pakko: skipped: {name}: {skipped.Reason}");
    }

    PrintHints(result, report);

    return result.Outcome switch
    {
        OperationOutcome.Completed => 0,
        OperationOutcome.Failed => 2,
        _ => 1,
    };
}
