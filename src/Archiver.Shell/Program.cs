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
// Uses T-14 smart folder logic in ZipArchiveService (SeparateFolders mode):
// single root folder → strips prefix; multiple roots → creates wrapper folder.
// -------------------------------------------------------------------------
static async Task RunExtractHereAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var router = await BuildExtractionRouterAsync(policy).ConfigureAwait(false);

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
            OnConflict = ConflictBehavior.Rename,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => router.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --extract-flat (T-F115): dump every archive's contents directly into its own containing
// folder — but this is NOT "no wrapper folder ever", despite the name. ExtractMode.SingleFolder
// still runs T-14's smart-foldering (ZipArchiveService and, since T-F118, TarSandboxedService
// alike): a single-root-folder archive unwraps its own top-level folder, but a genuinely
// multi-root archive (no common containing folder) still gets an <archive_name>\ wrapper
// created under DestinationFolder — the same T-14 logic --extract-here (SeparateFolders mode,
// above) uses, just without --extract-here's own extra per-run numbered-folder isolation.
// Contrast with RunExtractFolderAsync below, which unconditionally pre-computes a fresh
// subfolder regardless of the archive's own root shape.
// -------------------------------------------------------------------------
static async Task RunExtractHereFlatAsync(IReadOnlyList<string> archivePaths, GroupPolicyOptions policy)
{
    var router = await BuildExtractionRouterAsync(policy).ConfigureAwait(false);

    foreach (var archivePath in archivePaths)
    {
        var destFolder = Path.GetDirectoryName(archivePath) ?? ".";
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename,
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
            OnConflict = ConflictBehavior.Rename,
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
        MessageBoxW(IntPtr.Zero, "No errors detected in the archive(s).", title, MB_ICONINFORMATION);
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
                dialog.SetLine(2, FormatStatus(r));
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
    string title = paths.Count == 1
        ? $"{label}: {Path.GetFileName(paths[0].TrimEnd('\\'))}"
        : $"{label}: {paths.Count} files";

    NativeProgressDialog? dialog;
    try { dialog = new NativeProgressDialog(title); }
    catch (COMException) { dialog = null; }

    HashResult result;
    try
    {
        if (dialog is null)
        {
            result = await FileHashService.ComputeAsync(paths, algorithm, null, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            using (dialog)
            {
                using var cts = new CancellationTokenSource();
                var dialogLock = new object();

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
                        dialog.SetLine(2, FormatStatus(r));
                        dialog.SetProgress(r.BytesTransferred, r.TotalBytes);
                    }
                });

                result = await FileHashService.ComputeAsync(paths, algorithm, progress, cts.Token).ConfigureAwait(false);
            }
        }
    }
    catch (OperationCanceledException)
    {
        return;
    }

    ShowHashResults(title, result);
}

static void ShowHashResults(string title, HashResult result)
{
    var lines = new List<string>();
    if (result.Folder is { } folder)
    {
        lines.Add($"Files: {folder.FileCount}");
        lines.Add($"DataSum: {folder.DataSum}");
        lines.Add($"NamesSum: {folder.NamesSum}");
        lines.Add(string.Empty);
    }

    lines.AddRange(result.Entries.Take(MaxErrorLinesShown)
        .Select(e => e.Error is null
            ? $"{Path.GetFileName(e.SourcePath)}: {e.Hash}"
            : $"{Path.GetFileName(e.SourcePath)}: {e.Error}"));
    if (result.Entries.Count > MaxErrorLinesShown)
        lines.Add($"…and {result.Entries.Count - MaxErrorLinesShown} more");

    var message = string.Join(Environment.NewLine, lines);
    bool anyErrors = result.Entries.Any(e => e.Error is not null);
    MessageBoxW(IntPtr.Zero, message, title, anyErrors ? MB_ICONWARNING : MB_ICONINFORMATION);
}

static void ShowErrorSummary(string title, IReadOnlyList<ArchiveError> errors)
{
    if (errors.Count == 0)
    {
        MessageBoxW(IntPtr.Zero, "The operation failed.", title, MB_ICONERROR);
        return;
    }

    var lines = errors.Take(MaxErrorLinesShown)
        .Select(e => $"{Path.GetFileName(e.SourcePath)}: {e.Message}");
    var message = string.Join(Environment.NewLine, lines);
    if (errors.Count > MaxErrorLinesShown)
        message += $"{Environment.NewLine}…and {errors.Count - MaxErrorLinesShown} more";

    MessageBoxW(IntPtr.Zero, message, title, MB_ICONERROR);
}

static void ShowSkippedSummary(string title, IReadOnlyList<SkippedFile> skipped) =>
    MessageBoxW(IntPtr.Zero, ShellResultPresenter.BuildSkippedMessage(skipped), title, MB_ICONWARNING);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

static string FormatStatus(ProgressReport r) =>
    r.TotalBytes <= 0 ? $"{r.Percent}%" : $"{r.Percent}%  ·  {FormatBytes(r.BytesTransferred)} / {FormatBytes(r.TotalBytes)}";

static string FormatBytes(long bytes) => bytes switch
{
    >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
    >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
    >= 1_024 => $"{bytes / 1_024.0:F0} KB",
    _ => $"{bytes} B"
};
