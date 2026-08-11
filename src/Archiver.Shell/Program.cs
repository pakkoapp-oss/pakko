using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;

// T-F51: loaded once per invocation and threaded into every inline service-construction call
// site below — Archiver.Shell has no DI container, so this is the App.xaml.cs
// AddSingleton(GroupPolicyService.Load()) equivalent for this frontend.
GroupPolicyOptions policy = GroupPolicyService.Load();

var command = ShellArgumentParser.Parse(args);

if (command.Type == CommandType.Invalid)
{
    // WinExe: no console window shown. Exit silently.
    Environment.Exit(1);
    return;
}

switch (command.Type)
{
    case CommandType.OpenUiExtract:
        LaunchOpenUi("extract", command.Files);
        break;

    case CommandType.OpenUiArchive:
        LaunchOpenUi("archive", command.Files);
        break;

    case CommandType.OpenUiBrowse:
        LaunchOpenUi("browse", command.Files);
        break;

    case CommandType.ExtractHere:
        await RunExtractHereAsync(command.Files, policy).ConfigureAwait(false);
        break;

    case CommandType.ExtractHereFlat:
        await RunExtractHereFlatAsync(command.Files, policy).ConfigureAwait(false);
        break;

    case CommandType.ExtractFolder:
        await RunExtractFolderAsync(command.Files, policy).ConfigureAwait(false);
        break;

    case CommandType.Archive:
        await RunArchiveAsync(command.Files, command.Format, policy).ConfigureAwait(false);
        break;

    case CommandType.Test:
        await RunTestAsync(command.Files, policy).ConfigureAwait(false);
        break;

    case CommandType.Scan:
        await RunScanAsync(command.Files, policy).ConfigureAwait(false);
        break;

    case CommandType.Hash:
        await RunHashAsync(command.Files, command.Algorithm).ConfigureAwait(false);
        break;
}

// -------------------------------------------------------------------------
// Open-UI flow: encode files as a base64 JSON array and launch Archiver.App
// via the pakko:// URI scheme. Archiver.Shell exits immediately; the app
// takes over the user interaction.
// -------------------------------------------------------------------------
static void LaunchOpenUi(string operation, IReadOnlyList<string> files)
{
    var base64 = Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files)));
    var uri = $"pakko://{operation}?files={base64}";

    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}

// -------------------------------------------------------------------------
// --extract-here: extract each archive to its own sibling directory.
// Uses T-14 smart folder logic in ZipArchiveService (SeparateFolders mode): single root folder →
// strips prefix; multiple roots → creates wrapper folder; single root FILE (T-F154) → bypasses
// the per-archive subfolder entirely and lands next to the archive, matching how a real archiver
// (NanaZip/7-Zip) handles a single-file archive.
// -------------------------------------------------------------------------
static async Task RunExtractHereAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var router = await BuildExtractionRouterAsync(policy).ConfigureAwait(false);
    // T-F155: one wrapper per Explorer invocation (not per archive) so "apply to all" spans the
    // whole multi-select — see StickyApplyToAllConflictResolver's own doc comment for why a raw
    // ResolveConflictAsync = ShellConflictDialog.ShowAsync wire-through would re-prompt per archive.
    var conflictResolver = new StickyApplyToAllConflictResolver(ShellConflictDialog.ShowAsync);

    foreach (var archivePath in archivePaths)
    {
        var destFolder = Path.GetDirectoryName(archivePath) ?? ".";
        // T-F67: a plain OnConflict=Rename only renames individual conflicting files inside
        // an existing destination folder (that's the GUI app's merge behavior). The shell
        // command instead wants a brand-new numbered folder so re-extracting never silently
        // merges into — or does nothing to — a folder from a previous run.
        var folderName = GetUniqueFolderName(destFolder, ArchiveNaming.GetBaseName(archivePath));
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SeparateFolders,
            SeparateFolderName = folderName,
            // T-F155: the fresh numbered folder above makes a real per-file conflict unreachable
            // for a multi-file archive, but T-F154's unisolatedDestDir bypass means a single-root-
            // FILE archive still lands directly in destFolder — the exact case that can collide.
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = conflictResolver.ResolveAsync,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => router.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --extract-flat (T-F115): dump every archive's contents directly into its own containing
// folder. ExtractMode.SingleFolder still runs T-14's smart-foldering (ZipArchiveService and,
// since T-F118, TarSandboxedService alike) for the single-root-folder/single-root-file cases —
// a single-root-folder archive unwraps its own top-level folder. But since T-F156 (reversing
// T-F118 for this one mode, per a direct user decision — see DECISIONS.md), a genuinely
// multi-root archive (no common containing folder) no longer gets an <archive_name>\ wrapper
// here either — it really is "no wrapper folder ever" now, matching the name. Contrast with
// --extract-here (SeparateFolders mode, above), which always wraps per archive by design, and
// with RunExtractFolderAsync below, which unconditionally pre-computes a fresh subfolder
// regardless of the archive's own root shape.
// -------------------------------------------------------------------------
static async Task RunExtractHereFlatAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var router = await BuildExtractionRouterAsync(policy).ConfigureAwait(false);
    var conflictResolver = new StickyApplyToAllConflictResolver(ShellConflictDialog.ShowAsync);

    foreach (var archivePath in archivePaths)
    {
        var destFolder = Path.GetDirectoryName(archivePath) ?? ".";
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            // T-F155: this is the path that collides on essentially every file when an archive
            // is re-extracted into its own containing folder a second time.
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = conflictResolver.ResolveAsync,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => router.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --extract-folder: always extract into an explicit <archive_name>\ subfolder,
// regardless of the archive's internal structure.
// -------------------------------------------------------------------------
static async Task RunExtractFolderAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var router = await BuildExtractionRouterAsync(policy).ConfigureAwait(false);
    var conflictResolver = new StickyApplyToAllConflictResolver(ShellConflictDialog.ShowAsync);

    foreach (var archivePath in archivePaths)
    {
        var archiveDir = Path.GetDirectoryName(archivePath) ?? ".";
        var folderName = GetUniqueFolderName(archiveDir, ArchiveNaming.GetBaseName(archivePath));
        var destFolder = Path.Combine(archiveDir, folderName);
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            // T-F155: destFolder is always a fresh numbered folder, so this realistically stays
            // inert (no on-disk conflict possible) except for duplicate in-archive entry names —
            // wired for parity/consistency with the other two commands, not because it's expected
            // to fire in practice.
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = conflictResolver.ResolveAsync,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => router.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// T-F85: constructs one ExtractionRouter per invocation, calling DetectCapabilitiesAsync exactly
// once (it spawns a tar.exe process) — shared across every archive in the current selection
// rather than re-probed per archive.
static async Task<IExtractionRouter> BuildExtractionRouterAsync(GroupPolicyOptions policy)
{
    var tarService = new TarSandboxedService(policy);
    var capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
    return new ExtractionRouter(new ZipArchiveService(policy), tarService, capabilities, policy);
}

// -------------------------------------------------------------------------
// --archive: pack all source paths into a single ZIP placed next to the first
// item. A single selected item is named after itself; multiple items are named
// after their common containing folder instead of an arbitrary selected item
// (matches NanaZip's convention).
// -------------------------------------------------------------------------
static async Task RunArchiveAsync(IReadOnlyList<string> sourcePaths, ArchiveContainerFormat format, GroupPolicyOptions policy)
{
    // T-F153: a source path ending in a directory separator (e.g. an "--archive" CLI argument
    // typed/tab-completed with a trailing "\") made Path.GetDirectoryName(firstPath) below return
    // firstPath's OWN full path instead of its parent — placing the new archive INSIDE the source
    // folder itself rather than next to it — and separately made Path.GetFileNameWithoutExtension
    // return "", falling the archive's name back to the generic "archive" instead of the real
    // folder name. ArchiveCreationRouter/ZipArchiveService/TarSandboxedService normalize this
    // internally too (see ZipArchiveService.ArchiveAsync's identical comment), but this
    // destFolder/archiveName computation happens here, before any of that code runs.
    sourcePaths = [.. sourcePaths.Select(Path.TrimEndingDirectorySeparator)];
    var firstPath = sourcePaths[0];
    // T-F99: Path.GetDirectoryName returns null when firstPath is itself a root (e.g. "Z:\",
    // now a reachable single-item selection via the shell extension's Drive ItemType) — a root
    // has no parent to place the archive next to. Falls back to Desktop, the same default
    // destination MainViewModel.cs already uses, rather than "." (the process's own working
    // directory, which is unpredictable for a COM-surrogate-launched process).
    var destFolder = Path.GetDirectoryName(firstPath)
        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    string archiveName;
    if (sourcePaths.Count > 1)
    {
        archiveName = Path.GetFileName(destFolder);
        // Path.GetFileName("C:\") returns "C:" when the selection sits directly at a drive
        // root — invalid in a file name (colon), so fall back rather than let ArchiveAsync throw.
        if (string.IsNullOrEmpty(archiveName) || archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            archiveName = "archive";
    }
    else
    {
        // Path.GetFileNameWithoutExtension returns "" for dotfiles (e.g. ".gitignore" has no
        // name before its only dot) — fall back to the full name so we don't create a bare ".zip".
        // Both return "" for a drive root (e.g. "Z:\") — fall back to "archive" in that case too.
        archiveName = Path.GetFileNameWithoutExtension(firstPath);
        if (string.IsNullOrEmpty(archiveName))
            archiveName = Path.GetFileName(firstPath);
        if (string.IsNullOrEmpty(archiveName))
            archiveName = "archive";
    }

    var router = new ArchiveCreationRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), policy);
    var options = new ArchiveOptions
    {
        SourcePaths = sourcePaths,
        DestinationFolder = destFolder,
        ArchiveName = archiveName,
        Mode = ArchiveMode.SingleArchive,
        OnConflict = ConflictBehavior.Rename,
        Format = format,
    };

    string title = $"Archiving: {archiveName}";
    await RunWithProgressWindowAsync(title,
        (progress, ct) => router.ArchiveAsync(options, progress, ct))
        .ConfigureAwait(false);
}

// -------------------------------------------------------------------------
// --test: verify every entry's CRC-32 across all selected archives without writing
// anything to disk. Unlike Extract/Archive, a successful Test has no visible result on
// disk, so — unlike those two — it needs its own "no errors" confirmation, or a silent
// success would look indistinguishable from nothing having happened.
// -------------------------------------------------------------------------
const uint MB_ICONINFORMATION = 0x40;

static async Task RunTestAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var service = new ZipArchiveService(policy);
    string title = archivePaths.Count == 1
        ? $"Testing: {Path.GetFileName(archivePaths[0])}"
        : $"Testing {archivePaths.Count} archives";

    var result = await RunWithProgressWindowAsync(title,
        (progress, ct) => service.TestAsync(archivePaths, progress, ct))
        .ConfigureAwait(false);

    if (result.Success)
        _ = MessageBoxW(IntPtr.Zero, ResultMessagesLocalizer.Get("ResultNoErrorsDetected"), title, MB_ICONINFORMATION);
}

// Returns "name", or "name (1)", "name (2)", ... if "name" already exists under parentDir.
static string GetUniqueFolderName(string parentDir, string name)
{
    if (!Directory.Exists(Path.Combine(parentDir, name)))
        return name;

    int i = 1;
    string candidate;
    do { candidate = $"{name} ({i++})"; }
    while (Directory.Exists(Path.Combine(parentDir, candidate)));
    return candidate;
}

// -------------------------------------------------------------------------
// Shows progress via the Windows Shell's built-in IProgressDialog (shell32),
// in-process — no separate .exe, no IPC. Falls back to silent direct-service
// operation if the COM object can't be created (should not happen on any
// supported Windows version, but a shell-triggered command must never crash).
// -------------------------------------------------------------------------
static async Task<ArchiveResult> RunWithProgressWindowAsync(
    string title,
    Func<IProgress<ProgressReport>, CancellationToken, Task<ArchiveResult>> op)
{
    NativeProgressDialog? dialog;
    try
    {
        dialog = new NativeProgressDialog(title);
    }
    catch (COMException)
    {
        return await op(null!, CancellationToken.None).ConfigureAwait(false);
    }

    ArchiveResult result;
    using (dialog)
    {
        using var cts = new CancellationTokenSource();
        var dialogLock = new object();
        var speedSampler = new ProgressSpeedSampler();

        // Polls independently of progress reporting: Report() only fires when a
        // ProgressStream was constructed (totalBytes > 0), so gating Cancel on it left
        // Cancel inert for operations on zero-byte files. The lock keeps this timer's COM
        // calls from overlapping the progress callback's COM calls on the same object.
        using var cancelPoll = new Timer(_ =>
        {
            lock (dialogLock)
            {
                if (dialog.HasUserCancelled())
                    cts.Cancel();
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        var progress = new Progress<ProgressReport>(r =>
        {
            lock (dialogLock)
            {
                if (r.CurrentFile is not null)
                    dialog.SetLine(1, r.CurrentFile);
                dialog.SetLine(2, FormatStatus(r, speedSampler));
                dialog.SetProgress(r.BytesTransferred, r.TotalBytes);
            }
        });

        try
        {
            result = await op(progress, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ArchiveResult { Success = false };
        }
    }

    switch (ShellResultPresenter.Classify(result))
    {
        case ShellResultOutcome.Failed:
            ShowErrorSummary(title, result.Errors);
            break;
        case ShellResultOutcome.SkippedOnly:
            ShowSkippedSummary(title, result.SkippedFiles);
            break;
    }

    return result;
}

// The native progress dialog has no built-in way to report failures after it closes —
// unlike the deleted ProgressWindow, which showed a ContentDialog with an error summary.
const uint MB_ICONERROR = 0x10;
const uint MB_ICONWARNING = 0x30;
const int MaxErrorLinesShown = 10;

// -------------------------------------------------------------------------
// --hash: T-F128. Computes CRC-32/SHA-256 for the "Хеш-суми" context-menu submenu. Doesn't
// reuse RunWithProgressWindowAsync above -- that helper is typed around Task<ArchiveResult>,
// and FileHashService.HashResult doesn't fit that shape (it carries computed hash strings, not
// created files/archive errors) -- so this mirrors its dialog/cancel-poll/progress plumbing
// directly instead of forcing an ill-fitting abstraction onto a differently-shaped result.
// -------------------------------------------------------------------------
static async Task RunHashAsync(IReadOnlyList<string> paths, HashAlgorithmKind algorithm)
{
    string label = algorithm == HashAlgorithmKind.Crc32 ? "CRC-32" : "SHA-256";
    // T-F153: Path.TrimEndingDirectorySeparator (not just a bare TrimEnd('\\')) also handles a
    // forward-slash trailing separator — see RunArchiveAsync's identical fix for the full
    // rationale. Display-only here (the title bar), but kept consistent with the functional fix.
    string title = paths.Count == 1
        ? $"{label}: {Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]))}"
        : $"{label}: {paths.Count} files";

    NativeProgressDialog? dialog = TryCreateProgressDialog(title);

    HashResult result;
    try
    {
        result = dialog is null
            ? await FileHashService.ComputeAsync(paths, algorithm, null, CancellationToken.None).ConfigureAwait(false)
            : await ComputeHashWithDialogAsync(dialog, paths, algorithm).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        return;
    }

    ShowHashResults(title, result);
}

static async Task<HashResult> ComputeHashWithDialogAsync(NativeProgressDialog dialog, IReadOnlyList<string> paths, HashAlgorithmKind algorithm)
{
    using (dialog)
    {
        using var cts = new CancellationTokenSource();
        var dialogLock = new object();
        var speedSampler = new ProgressSpeedSampler();
        using var cancelPoll = CreateCancelPollTimer(dialog, dialogLock, cts);

        var progress = new Progress<ProgressReport>(r =>
        {
            lock (dialogLock)
            {
                if (r.CurrentFile is not null)
                    dialog.SetLine(1, r.CurrentFile);
                dialog.SetLine(2, FormatStatus(r, speedSampler));
                dialog.SetProgress(r.BytesTransferred, r.TotalBytes);
            }
        });

        return await FileHashService.ComputeAsync(paths, algorithm, progress, cts.Token).ConfigureAwait(false);
    }
}

// Shared by RunHashAsync/RunScanAsync -- both mirror RunWithProgressWindowAsync's dialog/cancel-
// poll plumbing directly (see the comment above RunHashAsync for why they don't reuse that
// ArchiveResult-typed helper), so at least the identical dialog-creation and cancel-poll-timer
// setup itself is not duplicated three times over.
static NativeProgressDialog? TryCreateProgressDialog(string title)
{
    try { return new NativeProgressDialog(title); }
    catch (COMException) { return null; }
}

static Timer CreateCancelPollTimer(NativeProgressDialog dialog, object dialogLock, CancellationTokenSource cts) =>
    new(_ =>
    {
        lock (dialogLock)
        {
            if (dialog.HasUserCancelled())
                cts.Cancel();
        }
    }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

static void ShowHashResults(string title, HashResult result)
{
    // T-F128 follow-up: a folder result shows only the aggregate Files/Size/DataSum/NamesSum,
    // matching NanaZip's own folder-hash summary - no per-file dump underneath (dropped per the
    // user's explicit ask; the per-file entries still exist on HashResult for the non-folder
    // branches below, which are a genuine per-file table, not a sum, so they keep listing each
    // file). Labels are localized (HashResultLocalizer); filenames/hex hashes are literal data,
    // not translated.
    string[] lines;
    if (result.Folder is { } folder)
    {
        lines =
        [
            HashResultLocalizer.Get("HashResultFilesLine", folder.FileCount),
            HashResultLocalizer.Get("HashResultSizeLine", $"{FormatBytes(folder.TotalBytes)} ({folder.TotalBytes:N0} bytes)"),
            HashResultLocalizer.Get("HashResultDataSumLine", folder.DataSum),
            HashResultLocalizer.Get("HashResultNamesSumLine", folder.NamesSum)
        ];
    }
    else
    {
        var entryLines = result.Entries.Take(MaxErrorLinesShown)
            .Select(e => e.Error is null
                ? $"{Path.GetFileName(e.SourcePath)}: {e.Hash}"
                : $"{Path.GetFileName(e.SourcePath)}: {e.Error}");
        lines = result.Entries.Count > MaxErrorLinesShown
            ? [.. entryLines, HashResultLocalizer.Get("HashResultAndMoreLine", result.Entries.Count - MaxErrorLinesShown)]
            : [.. entryLines];
    }

    var message = string.Join(Environment.NewLine, lines);
    bool anyErrors = result.Entries.Any(e => e.Error is not null);
    _ = MessageBoxW(IntPtr.Zero, message, title, anyErrors ? MB_ICONWARNING : MB_ICONINFORMATION);
}

// -------------------------------------------------------------------------
// --scan: T-F146. AMSI-based threat scan of an archive's quarantine-expanded contents. Doesn't
// reuse RunWithProgressWindowAsync (typed around Task<ArchiveResult>) or RunTestAsync's simpler
// single-message pattern -- ThreatScanResult is a genuine three-state result, not a success/
// failure one, so this mirrors RunHashAsync/ShowHashResults' dialog/cancel-poll/progress plumbing
// directly, same reasoning that comment already documents for Hash.
// -------------------------------------------------------------------------
static async Task RunScanAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    string title = archivePaths.Count == 1
        ? $"Scanning: {Path.GetFileName(archivePaths[0])}"
        : $"Scanning {archivePaths.Count} archives";

    var service = await BuildAntivirusScanServiceAsync(policy).ConfigureAwait(false);
    NativeProgressDialog? dialog = TryCreateProgressDialog(title);
    var options = new AntivirusScanOptions { ArchivePaths = archivePaths };

    ThreatScanResult result;
    try
    {
        result = dialog is null
            ? await service.ScanAsync(options, null, CancellationToken.None).ConfigureAwait(false)
            : await ScanWithDialogAsync(dialog, service, options).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        return;
    }

    ShowScanResults(title, result);
}

static async Task<ThreatScanResult> ScanWithDialogAsync(NativeProgressDialog dialog, AntivirusScanService service, AntivirusScanOptions options)
{
    using (dialog)
    {
        using var cts = new CancellationTokenSource();
        var dialogLock = new object();
        using var cancelPoll = CreateCancelPollTimer(dialog, dialogLock, cts);

        var progress = new Progress<ProgressReport>(r =>
        {
            lock (dialogLock)
            {
                if (r.CurrentFile is not null)
                    dialog.SetLine(1, r.CurrentFile);
                dialog.SetLine(2, $"{r.Percent}%");
                dialog.SetProgress(r.Percent, 100);
            }
        });

        return await service.ScanAsync(options, progress, cts.Token).ConfigureAwait(false);
    }
}

// T-F85-style: one AntivirusScanService per invocation, DetectCapabilitiesAsync called exactly
// once and shared across every archive in the selection, mirroring BuildExtractionRouterAsync.
static async Task<AntivirusScanService> BuildAntivirusScanServiceAsync(GroupPolicyOptions policy)
{
    var tarService = new TarSandboxedService(policy);
    var capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
    return new AntivirusScanService(capabilities, policy);
}

// Clean copy is deliberately "No threats found in this archive" -- never "safe" -- Pakko doesn't
// recurse into nested archives and can't make that broader claim (design guidance, docs/
// DECISIONS.md's T-F146 entry). Only problem entries (ThreatDetected/Inconclusive) are listed --
// mirrors ShowErrorSummary/ShowSkippedSummary's "only list what's wrong" convention, not a dump
// of every clean entry, which would be pure noise for a large archive.
static void ShowScanResults(string title, ThreatScanResult result)
{
    if (result.OverallVerdict == ThreatVerdict.Clean)
    {
        _ = MessageBoxW(IntPtr.Zero, ScanResultLocalizer.Get("ScanNoThreatsFound"), title, MB_ICONINFORMATION);
        return;
    }

    var problems = result.Findings.Where(f => f.Verdict != ThreatVerdict.Clean).ToList();
    var lines = problems.Take(MaxErrorLinesShown).Select(f =>
    {
        string label = f.EntryPath is { } entry
            ? $"{Path.GetFileName(f.ArchivePath)}/{entry}"
            : Path.GetFileName(f.ArchivePath);
        // ThreatName is realistically always null -- AMSI's own contract never returns one (see
        // AmsiScanner's doc comment) -- so the generic localized phrase is what actually ships; // NOSONAR: prose, not commented-out code (S125 false positive)
        // ThreatName is only used on the rare chance a future provider surfaces one.
        string detail = f.Verdict == ThreatVerdict.ThreatDetected
            ? f.ThreatName ?? ScanResultLocalizer.Get("ScanThreatDetectedGeneric")
            // Reason is always set for an Inconclusive finding by AntivirusScanService -- this
            // fallback is defensive only, should never actually be shown.
            : f.Reason ?? "unknown";
        return $"{label}: {detail}";
    }).ToList();

    if (problems.Count > MaxErrorLinesShown)
        lines.Add(ScanResultLocalizer.Get("ScanAndMoreLine", problems.Count - MaxErrorLinesShown));

    var message = string.Join(Environment.NewLine, lines);
    bool anyThreat = problems.Any(f => f.Verdict == ThreatVerdict.ThreatDetected);
    _ = MessageBoxW(IntPtr.Zero, message, title, anyThreat ? MB_ICONERROR : MB_ICONWARNING);
}

static void ShowErrorSummary(string title, IReadOnlyList<ArchiveError> errors)
{
    if (errors.Count == 0)
    {
        _ = MessageBoxW(IntPtr.Zero, ResultMessagesLocalizer.Get("ResultOperationFailed"), title, MB_ICONERROR);
        return;
    }

    var lines = errors.Take(MaxErrorLinesShown)
        .Select(e => $"{Path.GetFileName(e.SourcePath)}: {e.Message}");
    var message = string.Join(Environment.NewLine, lines);
    if (errors.Count > MaxErrorLinesShown)
        message += $"{Environment.NewLine}{ResultMessagesLocalizer.Get("ResultAndMoreLine", errors.Count - MaxErrorLinesShown)}";

    _ = MessageBoxW(IntPtr.Zero, message, title, MB_ICONERROR);
}

static void ShowSkippedSummary(string title, IReadOnlyList<SkippedFile> skipped) =>
    _ = MessageBoxW(IntPtr.Zero, ShellResultPresenter.BuildSkippedMessage(skipped), title, MB_ICONWARNING);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

// T-F142: speedSampler is per-operation (a fresh ProgressSpeedSampler constructed alongside each
// call site's own dialogLock/cts, matching Archiver.App's MainViewModel convention of a fresh
// instance per operation instead of a Reset()) — null for a caller that doesn't track speed
// (there is none left; both call sites below always pass one), matching the existing bytes-total
// gate (r.TotalBytes <= 0 shows no speed either, same as it shows no byte total).
static string FormatStatus(ProgressReport r, ProgressSpeedSampler? speedSampler)
{
    if (r.TotalBytes <= 0)
        return $"{r.Percent}%";

    string bytesPart = $"{FormatBytes(r.BytesTransferred)} / {FormatBytes(r.TotalBytes)}";
    double speedBytesPerSec = speedSampler?.Sample(r.BytesTransferred, DateTime.UtcNow) ?? 0;
    string speedPart = speedBytesPerSec >= 1 ? $"  ·  {FormatSpeed(speedBytesPerSec)}" : string.Empty;
    return $"{r.Percent}%  ·  {bytesPart}{speedPart}";
}

static string FormatBytes(long bytes) => bytes switch
{
    >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
    >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
    >= 1_024 => $"{bytes / 1_024.0:F0} KB",
    _ => $"{bytes} B"
};

static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
{
    >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F1} GB/s",
    >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
    >= 1_024 => $"{bytesPerSecond / 1_024:F0} KB/s",
    _ => $"{bytesPerSecond:F0} B/s"
};
