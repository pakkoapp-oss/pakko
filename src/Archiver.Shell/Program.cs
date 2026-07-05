using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;

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

    case CommandType.ExtractHere:
        await RunExtractHereAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.ExtractFolder:
        await RunExtractFolderAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.Archive:
        await RunArchiveAsync(command.Files).ConfigureAwait(false);
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
static async Task RunExtractHereAsync(IReadOnlyList<string> archivePaths)
{
    var service = new ZipArchiveService();

    foreach (var archivePath in archivePaths)
    {
        var destFolder = Path.GetDirectoryName(archivePath) ?? ".";
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Skip,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => service.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --extract-folder: always extract into an explicit <archive_name>\ subfolder,
// regardless of the archive's internal structure.
// -------------------------------------------------------------------------
static async Task RunExtractFolderAsync(IReadOnlyList<string> archivePaths)
{
    var service = new ZipArchiveService();

    foreach (var archivePath in archivePaths)
    {
        var archiveDir = Path.GetDirectoryName(archivePath) ?? ".";
        var folderName = Path.GetFileNameWithoutExtension(archivePath);
        var destFolder = Path.Combine(archiveDir, folderName);
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => service.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --archive: pack all source paths into a single ZIP placed next to the first
// item, named after the first item.
// -------------------------------------------------------------------------
static async Task RunArchiveAsync(IReadOnlyList<string> sourcePaths)
{
    var firstPath = sourcePaths[0];
    var destFolder = Path.GetDirectoryName(firstPath) ?? ".";
    var archiveName = Path.GetFileNameWithoutExtension(firstPath);

    var service = new ZipArchiveService();
    var options = new ArchiveOptions
    {
        SourcePaths = sourcePaths,
        DestinationFolder = destFolder,
        ArchiveName = archiveName,
        Mode = ArchiveMode.SingleArchive,
        OnConflict = ConflictBehavior.Skip,
    };

    string title = $"Archiving: {archiveName}";
    await RunWithProgressWindowAsync(title,
        (progress, ct) => service.ArchiveAsync(options, progress, ct))
        .ConfigureAwait(false);
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

    using (dialog)
    {
        using var cts = new CancellationTokenSource();

        var progress = new Progress<ProgressReport>(r =>
        {
            if (dialog.HasUserCancelled())
            {
                cts.Cancel();
                return;
            }

            if (r.CurrentFile is not null)
                dialog.SetLine(1, r.CurrentFile);
            dialog.SetLine(2, FormatStatus(r));
            dialog.SetProgress(r.BytesTransferred, r.TotalBytes);
        });

        try
        {
            return await op(progress, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ArchiveResult { Success = false };
        }
    }
}

static string FormatStatus(ProgressReport r) =>
    r.TotalBytes <= 0 ? $"{r.Percent}%" : $"{r.Percent}%  ·  {FormatBytes(r.BytesTransferred)} / {FormatBytes(r.TotalBytes)}";

static string FormatBytes(long bytes) => bytes switch
{
    >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
    >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
    >= 1_024 => $"{bytes / 1_024.0:F0} KB",
    _ => $"{bytes} B"
};
